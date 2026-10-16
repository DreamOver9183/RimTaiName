using System;
using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class QuotaResetClockTests
    {
        private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        [Theory]
        // Summer (PDT, UTC-7): 2026-10-16 03:00 UTC is 2026-10-15 20:00 PDT -> next midnight 10-16 07:00 UTC.
        [InlineData(2026, 10, 16, 3, 2026, 10, 16, 7)]
        // Taiwan afternoon after the reset: 2026-10-16 08:00 UTC (01:00 PDT) -> 10-17 07:00 UTC.
        [InlineData(2026, 10, 16, 8, 2026, 10, 17, 7)]
        // Winter (PST, UTC-8).
        [InlineData(2026, 1, 15, 12, 2026, 1, 16, 8)]
        // DST starts Sunday 2026-03-08: midnight that day is still PST, the next is PDT.
        [InlineData(2026, 3, 7, 20, 2026, 3, 8, 8)]
        [InlineData(2026, 3, 8, 12, 2026, 3, 9, 7)]
        // DST ends Sunday 2026-11-01: midnight that day is still PDT, the next is PST.
        [InlineData(2026, 10, 31, 20, 2026, 11, 1, 7)]
        [InlineData(2026, 11, 1, 12, 2026, 11, 2, 8)]
        public void NextResetIsPacificMidnight(int y, int mo, int d, int h, int ry, int rmo, int rd, int rh)
        {
            DateTime reset = QuotaResetClock.NextResetUtc(Utc(y, mo, d, h));
            Assert.Equal(Utc(ry, rmo, rd, rh), reset);
            Assert.Equal(DateTimeKind.Utc, reset.Kind);
        }

        [Fact]
        public void ExactlyAtMidnightReturnsTheFollowingMidnight()
        {
            Assert.Equal(Utc(2026, 10, 17, 7), QuotaResetClock.NextResetUtc(Utc(2026, 10, 16, 7)));
        }

        [Theory]
        [InlineData(2026, 3, 8, 9, 59, -8)]
        [InlineData(2026, 3, 8, 10, 0, -7)]
        [InlineData(2026, 11, 1, 8, 59, -7)]
        [InlineData(2026, 11, 1, 9, 0, -8)]
        [InlineData(2027, 3, 14, 10, 0, -7)]
        public void OffsetSwitchesAtTwoAmLocal(int y, int mo, int d, int h, int mi, int expectedHours)
        {
            Assert.Equal(TimeSpan.FromHours(expectedHours), QuotaResetClock.OffsetAt(Utc(y, mo, d, h, mi)));
        }
    }
}
