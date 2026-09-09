using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vanta.Services;

internal sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Blake0v2/Vanta/releases/latest";
    private readonly HttpClient _client;

    public UpdateService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vanta-Auto-Clicker", CurrentVersion.ToString()));
    }

    public async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(LatestReleaseApi, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateResult(false, CurrentVersion, null, null, null, null, "No public releases have been published yet.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
        var releaseUrl = root.GetProperty("html_url").GetString();
        var latest = Version.TryParse(tag.Split('-')[0], out var parsed) ? parsed : new Version(0, 0, 0);
        string? installerUrl = null;
        string? installerName = null;
        string? checksumUrl = null;

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (name?.EndsWith("-x64.msi", StringComparison.OrdinalIgnoreCase) == true)
                {
                    installerName = name;
                    installerUrl = downloadUrl;
                }
                else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = downloadUrl;
                }
            }
        }

        return latest > CurrentVersion
            ? new UpdateResult(true, latest, releaseUrl, installerUrl, installerName, checksumUrl, $"Vanta Auto Clicker {latest} is available.")
            : new UpdateResult(false, latest, releaseUrl, installerUrl, installerName, checksumUrl, $"You're up to date on Vanta Auto Clicker {DisplayVersion(CurrentVersion)}.");
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateResult update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!update.UpdateAvailable || string.IsNullOrWhiteSpace(update.InstallerUrl) || string.IsNullOrWhiteSpace(update.InstallerName))
        {
            throw new InvalidOperationException("This release does not include a Windows installer.");
        }

        var installerUri = new Uri(update.InstallerUrl);
        if (!installerUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !installerUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The release installer URL is not trusted.");
        }

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vanta",
            "Updates");
        Directory.CreateDirectory(updateDirectory);
        var destinationPath = Path.Combine(updateDirectory, Path.GetFileName(update.InstallerName));
        var temporaryPath = destinationPath + ".download";

        try
        {
            using var response = await _client.GetAsync(
                installerUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            var buffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                if (totalBytes is > 0)
                {
                    progress?.Report((double)downloadedBytes / totalBytes.Value);
                }
            }

            await destination.FlushAsync(cancellationToken);
            destination.Close();

            if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
            {
                await VerifyChecksumAsync(temporaryPath, update.InstallerName, update.ChecksumUrl, cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, true);
            progress?.Report(1);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private async Task VerifyChecksumAsync(
        string installerPath,
        string installerName,
        string checksumUrl,
        CancellationToken cancellationToken)
    {
        var checksumUri = new Uri(checksumUrl);
        if (!checksumUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !checksumUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The release checksum URL is not trusted.");
        }

        var checksumText = await _client.GetStringAsync(checksumUri, cancellationToken);
        var expectedChecksum = checksumText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length >= 2 && string.Equals(parts[^1], installerName, StringComparison.OrdinalIgnoreCase))?
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expectedChecksum))
        {
            throw new InvalidDataException("The release checksum file does not list the installer.");
        }

        await using var installer = File.OpenRead(installerPath);
        var actualChecksum = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
        if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded installer did not match the published checksum.");
        }
    }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    private static string DisplayVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    public static void OpenRelease(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void ApplyInstallerAndRestart(string installerPath)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable))
        {
            throw new InvalidOperationException("Vanta Auto Clicker could not locate its executable.");
        }

        var installedExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Vanta Auto Clicker",
            "Vanta Auto Clicker.exe");
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vanta",
            "Updates",
            "install.log");

        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $vanta = Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            if ($null -ne $vanta) { $vanta.WaitForExit() }
            & "$env:SystemRoot\System32\msiexec.exe" /i '{{PowerShellLiteral(installerPath)}}' /qn /norestart MSIDISABLERMRESTART=1 /L*v '{{PowerShellLiteral(logPath)}}'
            $result = $LASTEXITCODE
            if (($result -eq 0 -or $result -eq 3010) -and (Test-Path -LiteralPath '{{PowerShellLiteral(installedExecutable)}}')) {
                Start-Process -FilePath '{{PowerShellLiteral(installedExecutable)}}' -ArgumentList "--update-result=$result"
            } elseif (Test-Path -LiteralPath '{{PowerShellLiteral(currentExecutable)}}') {
                Start-Process -FilePath '{{PowerShellLiteral(currentExecutable)}}' -ArgumentList "--update-result=$result"
            }
            """;

        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Vanta Auto Clicker could not start the update helper.");
        }
    }

    private static string PowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

internal sealed record UpdateResult(
    bool UpdateAvailable,
    Version LatestVersion,
    string? ReleaseUrl,
    string? InstallerUrl,
    string? InstallerName,
    string? ChecksumUrl,
    string Message);
