namespace AutoHotkeyUX.Modern.Services;

/// <summary>Composes the existing runtime with application-owned catalog, execution and startup services.</summary>
internal sealed class ApplicationServices : IDisposable
{
    internal AutoHotkeyIntegration Integration { get; }
    internal AutoHotkeySettings Settings { get; }
    internal ScriptCatalogService Catalog { get; }
    internal ScriptExecutionService Execution { get; }
    internal ScriptStartupService ScriptStartup { get; }
    internal WindowsStartupService WindowsStartup { get; }
    internal WindowSpyService WindowSpy { get; } = new();
    internal CompilerService Compiler { get; }
    internal DocumentationService Documentation { get; }
    internal ShortcutPreferencesService ShortcutPreferences { get; }
    internal ShortcutActivityService ShortcutActivity { get; } = new();
    internal VisualFlowStore VisualFlows { get; } = new();
    internal string StateDirectory { get; }
    private readonly string? _diagnosticRegistryBase;

    /// <summary>Creates one instance of each shared service without redesigning the embedded runtime.</summary>
    internal ApplicationServices(string? diagnosticRoot = null)
    {
        _diagnosticRegistryBase = diagnosticRoot is null ? null : @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        Settings = _diagnosticRegistryBase is null ? new() : new(_diagnosticRegistryBase);
        StateDirectory = diagnosticRoot is null ? ServiceDiagnostics.StateDirectory : Path.Combine(diagnosticRoot, "state");
        Catalog = new(diagnosticRoot is null ? null : Path.Combine(diagnosticRoot, "Scripts"));
        WindowsStartup = new(_diagnosticRegistryBase is null ? null : _diagnosticRegistryBase + @"\Run");
        var locator = new AutoHotkeyRuntimeLocator(new EmbeddedAutoHotkeyRuntime(diagnosticRoot is null ? null : Path.Combine(StateDirectory, "runtime")));
        Integration = new AutoHotkeyIntegration(locator);
        Compiler = new CompilerService(Integration, new EmbeddedCompiler());
        Documentation = new DocumentationService(Integration);
        Execution = new ScriptExecutionService(() => Integration.FindRuntime(Settings.Read(@"Launcher\v2", "Build"))?.Path,
            new ScriptSessionStore(diagnosticRoot is null ? null : Path.Combine(StateDirectory, "managed-sessions.json")),
            path => VisualHotkeyConflicts.Check(path, Execution!.Snapshot(), Settings, Path.Combine(Catalog.RootDirectory, "Explorer Shortcuts.ahk")));
        ScriptStartup = new ScriptStartupService(Catalog.RootDirectory, Settings, Execution);
        ShortcutPreferences = new(Settings, ScriptStartup, Execution,
            preferences => ScriptStartup.ExplorerEnabled ? VisualHotkeyConflicts.CheckBuiltInAgainstRunning(preferences, Execution.Snapshot(), ScriptStartup.ExplorerScriptPath) : null);
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
    public void Dispose()
    {
        Compiler.Dispose(); Catalog.Dispose(); Execution.Dispose();
        if (_diagnosticRegistryBase is not null) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_diagnosticRegistryBase, false);
    }
}
