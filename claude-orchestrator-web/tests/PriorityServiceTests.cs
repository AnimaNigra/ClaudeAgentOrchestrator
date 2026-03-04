using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.Extensions.Configuration;

namespace ClaudeOrchestrator.Tests;

public class PriorityServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PriorityService _service;

    public PriorityServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataDir"] = _tempDir })
            .Build();

        _service = new PriorityService(config);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task CreateAsync_ReturnsItemWithCorrectText()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Hello"));
        Assert.Equal("Hello", item.Text);
        Assert.False(item.Done);
        Assert.Equal(0, item.Order);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSortedByOrder()
    {
        await _service.CreateAsync(new CreatePriorityRequest("First"));
        await _service.CreateAsync(new CreatePriorityRequest("Second"));

        var items = await _service.GetAllAsync();
        Assert.Equal(2, items.Count);
        Assert.True(items[0].Order <= items[1].Order);
    }

    [Fact]
    public async Task UpdateAsync_TogglesDone()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Task"));
        var updated = await _service.UpdateAsync(item.Id, new UpdatePriorityRequest(Done: true));
        Assert.NotNull(updated);
        Assert.True(updated!.Done);
    }

    [Fact]
    public async Task UpdateAsync_ChangesText()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Old"));
        var updated = await _service.UpdateAsync(item.Id, new UpdatePriorityRequest(Text: "New"));
        Assert.Equal("New", updated!.Text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Delete me"));
        var deleted = await _service.DeleteAsync(item.Id);
        Assert.True(deleted);
        var all = await _service.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task ReorderAsync_UpdatesOrder()
    {
        var a = await _service.CreateAsync(new CreatePriorityRequest("A"));
        var b = await _service.CreateAsync(new CreatePriorityRequest("B"));

        await _service.ReorderAsync(
        [
            new ReorderPriorityItem(a.Id, 10),
            new ReorderPriorityItem(b.Id, 5),
        ]);

        var items = await _service.GetAllAsync();
        Assert.Equal("B", items[0].Text); // B has order 5, comes first
        Assert.Equal("A", items[1].Text);
    }
}
