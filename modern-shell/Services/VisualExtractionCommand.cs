using System.Text;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Runs an explicit workflow extraction through the existing archive owner without loading WinUI.</summary>
internal static class VisualExtractionCommand
{
    /// <summary>Uses the same immutable request and extraction owner when no warm manager exists.</summary>
    internal static int RunV3(string body)
    {
        var request = VisualFlowRequestCodec.Extraction(body);
        var root = ResultRoot();
        VisualFlowReports.Prune(root);
        if (!VisualFlowReports.Begin(root, request)) return 0;
        try
        {
            var output = new ArchiveExtractionService().Extract(request.Source, request.Destination.Length == 0 ? null : request.Destination,
                request.Name.Length == 0 ? null : request.Name, request.Collision);
            VisualFlowReports.Complete(root, request.RequestId, output, null);
            return 0;
        }
        catch (Exception ex)
        {
            ServiceDiagnostics.Write("VisualFlow", "Cold extraction failed: " + request.RequestId, ex);
            VisualFlowReports.Complete(root, request.RequestId, null, ex);
            return 1;
        }
    }

    /// <summary>Uses the launching manager's registered result owner rather than accepting a report path from the request.</summary>
    private static string ResultRoot()
    {
        var key = Environment.GetEnvironmentVariable("AUTOHOTKEYUX_FLOW_KEY");
        if (string.IsNullOrEmpty(key)) key = @"HKCU\Software\AutoHotkey\Modern";
        if (key.Length > 512 || !Regex.IsMatch(key, @"\AHKCU\\Software\\[a-z0-9_.\\-]+\\Modern\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("The manager settings context is invalid.");
        var settings = new AutoHotkeySettings(key[5..^7]);
        var root = settings.Read("Modern", "WorkflowResultRoot", Path.Combine(ServiceDiagnostics.StateDirectory, "workflow-results"));
        if (!VisualFlowCodec.IsPath(root) || !Path.GetFileName(root).Equals("workflow-results", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid registered workflow result directory.");
        if (settings.BaseKey.StartsWith(@"Software\AutoHotkeyUX.Verify\", StringComparison.OrdinalIgnoreCase)) ServiceDiagnostics.UseIsolatedDirectory(Path.GetDirectoryName(root)!);
        return root;
    }

    /// <summary>Returns the unique output directory or a failure report; never replaces an existing report.</summary>
    internal static int Run(string source, string destination, string report)
    {
        if (!VisualFlowCodec.IsPath(report)) throw new ArgumentException("Use an absolute extraction report path.");
        using var stream = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        try
        {
            if (destination.Length != 0 && (!VisualFlowCodec.IsPath(destination) || !Directory.Exists(destination)))
                throw new ArgumentException("Choose an existing absolute extraction destination.");
            var output = new ArchiveExtractionService().Extract(source, destination.Length == 0 ? null : destination);
            writer.Write("ok\n" + output);
            ServiceDiagnostics.Write("VisualFlow", "Extracted " + source + " to " + output);
            return 0;
        }
        catch (Exception ex)
        {
            writer.Write("error\n" + ex.Message);
            ServiceDiagnostics.Write("VisualFlow", "Extraction failed: " + source, ex);
            return 1;
        }
    }
}
