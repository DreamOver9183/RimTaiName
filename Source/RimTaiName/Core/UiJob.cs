using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimTaiName.Core
{
    /// <summary>
    /// One background request started from the UI (main thread). The work runs on the thread pool and only
    /// hands its result back through a volatile field; <see cref="Reset"/> cancels it and drops any late result.
    /// The gate lease, if any, is released when the work finishes or is abandoned.
    /// </summary>
    internal sealed class UiJob<T>
        where T : class
    {
        private int generation;

        private CancellationTokenSource cancellation;

        private volatile T result;

        private volatile bool running;

        private bool resultReported = true;

        public bool Running => running;

        /// <summary>The finished result, or null while running or after a reset.</summary>
        public T Result => running ? null : result;

        /// <summary>
        /// Main thread. <paramref name="onError"/> turns an unexpected exception into a result; cancellation by
        /// <see cref="Reset"/> produces no result at all.
        /// </summary>
        public void Start(Func<CancellationToken, Task<T>> work, IDisposable lease, Func<Exception, T> onError)
        {
            Reset();
            int current = generation;
            var source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            cancellation = source;
            running = true;
            resultReported = false;
            Task.Run(async () =>
            {
                T value;
                try
                {
                    value = await work(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    value = onError(e);
                }
                finally
                {
                    lease?.Dispose();
                }
                if (Volatile.Read(ref generation) != current)
                    return;
                result = value;
                running = false;
            });
        }

        /// <summary>Main thread: cancel a running job and forget its result (key changed, window closed).</summary>
        public void Reset()
        {
            Interlocked.Increment(ref generation);
            CancellationTokenSource previous = cancellation;
            cancellation = null;
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }
            running = false;
            result = null;
            resultReported = true;
        }

        /// <summary>
        /// Main thread: true exactly once per finished result, for side effects that need the main thread
        /// (logging, saving the cache).
        /// </summary>
        public bool TakeNewResult()
        {
            if (resultReported || Result == null)
                return false;
            resultReported = true;
            return true;
        }
    }
}
