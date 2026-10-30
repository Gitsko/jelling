namespace Jelling.Core;

public interface IBatteryProvider
{
    Task<IReadOnlyList<BatteryDevice>> GetDevicesAsync();
}
