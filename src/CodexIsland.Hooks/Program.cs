using System.Text.Json;
using CodexIsland.Core.Signals;

try
{
    var eventName = args.FirstOrDefault();
    string? stdin = null;

    if (Console.IsInputRedirected)
    {
        stdin = Console.In.ReadToEnd();
    }

    eventName = ExtractEventName(stdin) ?? eventName ?? "ManualSet";
    var signal = ProjectSignalMapper.FromEvent(eventName);
    var statusFile = LocalProjectSignalService.ResolveStatusFile();
    Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);

    var payload = new
    {
        schema_version = 1,
        project_id = "codex",
        display_name = "Codex",
        signal = signal.ToString().ToLowerInvariant(),
        last_event = eventName,
        updated_at = DateTimeOffset.Now.ToString("O")
    };

    File.WriteAllText(statusFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
}
catch
{
    // Hooks must fail open so Codex is never blocked by the widget.
}

return 0;

static string? ExtractEventName(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return TryGetString(root, "hook_event_name") ??
               TryGetString(root, "hookEventName") ??
               TryGetString(root, "event") ??
               TryGetString(root, "type");
    }
    catch
    {
        return null;
    }
}

static string? TryGetString(JsonElement element, string name)
{
    return element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
}
