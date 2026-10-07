namespace AutoHotkeyUX.Modern.Services;

/// <summary>Composes the existing runtime with application-owned catalog, execution and startup services.</summary>
internal sealed class ApplicationServices : IDisposable
{
    internal AutoHotkeyIntegration Integration { get; }
    internal AutoHotkeySettings Settings { get; } = new();
    internal ScriptCatalogService Catalog { get; } = new();
    internal ScriptExecutionService Execution { get; }
    internal ScriptStartupService ScriptStartup { get; }
    internal WindowsStartupService WindowsStartup { get; } = new();
    internal WindowSpyService WindowSpy { get; } = new();
    internal CompilerService Compiler { get; }
    internal DocumentationService Documentation { get; }

    /// <summary>Creates one instance of each shared service without redesigning the embedded runtime.</summary>
    internal ApplicationServices()
    {
        var locator = new AutoHotkeyRuntimeLocator(new EmbeddedAutoHotkeyRuntime());
        Integration = new AutoHotkeyIntegration(locator);
        Compiler = new CompilerService(Integration, new EmbeddedCompiler());
        Documentation = new DocumentationService(Integration);
        Execution = new ScriptExecutionService(() => Integration.FindRuntime(Settings.Read(@"Launcher\v2", "Build"))?.Path,
            new ScriptSessionStore());
        ScriptStartup = new ScriptStartupService(Catalog.RootDirectory, Settings, Execution);
    }

    /// <summary>Recovers live identities before launching selected scripts and updates helper/startup executable paths.</summary>
    internal void Initialize()
    {
        Settings.Write("Modern", "ExecutablePath", Environment.ProcessPath ?? string.Empty);
        try { WindowsStartup.RefreshExecutablePath(); }
        catch (Exception ex) { ServiceDiagnostics.Write("WindowsStartup", "Existing startup command could not be updated; the workspace will continue to open.", ex); }
        Execution.Rehydrate();
        ScriptStartup.StartSelected();
        Catalog.Refresh();
    }

    /// <summary>Stops monitoring and detaches process handles while preserving running user scripts.</summary>
    public void Dispose() { Compiler.Dispose(); Catalog.Dispose(); Execution.Dispose(); }
}
