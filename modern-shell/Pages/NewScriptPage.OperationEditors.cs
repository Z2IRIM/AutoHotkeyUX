using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Names a node without exposing its internal action or result identity.</summary>
    private void AddDisplayNameEditor(FlowAction action)
    {
        var name = new TextBox { Header = "Display name", Text = action.DisplayName ?? FlowActionCard.Label(action.Kind), MaxLength = 80 };
        name.TextChanged += (_, _) => ChangeAction(current => current with { DisplayName = name.Text });
        ExtendedFields.Children.Add(name);
    }

    /// <summary>Adds a restrained heading within the existing property column.</summary>
    private void PropertyHeading(string text) => ExtendedFields.Children.Add(new TextBlock
    { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), FontSize = 14 });

    /// <summary>Binds one closed native select while preserving incomplete drafts and history.</summary>
    private void OptionSelect<T>(string header, T value, (T Value, string Label)[] choices, Action<T> changed, bool enabled = true) where T : struct, Enum
    {
        var options = choices.Select(choice => new OptionChoice<T>(choice.Value, choice.Label)).ToArray();
        var control = new ComboBox { Header = header, ItemsSource = options, DisplayMemberPath = "Label", SelectedItem = options.First(choice => choice.Value.Equals(value)),
            HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = enabled };
        control.SelectionChanged += (_, _) => { if (control.SelectedItem is OptionChoice<T> selected) changed(selected.Value); };
        ExtendedFields.Children.Add(control);
    }

    /// <summary>Retains invalid integer input as a validation error rather than rounding silently.</summary>
    private void OptionNumber(string header, int value, int minimum, int maximum, Action<int> changed, bool enabled = true)
    {
        var control = new NumberBox { Header = header, Minimum = minimum, Maximum = maximum, Value = value, SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, IsEnabled = enabled };
        control.ValueChanged += (_, args) => changed(double.IsFinite(args.NewValue) && args.NewValue == Math.Truncate(args.NewValue) ? (int)args.NewValue : -1);
        ExtendedFields.Children.Add(control);
    }

    /// <summary>Exposes inherited or independent geometry while retaining custom values across mode changes.</summary>
    private void RenderTerminalOptions(FlowAction action)
    {
        var options = action.Parameters?.Terminal ?? new();
        PropertyHeading("Terminal settings");
        OptionSelect("Configuration", options.Mode, new[] { (FlowConfigurationMode.Inherit, "App settings"), (FlowConfigurationMode.Custom, "Custom") },
            mode => ChangeTerminal(current => current with { Mode = mode }, true));
        var inherited = options.Mode == FlowConfigurationMode.Inherit;
        var preferences = _services.ShortcutPreferences.Saved;
        var shown = inherited ? options with { Width = preferences.TerminalWidth, Height = preferences.TerminalHeight, Gap = preferences.PointerGap,
            Program = preferences.TerminalProgram == "wt" ? FlowTerminalProgram.WindowsTerminal : preferences.TerminalProgram == "powershell" ? FlowTerminalProgram.WindowsPowerShell : FlowTerminalProgram.Auto,
            Position = preferences.TerminalPosition == "center" ? FlowTerminalPosition.Center : FlowTerminalPosition.Above } : options;
        OptionSelect("Program", shown.Program, new[] { (FlowTerminalProgram.Auto, "Auto"), (FlowTerminalProgram.WindowsTerminal, "Windows Terminal"), (FlowTerminalProgram.WindowsPowerShell, "Windows PowerShell") },
            value => ChangeTerminal(current => current with { Program = value }), !inherited);
        OptionSelect("Position", shown.Position, new[] { (FlowTerminalPosition.Above, "Above cursor"), (FlowTerminalPosition.Below, "Below cursor"), (FlowTerminalPosition.Left, "Left of cursor"),
            (FlowTerminalPosition.Right, "Right of cursor"), (FlowTerminalPosition.Center, "Screen center") }, value => ChangeTerminal(current => current with { Position = value }), !inherited);
        OptionNumber("Width (DIP)", shown.Width, 320, 2400, value => ChangeTerminal(current => current with { Width = value }), !inherited);
        OptionNumber("Height (DIP)", shown.Height, 200, 1600, value => ChangeTerminal(current => current with { Height = value }), !inherited);
        OptionNumber("Cursor gap (DIP)", shown.Gap, 0, 100, value => ChangeTerminal(current => current with { Gap = value }), !inherited);
        var ready = new CheckBox { Content = "Wait for the new window", IsChecked = options.WaitReady };
        ready.Click += (_, _) => ChangeTerminal(current => current with { WaitReady = ready.IsChecked == true }, true); ExtendedFields.Children.Add(ready);
        if (options.WaitReady) OptionNumber("Readiness timeout (ms)", options.TimeoutMs, 500, 10000, value => ChangeTerminal(current => current with { TimeoutMs = value }));
    }

    /// <summary>Updates one typed terminal option without discarding input bindings or custom geometry.</summary>
    private void ChangeTerminal(Func<FlowTerminalOptions, FlowTerminalOptions> changed, bool render = false) => ChangeAction(action => action with
    { Parameters = (action.Parameters ?? new()) with { Terminal = changed(action.Parameters?.Terminal ?? new()) } }, render);

    /// <summary>Exposes extraction destination, composed naming and non-overwriting collision policy.</summary>
    private void RenderExtractionOptions(FlowAction action)
    {
        var options = action.Parameters?.Extraction ?? new() { Mode = action.Parameters?.Destination is null ? FlowConfigurationMode.Inherit : FlowConfigurationMode.Custom,
            Destination = action.Parameters?.Destination is null ? FlowArchiveDestination.BesideArchive : FlowArchiveDestination.Custom };
        PropertyHeading("Extraction settings");
        OptionSelect("Configuration", options.Mode, new[] { (FlowConfigurationMode.Inherit, "App settings"), (FlowConfigurationMode.Custom, "Custom") },
            value => ChangeExtraction(current => current with { Mode = value }, true));
        if (options.Mode == FlowConfigurationMode.Custom)
        {
            OptionSelect("Root folder", options.Destination, new[] { (FlowArchiveDestination.BesideArchive, "Beside archive"), (FlowArchiveDestination.Custom, "Fixed / earlier directory"), (FlowArchiveDestination.Inherit, "App destination") },
                value => ChangeAction(current => current with { Parameters = (current.Parameters ?? new()) with { Extraction = (current.Parameters?.Extraction ?? options) with { Destination = value },
                    Destination = value == FlowArchiveDestination.Custom ? current.Parameters?.Destination ?? new FlowInput() : null } }, true));
            if (options.Destination == FlowArchiveDestination.Custom) AddInputEditor(action, "Destination source", action.Parameters?.Destination, false, "Destination");
            OptionSelect("Subfolder name", options.Naming, new[] { (FlowArchiveNaming.ArchiveName, "Archive base name"), (FlowArchiveNaming.Composition, "Text + result fields") },
                value => ChangeExtraction(current => current with { Naming = value, Name = value == FlowArchiveNaming.ArchiveName ? new() : new() { Parts = [new() { Literal = "Extracted" }] } }, true));
            if (options.Naming == FlowArchiveNaming.Composition) AddExpressionEditor(action, "Folder name", options.Name, expression => ChangeExtraction(current => current with { Name = expression }));
            OptionSelect("If the folder exists", options.Collision, new[] { (FlowArchiveCollision.AutoSuffix, "Add a number"), (FlowArchiveCollision.Error, "Stop with an error") }, value => ChangeExtraction(current => current with { Collision = value }));
        }
        else ExtendedFields.Children.Add(new TextBlock { Text = "Uses the app destination, archive base name and automatic numbering.", TextWrapping = TextWrapping.Wrap });
    }

    /// <summary>Updates immutable extraction options while preserving the configured destination source.</summary>
    private void ChangeExtraction(Func<FlowExtractionOptions, FlowExtractionOptions> changed, bool render = false) => ChangeAction(action => action with
    { Parameters = (action.Parameters ?? new()) with { Extraction = changed(action.Parameters?.Extraction ?? new()
        { Mode = action.Parameters?.Destination is null ? FlowConfigurationMode.Inherit : FlowConfigurationMode.Custom,
            Destination = action.Parameters?.Destination is null ? FlowArchiveDestination.BesideArchive : FlowArchiveDestination.Custom }) } }, render);

    /// <summary>Offers optional failure notification; all failed operations stop dependent actions.</summary>
    private void AddFailureOptions(FlowAction action)
    {
        var check = new CheckBox { Content = "Notify on failure", IsChecked = action.Parameters?.Failure?.Notify == true };
        check.Click += (_, _) => ChangeAction(current => current with { Parameters = (current.Parameters ?? new()) with { Failure = new() { Notify = check.IsChecked == true } } });
        var advanced = new Expander { Header = "Advanced", Content = check, HorizontalAlignment = HorizontalAlignment.Stretch };
        ExtendedFields.Children.Add(advanced);
    }

    /// <summary>Pairs a native choice with its serialized enum value.</summary>
    private sealed record OptionChoice<T>(T Value, string Label);
}
