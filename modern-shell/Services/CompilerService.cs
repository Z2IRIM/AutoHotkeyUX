using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns one compiler job and commits successful output without exposing partial executables.</summary>
internal sealed class CompilerService : IDisposable
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly EmbeddedCompiler _compiler;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task<CompilerResult>? _active;
    private bool _shuttingDown;
    private CompilerState _state = new(false, "Ready", "Select an AutoHotkey v2 script to begin.");
    internal event EventHandler? Changed;
    internal CompilerState State { get { lock (_gate) return _state; } }

    /// <summary>Uses the existing runtime chooser and a separate official compiler resource.</summary>
    internal CompilerService(AutoHotkeyIntegration integration, EmbeddedCompiler compiler)
    { _integration = integration; _compiler = compiler; }

    /// <summary>Accepts exactly one job, rejecting duplicate clicks even during resource preparation.</summary>
    internal Task<CompilerResult> CompileAsync(CompilerRequest request, IProgress<string>? output, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_shuttingDown) throw new InvalidOperationException("The application is shutting down.");
            if (_active is { IsCompleted: false }) throw new InvalidOperationException("A compiler job is already running.");
            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellation.Token;
            _state = new(true, "Preparing", string.Empty);
            _active = Task.Run(() => RunAsync(request, output, token));
            return _active;
        }
    }

    /// <summary>Cancels the current owned job without disabling future builds.</summary>
    internal void CancelCurrent() { lock (_gate) _cancellation?.Cancel(); }

    /// <summary>Prevents new jobs and waits for the owned compiler and temporary output to be cleaned up.</summary>
    internal async Task CancelAndDrainAsync()
    {
        Task<CompilerResult>? active;
        lock (_gate) { _shuttingDown = true; _cancellation?.Cancel(); active = _active; }
        if (active is not null) await active;
    }

    /// <summary>Validates inputs, invokes the silent CLI and atomically publishes only a completed executable.</summary>
    private async Task<CompilerResult> RunAsync(CompilerRequest request, IProgress<string>? output, CancellationToken cancellationToken)
    {
        string? temporary = null;
        string? scratchDirectory = null;
        Process? process = null;
        var destination = request.OutputPath;
        var exitCode = -1;
        try
        {
            var source = ValidateFile(request.SourcePath, ".ahk", "Source script");
            destination = Path.GetFullPath(request.OutputPath.Trim());
            if (!destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must end in .exe.");
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and output must be different files.");
            var directory = Path.GetDirectoryName(destination)!;
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing output directory.");
            if (File.Exists(destination) && !request.ReplaceExisting) throw new IOException("The output already exists. Confirm replacement inside the page or choose another filename.");
            var icon = string.IsNullOrWhiteSpace(request.IconPath) ? null : ValidateFile(request.IconPath, ".ico", "Icon");
            var build = request.Architecture == CompilerArchitecture.Bit32 ? "32-bit" : "64-bit";
            var runtime = _integration.FindRuntime(build) ?? throw new InvalidOperationException($"The built-in {build} runtime is unavailable.");
            var compilerPath = await _compiler.EnsureReadyAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            temporary = Path.Combine(directory, $".ahkux-build-{Guid.NewGuid():N}.exe");
            scratchDirectory = Path.Combine(directory, $".ahkux-work-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratchDirectory);
            var start = new ProcessStartInfo(compilerPath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(source)!
            };
            start.Environment["TEMP"] = scratchDirectory;
            start.Environment["TMP"] = scratchDirectory;
            foreach (var argument in new[] { "/silent", "verbose", "/in", source, "/out", temporary, "/base", runtime.Path, "/compress", "0", "/cp", "UTF-8" })
                start.ArgumentList.Add(argument);
            if (icon is not null) { start.ArgumentList.Add("/icon"); start.ArgumentList.Add(icon); }
            Append($"Preparing {build} · AutoHotkey {runtime.Version}", output);
            process = Process.Start(start) ?? throw new InvalidOperationException("The compiler process could not be started.");
            lock (_gate) _state = _state with { ProcessId = process.Id };
            Changed?.Invoke(this, EventArgs.Empty);
            using var readCancellation = new CancellationTokenSource();
            var stdout = ReadOutputAsync(process.StandardOutput, output, readCancellation.Token);
            var stderr = ReadOutputAsync(process.StandardError, output, readCancellation.Token);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                readCancellation.CancelAfter(TimeSpan.FromSeconds(2));
                await Task.WhenAll(stdout, stderr);
            }
            exitCode = process.ExitCode;
            cancellationToken.ThrowIfCancellationRequested();
            if (exitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length < 1024)
                throw new InvalidOperationException($"Compiler exited with code {exitCode}. No completed executable was committed.");
            using (var file = File.OpenRead(temporary))
                if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("The compiler output is not a Windows executable.");
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, request.ReplaceExisting);
            }
            Append($"Build succeeded.\nOutput: {destination}\nExit code: {exitCode}\nThe executable was not launched.", output);
            SetCompleted("Complete", destination, exitCode, true);
            ServiceDiagnostics.Write("Compiler", $"Build completed ({build}, exit {exitCode}).");
            return new(exitCode, true, false, destination, State.Log);
        }
        catch (OperationCanceledException)
        {
            Append("Build cancelled. No partial executable was committed.", output);
            SetCompleted("Cancelled", destination, exitCode, false);
            return new(exitCode, false, true, destination, State.Log);
        }
        catch (Exception ex)
        {
            Append(ex.Message, output); SetCompleted("Failed", destination, exitCode, false);
            ServiceDiagnostics.Write("Compiler", $"Build failed (exit {exitCode}).", ex);
            return new(exitCode, false, false, destination, State.Log);
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process.Dispose();
            }
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ServiceDiagnostics.Write("Compiler", "Temporary output cleanup failed.", ex); }
            }
            if (scratchDirectory is not null && Directory.Exists(scratchDirectory))
            {
                var full = Path.GetFullPath(scratchDirectory);
                var outputParent = Path.GetFullPath(Path.GetDirectoryName(destination)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(outputParent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(".ahkux-work-", StringComparison.Ordinal))
                    throw new IOException("Compiler scratch cleanup escaped its output directory.");
                try { Directory.Delete(full, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ServiceDiagnostics.Write("Compiler", "Build scratch cleanup failed.", ex); }
            }
        }
    }

    /// <summary>Reads compiler lines asynchronously, bounding a inherited pipe that stays open after exit.</summary>
    private async Task ReadOutputAsync(StreamReader reader, IProgress<string>? output, CancellationToken cancellationToken)
    {
        try { while (await reader.ReadLineAsync(cancellationToken) is { } line) Append(line, output); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Appends bounded output and publishes state changes without holding the service lock.</summary>
    private void Append(string line, IProgress<string>? output)
    {
        lock (_gate)
        {
            var log = _state.Log + line + "\n";
            if (log.Length > 65536) log = "[Earlier output omitted]\n" + log[^60000..];
            _state = _state with { Log = log, Status = "Building" };
        }
        output?.Report(line); Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stores the final result so returning to the page restores the completed job.</summary>
    private void SetCompleted(string status, string destination, int exitCode, bool succeeded)
    {
        lock (_gate) _state = _state with { IsRunning = false, Status = status, OutputPath = destination, ExitCode = exitCode, Succeeded = succeeded, ProcessId = null };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Normalizes required input paths and rejects missing or incorrectly typed files.</summary>
    private static string ValidateFile(string path, string extension, string description)
    {
        var full = Path.GetFullPath(path.Trim());
        if (!full.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new ArgumentException($"{description} must be an existing {extension} file.");
        return full;
    }

    /// <summary>Signals shutdown; the App owns the asynchronous drain before disposal.</summary>
    public void Dispose() { lock (_gate) { _shuttingDown = true; _cancellation?.Cancel(); } }
}
