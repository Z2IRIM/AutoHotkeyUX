using AutoHotkeyUX.Modern.Models;
using System.Security.Cryptography;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Generates deterministic AutoHotkey v2 source from the supported visual language without executing it.</summary>
internal static class VisualFlowGenerator
{
    internal const string OwnershipMarker = "; AutoHotkey UX visual workflow v1";

    /// <summary>Emits literal actions in order, applying scope to the correct trigger boundary.</summary>
    internal static string Generate(VisualFlowDocument value)
    {
        VisualFlowCodec.Validate(value);
        if (value.SchemaVersion == 2) return VisualFlowGeneratorV2.Generate(value);
        var code = new StringBuilder(OwnershipMarker + "\r\n")
            .Append("; Workflow: ").Append(value.Id.ToString("D")).Append("\r\n")
            .Append("#Requires AutoHotkey v2.0\r\n#SingleInstance Ignore\r\n\r\n");
        var trigger = value.Trigger;
        var scoped = trigger.Scope == FlowScopeKind.ActiveApplication;
        var condition = "WinActive(" + Literal("ahk_exe " + trigger.Application) + ")";
        if (trigger.Kind == FlowTriggerKind.Hotkey)
        {
            if (scoped) code.Append("#HotIf ").Append(condition).Append("\r\n");
            foreach (var (flag, symbol) in new[] { (FlowModifiers.Ctrl, "^"), (FlowModifiers.Alt, "!"), (FlowModifiers.Shift, "+"), (FlowModifiers.Win, "#") })
                if (trigger.Modifiers.HasFlag(flag)) code.Append(symbol);
            code.Append(trigger.Key.ToLowerInvariant()).Append(":: {\r\n");
        }
        else if (scoped) code.Append("if ").Append(condition).Append(" {\r\n");
        var indent = trigger.Kind == FlowTriggerKind.Hotkey || scoped ? "    " : "";
        foreach (var action in value.Actions) code.Append(indent).Append(ActionSource(action)).Append("\r\n");
        if (indent.Length > 0) code.Append("}\r\n");
        if (trigger.Kind == FlowTriggerKind.Hotkey && scoped) code.Append("#HotIf\r\n");
        return code.ToString();
    }

    /// <summary>Hashes exact file bytes so BOMs, line endings and manual edits remain observable.</summary>
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Escapes user text as one AHK string literal, never as statements or key syntax.</summary>
    internal static string Literal(string value) => "\"" + value.Replace("`", "``", StringComparison.Ordinal)
        .Replace("\"", "`\"", StringComparison.Ordinal).Replace("\r", "`r", StringComparison.Ordinal)
        .Replace("\n", "`n", StringComparison.Ordinal).Replace("\t", "`t", StringComparison.Ordinal) + "\"";

    /// <summary>Maps each validated action to one fixed statement with literal parameters.</summary>
    internal static string ActionSource(FlowAction action) => action.Kind switch
    {
        FlowActionKind.OpenProgram => "Run Chr(34) . " + Literal(action.Value) + " . Chr(34)",
        FlowActionKind.OpenFolder => "Run \"explorer.exe \" . Chr(34) . " + (action.Folder switch
        { FlowFolderKind.Documents => "A_MyDocuments", FlowFolderKind.Desktop => "A_Desktop", _ => Literal(action.Value) }) + " . Chr(34)",
        FlowActionKind.OpenWebsite => "Run " + Literal(action.Value),
        FlowActionKind.SendText => "SendText " + Literal(action.Value),
        FlowActionKind.SendKeys => "Send " + Literal(action.Value switch
        { "Enter" => "{Enter}", "Tab" => "{Tab}", "Escape" => "{Escape}", "Ctrl+C" => "^c", "Ctrl+V" => "^v", _ => throw new InvalidDataException("Unsupported keys.") }),
        FlowActionKind.Wait => "Sleep " + action.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException("Unsupported action.")
    };
}
