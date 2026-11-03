using Microsoft.UI.Dispatching;

namespace Jelling.App;

/// <summary>
/// Keeps <see cref="RightEdge"/> current without scanning Explorer every second: it measures when told that the
/// taskbar may have changed, again until two results agree while new buttons slide in, and now and then in case
/// nothing told it (a pinned app, a changed alignment).
/// </summary>
/// <remarks>Lives on the UI thread; the measurement itself runs on the thread pool.</remarks>
internal sealed class TaskButtonsMonitor : IDisposable
{
    private const int MaxFailedMeasurements = 3;

    // Long enough for Explorer to have added the button, short enough that the element covers it only briefly
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SafetyInterval = TimeSpan.FromSeconds(15);

    private readonly TaskButtons _taskButtons = new();
    private readonly DispatcherQueueTimer _settle;
    private readonly DispatcherQueueTimer _safety;

    private bool _measuring;
    private bool _pending;
    private int _failedMeasurements;
    private string? _lastError;
    private bool _reportedNoButtons;
    private bool _disposed;

    public TaskButtonsMonitor()
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        _settle = queue.CreateTimer();
        _settle.Interval = SettleDelay;
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => _ = MeasureAsync();

        _safety = queue.CreateTimer();
        _safety.Interval = SafetyInterval;
        _safety.Tick += (_, _) => _ = MeasureAsync();
    }

    /// <summary>Raised on the UI thread after every measurement, whether or not it succeeded.</summary>
    public event EventHandler? Measured;

    /// <summary>
    /// The rightmost app button's right edge in physical screen pixels, 0 without any. Null while unknown: before
    /// a measurement succeeded, without a taskbar, or after several failures in a row.
    /// </summary>
    public int? RightEdge { get; private set; }

    /// <summary>True once a measurement has finished, successful or not.</summary>
    public bool HasMeasured { get; private set; }

    /// <summary>Starts watching and measures right away; does nothing while already watching.</summary>
    /// <remarks>Called on every refresh of the badges, which must not each cost a scan of the taskbar.</remarks>
    public void Start()
    {
        if (_safety.IsRunning)
        {
            return;
        }

        _safety.Start();
        _ = MeasureAsync();
    }

    /// <summary>Stops watching; the next <see cref="Start"/> waits for a fresh measurement again.</summary>
    /// <remarks>The taskbar may have changed in between, so the last result no longer counts as measured.</remarks>
    public void Stop()
    {
        _safety.Stop();
        _settle.Stop();
        HasMeasured = false;
    }

    /// <summary>The taskbar may have changed; measures once Explorer has had time to lay out the buttons.</summary>
    public void Invalidate()
    {
        // Not restarted while it runs: a burst of window events still costs one measurement, and a window that
        // redraws nonstop (progress in its title) cannot put it off until the safety interval
        if (_safety.IsRunning && !_settle.IsRunning)
        {
            _settle.Start();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    // One measurement at a time: a busy Explorer must not pile them up. A request during one runs after it
    private async Task MeasureAsync()
    {
        if (_measuring)
        {
            _pending = true;
            return;
        }

        _measuring = true;
        var before = RightEdge;
        try
        {
            RightEdge = await Task.Run(_taskButtons.RightEdge);
            _failedMeasurements = 0;
            _lastError = null;
            ReportNoButtons();
        }
        catch (Exception exception)
        {
            // E.g. Explorer was busy; the last extent stays for a few tries. Kept for longer, it would miss
            // buttons opened since, so after that the tray icons take over until a measurement succeeds
            if (++_failedMeasurements >= MaxFailedMeasurements)
            {
                RightEdge = null;
            }

            var error = $"{exception.GetType()}: {exception.Message}";
            if (error != _lastError)
            {
                CrashLog.Write("TaskButtons", exception);
                _lastError = error;
            }
        }
        finally
        {
            _measuring = false;
            HasMeasured = true;
        }

        if (_disposed)
        {
            return;
        }

        // A button still sliding in or out changes the result; measuring until it holds catches its final place
        if (_pending || RightEdge != before)
        {
            _pending = false;
            Invalidate();
        }

        Measured?.Invoke(this, EventArgs.Empty);
    }

    // No button at all is rare, as pinned apps have one too. Logged once, in case a Windows update renamed the
    // button class and the element covers the buttons again
    private void ReportNoButtons()
    {
        if (RightEdge == 0 && !_reportedNoButtons)
        {
            CrashLog.Write("TaskButtons", new InvalidOperationException("No taskbar app buttons found"));
            _reportedNoButtons = true;
        }
    }
}
