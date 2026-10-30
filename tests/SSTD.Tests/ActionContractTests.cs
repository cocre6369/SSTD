using System.Text.Json;
using SSTD.Core;
using Xunit;

namespace SSTD.Tests;
public class ActionContractTests
{
    [Theory]
    [InlineData("{\"name\":\"Execute\",\"arguments\":{\"command\":\"cmd\"}}")]
    [InlineData("{\"name\":\"OpenApplication\",\"arguments\":{\"name\":\"Notepad\",\"command\":\"dir\"}}")]
    [InlineData("{\"name\":\"OpenApplication\",\"arguments\":{\"name\":\"Notepad\",\"runAsAdmin\":true}}")]
    [InlineData("{\"name\":\"TypeText\",\"arguments\":{\"text\":42}}")]
    [InlineData("{\"name\":\"PressKey\",\"arguments\":{}}")]
    [InlineData("{\"name\":\"Scroll\",\"arguments\":{\"direction\":\"diagonal\"}}")]
    [InlineData("{\"name\":\"UseShortcut\",\"arguments\":{\"keys\":\"ctrl+c\"}}")]
    [InlineData("{\"name\":\"SelectText\",\"arguments\":{\"start\":-1}}")]
    [InlineData("{\"name\":\"FindElement\",\"arguments\":{\"name\":\"x\",\"maxScrolls\":1000}}")]
    [InlineData("not json")]
    public void Rejects_Invalid_Or_Unregistered_Actions(string json) => Assert.NotEqual(ActionParseStatus.Valid, ActionContract.Parse(json).Status);
    [Fact]
    public void Valid_Action_Preserves_Exact_Arguments()
    {
        var result = ActionContract.Parse("""{"name":"TypeText","arguments":{"text":"hello\nworld","elementId":"e7"}}""");
        Assert.Equal(ActionParseStatus.Valid, result.Status); Assert.Equal("hello\nworld", result.Action!.Str("text")); Assert.Equal("e7", result.Action.Str("elementId"));
    }
    [Fact] public void Registry_Has_No_Arbitrary_Code_Action() => Assert.DoesNotContain(ActionContract.Names, n => n.Contains("shell", StringComparison.OrdinalIgnoreCase) || n.Contains("command", StringComparison.OrdinalIgnoreCase) || n.Contains("script", StringComparison.OrdinalIgnoreCase));
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    public void Settings_Accept_Local_Endpoint(string url) { var s = new SstdSettings { Endpoint = url }; Assert.Equal("", s.Validate()); }
    [Theory]
    [InlineData("https://cloud.example.com")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://user:password@localhost:11434")]
    [InlineData("http://localhost:11434/api")]
    [InlineData("http://localhost:11434/?token=x")]
    [InlineData("file:///c:/windows")]
    public void Settings_Reject_Nonlocal_Or_Unsafe_Endpoint(string url) => Assert.NotEqual("", new SstdSettings { Endpoint = url }.Validate());
    [Fact] public void Settings_Validate_Bounds()
    {
        var s = new SstdSettings { Temperature = double.NaN }; Assert.NotEqual("", s.Validate());
        s = new SstdSettings { MaxSteps = 101 }; Assert.NotEqual("", s.Validate());
        s = new SstdSettings { DesktopControl = true, UiAutomation = false, VisualRecognition = false }; Assert.NotEqual("", s.Validate());
    }
}
