using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Verifies bundled resources and real in-app navigation before checking tool integrations.</summary>
    internal async Task<object> VerifyToolsAsync()
    {
        using var compilerResource = GetType().Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern.Tools.Ahk2Exe.zip")
            ?? throw new InvalidDataException("The published application is missing its compiler ZIP.");
        var compilerHash = Convert.ToHexString(await SHA256.HashDataAsync(compilerResource));
        if (compilerHash != "C29B8C3A5124850D79FC9E66E2CA79677C377D7F31631AD3022BA159C5D9E3BE")
            throw new InvalidDataException("The published compiler resource has an unexpected SHA-256.");
        var checks = new List<object>();
        foreach (var (tag, expected) in new[]
        {
            ("spy", "WindowSpyPage"), ("compile", "CompilePage"), ("docs", "DocumentationPage")
        })
        {
            NavigateTo("home");
            _homePage!.OpenDiagnosticTool(tag);
            await Task.Delay(180);
            if (PageHost.Content is not Page page || page.GetType().Name != expected || !page.IsLoaded
                || HomeNavButton.IsChecked != true)
                throw new InvalidOperationException($"Tool '{tag}' must open its loaded page inside Home; actual: {PageHost.Content?.GetType().Name}.");
            checks.Add(new { Tool = tag, Page = page.GetType().Name, Loaded = page.IsLoaded, HomeSelected = true });
            NavigateTo("home");
            await Task.Delay(80);
            if (PageHost.Content?.GetType().Name != "HomePage")
                throw new InvalidOperationException("Returning from a tool did not restore Home.");
        }
        var capture = await VerifyWindowSpyAsync();
        var compiler = await VerifyCompilerAsync();
        var documents = await VerifyDocumentationAsync();
        NavigateTo("home");
        return new { Passed = true, CompilerResourceSha256 = compilerHash, Navigation = checks, WindowSpy = capture, Compiler = compiler, Documentation = documents };
    }

    /// <summary>Waits for a real asynchronous UI/service condition with a bounded diagnostic timeout.</summary>
    private static async Task WaitForToolAsync(Func<bool> condition, string description, int milliseconds = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Tool verification timed out: {description}");
            await Task.Delay(40);
        }
    }
}
