using System.Text.Json.Serialization;

namespace AutoHotkeyUX.Modern.Models;

/// <summary>Captures one explicit extraction independently of built-in shortcut enablement.</summary>
internal sealed record VisualExtractionRequest
{
    [JsonRequired] public Guid RequestId { get; init; }
    [JsonRequired] public Guid FlowId { get; init; }
    [JsonRequired] public Guid RunId { get; init; }
    [JsonRequired] public Guid StepId { get; init; }
    [JsonRequired] public string Source { get; init; } = "";
    [JsonRequired] public string Destination { get; init; } = "";
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public FlowArchiveCollision Collision { get; init; }
}

/// <summary>Reports one bounded invocation event without sending executable code.</summary>
internal sealed record VisualFlowEvent
{
    [JsonRequired] public Guid FlowId { get; init; }
    [JsonRequired] public Guid RunId { get; init; }
    [JsonRequired] public Guid StepId { get; init; }
    [JsonRequired] public string StepName { get; init; } = "";
    [JsonRequired] public string State { get; init; } = "";
    [JsonRequired] public string Detail { get; init; } = "";
    [JsonRequired] public long ElapsedMs { get; init; }
}
