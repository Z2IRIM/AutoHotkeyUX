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
    internal static FlowAction Action(FlowActionKind kind)
    {
        var action = new FlowAction { Kind = kind, DelayMs = kind == FlowActionKind.Wait ? 500 : 0,
        Value = kind switch { FlowActionKind.OpenProgram => "notepad.exe", FlowActionKind.OpenWebsite => "https://example.com", FlowActionKind.SendText => "Hello!", FlowActionKind.SendKeys => "Enter", _ => "" },
        Parameters = kind <= FlowActionKind.Wait ? null : new() { Input = kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory or FlowActionKind.ReadClipboard ? null : new(),
            TimeoutMs = kind == FlowActionKind.WaitForWindow ? 10000 : kind == FlowActionKind.ActivateWindow ? 3000 : 0,
            Condition = kind == FlowActionKind.IfElse ? FlowConditionKind.IsFolder : FlowConditionKind.None } };
        var parameters = action.Parameters ?? new();
        parameters = kind switch
        {
            FlowActionKind.OpenTerminal => parameters with { Terminal = new() },
            FlowActionKind.ExtractArchive => parameters with { Extraction = new() },
            FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory => parameters with { Context = new() },
            FlowActionKind.GetPathProperties => parameters with { Input = new() { Literal = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) } },
            FlowActionKind.JoinPath => parameters with { Input = new() { Literal = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }, JoinPath = new() { Segments = [new() { Literal = "Output" }] } },
            FlowActionKind.CreateDirectory => parameters with { Input = new() { Literal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Workflow output") }, Directory = new() },
            FlowActionKind.StopWorkflow => parameters with { Input = null, Stop = new() },
            FlowActionKind.Notify => parameters with { Input = null, Notification = new() }, _ => parameters
        };
        return kind <= FlowActionKind.Wait ? action : action with { Parameters = parameters };
    }

    /// <summary>Starts an empty v3 draft whose save becomes available after adding valid actions.</summary>
    internal static VisualFlowDocument Blank() => new() { SchemaVersion = 3, Actions = [] };

    /// <summary>Builds one editable click dispatcher with independently configurable terminal and extraction branches.</summary>
    internal static VisualFlowDocument ExplorerV3()
    {
        var target = Action(FlowActionKind.GetClickedObject);
        var terminal = Action(FlowActionKind.OpenTerminal) with { Parameters = new() { Input = FlowInput.Reference(target.Id, FlowResultField.Directory), Terminal = new() { Mode = FlowConfigurationMode.Custom } } };
        var extraction = Action(FlowActionKind.ExtractArchive) with { Parameters = new() { Input = FlowInput.Reference(target.Id), Extraction = new() { Mode = FlowConfigurationMode.Custom } } };
        var folder = new FlowAction { Kind = FlowActionKind.OpenFolder, Parameters = new() { Input = FlowInput.Reference(extraction.Id, FlowResultField.Directory) } };
        var notify = Action(FlowActionKind.Notify) with { Parameters = new() { Notification = new() { Message = new() { Parts = [new() { Literal = "Extracted to " }, FlowInput.Reference(extraction.Id, FlowResultField.Directory)] } } } };
        var archive = new FlowAction { Kind = FlowActionKind.IfElse, Parameters = new() { Input = FlowInput.Reference(target.Id), Condition = FlowConditionKind.IsArchive,
            Then = [extraction, folder, notify], Else = [Action(FlowActionKind.StopWorkflow)] } };
        return new() { SchemaVersion = 3, Trigger = new() { Key = "LButton", Modifiers = FlowModifiers.Alt, Scope = FlowScopeKind.ExplorerDesktop },
            Actions = [target, new() { Kind = FlowActionKind.IfElse, Parameters = new() { Input = FlowInput.Reference(target.Id), Condition = FlowConditionKind.IsFolder, Then = [terminal], Else = [archive] } }] };
    }

    /// <summary>Uses a distinct keyboard shortcut for the standalone current-directory terminal example.</summary>
    internal static VisualFlowDocument Terminal()
    {
        var source = Action(FlowActionKind.GetCurrentDirectory);
        return new() { SchemaVersion = 3, Trigger = new() { Key = "T", Scope = FlowScopeKind.ExplorerDesktop }, Actions = [source,
            Action(FlowActionKind.OpenTerminal) with { Parameters = new() { Input = FlowInput.Reference(source.Id, FlowResultField.Directory), Terminal = new() { Mode = FlowConfigurationMode.Custom } } }] };
    }

    /// <summary>Uses a distinct keyboard shortcut for one selected archive and its committed output directory.</summary>
    internal static VisualFlowDocument Extract()
    {
        var source = Action(FlowActionKind.GetSelectedObject);
        var extract = Action(FlowActionKind.ExtractArchive) with { Parameters = new() { Input = FlowInput.Reference(source.Id), Extraction = new() { Mode = FlowConfigurationMode.Custom } } };
        return new() { SchemaVersion = 3, Trigger = new() { Key = "E", Scope = FlowScopeKind.ExplorerDesktop }, Actions = [source, extract,
            new() { Kind = FlowActionKind.OpenFolder, Parameters = new() { Input = FlowInput.Reference(extract.Id, FlowResultField.Directory) } }] };
    }
}
