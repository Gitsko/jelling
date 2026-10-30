namespace Jelling.App;

/// <summary>
/// Appends exceptions to %LOCALAPPDATA%\Jelling\crash.log; a tray app has no console to report to.
/// </summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jelling", "crash.log");

    private static readonly Lock Gate = new();

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{source}] {exception}{Environment.NewLine}");
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason the app goes down
        }
    }
}
