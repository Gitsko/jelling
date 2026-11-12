using System.ComponentModel;
using System.Runtime.InteropServices;
using Jelling.Core;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace Jelling.App;

/// <summary>
/// Connects and disconnects Bluetooth audio devices the way the Bluetooth settings do: through the
/// one-shot properties the Bluetooth audio driver offers on each of its audio filters.
/// </summary>
/// <remarks>
/// Only audio devices: Windows offers no call that connects a mouse or keyboard, they connect themselves.
/// </remarks>
public static partial class BluetoothAudio
{
    // KSCATEGORY_AUDIO; a headset has one filter per profile, A2DP and Hands-Free
    internal const string FiltersAqs =
        "System.Devices.InterfaceClassGuid:=\"{6994AD04-93EF-11D0-A3CC-00A0C9223196}\""
        + " AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";

    // KSPROPSETID_BtAudio from ksmedia.h
    private static readonly Guid BtAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0;
    private const uint OneShotDisconnect = 1;
    private const uint KsPropertyTypeGet = 1;

    // What a device without any audio filter left reports, e.g. one removed since the row was built
    private const int ErrorFileNotFound = 2;

    // CTL_CODE(FILE_DEVICE_KS, 0, METHOD_NEITHER, FILE_ANY_ACCESS)
    private const uint IoctlKsProperty = 0x2F0003;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;

    /// <summary>Every audio filter, Bluetooth or not; <see cref="DeviceMerger"/> picks the Bluetooth ones.</summary>
    public static async Task<IReadOnlyList<AudioFilter>> GetFiltersAsync() =>
        (await FindFiltersAsync()).Select(f => f.Filter).ToList();

    /// <summary>
    /// Asks the driver to connect or disconnect every Bluetooth audio profile of the device. It returns once the
    /// driver took the request; the device changes its state a moment later and the watcher reports that.
    /// </summary>
    /// <exception cref="Win32Exception">No filter took the request; carries the last filter's error.</exception>
    public static async Task SetConnectedAsync(Guid deviceId, bool connect)
    {
        // Looked up again instead of kept from the last refresh: re-pairing gives the filters new paths
        var paths = (await FindFiltersAsync())
            .Where(f => f.Filter.ContainerId == deviceId && DeviceMerger.IsBluetoothAudioFilter(f.Filter.InstanceId))
            .Select(f => f.Path)
            .ToList();

        // Each request blocks until the driver has it, so off the UI thread
        await Task.Run(() =>
        {
            // Every profile gets the request, also after one took it; one taking it is enough
            var tookIt = false;
            var lastError = ErrorFileNotFound;
            foreach (var path in paths)
            {
                var error = Send(path, connect ? OneShotReconnect : OneShotDisconnect);
                tookIt |= error == 0;
                lastError = error == 0 ? lastError : error;
            }

            if (!tookIt)
            {
                throw new Win32Exception(lastError);
            }
        });
    }

    private static async Task<IEnumerable<(string Path, AudioFilter Filter)>> FindFiltersAsync()
    {
        var interfaces = await DeviceInformation.FindAllAsync(
            FiltersAqs, [PnpBatteryProvider.InstanceIdKey, PnpBatteryProvider.ContainerIdKey], DeviceInformationKind.DeviceInterface);

        return interfaces.Select(ToFilter).OfType<(string, AudioFilter)>();
    }

    private static (string Path, AudioFilter Filter)? ToFilter(DeviceInformation filter) =>
        filter.Properties.GetValueOrDefault(PnpBatteryProvider.InstanceIdKey) is string instanceId
        && filter.Properties.GetValueOrDefault(PnpBatteryProvider.ContainerIdKey) is Guid containerId
            ? (filter.Id, new AudioFilter(instanceId, containerId))
            : null;

    /// <returns>0 when the driver took the request, otherwise the Win32 error.</returns>
    private static int Send(string path, uint propertyId)
    {
        using var filter = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (filter.IsInvalid)
        {
            return Marshal.GetLastPInvokeError();
        }

        var property = new KsProperty { Set = BtAudioPropertySet, Id = propertyId, Flags = KsPropertyTypeGet };
        return DeviceIoControl(
            filter, IoctlKsProperty, ref property, Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _, IntPtr.Zero)
            ? 0
            : Marshal.GetLastPInvokeError();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle device, uint code, ref KsProperty input, int inputSize, IntPtr output, int outputSize,
        out int returned, IntPtr overlapped);
}
