using Jelling.Core;
using Windows.Devices.Enumeration;

namespace Jelling.App;

/// <summary>
/// Reports when Bluetooth devices appear, disappear, connect, disconnect or change their battery
/// level. It only signals; the list itself is re-read through <see cref="PnpBatteryProvider"/>.
/// Events arrive on thread-pool threads.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    // Not at once: a watcher that aborts again right away would otherwise restart in a tight loop, each
    // time enumerating every device container and refreshing the list
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StableRun = TimeSpan.FromMinutes(10);

    private readonly DeviceWatcher _nodes;
    private readonly DeviceWatcher _containers;
    private readonly RestartBackoff _nodesBackoff = new(RestartDelay, MaxRestartDelay, StableRun);
    private readonly RestartBackoff _containersBackoff = new(RestartDelay, MaxRestartDelay, StableRun);

    // The containers of Bluetooth nodes; every other container (monitors, USB hubs, ...) changes nothing shown
    private readonly HashSet<Guid> _bluetoothContainers = [];
    private readonly Lock _gate = new();

    private volatile bool _nodesEnumerated;
    private volatile bool _containersEnumerated;
    private volatile bool _disposed;

    public event EventHandler? Changed;

    public DeviceChangeWatcher()
    {
        // Updated only fires for requested properties, so the battery key must be in the list
        _nodes = DeviceInformation.CreateWatcher(
            PnpBatteryProvider.PresentDevicesAqs,
            [PnpBatteryProvider.InstanceIdKey, PnpBatteryProvider.ContainerIdKey, PnpBatteryProvider.BatteryKey],
            DeviceInformationKind.Device);

        // Disconnected devices stay present; only the container's Connected flag changes
        _containers = DeviceInformation.CreateWatcher(
            "", [PnpBatteryProvider.ConnectedKey], DeviceInformationKind.DeviceContainer);

        // Startup enumerates every existing device as Added; that is not a change
        _nodes.Added += (_, device) => OnNodeAdded(device);
        _nodes.Updated += (_, update) => { if (IsBluetooth(update.Id)) Raise(); };
        _nodes.Removed += (_, update) => { if (IsBluetooth(update.Id)) Raise(); };
        _nodes.EnumerationCompleted += (_, _) => OnEnumerationCompleted(() => _nodesEnumerated = true);
        _nodes.Stopped += (_, _) => OnStopped(_nodes, _nodesBackoff, () => _nodesEnumerated = false);

        // A new container may get its Bluetooth nodes only afterwards, so Added is not filtered
        _containers.Added += (_, _) => { if (_containersEnumerated) Raise(); };
        _containers.Updated += (_, update) => { if (IsBluetoothContainer(update.Id)) Raise(); };
        _containers.Removed += (_, update) => { if (IsBluetoothContainer(update.Id)) Raise(); };
        _containers.EnumerationCompleted += (_, _) => OnEnumerationCompleted(() => _containersEnumerated = true);
        _containers.Stopped += (_, _) => OnStopped(_containers, _containersBackoff, () => _containersEnumerated = false);
    }

    public void Start()
    {
        _nodes.Start();
        _containers.Start();
    }

    public void Dispose()
    {
        // Under the lock, so a restart cannot start a watcher again between its check and Stop: it either
        // started before (and is stopped here) or sees _disposed. Stop itself needs no lock
        lock (_gate)
        {
            _disposed = true;
        }

        Stop(_nodes);
        Stop(_containers);
    }

    private static void Stop(DeviceWatcher watcher)
    {
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    private void OnNodeAdded(DeviceInformation device)
    {
        if (!IsBluetooth(device.Id))
        {
            return;
        }

        if (device.Properties.GetValueOrDefault(PnpBatteryProvider.ContainerIdKey) is Guid containerId)
        {
            lock (_gate)
            {
                _bluetoothContainers.Add(containerId);
            }
        }

        if (_nodesEnumerated)
        {
            Raise();
        }
    }

    // Until both watchers have enumerated, Added is ignored and containers whose nodes are not known yet are
    // filtered out; a device that connected meanwhile is reported here. The list read before the start, or
    // before a restart, misses it otherwise
    // Under the lock: the two watchers complete on different threads, and each must see the other's flag,
    // or both could miss that they finished together
    private void OnEnumerationCompleted(Action setEnumerated)
    {
        bool both;
        lock (_gate)
        {
            setEnumerated();
            both = _nodesEnumerated && _containersEnumerated;
        }

        if (both)
        {
            Raise();
        }
    }

    private bool IsBluetoothContainer(string id)
    {
        lock (_gate)
        {
            return Guid.TryParse(id, out var containerId) && _bluetoothContainers.Contains(containerId);
        }
    }

    // Windows aborts a watcher e.g. when its service restarts; it does not start again by itself.
    // Stopped also follows Dispose's Stop, which must stay stopped
    // resetEnumerated: the restart enumerates everything as Added again
    private void OnStopped(DeviceWatcher watcher, RestartBackoff backoff, Action resetEnumerated)
    {
        if (_disposed || watcher.Status != DeviceWatcherStatus.Aborted)
        {
            return;
        }

        resetEnumerated();
        TimeSpan delay;
        lock (_gate)
        {
            delay = backoff.FirstDelay(Environment.TickCount64);
        }

        _ = RestartAsync(watcher, backoff, delay);
    }

    // Whatever changed while the watcher was down is reported once it has enumerated again
    private async Task RestartAsync(DeviceWatcher watcher, RestartBackoff backoff, TimeSpan firstDelay)
    {
        for (var delay = firstDelay; ; delay = backoff.Longer(delay))
        {
            await Task.Delay(delay);
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    watcher.Start();
                    backoff.Started(delay, Environment.TickCount64);
                    return;
                }
                catch (Exception exception)
                {
                    // Without a watcher the taskbar would only update when the flyout opens
                    CrashLog.Write("DeviceWatcher", exception);
                }
            }
        }
    }

    // Classic devices enumerate under BTHENUM, BLE devices under BTHLE
    private static bool IsBluetooth(string instanceId) =>
        instanceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
        || instanceId.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase);

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
