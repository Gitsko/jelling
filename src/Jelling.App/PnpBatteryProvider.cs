using Jelling.Core;
using Windows.Devices.Enumeration;

namespace Jelling.App;

/// <summary>
/// Reads the battery levels Windows itself shows under Settings > Bluetooth & devices.
/// </summary>
public sealed class PnpBatteryProvider : IBatteryProvider
{
    internal const string InstanceIdKey = "System.Devices.DeviceInstanceId";
    internal const string ContainerIdKey = "System.Devices.ContainerId";
    internal const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string CategoryIdsKey = "System.Devices.CategoryIds";
    internal const string ConnectedKey = "System.Devices.Connected";
    internal const string PresentDevicesAqs = "System.Devices.Present:=System.StructuredQueryType.Boolean#True";

    public async Task<IReadOnlyList<BatteryDevice>> GetDevicesAsync()
    {
        var nodesTask = DeviceInformation.FindAllAsync(
            PresentDevicesAqs, [InstanceIdKey, ContainerIdKey, BatteryKey], DeviceInformationKind.Device).AsTask();
        var containersTask = DeviceInformation.FindAllAsync(
            "", [CategoryIdsKey, ConnectedKey], DeviceInformationKind.DeviceContainer).AsTask();
        var filtersTask = BluetoothAudio.GetFiltersAsync();

        // All awaited together: should several fail, the other exceptions are observed too instead of
        // surfacing later as unobserved tasks
        await Task.WhenAll(nodesTask, containersTask, filtersTask);
        var nodes = nodesTask.Result.Select(ToNode).OfType<PnpNode>();
        var containers = containersTask.Result.Select(ToContainer).OfType<DeviceContainer>();

        return DeviceMerger.Merge(nodes, containers, filtersTask.Result);
    }

    private static PnpNode? ToNode(DeviceInformation device) =>
        device.Properties.GetValueOrDefault(InstanceIdKey) is string instanceId
        && device.Properties.GetValueOrDefault(ContainerIdKey) is Guid containerId
            ? new PnpNode(
                instanceId,
                containerId,
                device.Properties.GetValueOrDefault(BatteryKey) is { } battery ? Convert.ToInt32(battery) : null)
            : null;

    private static DeviceContainer? ToContainer(DeviceInformation container) =>
        Guid.TryParse(container.Id, out var id)
            ? new DeviceContainer(
                id,
                container.Name,
                container.Properties.GetValueOrDefault(CategoryIdsKey) as string[] ?? [],
                container.Properties.GetValueOrDefault(ConnectedKey) is true)
            : null;
}
