using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class AgentHistoryService
{
    private const int MaxSessionsPerGroup = 5;
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentHistoryService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "agents.json");
    }

    public async Task<List<AgentRecord>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task SaveAgentAsync(Agent agent, List<string>? taskIds = null)
    {
        var record = new AgentRecord
        {
            Id = agent.Id,
            Name = agent.Name,
            Cwd = agent.Cwd,
            SessionId = string.IsNullOrEmpty(agent.SessionId) ? null : agent.SessionId,
            CreatedAt = agent.CreatedAt,
            FinishedAt = agent.FinishedAt,
            TaskIds = taskIds ?? new List<string>(),
            WorktreePath = agent.WorktreePath,
            WorktreeBranch = agent.WorktreeBranch,
            OriginalCwd = agent.OriginalCwd,
        };

        await _lock.WaitAsync();
        try
        {
            var records = await ReadAsync();
            var idx = records.FindIndex(r => r.Id == record.Id);
            if (idx >= 0)
            {
                record.Notes = records[idx].Notes; // preserve user-edited notes
                records[idx] = record;
            }
            else records.Add(record);

            // Auto-cleanup: keep only MaxSessionsPerGroup newest sessions per (Name+Cwd) group
            var toRemove = records
                .GroupBy(r => (r.Name, r.Cwd))
                .SelectMany(g => g
                    .OrderByDescending(r => r.FinishedAt ?? r.CreatedAt)
                    .Skip(MaxSessionsPerGroup))
                .ToHashSet();

            if (toRemove.Count > 0)
                records.RemoveAll(r => toRemove.Contains(r));

            await WriteAsync(records);
        }
        finally { _lock.Release(); }
    }

    public async Task UpdateNotesAsync(string id, string? notes)
    {
        await _lock.WaitAsync();
        try
        {
            var records = await ReadAsync();
            var idx = records.FindIndex(r => r.Id == id);
            if (idx < 0) return;
            records[idx].Notes = notes;
            await WriteAsync(records);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var records = await ReadAsync();
            var idx = records.FindIndex(r => r.Id == id);
            if (idx < 0) return false;
            records.RemoveAt(idx);
            await WriteAsync(records);
            return true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Remove orchestrator hooks from settings.local.json in all known CWDs.
    /// Called at startup — no agents are running yet, so ALL orchestrator hooks are orphaned.
    /// </summary>
    public async Task CleanupOrphanedHooksAsync()
    {
        var records = await GetAllAsync();
        var cwds = records
            .Where(r => !string.IsNullOrEmpty(r.Cwd))
            .Select(r => r.Cwd!)
            .Distinct();

        foreach (var cwd in cwds)
        {
            try
            {
                var settingsPath = Path.Combine(cwd, ".claude", "settings.local.json");
                if (!File.Exists(settingsPath)) continue;

                var content = await File.ReadAllTextAsync(settingsPath);
                var json = JsonNode.Parse(content) as JsonObject;
                if (json?["hooks"] is not JsonObject hooksObj) continue;

                bool changed = false;
                foreach (var key in hooksObj.Select(kv => kv.Key).ToList())
                {
                    if (hooksObj[key] is not JsonArray arr) continue;

                    var cleaned = arr
                        .OfType<JsonObject>()
                        .Where(entry =>
                        {
                            var hooks = entry["hooks"] as JsonArray;
                            return hooks == null || !hooks.OfType<JsonObject>().Any(h =>
                                h["command"]?.GetValue<string>()?.Contains("/api/agents/") == true);
                        })
                        .Select(e => JsonNode.Parse(e.ToJsonString()))
                        .ToArray();

                    if (cleaned.Length != arr.Count)
                    {
                        changed = true;
                        if (cleaned.Length == 0)
                            hooksObj.Remove(key);
                        else
                        {
                            var newArr = new JsonArray();
                            foreach (var item in cleaned) newArr.Add(item);
                            hooksObj[key] = newArr;
                        }
                    }
                }

                if (!changed) continue;

                if (!hooksObj.Any())
                    json.Remove("hooks");

                await File.WriteAllTextAsync(settingsPath,
                    json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
        }
    }

    // Private helpers — must be called from within _lock
    private async Task<List<AgentRecord>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return new List<AgentRecord>();
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<AgentRecord>>(json, JsonOptions) ?? new();
    }

    private async Task WriteAsync(List<AgentRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
