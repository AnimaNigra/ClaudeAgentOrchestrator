using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly AgentHistoryService _history;

    public HistoryController(AgentHistoryService history)
    {
        _history = history;
    }

    // GET /api/history
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var records = await _history.GetAllAsync();
        return Ok(records.OrderByDescending(r => r.FinishedAt));
    }
}
