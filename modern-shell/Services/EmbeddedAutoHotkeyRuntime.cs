using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

internal sealed record EmbeddedRuntimeState(
    string RootDirectory,
    string RuntimePath,
    string UxDirectory,
    string Version);

/// <summary>
/// Materializes the AutoHotkey portable runtime bundled inside the single-file application.
/// </summary>
internal sealed class EmbeddedAutoHotkeyRuntime
{
    private const string RuntimeResourceName =
        "AutoHotkeyUX.Modern.Runtime.AutoHotkey.zip";

    private const string UxResourceName =
        "AutoHotkeyUX.Modern.Runtime.UX.zip";

    private const string ManifestResourceName =
        "AutoHotkeyUX.Modern.Runtime.Manifest.json";

    private static readonly object Gate = new();
    private readonly string? _baseDirectory;

    /// <summary>Allows explicit diagnostic instances to materialize payloads away from running user interpreters.</summary>
    internal EmbeddedAutoHotkeyRuntime(string? baseDirectory = null) => _baseDirectory = baseDirectory;

    /// <summary>
    /// Extracts and validates the embedded runtime into the app's private LocalAppData directory.
    /// </summary>
    internal EmbeddedRuntimeState EnsureReady()
    {
        lock (Gate)
        {
            var manifest = ReadManifest();
            var runtimeBytes = ReadResource(RuntimeResourceName);
            var uxBytes = ReadResource(UxResourceName);

            ValidateHash(
                runtimeBytes,
                manifest.RuntimeSha256,
                "AutoHotkey runtime");

            ValidateHash(
                uxBytes,
                manifest.UxSha256,
                "AutoHotkeyUX scripts");

            var baseDirectory = _baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AutoHotkeyUX.Modern",
                "runtime");

            var rootDirectory = Path.Combine(
                baseDirectory,
                manifest.Version);

            var runtimeMarker = Path.Combine(
                rootDirectory,
                ".runtime.sha256");

            var uxMarker = Path.Combine(
                rootDirectory,
                ".ux.sha256");

            if (IsCurrent(
                    rootDirectory,
                    runtimeMarker,
                    uxMarker,
                    manifest))
            {
                return BuildState(
                    rootDirectory,
                    manifest.Version);
            }

            Materialize(
                baseDirectory,
                rootDirectory,
                runtimeBytes,
                uxBytes,
                manifest);

            CleanupOldVersions(
                baseDirectory,
                rootDirectory);

            return BuildState(
                rootDirectory,
                manifest.Version);
        }
    }

    /// <summary>
    /// Returns a readable error instead of crashing runtime discovery if materialization fails.
    /// </summary>
    internal bool TryEnsureReady(
        out EmbeddedRuntimeState? state,
        out string? error)
    {
        try
        {
            state = EnsureReady();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            state = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Checks whether the already materialized payload matches both embedded payload hashes.
    /// </summary>
    private static bool IsCurrent(
        string rootDirectory,
        string runtimeMarker,
        string uxMarker,
        RuntimeManifest manifest)
    {
        if (!Directory.Exists(rootDirectory)
            || !File.Exists(runtimeMarker)
            || !File.Exists(uxMarker))
        {
            return false;
        }

        var runtimeHash = File.ReadAllText(runtimeMarker).Trim();
        var uxHash = File.ReadAllText(uxMarker).Trim();

        if (!runtimeHash.Equals(
                manifest.RuntimeSha256,
                StringComparison.OrdinalIgnoreCase)
            || !uxHash.Equals(
                manifest.UxSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var runtimePath = Path.Combine(
            rootDirectory,
            "v2",
            "AutoHotkey64.exe");

        var windowSpy = Path.Combine(
            rootDirectory,
            "UX",
            "WindowSpy.ahk");

        return File.Exists(runtimePath)
               && File.Exists(windowSpy);
    }

    /// <summary>
    /// Extracts runtime and UX payloads into a staging directory, validates them, then swaps atomically.
    /// </summary>
    private static void Materialize(
        string baseDirectory,
        string rootDirectory,
        byte[] runtimeBytes,
        byte[] uxBytes,
        RuntimeManifest manifest)
    {
        Directory.CreateDirectory(baseDirectory);

        var stagingDirectory =
            rootDirectory + ".staging-" + Guid.NewGuid().ToString("N");

        try
        {
            var runtimeDirectory = Path.Combine(
                stagingDirectory,
                "v2");

            var uxDirectory = Path.Combine(
                stagingDirectory,
                "UX");

            Directory.CreateDirectory(runtimeDirectory);
            Directory.CreateDirectory(uxDirectory);

            ExtractZip(
                runtimeBytes,
                runtimeDirectory);

            ExtractZip(
                uxBytes,
                uxDirectory);

            var runtimePath = Path.Combine(
                runtimeDirectory,
                "AutoHotkey64.exe");

            if (!File.Exists(runtimePath))
            {
                throw new InvalidDataException(
                    "The embedded AutoHotkey payload does not contain AutoHotkey64.exe.");
            }

            var version = FileVersionInfo
                .GetVersionInfo(runtimePath)
                .FileVersion
                ?? string.Empty;

            if (!version.StartsWith(
                    manifest.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Embedded AutoHotkey version mismatch. Expected {manifest.Version}, got {version}.");
            }

            File.WriteAllText(
                Path.Combine(stagingDirectory, ".runtime.sha256"),
                manifest.RuntimeSha256);

            File.WriteAllText(
                Path.Combine(stagingDirectory, ".ux.sha256"),
                manifest.UxSha256);

            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(
                    rootDirectory,
                    recursive: true);
            }

            Directory.Move(
                stagingDirectory,
                rootDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                try
                {
                    Directory.Delete(
                        stagingDirectory,
                        recursive: true);
                }
                catch
                {
                    // A stale staging folder is harmless and can be cleaned on a later launch.
                }
            }
        }
    }

    /// <summary>
    /// Extracts a ZIP resource while rejecting path traversal entries.
    /// </summary>
    private static void ExtractZip(
        byte[] archiveBytes,
        string destinationDirectory)
    {
        using var stream = new MemoryStream(
            archiveBytes,
            writable: false);

        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read);

        var root = Path.GetFullPath(
            destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(
                Path.Combine(
                    destinationDirectory,
                    entry.FullName));

            if (!destinationPath.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The embedded runtime archive contains an invalid path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(
                    destinationPath);
                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)!);

            entry.ExtractToFile(
                destinationPath,
                overwrite: true);
        }
    }

    /// <summary>
    /// Builds the stable descriptor used by runtime discovery and tool integration.
    /// </summary>
    private static EmbeddedRuntimeState BuildState(
        string rootDirectory,
        string version)
        => new(
            rootDirectory,
            Path.Combine(
                rootDirectory,
                "v2",
                "AutoHotkey64.exe"),
            Path.Combine(
                rootDirectory,
                "UX"),
            version);

    /// <summary>
    /// Removes old bundled runtime versions after the current version is ready.
    /// </summary>
    private static void CleanupOldVersions(
        string baseDirectory,
        string currentDirectory)
    {
        if (!Directory.Exists(baseDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     baseDirectory))
        {
            if (directory.Equals(
                    currentDirectory,
                    StringComparison.OrdinalIgnoreCase)
                || directory.Contains(
                    ".staging-",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
            catch
            {
                // Old runtime cleanup is best-effort and must not block startup.
            }
        }
    }

    /// <summary>
    /// Reads one embedded resource into memory so it can be hashed and extracted deterministically.
    /// </summary>
    private static byte[] ReadResource(
        string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var resource = assembly.GetManifestResourceStream(
            resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' is missing.");

        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// Reads runtime version and payload hashes generated during the build.
    /// </summary>
    private static RuntimeManifest ReadManifest()
    {
        var bytes = ReadResource(
            ManifestResourceName);

        return JsonSerializer.Deserialize<RuntimeManifest>(
                   bytes,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidDataException(
                   "The embedded AutoHotkey runtime manifest is invalid.");
    }

    /// <summary>
    /// Validates an embedded payload against the build-time manifest.
    /// </summary>
    private static void ValidateHash(
        byte[] bytes,
        string expectedHash,
        string label)
    {
        var actualHash = Convert.ToHexString(
            SHA256.HashData(bytes));

        if (!actualHash.Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{label} SHA-256 mismatch.");
        }
    }

    private sealed record RuntimeManifest(
        string Version,
        string RuntimeSha256,
        string UxSha256,
        string SourceUrl,
        string SourceTag);
}
