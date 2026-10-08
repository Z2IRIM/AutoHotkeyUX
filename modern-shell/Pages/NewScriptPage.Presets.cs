using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    private MenuFlyout? _presetFlyout;

    /// <summary>Adds a native floating menu for user-owned single-node configurations.</summary>
    private void BuildPresetLibrary()
    {
        _presetFlyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop,
            OverlayInputPassThroughElement = ActionLibrary };
        var style = new Style { TargetType = typeof(MenuFlyoutPresenter) };
        style.Setters.Add(new Setter(MinWidthProperty, 240d)); style.Setters.Add(new Setter(MaxWidthProperty, 320d));
        _presetFlyout.MenuFlyoutPresenterStyle = style;
        _presetFlyout.Opening += (_, _) => RefreshPresetMenu(); _presetFlyout.Opening += LibraryFlyout_Opening;
        var button = new Button { Content = "My actions", Flyout = _presetFlyout, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44, Margin = new Thickness(0, 10, 0, 0) };
        ActionLibrary.Children.Add(button); _libraryCategories.Add(button);
    }

    /// <summary>Loads presets only when requested and retains normal native keyboard/dismissal behavior.</summary>
    private void RefreshPresetMenu()
    {
        if (_presetFlyout is null) return;
        _presetFlyout.Items.Clear();
        try
        {
            var presets = _services.ActionPresets.Read();
            foreach (var preset in presets)
            {
                var item = new MenuFlyoutItem { Text = preset.Name, IsEnabled = !_busy && VisualFlowTree.CanInsert(_session.Document.Actions, _insertion, preset.Template.Kind),
                    Icon = new FontIcon { Glyph = FlowActionCard.Icon(preset.Template.Kind) } };
                item.Click += async (_, _) => await InsertPresetAsync(preset); _presetFlyout.Items.Add(item);
            }
            if (presets.Count == 0) _presetFlyout.Items.Add(new MenuFlyoutItem { Text = "Save a configured action first", IsEnabled = false });
            _presetFlyout.Items.Add(new MenuFlyoutSeparator());
            var manage = new MenuFlyoutItem { Text = "Manage presets…", IsEnabled = presets.Count > 0 };
            manage.Click += async (_, _) => await ManagePresetsAsync(); _presetFlyout.Items.Add(manage);
        }
        catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
    }

    /// <summary>Names and stores a configured node after full draft validation.</summary>
    private async void SavePreset_Click(object sender, RoutedEventArgs args)
    {
        if (_busy || _session.Selection is not { } selected || _branchSelection is not null) return;
        var action = VisualFlowTree.Find(_session.Document.Actions, selected)!;
        var name = new TextBox { Header = "Preset name", Text = action.DisplayName ?? FlowActionCard.Label(action.Kind), MaxLength = 80 };
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "Input sources are selected again when inserting this preset. Branch actions are not included.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Save action preset", Content = body, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        _busy = true; SetEditingEnabled(false);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var preset = VisualActionPresets.Capture(_session.Document, selected, name.Text);
            await Task.Run(() => _services.ActionPresets.Save(preset));
            ShowCreateMessage("Saved “" + preset.Name + "” in My actions.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ServiceDiagnostics.Write("VisualFlow", "Preset save failed.", ex); ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; SetEditingEnabled(true); RefreshValidation(); }
    }

    /// <summary>Rebinds all portable inputs before inserting a fresh node into the selected sequence.</summary>
    private async Task InsertPresetAsync(VisualActionPreset preset)
    {
        if (_busy) return;
        var target = preset.Template with { Id = Guid.NewGuid() };
        var sequence = VisualFlowTree.Sequence(_session.Document.Actions, _insertion).ToList();
        var position = _branchSelection is null ? sequence.FindIndex(action => action.Id == _session.Selection) : -1;
        sequence.Insert(position < 0 ? sequence.Count : position + 1, target);
        var candidate = VisualFlowSchema.Upgrade(_session.Document with { Actions = VisualFlowTree.SetSequence(_session.Document.Actions, _insertion, sequence.ToArray()) });
        var controls = new Dictionary<int, (ComboBox Source, TextBox Literal)>();
        var bindings = new Dictionary<int, FlowInput>();
        var body = new StackPanel { Spacing = 12 };
        foreach (var slot in preset.Slots)
        {
            var choices = new List<FlowSourceChoice> { new("Fixed value", new()) };
            foreach (var producer in VisualFlowTree.Available(candidate.Actions, target.Id))
                foreach (var field in VisualFlowTree.Outputs(producer, 3).Where(field => slot.Field is FlowResultField.ProcessId or FlowResultField.WindowId
                    ? field is FlowResultField.ProcessId or FlowResultField.WindowId : slot.Field is FlowResultField.Path or FlowResultField.Directory or FlowResultField.ParentDirectory
                        ? field is FlowResultField.Path or FlowResultField.Directory or FlowResultField.ParentDirectory or FlowResultField.Text : TextField(field)))
                    choices.Add(new(FlowActionCard.StepLabel(candidate, producer.Id) + " → " + VisualFlowSchema.FieldLabel(field), FlowInput.Reference(producer.Id, field)));
            var source = new ComboBox { Header = "Input " + (slot.Index + 1) + " · " + VisualFlowSchema.FieldLabel(slot.Field), ItemsSource = choices,
                DisplayMemberPath = "Label", SelectedIndex = choices.Count > 1 ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var text = new TextBox { Header = "Fixed value", MaxLength = 2048, Visibility = choices.Count > 1 ? Visibility.Collapsed : Visibility.Visible };
            source.SelectionChanged += (_, _) => text.Visibility = (source.SelectedItem as FlowSourceChoice)?.Input?.Kind == FlowInputKind.Literal ? Visibility.Visible : Visibility.Collapsed;
            body.Children.Add(source); body.Children.Add(text); controls.Add(slot.Index, (source, text));
        }
        _busy = true; SetEditingEnabled(false);
        try
        {
            if (controls.Count > 0)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Insert “" + preset.Name + "”", Content = new ScrollViewer { Content = body, MaxHeight = 440 },
                    PrimaryButtonText = "Insert", CloseButtonText = "Cancel" };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                foreach (var pair in controls) bindings[pair.Key] = (pair.Value.Source.SelectedItem as FlowSourceChoice)?.Input is { Kind: FlowInputKind.Result } result
                    ? result : new() { Literal = pair.Value.Literal.Text };
            }
            var bound = VisualActionPresets.Bind(preset, bindings);
            sequence[sequence.IndexOf(target)] = bound;
            candidate = VisualFlowSchema.Upgrade(_session.Document with { Actions = VisualFlowTree.SetSequence(_session.Document.Actions, _insertion, sequence.ToArray()) });
            VisualFlowValidation.ValidateAction(candidate, bound); _session.Replace(candidate); _session.Selection = bound.Id; _branchSelection = null;
        }
        catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; SetEditingEnabled(true); RenderDocument(); }
    }

    /// <summary>Renames and removes only explicit user selections from the preset library.</summary>
    private async Task ManagePresetsAsync()
    {
        if (_busy) return;
        var panel = new StackPanel { Spacing = 12 };
        foreach (var preset in _services.ActionPresets.Read())
        {
            var name = new TextBox { Text = preset.Name, MaxLength = 80 };
            var row = new StackPanel { Spacing = 6 }; row.Children.Add(name);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var rename = new Button { Content = "Rename" }; var remove = new Button { Content = "Delete" };
            rename.Click += (_, _) => { try { _services.ActionPresets.Save(preset with { Name = name.Text.Trim() }); } catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); } };
            remove.Click += (_, _) => { try { _services.ActionPresets.Delete(preset.Id); row.Visibility = Visibility.Collapsed; } catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); } };
            buttons.Children.Add(rename); buttons.Children.Add(remove); row.Children.Add(buttons); panel.Children.Add(row);
        }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "My actions", Content = new ScrollViewer { Content = panel, MaxHeight = 440 }, CloseButtonText = "Done" };
        await dialog.ShowAsync();
    }

    /// <summary>Duplicates one subtree, allocating new identities and repairing internal references.</summary>
    private void DuplicateAction_Click(object sender, RoutedEventArgs args)
    {
        if (_busy || _session.Selection is not { } selected || _branchSelection is not null) return;
        var row = VisualFlowTree.Walk(_session.Document.Actions).First(item => item.Action.Id == selected);
        if (VisualFlowTree.Walk(_session.Document.Actions).Count() + VisualFlowTree.Walk([row.Action]).Count() > VisualFlowCodec.MaximumActions) return;
        var name = row.Action.DisplayName ?? FlowActionCard.Label(row.Action.Kind);
        var duplicate = VisualActionPresets.Duplicate(row.Action) with { DisplayName = name[..Math.Min(name.Length, 75)] + " copy" };
        var sequence = VisualFlowTree.Sequence(_session.Document.Actions, row.Branch).ToList(); sequence.Insert(sequence.IndexOf(row.Action) + 1, duplicate);
        _session.Replace(_session.Document with { Actions = VisualFlowTree.SetSequence(_session.Document.Actions, row.Branch, sequence.ToArray()) });
        _session.Selection = duplicate.Id; RenderDocument();
    }
}
