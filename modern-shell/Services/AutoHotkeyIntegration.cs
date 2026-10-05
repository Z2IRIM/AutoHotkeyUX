using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace AutoHotkeyUX.Modern;

/// <summary>
/// Provides the Windows and AutoHotkey integration required by the modern shell.
/// </summary>
internal sealed class AutoHotkeyIntegration
{
    private const string AutoHotkeyKey = @"Software\AutoHotkey";
    private readonly string _uxDirectory;

    /// <summary>
    /// Captures the UX directory so existing AutoHotkey scripts and tools can be reused.
    /// </summary>
    public AutoHotkeyIntegration()
    {
        _uxDirectory = ResolveUxDirectory();
    }

    public string UxDirectory => _uxDirectory;

    /// <summary>
    /// Resolves the installed AutoHotkey root from the same registry locations used by the legacy UX.
    /// </summary>
    public string? GetInstallDirectory()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(AutoHotkeyKey);
            var value = key?.GetValue("InstallDir") as string;
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the preferred AutoHotkey v2 executable in an installed AutoHotkey tree.
    /// </summary>
    public string? GetAutoHotkeyExecutable()
    {
        var root = GetInstallDirectory();
        if (root is null)
        {
            return FindFromPath("AutoHotkey.exe");
        }

        var candidates = Environment.Is64BitOperatingSystem
            ? new[]
            {
                Path.Combine(root, "v2", "AutoHotkey64.exe"),
                Path.Combine(root, "v2", "AutoHotkey.exe"),
                Path.Combine(root, "AutoHotkey.exe")
            }
            : new[]
            {
                Path.Combine(root, "v2", "AutoHotkey32.exe"),
                Path.Combine(root, "v2", "AutoHotkey.exe"),
                Path.Combine(root, "AutoHotkey.exe")
            };

        return candidates.FirstOrDefault(File.Exists) ?? FindFromPath("AutoHotkey.exe");
    }

    /// <summary>
    /// Returns a concise runtime description for the Home page.
    /// </summary>
    public string GetRuntimeDescription()
    {
        var exe = GetAutoHotkeyExecutable();
        if (exe is null)
        {
            return "AutoHotkey runtime was not detected.";
        }

        var version = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "v2";
        var architecture = exe.Contains("64", StringComparison.OrdinalIgnoreCase) ? "64-bit"
            : exe.Contains("32", StringComparison.OrdinalIgnoreCase) ? "32-bit"
            : Environment.Is64BitProcess ? "64-bit" : "32-bit";

        return $"AutoHotkey {version} · {architecture} · {exe}";
    }

    /// <summary>
    /// Opens Window Spy using the installed copy first and the repository script as a fallback.
    /// </summary>
    public void OpenWindowSpy()
    {
        var documentsCopy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoHotkey",
            "WindowSpy.ahk");

        if (File.Exists(documentsCopy))
        {
            Process.Start(new ProcessStartInfo(documentsCopy) { UseShellExecute = true });
            return;
        }

        RunAhkScript(Path.Combine(_uxDirectory, "WindowSpy.ahk"));
    }

    /// <summary>
    /// Opens Ahk2Exe or starts the existing AutoHotkeyUX compiler installer when absent.
    /// </summary>
    public void OpenCompiler()
    {
        var installRoot = GetInstallDirectory();
        var compiler = installRoot is null ? null : Path.Combine(installRoot, "Compiler", "Ahk2Exe.exe");
        if (compiler is not null && File.Exists(compiler))
        {
            Process.Start(new ProcessStartInfo(compiler) { UseShellExecute = true });
            return;
        }

        RunAhkScript(Path.Combine(_uxDirectory, "install-ahk2exe.ahk"));
    }

    /// <summary>
    /// Opens the local v2 help file when installed, otherwise the official online documentation.
    /// </summary>
    public void OpenDocumentation()
    {
        var help = GetInstallDirectory() is { } root
            ? Path.Combine(root, "v2", "AutoHotkey.chm")
            : null;

        if (help is not null && File.Exists(help))
        {
            Process.Start(new ProcessStartInfo("hh.exe", $"\"{help}\"") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("https://www.autohotkey.com/docs/v2/") { UseShellExecute = true });
    }

    /// <summary>
    /// Creates a UTF-8 AutoHotkey script without overwriting an existing file.
    /// </summary>
    public string CreateScript(string directory, string requestedName, string content)
    {
        Directory.CreateDirectory(directory);

        var safeName = SanitizeFileName(requestedName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Untitled";
        }

        if (safeName.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
        {
            safeName = safeName[..^4];
        }

        var path = Path.Combine(directory, safeName + ".ahk");
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{safeName}-{index++}.ahk");
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Runs an existing AutoHotkey UX script through the detected v2 interpreter.
    /// </summary>
    private void RunAhkScript(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The AutoHotkey UX script could not be found.", scriptPath);
        }

        var exe = GetAutoHotkeyExecutable()
            ?? throw new InvalidOperationException("AutoHotkey v2 is not installed or could not be detected.");

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"\"{scriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Resolves the UX folder for installed, repository and published layouts.
    /// </summary>
    private string ResolveUxDirectory()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            GetInstallDirectory() is { } root ? Path.Combine(root, "UX") : string.Empty
        };

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, "WindowSpy.ahk")))
            ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Removes Windows-invalid filename characters from a requested script name.
    /// </summary>
    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    /// <summary>
    /// Searches PATH for an executable without launching a shell.
    /// </summary>
    private static string? FindFromPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue searching.
            }
        }

        return null;
    }
}

/// <summary>
/// Reads and writes only the existing AutoHotkey registry-backed UX settings.
/// </summary>
internal sealed class AutoHotkeySettings
{
    private const string BaseKey = @"Software\AutoHotkey";

    /// <summary>
    /// Reads a string setting using the same HKCU hierarchy as inc/config.ahk.
    /// </summary>
    public string Read(string section, string name, string defaultValue = "")
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path.Combine(BaseKey, section));
        return key?.GetValue(name)?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Writes a string setting using the same HKCU hierarchy as inc/config.ahk.
    /// </summary>
    public void Write(string section, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path.Combine(BaseKey, section));
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <summary>
    /// Reads a DWORD-style boolean setting while accepting legacy string values.
    /// </summary>
    public bool ReadBoolean(string section, string name, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path.Combine(BaseKey, section));
        var value = key?.GetValue(name);
        return value switch
        {
            int number => number != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            string text when int.TryParse(text, out var number) => number != 0,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Writes a registry DWORD boolean compatible with existing AutoHotkey UX reads.
    /// </summary>
    public void WriteBoolean(string section, string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path.Combine(BaseKey, section));
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Reads the current Edit Script shell command from the effective file association.
    /// </summary>
    public string ReadEditorCommand()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"AutoHotkeyScript\shell\edit\command");
        return key?.GetValue(null)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Writes the per-user Edit Script shell command without requiring elevation.
    /// </summary>
    public void WriteEditorCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\AutoHotkeyScript\shell\edit\command");
        key.SetValue(null, command, RegistryValueKind.String);
    }
}
