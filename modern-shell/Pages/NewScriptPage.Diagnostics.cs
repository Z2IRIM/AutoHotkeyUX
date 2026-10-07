using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Measures real template card bounds and selection previews without creating any user files.</summary>
    internal object VerifyTemplateLayout(bool expectStacked)
    {
        var previous = _selectedTemplate;
        var checks = new List<object>();
        try
        {
            foreach (var template in new[] { "Blank", "Hotkeys", "Automation" })
            {
                SelectTemplate(template);
                UpdateLayout();
                var buttons = new[] { BlankTemplateButton, HotkeysTemplateButton, AutomationTemplateButton };
                var bounds = buttons.Select(button =>
                {
                    var point = button.TransformToVisual(TemplateGrid).TransformPoint(new Point(0, 0));
                    return new { X = point.X, Y = point.Y, Width = button.ActualWidth, Height = button.ActualHeight };
                }).ToArray();
                var widths = bounds.Select(bound => bound.Width).ToArray();
                var gaps = Enumerable.Range(0, 2).Select(index => expectStacked
                    ? bounds[index + 1].Y - bounds[index].Y - bounds[index].Height
                    : bounds[index + 1].X - bounds[index].X - bounds[index].Width).ToArray();
                if (widths.Min() <= 0 || widths.Max() - widths.Min() > 1.5
                    || gaps.Any(gap => Math.Abs(gap - 16) > 1.5)
                    || bounds.Any(bound => bound.X < -1 || bound.X + bound.Width > TemplateGrid.ActualWidth + 1)
                    || buttons.Count(button => button.BorderThickness.Left == 2) != 1
                    || NormalizePreview(ScriptPreviewTextBox.Text) != NormalizePreview(GetTemplateContent(template)))
                    throw new InvalidOperationException($"Template '{template}' has uneven card geometry or an incorrect preview: "
                        + System.Text.Json.JsonSerializer.Serialize(new { expectStacked, PageWidth = ActualWidth, GridWidth = TemplateGrid.ActualWidth,
                            Bounds = bounds, Gaps = gaps, Rows = buttons.Select(Grid.GetRow).ToArray(), Columns = buttons.Select(Grid.GetColumn).ToArray(),
                            Spans = buttons.Select(Grid.GetColumnSpan).ToArray(), SelectedCount = buttons.Count(button => button.BorderThickness.Left == 2),
                            PreviewMatches = NormalizePreview(ScriptPreviewTextBox.Text) == NormalizePreview(GetTemplateContent(template)) }));
                checks.Add(new { Template = template, Bounds = bounds, Gaps = gaps });
            }
        }
        finally { SelectTemplate(previous); }
        return new { Stacked = expectStacked, GridWidth = TemplateGrid.ActualWidth, Checks = checks };
    }

    /// <summary>Ignores TextBox's native CR/LF normalization when comparing the same starter content.</summary>
    private static string NormalizePreview(string content) => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
