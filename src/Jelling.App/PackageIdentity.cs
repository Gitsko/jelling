using System.Runtime.InteropServices;

namespace Jelling.App;

/// <summary>
/// Whether this process runs from the Microsoft Store package or as the unpackaged setup/ZIP build; one exe serves both.
/// </summary>
internal static partial class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>The package family name, stable across updates; null when unpackaged.</summary>
    public static string? FamilyName { get; } = ReadFamilyName();

    public static bool IsPackaged => FamilyName is not null;

    private static unsafe string? ReadFamilyName()
    {
        uint length = 0;
        // Fails with APPMODEL_ERROR_NO_PACKAGE when unpackaged, otherwise asks for the buffer size first
        if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer)
        {
            return null;
        }

        // The length counts the terminating null
        var buffer = new char[length];
        fixed (char* name = buffer)
        {
            return GetCurrentPackageFamilyName(ref length, name) == 0 ? new string(name, 0, (int)length - 1) : null;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint length, char* name);
}
