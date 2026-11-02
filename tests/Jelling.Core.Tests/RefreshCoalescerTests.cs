namespace Jelling.Core.Tests;

public class RefreshCoalescerTests
{
    // Each refresh waits for the test to release it, so requests can arrive mid-run. SetResult runs the
    // coalescer's continuation inline on the test thread, so the trailing run has started when it returns
    private sealed class ControlledRefresh
    {
        private readonly Queue<TaskCompletionSource> _pending = new();

        public int Runs { get; private set; }

        public Task Run()
        {
            Runs++;
            var completion = new TaskCompletionSource();
            _pending.Enqueue(completion);
            return completion.Task;
        }

        public void CompleteNext() => _pending.Dequeue().SetResult();

        public void FailNext() => _pending.Dequeue().SetException(new InvalidOperationException());
    }

    [Fact]
    public void RunsRefreshOnRequest()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        _ = coalescer.Request();

        Assert.Equal(1, refresh.Runs);
    }

    [Fact]
    public void RequestDuringRunTriggersOneTrailingRun()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        _ = coalescer.Request();
        _ = coalescer.Request();
        Assert.Equal(1, refresh.Runs);

        refresh.CompleteNext();

        Assert.Equal(2, refresh.Runs);
    }

    [Fact]
    public void ManyRequestsDuringRunCollapseIntoOneTrailingRun()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        var first = coalescer.Request();
        _ = coalescer.Request();
        _ = coalescer.Request();
        _ = coalescer.Request();
        refresh.CompleteNext();
        refresh.CompleteNext();

        Assert.Equal(2, refresh.Runs);
        Assert.True(first.IsCompletedSuccessfully);
    }

    [Fact]
    public void RunsAgainAfterSynchronouslyCompletedRefresh()
    {
        var runs = 0;
        var coalescer = new RefreshCoalescer(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        _ = coalescer.Request();
        _ = coalescer.Request();

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task FailedRefreshDoesNotBlockLaterRequests()
    {
        var runs = 0;
        var coalescer = new RefreshCoalescer(() =>
        {
            runs++;
            return runs == 1 ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(coalescer.Request);
        await coalescer.Request();

        Assert.Equal(2, runs);
    }

    [Fact]
    public void RequestDuringFailedRunStillGetsItsTrailingRun()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        var first = coalescer.Request();
        _ = coalescer.Request();
        refresh.FailNext();

        Assert.Equal(2, refresh.Runs);

        refresh.CompleteNext();

        Assert.True(first.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task FailedRunWithoutLaterRequestPassesItsException()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        var first = coalescer.Request();
        refresh.FailNext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
    }

    [Fact]
    public async Task FailedTrailingRunFailsTheEarlierRequestToo()
    {
        var refresh = new ControlledRefresh();
        var coalescer = new RefreshCoalescer(refresh.Run);

        var first = coalescer.Request();
        _ = coalescer.Request();
        refresh.CompleteNext();
        refresh.FailNext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
    }
}
