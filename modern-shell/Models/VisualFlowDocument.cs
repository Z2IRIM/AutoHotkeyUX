using System.Text.Json.Serialization;

namespace AutoHotkeyUX.Modern.Models;

internal enum FlowTriggerKind { Hotkey, Startup }
internal enum FlowScopeKind { AnyApplication, ActiveApplication, ExplorerDesktop }
[Flags] internal enum FlowModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }
internal enum FlowActionKind { OpenProgram, OpenFolder, OpenWebsite, SendText, SendKeys, Wait,
    GetClickedObject, GetSelectedObject, GetCurrentDirectory, ReadClipboard, OpenTerminal, ExtractArchive, SetClipboard, WaitForWindow, ActivateWindow, IfElse,
    GetPathProperties, JoinPath, CreateDirectory, StopWorkflow, Notify }
internal enum FlowFolderKind { Documents, Desktop, Custom }

/// <summary>Stores a versioned visual workflow independently of the WinUI editor and generated source.</summary>
internal sealed record VisualFlowDocument
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public long Revision { get; init; }
    [JsonRequired] public string SourceSha256 { get; init; } = "";
    [JsonRequired] public FlowTrigger Trigger { get; init; } = new();
    [JsonRequired] public FlowAction[] Actions { get; init; } = [];
}

/// <summary>Describes a single trigger and an optional active-application scope for the whole workflow.</summary>
internal sealed record FlowTrigger
{
    [JsonRequired] public FlowTriggerKind Kind { get; init; }
    [JsonRequired] public FlowModifiers Modifiers { get; init; } = FlowModifiers.Ctrl | FlowModifiers.Alt;
    [JsonRequired] public string Key { get; init; } = "D";
    [JsonRequired] public FlowScopeKind Scope { get; init; }
    [JsonRequired] public string Application { get; init; } = "";
}

/// <summary>Stores one supported action with a stable identity and bounded literal parameters.</summary>
internal sealed record FlowAction
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public FlowActionKind Kind { get; init; }
    [JsonRequired] public string Value { get; init; } = "";
    [JsonRequired] public FlowFolderKind Folder { get; init; }
    [JsonRequired] public int DelayMs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisplayName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowParameters? Parameters { get; init; }
}

/// <summary>Retains the exact opened bytes needed to detect changes before updating either file.</summary>
internal sealed record VisualFlowOpened(string ScriptPath, VisualFlowDocument Document, string SourceHash, string SidecarHash);
