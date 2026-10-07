using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Runs an explicit workflow extraction through the existing archive owner without loading WinUI.</summary>
internal static class VisualExtractionCommand
{
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
