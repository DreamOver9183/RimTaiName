using System;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Gemini daily quotas reset at midnight Pacific time. Computed with the US daylight-saving rules instead of
    /// time zone ids, which Mono resolves unreliably on Windows.
    /// </summary>
    public static class QuotaResetClock
    {
        private static readonly TimeSpan Standard = TimeSpan.FromHours(-8);

        private static readonly TimeSpan Daylight = TimeSpan.FromHours(-7);

        /// <summary>The next midnight in America/Los_Angeles, as a UTC <see cref="DateTime"/>.</summary>
        public static DateTime NextResetUtc(DateTime utcNow)
        {
            if (utcNow.Kind == DateTimeKind.Local)
                utcNow = utcNow.ToUniversalTime();
            DateTime local = utcNow + OffsetAt(utcNow);
            DateTime nextMidnight = local.Date.AddDays(1);
            // Transitions happen at 02:00 local, so midnight belongs to the state of the previous evening.
            TimeSpan offset = IsDaylightAtMidnight(nextMidnight) ? Daylight : Standard;
            return DateTime.SpecifyKind(nextMidnight - offset, DateTimeKind.Utc);
        }

        /// <summary>UTC offset of Pacific time at the given UTC instant.</summary>
        public static TimeSpan OffsetAt(DateTime utc)
        {
            int year = utc.Year;
            // DST starts at 02:00 PST (10:00 UTC) on the second Sunday of March
            // and ends at 02:00 PDT (09:00 UTC) on the first Sunday of November.
            DateTime start = NthSunday(year, 3, 2).AddHours(10);
            DateTime end = NthSunday(year, 11, 1).AddHours(9);
            return utc >= start && utc < end ? Daylight : Standard;
        }

        private static bool IsDaylightAtMidnight(DateTime localDate)
        {
            DateTime start = NthSunday(localDate.Year, 3, 2);
            DateTime end = NthSunday(localDate.Year, 11, 1);
            return localDate > start && localDate <= end;
        }

        private static DateTime NthSunday(int year, int month, int n)
        {
            var first = new DateTime(year, month, 1);
            int toSunday = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(toSunday + 7 * (n - 1));
        }
    }
}
