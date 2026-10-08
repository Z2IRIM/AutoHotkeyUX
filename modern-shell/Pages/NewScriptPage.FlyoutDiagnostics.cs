using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Checks floating-menu geometry and compact rows using an unsaved, nonexecuted isolated draft.</summary>
    internal async Task<object> VerifyFlyoutLayoutAsync(Func<FrameworkElement, Task>? captureMenu = null)
    {
        _opened = null; _insertion = default; _branchSelection = null;
        _session.Load(_session.Document with { Actions = Enumerable.Range(0, 12)
            .Select(index => new FlowAction { Kind = FlowActionKind.Wait, DelayMs = index + 1 }).ToArray() });
        RenderDocument(); await Task.Delay(100); UpdateLayout();
        if (_libraryItems.Count != 16 || _libraryCategories.Count != 4)
            throw new InvalidOperationException("The floating library is incomplete.");
        var nodes = new FrameworkElement[] { EditorGrid, LibraryPanel, FlowPanel, PropertiesPanel, ActionLibrary }
            .Concat(_libraryCategories).ToArray();
        var before = nodes.Select(node => LayoutBounds(node, EditorGrid)).ToArray();
        for (var index = 0; index < _libraryCategories.Count; index++)
        {
            var category = _libraryCategories[index]; var menu = (MenuFlyout)category.Flyout;
            menu.ShowAt(category); await Task.Delay(100); UpdateLayout();
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .SingleOrDefault(item => item.IsOpen && item.Child is MenuFlyoutPresenter);
            if (popup?.Child is not FrameworkElement presenter || presenter.ActualWidth < 240 || menu.Items.Count != 4)
                throw new InvalidOperationException("A native action menu did not render.");
            var after = nodes.Select(node => LayoutBounds(node, EditorGrid)).ToArray();
            if (before.Zip(after).Any(pair => Math.Abs(pair.First.X - pair.Second.X) > 1
                || Math.Abs(pair.First.Y - pair.Second.Y) > 1 || Math.Abs(pair.First.Width - pair.Second.Width) > 1
                || Math.Abs(pair.First.Height - pair.Second.Height) > 1))
                throw new InvalidOperationException("Opening an action menu changed editor geometry.");
            if (index == 1 && captureMenu is not null) await captureMenu(presenter);
            if (menu.Items.OfType<MenuFlyoutItem>().SingleOrDefault(item => (FlowActionKind)item.Tag == FlowActionKind.Wait) is { } wait)
            {
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(wait)).Invoke(); await Task.Delay(100);
                if (_session.Document.Actions.Length != 13 || _session.Document.Actions[^1].Kind != FlowActionKind.Wait
                    || VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(item => item.IsOpen && item.Child is MenuFlyoutPresenter))
                    throw new InvalidOperationException("Native menu invocation did not add exactly one action and dismiss.");
            }
            else { menu.Hide(); await Task.Delay(100); }
        }
        ActionList.ScrollIntoView(_cards[0]); await Task.Delay(100); UpdateLayout();
        var rows = Enumerable.Range(0, _cards.Count).Select(index => ActionList.ContainerFromIndex(index))
            .OfType<ListViewItem>().Where(item => item.ActualHeight > 0).ToArray();
        var visible = rows.Count(item => { var bounds = LayoutBounds(item, ActionList);
            return bounds.Y >= -1 && bounds.Bottom <= ActionList.ActualHeight + 1; });
        if (ActionList.ActualHeight < 439 || ActionList.ActualHeight > 801 || rows.Length == 0 || visible < 6)
            throw new InvalidOperationException("The taller workflow list has insufficient visible rows.");
        if (rows.Max(item => item.ActualHeight) >= 76)
            throw new InvalidOperationException("Workflow rows retained their previous height.");
        return new { Passed = true, Categories = 4, Actions = 16, StableGeometry = true, NativeMenuInvocation = true,
            ListHeight = ActionList.ActualHeight, VisibleRows = visible, RowHeight = rows[0].ActualHeight,
            Layout = VerifyWorkflowLayout(), ExecutionCount = _services.Execution.Snapshot().Count };
    }

    /// <summary>Measures a native element relative to one layout owner in device-independent units.</summary>
    private static Rect LayoutBounds(FrameworkElement element, UIElement owner) =>
        new(element.TransformToVisual(owner).TransformPoint(default), new Size(element.ActualWidth, element.ActualHeight));
}
