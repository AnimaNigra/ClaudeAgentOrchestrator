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
            var agent = await _manager.SpawnAgentAsync(req.Name, req.Cwd, req.ResumeSessionId);
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

    /// <summary>Return git status and diff for the agent's working directory.</summary>
    [HttpGet("{id}/review")]
    public async Task<IActionResult> Review(string id, [FromServices] GitReviewService git)
    {
        var agent = _manager.GetAgent(id);
        if (agent is null) return NotFound();
        var result = await git.GetReviewAsync(agent.Cwd);
        return Ok(result);
    }

    /// <summary>Upload an image file into the agent's working directory and notify the agent.</summary>
    [HttpPost("{id}/upload")]
    public async Task<IActionResult> Upload(string id, IFormFile file)
    {
        var agent = _manager.GetAgent(id);
        if (agent is null) return NotFound(new { error = "Agent not found" });

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "Only image files are supported" });

        var baseDir = agent.Cwd ?? AppContext.BaseDirectory;
        var tmpDir = Path.Combine(baseDir, "tmp");
        Directory.CreateDirectory(tmpDir);

        var ext = Path.GetExtension(file.FileName) is { Length: > 0 } e ? e : ".png";
        var baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var fileName = $"{baseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";
        var fullPath = Path.Combine(tmpDir, fileName);
        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        try { await _manager.WriteInputAsync(id, $"{fullPath}\r"); }
        catch (KeyNotFoundException) { }

        return Ok(new { fileName, path = fullPath });
    }

    /// <summary>Open the agent's working directory in the system file explorer.</summary>
    [HttpPost("{id}/open-folder")]
    public IActionResult OpenFolder(string id)
    {
        var agent = _manager.GetAgent(id);
        if (agent is null) return NotFound();
        var dir = agent.Cwd ?? AppContext.BaseDirectory;
        if (!Directory.Exists(dir)) return BadRequest(new { error = "Directory does not exist" });
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
        return Ok();
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

    /// <summary>Called by the Statusline hook with structured usage JSON.</summary>
    [HttpPost("{id}/hook/statusline")]
    public async Task<IActionResult> HookStatusline(string id, [FromBody] JsonElement body)
    {
        int? contextPct = null;
        double? cost = null;
        int? rateLimitPct = null;
        string? rateLimitReset = null;
        string? modelName = null;

        if (body.TryGetProperty("context_window", out var cw))
        {
            if (cw.TryGetProperty("used_percentage", out var up) && up.ValueKind == JsonValueKind.Number)
                contextPct = up.GetInt32();
        }

        if (body.TryGetProperty("cost", out var c) &&
            c.TryGetProperty("total_cost_usd", out var tc) && tc.ValueKind == JsonValueKind.Number)
            cost = Math.Round(tc.GetDouble(), 4);

        if (body.TryGetProperty("rate_limits", out var rl) &&
            rl.TryGetProperty("five_hour", out var fh))
        {
            if (fh.TryGetProperty("used_percentage", out var rp) && rp.ValueKind == JsonValueKind.Number)
                rateLimitPct = (int)Math.Round(rp.GetDouble());
            if (fh.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number)
                rateLimitReset = DateTimeOffset.FromUnixTimeSeconds(ra.GetInt64())
                    .ToLocalTime().ToString("HH:mm");
        }

        if (body.TryGetProperty("model", out var model) &&
            model.TryGetProperty("display_name", out var dn))
            modelName = dn.GetString();

        await _manager.UpdateUsageAsync(id, contextPct, cost, rateLimitPct, rateLimitReset, modelName);
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
