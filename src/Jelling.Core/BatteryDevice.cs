namespace Jelling.Core;

public enum DeviceKind
{
    Other,
    Headset,
    Mouse,
    Keyboard,
    GameController,
    Speaker,
}

/// <param name="Id">The device container id; stable while the device stays paired.</param>
/// <param name="BatteryPercent">Null when the device reports no battery level.</param>
public sealed record BatteryDevice(Guid Id, string Name, DeviceKind Kind, int? BatteryPercent, bool IsConnected);
