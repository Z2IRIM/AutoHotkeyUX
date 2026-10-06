using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Extracts into a new sibling directory, validating every destination and preserving the source archive.</summary>
internal sealed class ArchiveExtractionService
{
    private const long MaximumBytes = 20L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 100000;
    private static readonly string[] Extensions = [".tar.gz", ".tgz", ".zip", ".7z", ".rar", ".tar"];

    /// <summary>Shares the supported archive suffixes with the lightweight shortcut request boundary.</summary>
    internal static bool SupportsPath(string path) => Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stages a complete extraction before committing a unique final folder name.</summary>
    internal string Extract(string archivePath)
    {
        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The archive no longer exists.", source);
        var extension = Extensions.FirstOrDefault(extension => source.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException("Supported archives: ZIP, 7z, RAR, TAR, TAR.GZ and TGZ.");
        var parent = Path.GetDirectoryName(source)!;
        var name = Path.GetFileName(source)[..^extension.Length];
        if (string.IsNullOrWhiteSpace(name)) name = "Extracted archive";
        var staging = Path.Combine(parent, ".autohotkeyux-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var committed = false;
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (extension is ".tar.gz" or ".tgz")
            {
                using var reader = ReaderFactory.OpenReader(input);
                ExtractEntries(reader, staging);
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(input);
                if (archive.IsComplete == false) throw new InvalidDataException("The archive is incomplete or needs other volumes.");
                if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                {
                    using var reader = archive.ExtractAllEntries();
                    ExtractEntries(reader, staging);
                }
                else
                {
                    var budget = new ExtractionBudget();
                    foreach (var entry in archive.Entries)
                        ExtractEntry(entry, destination =>
                        {
                            using var entryStream = entry.OpenEntryStream();
                            entryStream.CopyTo(destination, 131072);
                        }, archive.Type, staging, budget);
                }
            }
            for (var index = 0; index < 10000; index++)
            {
                var destination = Path.Combine(parent, index == 0 ? name : $"{name} ({index})");
                if (Directory.Exists(destination) || File.Exists(destination)) continue;
                try
                {
                    CommitDirectory(staging, destination);
                    committed = true;
                    ServiceDiagnostics.Write("Extraction", $"{source} -> {destination}");
                    return destination;
                }
                catch (IOException) when (Directory.Exists(destination) || File.Exists(destination))
                { /* Another extraction claimed this name; retry a new name without overwriting it. */ }
            }
            throw new IOException("No free destination folder name could be found.");
        }
        finally
        {
            if (!committed && Directory.Exists(staging))
            {
                // Only this operation's generated staging directory is eligible for recursive cleanup.
                if (!Path.GetDirectoryName(staging)!.Equals(parent, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(staging).StartsWith(".autohotkeyux-extract-", StringComparison.Ordinal))
                    throw new IOException("Extraction cleanup target failed validation.");
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { ServiceDiagnostics.Write("Extraction", $"Could not remove partial extraction: {staging}", ex); }
            }
        }
    }

    /// <summary>Retries bounded Windows sharing/access races at commit without repeating decompression or overwriting a destination.</summary>
    private static void CommitDirectory(string source, string destination)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException ex) when (attempt < 4 && (ex.HResult & 0xFFFF) is 5 or 32 or 33
                && !Directory.Exists(destination) && !File.Exists(destination))
            {
                ServiceDiagnostics.Write("Extraction", $"Directory commit encountered a Windows sharing/access conflict; retry {attempt + 1}.", ex);
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }

    /// <summary>Streams entries sequentially for solid 7z/RAR, with size, link and filename checks.</summary>
    private static void ExtractEntries(IReader reader, string root)
    {
        var budget = new ExtractionBudget();
        while (reader.MoveToNextEntry())
            ExtractEntry(reader.Entry, reader.WriteEntryTo, reader.Type, root, budget);
    }

    /// <summary>Applies the same path, link, output budget and checksum rules to streaming and random-access entries.</summary>
    private static void ExtractEntry(IEntry entry, Action<Stream> write, ArchiveType type, string root, ExtractionBudget budget)
    {
            if (++budget.Entries > MaximumEntries) throw new InvalidDataException("Archive exceeds 100,000 entries.");
            if (entry.IsDirectory && (entry.Key ?? "").Replace('/', '\\').TrimEnd('\\') == ".") return;
            if (entry.IsEncrypted) throw new NotSupportedException("Password-protected archives are not supported by quick extraction.");
            if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new InvalidDataException("Archive links are not allowed.");
            // ZIP Unix symlinks are encoded in external attributes rather than LinkTarget.
            if (entry is SharpCompress.Common.Zip.ZipEntry zip && ((zip.Attrib >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive symbolic links are not allowed.");
            var destination = ResolveDestination(root, entry.Key ?? string.Empty);
            if (entry.IsDirectory) { Directory.CreateDirectory(destination); return; }
            if (!budget.Paths.Add(destination)) throw new InvalidDataException("Archive contains colliding file names.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            // The bounded stream checks actual decompressed bytes rather than trusting archive metadata.
            using var limited = new BoundedExtractionStream(output, amount =>
            {
                budget.TotalBytes = checked(budget.TotalBytes + amount);
                if (budget.TotalBytes > MaximumBytes) throw new InvalidDataException("Archive exceeds the 20 GiB extraction limit.");
            });
            using var checksum = new SharpCompress.Crypto.Crc32Stream(limited);
            write(checksum);
            if (type == ArchiveType.Zip && checksum.Crc != unchecked((uint)entry.Crc))
                throw new InvalidDataException($"Archive checksum failed: {entry.Key}");
    }

    /// <summary>Shares a decompression budget across all entries of one extraction operation.</summary>
    private sealed class ExtractionBudget
    {
        internal long TotalBytes;
        internal int Entries;
        internal HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Rejects path traversal, alternate streams, ambiguous Windows names and special file targets.</summary>
    private static string ResolveDestination(string root, string key)
    {
        var normalized = key.Replace('/', '\\').TrimEnd('\\');
        while (normalized.StartsWith(".\\", StringComparison.Ordinal)) normalized = normalized[2..];
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
            throw new InvalidDataException("Archive contains an invalid absolute or empty path.");
        var parts = normalized.Split('\\');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Archive contains an unsafe path: {key}");
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
                || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9¹²³]$"))
                throw new InvalidDataException($"Archive contains a reserved Windows name: {key}");
        }
        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive entry leaves the extraction directory.");
        return destination;
    }
}

/// <summary>Counts actual output bytes while retaining the caller-owned file stream.</summary>
internal sealed class BoundedExtractionStream(Stream destination, Action<int> count) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => destination.Length;
    public override long Position { get => destination.Position; set => throw new NotSupportedException(); }
    /// <summary>Checks the decompression budget before committing a buffer to disk.</summary>
    public override void Write(byte[] buffer, int offset, int length) { count(length); destination.Write(buffer, offset, length); }
    /// <summary>Checks span writes using the same aggregate extraction budget.</summary>
    public override void Write(ReadOnlySpan<byte> buffer) { count(buffer.Length); destination.Write(buffer); }
    /// <summary>Flushes the caller-owned destination without closing it.</summary>
    public override void Flush() => destination.Flush();
    /// <summary>Rejects reads from a write-only extraction budget stream.</summary>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <summary>Rejects seeking, which could invalidate aggregate decompressed-byte accounting.</summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <summary>Rejects arbitrary file-length changes outside streamed extraction.</summary>
    public override void SetLength(long value) => throw new NotSupportedException();
}
