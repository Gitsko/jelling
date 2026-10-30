using Windows.Devices.Enumeration;

namespace Jelling.App;

/// <summary>
/// Reports when Bluetooth devices appear, disappear, connect, disconnect or change their battery
/// level. It only signals; the list itself is re-read through <see cref="PnpBatteryProvider"/>.
/// Events arrive on thread-pool threads.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    // Not at once: a watcher that aborts again right away would otherwise restart in a tight loop
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly DeviceWatcher _nodes;
    private readonly DeviceWatcher _containers;

    // The containers of Bluetooth nodes; every other container (monitors, USB hubs, ...) changes nothing shown
    private readonly HashSet<Guid> _bluetoothContainers = [];
    private readonly Lock _gate = new();

    private volatile bool _nodesEnumerated;
    private volatile bool _containersEnumerated;
    private volatile bool _disposed;

    /// <summary>Raised with a short description of what changed.</summary>
    public event EventHandler<string>? Changed;

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
        _nodes.Updated += (_, update) => { if (IsBluetooth(update.Id)) Raise("node updated", update.Id, update.Properties.Keys); };
        _nodes.Removed += (_, update) => { if (IsBluetooth(update.Id)) Raise("node removed", update.Id, []); };
        _nodes.EnumerationCompleted += (_, _) => _nodesEnumerated = true;
        _nodes.Stopped += (_, _) => OnStopped(_nodes, () => _nodesEnumerated = false);

        // A new container may get its Bluetooth nodes only afterwards, so Added is not filtered
        _containers.Added += (_, container) => { if (_containersEnumerated) Raise("container added", container.Id, []); };
        _containers.Updated += (_, update) => { if (IsBluetoothContainer(update.Id)) Raise("container updated", update.Id, update.Properties.Keys); };
        _containers.Removed += (_, update) => { if (IsBluetoothContainer(update.Id)) Raise("container removed", update.Id, []); };
        _containers.EnumerationCompleted += (_, _) => _containersEnumerated = true;
        _containers.Stopped += (_, _) => OnStopped(_containers, () => _containersEnumerated = false);
    }

    public void Start()
    {
        _nodes.Start();
        _containers.Start();
    }

    public void Dispose()
    {
        _disposed = true;
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
            Raise("node added", device.Id, []);
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
    private void OnStopped(DeviceWatcher watcher, Action resetEnumerated)
    {
        if (_disposed || watcher.Status != DeviceWatcherStatus.Aborted)
        {
            return;
        }

        resetEnumerated();
        _ = RestartAsync(watcher);
    }

    private async Task RestartAsync(DeviceWatcher watcher)
    {
        await Task.Delay(RestartDelay);
        if (_disposed)
        {
            return;
        }

        try
        {
            watcher.Start();

            // Whatever changed while the watcher was down went unreported
            Raise("watcher restarted", "", []);
        }
        catch (Exception exception)
        {
            CrashLog.Write("DeviceWatcher", exception);
        }
    }

    // Classic devices enumerate under BTHENUM, BLE devices under BTHLE
    private static bool IsBluetooth(string instanceId) =>
        instanceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
        || instanceId.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase);

    private void Raise(string what, string id, IEnumerable<string> properties) =>
        Changed?.Invoke(this, $"{what} {id} [{string.Join(", ", properties)}]");
}
