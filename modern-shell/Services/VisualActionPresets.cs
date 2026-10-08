using AutoHotkeyUX.Modern.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Persists portable single-node configurations outside the workflow document and runtime owners.</summary>
internal sealed class VisualActionPresets
{
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, WriteIndented = true, MaxDepth = 16 };
    private sealed record FileContent(int SchemaVersion, VisualActionPreset[] Items);

    /// <summary>Uses the manager's established state directory, including diagnostic isolation.</summary>
    internal VisualActionPresets(string root) => _path = Path.Combine(root, "my-actions.json");

    /// <summary>Returns validated portable templates while refusing unknown or linked storage.</summary>
    internal IReadOnlyList<VisualActionPreset> Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return [];
            if ((File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(_path).Length > VisualFlowCodec.MaximumJsonBytes)
                throw new InvalidDataException("The action preset file is linked or too large.");
            var data = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(_path), Options) ?? throw new InvalidDataException("Empty action preset file.");
            if (data.SchemaVersion != 1 || data.Items is null || data.Items.Length > 64 || data.Items.Any(item => item is null)
                || data.Items.Select(item => item.Id).Distinct().Count() != data.Items.Length)
                throw new InvalidDataException("Unsupported action preset file.");
            foreach (var item in data.Items) ValidateTemplate(item);
            return data.Items;
        }
    }

    /// <summary>Captures valid node settings and converts result bindings into numbered portable slots.</summary>
    internal static VisualActionPreset Capture(VisualFlowDocument flow, Guid id, string name)
    {
        VisualFlowCodec.Validate(flow);
        var action = VisualFlowTree.Find(flow.Actions, id) ?? throw new InvalidDataException("The selected action no longer exists.");
        var slots = new List<VisualPresetSlot>(); var index = 0;
        var template = VisualFlowSchema.MapInputs(action, input =>
        {
            var position = index++;
            if (input.Kind != FlowInputKind.Result) return input;
            slots.Add(new(position, input.Field)); return new();
        });
        template = template with { Id = Guid.Empty, DisplayName = null, Parameters = template.Parameters is { } p ? p with { Then = [], Else = [] } : null };
        var preset = new VisualActionPreset(Guid.NewGuid(), name.Trim(), template, slots.ToArray()); ValidateTemplate(preset); return preset;
    }

    /// <summary>Rebinds every dynamic input and allocates a fresh node identity in the receiving document.</summary>
    internal static FlowAction Bind(VisualActionPreset preset, IReadOnlyDictionary<int, FlowInput> bindings)
    {
        ValidateTemplate(preset); var index = 0;
        var slots = preset.Slots.Select(slot => slot.Index).ToHashSet();
        var action = VisualFlowSchema.MapInputs(preset.Template, input =>
        {
            var position = index++;
            if (!slots.Contains(position)) return input;
            return bindings.TryGetValue(position, out var value) ? value : throw new InvalidDataException("Bind every saved result input before insertion.");
        }) with { Id = Guid.NewGuid(), DisplayName = preset.Name };
        if (action.Parameters?.ArgumentInput is { Kind: FlowInputKind.Literal } argument)
            action = action with { Parameters = action.Parameters with { Arguments = argument.Literal, ArgumentInput = null } };
        return action;
    }

    /// <summary>Creates or updates a preset atomically without rewriting unrelated workflow files.</summary>
    internal void Save(VisualActionPreset value)
    {
        ValidateTemplate(value);
        lock (_gate)
        {
            var items = Read().ToList();
            if (items.Any(item => item.Id != value.Id && item.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("That preset name already exists.");
            items.RemoveAll(item => item.Id == value.Id); items.Add(value);
            if (items.Count > 64) throw new InvalidDataException("Keep at most 64 action presets.");
            Write(items.ToArray());
        }
    }

    /// <summary>Deletes only the explicitly selected preset while preserving the rest of the library.</summary>
    internal void Delete(Guid id) { lock (_gate) Write(Read().Where(item => item.Id != id).ToArray()); }

    /// <summary>Commits bounded JSON with a unique staging file under the same ordinary directory.</summary>
    private void Write(VisualActionPreset[] items)
    {
        var root = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Preset storage cannot be a linked directory.");
        var json = JsonSerializer.Serialize(new FileContent(1, items), Options);
        if (VisualFlowCodec.Utf8.GetByteCount(json) > VisualFlowCodec.MaximumJsonBytes) throw new InvalidDataException("The preset library is too large.");
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json, VisualFlowCodec.Utf8); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Rejects retained flow identities, nested action groups and malformed portable slots.</summary>
    private static void ValidateTemplate(VisualActionPreset value)
    {
        if (value is null || value.Id == Guid.Empty || value.Name is null || value.Name.Length is < 1 or > 80 || value.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(value.Name) || value.Template is null || value.Template.Id != Guid.Empty || value.Template.DisplayName is not null
            || value.Template.Value is null || !Enum.IsDefined(value.Template.Kind) || value.Slots is null || value.Slots.Any(slot => slot is null)
            || value.Template.Parameters is { } p && (p.Then is null || p.Else is null || p.Then.Length != 0 || p.Else.Length != 0 || p.Arguments is null || p.Comparison is null
                || p.Extraction is { } archive && archive.Name?.Parts is null || p.JoinPath is { Segments: null }
                || p.Notification is { } notification && (notification.Title?.Parts is null || notification.Message?.Parts is null)))
            throw new InvalidDataException("Invalid portable action preset.");
        var inputs = VisualFlowSchema.Inputs(value.Template).ToArray();
        if (inputs.Length > 32 || inputs.Any(input => input.Kind != FlowInputKind.Literal || input.StepId != Guid.Empty || input.Field != FlowResultField.Path
                || input.Literal is null || input.Literal.Contains('\0') || input.Literal.Length > 4096)
            || value.Slots.Select(slot => slot.Index).Distinct().Count() != value.Slots.Length
            || value.Slots.Any(slot => slot.Index < 0 || slot.Index >= inputs.Length || !Enum.IsDefined(slot.Field))) throw new InvalidDataException("Invalid action input slots.");
    }

    /// <summary>Copies a node subtree with fresh identities and repairs only its internal source references.</summary>
    internal static FlowAction Duplicate(FlowAction action)
    {
        var map = VisualFlowTree.Walk([action]).ToDictionary(row => row.Action.Id, _ => Guid.NewGuid());
        FlowAction Clone(FlowAction current)
        {
            var changed = VisualFlowSchema.MapInputs(current, input => input.Kind == FlowInputKind.Result && map.TryGetValue(input.StepId, out var id) ? input with { StepId = id } : input);
            return changed with { Id = map[current.Id], Parameters = changed.Parameters is { } p ? p with { Then = p.Then.Select(Clone).ToArray(), Else = p.Else.Select(Clone).ToArray() } : null };
        }
        return Clone(action);
    }
}
