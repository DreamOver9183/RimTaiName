using System;
using RimTaiName.Llm;
using Verse;

namespace RimTaiName.Core
{
    /// <summary>Player-facing text for Gemini results (main thread only: uses Translate).</summary>
    internal static class GeminiMessages
    {
        public static string Describe(GeminiResult result)
        {
            if (result == null)
                return string.Empty;
            switch (result.Error)
            {
                case GeminiErrorKind.None:
                    return "RimTaiName_Gemini_Ok".Translate();
                case GeminiErrorKind.BadRequest:
                    return "RimTaiName_Gemini_BadRequest".Translate(result.Detail ?? "-");
                case GeminiErrorKind.UnexpectedStatus:
                    return "RimTaiName_Gemini_UnexpectedStatus".Translate(result.StatusCode);
                case GeminiErrorKind.QuotaExceeded:
                    return "RimTaiName_Gemini_QuotaExceeded".Translate(QuotaResetLocalTime());
                default:
                    return ("RimTaiName_Gemini_" + result.Error).Translate();
            }
        }

        /// <summary>When the daily quota resets, in the player's local time (e.g. "15:00").</summary>
        public static string QuotaResetLocalTime() =>
            QuotaResetClock.NextResetUtc(DateTime.UtcNow).ToLocalTime().ToString("HH:mm");
    }
}
