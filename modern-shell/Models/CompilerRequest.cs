namespace AutoHotkeyUX.Modern.Models;

internal enum CompilerArchitecture { Bit64, Bit32 }
internal sealed record CompilerRequest(string SourcePath, string OutputPath, string? IconPath,
    CompilerArchitecture Architecture = CompilerArchitecture.Bit64, bool ReplaceExisting = false);
internal sealed record CompilerResult(int ExitCode, bool Succeeded, bool Cancelled, string OutputPath, string Log);
internal sealed record CompilerState(bool IsRunning, string Status, string Log, string? OutputPath = null, int? ExitCode = null, bool Succeeded = false, int? ProcessId = null);
