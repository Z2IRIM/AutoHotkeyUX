using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Writes bounded, atomic request results only under the manager-owned directory.</summary>
internal static class VisualFlowReports
{
    /// <summary>Derives an exact ordinary result path from the server root and request identity.</summary>
    internal static string PathFor(string root, Guid id)
    {
        if (id == Guid.Empty || !VisualFlowCodec.IsPath(root)) throw new InvalidDataException("Invalid workflow result owner.");
        return Path.Combine(Path.GetFullPath(root), id.ToString("N") + ".result");
    }

    /// <summary>Claims an extraction identity before decompression, preventing repeated warm or cold side effects.</summary>
    internal static bool Begin(string root, VisualExtractionRequest request)
    {
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Workflow result directory cannot be a link.");
        var claim = PathFor(root, request.RequestId) + ".claim";
        var staging = claim + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(Encoding.ASCII.GetBytes(fingerprint)); stream.Flush(true); }
            File.Move(staging, claim, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(claim))
        {
            if ((File.GetAttributes(claim) & FileAttributes.ReparsePoint) != 0 || new FileInfo(claim).Length != 64
                || File.ReadAllText(claim) != fingerprint) throw new InvalidDataException("Request identity already belongs to different extraction options.");
            return false;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    /// <summary>Commits one result atomically; callers cannot choose or overwrite another file.</summary>
    internal static void Complete(string root, Guid id, string? output, Exception? error)
    {
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Workflow result directory cannot be a link.");
        var path = PathFor(root, id);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var body = error is null ? "ok\n" + output : "error\n" + error.Message[..Math.Min(error.Message.Length, 2048)];
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(body);
                if (bytes.Length > 8192) throw new InvalidDataException("Workflow result is too large.");
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(staging, path, overwrite: false);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    /// <summary>Removes only an exact owned receipt, never traversing directories.</summary>
    internal static void Forget(string root, Guid id)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        var path = PathFor(root, id);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
    }

    /// <summary>Keeps stale cold-process receipts bounded during the next explicit request, without an idle timer.</summary>
    internal static void Prune(string root)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.result").Where(file => Regex.IsMatch(file.Name, "^[a-f0-9]{32}\\.result$"))
            .Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddMinutes(-15)))
            if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                var claim = file.FullName + ".claim";
                file.Delete();
                if (File.Exists(claim) && (File.GetAttributes(claim) & FileAttributes.ReparsePoint) == 0) File.Delete(claim);
            }
    }
}
