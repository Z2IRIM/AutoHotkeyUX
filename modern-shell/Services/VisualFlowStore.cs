using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Saves paired source and workflow files with collision protection and explicit interruption evidence.</summary>
internal sealed class VisualFlowStore
{
    private readonly object _queueLock = new();
    private Task _tail = Task.CompletedTask;
    private bool _closing;

    /// <summary>Serializes accepted app saves on a worker and allows shutdown to drain the same queue.</summary>
    internal Task<VisualFlowOpened> SaveAsync(string directory, string name, VisualFlowDocument flow, VisualFlowOpened? opened)
    {
        lock (_queueLock)
        {
            if (_closing) throw new InvalidOperationException("The application is closing; the workflow was not saved.");
            var task = _tail.ContinueWith(_ => opened is null ? Create(directory, name, flow) : Update(opened, flow), TaskScheduler.Default);
            _tail = task;
            return task;
        }
    }

    /// <summary>Stops accepting new saves and awaits every accepted write before the manager exits.</summary>
    internal async Task DrainAsync()
    {
        Task tail;
        lock (_queueLock) { _closing = true; tail = _tail; }
        try { await tail.ConfigureAwait(false); }
        catch (Exception ex) { ServiceDiagnostics.Write("VisualFlow", "An accepted workflow save failed before shutdown.", ex); }
    }

    /// <summary>Creates an unused pair, suffixing collisions on either name and never replacing a pre-existing file.</summary>
    internal VisualFlowOpened Create(string directory, string name, VisualFlowDocument flow)
    {
        VisualFlowCodec.Validate(flow);
        if (!VisualFlowCodec.IsPath(directory)) throw new ArgumentException("Choose an absolute script directory.");
        var root = Path.GetFullPath(directory);
        var stem = ScriptFileName.Normalize(name);
        Directory.CreateDirectory(root);
        var generated = Prepare(flow with { Revision = 1 });
        for (var index = 0; index < 1000; index++)
        {
            var path = Path.Combine(root, stem + (index == 0 ? "" : "-" + index) + ".ahk");
            using var claim = TryClaim(path);
            if (claim is null) continue;
            if (Path.Exists(path) || Path.Exists(path + ".flow.json") || Path.Exists(path + ".flow.pending")) continue;
            var pending = new VisualFlowPending(path, null, null, generated.Source, generated.Sidecar);
            pending.Stage();
            try
            {
                using var source = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using var sidecar = new FileStream(path + ".flow.json", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                VisualFlowPending.Write(source, generated.Source);
                VisualFlowPending.Write(sidecar, generated.Sidecar);
            }
            catch (Exception ex)
            {
                // Keep both intended bytes and any partially created files. No unlocked deletion risks an external edit.
                ServiceDiagnostics.Write("VisualFlow", "Creation interrupted; retained recovery material at " + pending.DirectoryPath, ex);
                throw new IOException("Creation did not finish. Recovery files were retained at " + pending.DirectoryPath + ". Retry creates a different name.", ex);
            }
            pending.Complete();
            return Result(path, generated);
        }
        throw new IOException("No unused script name is available. Choose another name.");
    }

    /// <summary>Updates the opened identity only while both raw files remain unchanged and exclusively locked.</summary>
    internal VisualFlowOpened Update(VisualFlowOpened opened, VisualFlowDocument flow)
    {
        VisualFlowCodec.Validate(flow);
        if (flow.Id != opened.Document.Id) throw new InvalidDataException("The editing identity changed. Reopen the workflow.");
        var path = ScriptPath(opened.ScriptPath);
        using var claim = TryClaim(path) ?? throw new IOException("This workflow is being saved by another process.");
        CheckRecovery(path);
        CheckOrdinaryFile(path); CheckOrdinaryFile(path + ".flow.json");
        using var source = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var sidecar = new FileStream(path + ".flow.json", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var before = Read(source, VisualFlowCodec.MaximumJsonBytes);
        var companionBefore = Read(sidecar, VisualFlowCodec.MaximumJsonBytes);
        if (VisualFlowGenerator.Hash(before) != opened.SourceHash || VisualFlowGenerator.Hash(companionBefore) != opened.SidecarHash)
            throw new IOException("The source or workflow file changed outside the editor. Your files were preserved; reopen before editing.");
        var current = VisualFlowCodec.Decode(VisualFlowCodec.Utf8.GetString(companionBefore));
        if (current.Id != opened.Document.Id || current.Revision != opened.Document.Revision)
            throw new IOException("The saved workflow revision changed. Reopen before editing.");
        var generated = Prepare(flow with { Revision = checked(current.Revision + 1) });
        var pending = new VisualFlowPending(path, before, companionBefore, generated.Source, generated.Sidecar);
        pending.Stage();
        pending.Apply(source, sidecar);
        return Result(path, generated);
    }

    /// <summary>Reopens only an intact supported pair whose source still matches its document and generator.</summary>
    internal VisualFlowOpened Open(string path)
    {
        path = ScriptPath(path);
        using var claim = TryClaim(path) ?? throw new IOException("This workflow is being saved by another process.");
        CheckRecovery(path);
        CheckOrdinaryFile(path); CheckOrdinaryFile(path + ".flow.json");
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sidecar = new FileStream(path + ".flow.json", FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = Read(source, VisualFlowCodec.MaximumJsonBytes);
        var documentBytes = Read(sidecar, VisualFlowCodec.MaximumJsonBytes);
        var document = VisualFlowCodec.Decode(VisualFlowCodec.Utf8.GetString(documentBytes));
        var hash = VisualFlowGenerator.Hash(bytes);
        if (hash != document.SourceSha256 || !bytes.SequenceEqual(VisualFlowCodec.Utf8.GetBytes(VisualFlowGenerator.Generate(document))))
            throw new InvalidDataException("This source was manually changed or no longer matches its workflow. It was preserved; use the code editor for this file.");
        return new(path, document, hash, VisualFlowGenerator.Hash(documentBytes));
    }

    /// <summary>Recognizes only our ownership marker and companion presence; it does not parse arbitrary AHK.</summary>
    internal static bool IsVisualCandidate(string path)
    {
        if (Path.Exists(path + ".flow.json") || Path.Exists(path + ".flow.pending")) return true;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var prefix = new byte[128]; var length = stream.Read(prefix);
        return System.Text.Encoding.UTF8.GetString(prefix, 0, length).StartsWith(VisualFlowGenerator.OwnershipMarker, StringComparison.Ordinal);
    }

    /// <summary>Serializes interpreter launch with visual saves and refuses interrupted bytes before stopping a live script.</summary>
    internal static FileStream? ClaimForExecution(string path)
    {
        if (!Path.Exists(path + ".flow.json") && !Path.Exists(path + ".flow.pending")) return null;
        var claim = TryClaim(path) ?? throw new IOException("This workflow is being saved. Wait before running or restarting it.");
        try { CheckRecovery(path); return claim; }
        catch { claim.Dispose(); throw; }
    }

    /// <summary>Builds both intended byte arrays before touching any destination file.</summary>
    private static (VisualFlowDocument Document, byte[] Source, byte[] Sidecar) Prepare(VisualFlowDocument flow)
    {
        var source = VisualFlowCodec.Utf8.GetBytes(VisualFlowGenerator.Generate(flow));
        var document = flow with { SourceSha256 = VisualFlowGenerator.Hash(source) };
        return (document, source, VisualFlowCodec.Utf8.GetBytes(VisualFlowCodec.Encode(document)));
    }
    /// <summary>Returns exact opened checksums for the next conflict check.</summary>
    private static VisualFlowOpened Result(string path, (VisualFlowDocument Document, byte[] Source, byte[] Sidecar) value)
        => new(path, value.Document, value.Document.SourceSha256, VisualFlowGenerator.Hash(value.Sidecar));
    /// <summary>Claims one file pair across manager instances; Windows removes the claim when its handle closes.</summary>
    private static FileStream? TryClaim(string path)
    {
        try { return new FileStream(path + ".flow.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { return null; }
    }
    /// <summary>Leaves interrupted saves untouched until a person inspects the retained recovery copies.</summary>
    private static void CheckRecovery(string path)
    { if (Path.Exists(path + ".flow.pending")) throw new IOException("An unfinished save needs inspection. Files were preserved at " + path + ".flow.pending."); }
    /// <summary>Rejects linked targets so recovery never follows them into unrelated files.</summary>
    private static void CheckOrdinaryFile(string path)
    { if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("Workflow targets must be ordinary files: " + path); }
    /// <summary>Normalizes a bounded absolute script path without allowing another extension.</summary>
    private static string ScriptPath(string path)
    { if (!VisualFlowCodec.IsPath(path) || !path.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an absolute .ahk path."); return Path.GetFullPath(path); }
    /// <summary>Reads bounded bytes from an already locked file, observing its current exact contents.</summary>
    private static byte[] Read(FileStream stream, int maximum)
    { if (stream.Length > maximum) throw new InvalidDataException("The workflow file is too large."); stream.Position = 0; var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes; }
}
