using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Installs v3 modules and upgrades only hash-confirmed untouched predecessors while preserving custom source.</summary>
internal sealed class ExplorerShortcutInstaller
{
    private const string CurrentMarker = "; AutoHotkeyUX Explorer shortcuts v3";
    private const string V2Marker = "; AutoHotkeyUX Explorer shortcuts v2";
    private const string V2Hash = "0776E9D92EE67B0684BFCFBFCB1DC34BE395BDF211AC936ADCD8B0F8EA2382C7";
    private static readonly Dictionary<string, string> V2Modules = new()
    {
        ["Shell"] = "209F5B7F6BCB4CEA5171DDA81134CD14EEA3804E51876AA7790F3CB05DC98309",
        ["Actions"] = "04E4D73D5C18B7E61DDC1BEC22D27819EC4716F939FE28A09A9797CDB7BDB591"
    };
    private const string PreviousMarker = "; AutoHotkeyUX Explorer shortcuts v1";
    private const string PreviousV3ActionsHash = "EFE46F26918874B073B9ABB31AC9732F275E9C5685150F5AD5C37A2ADEBD403A";
    private const string PreviousHash = "52A86335474FBD5A76FA81825F93F365F3E5AD52E4E459778D0E2AB0D831941D";
    private readonly string _path;
    internal string? Warning { get; private set; }

    /// <summary>Targets the existing editable built-in path, keeping helper modules below the same script root.</summary>
    internal ExplorerShortcutInstaller(string path) => _path = path;

    /// <summary>Preserves user edits, backs up an untouched predecessor and reports whether a running script needs restart.</summary>
    internal bool Ensure()
    {
        Warning = null;
        var root = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(root);
        if (!File.Exists(_path))
        {
            EnsureModules(root);
            WriteNew(_path, ReadResource("ExplorerShortcuts.ahk"));
            return false;
        }
        // An exclusive writer prevents a concurrent editor from changing the verified version during upgrade.
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var text = reader.ReadToEnd();
        var marker = text.Split('\n', 2)[0].TrimEnd('\r');
        if (marker == CurrentMarker)
        {
            var ownedRoot = Hash(text) == Hash(ReadResource("ExplorerShortcuts.ahk"));
            if (!ownedRoot) PreserveWarning("The v3 root script has user edits.");
            return EnsureModules(root, allowUpgrade: ownedRoot);
        }
        if (marker is not (PreviousMarker or V2Marker))
            throw new IOException($"A user file already uses '{_path}'. Rename it before enabling Explorer shortcuts.");
        var untouched = marker == PreviousMarker ? Hash(text) == PreviousHash : Hash(text) == V2Hash && V2Modules.All(module =>
            File.Exists(Path.Combine(root, "ExplorerShortcuts", "v2", module.Key + ".ahk"))
            && Hash(File.ReadAllText(Path.Combine(root, "ExplorerShortcuts", "v2", module.Key + ".ahk"))) == module.Value);
        if (!untouched)
        {
            PreserveWarning("The existing shortcut script or a helper has user edits or is missing.");
            return false;
        }
        EnsureModules(root);
        if (Warning is not null) return false;
        stream.Position = 0;
        var original = new byte[checked((int)stream.Length)];
        stream.ReadExactly(original);
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(backups);
        var previousVersion = marker == V2Marker ? "v2" : "v1";
        var backup = Path.Combine(backups, $"Explorer Shortcuts.{previousVersion}.{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
        using (var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { copy.Write(original); copy.Flush(true); }
        var encoding = new UTF8Encoding(true);
        var replacement = encoding.GetPreamble().Concat(encoding.GetBytes(ReadResource("ExplorerShortcuts.ahk"))).ToArray();
        try
        {
            stream.Position = 0;
            stream.Write(replacement);
            stream.SetLength(replacement.Length);
            stream.Flush(true);
        }
        catch
        {
            stream.Position = 0;
            stream.Write(original);
            stream.SetLength(original.Length);
            stream.Flush(true);
            throw;
        }
        ServiceDiagnostics.Write("Startup", $"Upgraded untouched Explorer shortcuts to v3; backup: {backup}");
        return true;
    }

    /// <summary>Preflights every helper before upgrading hash-confirmed shipped predecessors as one owned group.</summary>
    private bool EnsureModules(string root, bool allowUpgrade = true)
    {
        var directory = Path.Combine(root, "ExplorerShortcuts", "v3");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Shortcut helper directory cannot be a link.");
        var streams = new List<FileStream>();
        var replacements = new List<(string Name, FileStream Stream, byte[] Before, string Text)>();
        try
        {
            foreach (var name in new[] { "Shell", "Actions", "Preferences" })
            {
                var path = Path.Combine(directory, name + ".ahk");
                var shipped = ReadResource($"ExplorerShortcuts.{name}.ahk");
                if (!File.Exists(path)) { WriteNew(path, shipped); continue; }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Shortcut helpers cannot be links.");
                var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); streams.Add(file);
                if (file.Length > 256 * 1024) throw new IOException("Shortcut helper is too large.");
                var before = new byte[(int)file.Length]; file.ReadExactly(before);
                using var reader = new StreamReader(new MemoryStream(before), Encoding.UTF8, true);
                var existing = reader.ReadToEnd();
                if (Hash(existing) == Hash(shipped)) continue;
                if (Hash(existing) == Hash(ReadResource("VisualFlow.V2." + name + ".ahk"))
                    || name == "Actions" && Hash(existing) == PreviousV3ActionsHash) replacements.Add((name, file, before, shipped));
                else PreserveWarning($"The v3 {name} helper has user edits.");
            }
            if (!allowUpgrade || Warning is not null || replacements.Count == 0) return false;
            var backups = Path.Combine(root, "backups"); Directory.CreateDirectory(backups);
            if ((File.GetAttributes(backups) & FileAttributes.ReparsePoint) != 0) throw new IOException("Shortcut backup directory cannot be a link.");
            var backupPaths = new List<string>();
            foreach (var item in replacements)
            {
                var backup = Path.Combine(backups, $"Explorer Shortcuts.v3.{item.Name}.{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
                using (var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { copy.Write(item.Before); copy.Flush(true); }
                backupPaths.Add(backup);
            }
            try
            {
                var encoding = new UTF8Encoding(true);
                foreach (var item in replacements) Rewrite(item.Stream, encoding.GetPreamble().Concat(encoding.GetBytes(item.Text)).ToArray());
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var item in replacements) { try { Rewrite(item.Stream, item.Before); } catch (Exception recovery) { errors.Add(recovery); } }
                if (errors.Count > 1) throw new AggregateException("Shortcut helper upgrade failed; recovery copies are in " + backups, errors);
                throw;
            }
            ServiceDiagnostics.Write("Startup", "Updated owned v3 helpers; backups: " + string.Join(", ", backupPaths));
            return true;
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }

    /// <summary>Durably writes verified helper bytes through the already exclusive writer.</summary>
    private static void Rewrite(FileStream stream, byte[] bytes)
    { stream.Position = 0; stream.Write(bytes); stream.SetLength(bytes.Length); stream.Flush(true); }

    /// <summary>Records why automatic migration was refused, keeping all editable source in place.</summary>
    private void PreserveWarning(string reason)
    {
        Warning = reason + " It was preserved; automatic v3 migration/configuration may be unavailable for this custom script.";
        ServiceDiagnostics.Write("Startup", Warning);
    }

    /// <summary>Matches text-equivalent files across Git line endings and UTF-8 BOM differences.</summary>
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));

    /// <summary>Stages a durable resource then moves without overwrite, preventing partial includes and concurrent user replacement.</summary>
    private static void WriteNew(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
                { writer.Write(text); writer.Flush(); }
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Loads the shipped editable script source through its stable embedded-resource name.</summary>
    private static string ReadResource(string name)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoHotkeyUX.Modern." + name)
            ?? throw new InvalidOperationException("The embedded shortcut resource is missing: " + name);
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }
}
