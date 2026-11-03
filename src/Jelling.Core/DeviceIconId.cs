using System.Security.Cryptography;
using System.Text;

namespace Jelling.Core;

public static class DeviceIconId
{
    // No device interface GUID; a fixed key keeps the separator's taskbar entry across starts
    private static readonly Guid SeparatorKey = Guid.Parse("6a656c6c-696e-6773-6570-617261746f72");

    /// <summary>
    /// The notify icon GUID for the separator after the device icons; bound to the exe path like theirs.
    /// </summary>
    public static Guid Separator(string exePath) => For(exePath, SeparatorKey);

    /// <summary>
    /// The notify icon GUID for one device. Windows remembers an icon's taskbar position by this
    /// GUID, but also binds it to the exe path; reusing it from another path makes NIM_ADD fail.
    /// </summary>
    public static Guid For(string exePath, Guid deviceId)
    {
        var key = $"{exePath.ToUpperInvariant()}|{deviceId}";
        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
