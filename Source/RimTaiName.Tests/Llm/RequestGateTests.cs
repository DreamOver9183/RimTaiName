using System;
using System.Linq;
using System.Threading.Tasks;
using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class RequestGateTests
    {
        private DateTime now = new DateTime(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc);

        private RequestGate Gate(double intervalSeconds = 3) =>
            new RequestGate(() => now) { MinInterval = TimeSpan.FromSeconds(intervalSeconds) };

        [Fact]
        public void FirstRequestIsAllowed()
        {
            RequestGate gate = Gate();
            IDisposable lease = gate.TryEnter(out GateDenial denial, out TimeSpan wait);
            Assert.NotNull(lease);
            Assert.Equal(GateDenial.None, denial);
            Assert.Equal(TimeSpan.Zero, wait);
            Assert.True(gate.IsBusy);
        }

        [Fact]
        public void OnlyOneRequestAtATime()
        {
            RequestGate gate = Gate(0);
            using (gate.TryEnter(out _, out _))
            {
                Assert.Null(gate.TryEnter(out GateDenial denial, out _));
                Assert.Equal(GateDenial.Busy, denial);
            }
            Assert.False(gate.IsBusy);
            Assert.NotNull(gate.TryEnter(out _, out _));
        }

        [Fact]
        public void MinimumIntervalCountsFromThePreviousStart()
        {
            RequestGate gate = Gate(3);
            gate.TryEnter(out _, out _).Dispose();
            now = now.AddSeconds(1);
            Assert.Null(gate.TryEnter(out GateDenial denial, out TimeSpan wait));
            Assert.Equal(GateDenial.TooSoon, denial);
            Assert.Equal(TimeSpan.FromSeconds(2), wait);
            now = now.AddSeconds(2);
            Assert.NotNull(gate.TryEnter(out denial, out _));
            Assert.Equal(GateDenial.None, denial);
        }

        [Fact]
        public void LongRequestDoesNotAddExtraWait()
        {
            RequestGate gate = Gate(3);
            IDisposable lease = gate.TryEnter(out _, out _);
            now = now.AddSeconds(10);
            lease.Dispose();
            Assert.NotNull(gate.TryEnter(out _, out _));
        }

        [Fact]
        public void DisposingALeaseTwiceIsHarmless()
        {
            RequestGate gate = Gate(0);
            IDisposable first = gate.TryEnter(out _, out _);
            first.Dispose();
            IDisposable second = gate.TryEnter(out _, out _);
            first.Dispose(); // must not release the second request
            Assert.True(gate.IsBusy);
            second.Dispose();
            Assert.False(gate.IsBusy);
        }

        [Fact]
        public void IntervalIsClampedToZeroThroughOneHour()
        {
            RequestGate gate = Gate(0);
            gate.MinInterval = TimeSpan.FromSeconds(-5);
            Assert.Equal(TimeSpan.Zero, gate.MinInterval);
            gate.MinInterval = TimeSpan.MaxValue;
            Assert.Equal(TimeSpan.FromHours(1), gate.MinInterval);
            gate.TryEnter(out _, out _).Dispose();
            Assert.Null(gate.TryEnter(out GateDenial denial, out TimeSpan wait)); // no overflow
            Assert.Equal(GateDenial.TooSoon, denial);
            Assert.Equal(TimeSpan.FromHours(1), wait);
        }

        [Fact]
        public void ClockJumpingBackwardsDoesNotLockTheGate()
        {
            RequestGate gate = Gate(3);
            gate.TryEnter(out _, out _).Dispose();
            now = now.AddHours(-2);
            Assert.NotNull(gate.TryEnter(out GateDenial denial, out _));
            Assert.Equal(GateDenial.None, denial);
        }

        [Fact]
        public void ConcurrentCallersGetExactlyOneLease()
        {
            RequestGate gate = Gate(0);
            IDisposable[] leases = new IDisposable[64];
            Parallel.For(0, leases.Length, i => leases[i] = gate.TryEnter(out _, out _));
            Assert.Equal(1, leases.Count(l => l != null));
        }
    }
}
