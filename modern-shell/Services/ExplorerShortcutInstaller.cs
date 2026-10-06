using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Installs versioned shortcut modules and upgrades only the byte-equivalent, untouched built-in v1 script.</summary>
internal sealed class ExplorerShortcutInstaller
{
    private const string CurrentMarker = "; AutoHotkeyUX Explorer shortcuts v2";
    private const string PreviousMarker = "; AutoHotkeyUX Explorer shortcuts v1";
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
        if (marker == CurrentMarker) { EnsureModules(root); return false; }
        if (marker != PreviousMarker)
            throw new IOException($"A user file already uses '{_path}'. Rename it before enabling Explorer shortcuts.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));
        if (hash != PreviousHash)
        {
            Warning = "Explorer Shortcuts.ahk has user edits. It was preserved; the v2 fixes were not applied to this custom script.";
            ServiceDiagnostics.Write("Startup", Warning);
            return false;
        }
        EnsureModules(root);
        stream.Position = 0;
        var original = new byte[checked((int)stream.Length)];
        stream.ReadExactly(original);
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, $"Explorer Shortcuts.v1.{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
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
        ServiceDiagnostics.Write("Startup", $"Upgraded untouched Explorer shortcuts to v2; backup: {backup}");
        return true;
    }

    /// <summary>Creates missing versioned modules only; existing helper edits are never replaced.</summary>
    private static void EnsureModules(string root)
    {
        var directory = Path.Combine(root, "ExplorerShortcuts", "v2");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "Shell", "Actions" })
        {
            var path = Path.Combine(directory, name + ".ahk");
            if (!File.Exists(path)) WriteNew(path, ReadResource($"ExplorerShortcuts.{name}.ahk"));
        }
    }

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
