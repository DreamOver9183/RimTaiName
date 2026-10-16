using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimTaiName.Core
{
    /// <summary>
    /// One background request started from the UI (main thread). The work runs on the thread pool and only
    /// hands its result back under a lock; <see cref="Reset"/> cancels it and drops any late result.
    /// The gate lease, if any, is released when the work finishes or is abandoned.
    /// </summary>
    internal sealed class UiJob<T>
        where T : class
    {
        private readonly object sync = new object();

        private int generation;

        private CancellationTokenSource cancellation;

        private T result;

        private bool running;

        private bool resultReported = true;

        public bool Running
        {
            get
            {
                lock (sync)
                    return running;
            }
        }

        /// <summary>The finished result, or null while running or after a reset.</summary>
        public T Result
        {
            get
            {
                lock (sync)
                    return running ? null : result;
            }
        }

        /// <summary>
        /// Main thread. <paramref name="onError"/> turns an unexpected exception into a result (if it throws as
        /// well, the job simply ends without one); cancellation by <see cref="Reset"/> produces no result at all.
        /// </summary>
        public void Start(Func<CancellationToken, Task<T>> work, IDisposable lease, Func<Exception, T> onError)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));
            if (onError == null)
                throw new ArgumentNullException(nameof(onError));
            Reset();
            int current;
            var source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            lock (sync)
            {
                current = generation;
                cancellation = source;
                running = true;
                resultReported = false;
            }
            Task.Run(async () =>
            {
                T value = null;
                try
                {
                    value = await work(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Abandoned by Reset; the generation check below drops it.
                }
                catch (Exception e)
                {
                    try
                    {
                        value = onError(e);
                    }
                    catch (Exception)
                    {
                        value = null;
                    }
                }
                finally
                {
                    lease?.Dispose();
                }
                lock (sync)
                {
                    // Checked and written under the same lock as Reset, so a reset can never be overwritten.
                    if (generation != current)
                        return;
                    result = value;
                    running = false;
                }
            });
        }

        /// <summary>Main thread: cancel a running job and forget its result (key changed, window closed).</summary>
        public void Reset()
        {
            CancellationTokenSource previous;
            lock (sync)
            {
                generation++;
                previous = cancellation;
                cancellation = null;
                running = false;
                result = null;
                resultReported = true;
            }
            if (previous == null)
                return;
            try
            {
                previous.Cancel();
            }
            catch (AggregateException)
            {
                // A cancellation callback failed; the job is abandoned either way.
            }
            finally
            {
                previous.Dispose();
            }
        }

        /// <summary>
        /// Main thread: true exactly once per finished result, for side effects that need the main thread
        /// (logging, saving the cache).
        /// </summary>
        public bool TakeNewResult()
        {
            lock (sync)
            {
                if (resultReported || running || result == null)
                    return false;
                resultReported = true;
                return true;
            }
        }
    }
}
