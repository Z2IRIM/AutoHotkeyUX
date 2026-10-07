using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

// Runs a bounded service integration probe using isolated files and real test-owned AutoHotkey interpreters.
var runtime = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
if (args.Contains("--capabilities-only"))
{
    await VisualCapabilityChecks.RunAsync(runtime, root);
    return;
}
if (args.Contains("--visual-only"))
{
    await VisualFlowChecks.RunAsync(runtime, root);
    return;
}
if (args.Contains("--preference-failures-only"))
{
    await PreferenceFailureChecks.RunAsync(runtime, root, (passed, message) =>
    {
        if (!passed) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }, args[2]);
    return;
}
if (args.Contains("--preferences-only"))
{
    await PreferenceChecks.RunAsync(runtime, root, Path.GetFullPath(args[2]));
    return;
}
var passed = 0;
var scriptRoot = Path.Combine(root, "Documents", "AutoHotkey");

if (args.Contains("--shortcuts-only"))
{
    await ShortcutChecks.RunAsync(runtime, root, Path.GetFullPath(args[2]));
    return;
}

// Records an assertion with enough context to identify the failed boundary.
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    passed++;
}

// Waits for observable asynchronous state rather than repeating full test runs.
async Task Until(Func<bool> condition, string description)
{
    var timer = Stopwatch.StartNew();
    while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(30);
    Check(condition(), description);
}

// Creates only disposable scripts beneath this probe's explicitly named root.
string Script(string name, string content)
{
    Directory.CreateDirectory(scriptRoot);
    var path = Path.Combine(scriptRoot, name);
    File.WriteAllText(path, "#Requires AutoHotkey v2.0\n#SingleInstance Off\n" + content, new UTF8Encoding(true));
    return path;
}

if (!args.Contains("--archives-only"))
{
using (var catalog = new ScriptCatalogService(scriptRoot))
{
    Check(catalog.Snapshot().Count == 0, "catalog accepts a missing/empty root");
    var events = 0;
    catalog.Changed += (_, _) => Interlocked.Increment(ref events);
    var file = Script("测试 hotkeys.ahk", "Persistent\n");
    await Until(() => catalog.Snapshot().Count == 1, "watcher discovers a newly created root and script");
    var before = events;
    var size = new FileInfo(file).Length;
    for (var index = 0; index < 5; index++) File.AppendAllText(file, "; saved\n");
    await Until(() => catalog.Snapshot().FirstOrDefault()?.Size > size, "watcher updates modified metadata");
    Check(events - before <= 2, "five-save burst is debounced");
    var renamed = Path.Combine(scriptRoot, "renamed.ahk");
    File.Move(file, renamed);
    await Until(() => catalog.Snapshot().FirstOrDefault()?.Name == "renamed.ahk", "watcher handles rename");
    Script("second.ahk", "Persistent\n");
    await Until(() => catalog.Snapshot().Count == 2, "catalog handles multiple scripts");
    File.Delete(renamed);
    await Until(() => catalog.Snapshot().Count == 1, "watcher handles deletion");
    File.Delete(Path.Combine(scriptRoot, "second.ahk"));
    Directory.Delete(scriptRoot);
    await Until(() => catalog.Snapshot().Count == 0, "deleted root becomes an empty catalog");
    Script("recreated.ahk", "Persistent\n");
    await Until(() => catalog.Snapshot().Count == 1, "watcher recovers when the root is recreated");
}

var persistent = Script("persistent 空格.ahk", "Persistent\n");
var statePath = Path.Combine(root, "sessions.json");
var service = new ScriptExecutionService(() => runtime, new ScriptSessionStore(statePath));
try
{
    Check(service.Run(Path.Combine(scriptRoot, "missing.ahk")).State == ScriptState.Failed, "missing script fails visibly");
    using (var missingRuntime = new ScriptExecutionService(() => "C:\\missing-runtime.exe", new ScriptSessionStore(Path.Combine(root, "missing-runtime.json"))))
        Check(missingRuntime.Run(persistent).State == ScriptState.Failed, "missing runtime fails visibly");
    var first = service.Run(persistent);
    Check(first.State == ScriptState.Running && first.ProcessId > 0, "runs a Chinese/spaced path with the real embedded interpreter");
    Check(service.Run(persistent).ProcessId == first.ProcessId, "duplicate Run reuses the owned process");
    var second = service.Restart(persistent);
    Check(second.State == ScriptState.Running && second.ProcessId != first.ProcessId, "Restart performs stop then run");
    var saved = File.ReadAllText(statePath);
    Check(saved.Contains("processStartUtc") && !saved.Contains("Persistent"), "session JSON stores identity without script content");
    service.Dispose();
    using (var live = Process.GetProcessById(second.ProcessId!.Value))
        Check(!live.HasExited, "manager disposal preserves a running interpreter");
    service = new ScriptExecutionService(() => runtime, new ScriptSessionStore(statePath));
    service.Rehydrate();
    Check(service.Snapshot().Single().ProcessId == second.ProcessId, "rehydrates matching PID/start-time/runtime identity");
    Check(service.Run(persistent).ProcessId == second.ProcessId, "rehydrated handles remain usable for duplicate Run");
    var forgedPath = Path.Combine(root, "forged.json");
    new ScriptSessionStore(forgedPath).Save([new(persistent, second.ProcessId!.Value, second.ProcessStartUtc!.Value.AddSeconds(1))]);
    using (var forged = new ScriptExecutionService(() => runtime, new ScriptSessionStore(forgedPath)))
    {
        forged.Rehydrate();
        Check(forged.Snapshot().Count == 0, "rejects reused/mismatched PID start time");
    }
    new ScriptSessionStore(forgedPath).Save([new(persistent, second.ProcessId!.Value, second.ProcessStartUtc!.Value)]);
    using (var foreign = new ScriptExecutionService(() => Path.Combine(Environment.SystemDirectory, "cmd.exe"), new ScriptSessionStore(forgedPath)))
    {
        foreign.Rehydrate();
        Check(foreign.Snapshot().Count == 0, "rejects an unexpected process executable");
    }
    service.Stop(persistent);
    Check(service.Snapshot().Single().State == ScriptState.Stopped, "Stop reaches the terminal state");
    new ScriptSessionStore(forgedPath).Save([new(persistent, second.ProcessId!.Value, second.ProcessStartUtc!.Value)]);
    using (var stale = new ScriptExecutionService(() => runtime, new ScriptSessionStore(forgedPath)))
    {
        stale.Rehydrate();
        Check(stale.Snapshot().Count == 0, "discards a no-longer-existing PID");
    }
    File.WriteAllText(forgedPath, "{bad json");
    var corrupt = new ScriptSessionStore(forgedPath);
    Check(corrupt.Load().Count == 0 && corrupt.LastError is not null, "corrupted session JSON is recoverable and diagnosed");
    var immediate = Script("immediate.ahk", "ExitApp 0\n");
    service.Run(immediate);
    await Until(() => service.Snapshot().Any(session => session.ScriptPath == immediate && session.State == ScriptState.Stopped), "immediate normal exit is registered correctly");
    var failed = Script("failed.ahk", "ExitApp 23\n");
    service.Run(failed);
    await Until(() => service.Snapshot().Any(session => session.ScriptPath == failed && session.State == ScriptState.Failed), "nonzero interpreter exit is reported as Failed");
    var child = Script("child.ahk", "Persistent\n");
    var childPidFile = Path.Combine(root, "child-pid.txt");
    var parent = Script("parent.ahk", $"Run('\"{runtime}\" \"{child}\"', , , &childPid)\nFileAppend(String(childPid), '{childPidFile}', 'UTF-8')\nPersistent\n");
    service.Run(parent);
    await Until(() => File.Exists(childPidFile) && new FileInfo(childPidFile).Length > 0, "test parent starts a separate child interpreter");
    using var childProcess = Process.GetProcessById(int.Parse(File.ReadAllText(childPidFile)));
    _ = childProcess.SafeHandle;
    try
    {
        service.Stop(parent);
        Check(!childProcess.HasExited, "Stop preserves applications started by the script");
    }
    finally { if (!childProcess.HasExited) { childProcess.Kill(); childProcess.WaitForExit(3000); } }
    var invalid = Script("syntax error.ahk", "this is not valid (\n");
    service.Run(invalid);
    await Until(() => service.Snapshot().Any(session => session.ScriptPath == invalid && session.State == ScriptState.Failed), "script syntax errors exit without a blocking error dialog");
    var shortLived = Script("exit-race.ahk", "Sleep 15\nExitApp 0\n");
    for (var iteration = 0; iteration < 12; iteration++)
    {
        service.Run(shortLived);
        await Task.Delay(iteration % 3 == 0 ? 25 : 5);
        await Task.Run(() => service.Stop(shortLived)).WaitAsync(TimeSpan.FromSeconds(3));
    }
    Check(service.Snapshot().Any(session => session.ScriptPath == shortLived && session.State == ScriptState.Stopped), "natural-exit/Stop race remains responsive and idempotent across 12 lifetimes");
    var isolatedKey = @"Software\AutoHotkeyUX.Modern.Verification\" + Guid.NewGuid().ToString("N");
    try
    {
        var settings = new AutoHotkeySettings(isolatedKey);
        var startup = new ScriptStartupService(scriptRoot, settings, service);
        startup.SetRunAtSignIn(Path.Combine(scriptRoot, "deleted-startup.ahk"), true);
        startup.SetRunAtSignIn(persistent, true);
        startup.StartSelected();
        Check(startup.LastWarning is not null && service.Snapshot().Any(session => session.ScriptPath == persistent && session.State == ScriptState.Running), "one missing startup script is diagnosed while later scripts still start");
        service.Stop(persistent);
    }
    finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(isolatedKey, throwOnMissingSubKey: false); }
}
finally
{
    foreach (var session in service.Snapshot().Where(session => session.State == ScriptState.Running)) service.Stop(session.ScriptPath);
    service.Dispose();
}

using (var validation = Process.Start(new ProcessStartInfo(runtime)
{
    UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
    ArgumentList = { "/ErrorStdOut=UTF-8", Path.GetFullPath(args[2]), "--validate" }
})!)
{
    var errors = await validation.StandardError.ReadToEndAsync();
    var output = await validation.StandardOutput.ReadToEndAsync();
    await validation.WaitForExitAsync();
    Check(validation.ExitCode == 0 && errors.Length == 0 && output.Length == 0, "built-in AHK v2 script passes interpreter validation: " + errors + output);
}
Check(WindowsCommandLine.Split("\"C:\\Program Files\\Editor.exe\" --reuse-window \"%1\"")
    .SequenceEqual(["C:\\Program Files\\Editor.exe", "--reuse-window", "%1"]), "editor command parser preserves quoted arguments");
}

var extractor = new ArchiveExtractionService();
// Creates archive fixtures independently of the production extraction library.
string Zip(string name, params (string Name, string Content)[] entries)
{
    var path = Path.Combine(root, name + ".zip");
    using var stream = File.Create(path);
    using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
    foreach (var entry in entries)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entry.Name, CompressionLevel.NoCompression).Open());
        writer.Write(entry.Content);
    }
    return path;
}

// Checks rejected archives leave no committed output or staging directory.
void Reject(string archive, string description)
{
    var rejected = false;
    try { extractor.Extract(archive); }
    catch (Exception) { rejected = true; }
    Check(rejected && !Directory.EnumerateDirectories(root, ".autohotkeyux-extract-*").Any(), description);
}

var archivePath = Zip("中文 空格", ("nested/测试.txt", "payload"));
var outputPath = extractor.Extract(archivePath);
Check(File.ReadAllText(Path.Combine(outputPath, "nested", "测试.txt")) == "payload" && File.Exists(archivePath), "ZIP extracts unicode paths and preserves its source");
File.WriteAllText(Path.Combine(outputPath, "existing.txt"), "keep");
var duplicate = extractor.Extract(archivePath);
Check(duplicate != outputPath && File.ReadAllText(Path.Combine(outputPath, "existing.txt")) == "keep", "existing extraction folder is preserved and a new name is used");
Reject(Zip("traversal", ("../escape.txt", "unsafe")), "rejects parent traversal and rolls back partial output");
Check(!File.Exists(Path.Combine(root, "escape.txt")), "traversal never writes outside its extraction root");
Reject(Zip("alternate-stream", ("file.txt:payload", "unsafe")), "rejects Windows alternate data streams");
Reject(Zip("reserved", ("NUL.txt", "unsafe")), "rejects reserved Windows device paths");
Reject(Zip("collision", ("a.txt", "one"), ("A.txt", "two")), "rejects case-insensitive duplicate files");
var badCrc = Zip("checksum", ("payload.txt", "CRC_TEST_PAYLOAD"));
var bytes = File.ReadAllBytes(badCrc);
var marker = Encoding.UTF8.GetBytes("CRC_TEST_PAYLOAD");
var offset = Enumerable.Range(0, bytes.Length - marker.Length).First(index => bytes.AsSpan(index, marker.Length).SequenceEqual(marker));
bytes[offset] ^= 1;
File.WriteAllBytes(badCrc, bytes);
Reject(badCrc, "rejects ZIP checksum corruption before committing the folder");
var tarRoot = Path.Combine(root, "tar-source");
Directory.CreateDirectory(tarRoot);
File.WriteAllText(Path.Combine(tarRoot, "hello.txt"), "tar payload");
var tarPath = Path.Combine(root, "sample.tar");
TarFile.CreateFromDirectory(tarRoot, tarPath, includeBaseDirectory: false);
Check(File.ReadAllText(Path.Combine(extractor.Extract(tarPath), "hello.txt")) == "tar payload", "TAR extraction works");
var gzipPath = Path.Combine(root, "sample.tgz");
using (var file = File.Create(gzipPath))
using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
using (var input = File.OpenRead(tarPath)) input.CopyTo(gzip);
Check(File.ReadAllText(Path.Combine(extractor.Extract(gzipPath), "hello.txt")) == "tar payload", "TGZ extraction uses the compressed TAR reader");
if (args.Length > 3 && Directory.Exists(args[3]))
{
    foreach (var fixture in Directory.EnumerateFiles(args[3]))
    {
        var copy = Path.Combine(root, Path.GetFileName(fixture));
        File.Copy(fixture, copy);
        var folder = extractor.Extract(copy);
        Check(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(), Path.GetExtension(copy) + " upstream fixture extracts real entries");
    }
}
Console.WriteLine($"RESULT: {passed} checks passed; fixtures retained at {root}");
