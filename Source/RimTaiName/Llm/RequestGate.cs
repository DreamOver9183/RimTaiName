using System;
using System.Threading;

namespace RimTaiName.Llm
{
    public enum GateDenial
    {
        None,

        /// <summary>Another Gemini request is still running.</summary>
        Busy,

        /// <summary>The previous request started less than <see cref="RequestGate.MinInterval"/> ago.</summary>
        TooSoon,
    }

    /// <summary>
    /// Global limiter for Gemini requests (v3 §7.1): one request at a time and at most one start per
    /// <see cref="MinInterval"/>. Shared by translation and the settings page's connection test. Callers enter on
    /// the main thread and dispose the lease when the request finishes, on any thread.
    /// </summary>
    public sealed class RequestGate
    {
        private readonly object sync = new object();

        private readonly Func<DateTime> utcNow;

        private bool busy;

        private DateTime lastStartUtc = DateTime.MinValue;

        private TimeSpan minInterval = TimeSpan.FromSeconds(3);

        /// <param name="utcNow">Clock for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
        public RequestGate(Func<DateTime> utcNow = null)
        {
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Clamped to 0–1 hour.</summary>
        public TimeSpan MinInterval
        {
            get
            {
                lock (sync)
                    return minInterval;
            }
            set
            {
                lock (sync)
                    minInterval = value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxInterval ? MaxInterval : value;
            }
        }

        private static readonly TimeSpan MaxInterval = TimeSpan.FromHours(1);

        public bool IsBusy
        {
            get
            {
                lock (sync)
                    return busy;
            }
        }

        /// <summary>
        /// Claims the gate. On success the returned lease must be disposed when the request is over; on failure
        /// <paramref name="denial"/> says why and <paramref name="wait"/> how long until a start is allowed.
        /// </summary>
        public IDisposable TryEnter(out GateDenial denial, out TimeSpan wait)
        {
            lock (sync)
            {
                if (busy)
                {
                    denial = GateDenial.Busy;
                    wait = TimeSpan.Zero;
                    return null;
                }
                DateTime now = utcNow();
                // Elapsed time rather than lastStart + interval: no DateTime overflow, and a clock that jumped
                // backwards (elapsed < 0) does not lock the gate until it catches up.
                TimeSpan elapsed = now - lastStartUtc;
                TimeSpan remaining = lastStartUtc == DateTime.MinValue || elapsed < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : minInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    denial = GateDenial.TooSoon;
                    wait = remaining;
                    return null;
                }
                busy = true;
                lastStartUtc = now;
                denial = GateDenial.None;
                wait = TimeSpan.Zero;
                return new Lease(this);
            }
        }

        private void Exit()
        {
            lock (sync)
                busy = false;
        }

        private sealed class Lease : IDisposable
        {
            private RequestGate gate;

            public Lease(RequestGate gate)
            {
                this.gate = gate;
            }

            public void Dispose() => Interlocked.Exchange(ref gate, null)?.Exit();
        }
    }
}
