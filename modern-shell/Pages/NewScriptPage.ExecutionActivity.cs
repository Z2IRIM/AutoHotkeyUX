using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    private bool _activityRendering;
    private bool _activitySubscribed;
    private sealed record ExecutionChoice(Guid Id, string Label);
    private sealed record ExecutionRow(string Title, string Detail);

    /// <summary>Subscribes while this page is visible, reusing the manager's bounded event-driven activity.</summary>
    private void Execution_Loaded(object sender, RoutedEventArgs args)
    { if (!_activitySubscribed) { _services.ShortcutActivity.Changed += ExecutionActivity_Changed; _activitySubscribed = true; } RefreshExecutionActivity(); }
    /// <summary>Detaches page updates when navigation unloads this editor.</summary>
    private void Execution_Unloaded(object sender, RoutedEventArgs args)
    { _services.ShortcutActivity.Changed -= ExecutionActivity_Changed; _activitySubscribed = false; }
    /// <summary>Marshals worker feedback onto the existing WinUI dispatcher.</summary>
    private void ExecutionActivity_Changed(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() => { if (_activitySubscribed) RefreshExecutionActivity(); });
    /// <summary>Selects another retained invocation without changing the workflow document.</summary>
    private void ExecutionRun_Changed(object sender, SelectionChangedEventArgs args) { if (!_activityRendering) RefreshExecutionActivity(); }

    /// <summary>Shows branches, exact output paths and failures for only this document's selected invocation.</summary>
    private void RefreshExecutionActivity()
    {
        _activityRendering = true;
        try
        {
            var entries = _services.ShortcutActivity.Snapshot().Where(item => item.FlowId == _session.Document.Id).ToArray();
            var selected = (ExecutionRunCombo.SelectedItem as ExecutionChoice)?.Id;
            var choices = entries.GroupBy(item => item.RunId).Select(group => new ExecutionChoice(group.Key, group.First().Time.ToString("HH:mm:ss") + " · " + group.Key.ToString("N")[..8])).ToArray();
            ExecutionRunCombo.ItemsSource = choices; ExecutionRunCombo.DisplayMemberPath = "Label";
            ExecutionRunCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices.FirstOrDefault();
            var run = (ExecutionRunCombo.SelectedItem as ExecutionChoice)?.Id;
            ExecutionItems.ItemsSource = entries.Where(item => item.RunId == run).Reverse().Select(item => new ExecutionRow(item.StepName + " · " + item.State + " · " + item.DurationMilliseconds + " ms", item.Detail)).ToArray();
            ExecutionEmptyText.Visibility = choices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ExecutionRunCombo.Visibility = choices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _activityRendering = false; }
    }
}
