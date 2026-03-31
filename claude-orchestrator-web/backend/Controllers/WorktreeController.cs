using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorktreeController : ControllerBase
{
    private readonly WorktreeService _worktree;
    private readonly AgentManager _manager;

    public WorktreeController(WorktreeService worktree, AgentManager manager)
    {
        _worktree = worktree;
        _manager = manager;
    }

    /// <summary>List all worktrees for a given repository path.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return BadRequest(new { error = "cwd is required" });
        try
        {
            var worktrees = await _worktree.ListAsync(cwd);
            return Ok(worktrees);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a worktree and spawn a new agent in it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorktreeRequest req)
    {
        try
        {
            var (worktreePath, branch) = await _worktree.CreateAsync(req.Cwd, req.Name);
            var agent = await _manager.SpawnAgentAsync(req.Name, worktreePath,
                worktreePath: worktreePath, worktreeBranch: branch, originalCwd: req.Cwd);
            return Ok(agent);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Remove a worktree (and optionally its branch).</summary>
    [HttpDelete]
    public async Task<IActionResult> Remove(
        [FromQuery] string cwd,
        [FromQuery] string worktreePath,
        [FromQuery] bool deleteBranch = true)
    {
        try
        {
            await _worktree.RemoveAsync(cwd, worktreePath, deleteBranch);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Open a worktree directory in the file explorer.</summary>
    [HttpPost("open-folder")]
    public IActionResult OpenFolder([FromBody] OpenFolderRequest req)
    {
        if (!Directory.Exists(req.Path))
            return BadRequest(new { error = "Directory does not exist" });
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = req.Path,
            UseShellExecute = true,
        });
        return Ok();
    }
}
