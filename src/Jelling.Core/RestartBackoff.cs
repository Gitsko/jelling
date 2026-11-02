namespace Jelling.Core;

/// <summary>
/// How long to wait before restarting something that stopped: each failed start waits twice as long, up to
/// a maximum, and so does a restart that comes soon after the last one succeeded. Only after a stable run
/// does it start over at the shortest delay.
/// </summary>
public sealed class RestartBackoff
{
    private readonly TimeSpan _first;
    private readonly TimeSpan _max;
    private readonly TimeSpan _stableRun;

    // The delay the last successful start waited, and when it happened (Environment.TickCount64)
    private TimeSpan _lastDelay = TimeSpan.Zero;
    private long _startedAt;

    public RestartBackoff(TimeSpan first, TimeSpan max, TimeSpan stableRun)
    {
        _first = first;
        _max = max;
        _stableRun = stableRun;
    }

    /// <summary>The wait before the first try of a restart.</summary>
    public TimeSpan FirstDelay(long now) =>
        _lastDelay > TimeSpan.Zero && now - _startedAt < _stableRun.TotalMilliseconds ? Longer(_lastDelay) : _first;

    /// <summary>The wait after a try that failed.</summary>
    public TimeSpan Longer(TimeSpan delay) => delay * 2 < _max ? delay * 2 : _max;

    public void Started(TimeSpan delay, long now)
    {
        _lastDelay = delay;
        _startedAt = now;
    }
}
