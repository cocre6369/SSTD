using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using SSTD.Automation;
using SSTD.Core;
using SSTD.Security;
using SSTD.Vision;
using Xunit;

namespace SSTD.WindowsTests;
[Trait("Category","WindowsDesktop")]
public class GuardedBackendTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h,out uint pid);
    private static uint WindowPid(nint h,out uint pid)=>GetWindowThreadProcessId(h,out pid);
    private sealed class DenyApprovals : IApprovalBroker { public Task<bool> ApproveAsync(ApprovalRequest r,CancellationToken ct)=>Task.FromResult(false); }
    private sealed class Services : IActionServices
    {
        public Task<string> ComposeAsync(string instruction,CancellationToken ct)=>Task.FromResult("test");
        public Task WriteNewDocumentAsync(string path,string content,CancellationToken ct)=>Task.CompletedTask;
        public Task<string> DescribeApprovalAsync(AgentAction a,CancellationToken ct)=>Task.FromResult("test");
    }
    private static AgentAction A(string name,object args)=>new(name,JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.Clone()));
    [Fact]
    public async Task Real_Generic_UIA_Type_Click_Scroll_And_Find_With_Security()
    {
        var root=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(root,"SSTD.sln")))root=Path.GetDirectoryName(root)!;
        var exe=Path.Combine(root,"artifacts","test-fixture","SSTD.TestApp.exe");
        using var p=Process.Start(new ProcessStartInfo(exe,"--no-password"){UseShellExecute=true})!;
        try
        {
            var desktop=new WindowsDesktop(); var settings=new SstdSettings{DesktopControl=true,UiAutomation=true};
            WindowSnapshot? w=null; var sw=Stopwatch.StartNew();
            while(sw.Elapsed<TimeSpan.FromSeconds(20))
            {
                var handle=desktop.Handles().FirstOrDefault(h=>{WindowPid(h,out var pid);return pid==p.Id;});
                if(handle!=0){w=await desktop.DescribeAsyncForTest(handle);if(w.Elements.Count>3)break;}
                await Task.Delay(200);
            }
            Assert.NotNull(w);Assert.False(w!.IsPasswordContext);Assert.True(WindowsDesktop.Focus(w.Handle));
            w=await desktop.CaptureAsync(CancellationToken.None);
            var backend=new DesktopBackend(desktop,new InputController(desktop),null!,()=>settings,null!,new Services(),new WindowsVisualObserver());
            var security=new SecurityEngine(backend);
            backend=new DesktopBackend(desktop,new InputController(desktop),security,()=>settings,new WindowsVisualObserver(),new Services(),new WindowsVisualObserver());
            var executor=new ActionExecutor(security,new DenyApprovals(),backend,new Services());
            var editor=w.Elements.First(e=>e.AutomationId=="editor");
            var typed=await executor.ExecuteAsync(A("TypeText",new{text="SSTD guarded real input",elementId=editor.Id,append=false}),CancellationToken.None);
            Assert.True(typed.Success);Assert.True(typed.Verified);Assert.Contains("SSTD guarded real input",typed.Observation!.Elements.First(e=>e.AutomationId=="editor").Value);
            var button=typed.Observation.Elements.First(e=>e.Name=="Apply fixture action");
            var clicked=await executor.ExecuteAsync(A("ClickElement",new{elementId=button.Id}),CancellationToken.None);
            Assert.True(clicked.Success);Assert.Contains(clicked.Observation!.Elements,e=>e.Name=="Applied successfully");
            var found=await executor.ExecuteAsync(A("FindElement",new{name="Minecraft launcher",elementId=typed.Observation.Elements.First(e=>e.CanScroll&&e.AutomationId=="library").Id,maxScrolls=12}),CancellationToken.None);
            Assert.True(found.Success, found.Message);
            Assert.Contains(found.Observation!.Elements,e=>e.Name=="Minecraft launcher"&&!e.Offscreen);
        }
        finally {if(!p.HasExited)p.Kill();}
    }
}
internal static class TestDesktopExtensions
{
    public static Task<WindowSnapshot> DescribeAsyncForTest(this WindowsDesktop desktop,nint handle)=>Task.Run(()=>desktop.Describe(handle,true,CancellationToken.None));
}
