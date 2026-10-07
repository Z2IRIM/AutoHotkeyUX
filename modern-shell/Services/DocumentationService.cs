using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Extracts the bundled CHM headlessly into a validated versioned offline cache.</summary>
internal sealed class DocumentationService
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly Func<ProcessStartInfo, Process> _startExtractor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lifetime = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task<DocumentationLocation>> _active = [];
    private bool _shuttingDown;
    private Task? _draining;
    private static readonly string[] RequiredFiles = ["docs/index.htm", "docs/static/content.js", "docs/static/source/data_toc.js", "docs/static/theme.css"];
    private sealed record CacheManifest(string SourceHash, Dictionary<string, string> Files);

    /// <summary>Reuses runtime discovery and permits a controlled extractor in explicit lifecycle diagnostics.</summary>
    internal DocumentationService(AutoHotkeyIntegration integration, Func<ProcessStartInfo, Process>? startExtractor = null)
    {
        _integration = integration;
        _startExtractor = startExtractor ?? (start => Process.Start(start) ?? throw new InvalidOperationException("The offline help extractor could not start."));
    }

    /// <summary>Owns each accepted preparation so application shutdown can cancel and await its cleanup.</summary>
    internal Task<DocumentationLocation> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        lock (_lifetime)
        {
            if (_shuttingDown) throw new InvalidOperationException("The application is shutting down.");
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            var operation = RunPreparationAsync(linked.Token);
            _active.Add(operation);
            _ = operation.ContinueWith(completed =>
            {
                lock (_lifetime) _active.Remove(completed);
                linked.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return operation;
        }
    }

    /// <summary>Stops accepting work and waits for every owned extractor and staging cleanup before exit.</summary>
    internal Task CancelAndDrainAsync()
    {
        lock (_lifetime)
        {
            if (_draining is not null) return _draining;
            _shuttingDown = true; _shutdown.Cancel();
            return _draining = DrainAsync(_active.ToArray());
        }
    }

    /// <summary>Completes all accepted preparations, logging failures without abandoning exit cleanup.</summary>
    private async Task DrainAsync(Task[] operations)
    {
        try { await Task.WhenAll(operations); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ServiceDiagnostics.Write("Documentation", "Preparation failed while shutting down.", ex); }
        finally { _shutdown.Dispose(); }
    }

    /// <summary>Serializes extraction and validates actual files rather than trusting hh.exe's exit code.</summary>
    private async Task<DocumentationLocation> RunPreparationAsync(CancellationToken cancellationToken)
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
        RejectPathLinks(parent);
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
            using var process = _startExtractor(start);
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
            foreach (var file in EnumerateRegularFiles(staging, cancellationToken))
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

    /// <summary>Checks the complete regular file tree against its manifest, excluding linked or unlisted content.</summary>
    internal static bool ValidateCache(string root, string sourceHash, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(root)) return false;
            RejectPathLinks(root);
            var manifestPath = Path.Combine(root, ".cache.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 256 * 1024) return false;
            RejectLink(manifestPath);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || manifest.SourceHash != sourceHash || manifest.Files is null || manifest.Files.Count is < 4 or > 4096) return false;
            if (RequiredFiles.Any(file => !manifest.Files.ContainsKey(file))) return false;
            var actual = EnumerateRegularFiles(root, cancellationToken)
                .Where(file => !string.Equals(Path.GetRelativePath(root, file), ".cache.json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
            if (actual.Count != manifest.Files.Count) return false;
            foreach (var (relative, expectedHash) in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!actual.TryGetValue(relative, out var path)) return false;
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

    /// <summary>Rejects links anywhere in an existing cache path's ancestor chain before mapping or writing.</summary>
    private static void RejectPathLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try { RejectLink(current); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }

    /// <summary>Enumerates a bounded actual tree without following reparse points in intermediate directories.</summary>
    private static IEnumerable<string> EnumerateRegularFiles(string root, CancellationToken cancellationToken)
    {
        RejectPathLinks(root);
        var pending = new Stack<string>(); pending.Push(root);
        var entries = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested(); RejectLink(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > 8192) throw new InvalidDataException("The manual cache contains too many entries.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The manual cache contains a filesystem link.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path); else yield return path;
            }
        }
    }

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
