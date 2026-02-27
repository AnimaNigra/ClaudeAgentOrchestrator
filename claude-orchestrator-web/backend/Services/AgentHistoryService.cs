using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class AgentHistoryService
{
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
            TaskIds = taskIds ?? new List<string>()
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
