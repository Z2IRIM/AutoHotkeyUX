using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AutoHotkeyUX.Modern.Services;

if (args.Contains("--delay-extractor")) { Thread.Sleep(30000); return; }
var fixtureRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".verification", $"doc-regression-{Guid.NewGuid():N}"));
Directory.CreateDirectory(fixtureRoot);
var results = new Dictionary<string, bool>();
var links = new List<string>();
try
{
    var midRoot = CreateCache(Path.Combine(fixtureRoot, "middle"));
    var target = Path.Combine(fixtureRoot, "middle-docs");
    Directory.Move(Path.Combine(midRoot, "docs"), target);
    CreateJunction(Path.Combine(midRoot, "docs"), target);
    results["IntermediateJunctionRejected"] = !DocumentationService.ValidateCache(midRoot, "fixture", CancellationToken.None);
    var extraRoot = CreateCache(Path.Combine(fixtureRoot, "extra"));
    var extraTarget = Path.Combine(fixtureRoot, "outside-content"); Directory.CreateDirectory(extraTarget);
    await File.WriteAllTextAsync(Path.Combine(extraTarget, "private.txt"), "isolated local content");
    CreateJunction(Path.Combine(extraRoot, "docs", "extra"), extraTarget);
    results["UnlistedJunctionRejected"] = !DocumentationService.ValidateCache(extraRoot, "fixture", CancellationToken.None);
    var fileRoot = CreateCache(Path.Combine(fixtureRoot, "unlisted-file"));
    await File.WriteAllTextAsync(Path.Combine(fileRoot, "docs", "extra.htm"), "unlisted");
    results["UnlistedFileRejected"] = !DocumentationService.ValidateCache(fileRoot, "fixture", CancellationToken.None);
    var ancestorRoot = CreateCache(Path.Combine(fixtureRoot, "real-parent", "cache"));
    var parentLink = Path.Combine(fixtureRoot, "linked-parent"); CreateJunction(parentLink, Path.GetDirectoryName(ancestorRoot)!);
    results["AncestorJunctionRejected"] = !DocumentationService.ValidateCache(Path.Combine(parentLink, "cache"), "fixture", CancellationToken.None);
    results["RegularCacheAccepted"] = DocumentationService.ValidateCache(CreateCache(Path.Combine(fixtureRoot, "valid")), "fixture", CancellationToken.None);
    await VerifyShutdownAsync();
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = results.Values.All(value => value) ? 0 : 1;
}
finally
{
    foreach (var link in links.AsEnumerable().Reverse()) if (Directory.Exists(link)) Directory.Delete(link);
    if (!fixtureRoot.StartsWith(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".verification")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture cleanup escaped its root.");
    Directory.Delete(fixtureRoot, true);
}

// Builds a known manifest and bytes so only path-tree validation varies across cases.
string CreateCache(string root)
{
    Directory.CreateDirectory(root);
    var files = new Dictionary<string, string>();
    foreach (var relative in new[] { "docs/index.htm", "docs/static/content.js", "docs/static/source/data_toc.js", "docs/static/theme.css" })
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative); files.Add(relative, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    File.WriteAllText(Path.Combine(root, ".cache.json"), JsonSerializer.Serialize(new { SourceHash = "fixture", Files = files }));
    return root;
}

// Creates only a junction inside this isolated fixture using the Windows filesystem cmdlet.
void CreateJunction(string path, string target)
{
    if (!path.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !target.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture link.");
    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-NoProfile", "-Command", $"New-Item -ItemType Junction -Path '{path.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null" }) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!; process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd());
    links.Add(path);
}

// Holds an owned extractor alive to check cancellation and cleanup before the application can exit.
async Task VerifyShutdownAsync()
{
    var runtime = Path.Combine(fixtureRoot, "runtime"); Directory.CreateDirectory(runtime);
    var chm = Path.Combine(runtime, "AutoHotkey.chm"); await File.WriteAllTextAsync(chm, "isolated shutdown CHM fixture");
    var started = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
    string? staging = null;
    using var cancellation = new CancellationTokenSource();
    var service = new DocumentationService(new(Path.Combine(runtime, "AutoHotkey64.exe")), command =>
    {
        staging = command.ArgumentList[1];
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--delay-extractor");
        var process = Process.Start(start)!; started.TrySetResult(process); return process;
    });
    var preparation = service.EnsureReadyAsync(cancellation.Token);
    var owned = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    using var observation = Process.GetProcessById(owned.Id);
    try
    {
        var drain = typeof(DocumentationService).GetMethod("CancelAndDrainAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (drain is null) cancellation.Cancel(); else await ((Task)drain.Invoke(service, null)!).WaitAsync(TimeSpan.FromSeconds(10));
        results["ShutdownOwnedExtractorExited"] = drain is not null && observation.HasExited;
        results["ShutdownStagingCleaned"] = drain is not null && staging is not null && !Directory.Exists(staging);
        var rejected = false;
        if (drain is not null) { try { await service.EnsureReadyAsync(CancellationToken.None); } catch (InvalidOperationException) { rejected = true; } }
        results["ShutdownRejectsNewPreparation"] = rejected;
    }
    finally
    {
        cancellation.Cancel();
        try { await preparation.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
        if (!observation.HasExited) { observation.Kill(true); await observation.WaitForExitAsync(); }
    }
}
