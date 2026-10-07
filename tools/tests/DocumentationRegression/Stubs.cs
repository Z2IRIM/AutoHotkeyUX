namespace AutoHotkeyUX.Modern.Services;

internal sealed record DiagnosticRuntime(string Path, string Version);
internal sealed class AutoHotkeyIntegration(string runtimePath)
{
    // Supplies an isolated source file while exercising the real documentation service.
    internal DiagnosticRuntime FindRuntime() => new(runtimePath, "shutdown-fixture");
}
internal static class ServiceDiagnostics
{
    // Keeps this isolated regression runner's log away from user application state.
    internal static void Write(string area, string message, Exception? error = null) => Console.WriteLine($"{area}: {message} {error?.GetType().Name}");
}
