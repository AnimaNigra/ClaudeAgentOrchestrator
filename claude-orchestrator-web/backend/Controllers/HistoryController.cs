using ClaudeOrchestrator.Models;
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

    // PATCH /api/history/{id}  — update notes
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateNotes(string id, [FromBody] UpdateHistoryRequest req)
    {
        await _history.UpdateNotesAsync(id, req.Notes);
        return Ok();
    }

    // DELETE /api/history/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _history.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
