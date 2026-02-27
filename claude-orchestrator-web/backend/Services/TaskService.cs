using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class TaskService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TaskService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "tasks.json");
    }

    public async Task<List<TaskItem>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<TaskItem?> GetByIdAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            return tasks.FirstOrDefault(t => t.Id == id);
        }
        finally { _lock.Release(); }
    }

    public async Task<TaskItem> CreateAsync(CreateTaskRequest req)
    {
        var task = new TaskItem
        {
            Title = req.Title,
            Description = req.Description,
            Prompt = req.Prompt
        };

        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            tasks.Add(task);
            await WriteAsync(tasks);
            return task;
        }
        finally { _lock.Release(); }
    }

    public async Task<TaskItem?> UpdateAsync(string id, UpdateTaskRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var idx = tasks.FindIndex(t => t.Id == id);
            if (idx < 0) return null;

            var existing = tasks[idx];
            existing.Title = req.Title;
            existing.Description = req.Description;
            existing.Prompt = req.Prompt;
            if (req.Status is not null)
                existing.Status = req.Status;
            existing.AgentId = req.AgentId;
            existing.AgentName = req.AgentName;
            existing.UpdatedAt = DateTime.UtcNow;

            await WriteAsync(tasks);
            return existing;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var removed = tasks.RemoveAll(t => t.Id == id);
            if (removed > 0) await WriteAsync(tasks);
            return removed > 0;
        }
        finally { _lock.Release(); }
    }

    public async Task SetInProgressAsync(string taskId, string agentId, string agentName)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var task = tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null) return;
            task.Status = "in-progress";
            task.AgentId = agentId;
            task.AgentName = agentName;
            task.UpdatedAt = DateTime.UtcNow;
            await WriteAsync(tasks);
        }
        finally { _lock.Release(); }
    }

    // Private helpers — must be called from within _lock
    private async Task<List<TaskItem>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return new List<TaskItem>();
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new();
    }

    private async Task WriteAsync(List<TaskItem> tasks)
    {
        var json = JsonSerializer.Serialize(tasks, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
