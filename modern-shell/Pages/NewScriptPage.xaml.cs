using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;

namespace AutoHotkeyUX.Modern.Pages;

/// <summary>Builds supported workflows through native action cards; services retain file and execution ownership.</summary>
public sealed partial class NewScriptPage : Page
{
    private readonly ApplicationServices _services;
    private readonly Action _openScripts;
    private readonly VisualEditorSession _session = new();
    private readonly ObservableCollection<FlowActionCard> _cards = [];
    private VisualFlowOpened? _opened;
    private VisualFlowDocument _baseline = null!;
    private string _baselineName = "QuickActions";
    private string _baselineDirectory = "";
    private bool _rendering;
    private bool _busy;
    private bool _dragging;
    private int _layoutMode = -1;
    private FlowBranch _insertion;
    private FlowBranch? _branchSelection;
    private readonly List<Button> _libraryButtons = [];

    /// <summary>Initializes the approved starter and retains its draft when the content host navigates away.</summary>
    internal NewScriptPage(ApplicationServices services, Action openScripts)
    {
        _rendering = true; InitializeComponent();
        _services = services; _openScripts = openScripts;
        ScriptLocationTextBox.Text = services.Catalog.RootDirectory;
        TriggerKeyCombo.ItemsSource = VisualFlowCodec.Keys.Select(KeyLabel).ToArray(); SendKeysCombo.ItemsSource = VisualFlowCodec.SendKeys;
        ActionList.ItemsSource = _cards; BuildLibrary();
        _baseline = _session.Document; _baselineDirectory = ScriptLocationTextBox.Text;
        _rendering = false; RenderDocument();
    }

    /// <summary>Creates uniform native buttons from the same metadata used by cards and the inspector.</summary>
    private void BuildLibrary()
    {
        foreach (var (category, kinds) in FlowActionCard.Categories)
        {
            var group = new StackPanel { Spacing = 8 };
            var expander = new Expander { Header = category, Content = group, IsExpanded = category == "Context", HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), MinWidth = 0 };
            ActionLibrary.Children.Add(expander);
            foreach (var kind in kinds)
            {
            var content = new Grid { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
            content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new FontIcon { Glyph = FlowActionCard.Icon(kind), FontSize = 16 });
            var label = new TextBlock { Text = FlowActionCard.Label(kind), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var button = new Button { Tag = kind, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56, Padding = new Thickness(10) };
            button.Click += Library_Click; group.Children.Add(button); _libraryButtons.Add(button);
            }
        }
    }

    /// <summary>Synchronizes card selection, properties and generated source after a semantic workflow edit.</summary>
    private void RenderDocument(bool properties = true)
    {
        _rendering = true;
        try
        {
            var rows = FlowActionCard.Project(_session.Document).ToArray();
            if (_cards.Count == rows.Length && _cards.Zip(rows).All(pair => pair.First.Key == pair.Second.Key))
            {
                for (var index = 0; index < rows.Length; index++) if (_cards[index] != rows[index]) _cards[index] = rows[index];
            }
            else
            {
                _cards.Clear();
                foreach (var row in rows) _cards.Add(row);
            }
            if (_insertion.ParentId.HasValue && VisualFlowTree.Find(_session.Document.Actions, _insertion.ParentId)?.Kind != FlowActionKind.IfElse)
            { _insertion = default; _branchSelection = null; }
            ActionList.SelectedItem = _branchSelection is { } selectedBranch ? _cards.FirstOrDefault(card => card.IsBranch && card.Branch == selectedBranch)
                : _cards.FirstOrDefault(card => !card.IsBranch && card.Action.Id == _session.Selection);
            var trigger = _session.Document.Trigger;
            TriggerSummaryText.Text = trigger.Kind == FlowTriggerKind.Startup ? "When script starts" : HotkeyLabel(trigger);
            ScopeSummaryText.Text = trigger.Scope == FlowScopeKind.ExplorerDesktop ? "File Explorer and desktop" : trigger.Scope == FlowScopeKind.AnyApplication ? "Any active application"
                : trigger.Application.Length == 0 ? "Choose an application" : "Only " + trigger.Application;
            TriggerButton.BorderThickness = new Thickness(_session.Selection is null ? 2 : 1);
            TriggerButton.BorderBrush = (Brush)Application.Current.Resources[_session.Selection is null ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
            ActionCountText.Text = VisualFlowTree.Walk(_session.Document.Actions).Count() + " / " + VisualFlowCodec.MaximumActions + " actions";
            var nested = _cards.Any(card => card.IsBranch);
            ActionList.CanDragItems = !nested; ActionList.CanReorderItems = !nested;
            OrderHintText.Text = nested ? "Select IF TRUE or ELSE to add there. Arrow buttons reorder within the selected sequence."
                : "Actions run from top to bottom. Drag to reorder, or use the arrow buttons.";
            InsertionHintText.Text = _insertion.ParentId is null ? "New actions append to the main sequence."
                : "New actions append to " + (_insertion.IsElse ? "ELSE" : "IF TRUE") + " of " + FlowActionCard.StepLabel(_session.Document, _insertion.ParentId.Value) + ".";
            EmptyFlowText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (properties) RenderProperties();
        }
        finally { _rendering = false; }
        RefreshValidation();
    }

    /// <summary>Validates the full draft before enabling save, keeping incomplete form input visible.</summary>
    private void RefreshValidation()
    {
        if (_rendering) return;
        string? error = null;
        try
        {
            VisualFlowCodec.Validate(_session.Document); _ = ScriptFileName.Normalize(ScriptNameTextBox.Text);
            if (!VisualFlowCodec.IsPath(ScriptLocationTextBox.Text.Trim())) throw new ArgumentException("Choose an absolute script directory.");
            ScriptPreviewTextBox.Text = VisualFlowGenerator.Generate(_session.Document);
            _ = VisualFlowCodec.Encode(_session.Document with { Revision = Math.Max(1, _session.Document.Revision + 1),
                SourceSha256 = VisualFlowGenerator.Hash(VisualFlowCodec.Utf8.GetBytes(ScriptPreviewTextBox.Text)) });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.EncoderFallbackException)
        { error = ex.Message; ScriptPreviewTextBox.Text = "Complete the workflow properties to generate its code."; }
        FieldErrorsText.Text = error ?? ""; FieldErrorsText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !_busy && error is null && (_opened is null || HasChanges());
        SaveButton.Content = _opened is null ? "Create script" : "Save changes";
        UndoButton.IsEnabled = !_busy && _session.CanUndo; RedoButton.IsEnabled = !_busy && _session.CanRedo;
        var selected = VisualFlowTree.Walk(_session.Document.Actions).FirstOrDefault(item => item.Action.Id == _session.Selection);
        var sequence = VisualFlowTree.Sequence(_session.Document.Actions, selected.Branch);
        var index = Array.FindIndex(sequence, action => action.Id == _session.Selection);
        UpButton.IsEnabled = !_busy && _branchSelection is null && index > 0; DownButton.IsEnabled = !_busy && _branchSelection is null && index >= 0 && index < sequence.Length - 1;
        DeleteButton.IsEnabled = !_busy && _branchSelection is null && index >= 0;
        foreach (var button in _libraryButtons) button.IsEnabled = !_busy && VisualFlowTree.Walk(_session.Document.Actions).Count() < VisualFlowCodec.MaximumActions;
        var conflict = VisualHotkeyConflicts.BuiltIn(_session.Document.Trigger, _services.ShortcutPreferences.Saved, _services.ScriptStartup.ExplorerEnabled);
        ShortcutWarningText.Text = conflict ?? ""; ShortcutWarningText.Visibility = conflict is null ? Visibility.Collapsed : Visibility.Visible;
        CreateScriptStatusText.Text = _opened is null ? "Creates a script and its workflow file. Existing names receive a suffix. Actions run only after you start the script."
            : "Editing " + _opened.ScriptPath + ". Save does not restart a running script; use Restart in Scripts to apply changes.";
    }

    /// <summary>Tracks semantic draft differences rather than selection or replacement array instances.</summary>
    private bool HasChanges() => _session.Document.SchemaVersion != _baseline.SchemaVersion || _session.Document.Trigger != _baseline.Trigger || !VisualFlowTree.Same(_session.Document.Actions, _baseline.Actions)
        || ScriptNameTextBox.Text != _baselineName || ScriptLocationTextBox.Text != _baselineDirectory;

    /// <summary>Shows a readable shortcut without exposing AHK prefix syntax.</summary>
    private static string HotkeyLabel(FlowTrigger trigger) => string.Join(" + ", Enum.GetValues<FlowModifiers>()
        .Where(flag => flag != FlowModifiers.None && trigger.Modifiers.HasFlag(flag)).Select(flag => flag.ToString()).Append(KeyLabel(trigger.Key)));
    /// <summary>Names mouse buttons plainly while retaining persisted AHK key tokens.</summary>
    private static string KeyLabel(string key) => key == "LButton" ? "Left mouse button" : key == "MButton" ? "Middle mouse button" : key;

    /// <summary>Moves the inspector below the flow at medium widths and stacks all panels at small widths.</summary>
    private void EditorGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var mode = e.NewSize.Width >= 960 ? 0 : e.NewSize.Width >= 640 ? 1 : 2;
        if (_layoutMode == mode) return;
        _layoutMode = mode;
        EditorGrid.ColumnDefinitions[0].Width = mode == 0 ? new GridLength(180) : mode == 1 ? new GridLength(168) : new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[1].Width = mode == 2 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[2].Width = mode == 0 ? new GridLength(280) : new GridLength(0);
        Grid.SetColumn(FlowPanel, mode == 2 ? 0 : 1); Grid.SetRow(FlowPanel, mode == 2 ? 1 : 0);
        Grid.SetColumnSpan(LibraryPanel, mode == 2 ? 3 : 1); Grid.SetColumnSpan(FlowPanel, mode == 2 ? 3 : 1);
        Grid.SetColumn(PropertiesPanel, mode == 0 ? 2 : 0); Grid.SetRow(PropertiesPanel, mode == 0 ? 0 : mode == 1 ? 1 : 2);
        Grid.SetColumnSpan(PropertiesPanel, mode == 0 ? 1 : 3);
    }

    /// <summary>Stacks name and directory fields when labels and the native picker no longer fit side by side.</summary>
    private void DetailsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 640; Grid.SetColumnSpan(ScriptNameTextBox, stacked ? 2 : 1);
        Grid.SetColumn(LocationPanel, stacked ? 0 : 1); Grid.SetRow(LocationPanel, stacked ? 1 : 0); Grid.SetColumnSpan(LocationPanel, stacked ? 2 : 1);
    }
}
