using AutoHotkeyUX.Modern.Models;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Atomically stores process identities; damaged state never blocks application startup.</summary>
internal sealed class ScriptSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _path;
    internal string? LastError { get; private set; }

    /// <summary>Allows a private test store while using the agreed per-user path by default.</summary>
    internal ScriptSessionStore(string? path = null)
        => _path = path ?? Path.Combine(ServiceDiagnostics.StateDirectory, "managed-sessions.json");

    /// <summary>Reads bounded metadata, returning an empty set on corruption or unavailable storage.</summary>
    internal IReadOnlyList<PersistedScriptSession> Load()
    {
        LastError = null;
        try
        {
            if (!File.Exists(_path)) return [];
            if (new FileInfo(_path).Length > 1024 * 1024)
                throw new InvalidDataException("Managed session metadata exceeds 1 MiB.");
            var entries = JsonSerializer.Deserialize<List<PersistedScriptSession>>(File.ReadAllText(_path), JsonOptions) ?? [];
            return entries.Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.ScriptPath)
                && entry.Pid > 0 && entry.ProcessStartUtc.Kind == DateTimeKind.Utc).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"Session recovery: {ex.Message}";
            ServiceDiagnostics.Write("SessionStore", LastError, ex);
            return [];
        }
    }

    /// <summary>Replaces the metadata only after the new UTF-8 file has been completely flushed.</summary>
    internal void Save(IEnumerable<PersistedScriptSession> sessions)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, sessions, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Session persistence: {ex.Message}";
            ServiceDiagnostics.Write("SessionStore", LastError, ex);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { ServiceDiagnostics.Write("SessionStore", "Unable to remove the temporary state file.", ex); }
        }
    }
}
