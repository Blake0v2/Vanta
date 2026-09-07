using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Vanta.Services;

internal sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Blake0v2/Vanta/releases/latest";

    public async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vanta", CurrentVersion.ToString()));
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateResult(false, CurrentVersion, null, "No public releases have been published yet.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
        var url = root.GetProperty("html_url").GetString();
        var latest = Version.TryParse(tag.Split('-')[0], out var parsed) ? parsed : new Version(0, 0, 0);

        return latest > CurrentVersion
            ? new UpdateResult(true, latest, url, $"Vanta {latest} is available.")
            : new UpdateResult(false, latest, url, $"You're up to date on Vanta {CurrentVersion}.");
    }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    public static void OpenRelease(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

internal sealed record UpdateResult(bool UpdateAvailable, Version LatestVersion, string? ReleaseUrl, string Message);
