using System.Security.Principal;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Uses per-user named Windows handles to wake a silent manager without starting another script owner.</summary>
internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _enable;
    private readonly EventWaitHandle _exit;
    private readonly List<RegisteredWaitHandle> _waits = [];
    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private Action<string>? _handler;
    internal bool IsPrimary { get; }

    /// <summary>Claims manager ownership in this Windows session and retains signals until WinUI is ready.</summary>
    internal SingleInstanceService()
    {
        var key = @"Local\AutoHotkeyUX.Modern." + WindowsIdentity.GetCurrent().User!.Value;
        _mutex = new Mutex(true, key, out var created);
        if (created) IsPrimary = true;
        else
        {
            try { IsPrimary = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsPrimary = true; }
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".Show");
        _enable = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".EnableCore");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".Exit");
        if (IsPrimary)
        {
            Watch(_show, "show");
            Watch(_enable, "enable-core");
            Watch(_exit, "exit");
        }
    }

    /// <summary>Sends only the explicit command to the existing manager; background duplicate launches remain quiet.</summary>
    internal void Redirect(string[] arguments)
    {
        if (arguments.Contains("--exit-manager")) _exit.Set();
        else
        {
            if (arguments.Contains("--enable-core")) _enable.Set();
            if (!arguments.Contains("--background")) _show.Set();
        }
    }

    /// <summary>Connects the application dispatcher and drains any activation that arrived during initialization.</summary>
    internal void SetHandler(Action<string> handler)
    {
        lock (_gate)
        {
            _handler = handler;
            foreach (var command in _pending) handler(command);
            _pending.Clear();
        }
    }

    /// <summary>Queues handle signals on a worker thread rather than blocking the WinUI message loop.</summary>
    private void Watch(EventWaitHandle handle, string command)
    {
        _waits.Add(ThreadPool.RegisterWaitForSingleObject(handle, (_, _) =>
        {
            lock (_gate) { if (_handler is null) _pending.Add(command); else _handler(command); }
        }, null, Timeout.Infinite, executeOnlyOnce: false));
    }

    /// <summary>Releases the mutex on its owning main thread and removes all activation registrations.</summary>
    public void Dispose()
    {
        foreach (var wait in _waits) wait.Unregister(null);
        _show.Dispose(); _enable.Dispose(); _exit.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
