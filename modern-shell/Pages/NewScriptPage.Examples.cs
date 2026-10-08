using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Shows editable practical starters in the existing app surface.</summary>
    private void Examples_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement target) return;
        var menu = new MenuFlyout();
        foreach (var (label, name, create) in new (string, string, Func<VisualFlowDocument>)[]
        { ("Blank workflow", "QuickActions", VisualFlowExamples.Blank),
          ("Terminal / extract at clicked object", "ExplorerActions", VisualFlowExamples.ExplorerV3),
          ("Open terminal in current directory", "OpenTerminal", VisualFlowExamples.Terminal),
          ("Extract selected archive", "ExtractArchive", VisualFlowExamples.Extract),
          ("Copy selected object's full path", "CopySelectedPath", VisualFlowExamples.CopyPath),
          ("Launch program, wait, activate and type", "LaunchAndType", VisualFlowExamples.LaunchAndType) })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += async (_, _) => await LoadExampleAsync(name, create()); menu.Items.Add(item);
        }
        menu.ShowAt(target);
    }
    /// <summary>Replaces a draft only through the existing unsaved-change confirmation and never runs the starter.</summary>
    private async Task LoadExampleAsync(string name, VisualFlowDocument flow)
    {
        if (_busy) return;
        _busy = true; SetEditingEnabled(false);
        try
        {
            if (!await CanReplaceDraftAsync(XamlRoot)) return;
            _opened = null; _insertion = default; _branchSelection = null; _session.Load(flow);
            _rendering = true; ScriptNameTextBox.Text = name; ScriptLocationTextBox.Text = _services.Catalog.RootDirectory; _rendering = false;
            _baseline = flow; _baselineName = name; _baselineDirectory = ScriptLocationTextBox.Text;
            PageTitle.Text = "Build a workflow"; SavedActions.Visibility = Visibility.Collapsed;
        }
        finally { _busy = false; SetEditingEnabled(true); RenderDocument(); }
    }
    /// <summary>Returns the insertion point to the main sequence without moving existing actions.</summary>
    private void RootInsertion_Click(object sender, RoutedEventArgs e) { if (_busy) return; _insertion = default; _branchSelection = null; RenderDocument(); }
}
