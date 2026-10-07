using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Tests the real AHK receiver exclusively under a generated HKCU fixture key.
internal static class PreferenceRuntimeChecks
{
    /// <summary>Starts one disposable host and verifies authenticated live commits, rejection, idempotency and ownership.</summary>
    internal static async Task RunAsync(string runtime, string root, ShortcutPreferences preferences, Action<bool, string> check)
    {
        var key = @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        var settings = new AutoHotkeySettings(key);
        var folder = Path.Combine(root, "native-host"); Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "Explorer Shortcuts.ahk");
        new ExplorerShortcutInstaller(script).Ensure();
        var initial = new ShortcutPreferenceSnapshot(Guid.NewGuid().ToString("N"), preferences with { TerminalEnabled = false, ArchiveEnabled = false });
        settings.Write("Modern", ShortcutPreferenceCodec.SettingName, ShortcutPreferenceCodec.Encode(initial));
        var ready = Path.Combine(folder, "ready.txt");
        var start = new ProcessStartInfo(runtime) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/ErrorStdOut=UTF-8", script, "--preferences-host", "HKCU\\" + key + "\\Modern", ready }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(ready) && !process.HasExited && timer.ElapsedMilliseconds < 5000) await Task.Delay(20);
            if (process.HasExited) throw new Exception("Native preference host failed: " + await stdout + await stderr);
            check(File.Exists(ready), "the isolated v3 host publishes its real native endpoint");
            var session = new RunningScriptSession(script, process.Id, process.StartTime.ToUniversalTime(), DateTime.UtcNow, ScriptState.Running);
            var sender = new ShortcutPreferencesRuntime(settings, () => session);
            var next = new ShortcutPreferenceSnapshot(Guid.NewGuid().ToString("N"), initial.Preferences with
            { TerminalEnabled = true, TerminalShortcut = "ctrl-alt-middle", TerminalWidth = 960, TerminalPosition = "center" });
            sender.Apply(next);
            var encoded = ShortcutPreferenceCodec.Encode(next);
            check(settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == encoded,
                "live AHK bindings and the persisted snapshot acknowledge one configured revision");
            sender.Apply(next);
            check(settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == encoded, "replaying the same revision is idempotent");
            var window = (nint)long.Parse(settings.Read("Modern", "ShortcutScriptWindow"));
            var token = settings.Read("Modern", "ShortcutScriptToken");
            check(Send(window, "configure\nincorrect-token\n" + encoded) == 0, "a wrong endpoint token cannot configure the script");
            foreach (var invalid in new[] { encoded.Replace("width=960", "width=479"), encoded + "\nunknown=value",
                encoded.Replace("terminal-key=ctrl-alt-middle", "terminal-key=ctrl-left") })
                check(Send(window, "configure\n" + token + "\n" + invalid) == 2
                    && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == encoded
                    && Send(window, "query\n" + token + "\n" + next.Revision) == 1,
                    "the real receiver rejects invalid settings while retaining the running revision");
            var mismatch = new ShortcutPreferencesRuntime(settings, () => session with { ProcessStartUtc = session.ProcessStartUtc!.Value.AddSeconds(1) });
            var refused = false;
            try { mismatch.Apply(next); } catch (InvalidOperationException) { refused = true; }
            check(refused && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == encoded,
                "a recycled or mismatched process identity cannot receive settings");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
        }
        await VerifyCustomAsync(runtime, root, preferences, check);
    }

    /// <summary>Confirms an independently edited persistent script cannot be overwritten or silently configured.</summary>
    private static async Task VerifyCustomAsync(string runtime, string root, ShortcutPreferences preferences, Action<bool, string> check)
    {
        var key = @"Software\AutoHotkeyUX.Verify\" + Guid.NewGuid().ToString("N");
        var settings = new AutoHotkeySettings(key);
        var folder = Path.Combine(root, "custom-host"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Explorer Shortcuts.ahk");
        var original = "#Requires AutoHotkey v2.0\n#SingleInstance Off\n#NoTrayIcon\nPersistent true\n; custom action\n";
        File.WriteAllText(path, original);
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(folder, "sessions.json")));
        var startup = new ScriptStartupService(folder, settings, execution);
        var owner = new ShortcutPreferencesService(settings, startup, execution);
        try
        {
            await owner.SaveAsync(preferences);
            var saved = settings.Read("Modern", ShortcutPreferenceCodec.SettingName);
            var session = execution.Run(path);
            check(session.State == ScriptState.Running, "the edited script has a real test-owned persistent interpreter");
            var refused = false;
            try { await owner.SaveAsync(preferences with { TerminalWidth = 1100 }); } catch (InvalidOperationException) { refused = true; }
            check(refused && owner.Saved == preferences && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == saved
                && File.ReadAllText(path) == original && execution.Snapshot().Single().ProcessId == session.ProcessId,
                "an incompatible custom script preserves source, process and the previous saved settings");
        }
        finally { execution.Stop(path); await owner.DrainAsync(); Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    /// <summary>Sends bounded diagnostic bytes directly to the real receiver without altering production registry settings.</summary>
    private static nuint Send(nint window, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body + '\0'); var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var packet = new CopyData { Tag = 0x41584B33, Length = bytes.Length, Data = memory };
            if (SendMessageTimeout(window, 0x4A, 0, ref packet, 0x23, 1000, out var result) == 0)
                throw new TimeoutException("The isolated native preference receiver did not answer.");
            return result;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CopyData { internal nuint Tag; internal int Length; internal nint Data; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message,
        nuint wParam, ref CopyData packet, uint flags, uint timeout, out nuint result);
}
