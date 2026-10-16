using System;
using RimTaiName.Llm;
using Verse;

namespace RimTaiName.Core
{
    /// <summary>Player-facing text for Gemini results (main thread only: uses Translate).</summary>
    internal static class GeminiMessages
    {
        public static string Describe(GeminiResult result) =>
            result == null ? string.Empty : Describe(result.Error, result.StatusCode, result.Detail);

        public static string Describe(GeminiErrorKind error, int statusCode, string detail)
        {
            switch (error)
            {
                case GeminiErrorKind.None:
                    return "RimTaiName_Gemini_Ok".Translate();
                case GeminiErrorKind.BadRequest:
                    return "RimTaiName_Gemini_BadRequest".Translate(detail ?? "-");
                case GeminiErrorKind.UnexpectedStatus:
                    return "RimTaiName_Gemini_UnexpectedStatus".Translate(statusCode);
                case GeminiErrorKind.QuotaExceeded:
                    return "RimTaiName_Gemini_QuotaExceeded".Translate(QuotaResetLocalTime());
                default:
                    return ("RimTaiName_Gemini_" + error).Translate();
            }
        }

        /// <summary>Why the request gate refused a new request.</summary>
        public static string DescribeDenial(GateDenial denial, TimeSpan wait)
        {
            switch (denial)
            {
                case GateDenial.Busy:
                    return "RimTaiName_Gate_Busy".Translate();
                case GateDenial.TooSoon:
                    return "RimTaiName_Gate_TooSoon".Translate(Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
                default:
                    return string.Empty;
            }
        }

        /// <summary>When the daily quota resets, in the player's local time (e.g. "15:00").</summary>
        public static string QuotaResetLocalTime() =>
            QuotaResetClock.NextResetUtc(DateTime.UtcNow).ToLocalTime().ToString("HH:mm");
    }
}
