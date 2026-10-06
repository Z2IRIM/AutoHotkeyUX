using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Accepts bounded shortcut messages and serializes extraction on a worker without loading another manager.</summary>
internal sealed class ShortcutCommandService
{
    private const nuint Protocol = 0x41584B32;
    private const int MaximumPayloadBytes = 32768;
    private readonly IntPtr _window;
    private readonly AutoHotkeySettings _settings;
    private readonly Func<string, string> _extract;
    private readonly Action<string, bool> _notify;
    private readonly SubclassProcedure _procedure;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<(string Path, long Queued)> _queue = Channel.CreateBounded<(string, long)>(
        new BoundedChannelOptions(4) { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Task _worker;
    private bool _accepting = true;
    internal string Token { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Publishes only this process's native endpoint; an absent window supports isolated queue verification.</summary>
    internal ShortcutCommandService(IntPtr window, AutoHotkeySettings settings,
        Action<string, bool> notify, Func<string, string>? extract = null)
    {
        _window = window; _settings = settings; _notify = notify;
        _extract = extract ?? new ArchiveExtractionService().Extract;
        _procedure = WindowProcedure;
        if (window != IntPtr.Zero)
        {
            if (!SetWindowSubclass(window, _procedure, 2, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                _settings.Write("Modern", "ShortcutWindow", window.ToInt64().ToString());
                _settings.Write("Modern", "ShortcutPid", Environment.ProcessId.ToString());
                _settings.Write("Modern", "ShortcutToken", Token);
            }
            catch { RemoveWindowSubclass(window, _procedure, 2); throw; }
        }
        _worker = Task.Run(ProcessQueueAsync);
    }

    /// <summary>Copies WM_COPYDATA before returning and acknowledges only acceptance, duplicate or queue capacity.</summary>
    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data)
    {
        if (message != 0x4A) return DefSubclassProc(window, message, wParam, lParam);
        try
        {
            var incoming = Marshal.PtrToStructure<CopyData>(lParam);
            if (incoming.Tag != Protocol || incoming.Length is < 2 or > MaximumPayloadBytes || incoming.Data == IntPtr.Zero)
                return IntPtr.Zero;
            var bytes = new byte[incoming.Length];
            Marshal.Copy(incoming.Data, bytes, 0, bytes.Length);
            if (bytes[^1] != 0) return IntPtr.Zero;
            var body = new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - 1);
            return (IntPtr)TryEnqueue(body);
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        { return IntPtr.Zero; }
    }

    /// <summary>Validates the ephemeral protocol and absolute path without filesystem work on the window thread.</summary>
    internal int TryEnqueue(string body)
    {
        if (body.Length > MaximumPayloadBytes || body.Contains('\0')) return 0;
        var parts = body.Split('\n', 3);
        if (parts.Length != 3 || parts[0] != "extract" || parts[1] != Token) return 0;
        var path = parts[2];
        if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || !ArchiveExtractionService.SupportsPath(path)) return 0;
        try { path = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return 0; }
        lock (_gate)
        {
            if (!_accepting) return 0;
            if (_pending.Contains(path)) return 2;
            _pending.Add(path);
            if (_queue.Writer.TryWrite((path, Stopwatch.GetTimestamp()))) return 1;
            _pending.Remove(path);
            return 3;
        }
    }

    /// <summary>Isolates failed jobs, releases deduplication and records queue/extraction timings outside the UI thread.</summary>
    private async Task ProcessQueueAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                ServiceDiagnostics.Write("Shortcut", $"Extracting {job.Path}; queueMs={Stopwatch.GetElapsedTime(job.Queued).TotalMilliseconds:F0}");
                var destination = _extract(job.Path);
                ServiceDiagnostics.Write("Shortcut", $"Completed in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms: {destination}");
                Notify($"已解压到 {destination}", false);
            }
            catch (Exception ex)
            {
                ServiceDiagnostics.Write("Shortcut", $"Extraction failed: {job.Path}", ex);
                Notify($"解压失败：{ex.Message}", true);
            }
            finally { lock (_gate) _pending.Remove(job.Path); }
        }
    }

    /// <summary>Keeps a notification failure from faulting the extraction worker or losing later jobs.</summary>
    private void Notify(string message, bool error)
    {
        try { _notify(message, error); }
        catch (Exception ex) { ServiceDiagnostics.Write("Shortcut", "Could not report extraction result.", ex); }
    }

    /// <summary>Stops accepting requests on the owning thread and drains every accepted job before manager exit.</summary>
    internal Task DrainAsync()
    {
        lock (_gate)
        {
            if (!_accepting) return _worker;
            _accepting = false;
            _queue.Writer.TryComplete();
        }
        if (_window != IntPtr.Zero)
        {
            RemoveWindowSubclass(_window, _procedure, 2);
            try
            {
                if (_settings.Read("Modern", "ShortcutToken") == Token)
                {
                    _settings.Write("Modern", "ShortcutToken", "");
                    _settings.Write("Modern", "ShortcutWindow", "0");
                    _settings.Write("Modern", "ShortcutPid", "0");
                }
            }
            catch (Exception ex) { ServiceDiagnostics.Write("Shortcut", "Could not clear the stale shortcut endpoint.", ex); }
        }
        return _worker;
    }

    [StructLayout(LayoutKind.Sequential)] private struct CopyData { internal nuint Tag; internal int Length; internal IntPtr Data; }
    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
