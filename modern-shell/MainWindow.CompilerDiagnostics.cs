using AutoHotkeyUX.Modern.Models;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Exercises actual pinned compiler success, failure, file protection and owned cancellation.</summary>
    private async Task<object> VerifyCompilerAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".verification", $"tools-{Guid.NewGuid():N}", "中文 空格");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "sample script.ahk");
        await File.WriteAllTextAsync(source, "#Requires AutoHotkey v2.0\nExitApp\n");
        NavigateTo("compile");
        await Task.Delay(100);
        var output = Path.Combine(directory, "sample 64.exe");
        var result = await _services.Compiler.CompileAsync(new(source, output, null), null, CancellationToken.None);
        if (!result.Succeeded || result.ExitCode != 0) throw new InvalidOperationException($"64-bit compiler verification failed: {result.Log}");
        using (var file = File.OpenRead(output))
        using (var pe = new PEReader(file))
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new InvalidOperationException("The 64-bit build has the wrong PE architecture.");
        var existingHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
        var refused = await _services.Compiler.CompileAsync(new(source, output, null), null, CancellationToken.None);
        if (refused.Succeeded || !existingHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(output))))
            throw new InvalidOperationException("Existing compiler output was overwritten without approval.");
        var output32 = Path.Combine(directory, "sample 32.exe");
        var result32 = await _services.Compiler.CompileAsync(new(source, output32, null, CompilerArchitecture.Bit32), null, CancellationToken.None);
        if (!result32.Succeeded) throw new InvalidOperationException($"32-bit compiler verification failed: {result32.Log}");
        using (var file = File.OpenRead(output32))
        using (var pe = new PEReader(file))
            if (pe.PEHeaders.CoffHeader.Machine != Machine.I386) throw new InvalidOperationException("The 32-bit build has the wrong PE architecture.");
        var broken = Path.Combine(directory, "missing include.ahk");
        await File.WriteAllTextAsync(broken, "#Requires AutoHotkey v2.0\n#Include definitely-missing-include.ahk\n");
        var failed = await _services.Compiler.CompileAsync(new(broken, output, null, ReplaceExisting: true), null, CancellationToken.None);
        if (failed.Succeeded || failed.ExitCode == 0 || !existingHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(output))))
            throw new InvalidOperationException("A failing compile damaged the approved existing output or lost its error code.");
        var slow = Path.Combine(directory, "cancel build.ahk");
        await File.WriteAllTextAsync(slow, "#Requires AutoHotkey v2.0\n;@Ahk2Exe-Obey U_WAIT, = (Sleep(6000), 0)\nExitApp\n");
        var cancelOutput = Path.Combine(directory, "cancel.exe");
        var job = _services.Compiler.CompileAsync(new(slow, cancelOutput, null), null, CancellationToken.None);
        var duplicateRejected = false;
        try { _ = _services.Compiler.CompileAsync(new(source, cancelOutput, null), null, CancellationToken.None); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        if (!duplicateRejected) throw new InvalidOperationException("The compiler accepted concurrent builds.");
        await WaitForToolAsync(() => _services.Compiler.State.ProcessId is not null || job.IsCompleted, "owned compiler process");
        var pid = _services.Compiler.State.ProcessId;
        _services.Compiler.CancelCurrent();
        var cancelled = await job;
        if (!cancelled.Cancelled || File.Exists(cancelOutput) || Directory.EnumerateFileSystemEntries(directory, ".ahkux-*").Any())
            throw new InvalidOperationException($"Compiler cancellation or scratch cleanup failed: {cancelled.Log}");
        NavigateTo("home");
        return new { Success64 = true, Success32 = true, UnicodeAndSpacePaths = true, ExistingFileProtected = true,
            FailedReplacementProtected = true, DuplicateRejected = true, Cancelled = true, OwnedCompilerPid = pid,
            ScratchCleaned = true, FixtureDirectory = directory, Result64ExitCode = result.ExitCode, FailureExitCode = failed.ExitCode };
    }
}
