using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;

// Reproduces the two material final-review findings without touching live settings or scripts.
internal static class PreferenceFailureChecks
{
    /// <summary>Runs cleanup and real native lost-confirmation checks, optionally one boundary for RED evidence.</summary>
    internal static async Task RunAsync(string runtime, string root, Action<bool, string> check, string scope = "all")
    {
        Directory.CreateDirectory(root);
        if (scope != "confirmation") VerifyCleanup(root, check);
        if (scope != "cleanup") await VerifyLostConfirmationAsync(runtime, root, check);
    }

    /// <summary>Checks a legal trailing separator never prevents cleanup after partial extraction fails.</summary>
    private static void VerifyCleanup(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "Trailing output"); Directory.CreateDirectory(folder);
        var sentinel = Path.Combine(folder, "keep.txt"); File.WriteAllText(sentinel, "keep");
        var path = Path.Combine(root, "Unsafe.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("first.txt").Open())) writer.Write("partial");
            using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("blocked");
        }
        Exception? failure = null;
        try { new ArchiveExtractionService().Extract(path, folder + Path.DirectorySeparatorChar); }
        catch (Exception ex) { failure = ex; }
        check(failure is InvalidDataException && !failure.Message.Contains("cleanup target"),
            "a trailing separator preserves the original extraction failure");
        check(Directory.GetDirectories(folder, ".autohotkeyux-extract-*").Length == 0
            && File.ReadAllText(sentinel) == "keep" && File.Exists(path) && !File.Exists(Path.Combine(root, "escape.txt")),
            "partial output is removed while unrelated output and the source archive remain");
    }

    /// <summary>Drops query acknowledgements in a disposable copy of the actual AHK receiver after real persistence.</summary>
    private static async Task VerifyLostConfirmationAsync(string runtime, string root, Action<bool, string> check)
    {
        var key = @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        var settings = new AutoHotkeySettings(key);
        var folder = Path.Combine(root, "lost-confirmation"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Explorer Shortcuts.ahk"); new ExplorerShortcutInstaller(path).Ensure();
        var lost = Path.Combine(folder, "drop-query.txt"); File.WriteAllText(lost, "drop");
        var script = File.ReadAllText(path).Replace("InitializeShortcutPreferences()",
            "InitializeShortcutPreferences(\"HKCU\\" + key + "\\Modern\")");
        File.WriteAllText(path, script);
        var receiverPath = Path.Combine(folder, "ExplorerShortcuts", "v3", "Preferences.ahk");
        var receiver = File.ReadAllText(receiverPath).Replace("if parts[1] = \"query\"",
            "if parts[1] = \"query\" && FileExist(\"" + lost + "\")\n            return 2\n        if parts[1] = \"query\"");
        File.WriteAllText(receiverPath, receiver);
        var initial = new ShortcutPreferenceSnapshot(Guid.NewGuid().ToString("N"), ShortcutPreferences.Default with
            { TerminalEnabled = false, ArchiveEnabled = false });
        settings.Write("Modern", ShortcutPreferenceCodec.SettingName, ShortcutPreferenceCodec.Encode(initial));
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(folder, "sessions.json")));
        var startup = new ScriptStartupService(folder, settings, execution);
        var owner = new ShortcutPreferencesService(settings, startup, execution);
        try
        {
            check(execution.Run(path).State == ScriptState.Running, "lost-confirmation fixture owns its disposable interpreter");
            var timer = Stopwatch.StartNew();
            while (settings.Read("Modern", "ShortcutScriptToken").Length != 32 && timer.ElapsedMilliseconds < 5000) await Task.Delay(20);
            check(settings.Read("Modern", "ShortcutScriptToken").Length == 32, "lost-confirmation fixture exposes a private native endpoint");
            var updated = initial.Preferences with { TerminalWidth = 960 };
            var uncertain = false;
            try { await owner.SaveAsync(updated); } catch (UnconfirmedShortcutPreferencesException) { uncertain = true; }
            check(uncertain && owner.Saved == updated && owner.Warning is not null,
                "actual persistence with missing confirmation retains a durable session warning");
            var stored = settings.Read("Modern", ShortcutPreferenceCodec.SettingName);
            uncertain = false;
            try { await owner.SaveAsync(updated); } catch (UnconfirmedShortcutPreferencesException) { uncertain = true; }
            check(uncertain && owner.Warning is not null && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == stored,
                "equal-value saving cannot silently bypass pending runtime confirmation");
            File.Delete(lost);
            await owner.SaveAsync(updated);
            check(owner.Warning is null && owner.Saved == updated && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == stored,
                "a recovered query confirms the original revision without replaying the configure mutation");
        }
        finally { execution.Stop(path); await owner.DrainAsync(); Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
}
