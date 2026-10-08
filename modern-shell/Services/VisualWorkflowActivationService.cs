using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Coordinates an explicit visual-workflow replacement through existing startup and execution owners.</summary>
internal sealed class VisualWorkflowActivationService(ScriptStartupService startup, ScriptExecutionService execution, ShortcutPreferencesService preferences)
{
    /// <summary>Offers replacement only for an intact v3 pair whose known hotkey overlaps a live or enabled built-in.</summary>
    internal bool CanReplaceBuiltIn(string path)
    {
        if (!File.Exists(path + ".flow.json") || startup.IsExplorerScript(path)) return false;
        try
        {
            var flow = new VisualFlowStore().Open(path).Document;
            return flow.SchemaVersion == 3 && VisualHotkeyConflicts.BuiltIn(flow.Trigger, preferences.Saved, BuiltInRunning() || startup.ExplorerEnabled) is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        { ServiceDiagnostics.Write("VisualFlow", "Replacement information unavailable: " + path, ex); return false; }
    }

    /// <summary>Stops the owned built-in only after explicit consent, then confirms startup and compensates every failed commit.</summary>
    internal RunningScriptSession ReplaceBuiltIn(string path)
    {
        lock (startup.ExplorerMutationLock)
        lock (VisualHotkeyConflicts.MutationGate)
        {
            using var claim = VisualFlowStore.ClaimForExecution(path);
            var opened = new VisualFlowStore().Open(path, claimHeld: true);
            RequireStoppedCandidate(path, execution.Snapshot());
            var enabled = startup.ExplorerEnabled; var running = BuiltInRunning(); var login = startup.IsRunAtSignIn(path);
            if (opened.Document.SchemaVersion != 3 || VisualHotkeyConflicts.BuiltIn(opened.Document.Trigger, preferences.Saved, enabled || running) is null)
                throw new InvalidOperationException("The built-in conflict changed. Review the shortcut again before replacing it.");
            RunningScriptSession? attempted = null;
            return Replace(
                () => startup.SetExplorerEnabled(false),
                () =>
                {
                    var result = attempted = execution.Run(path, workflowClaimHeld: true, requireWorkflowReady: true);
                    if (result.State == ScriptState.Failed) throw new InvalidOperationException(result.Error);
                    return execution.WaitForWorkflowReady(result, opened.Document.Id);
                },
                () => { startup.SetRunAtSignIn(path, login || enabled); ServiceDiagnostics.Write("VisualFlow", "Explicitly replaced built-in shortcuts with " + path); },
                () => { if (attempted?.ProcessId is not null) execution.StopAttempt(attempted); },
                () => startup.SetRunAtSignIn(path, login),
                () =>
                {
                    startup.SetExplorerEnabled(enabled);
                    if (!enabled && running)
                    {
                        var restored = execution.Run(startup.ExplorerScriptPath);
                        if (restored.State == ScriptState.Failed) throw new InvalidOperationException(restored.Error);
                    }
                });
        }
    }

    /// <summary>Preserves a currently running source snapshot; replacement requires an explicit prior Stop.</summary>
    internal static void RequireStoppedCandidate(string path, IReadOnlyList<RunningScriptSession> sessions)
    {
        if (sessions.Any(session => session.ScriptPath.Equals(path, StringComparison.OrdinalIgnoreCase) && session.State is ScriptState.Starting or ScriptState.Running or ScriptState.Stopping))
            throw new InvalidOperationException("Stop this workflow explicitly before replacing the built-in. Its current session was preserved.");
    }

    /// <summary>Reads only the built-in session that this manager already owns.</summary>
    private bool BuiltInRunning() => execution.Snapshot().Any(session => startup.IsExplorerScript(session.ScriptPath) && session.State == ScriptState.Running);

    /// <summary>Commits startup after ready acknowledgement and restores metadata/process ownership in reverse order on failure.</summary>
    internal static RunningScriptSession Replace(Action disable, Func<RunningScriptSession> launchAndConfirm, Action commitStartup,
        Action stopAttempt, Action restoreStartup, Action restoreBuiltIn)
    {
        try { disable(); var result = launchAndConfirm(); commitStartup(); return result; }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure }; var stopped = false;
            try { stopAttempt(); stopped = true; } catch (Exception ex) { errors.Add(ex); }
            try { restoreStartup(); } catch (Exception ex) { errors.Add(ex); }
            if (stopped) { try { restoreBuiltIn(); } catch (Exception ex) { errors.Add(ex); } }
            if (errors.Count > 1) throw new AggregateException("Replacement failed and recovery needs attention. The existing settings and process states are recorded in diagnostics.", errors);
            throw new InvalidOperationException("Replacement failed; the previous built-in and startup choices were restored. " + failure.Message, failure);
        }
    }
}
