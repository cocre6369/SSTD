using System.Text.Json;
using SSTD.Core;
using SSTD.Planner;
using SSTD.Security;
using Xunit;

namespace SSTD.Tests;
internal sealed class StubExecutor : IActionExecutor
{
    public int Calls; public List<string> Actions = [];
    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken ct) { Calls++; Actions.Add(action.Name); ct.ThrowIfCancellationRequested(); return Task.FromResult(new ActionResult(true,true,"Verified test result")); }
}
internal sealed class ScriptedPlanner(params AgentReply[] replies) : IAgentPlanner
{
    public int Calls;
    public Task<AgentReply> NextAsync(string request, IReadOnlyList<ModelMessage> conversation, WindowSnapshot? observation, string? lastResult, IReadOnlyList<string> previousActions, Action<string> onToken, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(replies[Math.Min(Calls++,replies.Length-1)]); }
}
internal sealed class MemorySettings(SstdSettings settings) : ISettingsStore { public Task<SstdSettings> LoadAsync(CancellationToken ct)=>Task.FromResult(settings); public Task SaveAsync(SstdSettings s,CancellationToken ct)=>Task.CompletedTask; }
internal sealed class NullTasks : ITaskStore
{ public Task CreateAsync(TaskRecord r,CancellationToken ct)=>Task.CompletedTask; public Task EventAsync(string id,TaskEvent e,CancellationToken ct)=>Task.CompletedTask; public Task FinishAsync(string id,string s,CancellationToken ct)=>Task.CompletedTask; public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<TaskRecord>>([]); public Task<TaskDetail?> GetAsync(string id,CancellationToken ct)=>Task.FromResult<TaskDetail?>(null); public Task ClearAsync(CancellationToken ct)=>Task.CompletedTask; }
internal sealed class NullConversations : IConversationStore { public Task AppendAsync(ModelMessage m,CancellationToken ct)=>Task.CompletedTask; public Task<IReadOnlyList<ModelMessage>> RecentAsync(int n,CancellationToken ct)=>Task.FromResult<IReadOnlyList<ModelMessage>>([]); public Task ClearAsync(CancellationToken ct)=>Task.CompletedTask; }
internal sealed class NullDesktop : IWindowsSnapshotProvider { public Task<WindowSnapshot> CaptureAsync(CancellationToken ct)=>Task.FromResult(new WindowSnapshot()); public Task<WindowSnapshot?> FindWindowAsync(string name,CancellationToken ct)=>Task.FromResult<WindowSnapshot?>(null); }
public class RunnerTests
{
    private static AgentRunner Runner(IAgentPlanner planner,IActionExecutor executor,SstdSettings? settings=null) { var s=settings??new SstdSettings(); return new(planner,executor,new NullDesktop(),new MemorySettings(s),new NullTasks(),new NullConversations(),()=>s); }
    [Fact] public async Task Ordinary_Conversation_Needs_No_Automation()
    { var executor=new StubExecutor(); var result=await Runner(new ScriptedPlanner(new("Paris.",null,true)),executor).RunAsync("Capital of France?",default); Assert.True(result.Success); Assert.Equal(0,executor.Calls); }
    [Fact] public async Task Executes_One_Action_Per_Reasoning_Step()
    {
        var a=new AgentAction("InspectWindow",new Dictionary<string,JsonElement>());
        var executor=new StubExecutor(); var result=await Runner(new ScriptedPlanner(new("Inspecting",a,false),new("Done",null,true)),executor).RunAsync("Inspect",default);
        Assert.True(result.Success); Assert.Equal(1,executor.Calls);
    }
    [Fact] public async Task Cancellation_Prevents_Next_Action()
    {
        using var cts=new CancellationTokenSource(); cts.Cancel(); var executor=new StubExecutor();
        var result=await Runner(new ScriptedPlanner(new("test",null,true)),executor).RunAsync("Stop",cts.Token); Assert.False(result.Success); Assert.Equal(0,executor.Calls);
    }
    [Fact] public async Task Step_Limit_Does_Not_Report_Success()
    {
        var a=new AgentAction("InspectWindow",new Dictionary<string,JsonElement>());
        var result=await Runner(new ScriptedPlanner(new("Again",a,false)),new StubExecutor(),new SstdSettings{MaxSteps=2}).RunAsync("Never finish",default); Assert.False(result.Success);
    }
    [Fact] public async Task Secret_Request_Never_Reaches_Model_Or_Executor()
    { var planner=new ScriptedPlanner(new("bad",null,true)); var result=await Runner(planner,new StubExecutor()).RunAsync("password=SuperSecret123!",default); Assert.False(result.Success); Assert.Equal(0,((ScriptedPlanner)planner).Calls); }
}
