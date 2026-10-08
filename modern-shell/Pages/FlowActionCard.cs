using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

/// <summary>Projects sequences and explicit branch insertion rows into the existing native list.</summary>
internal sealed record FlowActionCard(FlowAction Action, int Number, FlowBranch Branch = default, int Depth = 0, bool IsBranch = false, string Detail = "")
{
    public string Key => Action.Id + (IsBranch ? Branch.IsElse ? ":else" : ":then" : "");
    public string Title => IsBranch ? (Branch.IsElse ? "ELSE" : "IF TRUE") + " · Add here" : Number + ". " + (Action.DisplayName ?? Label(Action.Kind));
    public string Glyph => IsBranch ? "\uE710" : Icon(Action.Kind);
    public Thickness Indent => new(Math.Min(Depth, 3) * 16, 0, 0, 0);
    public string Summary => IsBranch ? "Select this branch, then add an action from the library." : Detail;
    internal static readonly (string Category, FlowActionKind[] Kinds)[] Categories =
    [ ("Context", [FlowActionKind.GetClickedObject, FlowActionKind.GetSelectedObject, FlowActionKind.GetCurrentDirectory, FlowActionKind.ReadClipboard, FlowActionKind.GetPathProperties]),
      ("Files", [FlowActionKind.OpenFolder, FlowActionKind.OpenTerminal, FlowActionKind.ExtractArchive, FlowActionKind.JoinPath, FlowActionKind.CreateDirectory, FlowActionKind.SetClipboard]),
      ("Windows", [FlowActionKind.OpenProgram, FlowActionKind.WaitForWindow, FlowActionKind.ActivateWindow, FlowActionKind.OpenWebsite]),
      ("Input / logic", [FlowActionKind.SendText, FlowActionKind.SendKeys, FlowActionKind.Wait, FlowActionKind.IfElse, FlowActionKind.StopWorkflow, FlowActionKind.Notify]) ];

    /// <summary>Includes both empty branch entrances so a condition can be constructed without code.</summary>
    internal static IEnumerable<FlowActionCard> Project(VisualFlowDocument flow)
    {
        var numbers = VisualFlowTree.Walk(flow.Actions).Select((item, index) => (item.Action.Id, Number: index + 1)).ToDictionary(item => item.Id, item => item.Number);
        return Rows(flow.Actions, default, 0);
        IEnumerable<FlowActionCard> Rows(FlowAction[] actions, FlowBranch branch, int depth)
        {
            foreach (var action in actions)
            {
                yield return new(action, numbers[action.Id], branch, depth, Detail: Describe(flow, action));
                if (action.Kind != FlowActionKind.IfElse || action.Parameters is not { } p) continue;
                yield return new(action, 0, new(action.Id), depth + 1, true);
                foreach (var row in Rows(p.Then, new(action.Id), depth + 1)) yield return row;
                yield return new(action, 0, new(action.Id, true), depth + 1, true);
                foreach (var row in Rows(p.Else, new(action.Id, true), depth + 1)) yield return row;
            }
        }
    }
    /// <summary>Labels a producer by its current display number while references retain their stable GUID.</summary>
    internal static string StepLabel(VisualFlowDocument flow, Guid id)
    {
        var match = VisualFlowTree.Walk(flow.Actions).Select((item, index) => (item.Action, Number: index + 1)).FirstOrDefault(item => item.Action.Id == id);
        return match.Action is null ? "Removed step" : match.Number + ". " + (match.Action.DisplayName ?? Label(match.Action.Kind));
    }
    /// <summary>Formats input sources in product language without exposing generated variable names.</summary>
    internal static string InputLabel(VisualFlowDocument flow, FlowInput? input, string fallback = "") => input?.Kind == FlowInputKind.Result
        ? StepLabel(flow, input.StepId) + " → " + VisualFlowSchema.FieldLabel(input.Field) : input?.Literal ?? fallback;
    /// <summary>Provides short source/output descriptions for each supported action.</summary>
    private static string Describe(VisualFlowDocument flow, FlowAction action)
    {
        var input = InputLabel(flow, action.Parameters?.Input, action.Value).Replace('\r', ' ').Replace('\n', ' ');
        if (action.Kind == FlowActionKind.IfElse) return ConditionLabel(action.Parameters!.Condition) + " · " + input;
        if (action.Kind == FlowActionKind.OpenTerminal && action.Parameters?.Terminal is { } terminal)
            return terminal.Mode == FlowConfigurationMode.Inherit ? input + " · App settings" : input + " · " + terminal.Program + " · " + terminal.Position + " · " + terminal.Width + "×" + terminal.Height;
        if (action.Kind == FlowActionKind.ExtractArchive && action.Parameters?.Extraction is { } archive)
            return input + " · " + (archive.Mode == FlowConfigurationMode.Inherit ? "App settings" : archive.Destination + " · " + (archive.Collision == FlowArchiveCollision.AutoSuffix ? "Add a number" : "Fail if exists"));
        if (VisualFlowTree.Outputs(action.Kind).Length > 0 && input.Length == 0) return "Produces: " + string.Join(", ", VisualFlowTree.Outputs(action.Kind));
        return action.Kind switch { FlowActionKind.Wait => action.DelayMs + " ms", FlowActionKind.OpenFolder when action.Parameters?.Input is null && action.Folder != FlowFolderKind.Custom => action.Folder.ToString(),
            _ => input.Length == 0 ? "Set this action's properties" : input };
    }
    /// <summary>Shows condition names as ordinary UI labels rather than serialized enum tokens.</summary>
    internal static string ConditionLabel(FlowConditionKind kind) => kind switch
    { FlowConditionKind.IsFolder => "Is folder", FlowConditionKind.IsFile => "Is file", FlowConditionKind.IsArchive => "Is archive",
        FlowConditionKind.ExtensionEquals => "Extension equals", FlowConditionKind.IsEmpty => "Is empty", FlowConditionKind.IsNotEmpty => "Is not empty", FlowConditionKind.PathExists => "Path exists", _ => "Choose a condition" };
    /// <summary>Shares readable labels across library, properties and sequence cards.</summary>
    internal static string Label(FlowActionKind kind) => kind switch
    { FlowActionKind.OpenProgram => "Open program / file", FlowActionKind.OpenFolder => "Open folder", FlowActionKind.OpenWebsite => "Open website",
        FlowActionKind.SendText => "Send text", FlowActionKind.SendKeys => "Send keys", FlowActionKind.Wait => "Wait", FlowActionKind.GetClickedObject => "Get clicked object",
        FlowActionKind.GetSelectedObject => "Get selected object", FlowActionKind.GetCurrentDirectory => "Get current directory", FlowActionKind.ReadClipboard => "Read clipboard",
        FlowActionKind.OpenTerminal => "Open terminal", FlowActionKind.ExtractArchive => "Extract archive", FlowActionKind.SetClipboard => "Set clipboard",
        FlowActionKind.WaitForWindow => "Wait for window", FlowActionKind.ActivateWindow => "Activate window", FlowActionKind.IfElse => "If / Else",
        FlowActionKind.GetPathProperties => "Read path properties", FlowActionKind.JoinPath => "Join path", FlowActionKind.CreateDirectory => "Create directory",
        FlowActionKind.StopWorkflow => "Stop this workflow", FlowActionKind.Notify => "Notification", _ => kind.ToString() };
    /// <summary>Reuses the Windows symbol font for all action categories.</summary>
    internal static string Icon(FlowActionKind kind) => kind switch
    { FlowActionKind.OpenProgram => "\uE8A5", FlowActionKind.OpenFolder or FlowActionKind.GetCurrentDirectory => "\uE838", FlowActionKind.OpenWebsite => "\uE774",
        FlowActionKind.SendText => "\uE8D2", FlowActionKind.SendKeys => "\uE765", FlowActionKind.ReadClipboard or FlowActionKind.SetClipboard => "\uE77F",
        FlowActionKind.OpenTerminal => "\uE756", FlowActionKind.ExtractArchive => "\uE8B7", FlowActionKind.IfElse => "\uE8AB", FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject => "\uE8B0",
        FlowActionKind.ActivateWindow or FlowActionKind.WaitForWindow => "\uE737", _ => "\uE916" };
}
