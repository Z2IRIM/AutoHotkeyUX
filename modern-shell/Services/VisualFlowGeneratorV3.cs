using AutoHotkeyUX.Modern.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Emits versioned configurable operations over the existing Shell and terminal owners.</summary>
internal static class VisualFlowGeneratorV3
{
    private static readonly ConcurrentDictionary<string, string> Helpers = new(StringComparer.Ordinal);
    /// <summary>Builds a deterministic invocation-local workflow with bounded event-driven dispatch.</summary>
    internal static string Generate(VisualFlowDocument flow)
    {
        var all = VisualFlowTree.Walk(flow.Actions).Select(row => row.Action).ToArray();
        var terminal = all.Any(action => action.Kind == FlowActionKind.OpenTerminal);
        var shell = terminal || flow.Trigger.Scope == FlowScopeKind.ExplorerDesktop || all.Any(action => action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory);
        var mouse = flow.Trigger.Kind == FlowTriggerKind.Hotkey && flow.Trigger.Key.EndsWith("Button", StringComparison.Ordinal);
        var captures = all.Where(action => action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory)
            .Select(action => action.Kind == FlowActionKind.GetClickedObject ? "clicked" : action.Kind == FlowActionKind.GetSelectedObject ? "selected" : "directory").Distinct();
        var dispatch = "FlowDispatch(" + L(mouse ? flow.Trigger.Key : "") + ", [" + string.Join(", ", captures.Select(L)) + "])";
        var scope = flow.Trigger.Scope switch
        {
            FlowScopeKind.ActiveApplication => "WinActive(" + L("ahk_exe " + flow.Trigger.Application) + ")",
            FlowScopeKind.ExplorerDesktop => "FlowShellContext(" + B(mouse) + ")", _ => ""
        };
        var source = new StringBuilder("; AutoHotkey UX visual workflow v3\r\n; Workflow: " + flow.Id.ToString("D")
            + "\r\n#Requires AutoHotkey v2.0\r\n#SingleInstance Ignore\r\nCoordMode \"Mouse\", \"Screen\"\r\nSetWinDelay 0\r\n"
            + "global FlowIdentity := " + L(flow.Id.ToString("D")) + "\r\nFlowAnnounceReady()\r\n");
        if (flow.Trigger.Kind == FlowTriggerKind.Hotkey)
        {
            if (scope.Length > 0) source.Append("#HotIf ").Append(scope).Append("\r\n");
            if (mouse) source.Append('~');
            foreach (var (flag, symbol) in new[] { (FlowModifiers.Ctrl, "^"), (FlowModifiers.Alt, "!"), (FlowModifiers.Shift, "+"), (FlowModifiers.Win, "#") })
                if (flow.Trigger.Modifiers.HasFlag(flag)) source.Append(symbol);
            source.Append(flow.Trigger.Key).Append(":: ").Append(dispatch).Append("\r\n");
            if (scope.Length > 0) source.Append("#HotIf\r\n");
        }
        else source.Append(scope.Length > 0 ? "if " + scope + "\r\n    " : "").Append(dispatch).Append("\r\n");
        source.Append("\r\n; Executes one captured invocation; result records never escape this call.\r\nFlowRun(context) {\r\n    try {\r\n");
        if (flow.Trigger.Scope == FlowScopeKind.ActiveApplication) source.Append("        if !").Append(scope).Append("\r\n            throw FlowStopped(\"The scoped application is no longer active.\")\r\n");
        WriteActions(source, flow.Actions, "        ");
        source.Append("    } catch FlowStopped as stopped {\r\n        FlowEvent(context, context.StepId, context.StepName, \"stopped\", stopped.Message)\r\n    } catch Error as failure {\r\n        FlowEvent(context, context.StepId, context.StepName, \"failed\", failure.Message)\r\n        if context.NotifyErrors\r\n            TrayTip failure.Message, \"AutoHotkey workflow stopped\"\r\n    }\r\n}\r\n\r\n");
        source.Append(Resource("VisualFlow.Runtime.ahk"));
        source.Append(Resource("VisualFlow.V3.Scheduler.ahk")).Append(Resource("VisualFlow.V3.Operations.ahk")).Append(Resource("VisualFlow.V3.Protocol.ahk"));
        if (shell) source.Append(Resource("ExplorerShortcuts.Shell.ahk"));
        else source.Append("GetShellGestureContext() => false\r\nGetShellView(context) => false\r\nGetFileViewHit(x, y, desktop := false) => false\r\nResolveHitPath(view, hit) => \"\"\r\n");
        var actions = Resource("ExplorerShortcuts.Actions.ahk");
        source.Append(terminal ? actions : actions[..actions.IndexOf("; Places", StringComparison.Ordinal)]
            + "OpenTerminal(directory, x, y, report := \"\", preferences := false, completion := false) => false\r\n");
        var preferences = Resource("ExplorerShortcuts.Preferences.ahk");
        source.Append(preferences[..preferences.IndexOf("; Loads the snapshot once, registers only", StringComparison.Ordinal)]);
        return source.ToString();
    }

    /// <summary>Writes statements and exact per-node result events, including conditional branch decisions.</summary>
    private static void WriteActions(StringBuilder source, FlowAction[] actions, string indent)
    {
        foreach (var action in actions)
        {
            var p = action.Parameters; var input = Input(p?.Input, action.Value); var result = Result(action.Id);
            var id = L(action.Id.ToString("D")); var name = L(action.DisplayName ?? action.Kind.ToString());
            source.Append(indent).Append("context.StepId := ").Append(id).Append(", context.StepName := ").Append(name)
                .Append(", context.NotifyErrors := ").Append(B(p?.Failure?.Notify == true)).Append("\r\n");
            WriteEvent(source, indent, id, name, "started", L(""));
            if (action.Kind == FlowActionKind.IfElse)
            {
                source.Append(indent).Append("if FlowCondition3(").Append(input).Append(", ").Append(L(p!.Condition.ToString())).Append(", ").Append(L(p.Comparison)).Append(") {\r\n");
                WriteEvent(source, indent + "    ", id, name, "branch", L("True")); WriteActions(source, p.Then, indent + "    ");
                source.Append(indent).Append("} else {\r\n"); WriteEvent(source, indent + "    ", id, name, "branch", L("Else"));
                WriteActions(source, p.Else, indent + "    "); source.Append(indent).Append("}\r\n");
                WriteEvent(source, indent, id, name, "succeeded", L("")); continue;
            }
            var statement = action.Kind switch
            {
                FlowActionKind.OpenProgram => result + " := FlowProgram(" + input + ", " + (p?.ArgumentInput is null ? L(p?.Arguments ?? "") : Input(p.ArgumentInput)) + ", " + Input(p?.WorkingDirectory) + ", " + B(p?.ArgumentInput is not null) + ")",
                FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory => result + " := FlowCaptured(" + L(action.Kind == FlowActionKind.GetClickedObject ? "clicked" : action.Kind == FlowActionKind.GetSelectedObject ? "selected" : "directory") + ", context, " + B(p?.Context?.Missing != FlowMissingTarget.Error) + ")",
                FlowActionKind.GetPathProperties => result + " := FlowPath3(" + input + ")",
                FlowActionKind.ReadClipboard => result + " := {Text: A_Clipboard}",
                FlowActionKind.OpenTerminal => result + " := FlowTerminal3(" + input + ", context, " + Terminal(p?.Terminal ?? new()) + ")",
                FlowActionKind.ExtractArchive => result + " := FlowExtract3(" + input + ", " + Input(p?.Destination) + ", " + Extraction(p?.Extraction ?? new()) + ", context)",
                FlowActionKind.JoinPath => result + " := FlowJoinPath(" + input + ", [" + string.Join(", ", p!.JoinPath!.Segments.Select(part => Input(part))) + "])",
                FlowActionKind.CreateDirectory => result + " := FlowCreateDirectory(" + input + ", " + B(p?.Directory?.FailIfExists == true) + ")",
                FlowActionKind.StopWorkflow => "FlowStop(" + L(p!.Stop!.Reason) + ", " + B(p.Stop.Notify) + ")",
                FlowActionKind.Notify => "FlowNotify(" + Expression(p!.Notification!.Title) + ", " + Expression(p.Notification.Message) + ")",
                FlowActionKind.SetClipboard => "A_Clipboard := " + input,
                FlowActionKind.WaitForWindow => result + " := FlowWaitWindow(" + WindowInput(p!.Input!) + ", " + N(p.TimeoutMs) + ")",
                FlowActionKind.ActivateWindow => "FlowActivate(" + WindowInput(p!.Input!) + ", " + N(p.TimeoutMs) + ", context)",
                FlowActionKind.SendText => "FlowSendText(" + input + ", context)",
                FlowActionKind.SendKeys => "FlowSendKeys(" + L(action.Value) + ", context)",
                FlowActionKind.OpenFolder when p?.Input is not null => "FlowFolder(" + input + ")",
                FlowActionKind.OpenWebsite when p?.Input is not null => "FlowWebsite(" + input + ")",
                _ => VisualFlowGenerator.ActionSource(action)
            };
            source.Append(indent).Append(statement).Append("\r\n");
            var output = action.Kind switch
            {
                FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory or FlowActionKind.GetPathProperties or FlowActionKind.ExtractArchive or FlowActionKind.JoinPath or FlowActionKind.CreateDirectory => result + ".Path",
                FlowActionKind.OpenTerminal => result + ".Directory", FlowActionKind.WaitForWindow => "String(" + result + ".WindowId)",
                FlowActionKind.OpenProgram => "String(" + result + ".ProcessId)", _ => L("")
            };
            WriteEvent(source, indent, id, name, "succeeded", output);
        }
    }

    /// <summary>Emits closed terminal options without deriving behavior from global enable switches.</summary>
    private static string Terminal(FlowTerminalOptions value) => "{Mode: " + L(value.Mode.ToString()) + ", Program: "
        + L(value.Program == FlowTerminalProgram.WindowsTerminal ? "wt" : value.Program == FlowTerminalProgram.WindowsPowerShell ? "powershell" : "auto")
        + ", Position: " + L(value.Position.ToString().ToLowerInvariant()) + ", Width: " + N(value.Width) + ", Height: " + N(value.Height)
        + ", Gap: " + N(value.Gap) + ", WaitReady: " + B(value.WaitReady) + ", TimeoutMs: " + N(value.TimeoutMs) + "}";

    /// <summary>Snapshots extraction policy and resolves custom name parts within this invocation.</summary>
    private static string Extraction(FlowExtractionOptions value) => "{Mode: " + L(value.Mode.ToString()) + ", Destination: " + L(value.Destination.ToString())
        + ", Naming: " + L(value.Naming.ToString()) + ", Name: " + (value.Naming == FlowArchiveNaming.ArchiveName ? L("") : Expression(value.Name)) + ", Collision: " + L(value.Collision.ToString()) + "}";
    /// <summary>Joins typed text parts without compiling user expressions.</summary>
    private static string Expression(FlowTextExpression value) => "FlowCompose([" + string.Join(", ", value.Parts.Select(part => Input(part))) + "])";
    /// <summary>Emits one bounded event at a known document/run/step boundary.</summary>
    private static void WriteEvent(StringBuilder source, string indent, string id, string name, string state, string output) => source.Append(indent)
        .Append("FlowEvent(context, ").Append(id).Append(", ").Append(name).Append(", ").Append(L(state)).Append(", ").Append(output).Append(")\r\n");
    /// <summary>Resolves one fixed input or stable earlier-step field.</summary>
    private static string Input(FlowInput? input, string fallback = "") => input?.Kind == FlowInputKind.Result ? Result(input.StepId) + "." + input.Field : L(input?.Literal ?? fallback);
    /// <summary>Builds selectors only from validated executable names or owned identities.</summary>
    private static string WindowInput(FlowInput input) => input.Kind == FlowInputKind.Literal ? L("ahk_exe " + input.Literal)
        : input.Field == FlowResultField.ProcessId ? "\"ahk_pid \" . FlowPositiveId(" + Input(input) + ")" : "FlowPositiveId(" + Input(input) + ")";
    /// <summary>Uses stable node IDs as local generated variable names.</summary>
    private static string Result(Guid id) => "result_" + id.ToString("N");
    /// <summary>Reuses the version-stable literal escaper.</summary>
    private static string L(string value) => VisualFlowGenerator.Literal(value);
    /// <summary>Formats integer parameters independently of user locale.</summary>
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    /// <summary>Emits fixed AHK boolean keywords.</summary>
    private static string B(bool value) => value ? "true" : "false";
    /// <summary>Loads canonical current helpers only for the v3 source contract.</summary>
    private static string Resource(string name) => Helpers.GetOrAdd(name, ReadResource);

    /// <summary>Reads a helper once per process, preserving deterministic generated line endings.</summary>
    private static string ReadResource(string name)
    {
        using var stream = typeof(VisualFlowGeneratorV3).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern." + name)
            ?? throw new InvalidDataException("Missing v3 workflow helper: " + name);
        using var reader = new StreamReader(stream, VisualFlowCodec.Utf8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
    }
}
