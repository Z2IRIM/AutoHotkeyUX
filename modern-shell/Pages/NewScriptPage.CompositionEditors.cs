using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Restricts composed inputs to human-readable path/text fields.</summary>
    private static bool TextField(FlowResultField field) => field is FlowResultField.Path or FlowResultField.Directory or FlowResultField.ParentDirectory
        or FlowResultField.Name or FlowResultField.BaseName or FlowResultField.Extension or FlowResultField.Text or FlowResultField.TargetKind;

    /// <summary>Edits bounded text/result parts while keeping every change in shared document history.</summary>
    private void AddExpressionEditor(FlowAction action, string header, FlowTextExpression expression, Action<FlowTextExpression> changed)
    {
        PropertyHeading(header);
        var current = expression;
        for (var index = 0; index < expression.Parts.Length; index++)
        {
            var position = index; var part = expression.Parts[index];
            var choices = new List<FlowSourceChoice> { new("Fixed text", part.Kind == FlowInputKind.Literal ? part : new FlowInput()) };
            foreach (var producer in VisualFlowTree.Available(_session.Document.Actions, action.Id))
                foreach (var field in VisualFlowTree.Outputs(producer, _session.Document.SchemaVersion).Where(TextField))
                    choices.Add(new(FlowActionCard.StepLabel(_session.Document, producer.Id) + " → " + VisualFlowSchema.FieldLabel(field), FlowInput.Reference(producer.Id, field)));
            var selected = choices.FirstOrDefault(choice => choice.Input == part);
            if (selected is null) { selected = new("Missing result · choose again", part); choices.Add(selected); }
            var source = new ComboBox { Header = "Part " + (index + 1), ItemsSource = choices, DisplayMemberPath = "Label", SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            source.SelectionChanged += (_, _) =>
            {
                if (source.SelectedItem is not FlowSourceChoice { Input: { } input }) return;
                var parts = current.Parts.ToArray(); parts[position] = input; current = current with { Parts = parts }; changed(current); RenderDocument();
            };
            ExtendedFields.Children.Add(source);
            if (part.Kind == FlowInputKind.Literal)
            {
                var text = new TextBox { Header = "Text", Text = part.Literal, MaxLength = 2048, TextWrapping = TextWrapping.Wrap };
                text.TextChanged += (_, _) => { var parts = current.Parts.ToArray(); parts[position] = new() { Literal = text.Text }; current = current with { Parts = parts }; changed(current); };
                ExtendedFields.Children.Add(text);
            }
            var remove = new Button { Content = "Remove part", IsEnabled = expression.Parts.Length > 1 };
            remove.Click += (_, _) => { current = current with { Parts = current.Parts.Where((_, partIndex) => partIndex != position).ToArray() }; changed(current); RenderDocument(); };
            ExtendedFields.Children.Add(remove);
        }
        var add = new Button { Content = "Add part", IsEnabled = expression.Parts.Length < 8 };
        add.Click += (_, _) => { current = current with { Parts = [.. current.Parts, new() { Literal = "" }] }; changed(current); RenderDocument(); };
        ExtendedFields.Children.Add(add);
    }

    /// <summary>Shares typed controls for path preparation, deliberate stops and result notifications.</summary>
    private void RenderGenericOptions(FlowAction action)
    {
        var p = action.Parameters;
        if (action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory)
            OptionSelect("Unavailable target", p?.Context?.Missing ?? FlowMissingTarget.StopSilently,
                new[] { (FlowMissingTarget.StopSilently, "Stop silently"), (FlowMissingTarget.Error, "Fail with an error") },
                value => ChangeAction(current => current with { Parameters = (current.Parameters ?? new()) with { Context = new() { Missing = value } } }));
        if (action.Kind == FlowActionKind.JoinPath)
            AddExpressionEditor(action, "Relative path segments", new() { Parts = p?.JoinPath?.Segments ?? [new() { Literal = "Output" }] },
                expression => ChangeAction(current => current with { Parameters = current.Parameters! with { JoinPath = new() { Segments = expression.Parts } } }));
        if (action.Kind == FlowActionKind.CreateDirectory)
        {
            var exists = new CheckBox { Content = "Fail if directory exists", IsChecked = p?.Directory?.FailIfExists == true };
            exists.Click += (_, _) => ChangeAction(current => current with { Parameters = current.Parameters! with { Directory = new() { FailIfExists = exists.IsChecked == true } } });
            ExtendedFields.Children.Add(exists);
        }
        if (action.Kind == FlowActionKind.StopWorkflow)
        {
            var reason = new TextBox { Header = "Log reason", Text = p?.Stop?.Reason ?? "No supported target.", MaxLength = 2048 };
            reason.TextChanged += (_, _) => ChangeAction(current => current with { Parameters = current.Parameters! with { Stop = (current.Parameters.Stop ?? new()) with { Reason = reason.Text } } });
            ExtendedFields.Children.Add(reason);
            var notify = new CheckBox { Content = "Show notification", IsChecked = p?.Stop?.Notify == true };
            notify.Click += (_, _) => ChangeAction(current => current with { Parameters = current.Parameters! with { Stop = (current.Parameters.Stop ?? new()) with { Notify = notify.IsChecked == true } } });
            ExtendedFields.Children.Add(notify);
        }
        if (action.Kind == FlowActionKind.Notify)
        {
            var notice = p?.Notification ?? new();
            AddExpressionEditor(action, "Notification title", notice.Title, expression => ChangeAction(current => current with
                { Parameters = current.Parameters! with { Notification = (current.Parameters.Notification ?? new()) with { Title = expression } } }));
            AddExpressionEditor(action, "Message", notice.Message, expression => ChangeAction(current => current with
                { Parameters = current.Parameters! with { Notification = (current.Parameters.Notification ?? new()) with { Message = expression } } }));
        }
    }
}
