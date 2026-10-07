using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Verifies the native workflow UI with isolated files and captures rendered evidence at wide and narrow sizes.</summary>
    internal async Task<object> VerifyVisualCreationAsync(string reportPath)
    {
        NavigateTo("new"); await Task.Delay(120);
        var startup = _newScriptPage!.VerifyWorkflowLayout();
        await CaptureVisualAsync(Path.ChangeExtension(reportPath, "startup.png"));
        var startupAfterRender = _newScriptPage.VerifyWorkflowLayout();
        var shell = await VerifyPagesAsync();
        var scale = RootLayout.XamlRoot.RasterizationScale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(1600 * scale), (int)Math.Round(1000 * scale)));
        NavigateTo("new"); await Task.Delay(120);
        var wide = _newScriptPage!.VerifyWorkflowLayout();
        await CaptureVisualAsync(Path.ChangeExtension(reportPath, "wide.png"));
        var saved = await _newScriptPage.VerifyWorkflowAsync();
        NavigateTo("scripts"); await Task.Delay(80);
        if (_services.Catalog.Snapshot().All(entry => entry.Path != saved)) throw new InvalidOperationException("Created workflow did not reach the existing script catalog.");
        await OpenVisualScriptAsync(saved); await Task.Delay(80);
        var form = await _newScriptPage.VerifyRetainedWorkflowAsync();
        var review = await _newScriptPage.VerifyReviewBoundariesAsync(() => { NavigateTo("scripts"); NavigateTo("new"); });
        var capabilities = await _newScriptPage.VerifyCapabilitiesAsync();
        await Task.Delay(100); var practicalWide = _newScriptPage.VerifyWorkflowLayout();
        await CaptureVisualAsync(Path.ChangeExtension(reportPath, "practical-wide.png"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(1100 * scale), (int)Math.Round(900 * scale)));
        await Task.Delay(120);
        var medium = _newScriptPage.VerifyWorkflowLayout();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(MinimumWindowWidth, MinimumWindowHeight));
        await Task.Delay(120);
        var narrow = _newScriptPage.VerifyWorkflowLayout();
        await CaptureVisualAsync(Path.ChangeExtension(reportPath, "narrow.png"));
        return new { Passed = true, Shell = shell, Form = form, Review = review, Capabilities = capabilities, PracticalWide = practicalWide, Startup = startup, StartupAfterRender = startupAfterRender, Wide = wide, Medium = medium, Narrow = narrow,
            Capture = "WinUI RenderTargetBitmap with opaque theme background; Mica and native caption are excluded",
            DragCompletionBoundary = true, PhysicalMouseDrag = "unvalidated", StateDirectory = _services.StateDirectory };
    }

    /// <summary>Captures actual WinUI-rendered pixels for the explicit diagnostic command, without desktop input automation.</summary>
    private async Task CaptureVisualAsync(string path)
    {
        var previous = RootLayout.Background;
        var bitmap = new RenderTargetBitmap();
        try
        {
            // Mica is outside the XAML bitmap; use the semantic theme base to avoid transparent pixels in evidence.
            RootLayout.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            await bitmap.RenderAsync(RootLayout);
        }
        finally { RootLayout.Background = previous; }
        var pixels = await bitmap.GetPixelsAsync();
        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        var bytes = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync(); memory.Seek(0);
        using var read = new DataReader(memory.GetInputStreamAt(0));
        await read.LoadAsync((uint)memory.Size); var png = new byte[memory.Size]; read.ReadBytes(png);
        File.WriteAllBytes(path, png);
    }
}
