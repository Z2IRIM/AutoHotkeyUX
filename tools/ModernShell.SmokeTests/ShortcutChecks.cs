using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Diagnostics;
using System.Text;

internal static class ShortcutChecks
{
    /// <summary>Checks only changed boundaries: migration, live restart, nonblocking queue, duplicates, failure isolation and drain.</summary>
    internal static async Task RunAsync(string runtime, string root, string previousScript)
    {
        var count = 0;
        // Identifies the failed boundary and counts each distinct behavioral assertion.
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            Console.WriteLine("PASS: " + description);
            count++;
        }
        var key = @"Software\AutoHotkeyUX.Modern.Verification\" + Guid.NewGuid().ToString("N");
        var scriptRoot = Path.Combine(root, "shortcuts");
        Directory.CreateDirectory(scriptRoot);
        var path = Path.Combine(scriptRoot, "Explorer Shortcuts.ahk");
        var original = File.ReadAllBytes(previousScript);
        var settings = new AutoHotkeySettings(key);
        using var execution = new ScriptExecutionService(() => runtime, new ScriptSessionStore(Path.Combine(root, "shortcut-sessions.json")));
        try
        {
            File.WriteAllBytes(path, original);
            var first = execution.Run(path);
            Check(first.State == ScriptState.Running, "untouched v1 has an owned live interpreter");
            var startup = new ScriptStartupService(scriptRoot, settings, execution);
            startup.SetExplorerEnabled(true);
            var upgraded = execution.Snapshot().Single(session => session.ScriptPath == path);
            Check(upgraded.State == ScriptState.Running && upgraded.ProcessId != first.ProcessId, "v1 upgrade restarts only its owned interpreter");
            Check(File.ReadAllText(path).StartsWith("; AutoHotkeyUX Explorer shortcuts v3")
                && File.Exists(Path.Combine(scriptRoot, "ExplorerShortcuts", "v3", "Shell.ahk")), "v3 and include modules install together");
            Check(Directory.GetFiles(Path.Combine(scriptRoot, "backups")).Single() is var backup
                && File.ReadAllBytes(backup).SequenceEqual(original), "v1 backup retains the exact original bytes");
            startup.SetExplorerEnabled(true);
            Check(execution.Snapshot().Single(session => session.ScriptPath == path).ProcessId == upgraded.ProcessId,
                "repeated enable does not restart v3 or create another backup");
            execution.Stop(path);
            var custom = Encoding.UTF8.GetBytes(File.ReadAllText(previousScript) + "\n; user customization\n");
            File.WriteAllBytes(path, custom);
            startup.SetExplorerEnabled(true);
            Check(File.ReadAllBytes(path).SequenceEqual(custom) && startup.LastWarning is not null, "edited v1 is preserved and diagnosed");
            execution.Stop(path);
            File.WriteAllText(path, "; independent user script\n");
            var refused = false;
            try { startup.SetExplorerEnabled(true); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(path) == "; independent user script\n", "conflicting user file is refused without replacement");

            using var release = new ManualResetEventSlim(false);
            using var entered = new ManualResetEventSlim(false);
            var completed = 0;
            var failures = 0;
            var queue = new ShortcutCommandService(IntPtr.Zero, settings, (_, error) => { if (error) Interlocked.Increment(ref failures); }, (archive, _) =>
            {
                if (archive.EndsWith("first.zip")) { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
                if (archive.EndsWith("bad.zip")) throw new InvalidDataException("fixture failure");
                Interlocked.Increment(ref completed);
                return archive + ".output";
            });
            // Creates an authenticated protocol payload without doing file IO in the receiver.
            string Request(string name) => "extract\n" + queue.Token + "\n" + Path.Combine(root, name);
            try
            {
                Check(queue.TryEnqueue("extract\nwrong-token\n" + Path.Combine(root, "first.zip")) == 0
                    && queue.TryEnqueue("extract\n" + queue.Token + "\nrelative.zip") == 0
                    && queue.TryEnqueue(Request("unsupported.exe")) == 0, "invalid token, relative path and unsupported suffix are rejected");
                var timer = Stopwatch.StartNew();
                Check(queue.TryEnqueue(Request("first.zip")) == 1 && timer.ElapsedMilliseconds < 100, "acceptance returns before blocked extraction completes");
                Check(entered.Wait(TimeSpan.FromSeconds(3)), "accepted job reaches its worker");
                Check(queue.TryEnqueue(Request("first.zip")) == 2, "duplicate active archive is coalesced");
                Check(new[] { "bad.zip", "second.zip", "third.zip", "fourth.zip" }.All(name => queue.TryEnqueue(Request(name)) == 1)
                    && queue.TryEnqueue(Request("overflow.zip")) == 3, "pending capacity is bounded to four jobs");
                var drain = queue.DrainAsync();
                Check(!drain.IsCompleted && queue.TryEnqueue(Request("late.zip")) == 0, "exit stops acceptance and waits for accepted work");
                release.Set();
                await drain.WaitAsync(TimeSpan.FromSeconds(5));
                Check(completed == 4 && failures == 1, "one extraction failure does not discard later jobs and drain completes");
            }
            finally { release.Set(); await queue.DrainAsync(); }
        }
        finally
        {
            foreach (var session in execution.Snapshot().Where(session => session.State == ScriptState.Running)) execution.Stop(session.ScriptPath);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false);
        }
        Console.WriteLine($"Shortcuts: {count} checks passed.");
    }
}
