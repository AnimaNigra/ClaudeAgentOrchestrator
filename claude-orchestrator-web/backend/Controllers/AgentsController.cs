using Microsoft.AspNetCore.Mvc;
using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly AgentManager _manager;

    public AgentsController(AgentManager manager)
    {
        _manager = manager;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_manager.ListAgents());

    [HttpPost]
    public async Task<IActionResult> Spawn([FromBody] SpawnRequest req)
    {
        try
        {
            var agent = await _manager.SpawnAgentAsync(req.Name, req.Cwd);
            return Ok(agent);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Kill(string id)
    {
        var agent = _manager.GetAgent(id);
        if (agent is null) return NotFound();
        await _manager.KillAgentAsync(id);
        return NoContent();
    }

    /// <summary>Send keystrokes directly to the PTY (typed characters, Enter, Ctrl+C, etc.).</summary>
    [HttpPost("{id}/keystroke")]
    public async Task<IActionResult> Keystroke(string id, [FromBody] KeystrokeRequest req)
    {
        try
        {
            await _manager.WriteInputAsync(id, req.Data);
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Notify backend when the xterm.js terminal is resized.</summary>
    [HttpPost("{id}/resize")]
    public async Task<IActionResult> Resize(string id, [FromBody] ResizeRequest req)
    {
        await _manager.ResizePtyAsync(id, req.Cols, req.Rows);
        return Ok();
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var agent = _manager.GetAgent(id);
        return agent is null ? NotFound() : Ok(agent);
    }
}
