using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Provides three editable practical starters using the same typed model as ordinary library actions.</summary>
internal static class VisualFlowExamples
{
    /// <summary>Builds a folder/archive click workflow without duplicating the existing Shell or terminal implementation.</summary>
    internal static VisualFlowDocument Explorer()
    {
        var target = new FlowAction { Kind = FlowActionKind.GetClickedObject, Parameters = new() };
        var input = FlowInput.Reference(target.Id);
        var archive = new FlowAction { Kind = FlowActionKind.IfElse, Parameters = new() { Input = input, Condition = FlowConditionKind.IsArchive,
            Then = [new() { Kind = FlowActionKind.ExtractArchive, Parameters = new() { Input = input } }] } };
        return new() { SchemaVersion = 2, Trigger = new() { Key = "LButton", Modifiers = FlowModifiers.Alt, Scope = FlowScopeKind.ExplorerDesktop },
            Actions = [target, new() { Kind = FlowActionKind.IfElse, Parameters = new() { Input = input, Condition = FlowConditionKind.IsFolder,
                Then = [new() { Kind = FlowActionKind.OpenTerminal, Parameters = new() { Input = input } }], Else = [archive] } }] };
    }
    /// <summary>Copies the selected single Explorer object's full path with an explicit result reference.</summary>
    internal static VisualFlowDocument CopyPath()
    {
        var target = new FlowAction { Kind = FlowActionKind.GetSelectedObject, Parameters = new() };
        return new() { SchemaVersion = 2, Trigger = new() { Key = "C", Scope = FlowScopeKind.ExplorerDesktop },
            Actions = [target, new() { Kind = FlowActionKind.SetClipboard, Parameters = new() { Input = FlowInput.Reference(target.Id) } }] };
    }
    /// <summary>Starts a common editor then activates its discovered HWND before sending text.</summary>
    internal static VisualFlowDocument LaunchAndType()
    {
        var program = new FlowAction { Kind = FlowActionKind.OpenProgram, Value = "notepad.exe", Parameters = new() };
        var window = new FlowAction { Kind = FlowActionKind.WaitForWindow, Parameters = new() { Input = new() { Literal = "notepad.exe" }, TimeoutMs = 10000 } };
        return new() { SchemaVersion = 2, Trigger = new() { Key = "N" }, Actions = [program, window,
            new() { Kind = FlowActionKind.ActivateWindow, Parameters = new() { Input = FlowInput.Reference(window.Id, FlowResultField.WindowId), TimeoutMs = 3000 } },
            new() { Kind = FlowActionKind.SendText, Value = "Hello from my workflow!" }] };
    }
    /// <summary>Creates an action with bounded defaults; result wiring remains the editor's responsibility.</summary>
    internal static FlowAction Action(FlowActionKind kind) => new() { Kind = kind, DelayMs = kind == FlowActionKind.Wait ? 500 : 0,
        Value = kind switch { FlowActionKind.OpenProgram => "notepad.exe", FlowActionKind.OpenWebsite => "https://example.com", FlowActionKind.SendText => "Hello!", FlowActionKind.SendKeys => "Enter", _ => "" },
        Parameters = kind <= FlowActionKind.Wait ? null : new() { Input = kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory or FlowActionKind.ReadClipboard ? null : new(),
            TimeoutMs = kind == FlowActionKind.WaitForWindow ? 10000 : kind == FlowActionKind.ActivateWindow ? 3000 : 0,
            Condition = kind == FlowActionKind.IfElse ? FlowConditionKind.IsFolder : FlowConditionKind.None } };
}
