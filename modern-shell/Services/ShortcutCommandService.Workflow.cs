using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Services;

internal sealed partial class ShortcutCommandService
{
    private readonly string _workflowResultRoot;
    private readonly Func<VisualExtractionRequest, string> _workflowExtract;
    private readonly Dictionary<Guid, (VisualExtractionRequest Request, DateTimeOffset? Completed)> _workflowRequests = new();
    private readonly Dictionary<Guid, int> _workflowStatuses = new();

    /// <summary>Deduplicates exact request identities and shares the existing extraction queue regardless of built-in enablement.</summary>
    private int EnqueueWorkflow(string body)
    {
        VisualExtractionRequest request;
        try { request = VisualFlowRequestCodec.Extraction(body); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException) { return 0; }
        lock (_gate)
        {
            if (!_accepting) return 0;
            if (_workflowRequests.TryGetValue(request.RequestId, out var prior)) return prior.Request == request ? 2 : 0;
            foreach (var expired in _workflowRequests.Where(pair => pair.Value.Completed < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(pair => pair.Key).ToArray())
            {
                _workflowRequests.Remove(expired);
                _workflowStatuses.Remove(expired);
                // Cleanup is deferred to the worker; WM_COPYDATA performs no filesystem operations.
            }
            if (_workflowRequests.Count >= 256) return 3;
            _workflowRequests.Add(request.RequestId, (request, null));
            if (_queue.Writer.TryWrite(new(request.Source, request.Destination, Stopwatch.GetTimestamp(), request))) { _workflowStatuses[request.RequestId] = 1; return 1; }
            _workflowRequests.Remove(request.RequestId);
            return 3;
        }
    }

    /// <summary>Returns accepted/completed/error/publication-failed status for an exact request identity.</summary>
    private int WorkflowStatus(string id)
    {
        if (!Guid.TryParse(id, out var identity)) return 0;
        lock (_gate) return _workflowStatuses.GetValueOrDefault(identity);
    }

    /// <summary>Records bounded invocation feedback without filesystem work or a second activity owner.</summary>
    private int RecordWorkflowEvent(string body)
    {
        VisualFlowEvent value;
        try { value = VisualFlowRequestCodec.Event(body); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException) { return 0; }
        lock (_gate)
        {
            if (!_accepting) return 0;
            _activity.Record(new(DateTimeOffset.Now, "Workflow", value.State != "failed", "", value.ElapsedMs, value.Detail,
                value.State == "succeeded" ? value.Detail : null, value.FlowId, value.RunId, value.StepId, value.StepName, value.State));
        }
        return 1;
    }

    /// <summary>Runs an accepted explicit job once and reports the exact committed output or failure.</summary>
    private void ProcessWorkflow(VisualExtractionRequest request, long started)
    {
        Exception? failure = null; string? output = null;
        try
        {
            VisualFlowReports.Prune(_workflowResultRoot);
            if (!VisualFlowReports.Begin(_workflowResultRoot, request)) return;
            output = _workflowExtract(request);
        }
        catch (Exception ex) { failure = ex; }
        var status = failure is null ? 2 : 4;
        try { VisualFlowReports.Complete(_workflowResultRoot, request.RequestId, output, failure); }
        catch (Exception reportError)
        { status = 5; ServiceDiagnostics.Write("VisualFlow", $"Request {request.RequestId}: result publication failed; accepted extraction was not repeated.", reportError); }
        lock (_gate) _workflowStatuses[request.RequestId] = status;
        ServiceDiagnostics.Write("VisualFlow", $"Request {request.RequestId}, run {request.RunId}, step {request.StepId}; elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}; output={output}", failure);
    }
}
