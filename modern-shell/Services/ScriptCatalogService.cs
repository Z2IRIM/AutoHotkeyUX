using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns the sole script scan and a debounced watcher for the lifetime of the application.</summary>
internal sealed class ScriptCatalogService : IDisposable
{
    private readonly object _gate = new();
    private readonly Timer _debounce;
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<ScriptEntry> _entries = [];
    private bool _disposed;
    internal string RootDirectory { get; }
    internal string? LastError { get; private set; }
    internal event EventHandler? Changed;

    /// <summary>Uses the real Windows Documents known folder, including redirected Documents locations.</summary>
    internal ScriptCatalogService(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AutoHotkey"));
        _debounce = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
        Refresh();
    }

    /// <summary>Returns an immutable snapshot shared by Home and Scripts.</summary>
    internal IReadOnlyList<ScriptEntry> Snapshot()
    {
        lock (_gate) return _entries;
    }

    /// <summary>Scans only metadata and rebuilds the watch root after directory disappearance or watcher errors.</summary>
    internal void Refresh()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var entries = new List<ScriptEntry>();
            LastError = null;
            try
            {
                EnsureWatcher();
                if (Directory.Exists(RootDirectory))
                {
                    foreach (var path in Directory.EnumerateFiles(RootDirectory, "*.ahk", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var file = new FileInfo(path);
                            if (file.Exists) entries.Add(new ScriptEntry(file.Name, file.FullName, file.LastWriteTimeUtc, file.Length));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { ServiceDiagnostics.Write("Catalog", $"Could not read metadata: {path}", ex); }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LastError = ex.Message;
                ServiceDiagnostics.Write("Catalog", "Refresh failed.", ex);
            }
            _entries = entries.OrderByDescending(entry => entry.LastModifiedUtc)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Watches the workspace or its nearest existing ancestor so a missing root can appear later.</summary>
    private void EnsureWatcher()
    {
        var directory = RootDirectory;
        while (!Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory) ?? throw new DirectoryNotFoundException(RootDirectory);
        if (_watcher?.Path.Equals(directory, StringComparison.OrdinalIgnoreCase) == true) return;
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(directory)
        {
            Filter = "*",
            IncludeSubdirectories = !directory.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase),
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Coalesces relevant save bursts without refreshing for unrelated sibling files.</summary>
    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        if (IsRelevant(args.FullPath) || args is RenamedEventArgs rename && IsRelevant(rename.OldFullPath))
            ScheduleRefresh();
    }

    /// <summary>Recognizes root/ancestor directory changes as well as immediate .ahk children.</summary>
    private bool IsRelevant(string path) => path.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase)
        || RootDirectory.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || Path.GetDirectoryName(path)?.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase) == true
           && Path.GetExtension(path).Equals(".ahk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Marks an unreliable watcher for reconstruction and records the failed monitoring boundary.</summary>
    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            ServiceDiagnostics.Write("Catalog", "Watcher error; rebuilding on the next refresh.", args.GetException());
            _watcher?.Dispose();
            _watcher = null;
        }
        ScheduleRefresh();
    }

    /// <summary>Schedules one refresh 300 ms after the most recent filesystem change.</summary>
    private void ScheduleRefresh()
    {
        lock (_gate) { if (!_disposed) _debounce.Change(300, Timeout.Infinite); }
    }

    /// <summary>Stops monitoring at application shutdown without affecting script files.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _debounce.Dispose();
        }
    }
}
