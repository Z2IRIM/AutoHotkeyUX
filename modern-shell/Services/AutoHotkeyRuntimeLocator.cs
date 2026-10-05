using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

internal sealed record AutoHotkeyRuntimeInfo(
    string Path,
    string Version,
    string Architecture,
    string DiscoverySource,
    string? InstallDirectory);

/// <summary>
/// Locates AutoHotkey v2 across registry, shell associations, App Paths, Program Files and PATH.
/// </summary>
internal sealed class AutoHotkeyRuntimeLocator
{
    private static readonly string[] RuntimeFileNames =
    [
        "AutoHotkey64.exe",
        "AutoHotkey.exe",
        "AutoHotkey32.exe",
        "AutoHotkeyUX.exe"
    ];

    /// <summary>
    /// Returns the first usable v2 runtime, honoring an optional architecture preference.
    /// </summary>
    internal AutoHotkeyRuntimeInfo? FindPreferred(string preferredBuild = "")
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates(preferredBuild))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(candidate.Path));
            }
            catch
            {
                continue;
            }

            if (!seen.Add(fullPath) || !File.Exists(fullPath))
            {
                continue;
            }

            if (!LooksLikeAutoHotkeyRuntime(fullPath))
            {
                continue;
            }

            var info = BuildRuntimeInfo(fullPath, candidate.Source);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns an installed AutoHotkey root even when the runtime itself cannot be resolved.
    /// </summary>
    internal string? FindInstallDirectory()
    {
        var runtime = FindPreferred();
        if (!string.IsNullOrWhiteSpace(runtime?.InstallDirectory))
        {
            return runtime.InstallDirectory;
        }

        return ReadInstallDirectories().FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Produces ordered candidates from all supported Windows discovery sources.
    /// </summary>
    private IEnumerable<RuntimeCandidate> EnumerateCandidates(string preferredBuild)
    {
        foreach (var root in ReadInstallDirectories())
        {
            foreach (var candidate in ExpandInstallRoot(
                         root,
                         "AutoHotkey InstallDir",
                         preferredBuild))
            {
                yield return candidate;
            }
        }

        foreach (var path in ReadShellAssociationExecutables())
        {
            yield return new RuntimeCandidate(
                path,
                "AHK file association");
        }

        foreach (var path in ReadAppPaths())
        {
            yield return new RuntimeCandidate(
                path,
                "Windows App Paths");
        }

        foreach (var root in ReadUninstallInstallLocations())
        {
            foreach (var candidate in ExpandInstallRoot(
                         root,
                         "Uninstall registry",
                         preferredBuild))
            {
                yield return candidate;
            }
        }

        foreach (var root in GetKnownInstallRoots())
        {
            foreach (var candidate in ExpandInstallRoot(
                         root,
                         "Known install location",
                         preferredBuild))
            {
                yield return candidate;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
            {
                foreach (var fileName in OrderFileNames(preferredBuild))
                {
                    yield return new RuntimeCandidate(
                        Path.Combine(directory, fileName),
                        "PATH");
                }
            }
        }
    }

    /// <summary>
    /// Expands an AutoHotkey installation root into common v2 executable locations.
    /// </summary>
    private static IEnumerable<RuntimeCandidate> ExpandInstallRoot(
        string root,
        string source,
        string preferredBuild)
    {
        foreach (var fileName in OrderFileNames(preferredBuild))
        {
            yield return new RuntimeCandidate(
                Path.Combine(root, "v2", fileName),
                source);

            yield return new RuntimeCandidate(
                Path.Combine(root, fileName),
                source);
        }

        // AutoHotkey v2's installer gives AutoHotkeyUX its own interpreter copy.
        // The legacy UX and shell verbs may point to this executable directly.
        yield return new RuntimeCandidate(
            Path.Combine(root, "UX", "AutoHotkeyUX.exe"),
            source);
    }

    /// <summary>
    /// Orders runtime names so a requested architecture is evaluated first.
    /// </summary>
    private static IEnumerable<string> OrderFileNames(string preferredBuild)
    {
        if (preferredBuild == "32-bit")
        {
            yield return "AutoHotkey32.exe";
            yield return "AutoHotkey.exe";
            yield return "AutoHotkey64.exe";
            yield break;
        }

        if (preferredBuild == "64-bit")
        {
            yield return "AutoHotkey64.exe";
            yield return "AutoHotkey.exe";
            yield return "AutoHotkey32.exe";
            yield break;
        }

        foreach (var fileName in RuntimeFileNames)
        {
            yield return fileName;
        }
    }

    /// <summary>
    /// Reads AutoHotkey InstallDir from per-user and machine-wide registry hives.
    /// </summary>
    private static IEnumerable<string> ReadInstallDirectories()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            string? value = null;
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\AutoHotkey");
                value = key?.GetValue("InstallDir") as string;
            }
            catch
            {
                // Registry access can fail under constrained policies.
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    /// <summary>
    /// Extracts AutoHotkey executables from the effective .ahk Open command.
    /// </summary>
    private static IEnumerable<string> ReadShellAssociationExecutables()
    {
        string command = string.Empty;
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(
                @"AutoHotkeyScript\shell\open\command");
            command = key?.GetValue(null)?.ToString() ?? string.Empty;
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(
                     command,
                     "(?:\\\"(?<quoted>[^\\\"]+\\.exe)\\\"|(?<plain>[^\\s\\\"]+\\.exe))",
                     RegexOptions.IgnoreCase))
        {
            var path = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["plain"].Value;

            if (LooksLikeAutoHotkeyRuntime(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// Reads registered executable paths from the Windows App Paths mechanism.
    /// </summary>
    private static IEnumerable<string> ReadAppPaths()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var name in RuntimeFileNames)
            {
                string? value = null;
                try
                {
                    using var key = hive.OpenSubKey(
                        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                    value = key?.GetValue(null)?.ToString();
                }
                catch
                {
                    // Continue with remaining discovery sources.
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim('"');
                }
            }
        }
    }

    /// <summary>
    /// Reads the install location recorded by the official AutoHotkey uninstaller registration.
    /// </summary>
    private static IEnumerable<string> ReadUninstallInstallLocations()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            string? value = null;
            try
            {
                using var key = hive.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AutoHotkey");
                value = key?.GetValue("InstallLocation")?.ToString();
            }
            catch
            {
                // Continue with remaining discovery sources.
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim('"');
            }
        }
    }

    /// <summary>
    /// Returns official default install roots for all-user and per-user AutoHotkey installations.
    /// </summary>
    private static IEnumerable<string> GetKnownInstallRoots()
    {
        foreach (var root in new[]
                 {
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         "AutoHotkey"),
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         "AutoHotkey"),
                     Path.Combine(
                         Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
                         "AutoHotkey"),
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs",
                         "AutoHotkey")
                 })
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return root;
            }
        }
    }

    /// <summary>
    /// Builds a stable runtime descriptor and rejects explicit v1 executables.
    /// </summary>
    private static AutoHotkeyRuntimeInfo? BuildRuntimeInfo(
        string path,
        string source)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var version =
                versionInfo.FileVersion
                ?? versionInfo.ProductVersion
                ?? "v2";

            var normalizedVersion = version
                .Split(' ', '+')[0];

            if (Version.TryParse(normalizedVersion, out var parsed)
                && parsed.Major < 2)
            {
                return null;
            }

            return new AutoHotkeyRuntimeInfo(
                path,
                version,
                GetExecutableArchitecture(path),
                source,
                InferInstallDirectory(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Filters shell commands so UX executables are not mistaken for the interpreter.
    /// </summary>
    private static bool LooksLikeAutoHotkeyRuntime(string path)
    {
        var name = Path.GetFileName(path);
        return RuntimeFileNames.Contains(
            name,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the PE machine type so AutoHotkeyUX.exe can report its real architecture.
    /// </summary>
    private static string GetExecutableArchitecture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();

            stream.Position = peOffset + 4;
            var machine = reader.ReadUInt16();

            return machine switch
            {
                0x014c => "32-bit",
                0x8664 => "64-bit",
                0xAA64 => "ARM64",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Infers the AutoHotkey installation root from a located runtime path.
    /// </summary>
    private static string? InferInstallDirectory(string runtimePath)
    {
        var directory = Directory.GetParent(runtimePath);
        if (directory is null)
        {
            return null;
        }

        if (directory.Name.Equals(
                "v2",
                StringComparison.OrdinalIgnoreCase)
            || directory.Name.Equals(
                "UX",
                StringComparison.OrdinalIgnoreCase))
        {
            return directory.Parent?.FullName;
        }

        if (directory.Name.Equals(
                "AutoHotkey",
                StringComparison.OrdinalIgnoreCase))
        {
            return directory.FullName;
        }

        return null;
    }

    private sealed record RuntimeCandidate(
        string Path,
        string Source);
}
