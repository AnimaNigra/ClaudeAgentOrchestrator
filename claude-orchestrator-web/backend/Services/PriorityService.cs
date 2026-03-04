using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class PriorityService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PriorityService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "priorities.json");
    }

    public async Task<List<PriorityItem>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            return items.OrderBy(i => i.Order).ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task<PriorityItem> CreateAsync(CreatePriorityRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var item = new PriorityItem
            {
                Text = req.Text,
                Order = items.Count > 0 ? items.Max(i => i.Order) + 1 : 0
            };
            items.Add(item);
            await WriteAsync(items);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task<PriorityItem?> UpdateAsync(string id, UpdatePriorityRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var item = items.FirstOrDefault(i => i.Id == id);
            if (item is null) return null;
            if (req.Text is not null) item.Text = req.Text;
            if (req.Done is not null) item.Done = req.Done.Value;
            await WriteAsync(items);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task ReorderAsync(List<ReorderPriorityItem> reorderList)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            foreach (var r in reorderList)
            {
                var item = items.FirstOrDefault(i => i.Id == r.Id);
                if (item is not null) item.Order = r.Order;
            }
            await WriteAsync(items);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var removed = items.RemoveAll(i => i.Id == id);
            if (removed > 0) await WriteAsync(items);
            return removed > 0;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<PriorityItem>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return [];
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<PriorityItem>>(json, JsonOptions) ?? [];
    }

    private async Task WriteAsync(List<PriorityItem> items)
    {
        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
