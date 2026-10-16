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
        public const string HarmonyId = "RimTaiName.Dev";

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

        public override string SettingsCategory() => "RimTaiName";

        public override void DoSettingsWindowContents(Rect inRect) => SettingsPage.Draw(inRect, Settings, Cache, Gemini);

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
