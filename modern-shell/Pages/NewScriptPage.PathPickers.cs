using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Offers the established native picker for path-bearing literal inputs only.</summary>
    private void AddPathBrowse(FlowAction action, string slot)
    {
        var directory = slot is "Destination" or "WorkingDirectory" || action.Kind is FlowActionKind.OpenFolder or FlowActionKind.OpenTerminal or FlowActionKind.JoinPath or FlowActionKind.CreateDirectory;
        var file = slot == "Input" && action.Kind is FlowActionKind.ExtractArchive or FlowActionKind.OpenProgram or FlowActionKind.GetPathProperties;
        if (!directory && !file) return;
        var button = new Button { Content = directory ? "Choose folder…" : "Choose file…" };
        button.Click += async (_, _) =>
        {
            var selected = action.Id;
            try
            {
                var window = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
                string? path;
                if (directory)
                {
                    var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
                    path = (await picker.PickSingleFolderAsync())?.Path;
                }
                else
                {
                    var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
                    path = (await picker.PickSingleFileAsync())?.Path;
                }
                if (path is not null && !_busy && _session.Selection == selected) SetInput(slot, new() { Literal = path }, true);
            }
            catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
        };
        ExtendedFields.Children.Add(button);
    }
}
