namespace SSTD.Core;
public static class PlannerSchema
{
    public const string Value = """
    {"type":"object","additionalProperties":false,"required":["reply","action","completed"],"properties":{"reply":{"type":"string"},"completed":{"type":"boolean"},"action":{"anyOf":[{"type":"null"},{"type":"object","additionalProperties":false,"required":["name","arguments"],"properties":{"name":{"type":"string","enum":["OpenApplication","OpenDocument","FocusWindow","SwitchWindow","CloseApplication","ClickElement","TypeText","PasteText","PressKey","UseShortcut","Scroll","FindElement","ReadText","SelectText","CopyText","WaitForElement","WaitForApplication","InspectWindow","InspectUIHierarchy","CaptureRelevantScreenRegion","Navigate","ComposeText","CreateDocument"]},"arguments":{"type":"object"}}}]}}}
    """;
}
