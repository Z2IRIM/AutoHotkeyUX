using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class HomePage
{
    /// <summary>Exercises the existing Home click handlers without synthesizing external desktop input.</summary>
    internal void OpenDiagnosticTool(string tag)
    {
        var args = new RoutedEventArgs();
        if (tag == "spy") WindowSpyButton_Click(this, args);
        else if (tag == "compile") CompileButton_Click(this, args);
        else if (tag == "docs") DocumentationButton_Click(this, args);
        else throw new ArgumentException("Unknown tool diagnostic route.", nameof(tag));
    }
}
