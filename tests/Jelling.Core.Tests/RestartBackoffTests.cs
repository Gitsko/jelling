namespace Jelling.Core.Tests;

public class RestartBackoffTests
{
    private static RestartBackoff Create() =>
        new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), stableRun: TimeSpan.FromMinutes(10));

    [Fact]
    public void FirstRestartWaitsTheShortestDelay()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), Create().FirstDelay(now: 0));
    }

    [Fact]
    public void FailedStartWaitsTwiceAsLong()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Create().Longer(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DelayStopsAtTheMaximum()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Create().Longer(TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void AbortSoonAfterARestartKeepsBackingOff()
    {
        var backoff = Create();
        backoff.Started(TimeSpan.FromSeconds(20), now: 1_000);

        Assert.Equal(TimeSpan.FromSeconds(40), backoff.FirstDelay(now: 61_000));
    }

    [Fact]
    public void AbortAfterAStableRunStartsOverAtTheShortestDelay()
    {
        var backoff = Create();
        backoff.Started(TimeSpan.FromSeconds(20), now: 1_000);

        Assert.Equal(TimeSpan.FromSeconds(5), backoff.FirstDelay(now: 1_000 + (long)TimeSpan.FromMinutes(10).TotalMilliseconds));
    }
}
