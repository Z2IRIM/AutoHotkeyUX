using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns one immutable saved snapshot and serializes live updates with built-in startup changes.</summary>
internal sealed class ShortcutPreferencesService
{
    private readonly AutoHotkeySettings _settings;
    private readonly ScriptStartupService _startup;
    private readonly ScriptExecutionService _execution;
    private readonly ShortcutPreferencesRuntime _runtime;
    private ShortcutPreferenceSnapshot _snapshot;
    private readonly object _lifecycle = new();
    private Task _pending = Task.CompletedTask;
    private bool _accepting = true;
    internal string? Warning { get; private set; }
    internal ShortcutPreferences Saved => Volatile.Read(ref _snapshot).Preferences;
    internal event EventHandler? Changed;

    /// <summary>Loads preferences once and keeps the native sender tied to the managed built-in identity.</summary>
    internal ShortcutPreferencesService(AutoHotkeySettings settings, ScriptStartupService startup, ScriptExecutionService execution)
    {
        _settings = settings; _startup = startup; _execution = execution;
        _runtime = new(settings, FindLiveSession);
        _snapshot = ReadSaved();
    }

    /// <summary>Validates off the UI thread, then commits one revision or retains the saved snapshot on rejection.</summary>
    internal Task SaveAsync(ShortcutPreferences preferences)
    {
        lock (_lifecycle)
        {
            if (!_accepting) throw new InvalidOperationException("The manager is closing; no new preference updates are accepted.");
            if (!_pending.IsCompleted) throw new InvalidOperationException("A preference update is already in progress.");
            return _pending = Task.Run(() =>
            {
                lock (_startup.ExplorerMutationLock)
                {
                    var normalized = preferences with { ArchiveFolder = preferences.ArchiveFolder.Trim() };
                    var errors = ShortcutPreferenceCodec.Validate(normalized, true);
                    if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors.Values));
                    if (normalized == Saved && Warning is null) return;
                    var snapshot = new ShortcutPreferenceSnapshot(Guid.NewGuid().ToString("N"), normalized);
                    try
                    {
                        if (FindLiveSession() is not null || _startup.ExplorerEnabled) _runtime.Apply(snapshot);
                        else _settings.Write("Modern", ShortcutPreferenceCodec.SettingName, ShortcutPreferenceCodec.Encode(snapshot));
                    }
                    catch (UnconfirmedShortcutPreferencesException)
                    {
                        Volatile.Write(ref _snapshot, ReadSaved());
                        Changed?.Invoke(this, EventArgs.Empty);
                        throw;
                    }
                    Volatile.Write(ref _snapshot, snapshot);
                    Warning = null;
                    ServiceDiagnostics.Write("Shortcut", $"Saved preferences revision {snapshot.Revision}; terminal={normalized.TerminalEnabled}; archive={normalized.ArchiveEnabled}");
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }

    /// <summary>Stops new updates and observes the accepted transaction before services or native windows are released.</summary>
    internal async Task DrainAsync()
    {
        Task pending;
        lock (_lifecycle) { _accepting = false; pending = _pending; }
        try { await pending; }
        catch (Exception ex) { ServiceDiagnostics.Write("Shortcut", "The last preference update failed before shutdown.", ex); }
    }

    /// <summary>Returns only the process managed at the reserved built-in path.</summary>
    private RunningScriptSession? FindLiveSession() => _execution.Snapshot().FirstOrDefault(item =>
        _startup.IsExplorerScript(item.ScriptPath) && item.State == ScriptState.Running);

    /// <summary>Falls back visibly on corrupt storage without rewriting the user's registry value.</summary>
    private ShortcutPreferenceSnapshot ReadSaved()
    {
        try { return ShortcutPreferenceCodec.Decode(_settings.Read("Modern", ShortcutPreferenceCodec.SettingName)); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Warning = "Saved shortcut preferences are invalid. Defaults are shown; save to replace the invalid snapshot.";
            ServiceDiagnostics.Write("Shortcut", Warning, ex);
            return ShortcutPreferenceSnapshot.Default;
        }
    }
}
