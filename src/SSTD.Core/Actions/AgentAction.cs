using System.Text.Json;

namespace SSTD.Core;

/// <summary>Closed vocabulary. There is deliberately no shell/script/evaluate action.</summary>
public sealed record AgentAction(string Name, IReadOnlyDictionary<string, JsonElement> Arguments)
{
    public string Str(string key, string fallback = "") =>
        Arguments.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    public int Int(string key, int fallback = 0) =>
        Arguments.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    public bool Bool(string key, bool fallback = false) => Arguments.TryGetValue(key, out var v) &&
        v.ValueKind is (JsonValueKind.True or JsonValueKind.False) ? v.GetBoolean() : fallback;
    public string Canonical() => Name + ":" + string.Join(",", Arguments.OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => x.Key + "=" + x.Value.GetRawText()));
}
public enum ActionParseStatus { Valid, UnknownAction, InvalidParameters, InvalidJson }
public sealed record ActionParseResult(ActionParseStatus Status, AgentAction? Action, string Error);

public static class ActionContract
{
    private sealed record Spec(HashSet<string> Required, HashSet<string> Allowed, Permission Permission);
    private static readonly HashSet<string> Empty = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Spec> Specs = new(StringComparer.Ordinal)
    {
        ["OpenApplication"] = new(["name"], ["name"], Permission.DesktopInteraction),
        ["OpenDocument"] = new(["path"], ["path"], Permission.DesktopInteraction),
        ["FocusWindow"] = new(["windowId"], ["windowId"], Permission.DesktopInteraction),
        ["SwitchWindow"] = new(["windowId"], ["windowId"], Permission.DesktopInteraction),
        ["CloseApplication"] = new(["windowId"], ["windowId"], Permission.DesktopInteraction),
        ["ClickElement"] = new(["elementId"], ["elementId"], Permission.DesktopInteraction),
        ["TypeText"] = new(["text"], ["text", "elementId", "append"], Permission.DesktopInteraction),
        ["PasteText"] = new(["text"], ["text", "elementId"], Permission.DesktopInteraction),
        ["PressKey"] = new(["key"], ["key"], Permission.DesktopInteraction),
        ["UseShortcut"] = new(["keys"], ["keys"], Permission.DesktopInteraction),
        ["Scroll"] = new(["direction"], ["direction", "elementId", "amount"], Permission.DesktopInteraction),
        ["FindElement"] = new(["name"], ["name", "elementId", "scroll", "maxScrolls"], Permission.ReadOnly),
        ["ReadText"] = new(Empty, ["elementId"], Permission.ReadOnly),
        ["SelectText"] = new(Empty, ["elementId", "start", "length"], Permission.DesktopInteraction),
        ["CopyText"] = new(Empty, ["elementId"], Permission.DesktopInteraction),
        ["WaitForElement"] = new(["name"], ["name", "timeoutSeconds"], Permission.ReadOnly),
        ["WaitForApplication"] = new(["name"], ["name", "timeoutSeconds"], Permission.ReadOnly),
        ["InspectWindow"] = new(Empty, Empty, Permission.ReadOnly),
        ["InspectUIHierarchy"] = new(Empty, Empty, Permission.ReadOnly),
        ["CaptureRelevantScreenRegion"] = new(Empty, ["elementId"], Permission.ReadOnly),
        ["Navigate"] = new(["url"], ["url"], Permission.DesktopInteraction),
        ["ComposeText"] = new(["instruction"], ["instruction"], Permission.ReadOnly),
        ["CreateDocument"] = new(["path", "content"], ["path", "content"], Permission.NewFile),
    };
    public static IReadOnlyCollection<string> Names => Specs.Keys;
    public static Permission Classification(string name) => Specs.TryGetValue(name, out var s) ? s.Permission : Permission.ExternalCommit;
    public static ActionParseResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            return Parse(doc.RootElement);
        }
        catch (JsonException) { return new(ActionParseStatus.InvalidJson, null, "The model did not return a valid action object."); }
    }
    public static ActionParseResult Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var nameEl) ||
            nameEl.ValueKind != JsonValueKind.String || root.TryGetProperty("command", out _))
            return new(ActionParseStatus.InvalidJson, null, "Expected an object with a registered action name.");
        var name = nameEl.GetString()!;
        if (!Specs.TryGetValue(name, out var spec)) return new(ActionParseStatus.UnknownAction, null, $"Unregistered action: {name[..Math.Min(60, name.Length)]}");
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("arguments", out var a))
        {
            if (a.ValueKind != JsonValueKind.Object) return new(ActionParseStatus.InvalidParameters, null, "arguments must be an object.");
            foreach (var p in a.EnumerateObject())
            {
                if (!spec.Allowed.Contains(p.Name)) return new(ActionParseStatus.InvalidParameters, null, $"Unknown argument {p.Name} for {name}.");
                args[p.Name] = p.Value.Clone();
            }
        }
        if (spec.Required.Any(k => !args.ContainsKey(k))) return new(ActionParseStatus.InvalidParameters, null, $"Missing required argument for {name}.");
        var action = new AgentAction(name, args);
        var error = Validate(action);
        return error.Length == 0 ? new(ActionParseStatus.Valid, action, "") : new(ActionParseStatus.InvalidParameters, null, error);
    }
    private static string Validate(AgentAction a)
    {
        foreach (var (k, v) in a.Arguments)
        {
            if (v.ValueKind is JsonValueKind.Object or JsonValueKind.Array && k != "keys") return $"{k} has an unsupported value type.";
            if (v.ValueKind == JsonValueKind.String && v.GetString()!.Length > (k is "content" or "text" ? 32000 : 2048)) return $"{k} is too long.";
        }
        string Required(string k) => a.Str(k).Trim().Length == 0 ? $"{k} must be a nonempty string.\n" : "";
        string Integer(string k, int min, int max, bool optional = false) =>
            (optional && !a.Arguments.ContainsKey(k)) || a.Int(k, int.MinValue) is var n && n >= min && n <= max ? "" : $"{k} must be an integer from {min} to {max}.\n";
        string err = "";
        foreach (var k in Specs[a.Name].Required) if (Specs[a.Name].Allowed.Contains(k) && a.Arguments.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String) err += Required(k);
        switch (a.Name)
        {
            case "TypeText": case "PasteText":
                if (!a.Arguments.TryGetValue("text", out var text) || text.ValueKind != JsonValueKind.String) return "text must be a string.";
                if (a.Str("text").Length == 0) return "text cannot be empty.";
                if (a.Arguments.ContainsKey("append") && a.Arguments["append"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "append must be boolean.";
                break;
            case "FocusWindow": case "SwitchWindow": case "CloseApplication":
                if (!long.TryParse(a.Str("windowId"), out var h) || h <= 0) return "windowId must be a currently observed positive handle.";
                break;
            case "Scroll":
                if (a.Str("direction") is not ("up" or "down" or "left" or "right")) return "direction must be up, down, left, or right.";
                err += Integer("amount", 1, 5, true); break;
            case "FindElement":
                err += Integer("maxScrolls", 0, 12, true);
                if (a.Arguments.ContainsKey("scroll") && a.Arguments["scroll"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "scroll must be boolean.";
                break;
            case "SelectText": err += Integer("start", 0, 32000, true) + Integer("length", 0, 32000, true); break;
            case "WaitForElement": case "WaitForApplication": err += Integer("timeoutSeconds", 1, 30, true); break;
            case "UseShortcut":
                if (!a.Arguments.TryGetValue("keys", out var keys) || keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() is < 2 or > 4 ||
                    keys.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Length > 16)) return "keys must contain 2–4 key names.";
                break;
            case "PressKey": if (a.Str("key").Length > 16) return "Invalid key name."; break;
            case "CreateDocument": if (a.Str("content").Length == 0) return "Document content cannot be empty."; break;
        }
        if (a.Arguments.ContainsKey("elementId") && a.Str("elementId").Length == 0) err += "elementId cannot be empty.";
        return err.Trim();
    }
}
