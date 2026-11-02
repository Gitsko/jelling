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
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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

                var details = UserProfile.Length > 0
                    ? exception?.ToString().Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
                    : exception?.ToString();
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{source}] {details}{Environment.NewLine}");
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason the app goes down
        }
    }
}
