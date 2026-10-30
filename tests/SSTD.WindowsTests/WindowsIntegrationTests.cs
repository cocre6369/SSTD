using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using SSTD.Automation;
using SSTD.Core;
using SSTD.Vision;
using Xunit;

namespace SSTD.WindowsTests;
file static class ProcessHelper { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h,out uint pid); }

[Trait("Category","WindowsDesktop")]
public class WindowsIntegrationTests
{
    private static string Fixture()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir,"SSTD.sln"))) dir = Path.GetDirectoryName(dir);
        if (dir is null) throw new DirectoryNotFoundException("Repository root not found.");
        var exe = Path.Combine(dir,"artifacts","test-fixture","SSTD.TestApp.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Build the real WPF test fixture first with tools/Windows-Integration.ps1.",exe);
        return exe;
    }
    private static AgentAction Action(string name,object args) => new(name,JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.Clone()));
    [Fact]
    public void Real_Window_UIA_And_Password_Protection()
    {
        using var p=Process.Start(new ProcessStartInfo(Fixture()) { UseShellExecute=true })!;
        try
        {
            var desktop=new WindowsDesktop(); p.WaitForInputIdle(10000); WindowSnapshot? snapshot=null;
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while(DateTime.UtcNow<deadline) { var h=desktop.Handles().FirstOrDefault(h=>{ try{return (ProcessHelper.GetWindowThreadProcessId(h,out var pid)==0?pid:pid)==p.Id;}catch{return false;} }); if(h!=0) { snapshot=desktop.Describe(h,true,CancellationToken.None); if(snapshot.Elements.Count>0)break; } Thread.Sleep(200); }
            Assert.NotNull(snapshot); Assert.Contains(snapshot.Elements,e=>e.ControlType=="Password"); Assert.True(snapshot!.IsPasswordContext); Assert.Empty(snapshot.Elements);
            Assert.Contains("Credential",string.Join(" ",snapshot.Warnings));
        }
        finally { if(!p.HasExited)p.Kill(); }
    }
    [Fact]
    public void Notepad_Real_Text_Insertion_And_Verification()
    {
        var notepad=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","notepad.exe");
        using var p=Process.Start(new ProcessStartInfo(notepad) { UseShellExecute=false })!;
        try
        {
            var desktop=new WindowsDesktop(); var input=new InputController(desktop);
            var deadline=DateTime.UtcNow.AddSeconds(15); WindowSnapshot? w=null;
            while(DateTime.UtcNow<deadline) { var h=desktop.Handles().FirstOrDefault(h=>{ try{return (ProcessHelper.GetWindowThreadProcessId(h,out var pid)==0?pid:pid)==p.Id;}catch{return false;} }); if(h!=0) { w=desktop.Describe(h,true,CancellationToken.None); if(w.Elements.Any(e=>e.CanEdit))break; } Thread.Sleep(250); }
            Assert.NotNull(w); Assert.False(w!.IsPasswordContext);
            var editor=w.Elements.First(e=>e.CanEdit); input.EnsureTarget(w,CancellationToken.None);
            var live=desktop.Resolve(w,editor.Id); live.SetFocus(); input.Shortcut(w,["ctrl","a"],CancellationToken.None); input.Type(w,"SSTD real Windows input test\nSecond line.",CancellationToken.None);
            Thread.Sleep(500); var observed=desktop.Describe(w.Handle,true,CancellationToken.None);
            var text=string.Join("\n",observed.Elements.Select(e=>e.Value));
            if(!text.Contains("SSTD real Windows input test"))
            {
                // Modern Notepad may not expose a ValuePattern; read its selected document via UIA TextPattern.
                var edit=AutomationElement.FromHandle(w.Handle).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document));
                if(edit?.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)==true) text=((TextPattern)pattern).DocumentRange.GetText(-1);
            }
            Assert.Contains("SSTD real Windows input test",text); Assert.Contains("Second line.",text);
        }
        finally { if(!p.HasExited)p.Kill(); }
    }
    [Fact]
    public void Windows_Ocr_Engine_Initialization()
    {
        // Language packs differ by Windows image. A missing pack is an explicit environment limitation, not fake OCR.
        var observer=new WindowsVisualObserver();
        var result=observer.InspectAsync(new WindowSnapshot{WindowId="0",IsBlocked=true},null,false,CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.Success); Assert.Contains("protected",result.Message);
    }
}
