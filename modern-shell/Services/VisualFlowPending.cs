namespace AutoHotkeyUX.Modern.Services;

/// <summary>Retains intended bytes and optional rollback copies without claiming two files form an atomic transaction.</summary>
internal sealed class VisualFlowPending
{
    internal string DirectoryPath { get; }
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>Defines only fixed recovery filenames beneath the paired script's own pending directory.</summary>
    internal VisualFlowPending(string path, byte[]? before, byte[]? companionBefore, byte[] next, byte[] companionNext)
    {
        DirectoryPath = path + ".flow.pending";
        _files.Add("source.next", next); _files.Add("workflow.next", companionNext);
        if (before is not null) _files.Add("source.before", before);
        if (companionBefore is not null) _files.Add("workflow.before", companionBefore);
        _files.Add("README.txt", VisualFlowCodec.Utf8.GetBytes("Unfinished visual workflow save. Keep these copies before recovering.\r\n"
            + "source.next/workflow.next are intended new bytes. source.before/workflow.before, when present, are previous bytes.\r\n"
            + "The app does not automatically overwrite current files. Inspect both and restore a matching pair manually.\r\n"));
    }

    /// <summary>Durably stages complete recovery bytes before the first destination write.</summary>
    internal void Stage()
    {
        if (Path.Exists(DirectoryPath)) throw new IOException("Recovery files already exist: " + DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("The recovery directory is linked.");
        foreach (var entry in _files)
        {
            using var stream = new FileStream(Path.Combine(DirectoryPath, entry.Key), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Write(stream, entry.Value);
        }
    }

    /// <summary>Deletes only the unchanged, fixed staging files after a confirmed save or rollback.</summary>
    internal void Complete()
    {
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0
            || Directory.GetFileSystemEntries(DirectoryPath).Length != _files.Count)
            throw new IOException("Recovery material changed; it was retained at " + DirectoryPath);
        foreach (var entry in _files)
        {
            var path = Path.Combine(DirectoryPath, entry.Key);
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                || new FileInfo(path).Length != entry.Value.Length || !File.ReadAllBytes(path).SequenceEqual(entry.Value))
                throw new IOException("Recovery material changed; it was retained at " + DirectoryPath);
        }
        foreach (var name in _files.Keys) File.Delete(Path.Combine(DirectoryPath, name));
        Directory.Delete(DirectoryPath, recursive: false);
    }

    /// <summary>Replaces bytes through an exclusively held stream and flushes the actual storage boundary.</summary>
    internal static void Write(FileStream stream, byte[] bytes)
    { stream.Position = 0; stream.Write(bytes); stream.SetLength(bytes.Length); stream.Flush(flushToDisk: true); }

    /// <summary>Applies a staged update through both held streams, restoring previous bytes if either write fails.</summary>
    internal void Apply(FileStream source, FileStream sidecar)
    {
        try { Write(source, _files["source.next"]); Write(sidecar, _files["workflow.next"]); }
        catch (Exception saveError)
        {
            try { Write(source, _files["source.before"]); Write(sidecar, _files["workflow.before"]); Complete(); }
            catch (Exception rollbackError)
            {
                ServiceDiagnostics.Write("VisualFlow", "Rollback failed; recovery copies remain at " + DirectoryPath, rollbackError);
                throw new IOException("Saving and rollback failed. Keep the recovery copies at " + DirectoryPath + ".", new AggregateException(saveError, rollbackError));
            }
            throw new IOException("Saving failed; the previous source and workflow were restored.", saveError);
        }
        Complete();
    }
}
