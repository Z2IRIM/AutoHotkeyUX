using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Diagnostics;
using System.Text;

/// <summary>Checks new workflow boundaries using actual filesystem bytes and the bundled v2 interpreter.</summary>
internal static class VisualFlowChecks
{
    // Runs each independent boundary even on RED so failures describe missing behavior rather than compilation.
    internal static async Task RunAsync(string runtime, string root)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (name, check) in new (string, Action)[]
        {
            ("literal escaping and scoped generation", Generation),
            ("invalid workflow parameters rejected", Validation),
            ("strict sidecar schema round-trip", Codec),
            ("collision-safe pair creation", () => Create(root)),
            ("source and sidecar external changes protected", () => Conflicts(root)),
            ("locked target and pending recovery preserve source", () => Recovery(root)),
            ("second-file write failure rolls back both files", () => Rollback(root)),
            ("unfinished workflow cannot run or restart", () => PendingExecution(runtime, root)),
            ("workflow ordering and bounded history", History)
        })
        {
            try { check(); Console.WriteLine("PASS: " + name); count++; }
            catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL: " + name + " — " + ex.Message); }
        }
        if (failures.Count == 0)
        {
            await Runtime(runtime, root);
            await QueuedSave(root);
            count += 2;
        }
        if (failures.Count > 0) throw new Exception(string.Join(", ", failures));
        Console.WriteLine($"VISUAL: {count}/{count} boundary groups passed");
    }

    // Fails if a user literal escapes into executable AHK or the application condition scopes the wrong trigger.
    private static void Generation()
    {
        var flow = Flow(new FlowAction() { Kind = FlowActionKind.SendText, Value = "中`\"文\r\n\t" });
        var source = VisualFlowGenerator.Generate(flow);
        Check(source.Contains("SendText \"中```\"文`r`n`t\"", StringComparison.Ordinal), "literal quote/backtick/control escaping");
        Check(source.Contains("^!d:: {", StringComparison.Ordinal), "Ctrl+Alt+D trigger");
        var scoped = flow with { Trigger = flow.Trigger with { Application = "explorer.exe" } };
        Check(VisualFlowGenerator.Generate(scoped).Contains("#HotIf WinActive(\"ahk_exe explorer.exe\")"), "hotkey scope");
        var startup = scoped with { Trigger = scoped.Trigger with { Kind = FlowTriggerKind.Startup } };
        var generated = VisualFlowGenerator.Generate(startup);
        Check(generated.Contains("if WinActive(\"ahk_exe explorer.exe\") {") && !generated.Contains("::"), "startup scope");
    }

    // Fails if malformed or unbounded user input is accepted as executable workflow data.
    private static void Validation()
    {
        foreach (var action in new FlowAction[]
        {
            new() { Kind = FlowActionKind.OpenWebsite, Value = "file:///C:/test" },
            new() { Kind = FlowActionKind.OpenWebsite, Value = "https://" },
            new() { Kind = FlowActionKind.Wait, DelayMs = -1 },
            new() { Kind = FlowActionKind.Wait, DelayMs = 60001 },
            new() { Kind = FlowActionKind.SendKeys, Value = "{Run calc}" },
            new() { Kind = FlowActionKind.OpenFolder, Folder = FlowFolderKind.Custom, Value = "relative" },
            new() { Kind = FlowActionKind.OpenProgram, Value = "" },
            new() { Kind = FlowActionKind.SendText, Value = new string('x', 4097) },
            new() { Kind = (FlowActionKind)99 },
            new() { Kind = FlowActionKind.SendText, Value = null! }
        }) Reject(() => VisualFlowCodec.Validate(Flow(action)), "invalid action");
        Reject(() => VisualFlowCodec.Validate(Flow() with { Trigger = new() { Key = "D::Run" } }), "hotkey injection");
        Reject(() => VisualFlowCodec.Validate(Flow() with { Trigger = new() { Application = "x.exe\nRun" } }), "scope injection");
        Reject(() => VisualFlowCodec.Validate(Flow() with { Actions = Enumerable.Range(0, 25).Select(_ => new FlowAction { Kind = FlowActionKind.Wait }).ToArray() }), "action bound");
    }

    // Fails if unknown future fields/types or missing required fields can silently lose semantics when reopened.
    private static void Codec()
    {
        var flow = Flow(new FlowAction() { Kind = FlowActionKind.Wait, DelayMs = 500 }) with { Revision = 1, SourceSha256 = new string('A', 64) };
        var json = VisualFlowCodec.Encode(flow);
        Check(VisualFlowCodec.Decode(json).Actions[0].DelayMs == 500, "round-trip");
        Reject(() => VisualFlowCodec.Decode(json[..^1] + ",\"future\":true}"), "unknown field");
        Reject(() => VisualFlowCodec.Decode(json.Replace("\"Wait\"", "99")), "numeric enum");
        Reject(() => VisualFlowCodec.Decode(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9")), "future schema");
        Reject(() => VisualFlowCodec.Decode("{}"), "missing fields");
        Reject(() => VisualFlowCodec.Decode(new string(' ', 262145)), "JSON bound");
    }

    // Fails if a name collision on either file overwrites an existing script or loses its companion.
    private static void Create(string root)
    {
        var folder = Fresh(root, "create");
        File.WriteAllText(Path.Combine(folder, "same.ahk.flow.json"), "keep");
        var store = new VisualFlowStore();
        var first = store.Create(folder, "same.ahk", Flow());
        Check(Path.GetFileName(first.ScriptPath) == "same-1.ahk", "sidecar collision suffix");
        Check(File.ReadAllText(Path.Combine(folder, "same.ahk.flow.json")) == "keep", "orphan preserved");
        var names = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => new VisualFlowStore().Create(folder, "same", Flow())))).GetAwaiter().GetResult();
        Check(names.Select(value => value.ScriptPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "concurrent creation unique");
        Check(store.Open(first.ScriptPath).Document.Revision == 1, "pair reopens");
        Reject(() => store.Create(folder, "CON", Flow()), "reserved device name");
    }

    // Fails if update trusts metadata and overwrites either raw file after another editor has changed it.
    private static void Conflicts(string root)
    {
        var folder = Fresh(root, "conflicts");
        var store = new VisualFlowStore();
        var opened = store.Create(folder, "source", Flow());
        File.AppendAllText(opened.ScriptPath, "; manual edit\n");
        var bytes = File.ReadAllBytes(opened.ScriptPath);
        Reject(() => store.Update(opened, Flow()), "source changed");
        Check(bytes.SequenceEqual(File.ReadAllBytes(opened.ScriptPath)), "manual source preserved");
        Reject(() => store.Open(opened.ScriptPath), "modified source cannot masquerade as visual");
        var side = store.Create(folder, "side", Flow());
        File.AppendAllText(side.ScriptPath + ".flow.json", " ");
        Reject(() => store.Update(side, Flow()), "sidecar raw bytes changed");
        var saved = store.Create(folder, "saved", Flow());
        var updated = store.Update(saved, saved.Document with { Actions = [new() { Kind = FlowActionKind.Wait, DelayMs = 123 }] });
        Check(updated.Document.Revision == 2 && store.Open(saved.ScriptPath).Document.Actions[0].DelayMs == 123, "revision and emitted source update");
        Reject(() => store.Update(saved, Flow()), "stale revision retry");
    }

    // Fails if a denied write or interrupted transaction is treated as a normal editable script.
    private static void Recovery(string root)
    {
        var store = new VisualFlowStore();
        var opened = store.Create(Fresh(root, "recovery"), "locked", Flow());
        var original = File.ReadAllBytes(opened.ScriptPath);
        using (var locked = new FileStream(opened.ScriptPath + ".flow.json", FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => store.Update(opened, Flow()), "locked sidecar");
        Check(original.SequenceEqual(File.ReadAllBytes(opened.ScriptPath)), "write failure preserves source");
        Directory.CreateDirectory(opened.ScriptPath + ".flow.pending");
        File.WriteAllText(Path.Combine(opened.ScriptPath + ".flow.pending", "source.before"), "recovery copy");
        Reject(() => store.Open(opened.ScriptPath), "interrupted transaction blocks editing");
        Reject(() => store.Update(opened, Flow()), "interrupted transaction blocks overwriting");
        Check(original.SequenceEqual(File.ReadAllBytes(opened.ScriptPath)), "recovery does not guess");
    }

    // Fails if sorting loses stable action identity or undo/redo changes the document being generated.
    private static void History()
    {
        var a = new FlowAction { Kind = FlowActionKind.Wait, DelayMs = 1 };
        var b = new FlowAction { Kind = FlowActionKind.Wait, DelayMs = 2 };
        var session = new VisualEditorSession();
        session.Replace(Flow(a, b));
        session.Move(a.Id, 1);
        Check(session.Document.Actions[0].Id == b.Id, "move persisted");
        session.Undo(); Check(session.Document.Actions[0].Id == a.Id, "undo move");
        session.Redo(); Check(session.Document.Actions[0].Id == b.Id, "redo move");
        session.Move(Guid.Empty, 0); Check(session.Document.Actions.Length == 2, "missing step is no-op");
    }

    // Fails if a partial companion write can leave new source paired with the old document after an ordinary failure.
    private static void Rollback(string root)
    {
        var folder = Fresh(root, "rollback");
        var path = Path.Combine(folder, "pair.ahk");
        var before = Encoding.UTF8.GetBytes("original source");
        var companion = Encoding.UTF8.GetBytes("original workflow");
        File.WriteAllBytes(path, before); File.WriteAllBytes(path + ".flow.json", companion);
        var pending = new VisualFlowPending(path, before, companion, Encoding.UTF8.GetBytes("new source"), Encoding.UTF8.GetBytes("new workflow"));
        pending.Stage();
        using (var source = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var side = new FailOnceStream(path + ".flow.json"))
            Reject(() => pending.Apply(source, side), "injected second-file write failure");
        Check(File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllBytes(path + ".flow.json").SequenceEqual(companion), "both original byte sequences restored");
        Check(!Directory.Exists(path + ".flow.pending"), "confirmed rollback clears only its staging copies");
    }

    // Fails if exit discards an accepted save or later submits are silently accepted after draining begins.
    private static async Task QueuedSave(string root)
    {
        var folder = Fresh(root, "queue");
        var store = new VisualFlowStore();
        var saving = store.SaveAsync(folder, "accepted", Flow(), null);
        await store.DrainAsync();
        var saved = await saving;
        Check(store.Open(saved.ScriptPath).Document.Revision == 1, "accepted save completed before drain returned");
        Reject(() => store.SaveAsync(folder, "late", Flow(), null).GetAwaiter().GetResult(), "submit after drain");
        Console.WriteLine("PASS: accepted saves drain before exit; late submits rejected");
    }

    // Fails if interrupted or in-flight workflow bytes can run, or a denied restart terminates an existing interpreter.
    private static void PendingExecution(string runtime, string root)
    {
        var folder = Fresh(root, "pending-run");
        var path = Path.Combine(folder, "pending.ahk");
        File.WriteAllText(path, "#Requires AutoHotkey v2.0\nPersistent\n");
        var pending = path + ".flow.pending";
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(folder, "sessions.json")));
        try
        {
            Directory.CreateDirectory(pending);
            Check(execution.Run(path).State == ScriptState.Failed, "unfinished source was launched");
            Directory.Delete(pending);
            var running = execution.Run(path);
            Check(running.State == ScriptState.Running, "completed source did not launch");
            Directory.CreateDirectory(pending);
            Reject(() => execution.Restart(path), "restart while workflow pending");
            Check(execution.Snapshot().Single().ProcessId == running.ProcessId && execution.Snapshot().Single().State == ScriptState.Running, "denied restart stopped the running interpreter");
        }
        finally { execution.Stop(path); }
    }

    // Parses every action without executing it, then verifies generated literal text in a test-owned Notepad window.
    private static async Task Runtime(string runtime, string root)
    {
        var flow = Flow(new FlowAction() { Kind = FlowActionKind.OpenProgram, Value = "notepad.exe" },
            new() { Kind = FlowActionKind.OpenFolder, Folder = FlowFolderKind.Documents },
            new() { Kind = FlowActionKind.OpenFolder, Folder = FlowFolderKind.Custom, Value = @"C:\测试 path" },
            new() { Kind = FlowActionKind.OpenWebsite, Value = "https://example.com/?a=1&b=2" },
            new() { Kind = FlowActionKind.SendText, Value = "中`\"\n" },
            new() { Kind = FlowActionKind.SendKeys, Value = "Ctrl+C" },
            new() { Kind = FlowActionKind.Wait, DelayMs = 500 });
        foreach (var trigger in new[] { FlowTriggerKind.Hotkey, FlowTriggerKind.Startup })
        {
            var path = Path.Combine(root, "parse-" + trigger + ".ahk");
            File.WriteAllText(path, "ExitApp 0\n" + VisualFlowGenerator.Generate(flow with { Trigger = flow.Trigger with { Kind = trigger, Application = "explorer.exe" } }), new UTF8Encoding(false));
            var start = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("/ErrorStdOut=UTF-8"); start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(process.ExitCode == 0, "AHK syntax: " + await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync());
        }
        Console.WriteLine("PASS: actual AHK v2 parses all supported actions and both scoped triggers");
        var marker = Path.Combine(root, "run-marker.txt");
        var runtimePath = Path.Combine(root, "generated-runtime.ahk");
        var startup = Flow(new FlowAction { Kind = FlowActionKind.Wait, DelayMs = 25 }) with { Trigger = new() { Kind = FlowTriggerKind.Startup } };
        File.WriteAllText(runtimePath, VisualFlowGenerator.Generate(startup) + "FileAppend \"ran`n\", " + Quote(marker) + ", \"UTF-8\"\nPersistent\n", new UTF8Encoding(false));
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(root, "visual-sessions.json")));
        try
        {
            var first = execution.Run(runtimePath);
            await Until(() => File.Exists(marker) && File.ReadAllLines(marker).Length == 1);
            var second = execution.Restart(runtimePath);
            await Until(() => File.ReadAllLines(marker).Length == 2);
            Check(second.ProcessId != first.ProcessId && second.State == ScriptState.Running, "generated startup works with existing Run/Restart owner");
        }
        finally { execution.Stop(runtimePath); }
        Console.WriteLine("PASS: generated startup executes a safe wait with owned Run/Restart");
    }

    // Builds a bounded test workflow; no test actions execute in the user's desktop.
    private static VisualFlowDocument Flow(params FlowAction[] actions) => new() { Actions = actions.Length == 0 ? [new() { Kind = FlowActionKind.Wait, DelayMs = 500 }] : actions };
    // Allocates a disposable directory under the supplied verification root.
    private static string Fresh(string root, string label) { var path = Path.Combine(root, label + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    // Asserts rejection without accidentally treating a failed assertion as a successful rejection.
    private static void Reject(Action action, string description) { try { action(); } catch (Exception ex) when (ex is not NotImplementedException) { return; } throw new Exception("Accepted " + description); }
    // Reports a concrete broken boundary.
    private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); }

    // Uses literal quoting for the test's own marker path, independently of the production generator.
    private static string Quote(string value) => "\"" + value.Replace("`", "``").Replace("\"", "`\"") + "\"";
    // Waits only for this probe's bounded filesystem side effect.
    private static async Task Until(Func<bool> predicate) { var watch = Stopwatch.StartNew(); while (!predicate() && watch.ElapsedMilliseconds < 5000) await Task.Delay(25); Check(predicate(), "runtime marker timed out"); }

    // Injects one real partial filesystem write failure; rollback still uses the same physical file handle.
    private sealed class FailOnceStream(string path) : FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
    {
        private bool _failed;
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (!_failed) { _failed = true; base.Write(buffer[..Math.Min(3, buffer.Length)]); throw new IOException("Injected partial disk write failure"); } base.Write(buffer); }
    }
}
