using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

internal sealed record AutoHotkeyInstallerResult(
    string Version,
    string InstallerPath,
    int ExitCode);

/// <summary>
/// Downloads, verifies and launches the latest stable AutoHotkey v2 installer from the official site.
/// </summary>
internal sealed class AutoHotkeyInstallerService
{
    private const string StableRoot = "https://www.autohotkey.com/download/2.0/";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>
    /// Downloads the latest stable setup package, verifies its published SHA-256 and waits for setup to finish.
    /// </summary>
    internal async Task<AutoHotkeyInstallerResult> DownloadAndInstallLatestStableAsync(
        CancellationToken cancellationToken = default)
    {
        var version = await ResolveLatestStableVersionAsync(cancellationToken);
        var fileName = $"AutoHotkey_{version}_setup.exe";
        var installerUri = new Uri(new Uri(StableRoot), fileName);
        var hashUri = new Uri(new Uri(StableRoot), fileName + ".sha256");

        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            "AutoHotkeyUX.Modern",
            "downloads");

        Directory.CreateDirectory(downloadDirectory);

        var installerPath = Path.Combine(downloadDirectory, fileName);
        var tempPath = installerPath + ".partial";

        try
        {
            await DownloadFileAsync(installerUri, tempPath, cancellationToken);

            var expectedHash = await DownloadExpectedHashAsync(
                hashUri,
                cancellationToken);

            var actualHash = await ComputeSha256Async(
                tempPath,
                cancellationToken);

            if (!string.Equals(
                    expectedHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"AutoHotkey installer SHA-256 mismatch. Expected {expectedHash}, got {actualHash}.");
            }

            File.Move(tempPath, installerPath, true);

            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "AutoHotkey");

            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"/silent /user /to \"{installDirectory}\"",
                    UseShellExecute = true
                })
                ?? throw new InvalidOperationException(
                    "Windows could not start the AutoHotkey installer.");

            await process.WaitForExitAsync(cancellationToken);

            return new AutoHotkeyInstallerResult(
                version,
                installerPath,
                process.ExitCode);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // A stale partial download is harmless and can be replaced next time.
                }
            }
        }
    }

    /// <summary>
    /// Resolves the latest stable 2.0 version using version.txt first and the official index as fallback.
    /// </summary>
    private static async Task<string> ResolveLatestStableVersionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var versionText = await HttpClient.GetStringAsync(
                new Uri(new Uri(StableRoot), "version.txt"),
                cancellationToken);

            var trimmed = versionText.Trim();
            if (Version.TryParse(trimmed, out var parsed)
                && parsed.Major == 2
                && parsed.Minor == 0)
            {
                return parsed.ToString();
            }
        }
        catch (HttpRequestException)
        {
            // Some mirrors do not expose version.txt; fall through to directory parsing.
        }

        var html = await HttpClient.GetStringAsync(
            StableRoot,
            cancellationToken);

        var versions = Regex.Matches(
                html,
                @"AutoHotkey_(?<version>2\.0\.\d+)_setup\.exe",
                RegexOptions.IgnoreCase)
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => Version.TryParse(value, out var version)
                ? version
                : null)
            .Where(version => version is not null)
            .Cast<Version>()
            .OrderByDescending(version => version)
            .ToList();

        return versions.FirstOrDefault()?.ToString()
            ?? throw new InvalidOperationException(
                "Could not determine the latest stable AutoHotkey v2 release from the official download index.");
    }

    /// <summary>
    /// Downloads a file to disk without buffering the whole installer in memory.
    /// </summary>
    private static async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Reads the official checksum file and extracts its 64-character SHA-256 digest.
    /// </summary>
    private static async Task<string> DownloadExpectedHashAsync(
        Uri hashUri,
        CancellationToken cancellationToken)
    {
        var text = await HttpClient.GetStringAsync(
            hashUri,
            cancellationToken);

        var match = Regex.Match(
            text,
            @"\b(?<hash>[A-Fa-f0-9]{64})\b");

        return match.Success
            ? match.Groups["hash"].Value
            : throw new InvalidDataException(
                "The official AutoHotkey checksum file did not contain a valid SHA-256 digest.");
    }

    /// <summary>
    /// Computes SHA-256 using asynchronous file reads for the downloaded installer.
    /// </summary>
    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Creates the shared HTTP client with a small, explicit user agent.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "AutoHotkeyUX.Modern/1.0");

        return client;
    }
}
