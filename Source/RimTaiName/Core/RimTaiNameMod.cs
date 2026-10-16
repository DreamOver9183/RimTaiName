using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using HarmonyLib;
using RimTaiName.Core;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using UnityEngine;
using Verse;

namespace RimTaiName
{
    public class RimTaiNameMod : Mod
    {
        public const string HarmonyId = "DreamOver9183.RimTaiName";

        private const string CacheFileName = "RimTaiName_Cache.xml";

        public RimTaiNameMod(ModContentPack content) : base(content)
        {
            // Mono may still default to TLS 1.0/1.1; Google requires 1.2. Kept out of the pure C# layers
            // because ServicePointManager is obsolete on the net10.0 test target.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            ApiKeyStore.Initialize(GenFilePaths.ConfigFolderPath);
            Settings = GetSettings<RimTaiNameSettings>();
            Settings.Sanitize();
            Cache = new PersonalNameCache(Path.Combine(GenFilePaths.ConfigFolderPath, CacheFileName));
            Cache.Load();
            if (Cache.LastError != null)
                ModLog.Warning("personal cache could not be read and was set aside: " + Cache.LastError);
            // The only place the plain key is handed to network code (see CLAUDE.md "API Key 規則").
            Gemini = new GeminiInteractionsClient(ApiKeyStore.Read);

            // Loaded on first lookup; a missing file only weakens translation (see LoadError).
            Dictionary = new BuiltInDictionary(Path.Combine(content.RootDir, "Dictionary", "BaseNames_zhTW.tsv"));
            Processor = new NameProcessorService(Dictionary, Cache);
            string rules = PromptBuilder.LoadRules(Path.Combine(content.RootDir, "Prompts", PromptBuilder.RulesFileName), out string rulesError);
            if (rules == null)
                ModLog.Warning("translation rules could not be read, using the minimum built-in rules: " + rulesError);
            Prompts = new PromptBuilder(rules);
            Translator = new GeminiNameTranslator(Gemini, Prompts, () => Settings.modelId);
            Gate = new RequestGate();

            var harmony = new Harmony(HarmonyId);
            // Patch each class on its own so one failing patch never takes the others (or vanilla) down.
            foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (!type.GetCustomAttributes(typeof(HarmonyPatch), false).Any())
                    continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    ModLog.Error($"patch {type.Name} failed: {e}");
                }
            }
            ModLog.Message($"loaded: Harmony {typeof(Harmony).Assembly.GetName().Version}, " +
                           $"{harmony.GetPatchedMethods().Count()} methods patched, key storage {ApiKeyStore.Mode}, " +
                           $"{Cache.Count} cached names");
        }

        public static RimTaiNameSettings Settings { get; private set; }

        public static PersonalNameCache Cache { get; private set; }

        public static GeminiInteractionsClient Gemini { get; private set; }

        public static BuiltInDictionary Dictionary { get; private set; }

        public static NameProcessorService Processor { get; private set; }

        public static PromptBuilder Prompts { get; private set; }

        /// <summary>Translation requests for the pipeline (ILlmClient).</summary>
        public static GeminiNameTranslator Translator { get; private set; }

        /// <summary>One Gemini request at a time, spaced by the configured minimum interval.</summary>
        public static RequestGate Gate { get; private set; }

        /// <summary>
        /// Main thread: claims the request gate with the current minimum interval. Returns a lease to dispose when
        /// the request ends, or null with a player-facing <paramref name="refusal"/>.
        /// </summary>
        public static IDisposable TryEnterGate(out string refusal)
        {
            Gate.MinInterval = TimeSpan.FromSeconds(Settings.minIntervalSeconds);
            IDisposable lease = Gate.TryEnter(out GateDenial denial, out TimeSpan wait);
            refusal = lease == null ? GeminiMessages.DescribeDenial(denial, wait) : null;
            return lease;
        }

        /// <summary>The name from About.xml, so the settings list matches the mod list and the Workshop page.</summary>
        public override string SettingsCategory() => Content?.Name ?? "RimTaiName";

        public override void DoSettingsWindowContents(Rect inRect) => SettingsPage.Draw(inRect);

        /// <summary>Called when the settings window closes.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            SettingsPage.OnClosed();
            if (!Cache.SaveIfDirty())
                ModLog.Warning("personal cache could not be saved: " + Cache.LastError);
        }
    }
}
