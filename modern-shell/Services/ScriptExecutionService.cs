using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Serializes interpreter lifetime operations and never claims externally started scripts.</summary>
internal sealed class ScriptExecutionService : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<string?> _runtimePath;
    private readonly ScriptSessionStore _store;
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RunningScriptSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StringBuilder> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VisualExecutionSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private readonly Func<string, FlowTrigger?, string?>? _workflowConflict;
    private readonly string? _registryBase;
    private readonly string? _diagnosticLocalDataRoot;
    internal string? LastWarning { get; private set; }
    internal event EventHandler? Changed;

    /// <summary>Reuses the shell runtime locator through a provider without coupling process handling to XAML.</summary>
    internal ScriptExecutionService(Func<string?> runtimePath, ScriptSessionStore store, Func<string, FlowTrigger?, string?>? workflowConflict = null,
        string? registryBase = null, string? diagnosticLocalDataRoot = null)
    {
        _runtimePath = runtimePath;
        _store = store;
        _workflowConflict = workflowConflict;
        _registryBase = registryBase; _diagnosticLocalDataRoot = diagnosticLocalDataRoot;
    }

    /// <summary>Returns a stable snapshot while process-exit events can arrive on a worker thread.</summary>
    internal IReadOnlyList<RunningScriptSession> Snapshot()
    {
        lock (_gate) return _sessions.Values.ToArray();
    }

    /// <summary>Recovers only PID/start-time/runtime matches and discards stale or unverifiable metadata.</summary>
    internal void Rehydrate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var saved = _store.Load();
            LastWarning = _store.LastError;
            var runtime = saved.Count == 0 ? null : _runtimePath();
            foreach (var entry in saved)
            {
                Process? process = null;
                try
                {
                    var path = Path.GetFullPath(entry.ScriptPath);
                    if (!File.Exists(path) || _processes.ContainsKey(path) || runtime is null) continue;
                    process = Process.GetProcessById(entry.Pid);
                    _ = process.SafeHandle; // Pin the actual process object before validating its identity.
                    if (process.HasExited || process.StartTime.ToUniversalTime() != entry.ProcessStartUtc
                        || !string.Equals(process.MainModule?.FileName, runtime, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var known = entry.ShortcutSnapshotKnown && VisualExecutionSource.IsValidTrigger(entry.RegisteredTrigger);
                    if (known && entry.SnapshotPath is not null && entry.SnapshotSha256 is not null && entry.RegisteredTrigger is not null)
                    {
                        try { _sources[path] = VisualExecutionSource.Restore(entry.SnapshotPath, entry.SnapshotSha256, entry.RegisteredTrigger, _store.SnapshotDirectory); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                        { known = false; ServiceDiagnostics.Write("Execution", "Recovered visual snapshot unavailable; restart is needed for shortcut certainty.", ex); }
                    }
                    else if (entry.RegisteredTrigger is not null) known = false;
                    _sessions[path] = new(path, entry.Pid, entry.ProcessStartUtc, entry.ProcessStartUtc, ScriptState.Running,
                        RegisteredTrigger: entry.RegisteredTrigger, ShortcutSnapshotKnown: known, SnapshotPath: entry.SnapshotPath, SnapshotSha256: entry.SnapshotSha256);
                    Attach(path, process);
                    process = null; // The service now owns the handle.
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                    or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                {
                    if (process is not null)
                    {
                        if (_sources.Remove(entry.ScriptPath, out var rejectedSource)) rejectedSource.Release(delete: false);
                        _processes.Remove(entry.ScriptPath);
                        _sessions.Remove(entry.ScriptPath);
                        _errors.Remove(entry.ScriptPath);
                        process.Exited -= Process_Exited;
                    }
                    ServiceDiagnostics.Write("Execution", $"Rejected saved process identity for {entry.ScriptPath}.", ex);
                }
                finally { process?.Dispose(); }
            }
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Launches one owned interpreter per canonical path, refusing unfinished workflows through the shared save claim.</summary>
    internal RunningScriptSession Run(string scriptPath, bool workflowClaimHeld = false, VisualExecutionSource? executionSource = null, bool requireWorkflowReady = false)
    {
        var path = Path.GetFullPath(scriptPath);
        lock (VisualHotkeyConflicts.MutationGate)
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processes.TryGetValue(path, out var existing))
            {
                if (!existing.HasExited) { executionSource?.Release(delete: true); return _sessions[path]; }
                CompleteExit(path, existing);
            }
            _sessions[path] = new(path, null, null, DateTime.UtcNow, ScriptState.Starting);
            Changed?.Invoke(this, EventArgs.Empty);
            Process? process = null;
            VisualExecutionSource? captured = executionSource;
            try
            {
                using var workflowClaim = workflowClaimHeld ? null : VisualFlowStore.ClaimForExecution(path);
                captured ??= VisualExecutionSource.Capture(path, _store.SnapshotDirectory);
                if (requireWorkflowReady && captured?.SupportsReady != true) throw new InvalidOperationException("Built-in replacement requires an intact v3 visual workflow.");
                var conflict = _workflowConflict?.Invoke(path, captured?.Trigger);
                if (conflict is not null) throw new InvalidOperationException(conflict);
                if (!File.Exists(path)) throw new FileNotFoundException("The script no longer exists.", path);
                if (!Path.GetExtension(path).Equals(".ahk", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Select an AutoHotkey .ahk script.");
                var runtime = _runtimePath();
                if (string.IsNullOrWhiteSpace(runtime) || !File.Exists(runtime))
                    throw new FileNotFoundException("The built-in AutoHotkey v2 runtime is unavailable.", runtime);
                var start = new ProcessStartInfo(runtime)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(path)!,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("/ErrorStdOut=UTF-8");
                if (_registryBase is not null) start.Environment["AUTOHOTKEYUX_FLOW_KEY"] = @"HKCU\" + _registryBase + @"\Modern";
                if (_diagnosticLocalDataRoot is not null) start.Environment["LOCALAPPDATA"] = _diagnosticLocalDataRoot;
                start.ArgumentList.Add(captured?.Path ?? path);
                if (requireWorkflowReady) { start.ArgumentList.Add("--signal-ready"); start.ArgumentList.Add(captured!.ReadyPath); }
                process = new Process { StartInfo = start };
                if (!process.Start()) throw new InvalidOperationException("AutoHotkey did not start.");
                _ = process.SafeHandle;
                var started = process.StartTime.ToUniversalTime();
                _sessions[path] = new(path, process.Id, started, DateTime.UtcNow, ScriptState.Running, RegisteredTrigger: captured?.Trigger,
                    ShortcutSnapshotKnown: true, SnapshotPath: captured?.Path, SnapshotSha256: captured?.Hash);
                if (captured is not null) { _sources[path] = captured; captured = null; }
                Attach(path, process, readErrors: true);
                process = null;
                Persist();
                ServiceDiagnostics.Write("Execution", $"Run {path}: {_sessions[path].State}");
            }
            catch (Exception ex)
            {
                captured?.Release(delete: true);
                if (_sources.Remove(path, out var failedSource)) failedSource.Release(delete: true);
                // A failure after Start must not leak an untracked interpreter.
                if (process is not null)
                {
                    _processes.Remove(path);
                    _errors.Remove(path);
                    process.Exited -= Process_Exited;
                    process.ErrorDataReceived -= Process_ErrorDataReceived;
                    try { if (!process.HasExited) process.Kill(); }
                    catch (Exception cleanupError) when (cleanupError is InvalidOperationException or System.ComponentModel.Win32Exception)
                    { ServiceDiagnostics.Write("Execution", "Interpreter cleanup failed after an incomplete start.", cleanupError); }
                    process.Dispose();
                }
                _sessions[path] = new(path, null, null, DateTime.UtcNow, ScriptState.Failed, ex.Message);
                Persist();
                ServiceDiagnostics.Write("Execution", $"Run failed: {path}", ex);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return _sessions[path];
        }
    }

    /// <summary>Stops only the retained interpreter handle, preserving applications it started.</summary>
    internal void Stop(string scriptPath)
    {
        var path = Path.GetFullPath(scriptPath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_processes.TryGetValue(path, out var process)) return;
            if (process.HasExited) { CompleteExit(path, process); return; }
            var session = _sessions[path];
            if (process.StartTime.ToUniversalTime() != session.ProcessStartUtc)
                throw new InvalidOperationException("Process identity could not be confirmed; Stop was refused.");
            _sessions[path] = session with { State = ScriptState.Stopping, Error = null };
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                process.Kill(); // Intentionally do not kill the process tree.
                if (!process.WaitForExit(5000)) throw new TimeoutException("The script has not stopped after five seconds.");
                if (_processes.TryGetValue(path, out var current) && ReferenceEquals(current, process))
                    CompleteExit(path, process);
            }
            catch (Exception ex)
            {
                if (_processes.ContainsKey(path))
                    _sessions[path] = session with { State = ScriptState.Running, Error = ex.Message };
                ServiceDiagnostics.Write("Execution", $"Stop failed: {path}", ex);
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }
    }

    /// <summary>Stops only the exact process identity created by a failed activation attempt.</summary>
    internal void StopAttempt(RunningScriptSession attempted)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(attempted.ScriptPath, out var current) || current.ProcessId != attempted.ProcessId || current.ProcessStartUtc != attempted.ProcessStartUtc) return;
            Stop(attempted.ScriptPath);
        }
    }

    /// <summary>Claims visual source before stopping it, preserving a live interpreter when a save is unfinished or in flight.</summary>
    internal RunningScriptSession Restart(string scriptPath, bool workflowClaimHeld = false, bool requireWorkflowReady = false)
    {
        var path = Path.GetFullPath(scriptPath);
        lock (VisualHotkeyConflicts.MutationGate)
        lock (_gate)
        {
            using var workflowClaim = workflowClaimHeld ? null : VisualFlowStore.ClaimForExecution(path);
            var captured = VisualExecutionSource.Capture(path, _store.SnapshotDirectory);
            try
            {
                var conflict = _workflowConflict?.Invoke(path, captured?.Trigger);
                if (conflict is not null) throw new InvalidOperationException(conflict);
                Stop(path);
                var transferred = captured; captured = null;
                return Run(path, workflowClaimHeld: true, executionSource: transferred, requireWorkflowReady: requireWorkflowReady);
            }
            finally { captured?.Release(delete: true); }
        }
    }

    /// <summary>Confirms the exact pinned v3 script completed parsing before committing built-in replacement.</summary>
    internal RunningScriptSession WaitForWorkflowReady(RunningScriptSession started, Guid flowId, int timeoutMs = 6000)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            string ready;
            RunningScriptSession current;
            lock (_gate)
            {
                current = _sessions.GetValueOrDefault(started.ScriptPath) ?? throw new InvalidOperationException("The attempted script is no longer tracked.");
                if (current.State != ScriptState.Running || current.ProcessId != started.ProcessId || !_sources.TryGetValue(started.ScriptPath, out var source))
                    throw new InvalidOperationException(current.Error ?? "The replacement script stopped before it became ready.");
                ready = source.ReadyPath;
            }
            try
            {
                if (File.Exists(ready) && new FileInfo(ready).Length <= 128
                    && File.ReadAllText(ready).Trim().Replace("\r\n", "\n", StringComparison.Ordinal) == current.ProcessId + "\n" + flowId.ToString("D")) return current;
            }
            catch (IOException) { /* A short-lived writer can still be completing its ready receipt. */ }
            Thread.Sleep(20);
        }
        throw new TimeoutException("The replacement script did not confirm startup within six seconds.");
    }

    /// <summary>Registers callbacks after ownership metadata, then checks the already-exited race explicitly.</summary>
    private void Attach(string path, Process process, bool readErrors = false)
    {
        _processes[path] = process;
        _errors[path] = new StringBuilder();
        process.Exited += Process_Exited;
        if (readErrors)
        {
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.BeginErrorReadLine();
        }
        process.EnableRaisingEvents = true;
        if (_processes.TryGetValue(path, out var current) && ReferenceEquals(current, process)
            && process.HasExited) CompleteExit(path, process);
    }

    /// <summary>Captures interpreter diagnostics with a bound rather than buffering unlimited script output.</summary>
    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null) return;
        lock (_gate)
        {
            var path = _processes.FirstOrDefault(pair => ReferenceEquals(pair.Value, sender)).Key;
            if (path is not null && _errors.TryGetValue(path, out var text) && text.Length < 16384)
                text.AppendLine(args.Data[..Math.Min(args.Data.Length, 16384 - text.Length)]);
        }
    }

    /// <summary>Moves an exited owned process to its terminal state without operating on a recycled PID.</summary>
    private void Process_Exited(object? sender, EventArgs args)
    {
        // Process invokes Exited while holding its own lock; never wait for our service lock there.
        if (sender is Process process) ThreadPool.QueueUserWorkItem(_ => HandleProcessExit(process));
    }

    /// <summary>Processes a queued exit after the Process callback released its own synchronization lock.</summary>
    private void HandleProcessExit(Process process)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var path = _processes.FirstOrDefault(pair => ReferenceEquals(pair.Value, process)).Key;
            if (path is not null) CompleteExit(path, process);
        }
    }

    /// <summary>Removes process ownership, saves active sessions and notifies consumers of the exact exit state.</summary>
    private void CompleteExit(string path, Process process)
    {
        if (!_processes.TryGetValue(path, out var owned) || !ReferenceEquals(owned, process)) return;
        var session = _sessions[path];
        var failed = session.State != ScriptState.Stopping && process.ExitCode != 0;
        var details = _errors.GetValueOrDefault(path)?.ToString().Trim();
        _sessions[path] = session with
        {
            State = failed ? ScriptState.Failed : ScriptState.Stopped,
            Error = failed ? (string.IsNullOrEmpty(details) ? $"AutoHotkey exited with code {process.ExitCode}." : details) : null
        };
        _processes.Remove(path);
        if (_sources.Remove(path, out var source)) source.Release(delete: true);
        _errors.Remove(path);
        process.Exited -= Process_Exited;
        process.ErrorDataReceived -= Process_ErrorDataReceived;
        process.Dispose();
        Persist();
        ServiceDiagnostics.Write("Execution", $"Exit {path}: {_sessions[path].State}; {_sessions[path].Error}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves just the identities of owned processes that have not completed their exit callback.</summary>
    private void Persist()
    {
        _store.Save(_sessions.Values.Where(session => _processes.ContainsKey(session.ScriptPath)
            && session.ProcessId.HasValue && session.ProcessStartUtc.HasValue)
            .Select(session => new PersistedScriptSession(session.ScriptPath, session.ProcessId!.Value, session.ProcessStartUtc!.Value,
                session.RegisteredTrigger, session.ShortcutSnapshotKnown, session.SnapshotPath, session.SnapshotSha256)));
        if (_store.LastError is not null) LastWarning = _store.LastError;
    }

    /// <summary>Preserves running scripts on manager exit while detaching callbacks and disposing handles.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Persist();
            _disposed = true;
            foreach (var process in _processes.Values)
            {
                process.Exited -= Process_Exited;
                process.ErrorDataReceived -= Process_ErrorDataReceived;
                process.Dispose();
            }
            _processes.Clear();
            foreach (var source in _sources.Values) source.Release(delete: false);
            _sources.Clear();
        }
    }
}
