using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimTaiName.Spike;
using UnityEngine;
using Verse;

namespace RimTaiName
{
    public class RimTaiNameMod : Mod
    {
        public const string HarmonyId = "RimTaiName.Dev";

        public RimTaiNameMod(ModContentPack content) : base(content)
        {
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
                    Log.Error($"[RimTaiName] patch {type.Name} failed: {e}");
                }
            }
            Log.Message($"[RimTaiName] loaded: Harmony {typeof(Harmony).Assembly.GetName().Version}, " +
                        $"{harmony.GetPatchedMethods().Count()} methods patched");
            ConnectivityProbe.Start();
        }

        public override string SettingsCategory() => "RimTaiName";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("RimTaiName_Spike_HttpsStatus".Translate(ConnectivityProbe.Status));
            if (list.ButtonText("RimTaiName_Spike_HttpsTest".Translate()))
                ConnectivityProbe.Start();
            list.End();
        }
    }
}
