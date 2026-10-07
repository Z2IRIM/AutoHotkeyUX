using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class WindowSpyPage
{
    /// <summary>Seeds an existing external target for the explicit diagnostic without moving the user's pointer.</summary>
    internal void RetainDiagnosticTarget(nint target) => _lastTarget = target;

    /// <summary>Invokes the same pause handler exercised by the page's native button.</summary>
    internal void ToggleDiagnosticPause() => Pause_Click(this, new RoutedEventArgs());

    /// <summary>Checks that actual captured selectors are displayed in the live native page.</summary>
    internal bool HasDiagnosticSnapshot => _snapshot is not null && SelectorsTextBox.Text.Contains("ahk_id 0x", StringComparison.Ordinal);
}
