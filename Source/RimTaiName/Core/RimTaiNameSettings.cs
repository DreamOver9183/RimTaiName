using RimTaiName.Llm;
using UnityEngine;
using Verse;

namespace RimTaiName.Core
{
    /// <summary>Mod settings saved in RimWorld's settings XML. The API key is deliberately NOT here (see ApiKeyStore).</summary>
    public class RimTaiNameSettings : ModSettings
    {
        public const float MinInterval = 1f;

        public const float MaxInterval = 30f;

        public const float DefaultInterval = 3f;

        /// <summary>Gemini model id; always valid per <see cref="ModelId.TryNormalize"/>.</summary>
        public string modelId = ModelId.Default;

        /// <summary>Shortest time between two Gemini requests, in seconds.</summary>
        public float minIntervalSeconds = DefaultInterval;

        /// <summary>Fill a blank nickname with the translated first/last name it stands for (6.3).</summary>
        public bool keepVanillaAddress = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref modelId, "modelId", ModelId.Default);
            Scribe_Values.Look(ref minIntervalSeconds, "minIntervalSeconds", DefaultInterval);
            Scribe_Values.Look(ref keepVanillaAddress, "keepVanillaAddress", true);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Sanitize();
        }

        /// <summary>Repairs values edited by hand in the XML.</summary>
        public void Sanitize()
        {
            modelId = ModelId.TryNormalize(modelId, out string normalized) ? normalized : ModelId.Default;
            minIntervalSeconds = float.IsNaN(minIntervalSeconds)
                ? DefaultInterval
                : Mathf.Clamp(minIntervalSeconds, MinInterval, MaxInterval);
        }
    }
}
