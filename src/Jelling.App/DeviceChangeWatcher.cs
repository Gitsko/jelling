using Windows.Devices.Enumeration;

namespace Jelling.App;

/// <summary>
/// Reports when Bluetooth devices appear, disappear, connect, disconnect or change their battery
/// level. It only signals; the list itself is re-read through <see cref="PnpBatteryProvider"/>.
/// Events arrive on thread-pool threads.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private readonly DeviceWatcher _nodes;
    private readonly DeviceWatcher _containers;
    private bool _nodesEnumerated;
    private bool _containersEnumerated;

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
        _nodes.Added += (_, device) => { if (_nodesEnumerated && IsBluetooth(device.Id)) Raise("node added", device.Id, []); };
        _nodes.Updated += (_, update) => { if (IsBluetooth(update.Id)) Raise("node updated", update.Id, update.Properties.Keys); };
        _nodes.Removed += (_, update) => { if (IsBluetooth(update.Id)) Raise("node removed", update.Id, []); };
        _nodes.EnumerationCompleted += (_, _) => _nodesEnumerated = true;

        _containers.Added += (_, container) => { if (_containersEnumerated) Raise("container added", container.Id, []); };
        _containers.Updated += (_, update) => Raise("container updated", update.Id, update.Properties.Keys);
        _containers.Removed += (_, update) => Raise("container removed", update.Id, []);
        _containers.EnumerationCompleted += (_, _) => _containersEnumerated = true;
    }

    public void Start()
    {
        _nodes.Start();
        _containers.Start();
    }

    public void Dispose()
    {
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

    // Classic devices enumerate under BTHENUM, BLE devices under BTHLE
    private static bool IsBluetooth(string instanceId) =>
        instanceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
        || instanceId.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase);

    private void Raise(string what, string id, IEnumerable<string> properties) =>
        Changed?.Invoke(this, $"{what} {id} [{string.Join(", ", properties)}]");
}
