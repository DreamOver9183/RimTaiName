using System;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Core;
using Xunit;

namespace RimTaiName.Tests.Core
{
    public class UiJobTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private sealed class Box
        {
            public Box(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class Lease : IDisposable
        {
            private int disposed;

            public int Disposed => Volatile.Read(ref disposed);

            public void Dispose() => Interlocked.Increment(ref disposed);
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 500 && !condition(); i++)
                await Task.Delay(10, Ct);
            Assert.True(condition(), "condition not reached");
        }

        [Fact]
        public async Task DeliversTheResultAndReleasesTheLease()
        {
            var job = new UiJob<Box>();
            var lease = new Lease();
            job.Start(_ => Task.FromResult(new Box("done")), lease, e => null);
            await WaitFor(() => job.Result != null);
            Assert.False(job.Running);
            Assert.Equal("done", job.Result.Value);
            Assert.Equal(1, lease.Disposed);
        }

        [Fact]
        public async Task ResultIsHiddenWhileRunning()
        {
            var job = new UiJob<Box>();
            var release = new TaskCompletionSource<bool>();
            job.Start(async _ =>
            {
                await release.Task;
                return new Box("late");
            }, null, e => null);
            Assert.True(job.Running);
            Assert.Null(job.Result);
            release.SetResult(true);
            await WaitFor(() => job.Result != null);
        }

        [Fact]
        public async Task ResetCancelsAndDropsTheLateResult()
        {
            var job = new UiJob<Box>();
            var lease = new Lease();
            var started = new TaskCompletionSource<bool>();
            job.Start(async token =>
            {
                started.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return new Box("never");
            }, lease, e => new Box("error"));
            await started.Task;
            job.Reset();
            await WaitFor(() => lease.Disposed == 1);
            Assert.False(job.Running);
            Assert.Null(job.Result);
        }

        [Fact]
        public async Task ResultOfAnOlderStartIsIgnored()
        {
            var job = new UiJob<Box>();
            var slow = new TaskCompletionSource<bool>();
            job.Start(async _ =>
            {
                await slow.Task;
                return new Box("old");
            }, null, e => null);
            job.Start(_ => Task.FromResult(new Box("new")), null, e => null);
            await WaitFor(() => job.Result != null);
            slow.SetResult(true);
            await Task.Delay(50, Ct);
            Assert.Equal("new", job.Result.Value);
        }

        [Fact]
        public async Task UnexpectedExceptionBecomesAResult()
        {
            var job = new UiJob<Box>();
            var lease = new Lease();
            job.Start(_ => throw new InvalidOperationException("boom"), lease, e => new Box(e.GetType().Name));
            await WaitFor(() => job.Result != null);
            Assert.Equal("InvalidOperationException", job.Result.Value);
            Assert.Equal(1, lease.Disposed);
        }

        [Fact]
        public async Task FailingErrorHandlerStillEndsTheJob()
        {
            var job = new UiJob<Box>();
            var lease = new Lease();
            job.Start(_ => throw new InvalidOperationException("boom"), lease, e => throw new NotSupportedException("handler"));
            await WaitFor(() => !job.Running);
            Assert.Null(job.Result);
            Assert.Equal(1, lease.Disposed);
            Assert.False(job.TakeNewResult());
        }

        [Fact]
        public void NullDelegatesAreRejected()
        {
            var job = new UiJob<Box>();
            Assert.Throws<ArgumentNullException>(() => job.Start(null, null, e => null));
            Assert.Throws<ArgumentNullException>(() => job.Start(_ => Task.FromResult(new Box("x")), null, null));
            Assert.False(job.Running);
        }

        [Fact]
        public async Task ResetRacingWithCompletionNeverLeavesAStaleResult()
        {
            var job = new UiJob<Box>();
            for (int i = 0; i < 200; i++)
            {
                job.Start(_ => Task.FromResult(new Box("x")), null, e => null);
                job.Reset();
                Assert.False(job.Running);
                await Task.Yield();
                Assert.Null(job.Result);
            }
            await Task.Delay(50, Ct);
            Assert.Null(job.Result);
        }

        [Fact]
        public async Task NewResultIsReportedOnce()
        {
            var job = new UiJob<Box>();
            Assert.False(job.TakeNewResult());
            job.Start(_ => Task.FromResult(new Box("x")), null, e => null);
            await WaitFor(() => job.Result != null);
            Assert.True(job.TakeNewResult());
            Assert.False(job.TakeNewResult());
            job.Reset();
            Assert.False(job.TakeNewResult());
        }
    }
}
