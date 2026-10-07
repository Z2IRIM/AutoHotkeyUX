using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Reproduces review findings through native fields and legal external file updates in the isolated workspace.</summary>
    internal async Task<object> VerifyReviewBoundariesAsync(Action revisit)
    {
        var failures = new List<string>();
        Trigger_Click(this, new RoutedEventArgs());
        ApplicationBox.Text = ""; await Task.Delay(60);
        CheckReview(!SaveButton.IsEnabled && FieldErrorsText.Visibility == Visibility.Visible, "Empty application scope permits saving", failures);
        ActionList.SelectedIndex = 0; await Task.Delay(60);
        Trigger_Click(this, new RoutedEventArgs()); revisit(); await Task.Delay(60);
        CheckReview(ScopeCombo.SelectedIndex == 1 && ApplicationBox.Visibility == Visibility.Visible && ApplicationBox.Text == "" && !SaveButton.IsEnabled,
            "Incomplete application scope was lost by selection or navigation", failures);
        Undo_Click(this, new RoutedEventArgs());
        CheckReview(ScopeCombo.SelectedIndex == 1 && ApplicationBox.Text == "explorer.exe", "Undo lost the scoped application", failures);
        Redo_Click(this, new RoutedEventArgs());
        CheckReview(ScopeCombo.SelectedIndex == 1 && ApplicationBox.Text == "" && !SaveButton.IsEnabled, "Redo made an incomplete scope global", failures);
        ApplicationBox.Text = "explorer.exe"; await Task.Delay(60);

        _cards.Move(0, 1); CommitCardOrder();
        CheckReview(_cards.Select((card, index) => card.Number == index + 1).All(value => value), "Native reorder leaves stale card numbers", failures);
        Undo_Click(this, new RoutedEventArgs());

        var external = await Task.Run(() => _services.VisualFlows.Update(_opened!, _opened!.Document with
        { Actions = _opened.Document.Actions.Select(action => action.Kind == FlowActionKind.Wait ? action with { DelayMs = 900 } : action).ToArray() }));
        await OpenAsync(external.ScriptPath, XamlRoot);
        CheckReview(_opened?.SourceHash == external.SourceHash && _opened.SidecarHash == external.SidecarHash
            && _session.Document.Actions.Single(action => action.Kind == FlowActionKind.Wait).DelayMs == 900, "Same-path reopen retains an old legal revision", failures);
        // Reset only the probe after recording a failure so the independent byte-formatting boundary still runs.
        _opened = external; _session.Load(external.Document); RememberSaved(); RenderDocument();
        await File.AppendAllTextAsync(external.ScriptPath + ".flow.json", "\n");
        var formatted = _services.VisualFlows.Open(external.ScriptPath);
        await OpenAsync(external.ScriptPath, XamlRoot);
        CheckReview(_opened?.SidecarHash == formatted.SidecarHash, "Same-path reopen retains stale sidecar bytes", failures);
        var opening = OpenAsync(external.ScriptPath, XamlRoot);
        var guarded = false;
        try { await OpenAsync(external.ScriptPath, XamlRoot); }
        catch (InvalidOperationException) { guarded = true; }
        catch (IOException) { }
        try { await opening; }
        catch (IOException) { failures.Add("First overlapping open loses the workflow claim"); }
        CheckReview(guarded, "Overlapping workflow opens are accepted", failures);

        if (failures.Count > 0) throw new InvalidOperationException("Review boundaries: " + string.Join("; ", failures));
        _session.Selection = _session.Document.Actions.Single(action => action.Kind == FlowActionKind.Wait).Id; RenderDocument();
        WaitBox.Value = 1000; await Task.Delay(60); await SaveDraftAsync();
        if (_opened?.Document.Revision != 4 || _services.VisualFlows.Open(_opened.ScriptPath).Document.Actions.Single(action => action.Kind == FlowActionKind.Wait).DelayMs != 1000)
            throw new InvalidOperationException("Refreshed same-path workflow cannot be saved.");
        return new { Passed = true, IncompleteScopeRetained = true, ReorderNumbersCorrect = true, ExternalRevisionReloaded = true,
            ReformattedSidecarReloaded = true, ConcurrentOpenRejected = true, UpdatedRevision = 4 };
    }

    /// <summary>Collects independent review failures without hiding later reproduction evidence.</summary>
    private static void CheckReview(bool condition, string message, List<string> failures) { if (!condition) failures.Add(message); }
}
