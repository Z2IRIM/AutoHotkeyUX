using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class ShortcutSettingsPage
{
    private int _pointerIndex;
    private static readonly (double X, double Y)[] PointerPositions = [(0.75, 0.82), (0.18, 0.73), (0.55, 0.2)];

    /// <summary>Shows how draft dimensions and gap place a new terminal within the illustrated work area.</summary>
    private void RenderPlacement()
    {
        if (!_ready || PlacementCanvas.ActualWidth <= 0 || PlacementCanvas.ActualHeight <= 0) return;
        var width = PlacementCanvas.ActualWidth; var height = PlacementCanvas.ActualHeight;
        var point = PointerPositions[_pointerIndex];
        var pointerX = point.X * (width - 18); var pointerY = point.Y * (height - 18);
        var scale = width / 1600;
        var terminalWidth = Math.Clamp(Math.Max(0, _draft.TerminalWidth) * scale, 100, Math.Max(100, width - 8));
        var terminalHeight = Math.Clamp(Math.Max(0, _draft.TerminalHeight) * scale, 62, Math.Max(62, height - 8));
        var x = _draft.TerminalPosition == "center" ? (width - terminalWidth) / 2 : pointerX - terminalWidth / 2;
        var y = _draft.TerminalPosition == "center" ? (height - terminalHeight) / 2 : pointerY - terminalHeight - Math.Max(0, _draft.PointerGap) * scale;
        MiniTerminal.Width = terminalWidth; MiniTerminal.Height = terminalHeight;
        Canvas.SetLeft(MiniTerminal, Math.Clamp(x, 4, Math.Max(4, width - terminalWidth - 4)));
        Canvas.SetTop(MiniTerminal, Math.Clamp(y, 4, Math.Max(4, height - terminalHeight - 4)));
        Canvas.SetLeft(MiniPointer, pointerX); Canvas.SetTop(MiniPointer, pointerY);
        PlacementCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        PlacementSizeText.Text = $"{_draft.TerminalWidth} × {_draft.TerminalHeight}";
        MiniTerminalTitle.Text = _draft.TerminalProgram == "powershell" ? "Windows PowerShell" : "Windows Terminal";
    }

    /// <summary>Changes only the simulated click point; no real windows or pointer are moved.</summary>
    private void Placement_Click(object sender, RoutedEventArgs e)
    { _pointerIndex = (_pointerIndex + 1) % PointerPositions.Length; RenderPlacement(); }
    /// <summary>Redraws the illustration after layout has its actual size.</summary>
    private void PlacementScene_SizeChanged(object sender, SizeChangedEventArgs e) => RenderPlacement();

    /// <summary>Stacks the placement illustration when two columns would compress the form.</summary>
    private void TerminalLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 720;
        Grid.SetColumnSpan(TerminalFields, stacked ? 2 : 1);
        Grid.SetColumn(PlacementPanel, stacked ? 0 : 1); Grid.SetRow(PlacementPanel, stacked ? 1 : 0);
        Grid.SetColumnSpan(PlacementPanel, stacked ? 2 : 1);
        PlacementPanel.MaxWidth = stacked ? 480 : double.PositiveInfinity;
        PlacementPanel.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
    }

    /// <summary>Keeps complete numeric values readable in narrow windows.</summary>
    private void DimensionsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 350;
        var controls = new[] { WidthBox, HeightBox, GapBox };
        for (var index = 0; index < controls.Length; index++)
        { Grid.SetColumn(controls[index], stacked ? 0 : index); Grid.SetRow(controls[index], stacked ? index : 0); Grid.SetColumnSpan(controls[index], stacked ? 3 : 1); }
    }

    /// <summary>Stacks archive selectors at the same practical field width as the terminal form.</summary>
    private void ArchiveFieldsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 600;
        Grid.SetColumnSpan(ArchiveKeyCombo, stacked ? 2 : 1);
        Grid.SetColumn(DestinationCombo, stacked ? 0 : 1); Grid.SetRow(DestinationCombo, stacked ? 1 : 0);
        Grid.SetColumnSpan(DestinationCombo, stacked ? 2 : 1);
    }

    /// <summary>Moves save actions below status when the available width cannot fit both.</summary>
    private void SaveBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 620;
        Grid.SetColumn(SaveButtons, stacked ? 0 : 1); Grid.SetRow(SaveButtons, stacked ? 1 : 0);
        Grid.SetColumnSpan(SaveButtons, stacked ? 2 : 1);
    }
}
