using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrioritiesController : ControllerBase
{
    private readonly PriorityService _priorities;

    public PrioritiesController(PriorityService priorities)
    {
        _priorities = priorities;
    }

    // GET /api/priorities
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _priorities.GetAllAsync();
        return Ok(items);
    }

    // POST /api/priorities
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePriorityRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "Text is required" });

        var item = await _priorities.CreateAsync(req);
        return Ok(item);
    }

    // PUT /api/priorities/reorder  — must be before {id} route to avoid ambiguity
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<ReorderPriorityItem> reorderList)
    {
        await _priorities.ReorderAsync(reorderList);
        return Ok(new { ok = true });
    }

    // PUT /api/priorities/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePriorityRequest req)
    {
        var item = await _priorities.UpdateAsync(id, req);
        if (item is null) return NotFound(new { error = "Priority not found" });
        return Ok(item);
    }

    // DELETE /api/priorities/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _priorities.DeleteAsync(id);
        if (!deleted) return NotFound(new { error = "Priority not found" });
        return Ok(new { deleted = true });
    }
}
