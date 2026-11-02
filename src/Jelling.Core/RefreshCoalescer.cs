using System.Runtime.ExceptionServices;

namespace Jelling.Core;

/// <summary>
/// Runs a refresh at most once at a time. Requests that arrive during a run collapse into one
/// trailing run, so a device change reported mid-query is never lost. Not thread-safe: request
/// from one thread (the UI thread), so the refresh also continues there.
/// </summary>
public sealed class RefreshCoalescer(Func<Task> refresh)
{
    private Task? _running;
    private bool _requestedAgain;

    public Task Request()
    {
        // A refresh that completed synchronously leaves a finished task behind, not a running one
        if (_running is { IsCompleted: false })
        {
            _requestedAgain = true;
            return _running;
        }

        _running = RunAsync();
        return _running;
    }

    private async Task RunAsync()
    {
        ExceptionDispatchInfo? failure;
        do
        {
            _requestedAgain = false;
            failure = null;
            try
            {
                await refresh();
            }
            catch (Exception exception)
            {
                // A request that came during the failed run still gets its own run
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }
        while (_requestedAgain);

        // Only the last run's outcome matters to the callers; an earlier failure was superseded
        failure?.Throw();
    }
}
