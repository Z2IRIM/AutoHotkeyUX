using System.Text.Json.Serialization;

namespace AutoHotkeyUX.Modern.Models;

internal enum FlowInputKind { Literal, Result }
internal enum FlowResultField { Path, Directory, Name, Extension, Text, ProcessId, WindowId,
    ParentDirectory, BaseName, TargetKind, Exists, Success, MouseX, MouseY }
internal enum FlowConditionKind { None, IsFolder, IsFile, IsArchive, ExtensionEquals, IsEmpty, IsNotEmpty, PathExists }

/// <summary>Represents a fixed value or one typed earlier result, never an executable expression.</summary>
internal sealed record FlowInput
{
    public FlowInputKind Kind { get; init; }
    public string Literal { get; init; } = "";
    public Guid StepId { get; init; }
    public FlowResultField Field { get; init; }
    /// <summary>Creates an explicit reference to a prior step output.</summary>
    internal static FlowInput Reference(Guid id, FlowResultField field = FlowResultField.Path) => new() { Kind = FlowInputKind.Result, StepId = id, Field = field };
}

/// <summary>Extends v2 actions while keeping v1 JSON and source bytes unchanged.</summary>
internal sealed record FlowParameters
{
    public FlowInput? Input { get; init; }
    public string Arguments { get; init; } = "";
    public FlowInput? ArgumentInput { get; init; }
    public FlowInput? WorkingDirectory { get; init; }
    public FlowInput? Destination { get; init; }
    public int TimeoutMs { get; init; }
    public FlowConditionKind Condition { get; init; }
    public string Comparison { get; init; } = "";
    public FlowAction[] Then { get; init; } = [];
    public FlowAction[] Else { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowTerminalOptions? Terminal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowExtractionOptions? Extraction { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowContextOptions? Context { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowJoinPathOptions? JoinPath { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowDirectoryOptions? Directory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowStopOptions? Stop { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowNotificationOptions? Notification { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FlowFailureOptions? Failure { get; init; }
}

/// <summary>Names an insertion sequence independently of action selection and undo history.</summary>
internal readonly record struct FlowBranch(Guid? ParentId = null, bool IsElse = false);
