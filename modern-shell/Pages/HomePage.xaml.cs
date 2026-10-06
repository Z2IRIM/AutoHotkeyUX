using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace AutoHotkeyUX.Modern.Pages;

internal sealed record RecentScriptItem(
    string Name,
    string Path,
    string ModifiedText);

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
        RefreshRecentScripts();
    }

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
        var directory = GetScriptsDirectory();
        Directory.CreateDirectory(directory);

        Process.Start(
            new ProcessStartInfo(
                "explorer.exe",
                $"\"{directory}\"")
            {
                UseShellExecute = true
            });
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
    /// Reads up to three recently modified .ahk files from the normal Documents\AutoHotkey workspace.
    /// </summary>
    private void RefreshRecentScripts()
    {
        var directory = GetScriptsDirectory();

        if (!Directory.Exists(directory))
        {
            RecentScriptsList.ItemsSource = Array.Empty<RecentScriptItem>();
            RecentScriptsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var recent = Directory
                .EnumerateFiles(
                    directory,
                    "*.ahk",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(3)
                .Select(file => new RecentScriptItem(
                    file.Name,
                    file.FullName,
                    FormatRelativeTime(file.LastWriteTimeUtc)))
                .ToList();

            RecentScriptsList.ItemsSource = recent;
            RecentScriptsEmptyText.Visibility =
                recent.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        catch
        {
            RecentScriptsList.ItemsSource = Array.Empty<RecentScriptItem>();
            RecentScriptsEmptyText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Resolves the conventional per-user script workspace used by the UX shell.
    /// </summary>
    private static string GetScriptsDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoHotkey");

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
