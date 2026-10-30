using SSTD.Core;

namespace SSTD.Planner;

/// <summary>One model decision per fresh observation. Neither the model nor UI can execute an unregistered action.</summary>
public sealed class AgentRunner(IAgentPlanner planner, IActionExecutor executor, IWindowsSnapshotProvider desktop,
    ISettingsStore settingsStore, ITaskStore tasks, IConversationStore conversations, Func<SstdSettings> settings)
{
    public event Action<TaskEvent>? EventRaised;
    public event Action<string>? TokenRaised;
    public event Action<string>? ReplyCompleted;
    public async Task<AgentRunResult> RunAsync(string request, CancellationToken ct)
    {
        request = request.Trim();
        if (request.Length is 0 or > 8000) throw new ArgumentException("Enter a request between 1 and 8000 characters.");
        if (SensitiveData.ContainsSecret(request)) return new(false, false, "This request appears to contain credentials or a secret. Remove it and enter secrets manually in the destination application. Nothing was sent to Ollama or stored.", 0);
        var s = settings(); var id = Guid.NewGuid().ToString("N"); _currentTask.Value = id; var now = DateTimeOffset.UtcNow;
        var conversation = s.ConversationHistory ? await conversations.RecentAsync(12, ct).ConfigureAwait(false) : [];
        var messages = new List<ModelMessage>(conversation);
        await tasks.CreateAsync(new(id, request, "Running", now, now), ct).ConfigureAwait(false);
        await conversations.AppendAsync(new("user", request), ct).ConfigureAwait(false);
        Emit("Task", "Request accepted by the local action engine.");
        var history = new List<string>(); var failures = 0; var invalid = 0; var verifiedActions = 0; var performedActions = 0;
        WindowSnapshot? observation = null; string? lastResult = null; string final = ""; var successful = false;
        try
        {
            if (s.DesktopControl)
            {
                Emit("Observe", "Inspecting the current application before planning.");
                observation = await desktop.CaptureAsync(ct).ConfigureAwait(false);
                if (observation.IsBlocked || observation.IsPasswordContext) Emit("Observe", "Current window is protected; desktop actions remain subject to policy.", false);
            }
            for (var step = 0; step < s.MaxSteps; step++)
            {
                ct.ThrowIfCancellationRequested(); Emit("Reason", $"Requesting one structured next step from Ollama ({step + 1}/{s.MaxSteps}).");
                AgentReply reply;
                try
                {
                    var streamed = "";
                    reply = await planner.NextAsync(request, messages, observation, lastResult, history, token => { streamed += token; TokenRaised?.Invoke(token); }, ct).ConfigureAwait(false);
                    if (reply.Message.Length > 0) ReplyCompleted?.Invoke(reply.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && (ex is InvalidOperationException || ex.GetType().Name == "OllamaException"))
                {
                    if (++invalid >= 2) throw;
                    lastResult = "INVALID MODEL OUTPUT: " + SensitiveData.SafeError(ex) + ". Return the required JSON schema with one registered action or null.";
                    Emit("Validate", "Malformed model output rejected; requesting a corrected structured response.", false); continue;
                }
                if (reply.Action is null)
                {
                    final = reply.Message;
                    successful = reply.Completed;
                    // A conversational answer is not an automation verification.
                    Emit("Finish", reply.Completed ? "Assistant reported the task concluded." : "Assistant needs clarification; no further actions were taken.", reply.Completed);
                    break;
                }
                var action = reply.Action;
                var parsed = ActionContract.Parse(System.Text.Json.JsonSerializer.Serialize(new { name = action.Name, arguments = action.Arguments }));
                if (parsed.Status != ActionParseStatus.Valid) { lastResult = "Action rejected: " + parsed.Error; Emit("Validate", parsed.Error, false); if (++invalid >= 3) break; continue; }
                var signature = action.Canonical();
                if (history.TakeLast(4).Count(x => x == signature) >= 2)
                { final = "Stopped a repeated action instead of clicking blindly. Please refine the request or inspect the target application."; Emit("Recover", final, false); break; }
                history.Add(signature);
                Emit("Action", action.Name + " requested; validating against the independent safety policy.");
                var result = await executor.ExecuteAsync(action, ct).ConfigureAwait(false);
                performedActions++;
                if (result.Success && result.Verified) verifiedActions++;
                Emit(result.Success ? "Verify" : "Rejected", action.Name + ": " + result.Message, result.Success);
                if (!result.Success)
                {
                    failures++;
                    if (result.Message.Contains("Safety policy", StringComparison.Ordinal) || result.Message.Contains("cancelled the approval", StringComparison.Ordinal))
                    { final = result.Message; break; }
                    if (failures >= 3) { final = "Stopped after repeated failures. " + result.Message; break; }
                }
                else failures = 0;
                lastResult = System.Text.Json.JsonSerializer.Serialize(new { action = action.Name, success = result.Success, verified = result.Verified, result = result.Message });
                if (result.Observation is not null) observation = result.Observation;
                else if (s.DesktopControl) observation = await desktop.CaptureAsync(ct).ConfigureAwait(false);
                if (step == s.MaxSteps - 1) { final = "The task step limit was reached before completion. Review the activity log; some steps may have been performed."; Emit("Limit", final, false); }
            }
            if (final.Length == 0) final = successful ? "Task concluded. See verified steps in the activity log." : "The task did not reach a verified completion.";
            if (successful && performedActions > 0 && verifiedActions == 0) { successful = false; final += " No desktop action could be verified, so success is not claimed."; }
            var status = successful ? (performedActions == 0 || verifiedActions > 0 ? "Completed" : "Unverified") : ct.IsCancellationRequested ? "Stopped" : "Incomplete";
            await tasks.FinishAsync(id, status, CancellationToken.None).ConfigureAwait(false);
            await conversations.AppendAsync(new("assistant", final), CancellationToken.None).ConfigureAwait(false);
            return new(successful, verifiedActions > 0, final, performedActions);
        }
        catch (OperationCanceledException)
        {
            final = "Stopped. No further actions will be sent. An input or external change already accepted by Windows cannot be rolled back.";
            Emit("Stopped", final, false); await tasks.FinishAsync(id, "Stopped", CancellationToken.None).ConfigureAwait(false);
            return new(false, false, final, performedActions);
        }
        catch (Exception ex)
        {
            final = "Task stopped: " + SensitiveData.SafeError(ex); Emit("Error", final, false);
            await tasks.FinishAsync(id, "Error", CancellationToken.None).ConfigureAwait(false);
            return new(false, false, final, performedActions);
        }
    }
    private void Emit(string phase, string message, bool success = true)
    {
        var ev = new TaskEvent(DateTimeOffset.UtcNow, phase, SensitiveData.Redact(message), success); EventRaised?.Invoke(ev);
        // History is best effort; storage failure must not leave the STOP button disabled.
        _ = PersistAsync(ev);
    }
    private async Task PersistAsync(TaskEvent ev)
    {
        try
        {
            // Every run has a single logical task; ID is captured below via the async-local scope.
            if (_currentTask.Value is { } id) await tasks.EventAsync(id, ev, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { EventRaised?.Invoke(new(DateTimeOffset.UtcNow, "Storage", "History could not be saved: " + SensitiveData.SafeError(ex), false)); }
    }
    private readonly AsyncLocal<string> _currentTask = new();
}
