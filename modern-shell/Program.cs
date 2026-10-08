using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Text.Json;

namespace AutoHotkeyUX.Modern;

/// <summary>Keeps extraction commands headless and establishes one manager before initializing WinUI.</summary>
internal static class Program
{
    internal static int ExitCode { get; set; }
    /// <summary>Runs a one-shot helper or starts the normal/silent single-instance desktop application.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--flow-extract-v3")
                return VisualExtractionCommand.RunV3(args[1]);
            if (args.Length == 4 && args[0] == "--flow-extract")
                return VisualExtractionCommand.Run(args[1], args[2], args[3]);
            if (args.Length == 2 && args[0] == "--extract")
            {
                var preferences = ShortcutPreferenceCodec.Decode(new AutoHotkeySettings().Read("Modern", ShortcutPreferenceCodec.SettingName)).Preferences;
                if (!preferences.ArchiveEnabled) throw new InvalidOperationException("Quick archive extraction is disabled in Settings.");
                var destination = new ArchiveExtractionService().Extract(args[1], preferences.ArchiveDestination == "custom" ? preferences.ArchiveFolder : null);
                Console.WriteLine(destination);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--verify-runtime")
            {
                var runtime = new EmbeddedAutoHotkeyRuntime().EnsureReady();
                var resource = typeof(Program).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern.ExplorerShortcuts.ahk");
                using (resource)
                    File.WriteAllText(args[1], JsonSerializer.Serialize(new
                    { runtime.RuntimePath, runtime.Version, ExplorerScriptEmbedded = resource is not null }));
                return 0;
            }
            var verifyPreferences = Array.IndexOf(args, "--verify-preferences");
            var verifyVisual = Array.IndexOf(args, "--verify-visual-creation");
            if (verifyPreferences >= 0 && verifyPreferences + 1 >= args.Length) throw new ArgumentException("Specify an absolute preference verification report path.");
            if (verifyVisual >= 0 && verifyVisual + 1 >= args.Length) throw new ArgumentException("Specify an absolute visual creation verification report path.");
            using var singleInstance = new SingleInstanceService(verifyPreferences >= 0 || verifyVisual >= 0 ? Guid.NewGuid().ToString("N") : null);
            if (!singleInstance.IsPrimary) { singleInstance.Redirect(args); return 0; }
            if (args.Contains("--exit-manager")) return 0;
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(launch =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App(args, singleInstance);
            });
            return ExitCode;
        }
        catch (Exception ex)
        {
            ServiceDiagnostics.Write("Program", "Application command failed.", ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
