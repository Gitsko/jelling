using System.Runtime.InteropServices;
using System.Text;
using Jelling.Core;

namespace Jelling.App;

/// <summary>
/// Appends exceptions to %LOCALAPPDATA%\Jelling\crash.log; a tray app has no console to report to.
/// </summary>
public static class CrashLog
{
    // A failure that repeats for months must not fill the disk; one older file is kept
    private const long MaxLength = 1024 * 1024;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jelling", "crash.log");

    private static readonly string OldLogPath = Path.ChangeExtension(LogPath, ".old.log");

    private static readonly Lock Gate = new();

    // Paths in stack traces and messages carry the user name; the log gets posted in issues
    private static readonly PathRedactor UserProfile = UserProfileRedactor();

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (new FileInfo(LogPath) is { Exists: true, Length: > MaxLength })
                {
                    File.Move(LogPath, OldLogPath, overwrite: true);
                }

                var details = exception is null ? "" : UserProfile.Redact(exception.ToString());
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{source}] {details}{Environment.NewLine}");
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason the app goes down
        }
    }

    // The 8.3 short form too: TEMP often holds it
    private static PathRedactor UserProfileRedactor()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new PathRedactor([profile, profile.Length > 0 ? ShortPath(profile) : ""], "%USERPROFILE%");
    }

    private static string ShortPath(string path)
    {
        var buffer = new StringBuilder(260);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString(0, (int)length) : "";
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}
