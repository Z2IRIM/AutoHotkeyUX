using AutoHotkeyUX.Modern.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Validates the bounded supported language and rejects sidecars whose semantics cannot be preserved.</summary>
internal static class VisualFlowCodec
{
    internal const int MaximumActions = 24;
    internal const int MaximumJsonBytes = 262144;
    internal static readonly string[] Keys = Enumerable.Range('A', 26).Select(key => ((char)key).ToString())
        .Concat(Enumerable.Range(1, 12).Select(key => "F" + key)).ToArray();
    internal static readonly string[] SendKeys = ["Enter", "Tab", "Escape", "Ctrl+C", "Ctrl+V"];
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = true,
        MaxDepth = 12
    };

    /// <summary>Serializes only a validated document with a bounded UTF-8 representation.</summary>
    internal static string Encode(VisualFlowDocument value)
    {
        Validate(value);
        var json = JsonSerializer.Serialize(value, Options);
        if (Utf8.GetByteCount(json) > MaximumJsonBytes) throw new InvalidDataException("The workflow document is too large.");
        return json;
    }

    /// <summary>Rejects missing, duplicate, unknown or incompatible data instead of discarding its meaning.</summary>
    internal static VisualFlowDocument Decode(string value)
    {
        if (Utf8.GetByteCount(value) > MaximumJsonBytes) throw new InvalidDataException("The workflow document is too large.");
        using var json = JsonDocument.Parse(value, new() { MaxDepth = 12 });
        CheckDuplicateProperties(json.RootElement);
        var result = JsonSerializer.Deserialize<VisualFlowDocument>(value, Options)
            ?? throw new InvalidDataException("The workflow document is empty.");
        Validate(result);
        if (result.Revision < 1 || !Regex.IsMatch(result.SourceSha256, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The saved workflow has an invalid revision or source checksum.");
        return result;
    }

    /// <summary>Constrains executable semantics independently of UI controls and JSON serialization.</summary>
    internal static void Validate(VisualFlowDocument value)
    {
        if (value is null || value.SchemaVersion != 1 || value.Id == Guid.Empty || value.Revision < 0
            || value.Revision == long.MaxValue || value.SourceSha256 is null)
            throw new InvalidDataException("Unsupported workflow version or identity.");
        var trigger = value.Trigger;
        if (trigger is null || !Enum.IsDefined(trigger.Kind) || (trigger.Modifiers & ~((FlowModifiers)15)) != 0
            || !Keys.Contains(trigger.Key, StringComparer.Ordinal)) throw new InvalidDataException("Choose a supported trigger and key.");
        if (trigger.Kind == FlowTriggerKind.Hotkey && trigger.Modifiers == FlowModifiers.None && !trigger.Key.StartsWith('F'))
            throw new InvalidDataException("Letter hotkeys need at least one modifier.");
        if (!Enum.IsDefined(trigger.Scope) || trigger.Application is null || trigger.Application.Length > 128
            || (trigger.Scope == FlowScopeKind.ActiveApplication && trigger.Application.Length == 0)
            || (trigger.Scope == FlowScopeKind.AnyApplication && trigger.Application.Length != 0)
            || (trigger.Application.Length > 0 && !Regex.IsMatch(trigger.Application, "^[a-zA-Z0-9_ .-]+\\.exe$", RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Use an application file name such as explorer.exe.");
        if (value.Actions is null || value.Actions.Length is < 1 or > MaximumActions)
            throw new InvalidDataException($"Add between 1 and {MaximumActions} actions.");
        var identities = new HashSet<Guid>();
        foreach (var action in value.Actions)
        {
            if (action is null || action.Id == Guid.Empty || !identities.Add(action.Id) || !Enum.IsDefined(action.Kind)
                || !Enum.IsDefined(action.Folder) || action.Value is null || action.Value.Contains('\0'))
                throw new InvalidDataException("The workflow contains an invalid action.");
            _ = Utf8.GetByteCount(action.Value);
            if (action.Kind != FlowActionKind.Wait && action.DelayMs != 0
                || action.Kind != FlowActionKind.OpenFolder && action.Folder != FlowFolderKind.Documents)
                throw new InvalidDataException("An action contains parameters from a different action type.");
            switch (action.Kind)
            {
                case FlowActionKind.OpenProgram:
                    if (!IsPath(action.Value) && !Regex.IsMatch(action.Value, "^[a-zA-Z0-9_.-]+\\.exe$", RegexOptions.CultureInvariant))
                        throw new InvalidDataException("Choose an absolute program/file path or an executable name such as notepad.exe.");
                    break;
                case FlowActionKind.OpenFolder:
                    if (action.Folder == FlowFolderKind.Custom ? !IsPath(action.Value) : action.Value.Length != 0)
                        throw new InvalidDataException("Choose Documents, Desktop, or an absolute custom folder path.");
                    break;
                case FlowActionKind.OpenWebsite:
                    if (action.Value.Length > 2048 || action.Value.Any(char.IsWhiteSpace)
                        || !Uri.TryCreate(action.Value, UriKind.Absolute, out var uri)
                        || (uri.Scheme != "http" && uri.Scheme != "https") || uri.Host.Length == 0)
                        throw new InvalidDataException("Enter a complete HTTP or HTTPS address.");
                    break;
                case FlowActionKind.SendText:
                    if (action.Value.Length is < 1 or > 4096) throw new InvalidDataException("Enter between 1 and 4096 text characters.");
                    break;
                case FlowActionKind.SendKeys:
                    if (!SendKeys.Contains(action.Value, StringComparer.Ordinal)) throw new InvalidDataException("Choose a supported key combination.");
                    break;
                case FlowActionKind.Wait:
                    if (action.DelayMs is < 0 or > 60000 || action.Value.Length != 0)
                        throw new InvalidDataException("Wait must be an integer from 0 to 60000 milliseconds.");
                    break;
            }
        }
    }

    /// <summary>Accepts literal Windows paths while keeping arguments, control characters and wildcards out.</summary>
    internal static bool IsPath(string value) => value.Length is > 0 and <= 2048 && Path.IsPathFullyQualified(value)
        && !value.Any(char.IsControl) && value.IndexOfAny(['"', '<', '>', '|', '?', '*']) < 0;

    /// <summary>Detects duplicate JSON properties before the deserializer could silently select their last value.</summary>
    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate workflow property: " + property.Name);
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateProperties(child);
    }
}
