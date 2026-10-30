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

                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{source}] {exception}{Environment.NewLine}");
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason the app goes down
        }
    }
}
