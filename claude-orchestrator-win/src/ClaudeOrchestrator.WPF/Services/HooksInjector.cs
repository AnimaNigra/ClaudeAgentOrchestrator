using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeOrchestrator.WPF.Services;

public class HooksInjector(string agentId, string orchestratorUrl)
{
    private string? _settingsPath;
    private bool _createdByUs;

    public async Task InjectAsync(string cwd)
    {
        var claudeDir = Path.Combine(cwd, ".claude");
        _settingsPath = Path.Combine(claudeDir, "settings.json");
        _createdByUs = !File.Exists(_settingsPath);

        JsonObject json;
        if (!_createdByUs)
        {
            try
            {
                var text = await File.ReadAllTextAsync(_settingsPath);
                json = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch { json = new JsonObject(); _createdByUs = true; }
        }
        else json = new JsonObject();

        if (json["hooks"] is not JsonObject hooksObj)
        {
            hooksObj = new JsonObject();
            json["hooks"] = hooksObj;
        }

        var url = $"{orchestratorUrl}/api/agents/{agentId}";
        AppendHook(hooksObj, "Stop",         $"curl -s -X POST \"{url}/hook/stop\"");
        AppendHook(hooksObj, "Notification", $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/notification\"");
        AppendHook(hooksObj, "PreToolUse",   $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/pre-tool\"");

        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(_settingsPath,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task RemoveAsync()
    {
        if (_settingsPath is null || !File.Exists(_settingsPath)) return;
        try
        {
            var text = await File.ReadAllTextAsync(_settingsPath);
            var json = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            var fragment = $"/api/agents/{agentId}/";

            if (json["hooks"] is JsonObject hooksObj)
            {
                foreach (var key in hooksObj.Select(kv => kv.Key).ToList())
                {
                    if (hooksObj[key] is not JsonArray arr) continue;
                    var keep = arr.OfType<JsonObject>()
                        .Where(e => {
                            var hooks = e["hooks"] as JsonArray;
                            return hooks == null || !hooks.OfType<JsonObject>().Any(h =>
                                h["command"]?.GetValue<string>()?.Contains(fragment) == true);
                        })
                        .Select(e => JsonNode.Parse(e.ToJsonString()))
                        .ToArray();
                    if (keep.Length == 0) hooksObj.Remove(key);
                    else { var a = new JsonArray(); foreach (var i in keep) a.Add(i); hooksObj[key] = a; }
                }
                if (!hooksObj.Any()) json.Remove("hooks");
            }

            if (!json.Any() && _createdByUs)
            {
                File.Delete(_settingsPath);
                var dir = Path.GetDirectoryName(_settingsPath)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            else
                await File.WriteAllTextAsync(_settingsPath,
                    json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static void AppendHook(JsonObject hooksObj, string eventType, string command)
    {
        var entry = new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = command }
            }
        };
        if (hooksObj[eventType] is JsonArray arr) arr.Add(entry);
        else hooksObj[eventType] = new JsonArray { entry };
    }
}
