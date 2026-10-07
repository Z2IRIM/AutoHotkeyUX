namespace AutoHotkeyUX.Modern.Models;

internal sealed record WindowSpyOptions(bool FollowMouse = true, bool IncludeWindowText = false, bool FreezeWithModifiers = true);
internal readonly record struct SpyPoint(int X, int Y)
{
    /// <summary>Formats physical pixel coordinates for the inspection surface.</summary>
    public override string ToString() => $"{X}, {Y}";
}
internal readonly record struct SpyBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Formats the rectangle without interpreting UI text as input.</summary>
    public override string ToString() => $"x {X}   y {Y}   w {Width}   h {Height}";
}
internal sealed record WindowSpySnapshot(nint TargetWindow, string Title, string Class, string ProcessName, uint ProcessId,
    SpyPoint MouseScreen, SpyPoint MouseWindow, SpyPoint MouseClient, string PixelColor,
    string ControlClassNN, string ControlText, SpyBounds? ControlBounds, SpyBounds WindowBounds, SpyBounds ClientBounds,
    string StatusText, string VisibleText, string AllText, string? ReadWarning)
{
    internal string Selectors => $"ahk_class {Class}\nahk_exe {ProcessName}\nahk_pid {ProcessId}\nahk_id 0x{TargetWindow:X}";
}
