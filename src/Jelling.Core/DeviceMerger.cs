namespace Jelling.Core;

/// <summary>One PnP device node, as Windows reports it.</summary>
/// <param name="BatteryPercent">DEVPKEY_Bluetooth_Battery; null when the node has none.</param>
public sealed record PnpNode(string InstanceId, Guid ContainerId, int? BatteryPercent);

/// <summary>
/// The device container Windows groups all nodes of one physical device into.
/// </summary>
/// <param name="CategoryIds">Ordered by priority, e.g. "Input.Keyboard", "Input.Mouse" for a keyboard.</param>
public sealed record DeviceContainer(Guid Id, string Name, IReadOnlyList<string> CategoryIds, bool IsConnected);

/// <summary>
/// Joins Bluetooth device nodes with their containers. Classic devices keep the battery level on a
/// service node (e.g. Hands-Free), BLE devices on the device node; both share the device's container.
/// </summary>
public static class DeviceMerger
{
    public static IReadOnlyList<BatteryDevice> Merge(IEnumerable<PnpNode> nodes, IEnumerable<DeviceContainer> containers)
    {
        var containersById = containers.ToDictionary(c => c.Id);

        return nodes
            .Where(IsBluetoothNode)
            .GroupBy(n => n.ContainerId)
            .Where(g => g.Any(IsDeviceNode) && containersById.ContainsKey(g.Key))
            .Select(g =>
            {
                var container = containersById[g.Key];
                // The property is a raw byte; a value outside 0-100 is no level, and another node may still have one.
                // Ordered, so two nodes with different levels give the same one whatever order Windows lists them in
                var battery = g.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase)
                    .Select(n => n.BatteryPercent)
                    .FirstOrDefault(p => p is >= 0 and <= 100);
                return new BatteryDevice(container.Id, container.Name, KindFromCategories(container.CategoryIds), battery, container.IsConnected);
            })
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static readonly (string Prefix, DeviceKind Kind)[] KindsByCategoryPrefix =
    [
        ("Communication.Headset", DeviceKind.Headset),
        ("Audio.Headphone", DeviceKind.Headset),
        ("Audio.Speakers", DeviceKind.Speaker),
        ("Input.Mouse", DeviceKind.Mouse),
        ("Input.Keyboard", DeviceKind.Keyboard),
        ("Input.Gaming", DeviceKind.GameController),
    ];

    public static DeviceKind KindFromCategories(IEnumerable<string> categoryIds)
    {
        // Category ids come ordered by priority, so the first one with a known prefix decides
        foreach (var id in categoryIds)
        {
            foreach (var (prefix, kind) in KindsByCategoryPrefix)
            {
                if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }
        }

        return DeviceKind.Other;
    }

    // Classic devices enumerate under BTHENUM, BLE devices under BTHLE
    private static bool IsBluetoothNode(PnpNode node) =>
        node.InstanceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
        || node.InstanceId.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase);

    // The device node itself (BTHENUM\Dev_<mac>, BTHLE\Dev_<mac>), as opposed to its service nodes
    private static bool IsDeviceNode(PnpNode node) =>
        node.InstanceId.Contains(@"\Dev_", StringComparison.OrdinalIgnoreCase);
}
