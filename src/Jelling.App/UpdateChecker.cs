using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jelling.Core;
using Microsoft.UI.Dispatching;

namespace Jelling.App;

/// <summary>
/// Looks up the latest release on GitHub once a day. It only reports a newer version; the user downloads it
/// from the releases page, as the app is not code-signed and must not replace itself.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    // Drafts and pre-releases are left out of "latest", so only finished versions are offered
    private const string LatestReleaseApi = "https://api.github.com/repos/Gitsko/jelling/releases/latest";

    // Autostart runs before the network is up; the first check waits a minute
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly DispatcherQueueTimer _timer;

    public UpdateChecker()
    {
        // GitHub refuses API requests without a User-Agent
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jelling", CurrentVersion.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = FirstCheckDelay;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = CheckInterval;
            _ = CheckAsync();
        };
        _timer.Start();
    }

    public static Version CurrentVersion { get; } = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Raised on the UI thread with the newer release's version.</summary>
    public event EventHandler<Version>? UpdateAvailable;

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    private async Task CheckAsync()
    {
        try
        {
            var release = await _http.GetFromJsonAsync<Release>(LatestReleaseApi);
            if (ReleaseVersion.Newer(release?.TagName, CurrentVersion) is { } latest)
            {
                UpdateAvailable?.Invoke(this, latest);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline, or no release yet; the next day's check tries again. Not worth a crash.log entry
        }
        catch (Exception exception)
        {
            CrashLog.Write("Update", exception);
        }
    }

    private sealed record Release([property: JsonPropertyName("tag_name")] string? TagName);
}
