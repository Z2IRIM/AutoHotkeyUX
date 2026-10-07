using AutoHotkeyUX.Modern.Models;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Emits a deterministic v2 workflow with fixed operations and reusable existing Shell/terminal helpers.</summary>
internal static class VisualFlowGeneratorV2
{
    /// <summary>Builds a scoped trigger around a callable workflow with per-run local results.</summary>
    internal static string Generate(VisualFlowDocument flow)
    {
        var all = VisualFlowTree.Walk(flow.Actions).Select(item => item.Action).ToArray();
        var shell = all.Any(action => action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory)
            || flow.Trigger.Scope == FlowScopeKind.ExplorerDesktop;
        var terminal = all.Any(action => action.Kind == FlowActionKind.OpenTerminal);
        shell |= terminal;
        var mouse = flow.Trigger.Kind == FlowTriggerKind.Hotkey && flow.Trigger.Key.EndsWith("Button", StringComparison.Ordinal);
        var source = new StringBuilder("; AutoHotkey UX visual workflow v2\r\n; Workflow: " + flow.Id.ToString("D") + "\r\n#Requires AutoHotkey v2.0\r\n#SingleInstance Ignore\r\n");
        source.Append("CoordMode \"Mouse\", \"Screen\"\r\nSetWinDelay 0\r\n");
        if (terminal) source.Append("try GetShortcutRuntime().Snapshot := ParseShortcutPreferences(RegRead(\"HKCU\\Software\\AutoHotkey\\Modern\", \"ShortcutPreferences\", \"\"))\r\ncatch Error\r\n    GetShortcutRuntime().Snapshot := ParseShortcutPreferences(\"\")\r\n");
        var scope = flow.Trigger.Scope switch
        {
            FlowScopeKind.ActiveApplication => "WinActive(" + L("ahk_exe " + flow.Trigger.Application) + ")",
            FlowScopeKind.ExplorerDesktop => "FlowShellContext(" + (mouse ? "true" : "false") + ")",
            _ => ""
        };
        if (flow.Trigger.Kind == FlowTriggerKind.Hotkey)
        {
            if (scope.Length > 0) source.Append("#HotIf ").Append(scope).Append("\r\n");
            if (mouse) source.Append('~');
            foreach (var (flag, symbol) in new[] { (FlowModifiers.Ctrl, "^"), (FlowModifiers.Alt, "!"), (FlowModifiers.Shift, "+"), (FlowModifiers.Win, "#") })
                if (flow.Trigger.Modifiers.HasFlag(flag)) source.Append(symbol);
            source.Append(flow.Trigger.Key).Append(":: FlowRun()\r\n");
            if (scope.Length > 0) source.Append("#HotIf\r\n");
        }
        else source.Append(scope.Length > 0 ? "if " + scope + "\r\n    " : "").Append("FlowRun()\r\n");
        source.Append("\r\n; Executes one workflow; results cannot escape their invocation or conditional branch.\r\nFlowRun() {\r\n    step := \"trigger\"\r\n    try {\r\n        context := FlowCapture(" + L(mouse ? flow.Trigger.Key : "") + ")\r\n        if !context\r\n            return\r\n");
        WriteActions(source, flow.Actions, "        ");
        source.Append("    }\r\n    catch Error as failure\r\n        FlowFailure(step, failure)\r\n}\r\n\r\n");
        source.Append(Resource("VisualFlow.Runtime.ahk"));
        if (shell) source.Append(Resource("ExplorerShortcuts.Shell.ahk"));
        if (terminal)
        {
            source.Append(Resource("ExplorerShortcuts.Actions.ahk"));
            var preferences = Resource("ExplorerShortcuts.Preferences.ahk");
            var marker = "; Loads the snapshot once, registers only";
            var end = preferences.IndexOf(marker, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("The shared terminal preference resource changed without a generator migration.");
            source.Append(preferences[..end]);
        }
        if (!shell) source.Append("; Unused context fallback for workflows without Shell actions.\r\nGetShellGestureContext() => false\r\nGetShellView(context) => false\r\nGetFileViewHit(x, y, desktop := false) => false\r\nResolveHitPath(view, hit) => \"\"\r\n");
        if (!terminal)
        {
            var actions = Resource("ExplorerShortcuts.Actions.ahk");
            var end = actions.IndexOf("; Places", StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("Shared quoting resource changed without a generator migration.");
            source.Append(actions[..end]).Append("OpenTerminal(directory, x, y) => false\r\n");
        }
        return source.ToString();
    }
    /// <summary>Writes branch bodies recursively and binds only declared output records.</summary>
    private static void WriteActions(StringBuilder source, FlowAction[] actions, string indent)
    {
        foreach (var action in actions)
        {
            var p = action.Parameters; var input = Input(p?.Input, action.Value); var result = Result(action.Id);
            source.Append(indent).Append("step := ").Append(L(action.Kind + " " + action.Id.ToString("N"))).Append("\r\n");
            if (action.Kind == FlowActionKind.IfElse)
            {
                source.Append(indent).Append("if FlowCondition(").Append(input).Append(", ").Append(L(p!.Condition.ToString())).Append(", ").Append(L(p.Comparison)).Append(") {\r\n");
                WriteActions(source, p.Then, indent + "    "); source.Append(indent).Append("} else {\r\n");
                WriteActions(source, p.Else, indent + "    "); source.Append(indent).Append("}\r\n"); continue;
            }
            var statement = action.Kind switch
            {
                FlowActionKind.OpenProgram => result + " := FlowProgram(" + input + ", " + (p?.ArgumentInput is null ? L(p?.Arguments ?? "") : Input(p.ArgumentInput)) + ", " + Input(p?.WorkingDirectory) + ", " + (p?.ArgumentInput is null ? "false" : "true") + ")",
                FlowActionKind.GetClickedObject => result + " := FlowAcquire(\"clicked\", context)",
                FlowActionKind.GetSelectedObject => result + " := FlowAcquire(\"selected\", context)",
                FlowActionKind.GetCurrentDirectory => result + " := FlowAcquire(\"directory\", context)",
                FlowActionKind.ReadClipboard => result + " := {Text: A_Clipboard}",
                FlowActionKind.OpenTerminal => "FlowTerminal(" + input + ", context)",
                FlowActionKind.ExtractArchive => result + " := FlowExtract(" + input + ", " + Input(p?.Destination) + ")",
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
        }
    }
    /// <summary>Resolves a fixed value or earlier generated variable without user-supplied syntax.</summary>
    private static string Input(FlowInput? input, string fallback = "") => input?.Kind == FlowInputKind.Result ? Result(input.StepId) + "." + input.Field : L(input?.Literal ?? fallback);
    /// <summary>Builds a closed window selector for an executable, a PID or an HWND.</summary>
    private static string WindowInput(FlowInput input) => input.Kind == FlowInputKind.Literal ? L("ahk_exe " + input.Literal)
        : input.Field == FlowResultField.ProcessId ? "\"ahk_pid \" . FlowPositiveId(" + Input(input) + ")" : "FlowPositiveId(" + Input(input) + ")";
    /// <summary>Uses identity-based names so reordering cannot redirect an output reference.</summary>
    private static string Result(Guid id) => "result_" + id.ToString("N");
    /// <summary>Delegates literal escaping to the unchanged v1 string encoder.</summary>
    private static string L(string value) => VisualFlowGenerator.Literal(value);
    /// <summary>Formats numbers deterministically regardless of the host's locale.</summary>
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    /// <summary>Reads canonical helper source from the same assembly resources as the existing core.</summary>
    private static string Resource(string name)
    {
        using var stream = typeof(VisualFlowGeneratorV2).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern." + name)
            ?? throw new InvalidDataException("Missing workflow helper: " + name);
        using var reader = new StreamReader(stream, VisualFlowCodec.Utf8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
    }
}
