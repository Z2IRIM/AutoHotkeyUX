using AutoHotkeyUX.Modern.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Pins exact visual source bytes so a save cannot change the hotkey registered by a newly starting interpreter.</summary>
internal sealed class VisualExecutionSource
{
    private readonly FileStream _stream;
    private readonly string _root;
    internal string Path { get; }
    internal string Hash { get; }
    internal FlowTrigger Trigger { get; }
    internal bool SupportsReady { get; }
    internal string ReadyPath => Path + ".ready";
    /// <summary>Retains the read-shared handle until the interpreter exits or the manager detaches.</summary>
    private VisualExecutionSource(string path, string hash, FlowTrigger trigger, FileStream stream, bool supportsReady)
    { Path = path; Hash = hash; Trigger = trigger; _stream = stream; SupportsReady = supportsReady; _root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)!)!; }

    /// <summary>Creates a private immutable copy only for an intact visual pair; ordinary/manual code keeps its original path.</summary>
    internal static VisualExecutionSource? Capture(string sourcePath, string root)
    {
        if (!File.Exists(sourcePath + ".flow.json")) return null;
        VisualFlowOpened opened;
        try { opened = new VisualFlowStore().Open(sourcePath, claimHeld: true); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        { ServiceDiagnostics.Write("VisualFlow", "Manual/unsupported source runs as ordinary code: " + sourcePath, ex); return null; }
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = Read(source);
        if (VisualFlowGenerator.Hash(bytes) != opened.SourceHash) throw new IOException("The visual source changed before launch; retry after saving.");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Execution snapshot directory must not be a link.");
        var folder = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(folder)) throw new IOException("Execution snapshot identity collision.");
        Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(sourcePath));
        FileStream? stream = null;
        try
        {
            stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            stream.Write(bytes); stream.Flush(true);
            return new(path, opened.SourceHash, opened.Document.Trigger, stream, opened.Document.SchemaVersion == 3);
        }
        catch { if (stream is not null) { stream.Dispose(); File.Delete(path); } if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); throw; }
    }
    /// <summary>Re-pins only a verified ordinary file within this session store's private snapshot directory.</summary>
    internal static VisualExecutionSource Restore(string path, string hash, FlowTrigger trigger, string root)
    {
        if (!Owns(path, root) || !OrdinaryDirectories(path, root) || !Regex.IsMatch(hash, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant)
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Invalid visual execution snapshot identity.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var bytes = Read(stream);
            if (VisualFlowGenerator.Hash(bytes) != hash) throw new InvalidDataException("The visual execution snapshot changed.");
            return new(path, hash, trigger, stream, VisualFlowCodec.Utf8.GetString(bytes).StartsWith("; AutoHotkey UX visual workflow v3\r\n", StringComparison.Ordinal));
        }
        catch { stream.Dispose(); throw; }
    }
    /// <summary>Validates a recorded trigger without trusting the workflow's current on-disk version.</summary>
    internal static bool IsValidTrigger(FlowTrigger? trigger)
    {
        if (trigger is null) return true;
        try { VisualFlowCodec.Validate(new() { SchemaVersion = 2, Trigger = trigger, Actions = [new() { Kind = FlowActionKind.Wait }] }); return true; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { return false; }
    }
    /// <summary>Closes ownership and deletes only the exact test/app-created file on interpreter exit.</summary>
    internal void Release(bool delete)
    {
        _stream.Dispose();
        if (!delete) return;
        try
        {
            if (!Owns(Path, _root) || !OrdinaryDirectories(Path, _root)) throw new IOException("Snapshot cleanup refused a changed directory identity.");
            File.Delete(ReadyPath); File.Delete(Path); Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ServiceDiagnostics.Write("VisualFlow", "Could not remove completed snapshot " + Path, ex); }
    }
    /// <summary>Rejects linked snapshot directories during recovery and cleanup without traversing outside the owner.</summary>
    private static bool OrdinaryDirectories(string path, string root) =>
        (File.GetAttributes(root) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory
        && (File.GetAttributes(System.IO.Path.GetDirectoryName(path)!) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
    /// <summary>Constrain recovered paths to a single GUID-named file in the intended directory.</summary>
    internal static bool Owns(string path, string root)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)) return false;
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        return System.IO.Path.GetDirectoryName(directory)?.Equals(System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) == true
            && Regex.IsMatch(System.IO.Path.GetFileName(directory), "^[0-9a-f]{32}$", RegexOptions.CultureInvariant)
            && System.IO.Path.GetExtension(path).Equals(".ahk", StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Reads only bounded generator output from an already pinned file handle.</summary>
    private static byte[] Read(FileStream stream)
    { if (stream.Length > VisualFlowCodec.MaximumJsonBytes) throw new InvalidDataException("Execution source is too large."); stream.Position = 0; var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes; }
}
