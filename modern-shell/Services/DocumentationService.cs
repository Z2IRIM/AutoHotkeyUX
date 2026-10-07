using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Extracts the bundled CHM headlessly into a validated versioned offline cache.</summary>
internal sealed class DocumentationService
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly string[] RequiredFiles = ["docs/index.htm", "docs/static/content.js", "docs/static/source/data_toc.js", "docs/static/theme.css"];
    private sealed record CacheManifest(string SourceHash, Dictionary<string, string> Files);

    /// <summary>Reuses the existing runtime discovery without modifying its materialization pipeline.</summary>
    internal DocumentationService(AutoHotkeyIntegration integration) => _integration = integration;

    /// <summary>Serializes extraction and validates actual files rather than trusting hh.exe's exit code.</summary>
    internal async Task<DocumentationLocation> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => PrepareAsync(cancellationToken), cancellationToken); }
        finally { _gate.Release(); }
    }

    /// <summary>Publishes a complete cache only after extraction and file hashes have been verified.</summary>
    private async Task<DocumentationLocation> PrepareAsync(CancellationToken cancellationToken)
    {
        var runtime = _integration.FindRuntime() ?? throw new InvalidOperationException("The built-in runtime is unavailable.");
        var chm = Path.Combine(Path.GetDirectoryName(runtime.Path)!, "AutoHotkey.chm");
        if (!File.Exists(chm)) throw new FileNotFoundException("The bundled offline manual is missing.", chm);
        var sourceHash = Hash(chm);
        var version = runtime.Version.TrimEnd('.');
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHotkeyUX.Modern", "documentation");
        Directory.CreateDirectory(parent);
        RejectLink(parent);
        var root = Path.Combine(parent, $"{version}-{sourceHash[..12]}");
        if (ValidateCache(root, sourceHash, cancellationToken)) return new(root, "docs/index.htm", version);
        var staging = Path.Combine(parent, $".stage-{Guid.NewGuid():N}");
        var previous = Path.Combine(parent, $".invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "hh.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("-decompile"); start.ArgumentList.Add(staging); start.ArgumentList.Add(chm);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The offline help extractor could not start.");
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Offline manual extraction took too long. Retry from Documentation.");
            }
            if (RequiredFiles.Any(file => !File.Exists(Path.Combine(staging, file))))
                throw new InvalidDataException("Offline extraction did not produce the required manual files. Retry from Documentation.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLink(file);
                files.Add(Path.GetRelativePath(staging, file).Replace('\\', '/'), Hash(file));
            }
            File.WriteAllText(Path.Combine(staging, ".cache.json"), JsonSerializer.Serialize(new CacheManifest(sourceHash, files)));
            if (!ValidateCache(staging, sourceHash, cancellationToken)) throw new InvalidDataException("Offline cache verification failed.");
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(root)) { RejectLink(root); Directory.Move(root, previous); }
            try { Directory.Move(staging, root); }
            catch { if (Directory.Exists(previous) && !Directory.Exists(root)) Directory.Move(previous, root); throw; }
            ServiceDiagnostics.Write("Documentation", $"Prepared offline manual {version} ({files.Count} files).");
            return new(root, "docs/index.htm", version);
        }
        finally
        {
            DeleteOwnedDirectory(staging, parent);
            DeleteOwnedDirectory(previous, parent);
        }
    }

    /// <summary>Detects missing, truncated, linked or altered files in a completed offline cache.</summary>
    internal static bool ValidateCache(string root, string sourceHash, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(root)) return false;
            RejectLink(root);
            var manifestPath = Path.Combine(root, ".cache.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 256 * 1024) return false;
            var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || manifest.SourceHash != sourceHash || manifest.Files is null || manifest.Files.Count is < 4 or > 4096) return false;
            if (RequiredFiles.Any(file => !manifest.Files.ContainsKey(file))) return false;
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var (relative, expectedHash) in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
                RejectLink(path);
                if (Hash(path) != expectedHash) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }

    /// <summary>Hashes cache/source bytes without changing the original CHM.</summary>
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }

    /// <summary>Rejects filesystem links at extraction/cache boundaries.</summary>
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("The manual cache must contain regular files and directories."); }

    /// <summary>Removes only a checked child staging directory owned by this materializer.</summary>
    private static void DeleteOwnedDirectory(string path, string parent)
    {
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cache cleanup escaped the application directory.");
        if (!Directory.Exists(full)) return;
        RejectLink(full);
        try { Directory.Delete(full, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ServiceDiagnostics.Write("Documentation", "Temporary cache cleanup failed.", ex); }
    }
}
