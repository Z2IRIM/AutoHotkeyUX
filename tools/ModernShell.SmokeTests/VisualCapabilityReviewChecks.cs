using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.Win32;
using System.Diagnostics;

/// <summary>Reproduces the three final-review findings using isolated metadata and test-owned interpreters.</summary>
internal static class VisualCapabilityReviewChecks
{
    /// <summary>Runs each finding independently so RED evidence covers the entire one-pass repair scope.</summary>
    internal static async Task RunAsync(string runtime, string root)
    {
        var failures = new List<string>();
        foreach (var (name, check) in new (string, Func<Task>)[]
        { ("live shortcut snapshot survives save and recovery", () => Snapshot(runtime, root)),
          ("editor rejects excessive conditions before draft mutation", Depth), ("mouse scopes include inactive Explorer clicks", () => Scopes(runtime, root)) })
        {
            try { await check(); Console.WriteLine("PASS: " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL: " + name + " — " + ex.Message); }
        }
        if (failures.Count > 0) throw new Exception(string.Join(", ", failures));
        Console.WriteLine("REVIEW: 3/3 findings passed");
    }
    /// <summary>Runs a harmless hotkey, saves a different key, and checks actual registered state and recovery.</summary>
    private static async Task Snapshot(string runtime, string root)
    {
        var registry = @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        var settings = new AutoHotkeySettings(registry); var store = new VisualFlowStore();
        var trigger = new FlowTrigger { Key = "F11", Modifiers = FlowModifiers.Ctrl | FlowModifiers.Alt | FlowModifiers.Shift };
        var flow = new VisualFlowDocument { Trigger = trigger, Actions = [new() { Kind = FlowActionKind.Wait, DelayMs = 1 }] };
        var old = store.Create(root, "live-first", flow); var second = store.Create(root, "live-second", flow with { Id = Guid.NewGuid() });
        var state = new ScriptSessionStore(Path.Combine(root, "live-sessions.json")); ScriptExecutionService? service = null;
        service = new(() => runtime, state, (path, trigger) => VisualHotkeyConflicts.Check(path, service!.Snapshot(), settings, launchTrigger: trigger));
        try
        {
            var launched = service.Run(old.ScriptPath);
            Require(launched.State == ScriptState.Running && launched.RegisteredTrigger == trigger && launched.SnapshotPath is not null, "first interpreter launch snapshot");
            await Task.Delay(100);
            var locked = false;
            try { File.WriteAllText(launched.SnapshotPath!, "mutate"); } catch (IOException) { locked = true; }
            Require(locked, "the interpreter source could change before it loaded its captured key");
            var changed = store.Update(old, old.Document with { Trigger = trigger with { Key = "F10" } });
            Require(service.Run(second.ScriptPath).State == ScriptState.Failed, "Saved new disk key incorrectly freed the old running key");
            using (VisualFlowStore.ClaimForExecution(old.ScriptPath)) Require(service.Run(second.ScriptPath).State == ScriptState.Failed, "An in-flight save bypassed the live snapshot");
            service.Dispose();
            service = new(() => runtime, state, (path, trigger) => VisualHotkeyConflicts.Check(path, service!.Snapshot(), settings, launchTrigger: trigger)); service.Rehydrate();
            Require(service.Snapshot().Any(session => session.ScriptPath == old.ScriptPath && session.State == ScriptState.Running), "recovered owned identity");
            Require(service.Run(second.ScriptPath).State == ScriptState.Failed, "Recovery trusted new disk key rather than the launch snapshot");
            Require(service.Restart(changed.ScriptPath).State == ScriptState.Running, "restart updates live identity");
            Require(service.Run(second.ScriptPath).State == ScriptState.Running, "old key remains blocked after successful restart");
            var unknown = service.Snapshot().Single(session => session.ScriptPath == old.ScriptPath) with { ShortcutSnapshotKnown = false, RegisteredTrigger = null };
            Require(VisualHotkeyConflicts.Check(second.ScriptPath, [unknown], settings, launchTrigger: trigger)?.Contains("not recorded reliably", StringComparison.Ordinal) == true,
                "older recovery guessed its registered key from disk");
            Require(VisualHotkeyConflicts.CheckBuiltInAgainstRunning(ShortcutPreferences.Default, [unknown]) is not null, "unknown live shortcut was skipped for builtin mutation");
            Require(VisualHotkeyConflicts.CheckBuiltInAgainstRunning(ShortcutPreferences.Default with { TerminalEnabled = false, ArchiveEnabled = false }, [unknown]) is null,
                "unknown registration blocked turning all builtin bindings off");
        }
        finally { service.Stop(old.ScriptPath); service.Stop(second.ScriptPath); service.Dispose(); Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
    /// <summary>Requires over-depth replacement to preserve the prior draft while three layers remain editable.</summary>
    private static Task Depth()
    {
        var session = new VisualEditorSession(); var before = session.Document;
        var invalid = before with { SchemaVersion = 2, Actions = [Condition(4)] };
        var rejected = false;
        try { session.Replace(invalid); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && VisualFlowTree.Same(session.Document.Actions, before.Actions), "four conditions entered the draft instead of being rejected");
        var valid = before with { SchemaVersion = 2, Actions = [Condition(3)] }; session.Load(valid);
        var leaf = VisualFlowTree.Walk(valid.Actions).Last().Action;
        session.Replace(valid with { Actions = VisualFlowTree.Update(valid.Actions, leaf.Id, action => action with { DelayMs = 10 }) }); session.Undo(); session.Redo();
        Require(VisualFlowTree.Find(session.Document.Actions, leaf.Id)?.DelayMs == 10, "three-layer ordinary action/history rejected");
        return Task.CompletedTask;
    }
    /// <summary>Creates a bounded nested shape without relying on the production insertion implementation.</summary>
    private static FlowAction Condition(int depth) => new() { Kind = FlowActionKind.IfElse, Parameters = new() { Input = new() { Literal = "x" }, Condition = FlowConditionKind.IsNotEmpty,
        Then = [depth == 1 ? new() { Kind = FlowActionKind.Wait, DelayMs = 1 } : Condition(depth - 1)] } };
    /// <summary>Distinguishes mouse-under-window predicates from foreground-only keyboard scopes.</summary>
    private static async Task Scopes(string runtime, string root)
    {
        var mouse = VisualFlowExamples.Explorer().Trigger;
        var notepad = mouse with { Scope = FlowScopeKind.ActiveApplication, Application = "notepad.exe" };
        Require(VisualHotkeyConflicts.Collides(mouse, notepad), "inactive Explorer and active Notepad mouse scopes falsely treated as exclusive");
        Require(VisualHotkeyConflicts.BuiltIn(notepad, ShortcutPreferences.Default) is not null, "builtin mouse overlap missed");
        Require(!VisualHotkeyConflicts.Collides(mouse with { Key = "D" }, notepad with { Key = "D" }), "foreground keyboard scopes unnecessarily overlap");
        var marker = Path.Combine(root, "scope-time.txt");
        var flow = new VisualFlowDocument { SchemaVersion = 2, Trigger = mouse with { Scope = FlowScopeKind.ActiveApplication, Application = "AutoHotkeyUX-no-active-window.exe" },
            Actions = [new() { Kind = FlowActionKind.Wait, DelayMs = 2000 }] };
        var script = Path.Combine(root, "scope-guard.ahk");
        File.WriteAllText(script, "#Requires AutoHotkey v2.0\nstarted := A_TickCount\nFlowRun()\nFileAppend A_TickCount - started, " + VisualFlowGenerator.Literal(marker) + ", \"UTF-8\"\nExitApp 0\n" + VisualFlowGenerator.Generate(flow), VisualFlowCodec.Utf8);
        var start = new ProcessStartInfo(runtime) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.ArgumentList.Add("/ErrorStdOut=UTF-8"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { process.Kill(); throw; }
        Require(process.ExitCode == 0 && int.Parse(File.ReadAllText(marker)) < 500, "inactive application scope ran its actions: " + await stdout + await stderr);
    }
    /// <summary>Fails at a concrete user-visible boundary.</summary>
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
