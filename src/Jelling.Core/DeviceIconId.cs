using System.Security.Cryptography;
using System.Text;

namespace Jelling.Core;

public static class DeviceIconId
{
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
