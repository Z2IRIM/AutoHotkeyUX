using AutoHotkeyUX.Modern.Models;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Stores script startup choices and safely updates the editable built-in through its dedicated installer.</summary>
internal sealed class ScriptStartupService
{
    private readonly ExplorerShortcutInstaller _installer;
    private readonly AutoHotkeySettings _settings;
    private readonly ScriptExecutionService _execution;
    private readonly object _gate = new();
    internal object ExplorerMutationLock => _gate;
    internal string ExplorerScriptPath { get; }
    internal event EventHandler? Changed;
    internal string? LastWarning { get; private set; }

    /// <summary>Uses the same catalog root and the existing registry-backed user settings.</summary>
    internal ScriptStartupService(string root, AutoHotkeySettings settings, ScriptExecutionService execution)
    {
        ExplorerScriptPath = Path.Combine(root, "Explorer Shortcuts.ahk");
        _installer = new ExplorerShortcutInstaller(ExplorerScriptPath);
        _settings = settings;
        _execution = execution;
    }

    internal bool ExplorerEnabled => _settings.ReadBoolean("Modern", "ExplorerShortcuts", false);

    /// <summary>Identifies the reserved built-in script path using Windows path comparison.</summary>
    internal bool IsExplorerScript(string path) => ExplorerScriptPath.Equals(path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports the startup choice; the built-in shortcut choice is controlled by its settings toggle.</summary>
    internal bool IsRunAtSignIn(string path) => IsExplorerScript(path) ? ExplorerEnabled : ReadSelected().Contains(path);

    /// <summary>Changes metadata only; selecting startup does not start an arbitrary user script immediately.</summary>
    internal void SetRunAtSignIn(string path, bool enabled)
    {
        if (IsExplorerScript(path)) throw new InvalidOperationException("Use Explorer shortcuts in Settings.");
        lock (_gate)
        {
            var selected = ReadSelected();
            if (enabled) selected.Add(Path.GetFullPath(path)); else selected.Remove(Path.GetFullPath(path));
            _settings.Write("Modern", "StartupScripts", JsonSerializer.Serialize(selected.Order(StringComparer.OrdinalIgnoreCase)));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts chosen scripts after session recovery, reusing duplicate-run prevention.</summary>
    internal void StartSelected()
    {
        LastWarning = null;
        if (ExplorerEnabled)
        {
            try { StartExplorer(); }
            catch (Exception ex) { RecordStartupFailure(ExplorerScriptPath, ex); }
        }
        foreach (var path in ReadSelected())
        {
            if (IsExplorerScript(path)) continue;
            try { StartChecked(path); }
            catch (Exception ex) { RecordStartupFailure(path, ex); }
        }
    }

    /// <summary>Isolates each startup failure so a moved/deleted script cannot prevent the manager or other scripts from starting.</summary>
    private void RecordStartupFailure(string path, Exception exception)
    {
        LastWarning = $"Could not start {Path.GetFileName(path)}: {exception.Message}";
        ServiceDiagnostics.Write("Startup", LastWarning, exception);
    }

    /// <summary>Serializes built-in enable/stop with preference transactions and keeps the previous enable choice on failure.</summary>
    internal void SetExplorerEnabled(bool enabled)
    {
        lock (_gate)
        {
            var previous = ExplorerEnabled;
            try
            {
                if (enabled) StartExplorer();
                else _execution.Stop(ExplorerScriptPath);
                _settings.WriteBoolean("Modern", "ExplorerShortcuts", enabled);
            }
            catch
            {
                _settings.WriteBoolean("Modern", "ExplorerShortcuts", previous);
                throw;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Surfaces a failed interpreter start rather than treating an enabled preference as running proof.</summary>
    private void StartChecked(string path)
    {
        var session = _execution.Run(path);
        if (session.State == ScriptState.Failed) throw new InvalidOperationException(session.Error);
    }

    /// <summary>Restarts only an owned upgraded predecessor; custom scripts retain their existing process.</summary>
    private void StartExplorer()
    {
        var upgraded = _installer.Ensure();
        LastWarning = _installer.Warning;
        var session = upgraded ? _execution.Restart(ExplorerScriptPath) : _execution.Run(ExplorerScriptPath);
        if (session.State == ScriptState.Failed) throw new InvalidOperationException(session.Error);
    }

    /// <summary>Reads bounded path metadata and retains corruption diagnostics without storing script contents.</summary>
    private HashSet<string> ReadSelected()
    {
        try
        {
            var json = _settings.Read("Modern", "StartupScripts", "[]");
            if (json.Length > 1024 * 1024) throw new InvalidDataException("Startup script metadata exceeds 1 MiB.");
            return new HashSet<string>((JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
        {
            ServiceDiagnostics.Write("Startup", "Invalid startup script metadata; automatic user script launches were skipped.", ex);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
