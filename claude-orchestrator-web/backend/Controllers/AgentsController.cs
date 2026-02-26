using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
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

    // ── Claude Code hooks ──────────────────────────────────────────────────

    /// <summary>Called by the Stop hook when Claude finishes a response.</summary>
    [HttpPost("{id}/hook/stop")]
    public async Task<IActionResult> HookStop(string id)
    {
        await _manager.MarkIdleAsync(id);
        return Ok();
    }

    /// <summary>Called by the Notification hook when Claude sends a notification.</summary>
    [HttpPost("{id}/hook/notification")]
    public async Task<IActionResult> HookNotification(string id, [FromBody] JsonElement body)
    {
        var message = body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        await _manager.NotifyAsync(id, message);
        // Also mark idle — notification usually means Claude is waiting
        await _manager.MarkIdleAsync(id);
        return Ok();
    }

    /// <summary>
    /// Called by the PreToolUse hook. Blocks until the user approves or denies.
    /// Returns { approved: bool, reason?: string }.
    /// </summary>
    [HttpPost("{id}/hook/pre-tool")]
    public async Task<IActionResult> HookPreTool(string id, [FromBody] JsonElement body)
    {
        var toolName = body.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "";
        object? toolInput = body.TryGetProperty("tool_input", out var ti) ? (object)ti : null;

        var (approved, reason) = await _manager.RequestPermissionAsync(id, toolName, toolInput);
        // Return format that Claude Code reads directly from hook stdout
        return Ok(approved
            ? (object)new { decision = "approve" }
            : new { decision = "block", reason = reason ?? "User denied this action." });
    }

    // ── Permission responses (called by frontend) ──────────────────────────

    [HttpPost("{id}/permission/{requestId}")]
    public IActionResult RespondPermission(
        string id, string requestId, [FromBody] PermissionRespondRequest req)
    {
        _manager.RespondPermission(requestId, req.Approved, req.Reason);
        return Ok();
    }
}
