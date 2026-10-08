using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Diagnostics;
using System.IO.Compression;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Runs explicitly approved native probes against this diagnostic instance and its private fixture directory.</summary>
    internal async Task<object> VerifyConfigurableRuntimeAsync()
    {
        var root = Path.Combine(_services.StateDirectory, "configurable-probes"); Directory.CreateDirectory(root);
        var runtime = _integration.FindRuntime()?.Path ?? throw new InvalidOperationException("AHK runtime is unavailable.");
        var archive = Path.Combine(root, "sample 中文.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry("nested/hello.txt").Open()); writer.Write("fixture payload"); }
        var helpers = VisualFlowGenerator.Generate(VisualFlowExamples.Terminal() with { Trigger = new() { Kind = FlowTriggerKind.Startup } });
        var terminal = Path.Combine(root, "terminal.txt"); var warm = Path.Combine(root, "warm.txt"); var cold = Path.Combine(root, "cold.txt");
        await RunConfigurableProbeAsync(runtime, root, "terminal", TerminalProbe(root, terminal), helpers);
        if (!File.ReadAllText(terminal).StartsWith("ready\n", StringComparison.Ordinal)) throw new InvalidOperationException("The native terminal did not return its ready window.");
        var extractionBody = "global FlowIdentity := " + Q(Guid.NewGuid().ToString("D")) + "\n"
            + "context := {RunId: FlowGuid(), StepId: FlowGuid(), StepName: \"Extract archive\", Started: A_TickCount}\n"
            + "options := {Mode: \"Custom\", Destination: \"Custom\", Naming: \"Composition\", Name: \"Configured output\", Collision: \"AutoSuffix\"}\n"
            + "FlowEvent(context, context.StepId, context.StepName, \"started\", \"\")\n"
            + "result := FlowExtract3(" + Q(archive) + ", " + Q(root) + ", options, context)\n"
            + "if !result.Success || !DirExist(result.Directory)\n    throw Error(\"No committed extraction output.\")\n"
            + "FlowEvent(context, context.StepId, context.StepName, \"succeeded\", result.Directory)\nFileAppend result.Directory, {REPORT}, \"UTF-8-RAW\"\n";
        await RunConfigurableProbeAsync(runtime, root, "warm", extractionBody.Replace("{REPORT}", Q(warm), StringComparison.Ordinal), helpers);
        var output = File.ReadAllText(warm);
        if (File.ReadAllText(Path.Combine(output, "nested", "hello.txt")) != "fixture payload") throw new InvalidOperationException("The warm extraction result is incomplete.");
        var window = _services.Settings.Read("Modern", "ShortcutWindow");
        _services.Settings.Write("Modern", "ShortcutWindow", "0");
        try { await RunConfigurableProbeAsync(runtime, root, "cold", extractionBody.Replace("{REPORT}", Q(cold), StringComparison.Ordinal), helpers); }
        finally { _services.Settings.Write("Modern", "ShortcutWindow", window); }
        var coldOutput = File.ReadAllText(cold);
        if (coldOutput == output || File.ReadAllText(Path.Combine(coldOutput, "nested", "hello.txt")) != "fixture payload" || !File.Exists(archive))
            throw new InvalidOperationException("Cold extraction did not preserve its source and previous output.");
        var failureBody = "global FlowIdentity := " + Q(Guid.NewGuid().ToString("D")) + "\n"
            + "context := {RunId: FlowGuid(), StepId: FlowGuid(), StepName: \"Collision\", Started: A_TickCount}\n"
            + "options := {Mode: \"Custom\", Destination: \"Custom\", Naming: \"Composition\", Name: \"Configured output\", Collision: \"Error\"}\n"
            + "rejected := false\ntry FlowExtract3(" + Q(archive) + ", " + Q(root) + ", options, context)\ncatch Error\n    rejected := true\n"
            + "if !rejected\n    throw Error(\"Existing output was not protected.\")\n"
            + "path := FlowJoinPath(" + Q(root) + ", [\"Prepared\"])\ncreated := FlowCreateDirectory(path.Path, false)\n"
            + "if !created.Success || !FlowCondition3(created.Path, \"PathExists\", \"\") || FlowCompose([\"done \", created.Name]) != \"done Prepared\"\n    throw Error(\"Generic path/text operations failed.\")\n";
        await RunConfigurableProbeAsync(runtime, root, "failure-paths", failureBody, helpers);
        if (File.ReadAllText(Path.Combine(output, "nested", "hello.txt")) != "fixture payload") throw new InvalidOperationException("Failure altered the previous output.");
        var replacement = await Task.Run(() => VerifyNativeReplacement(root, runtime));
        return new { Passed = true, Terminal = File.ReadAllText(terminal), WarmOutput = output, ColdOutput = coldOutput,
            ExistingOutputProtected = true, GenericPathOperations = true, ReadyAndRecovery = replacement, FixtureRoot = root,
            PhysicalExplorerAndDesktopGesture = "unvalidated" };
    }

    /// <summary>Checks actual above/below placement and closes only the two freshly returned terminal HWNDs.</summary>
    private static string TerminalProbe(string root, string report) =>
        "MouseGetPos &mouseX, &mouseY\nwork := GetClickMonitor(mouseX, mouseY)\n"
        + "context := {X: (work.Left + work.Right) // 2, Y: work.Bottom - 40}\nwindows := []\ntry {\n"
        + "    for position in [\"above\", \"below\"] {\n"
        + "        if position = \"below\"\n            context.Y := work.Top + 40\n"
        + "        options := {Mode: \"Custom\", Program: \"powershell\", Position: position, Width: 800, Height: 440, Gap: 16, WaitReady: true, TimeoutMs: 6000}\n"
        + "        result := FlowTerminal3(" + Q(root) + ", context, options)\n        windows.Push(result.WindowId)\n"
        + "        WinGetPos &x, &y, &width, &height, \"ahk_id \" result.WindowId\n"
        + "        if x < work.Left - 1 || y < work.Top - 1 || x + width > work.Right + 1 || y + height > work.Bottom + 1\n            throw Error(\"Terminal escaped the clicked monitor work area.\")\n"
        + "        if position = \"above\" && y + height >= context.Y || position = \"below\" && y <= context.Y\n            throw Error(\"Terminal did not open on the requested cursor side.\")\n"
        + "        FileAppend \"ready`n\" position \" hwnd=\" result.WindowId \" x=\" x \" y=\" y \" width=\" width \" height=\" height \"`n\", " + Q(report) + ", \"UTF-8-RAW\"\n"
        + "        WinClose \"ahk_id \" result.WindowId\n    }\n} finally {\n    for window in windows\n        if WinExist(\"ahk_id \" window)\n            WinClose \"ahk_id \" window\n}\n";

    /// <summary>Collects real parser/runtime output with a bound while keeping the manager dispatcher free for IPC.</summary>
    private async Task RunConfigurableProbeAsync(string runtime, string root, string name, string body, string helpers)
    {
        var path = Path.Combine(root, name + ".ahk");
        File.WriteAllText(path, "#Requires AutoHotkey v2.0\n" + body + "\nExitApp 0\n" + helpers, VisualFlowCodec.Utf8);
        var start = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/ErrorStdOut=UTF-8"); start.ArgumentList.Add(path);
        start.Environment["AUTOHOTKEYUX_FLOW_KEY"] = @"HKCU\" + _services.Settings.BaseKey + @"\Modern";
        start.Environment["LOCALAPPDATA"] = Path.GetDirectoryName(_services.StateDirectory)!;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("AHK probe did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
        catch (TimeoutException) { if (!process.HasExited) process.Kill(); throw new TimeoutException("Native probe timed out: " + name); }
        var details = await stdout + await stderr;
        File.WriteAllText(Path.Combine(root, name + ".log"), details);
        if (process.ExitCode != 0) throw new InvalidOperationException("Native probe failed: " + name + "\n" + details);
    }

    /// <summary>Exercises a real failed start followed by a ready replacement using only isolated owned interpreters.</summary>
    private object VerifyNativeReplacement(string root, string runtime)
    {
        var formal = new AutoHotkeySettings();
        var formalPreferences = ShortcutPreferenceCodec.Decode(formal.Read("Modern", ShortcutPreferenceCodec.SettingName)).Preferences;
        if (formal.ReadBoolean("Modern", "ExplorerShortcuts", false) && (formalPreferences.TerminalShortcut == "ctrl-alt-middle" || formalPreferences.ArchiveShortcut == "ctrl-alt-middle"))
            return new { Passed = false, Status = "unvalidated", Reason = "The diagnostic binding is already used by the formal built-in." };
        var fixture = Path.Combine(root, "replacement"); Directory.CreateDirectory(fixture);
        var saved = _services.Settings.Read("Modern", ShortcutPreferenceCodec.SettingName);
        var preferences = ShortcutPreferences.Default with { TerminalShortcut = "ctrl-alt-middle", ArchiveEnabled = false, TerminalProgram = "powershell" };
        _services.Settings.Write("Modern", ShortcutPreferenceCodec.SettingName, ShortcutPreferenceCodec.Encode(new(Guid.NewGuid().ToString("N"), preferences)));
        ScriptExecutionService? execution = null; var failNextLaunch = false; var core = Path.Combine(fixture, "Explorer Shortcuts.ahk");
        execution = new(() => { if (!failNextLaunch) return runtime; failNextLaunch = false; return Path.Combine(fixture, "missing-runtime.exe"); },
            new ScriptSessionStore(Path.Combine(fixture, "sessions.json")),
            (path, trigger) => VisualHotkeyConflicts.Check(path, execution!.Snapshot(), _services.Settings, core, trigger),
            registryBase: _services.Settings.BaseKey, diagnosticLocalDataRoot: Path.GetDirectoryName(_services.StateDirectory));
        var startup = new ScriptStartupService(fixture, _services.Settings, execution);
        var service = new ShortcutPreferencesService(_services.Settings, startup, execution);
        var activation = new VisualWorkflowActivationService(startup, execution, service);
        var flow = VisualFlowExamples.ExplorerV3() with { Trigger = new() { Key = "MButton", Modifiers = FlowModifiers.Ctrl | FlowModifiers.Alt, Scope = FlowScopeKind.ExplorerDesktop } };
        var opened = new VisualFlowStore().Create(fixture, "replacement", flow);
        try
        {
            startup.SetExplorerEnabled(true);
            var previous = new VisualFlowStore().Create(fixture, "running-candidate", flow with
            { Id = Guid.NewGuid(), Trigger = flow.Trigger with { Modifiers = FlowModifiers.Ctrl | FlowModifiers.Alt | FlowModifiers.Shift } });
            var live = execution.Run(previous.ScriptPath); Thread.Sleep(200);
            if (execution.Snapshot().Single(session => session.ScriptPath == previous.ScriptPath).State != ScriptState.Running)
                throw new InvalidOperationException("The original candidate did not remain running.");
            new VisualFlowStore().Update(previous, previous.Document with { Trigger = flow.Trigger });
            var preserved = false;
            try { activation.ReplaceBuiltIn(previous.ScriptPath); } catch (InvalidOperationException) { preserved = true; }
            if (!preserved || !startup.ExplorerEnabled || execution.Snapshot().Single(session => session.ScriptPath == previous.ScriptPath).ProcessId != live.ProcessId)
                throw new InvalidOperationException("Replacement affected an already running candidate snapshot.");
            execution.Stop(previous.ScriptPath); failNextLaunch = true;
            var failed = false;
            try { activation.ReplaceBuiltIn(opened.ScriptPath); } catch (InvalidOperationException) { failed = true; }
            if (!failed || !startup.ExplorerEnabled || startup.IsRunAtSignIn(opened.ScriptPath)
                || execution.Snapshot().All(session => !startup.IsExplorerScript(session.ScriptPath) || session.State != ScriptState.Running))
                throw new InvalidOperationException("A failed native start did not restore the owned built-in and login choice.");
            var ready = activation.ReplaceBuiltIn(opened.ScriptPath);
            if (ready.State != ScriptState.Running || startup.ExplorerEnabled || !startup.IsRunAtSignIn(opened.ScriptPath))
                throw new InvalidOperationException("A ready native replacement did not commit its startup choice.");
            return new { Passed = true, RunningCandidatePreserved = true, FailedLaunchRecovery = true, ExactReadyAcknowledgement = true, StartupTransferred = true };
        }
        finally
        {
            foreach (var session in execution.Snapshot().Where(session => session.State == ScriptState.Running)) execution.Stop(session.ScriptPath);
            startup.SetExplorerEnabled(false); startup.SetRunAtSignIn(opened.ScriptPath, false); execution.Dispose();
            _services.Settings.Write("Modern", ShortcutPreferenceCodec.SettingName, saved);
        }
    }

    /// <summary>Uses the shared source literal escaper for test-owned paths and values.</summary>
    private static string Q(string value) => VisualFlowGenerator.Literal(value);
}
