using System.IO.Compression;
using System.Security.Cryptography;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Materializes a verified official compiler in a cache separate from the frozen runtime.</summary>
internal sealed class EmbeddedCompiler
{
    internal const string Version = "1.1.37.02a2";
    private const string ZipHash = "C29B8C3A5124850D79FC9E66E2CA79677C377D7F31631AD3022BA159C5D9E3BE";
    private const string ExecutableHash = "E54A599B19BAA5C1688849BBAE7A9CF049EEFCCD4F704C67941B40DA13A625B2";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Shares materialization and leaves the existing cache intact when preparation fails.</summary>
    internal async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Materialize(cancellationToken), cancellationToken); }
        finally { Gate.Release(); }
    }

    /// <summary>Validates both embedded ZIP and executable before atomically publishing the cache file.</summary>
    private static string Materialize(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoHotkeyUX.Modern", "tools", "compiler", Version);
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The compiler cache must be a regular application directory.");
        var executable = Path.Combine(directory, "Ahk2Exe.exe");
        if (HashMatches(executable, ExecutableHash)) return executable;
        using var resource = typeof(EmbeddedCompiler).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern.Tools.Ahk2Exe.zip")
            ?? throw new InvalidDataException("The bundled compiler resource is missing. Rebuild with prepare-embedded-tools.ps1.");
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())) != ZipHash)
            throw new InvalidDataException("The embedded compiler ZIP failed its SHA-256 check.");
        bytes.Position = 0;
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        if (archive.Entries.Count != 1 || archive.Entries[0].FullName != "Ahk2Exe.exe" || archive.Entries[0].Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The pinned compiler archive contains unexpected entries.");
        var temporary = Path.Combine(directory, $"compiler-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var input = archive.Entries[0].Open()) input.CopyTo(output);
            cancellationToken.ThrowIfCancellationRequested();
            if (!HashMatches(temporary, ExecutableHash)) throw new InvalidDataException("The compiler executable failed its SHA-256 check.");
            File.Move(temporary, executable, overwrite: true);
            ServiceDiagnostics.Write("Compiler", $"Prepared official Ahk2Exe {Version}.");
            return executable;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Rejects missing, linked or altered compiler cache files.</summary>
    private static bool HashMatches(string path, string hash)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)) == hash;
    }
}
