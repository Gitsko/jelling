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
        do
        {
            _requestedAgain = false;
            await refresh();
        }
        while (_requestedAgain);
    }
}
