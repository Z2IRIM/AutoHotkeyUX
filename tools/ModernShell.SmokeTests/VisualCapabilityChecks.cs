using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

/// <summary>Proves versioned language acceptance before extending the native editor.</summary>
internal static class VisualCapabilityChecks
{
    /// <summary>Runs bounded capability probes without touching user scripts or clipboard.</summary>
    internal static async Task RunAsync(string runtime, string root)
    {
        var legacy = new VisualFlowDocument { Revision = 1, SourceSha256 = new string('A', 64),
            Actions = [new() { Kind = FlowActionKind.Wait, DelayMs = 10 }] };
        var json = VisualFlowCodec.Encode(legacy).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        var decoded = VisualFlowCodec.Decode(json);
        if (decoded.SchemaVersion != 2 || !VisualFlowGenerator.Generate(decoded).Contains("visual workflow v2", StringComparison.Ordinal))
            throw new Exception("FAIL: version 2 is not generated as a distinct workflow language");
        Console.WriteLine("PASS: distinct v2 codec and generator");
        var examples = new[] { VisualFlowExamples.Explorer(), VisualFlowExamples.CopyPath(), VisualFlowExamples.LaunchAndType() };
        foreach (var flow in examples) { VisualFlowCodec.Validate(flow); var store = new VisualFlowStore(); var opened = store.Create(root, "example", flow); Require(VisualFlowTree.Same(store.Open(opened.ScriptPath).Document.Actions, flow.Actions), "example reopen"); }
        Console.WriteLine("PASS: three useful examples create and reopen");
        References(); History(); Conflicts(); Extraction(root); Legacy(root);
        await Parse(runtime, root, examples); await Runtime(runtime, root);
        Console.WriteLine("CAPABILITIES: 9/9 boundary groups passed");
    }

    /// <summary>Rejects forward, deleted, cross-branch and invalid-field references, including source-free strict data.</summary>
    private static void References()
    {
        var flow = VisualFlowExamples.Explorer();
        var target = flow.Actions[0]; var condition = flow.Actions[1];
        Reject(() => VisualFlowCodec.Validate(flow with { Actions = [condition, target] }), "earlier result");
        Reject(() => VisualFlowCodec.Validate(flow with { Actions = [condition] }), "earlier result");
        var producer = new FlowAction { Kind = FlowActionKind.ReadClipboard, Parameters = new() };
        var consumer = new FlowAction { Kind = FlowActionKind.SetClipboard, Parameters = new() { Input = FlowInput.Reference(producer.Id, FlowResultField.Text) } };
        var branch = condition with { Parameters = condition.Parameters! with { Then = [producer], Else = [] } };
        Reject(() => VisualFlowCodec.Validate(flow with { Actions = [target, branch, consumer] }), "earlier result");
        Reject(() => VisualFlowCodec.Validate(flow with { Actions = [target, branch with { Parameters = branch.Parameters! with { Else = [consumer] } }] }), "earlier result");
        var valid = flow with { Actions = [target, branch with { Parameters = branch.Parameters! with { Then = [producer, consumer] } }] };
        VisualFlowCodec.Validate(valid);
        Reject(() => VisualFlowCodec.Validate(valid with { Actions = VisualFlowTree.Update(valid.Actions, consumer.Id, action => action with { Parameters = action.Parameters! with { Input = FlowInput.Reference(producer.Id) } }) }), "earlier result");
        Reject(() => VisualFlowCodec.Validate(valid with { Actions = [.. valid.Actions, consumer with { Id = Guid.NewGuid() }] }), "earlier result");
        Reject(() => VisualFlowCodec.Decode(VisualFlowCodec.Encode(valid with { Revision = 1, SourceSha256 = new string('A', 64) }).Replace("\"condition\": \"IsFolder\"", "\"condition\": \"IsFolder\", \"eval\": \"ExitApp\"")), "eval");
        Console.WriteLine("PASS: typed results respect order, deletion and branch visibility");
    }
    /// <summary>Checks nested edits and deep history without mutating array-backed snapshots.</summary>
    private static void History()
    {
        var flow = VisualFlowExamples.Explorer(); var session = new VisualEditorSession(); session.Load(flow);
        session.Replace(flow with { Actions = VisualFlowTree.Copy(flow.Actions) }); Require(!session.CanUndo, "deep copy is not an edit");
        var branch = new FlowBranch(flow.Actions[1].Id); var first = VisualFlowTree.Sequence(flow.Actions, branch)[0];
        var second = VisualFlowExamples.Action(FlowActionKind.Wait);
        session.Replace(flow with { Actions = VisualFlowTree.SetSequence(flow.Actions, branch, [first, second]) }); session.Selection = second.Id;
        session.Move(second.Id, 0); Require(VisualFlowTree.Sequence(session.Document.Actions, branch)[0].Id == second.Id, "nested move");
        session.Undo(); Require(VisualFlowTree.Sequence(session.Document.Actions, branch)[1].Id == second.Id, "nested undo");
        session.Redo(); Require(VisualFlowTree.Sequence(session.Document.Actions, branch)[0].Id == second.Id, "nested redo");
        Require(flow.Actions[1].Parameters!.Then.Length == 1, "original snapshot preserved");
        Console.WriteLine("PASS: nested ordering and deep undo preserve snapshots");
    }
    /// <summary>Proves scope-aware collision detection without changing user's shortcuts.</summary>
    private static void Conflicts()
    {
        var trigger = VisualFlowExamples.Explorer().Trigger;
        Require(VisualHotkeyConflicts.BuiltIn(trigger, ShortcutPreferences.Default) is not null, "builtin collision");
        Require(VisualHotkeyConflicts.BuiltIn(trigger, ShortcutPreferences.Default, false) is null, "disabled builtin");
        Require(VisualHotkeyConflicts.BuiltIn(trigger with { Scope = FlowScopeKind.ActiveApplication, Application = "notepad.exe" }, ShortcutPreferences.Default) is null, "separate scope");
        Require(VisualHotkeyConflicts.Collides(trigger, trigger with { Scope = FlowScopeKind.ActiveApplication, Application = "explorer.exe" }), "Explorer overlaps desktop scope");
        Require(!VisualHotkeyConflicts.Collides(trigger, trigger with { Modifiers = FlowModifiers.Alt | FlowModifiers.Shift }), "different modifier");
        Console.WriteLine("PASS: known shortcut collisions and distinct scopes");
    }
    /// <summary>Checks explicit extraction independent of shortcut preferences, including retained outputs on retry/failure.</summary>
    private static void Extraction(string root)
    {
        var archive = Path.Combine(root, "capability 中文.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open()); writer.Write("keep"); }
        var report = Path.Combine(root, "extract-report.txt"); Require(VisualExtractionCommand.Run(archive, "", report) == 0, "explicit extract");
        var output = File.ReadAllText(report)[3..]; Require(File.ReadAllText(Path.Combine(output, "hello.txt")) == "keep", "result path");
        var retry = Path.Combine(root, "extract-retry.txt"); Require(VisualExtractionCommand.Run(archive, "", retry) == 0 && File.ReadAllText(retry)[3..] != output, "retry suffix");
        Require(File.Exists(archive), "source retained");
        var failed = Path.Combine(root, "extract-failed.txt"); Require(VisualExtractionCommand.Run(archive, Path.Combine(root, "missing"), failed) == 1 && File.ReadAllText(failed).StartsWith("error\n"), "error report");
        Require(File.ReadAllText(Path.Combine(output, "hello.txt")) == "keep", "failure preserves output");
        Console.WriteLine("PASS: explicit extraction returns unique output and preserves failure evidence");
    }
    /// <summary>Verifies byte-for-byte v1 output and explicit upgrade while retaining the store's external-edit guard.</summary>
    private static void Legacy(string root)
    {
        var flow = new VisualFlowDocument { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Actions = [new() { Kind = FlowActionKind.Wait, DelayMs = 500 }] };
        var expected = "; AutoHotkey UX visual workflow v1\r\n; Workflow: 11111111-1111-1111-1111-111111111111\r\n#Requires AutoHotkey v2.0\r\n#SingleInstance Ignore\r\n\r\n^!d:: {\r\n    Sleep 500\r\n}\r\n";
        Require(VisualFlowGenerator.Generate(flow) == expected, "legacy exact source");
        var store = new VisualFlowStore(); var old = store.Create(root, "legacy", flow); Require(!File.ReadAllText(old.ScriptPath + ".flow.json").Contains("parameters"), "legacy JSON unchanged");
        var upgraded = store.Update(old, flow with { SchemaVersion = 2, Actions = [.. flow.Actions, new() { Kind = FlowActionKind.ReadClipboard, Parameters = new() }] });
        Require(store.Open(upgraded.ScriptPath).Document.SchemaVersion == 2, "upgrade reopen");
        File.AppendAllText(upgraded.ScriptPath, "; manual change"); Reject(() => store.Update(upgraded, upgraded.Document), "changed outside");
        Console.WriteLine("PASS: v1 exact bytes, explicit v2 upgrade and external edits protected");
    }
    /// <summary>Parses all three generated flows with the actual AHK interpreter without triggering them.</summary>
    private static async Task Parse(string runtime, string root, VisualFlowDocument[] flows)
    {
        for (var index = 0; index < flows.Length; index++)
        {
            var source = VisualFlowGenerator.Generate(flows[index]);
            Require(VisualFlowCodec.Utf8.GetByteCount(source) < VisualFlowCodec.MaximumJsonBytes, "bounded generated source");
            var file = Path.Combine(root, "parse-" + index + ".ahk"); File.WriteAllText(file, "#Requires AutoHotkey v2.0\nExitApp 0\n" + source, VisualFlowCodec.Utf8);
            await Run(runtime, file, root);
        }
        Console.WriteLine("PASS: actual AHK v2 parses Shell, terminal, extraction, branches and window workflows");
    }
    /// <summary>Executes conditions and bounded window waiting only against a test-owned process and isolated logs.</summary>
    private static async Task Runtime(string runtime, string root)
    {
        var fixture = Path.Combine(root, "window fixture 中文.ahk"); File.WriteAllText(fixture, "#Requires AutoHotkey v2.0\ng := Gui(, \"AutoHotkeyUX capability probe\")\ng.AddText(, \"Test-owned window\")\ng.Show(\"NoActivate w180 h60\")\nSetTimer (() => ExitApp(0)), -2500\n", VisualFlowCodec.Utf8);
        var program = new FlowAction { Kind = FlowActionKind.OpenProgram, Value = runtime, Parameters = new() { Arguments = '"' + fixture + '"', WorkingDirectory = new() { Literal = root } } };
        var window = new FlowAction { Kind = FlowActionKind.WaitForWindow, Parameters = new() { Input = FlowInput.Reference(program.Id, FlowResultField.ProcessId), TimeoutMs = 1500 } };
        var flow = new VisualFlowDocument { SchemaVersion = 2, Actions = [program, window] };
        var marker = Path.Combine(root, "runtime-marker.txt"); var source = VisualFlowGenerator.Generate(flow);
        var file = Path.Combine(root, "runtime.ahk");
        var quotedInput = "测试 \"quoted\" C:\\end\\";
        File.WriteAllText(file, "#Requires AutoHotkey v2.0\nFlowRun()\nif !FlowCondition(" + VisualFlowGenerator.Literal(root) + ", \"IsFolder\", \"\") || !FlowCondition(\"ZIP\", \"ExtensionEquals\", \"zip\") || FlowAbsolutePath(\"relative\\folder\") || !FlowAbsolutePath(\"\\\\nas.example\\share\\folder\")\n    ExitApp 2\nFileAppend FlowQuoteArgument(" + VisualFlowGenerator.Literal(quotedInput) + "), " + VisualFlowGenerator.Literal(marker) + ", \"UTF-8\"\nExitApp 0\n" + source, VisualFlowCodec.Utf8);
        await Run(runtime, file, root); Require(File.Exists(marker) && File.ReadAllText(marker) == '"' + "测试 \\" + '"' + "quoted\\" + '"' + " C:\\end\\\\" + '"'
            && !File.Exists(Path.Combine(root, "AutoHotkeyUX.Modern", "state", "workflows.log")), "owned program PID, quoting, extension and path guards succeeded");
        var bad = new FlowAction { Kind = FlowActionKind.WaitForWindow, Parameters = new() { Input = new() { Literal = "AutoHotkeyUX-no-such-program.exe" }, TimeoutMs = 50 } };
        var timeoutFile = Path.Combine(root, "timeout.ahk");
        File.WriteAllText(timeoutFile, "#Requires AutoHotkey v2.0\nFlowRun()\nExitApp 0\n" + VisualFlowGenerator.Generate(flow with { Actions = [bad] }), VisualFlowCodec.Utf8);
        await Run(runtime, timeoutFile, root); Require(File.ReadAllText(Path.Combine(root, "AutoHotkeyUX.Modern", "state", "workflows.log")).Contains("Timed out"), "timeout observable");
        Console.WriteLine("PASS: real program PID/result/window wait, Unicode arguments and timeout log");
    }
    /// <summary>Bounds test-owned interpreter lifetime and reports actual parse/runtime output.</summary>
    private static async Task Run(string runtime, string path, string root)
    {
        var start = new ProcessStartInfo(runtime) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("/ErrorStdOut=UTF-8"); start.ArgumentList.Add(path); start.Environment["LOCALAPPDATA"] = root;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (TimeoutException) { process.Kill(); throw new Exception("AHK probe timed out: " + path); }
        Require(process.ExitCode == 0, "AHK failed: " + await stdout + await stderr);
    }
    /// <summary>Requires the intended boundary to reject instead of swallowing a test assertion.</summary>
    private static void Reject(Action action, string expected)
    { try { action(); } catch (Exception ex) { Require(ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "Wrong rejection: " + ex.Message); return; } throw new Exception("Accepted invalid " + expected); }
    /// <summary>Reports a concrete failed capability boundary.</summary>
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
}
