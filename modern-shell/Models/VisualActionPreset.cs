namespace AutoHotkeyUX.Modern.Models;

/// <summary>Stores one configured node and explicit unbound result slots, never another flow's producer IDs.</summary>
internal sealed record VisualActionPreset(Guid Id, string Name, FlowAction Template, VisualPresetSlot[] Slots);
/// <summary>Identifies a dynamic input's position and original field type for reinsertion.</summary>
internal sealed record VisualPresetSlot(int Index, FlowResultField Field);
