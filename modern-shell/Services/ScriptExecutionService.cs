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
    private bool _disposed;
    internal string? LastWarning { get; private set; }
    internal event EventHandler? Changed;

    /// <summary>Reuses the shell runtime locator through a provider without coupling process handling to XAML.</summary>
    internal ScriptExecutionService(Func<string?> runtimePath, ScriptSessionStore store)
    {
        _runtimePath = runtimePath;
        _store = store;
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
                    _sessions[path] = new(path, entry.Pid, entry.ProcessStartUtc, entry.ProcessStartUtc, ScriptState.Running);
                    Attach(path, process);
                    process = null; // The service now owns the handle.
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                    or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                {
                    if (process is not null)
                    {
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
    internal RunningScriptSession Run(string scriptPath, bool workflowClaimHeld = false)
    {
        var path = Path.GetFullPath(scriptPath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processes.TryGetValue(path, out var existing))
            {
                if (!existing.HasExited) return _sessions[path];
                CompleteExit(path, existing);
            }
            _sessions[path] = new(path, null, null, DateTime.UtcNow, ScriptState.Starting);
            Changed?.Invoke(this, EventArgs.Empty);
            Process? process = null;
            try
            {
                using var workflowClaim = workflowClaimHeld ? null : VisualFlowStore.ClaimForExecution(path);
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
                start.ArgumentList.Add(path);
                process = new Process { StartInfo = start };
                if (!process.Start()) throw new InvalidOperationException("AutoHotkey did not start.");
                _ = process.SafeHandle;
                var started = process.StartTime.ToUniversalTime();
                _sessions[path] = new(path, process.Id, started, DateTime.UtcNow, ScriptState.Running);
                Attach(path, process, readErrors: true);
                process = null;
                Persist();
                ServiceDiagnostics.Write("Execution", $"Run {path}: {_sessions[path].State}");
            }
            catch (Exception ex)
            {
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

    /// <summary>Claims visual source before stopping it, preserving a live interpreter when a save is unfinished or in flight.</summary>
    internal RunningScriptSession Restart(string scriptPath)
    {
        var path = Path.GetFullPath(scriptPath);
        lock (_gate) { using var workflowClaim = VisualFlowStore.ClaimForExecution(path); Stop(path); return Run(path, workflowClaimHeld: true); }
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
            .Select(session => new PersistedScriptSession(session.ScriptPath, session.ProcessId!.Value, session.ProcessStartUtc!.Value)));
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
        }
    }
}
