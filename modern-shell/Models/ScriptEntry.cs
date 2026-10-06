namespace AutoHotkeyUX.Modern.Models;

/// <summary>Describes a script on disk without retaining its contents.</summary>
internal sealed record ScriptEntry(string Name, string Path, DateTime LastModifiedUtc, long Size)
{
    public string ModifiedText => LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
