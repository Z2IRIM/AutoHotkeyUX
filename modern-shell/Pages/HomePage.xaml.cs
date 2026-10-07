using AutoHotkeyUX.Modern.Services;
using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class HomePage : Page
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly Action _openNewScript;
    private readonly Action _openSettings;
    private readonly ScriptCatalogService _catalog;
    private readonly Action<WorkspaceTool> _openTool;
    private bool _subscribed;

    /// <summary>
    /// Creates the Home page around the shared AutoHotkey integration service.
    /// </summary>
    internal HomePage(
        AutoHotkeyIntegration integration,
        Action openNewScript,
        Action openSettings,
        ScriptCatalogService catalog,
        Action<WorkspaceTool> openTool)
    {
        InitializeComponent();
        _integration = integration;
        _openNewScript = openNewScript;
        _openSettings = openSettings;
        _catalog = catalog;
        _openTool = openTool;
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    /// <summary>
    /// Refreshes the runtime badge and exact discovery path.
    /// </summary>
    internal void RefreshRuntime()
    {
        var runtime = _integration.FindRuntime();
        if (runtime is null)
        {
            RuntimeStatusIcon.Glyph = "";
            RuntimeStatusText.Text = "Built-in runtime unavailable";
            RuntimeDetailsText.Text =
                "The bundled AutoHotkey runtime could not be prepared.";
            RuntimeReadyBadge.Visibility = Visibility.Collapsed;
            ManageRuntimeButton.Content = "Retry";
            return;
        }

        RuntimeStatusIcon.Glyph = "";
        RuntimeStatusText.Text = $"AutoHotkey {runtime.Version}";
        RuntimeDetailsText.Text =
            $"{runtime.Path} · {runtime.Architecture} · {runtime.DiscoverySource}";
        RuntimeReadyBadge.Visibility = Visibility.Visible;
        ManageRuntimeButton.Content = "Manage";
    }

    /// <summary>
    /// Refreshes runtime and recent-script information whenever the Home page returns to view.
    /// </summary>
    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRuntime();
        if (!_subscribed)
        {
            _catalog.Changed += Catalog_Changed;
            _subscribed = true;
        }
        RefreshRecentScripts();
    }

    /// <summary>Detaches shared catalog updates while Home is not visible.</summary>
    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    { _subscribed = false; _catalog.Changed -= Catalog_Changed; }

    /// <summary>Dispatches filesystem notifications to the Home UI only while it remains loaded.</summary>
    private void Catalog_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { if (_subscribed) RefreshRecentScripts(); });

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
    /// Opens the user's AutoHotkey documents directory without requiring a shell file association.
    /// </summary>
    private void OpenScriptsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        RunAction("Open scripts folder", () => _integration.OpenScriptsFolder(_catalog.RootDirectory));
    }

    /// <summary>
    /// Navigates to the app-owned Window Spy page.
    /// </summary>
    private void WindowSpyButton_Click(object sender, RoutedEventArgs e)
    {
        _openTool(WorkspaceTool.WindowSpy);
    }

    /// <summary>
    /// Navigates to the app-owned compiler form.
    /// </summary>
    private void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        _openTool(WorkspaceTool.Compile);
    }

    /// <summary>
    /// Navigates to the app-owned offline documentation page.
    /// </summary>
    private void DocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        _openTool(WorkspaceTool.Documentation);
    }

    /// <summary>
    /// Displays the three newest entries from the same catalog used by Scripts.
    /// </summary>
    private void RefreshRecentScripts()
    {
        var recent = _catalog.Snapshot().Take(3).Select(entry => new
        { entry.Name, entry.Path, ModifiedText = FormatRelativeTime(entry.LastModifiedUtc) }).ToArray();
        RecentScriptsList.ItemsSource = recent;
        RecentScriptsEmptyText.Visibility = recent.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_catalog.LastError is not null)
        { ActionInfoBar.Message = _catalog.LastError; ActionInfoBar.Severity = InfoBarSeverity.Warning; ActionInfoBar.IsOpen = true; }
    }

    /// <summary>
    /// Produces a compact, stable relative timestamp for the recent-scripts surface.
    /// </summary>
    private static string FormatRelativeTime(DateTime modifiedUtc)
    {
        var elapsed = DateTime.UtcNow - modifiedUtc;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)}h ago";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"{Math.Max(1, (int)elapsed.TotalDays)}d ago";
        }

        return modifiedUtc
            .ToLocalTime()
            .ToString("yyyy-MM-dd");
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
