using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly TaskService _tasks;
    private readonly AgentManager _agents;

    public TasksController(TaskService tasks, AgentManager agents)
    {
        _tasks = tasks;
        _agents = agents;
    }

    // GET /api/tasks
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await _tasks.GetAllAsync();
        return Ok(tasks);
    }

    // POST /api/tasks
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required" });

        var task = await _tasks.CreateAsync(req);
        return Ok(task);
    }

    // PUT /api/tasks/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTaskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required" });

        var task = await _tasks.UpdateAsync(id, req);
        if (task is null) return NotFound(new { error = "Task not found" });
        return Ok(task);
    }

    // DELETE /api/tasks/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _tasks.DeleteAsync(id);
        if (!deleted) return NotFound(new { error = "Task not found" });
        return Ok(new { deleted = true });
    }

    // POST /api/tasks/{id}/assign  — body: { agentId }
    [HttpPost("{id}/assign")]
    public async Task<IActionResult> Assign(string id, [FromBody] AssignTaskRequest req)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null) return NotFound(new { error = "Task not found" });

        var agent = _agents.GetAgent(req.AgentId);
        if (agent is null || agent.Status == AgentStatus.Done || agent.Status == AgentStatus.Error)
            return BadRequest(new { error = "Agent is not available" });

        // Send the prompt to the agent (if there is one)
        if (!string.IsNullOrEmpty(task.Prompt))
        {
            await _agents.WriteInputAsync(req.AgentId, task.Prompt + "\n");
        }

        // Update task to in-progress
        await _tasks.SetInProgressAsync(id, agent.Id, agent.Name);

        var updated = await _tasks.GetByIdAsync(id);
        return Ok(updated);
    }
}
