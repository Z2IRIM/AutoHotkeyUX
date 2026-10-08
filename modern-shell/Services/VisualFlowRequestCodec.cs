using AutoHotkeyUX.Modern.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Closes the v3 IPC contract before queuing any filesystem operation.</summary>
internal static class VisualFlowRequestCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, MaxDepth = 4
    };

    /// <summary>Rejects malformed identities and invalid output names without touching the filesystem.</summary>
    internal static VisualExtractionRequest Extraction(string body)
    {
        var request = Read<VisualExtractionRequest>(body);
        if (request.RequestId == Guid.Empty || request.FlowId == Guid.Empty || request.RunId == Guid.Empty || request.StepId == Guid.Empty
            || request.Source is null || !VisualFlowCodec.IsPath(request.Source) || !ArchiveExtractionService.SupportsPath(request.Source)
            || request.Destination is null || request.Destination.Length != 0 && !VisualFlowCodec.IsPath(request.Destination)
            || request.Name is null || !Enum.IsDefined(request.Collision)) throw new InvalidDataException("Invalid workflow extraction request.");
        ArchiveExtractionService.ValidateOutputName(request.Name);
        return request;
    }

    /// <summary>Accepts only supported invocation states and bounded human-readable output.</summary>
    internal static VisualFlowEvent Event(string body)
    {
        var value = Read<VisualFlowEvent>(body);
        if (value.FlowId == Guid.Empty || value.RunId == Guid.Empty || value.StepName is null || value.StepName.Length is < 1 or > 80
            || value.StepName.Any(char.IsControl) || value.State is not ("started" or "succeeded" or "branch" or "failed" or "stopped")
            || value.Detail is null || value.Detail.Length > 2048 || value.Detail.Contains('\0') || value.ElapsedMs is < 0 or > 3600000)
            throw new InvalidDataException("Invalid workflow activity event.");
        return value;
    }

    /// <summary>Rejects duplicate/unknown JSON members before deserializing a protocol record.</summary>
    private static T Read<T>(string body)
    {
        if (VisualFlowCodec.Utf8.GetByteCount(body) > 32700) throw new InvalidDataException("Workflow request is too large.");
        using var json = JsonDocument.Parse(body, new() { MaxDepth = 4 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected one workflow request object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in json.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate workflow request property.");
        return JsonSerializer.Deserialize<T>(body, Options) ?? throw new InvalidDataException("Empty workflow request.");
    }
}
