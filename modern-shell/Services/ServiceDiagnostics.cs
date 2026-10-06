using System.Diagnostics;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Writes small, bounded lifecycle diagnostics without introducing a logging subsystem.</summary>
internal static class ServiceDiagnostics
{
    private static readonly object Gate = new();
    internal static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoHotkeyUX.Modern", "state");

    /// <summary>Records a component/action and preserves the original exception details.</summary>
    internal static void Write(string component, string message, Exception? error = null)
    {
        var line = $"{DateTimeOffset.Now:O} [{component}] {message}{(error is null ? "" : $"\n{error}")}\n";
        Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                var path = Path.Combine(StateDirectory, "manager.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Unable to write manager diagnostics: {logError}");
            }
        }
    }
}
