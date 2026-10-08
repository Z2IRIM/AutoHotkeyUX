using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Checks the new language and legacy golden files without launching an interpreter or manager.</summary>
internal static class ConfigurableActionChecks
{
    /// <summary>Captures the pre-change v2 contract once, then exercises schema v3 acceptance.</summary>
    internal static void Model(string root, bool captureBaseline)
    {
        var legacy = VisualFlowExamples.Explorer() with { Revision = 1 };
        var source = VisualFlowGenerator.Generate(legacy);
        legacy = legacy with { SourceSha256 = VisualFlowGenerator.Hash(VisualFlowCodec.Utf8.GetBytes(source)) };
        var fixture = Path.Combine(root, "legacy-v2.ahk");
        if (captureBaseline)
        {
            File.WriteAllText(fixture, source, VisualFlowCodec.Utf8);
            File.WriteAllText(fixture + ".flow.json", VisualFlowCodec.Encode(legacy), VisualFlowCodec.Utf8);
            Console.WriteLine("PASS: captured exact pre-change v2 source and document");
        }
        else
        {
            var old = VisualFlowCodec.Decode(File.ReadAllText(fixture + ".flow.json"));
            Check(VisualFlowGenerator.Generate(old) == File.ReadAllText(fixture), "v2 source remains byte-compatible after new helpers");
        }
        var json = JsonNode.Parse(VisualFlowCodec.Encode(legacy))!;
        json["schemaVersion"] = 3;
        json["actions"] = JsonNode.Parse("""
            [{"id":"2fbb792c-66b8-480b-b725-536c1d2ec53f","kind":"OpenTerminal","value":"","folder":"Documents","delayMs":0,
              "parameters":{"input":{"kind":"Literal","literal":"C:\\Windows","stepId":"00000000-0000-0000-0000-000000000000","field":"Path"},
              "terminal":{"mode":"Custom","program":"WindowsPowerShell","position":"Below","width":1000,"height":600,"gap":16,"waitReady":true,"timeoutMs":4000}}}]
            """);
        var configured = VisualFlowCodec.Decode(json.ToJsonString());
        Check(configured.SchemaVersion == 3, "v3 configurable terminal is accepted");
        Check(VisualFlowCodec.Encode(configured).Contains("1000"), "configured terminal dimensions survive serialization");
        var generated = VisualFlowGenerator.Generate(configured);
        Check(generated.Contains("FlowTerminal3(") && generated.Contains("1000") && generated.Contains("below"), "v3 generator emits independent terminal parameters");
        Reject(() => VisualFlowCodec.Validate(configured with { SchemaVersion = 2 }), "v2 cannot smuggle configurable v3 settings");
        Reject(() => VisualFlowCodec.Validate(configured with { Actions = [new() { Kind = FlowActionKind.GetPathProperties,
            Parameters = new() { Input = new() { Literal = null! } } }] }), "null path input fails at the validation boundary");
        var terminal = configured.Actions[0];
        var activate = new FlowAction { Kind = FlowActionKind.ActivateWindow, Parameters = new() { Input = FlowInput.Reference(terminal.Id, FlowResultField.WindowId), TimeoutMs = 3000 } };
        VisualFlowCodec.Validate(configured with { Actions = [terminal, activate] });
        Console.WriteLine("PASS: ready terminal HWND can feed a later activation");
        Reject(() => VisualFlowCodec.Validate(configured with { Actions = [terminal with { Parameters = terminal.Parameters! with { Terminal = terminal.Parameters.Terminal! with { WaitReady = false } } }, activate] }), "nonwaiting terminal cannot supply a ready HWND");
        Reject(() => VisualFlowCodec.Validate(configured with { Actions = [activate, terminal] }), "later result references are rejected");
        var session = new VisualEditorSession(); session.Load(configured);
        session.Replace(configured with { Actions = [terminal with { Parameters = terminal.Parameters! with { Terminal = terminal.Parameters.Terminal! with { Width = 1100 } } }] });
        Check(session.CanUndo && session.Document.Actions[0].Parameters!.Terminal!.Width == 1100, "typed operation edits enter history");
        session.Undo(); Check(session.Document.Actions[0].Parameters!.Terminal!.Width == 1000, "undo restores typed operation settings");
        var notice = new FlowAction { Kind = FlowActionKind.Notify, Parameters = new() { Notification = new() { Message = new() { Parts = [FlowInput.Reference(terminal.Id, FlowResultField.Directory)] } } } };
        var branch = new FlowAction { Kind = FlowActionKind.IfElse, Parameters = new() { Input = new() { Literal = @"C:\Windows" }, Condition = FlowConditionKind.IsFolder, Then = [terminal] } };
        Reject(() => VisualFlowCodec.Validate(configured with { Actions = [branch, notice] }), "composed text cannot read another branch's result");
        Protocol();
        LegacyMigration();
        Presets(root, configured);
        Replacement();
        QueueAndReports(root);
    }

    /// <summary>Reports only assertions backed by the current invocation.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    /// <summary>Exercises closed protocol inputs without constructing a native endpoint or performing extraction.</summary>
    private static void Protocol()
    {
        var request = new VisualExtractionRequest { RequestId = Guid.NewGuid(), FlowId = Guid.NewGuid(), RunId = Guid.NewGuid(), StepId = Guid.NewGuid(),
            Source = @"C:\Downloads\Sample.zip", Destination = @"C:\Downloads", Name = "Sample extracted", Collision = FlowArchiveCollision.Error };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        var body = JsonSerializer.Serialize(request, options);
        Check(VisualFlowRequestCodec.Extraction(body) == request, "IPC preserves immutable extraction options and request identity");
        Reject(() => VisualFlowRequestCodec.Extraction(body[..^1] + ",\"name\":\"other\"}"), "duplicate IPC fields are rejected");
        Reject(() => VisualFlowRequestCodec.Extraction(JsonSerializer.Serialize(request with { Name = "..\\outside" }, options)), "composed extraction names cannot traverse directories");
        Reject(() => VisualFlowRequestCodec.Extraction(JsonSerializer.Serialize(request with { Name = "CON" }, options)), "reserved Windows output names are rejected");
        Reject(() => VisualFlowRequestCodec.Extraction(body.Replace("\"source\":\"C:\\\\Downloads\\\\Sample.zip\"", "\"source\":null")), "null IPC paths are rejected predictably");
        var item = new VisualFlowEvent { FlowId = request.FlowId, RunId = request.RunId, StepId = request.StepId, StepName = "Extract archive", State = "succeeded", Detail = @"C:\Downloads\Sample extracted", ElapsedMs = 125 };
        Check(VisualFlowRequestCodec.Event(JsonSerializer.Serialize(item, options)) == item, "step feedback retains document/run/step identity and output");
    }

    /// <summary>Checks portability, stored options, fresh identities and branch-local reference repair.</summary>
    private static void Presets(string root, VisualFlowDocument configured)
    {
        var source = VisualFlowExamples.Action(FlowActionKind.GetCurrentDirectory);
        var terminal = configured.Actions[0] with { Parameters = configured.Actions[0].Parameters! with { Input = FlowInput.Reference(source.Id, FlowResultField.Directory) } };
        var original = configured with { Actions = [source, terminal] };
        var preset = VisualActionPresets.Capture(original, terminal.Id, "My terminal");
        Check(preset.Slots.Length == 1 && !VisualFlowSchema.Inputs(preset.Template).Any(input => input.StepId != Guid.Empty), "preset capture removes original producer identities");
        Reject(() => VisualActionPresets.Bind(preset, new Dictionary<int, FlowInput>()), "portable preset requires every input to be rebound");
        var other = VisualFlowExamples.Action(FlowActionKind.GetCurrentDirectory);
        var bound = VisualActionPresets.Bind(preset, new Dictionary<int, FlowInput> { [0] = FlowInput.Reference(other.Id, FlowResultField.Directory) });
        VisualFlowCodec.Validate(original with { Actions = [other, bound] });
        Check(bound.Id != terminal.Id && bound.Parameters!.Input!.StepId == other.Id && bound.Parameters.Terminal!.Width == 1000, "preset rebind retains custom geometry and allocates a new node");
        var library = new VisualActionPresets(Path.Combine(root, "preset-library"));
        library.Save(preset); library.Save(preset with { Name = "Renamed terminal" });
        Check(library.Read().Single().Name == "Renamed terminal", "preset rename and disk roundtrip preserve one library entry");
        library.Delete(preset.Id); Check(library.Read().Count == 0, "preset delete preserves an empty valid library");
        Reject(() => VisualActionPresets.Bind(preset with { Slots = [null!] }, new Dictionary<int, FlowInput>()), "malformed preset slots fail predictably");
        var condition = new FlowAction { Kind = FlowActionKind.IfElse, Parameters = new() { Input = new() { Literal = @"C:\Windows" }, Condition = FlowConditionKind.IsFolder,
            Then = [source, terminal] } };
        var duplicate = VisualActionPresets.Duplicate(condition);
        var nodes = duplicate.Parameters!.Then;
        Check(duplicate.Id != condition.Id && nodes[0].Id != source.Id && nodes[1].Parameters!.Input!.StepId == nodes[0].Id, "duplicated subtrees remap internal source references");
        foreach (var example in new[] { VisualFlowExamples.Terminal(), VisualFlowExamples.Extract(), VisualFlowExamples.ExplorerV3() })
        {
            VisualFlowCodec.Validate(example);
            Check(VisualFlowGenerator.Generate(example).Contains("FlowAnnounceReady()"), "editable v3 example generates a ready-aware workflow");
        }
        File.WriteAllText(Path.Combine(root, "example-terminal-v3.ahk"), VisualFlowGenerator.Generate(VisualFlowExamples.Terminal()), VisualFlowCodec.Utf8);
        File.WriteAllText(Path.Combine(root, "example-explorer-v3.ahk"), VisualFlowGenerator.Generate(VisualFlowExamples.ExplorerV3()), VisualFlowCodec.Utf8);
    }

    /// <summary>Preserves operation and failure behavior when a cosmetic edit first upgrades a legacy document.</summary>
    private static void LegacyMigration()
    {
        var legacy = VisualFlowExamples.Explorer(); var upgraded = VisualFlowSchema.Upgrade(legacy);
        var actions = VisualFlowTree.Walk(upgraded.Actions).Select(item => item.Action).ToArray();
        var extraction = actions.Single(action => action.Kind == FlowActionKind.ExtractArchive).Parameters!.Extraction!;
        Check(extraction.Mode == FlowConfigurationMode.Custom && extraction.Destination == FlowArchiveDestination.BesideArchive, "legacy empty extraction destination still means beside the archive after upgrade");
        Check(actions.Single(action => action.Kind == FlowActionKind.GetClickedObject).Parameters?.Context?.Missing == FlowMissingTarget.Error
            && actions.All(action => action.Parameters?.Failure?.Notify == true), "legacy missing-target and error visibility survive upgrade");
        Check(actions.Single(action => action.Kind == FlowActionKind.OpenTerminal).Parameters?.Terminal?.WaitReady == false, "legacy terminal keeps its asynchronous sequencing until readiness is explicitly selected");
        var fresh = VisualFlowSchema.Upgrade(VisualFlowExamples.ExplorerV3());
        Check(VisualFlowTree.Walk(fresh.Actions).All(item => item.Action.Parameters?.Failure?.Notify != true)
            && fresh.Actions[0].Parameters?.Context?.Missing == FlowMissingTarget.StopSilently, "new v3 workflows retain their deliberate quiet defaults");
    }

    /// <summary>Exercises transaction compensation without starting processes or changing real startup settings.</summary>
    private static void Replacement()
    {
        var running = new RunningScriptSession("fixture.ahk", 456, DateTime.UtcNow, DateTime.UtcNow, ScriptState.Running);
        var refused = false;
        try { VisualWorkflowActivationService.RequireStoppedCandidate(running.ScriptPath, [running]); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "replacement requires an explicit Stop of an already running candidate and preserves its snapshot");
        var enabled = true; var login = false; var attemptRunning = false;
        try
        {
            VisualWorkflowActivationService.Replace(() => enabled = false,
                () => { attemptRunning = true; throw new InvalidOperationException("simulated startup failure"); },
                () => login = true, () => attemptRunning = false, () => login = false, () => enabled = true);
            throw new Exception("FAIL: expected startup failure");
        }
        catch (InvalidOperationException) { Check(enabled && !login && !attemptRunning, "failed replacement restores built-in and login choices and stops only its attempt"); }
        var result = VisualWorkflowActivationService.Replace(() => enabled = false,
            () => new("fixture.ahk", 123, null, DateTime.UtcNow, ScriptState.Running), () => login = true,
            () => throw new Exception("unexpected compensation"), () => login = false, () => enabled = true);
        Check(!enabled && login && result.State == ScriptState.Running, "ready replacement commits startup only after launch confirmation");
    }

    /// <summary>Checks acceptance, exact identity deduplication, saturation, drain and publication failure with injected work.</summary>
    private static void QueueAndReports(string root)
    {
        var owner = Path.Combine(root, "queue-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(owner);
        ServiceDiagnostics.UseIsolatedDirectory(Path.Combine(owner, "logs"));
        var settings = new AutoHotkeySettings(@"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N"));
        using var release = new ManualResetEventSlim(false); using var entered = new ManualResetEventSlim(false); var calls = 0;
        var queue = new ShortcutCommandService(IntPtr.Zero, settings, (_, _) => { }, workflowResultRoot: Path.Combine(owner, "workflow-results"), workflowExtract: request =>
        { Interlocked.Increment(ref calls); entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("fixture wait"); return owner; });
        var serializer = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        VisualExtractionRequest Request() => new() { RequestId = Guid.NewGuid(), FlowId = Guid.NewGuid(), RunId = Guid.NewGuid(), StepId = Guid.NewGuid(), Source = Path.Combine(owner, "fixture.zip") };
        string Payload(VisualExtractionRequest request) => "flow-extract\n" + queue.Token + "\n" + JsonSerializer.Serialize(request, serializer);
        var first = Request();
        try
        {
            Check(queue.TryEnqueue(Payload(first)) == 1 && entered.Wait(TimeSpan.FromSeconds(2)), "explicit request reaches worker while acceptance remains nonblocking");
            var competitor = new ShortcutCommandService(IntPtr.Zero, settings, (_, _) => { }, workflowResultRoot: Path.Combine(owner, "workflow-results"), workflowExtract: _ => throw new Exception("must not execute"));
            competitor.TryEnqueue("flow-extract\n" + competitor.Token + "\n" + JsonSerializer.Serialize(first with { Name = "Changed" }, serializer));
            competitor.DrainAsync().GetAwaiter().GetResult();
            Check(!File.Exists(VisualFlowReports.PathFor(Path.Combine(owner, "workflow-results"), first.RequestId)), "another owner with different options cannot poison the active request's result");
            Check(competitor.TryEnqueue("flow-status\n" + competitor.Token + "\n" + first.RequestId) == 6, "cross-owner identity conflict has a bounded rejection status");
            Check(queue.TryEnqueue(Payload(first)) == 2 && queue.TryEnqueue(Payload(first with { Name = "Changed" })) == 0, "request identity deduplicates exact options and rejects changed options");
            Check(Enumerable.Range(0, 4).All(_ => queue.TryEnqueue(Payload(Request())) == 1) && queue.TryEnqueue(Payload(Request())) == 3, "shared workflow queue rejects overflow without accepting extra work");
        }
        finally { release.Set(); queue.DrainAsync().GetAwaiter().GetResult(); }
        var result = VisualFlowReports.PathFor(Path.Combine(owner, "workflow-results"), first.RequestId);
        Check(calls == 5 && File.ReadAllText(result) == "ok\n" + owner, "drain completes every accepted job once and publishes its exact result");
        Check(!VisualFlowReports.Begin(Path.Combine(owner, "workflow-results"), first), "durable exact receipt prevents repeated work after manager restart");
        Reject(() => VisualFlowReports.Begin(Path.Combine(owner, "workflow-results"), first with { Name = "Changed" }), "durable receipt rejects reuse with different options");
        var blocked = Path.Combine(owner, "blocked-owner"); File.WriteAllText(blocked, "keep");
        var failure = new ShortcutCommandService(IntPtr.Zero, settings, (_, _) => { }, workflowResultRoot: blocked, workflowExtract: _ => throw new Exception("must not execute"));
        var bad = Request();
        Check(failure.TryEnqueue("flow-extract\n" + failure.Token + "\n" + JsonSerializer.Serialize(bad, serializer)) == 1, "publication failure probe was accepted");
        failure.DrainAsync().GetAwaiter().GetResult();
        Check(failure.TryEnqueue("flow-status\n" + failure.Token + "\n" + bad.RequestId) == 5 && File.ReadAllText(blocked) == "keep", "result publication failure is queryable and preserves the blocking file");
    }

    /// <summary>Requires an intentional validation exception for unsupported semantic data.</summary>
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("FAIL: " + message);
    }
}
