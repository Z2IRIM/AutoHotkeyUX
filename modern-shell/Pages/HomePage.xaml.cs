using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class HomePage : Page
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly Action _openNewScript;
    private readonly Action _openSettings;

    /// <summary>
    /// Creates the Home page around the shared AutoHotkey integration service.
    /// </summary>
    internal HomePage(
        AutoHotkeyIntegration integration,
        Action openNewScript,
        Action openSettings)
    {
        InitializeComponent();
        _integration = integration;
        _openNewScript = openNewScript;
        _openSettings = openSettings;
        Loaded += HomePage_Loaded;
    }

    /// <summary>
    /// Refreshes the runtime badge and exact discovery path.
    /// </summary>
    internal void RefreshRuntime()
    {
        var runtime = _integration.FindRuntime();
        if (runtime is null)
        {
            RuntimeStatusIcon.Glyph = "\uE711";
            RuntimeStatusText.Text = "Built-in runtime unavailable";
            RuntimeDetailsText.Text =
                "The bundled AutoHotkey runtime could not be prepared. Retry, then check startup diagnostics if the problem continues.";
            ManageRuntimeButton.Content = "Retry";
            return;
        }

        RuntimeStatusIcon.Glyph = "\uE73E";
        RuntimeStatusText.Text = $"AutoHotkey {runtime.Version} detected";
        RuntimeDetailsText.Text =
            $"{runtime.Path} · {runtime.Architecture} · discovered via {runtime.DiscoverySource}";
        ManageRuntimeButton.Content = "Manage";
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e) => RefreshRuntime();

    private void NewScriptButton_Click(object sender, RoutedEventArgs e) => _openNewScript();

    /// <summary>
    /// Opens settings when a runtime is ready or retries bundled-runtime materialization.
    /// </summary>
    private void ManageRuntimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_integration.FindRuntime() is not null)
        {
            _openSettings();
            return;
        }

        RefreshRuntime();

        if (_integration.FindRuntime() is null)
        {
            ActionInfoBar.Title = "Built-in AutoHotkey runtime";
            ActionInfoBar.Message =
                "The bundled runtime is still unavailable. Restart the app and check startup-error.log if this persists.";
            ActionInfoBar.Severity = InfoBarSeverity.Warning;
            ActionInfoBar.IsOpen = true;
        }
    }

    /// <summary>
    /// Opens Window Spy and surfaces launch failures inside the page.
    /// </summary>
    private void WindowSpyButton_Click(object sender, RoutedEventArgs e)
    {
        RunAction("Window Spy", _integration.OpenWindowSpy);
    }

    /// <summary>
    /// Opens Ahk2Exe or the existing compiler installer.
    /// </summary>
    private void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        RunAction("Compile", _integration.OpenCompiler);
    }

    /// <summary>
    /// Opens local AutoHotkey help when available and otherwise the official online docs.
    /// </summary>
    private void DocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        RunAction("Documentation", _integration.OpenDocumentation);
    }

    /// <summary>
    /// Executes a quick action and reports failures without terminating the shell.
    /// </summary>
    private void RunAction(string title, Action action)
    {
        try
        {
            action();
            ActionInfoBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ActionInfoBar.Title = title;
            ActionInfoBar.Message = ex.Message;
            ActionInfoBar.Severity = InfoBarSeverity.Warning;
            ActionInfoBar.IsOpen = true;
        }
    }
}
