using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;

// Covers changed service boundaries with isolated files, registry state and owned interpreters.
internal static class PreferenceChecks
{
    private static int _passed;

    /// <summary>Runs integration checks for settings, migration, output, history and native configuration.</summary>
    internal static async Task RunAsync(string runtime, string root, string repository)
    {
        Directory.CreateDirectory(root);
        var custom = Path.Combine(root, "Chosen destination 输出");
        Directory.CreateDirectory(custom);
        var snapshot = new ShortcutPreferenceSnapshot(Guid.NewGuid().ToString("N"),
            ShortcutPreferences.Default with { ArchiveDestination = "custom", ArchiveFolder = custom });
        var encoded = ShortcutPreferenceCodec.Encode(snapshot);
        Check(ShortcutPreferenceCodec.Decode(encoded) == snapshot, "the complete Unicode preference snapshot round-trips");
        Check(ShortcutPreferenceCodec.Decode("").Preferences == ShortcutPreferences.Default,
            "missing preferences preserve the existing Alt-left behavior");
        foreach (var invalid in new[] { encoded + "\nwidth=800", encoded.Replace("schema=1", "schema=2"),
            encoded.Replace("terminal-enabled=1", "terminal-enabled=true"), encoded.Replace("width=800", "width=800.5"),
            encoded.Replace("terminal-key=alt-left", "terminal-key=ctrl-left"), encoded + "\0", new string('x', 8193) })
            Reject(() => ShortcutPreferenceCodec.Decode(invalid), "malformed, reserved or unsupported snapshots are rejected");
        Check(ShortcutPreferenceCodec.Validate(snapshot.Preferences, true).Count == 0
            && ShortcutPreferenceCodec.Validate(snapshot.Preferences with { ArchiveFolder = @"\\?\C:\output" }, false).Count > 0,
            "a real fixed destination is accepted while device paths are blocked");

        var archive = Path.Combine(root, "Toolkit.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open())) writer.Write("hello");
        var extractor = new ArchiveExtractionService();
        var output = extractor.Extract(archive, custom);
        Check(Path.GetDirectoryName(output) == custom && File.Exists(archive)
            && File.ReadAllText(Path.Combine(output, "hello.txt")) == "hello", "custom extraction retains the source and exact contents");
        Check(extractor.Extract(archive, custom) == output + " (1)" && File.Exists(Path.Combine(output, "hello.txt")),
            "custom output collisions keep existing folders");
        Reject(() => extractor.Extract(archive, Path.Combine(root, "absent")), "an unavailable output root is rejected before staging");
        Check(WindowInspectionNative.ClassNN((nint)42, "Button", [], Stopwatch.StartNew()) == "Unavailable",
            "Window Spy does not invent ClassNN beyond the bounded child snapshot");
        VerifyMigration(root, repository);
        await VerifyQueueAsync(runtime, root, snapshot.Preferences);
        await PreferenceRuntimeChecks.RunAsync(runtime, root, snapshot.Preferences, Check);
        Console.WriteLine($"Preferences: {_passed} distinct checks passed.");
    }

    /// <summary>Checks upgrade ownership against the exact previous source and its two editable helpers.</summary>
    private static void VerifyMigration(string root, string repository)
    {
        var original = File.ReadAllBytes(Path.Combine(repository, "tools", "ModernShell.SmokeTests", "fixtures", "ExplorerShortcuts.v2.ahk"));
        foreach (var customized in new[] { false, true })
        {
            var folder = Path.Combine(root, customized ? "custom-v2" : "original-v2");
            var modules = Path.Combine(folder, "ExplorerShortcuts", "v2");
            Directory.CreateDirectory(modules);
            var path = Path.Combine(folder, "Explorer Shortcuts.ahk");
            File.WriteAllBytes(path, original);
            foreach (var name in new[] { "Shell", "Actions" }) File.Copy(
                Path.Combine(repository, "modern-shell", "Resources", "ExplorerShortcuts", "v2", name + ".ahk"), Path.Combine(modules, name + ".ahk"));
            var actions = Path.Combine(modules, "Actions.ahk");
            if (customized) File.AppendAllText(actions, "\n; user extension\n");
            var helper = File.ReadAllBytes(actions);
            var installer = new ExplorerShortcutInstaller(path);
            var changed = installer.Ensure();
            if (customized)
                Check(!changed && installer.Warning is not null && File.ReadAllBytes(path).SequenceEqual(original)
                    && File.ReadAllBytes(actions).SequenceEqual(helper), "a customized v2 helper prevents migration and is preserved");
            else
            {
                Check(changed && File.ReadAllText(path).StartsWith("; AutoHotkeyUX Explorer shortcuts v3")
                    && File.Exists(Path.Combine(folder, "ExplorerShortcuts", "v3", "Preferences.ahk")), "untouched v2 upgrades with all v3 modules");
                Check(File.ReadAllBytes(Directory.GetFiles(Path.Combine(folder, "backups")).Single()).SequenceEqual(original)
                    && File.ReadAllBytes(actions).SequenceEqual(helper) && !installer.Ensure(), "the exact predecessor backup and old helpers survive an idempotent upgrade");
            }
        }
    }

    /// <summary>Proves queued jobs retain their destination, failures remain visible, and disabled actions stop acceptance.</summary>
    private static async Task VerifyQueueAsync(string runtime, string root, ShortcutPreferences initial)
    {
        var key = @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        var settings = new AutoHotkeySettings(key);
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(root, "queue-sessions.json")));
        var startup = new ScriptStartupService(Path.Combine(root, "queue-scripts"), settings, execution);
        var preferences = new ShortcutPreferencesService(settings, startup, execution);
        var activity = new ShortcutActivityService();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var received = new List<string?>();
        var queue = new ShortcutCommandService(0, settings, (_, _) => { }, (path, destination) =>
        {
            received.Add(destination);
            if (path.EndsWith("first.zip")) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            if (path.EndsWith("bad.zip")) throw new InvalidDataException("Fixture failure");
            return Path.Combine(destination!, "Toolkit");
        }, preferences, activity);
        try
        {
            await preferences.SaveAsync(initial);
            var saved = settings.Read("Modern", ShortcutPreferenceCodec.SettingName);
            await RejectAsync(() => preferences.SaveAsync(initial with { TerminalShortcut = "ctrl-left" }),
                "invalid saving retains the previous snapshot");
            Check(saved == settings.Read("Modern", ShortcutPreferenceCodec.SettingName) && preferences.Saved == initial,
                "failed validation changes neither saved state nor persisted fields");
            string Request(string name) => "extract\n" + queue.Token + "\n" + Path.Combine(root, name);
            Check(queue.TryEnqueue(Request("first.zip")) == 1 && entered.Wait(TimeSpan.FromSeconds(3)), "extraction runs on the worker before the queue is changed");
            Check(queue.TryEnqueue(Request("bad.zip")) == 1 && queue.TryEnqueue(Request("second.zip")) == 1,
                "later archives are accepted while the first extraction is blocked");
            var nextFolder = Path.Combine(root, "Next output"); Directory.CreateDirectory(nextFolder);
            await preferences.SaveAsync(initial with { ArchiveEnabled = false, ArchiveFolder = nextFolder });
            Check(queue.TryEnqueue(Request("late.zip")) == 0, "disabling extraction blocks new requests without discarding accepted work");
            release.Set(); await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(received.Count == 3 && received.All(folder => folder == initial.ArchiveFolder)
                && activity.Snapshot().Count == 3 && activity.Snapshot().Count(entry => entry.Succeeded) == 2,
                "accepted jobs retain their original destination and continue after one failure");
            for (var i = 0; i < 55; i++) activity.Record(new(DateTimeOffset.Now, "Terminal", true, "source " + i, i, "Completed"));
            Check(activity.Snapshot().Count == 50 && activity.Snapshot()[0].Source == "source 54"
                && activity.Snapshot()[49].Source == "source 5", "session activity drops the oldest entry at its fixed limit of 50");
        }
        finally { release.Set(); await queue.DrainAsync(); await preferences.DrainAsync(); Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    /// <summary>Confirms a synchronous invalid request is rejected rather than silently applied.</summary>
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException) { Check(true, message); return; }
        Check(false, message);
    }

    /// <summary>Confirms a failed save is observable at the asynchronous service boundary.</summary>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); } catch (InvalidDataException) { Check(true, message); return; }
        Check(false, message);
    }

    /// <summary>Names each failure at its actual changed boundary.</summary>
    private static void Check(bool passed, string message)
    { if (!passed) throw new Exception("FAIL: " + message); _passed++; Console.WriteLine("PASS: " + message); }
}
