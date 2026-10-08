using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Text.Json.Nodes;

/// <summary>Checks the new language and legacy golden files without launching an interpreter or manager.</summary>
internal static class ConfigurableActionChecks
{
    /// <summary>Captures the pre-change v2 contract once, then exercises schema v3 acceptance.</summary>
    internal static void Model(string root, bool captureBaseline)
    {
        var legacy = VisualFlowExamples.Explorer() with { Revision = 1 };
        var source = VisualFlowGenerator.Generate(legacy);
        legacy = legacy with { SourceSha256 = VisualFlowGenerator.Hash(VisualFlowCodec.Utf8.GetBytes(source)) };
        var fixture = Path.Combine(root, "legacy-v2.ahk");
        if (captureBaseline)
        {
            File.WriteAllText(fixture, source, VisualFlowCodec.Utf8);
            File.WriteAllText(fixture + ".flow.json", VisualFlowCodec.Encode(legacy), VisualFlowCodec.Utf8);
            Console.WriteLine("PASS: captured exact pre-change v2 source and document");
        }
        else
        {
            var old = VisualFlowCodec.Decode(File.ReadAllText(fixture + ".flow.json"));
            Check(VisualFlowGenerator.Generate(old) == File.ReadAllText(fixture), "v2 source remains byte-compatible after new helpers");
        }
        var json = JsonNode.Parse(VisualFlowCodec.Encode(legacy))!;
        json["schemaVersion"] = 3;
        json["actions"] = JsonNode.Parse("""
            [{"id":"2fbb792c-66b8-480b-b725-536c1d2ec53f","kind":"OpenTerminal","value":"","folder":"Documents","delayMs":0,
              "parameters":{"input":{"kind":"Literal","literal":"C:\\Windows","stepId":"00000000-0000-0000-0000-000000000000","field":"Path"},
              "terminal":{"mode":"Custom","program":"WindowsPowerShell","position":"Below","width":1000,"height":600,"gap":16,"waitReady":true,"timeoutMs":4000}}}]
            """);
        var configured = VisualFlowCodec.Decode(json.ToJsonString());
        Check(configured.SchemaVersion == 3, "v3 configurable terminal is accepted");
        Check(VisualFlowCodec.Encode(configured).Contains("1000"), "configured terminal dimensions survive serialization");
    }

    /// <summary>Reports only assertions backed by the current invocation.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
