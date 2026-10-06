using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>
/// Provides Windows and AutoHotkey actions used by the WinUI 3 presentation layer.
/// </summary>
internal sealed class AutoHotkeyIntegration
{
    private readonly AutoHotkeyRuntimeLocator _runtimeLocator;

    /// <summary>
    /// Stores the shared runtime locator so all actions use the same discovery rules.
    /// </summary>
    internal AutoHotkeyIntegration(
        AutoHotkeyRuntimeLocator runtimeLocator)
    {
        _runtimeLocator = runtimeLocator;
    }

    /// <summary>
    /// Returns the currently preferred AutoHotkey runtime.
    /// </summary>
    internal AutoHotkeyRuntimeInfo? FindRuntime(
        string preferredBuild = "")
        => _runtimeLocator.FindPreferred(preferredBuild);

    /// <summary>
    /// Reports whether the effective .ahk open command currently uses launcher.ahk.
    /// </summary>
    internal bool IsLauncherEnabled()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(
                @"AutoHotkeyScript\shell\open\command");
            var command =
                key?.GetValue(null)?.ToString()
                ?? string.Empty;

            return command.Contains(
                "launcher.ahk",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens Window Spy using the user's copy first and the installed UX copy as fallback.
    /// </summary>
    internal void OpenWindowSpy()
    {
        var documentsCopy = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "AutoHotkey",
            "WindowSpy.ahk");

        if (File.Exists(documentsCopy))
        {
            RunAhkScript(documentsCopy);
            return;
        }

        RunAhkScript(
            Path.Combine(
                ResolveUxDirectory(),
                "WindowSpy.ahk"));
    }

    /// <summary>
    /// Opens Ahk2Exe or starts the existing AutoHotkeyUX compiler installer when needed.
    /// </summary>
    internal void OpenCompiler()
    {
        var installRoot =
            _runtimeLocator.FindInstallDirectory();

        var compiler = installRoot is null
            ? null
            : Path.Combine(
                installRoot,
                "Compiler",
                "Ahk2Exe.exe");

        if (compiler is not null
            && File.Exists(compiler))
        {
            Process.Start(
                new ProcessStartInfo(compiler)
                {
                    UseShellExecute = true
                });
            return;
        }

        RunAhkScript(
            Path.Combine(
                ResolveUxDirectory(),
                "install-ahk2exe.ahk"));
    }

    /// <summary>
    /// Opens local v2 help when available and otherwise the official web documentation.
    /// </summary>
    internal void OpenDocumentation()
    {
        var installRoot =
            _runtimeLocator.FindInstallDirectory();

        var help = installRoot is null
            ? null
            : Path.Combine(
                installRoot,
                "v2",
                "AutoHotkey.chm");

        if (help is not null
            && File.Exists(help))
        {
            Process.Start(
                new ProcessStartInfo(
                    "hh.exe",
                    $"\"{help}\"")
                {
                    UseShellExecute = true
                });
            return;
        }

        Process.Start(
            new ProcessStartInfo(
                "https://www.autohotkey.com/docs/v2/")
            {
                UseShellExecute = true
            });
    }

    /// <summary>
    /// Creates a UTF-8 AutoHotkey script without overwriting an existing file.
    /// </summary>
    internal string CreateScript(
        string directory,
        string requestedName,
        string content)
    {
        Directory.CreateDirectory(directory);

        var safeName =
            SanitizeFileName(requestedName);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Untitled";
        }

        if (safeName.EndsWith(
                ".ahk",
                StringComparison.OrdinalIgnoreCase))
        {
            safeName = safeName[..^4];
        }

        var path =
            Path.Combine(
                directory,
                safeName + ".ahk");

        var index = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(
                directory,
                $"{safeName}-{index++}.ahk");
        }

        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(false));

        return path;
    }

    /// <summary>
    /// Reveals a created script in File Explorer.
    /// </summary>
    internal void RevealFile(string path)
    {
        Process.Start(
            new ProcessStartInfo(
                "explorer.exe",
                $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
    }

    /// <summary>Runs the configured Edit Script command without changing file associations or editor settings.</summary>
    internal void EditScript(string path, AutoHotkeySettings settings)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The script no longer exists.", path);
        var command = settings.ReadEditorCommand();
        if (string.IsNullOrWhiteSpace(command))
        {
            var fallback = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"))
            { UseShellExecute = false };
            fallback.ArgumentList.Add(path);
            Process.Start(fallback);
            return;
        }
        var arguments = WindowsCommandLine.Split(Environment.ExpandEnvironmentVariables(command));
        if (arguments.Length == 0) throw new InvalidOperationException("The configured editor command is invalid.");
        var start = new ProcessStartInfo(arguments[0]) { UseShellExecute = false };
        var substituted = false;
        foreach (var argument in arguments.Skip(1))
        {
            substituted |= argument.Contains("%1", StringComparison.Ordinal) || argument.Contains("%L", StringComparison.OrdinalIgnoreCase);
            start.ArgumentList.Add(argument.Replace("%1", path, StringComparison.Ordinal).Replace("%L", path, StringComparison.OrdinalIgnoreCase));
        }
        if (!substituted) start.ArgumentList.Add(path);
        Process.Start(start);
    }

    /// <summary>Creates and opens the shared workspace using Explorer, independently of .ahk associations.</summary>
    internal void OpenScriptsFolder(string directory)
    {
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(directory);
        Process.Start(start);
    }

    /// <summary>
    /// Applies the launcher or a concrete v2 runtime to the per-user .ahk shell association.
    /// </summary>
    internal void SetLauncherMode(
        bool useLauncher,
        string preferredBuild)
    {
        const string shellKey =
            @"Software\Classes\AutoHotkeyScript\Shell";

        string command;
        string friendlyName;

        if (useLauncher)
        {
            var runtime =
                FindRuntime()
                ?? throw new InvalidOperationException(
                    "AutoHotkey v2 is not installed or could not be detected.");

            var launcher =
                Path.Combine(
                    ResolveUxDirectory(),
                    "launcher.ahk");

            if (!File.Exists(launcher))
            {
                throw new FileNotFoundException(
                    "The AutoHotkey launcher script could not be found.",
                    launcher);
            }

            command =
                $"\"{runtime.Path}\" \"{launcher}\" \"%1\" %*";

            friendlyName =
                "AutoHotkey Launcher";
        }
        else
        {
            var runtime =
                FindRuntime(preferredBuild)
                ?? throw new InvalidOperationException(
                    "No suitable AutoHotkey v2 interpreter was found.");

            command =
                $"\"{runtime.Path}\" \"%1\" %*";

            friendlyName =
                FileVersionInfo
                    .GetVersionInfo(runtime.Path)
                    .FileDescription
                ?? "AutoHotkey";
        }

        using var shell =
            Registry.CurrentUser.CreateSubKey(
                shellKey);

        using var open =
            shell.CreateSubKey("Open");

        open.SetValue(
            "FriendlyAppName",
            friendlyName,
            RegistryValueKind.String);

        using var openCommand =
            open.CreateSubKey("Command");

        openCommand.SetValue(
            null,
            command,
            RegistryValueKind.String);

        using var runAsCommand =
            shell.CreateSubKey(
                @"RunAs\Command");

        runAsCommand.SetValue(
            null,
            command,
            RegistryValueKind.String);
    }

    /// <summary>
    /// Runs an existing AutoHotkeyUX script with the discovered v2 runtime.
    /// </summary>
    private void RunAhkScript(
        string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "The AutoHotkey UX script could not be found.",
                scriptPath);
        }

        var runtime =
            FindRuntime()
            ?? throw new InvalidOperationException(
                "AutoHotkey v2 is not installed or could not be detected.");

        Process.Start(
            new ProcessStartInfo
            {
                FileName = runtime.Path,
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory =
                    Path.GetDirectoryName(
                        scriptPath)!,
                UseShellExecute = true
            });
    }

    /// <summary>
    /// Resolves the AutoHotkeyUX directory for installed and repository development layouts.
    /// </summary>
    private string ResolveUxDirectory()
    {
        var installRoot =
            _runtimeLocator.FindInstallDirectory();

        if (!string.IsNullOrWhiteSpace(
                installRoot))
        {
            var installedUx =
                Path.Combine(
                    installRoot,
                    "UX");

            if (File.Exists(
                    Path.Combine(
                        installedUx,
                        "WindowSpy.ahk")))
            {
                return installedUx;
            }
        }

        var current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        for (var depth = 0;
             current is not null && depth < 8;
             depth++)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "WindowSpy.ahk")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "AutoHotkeyUX scripts could not be located.");
    }

    /// <summary>
    /// Removes Windows-invalid filename characters from a requested script name.
    /// </summary>
    private static string SanitizeFileName(
        string value)
    {
        var invalid =
            Path.GetInvalidFileNameChars();

        return new string(
                value
                    .Where(c => !invalid.Contains(c))
                    .ToArray())
            .Trim();
    }
}
