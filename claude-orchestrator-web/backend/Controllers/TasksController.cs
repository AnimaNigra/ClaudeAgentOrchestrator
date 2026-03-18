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

    // POST /api/tasks/{id}/upload  — attach image to task
    [HttpPost("{id}/upload")]
    public async Task<IActionResult> Upload(string id, IFormFile file)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null) return NotFound(new { error = "Task not found" });

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "Only image files are supported" });

        var dir = Path.Combine("data", "task-attachments", id);
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(file.FileName) is { Length: > 0 } e ? e : ".png";
        var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        task.Attachments.Add(fullPath);
        task.UpdatedAt = DateTime.UtcNow;
        await _tasks.UpdateAttachmentsAsync(id, task.Attachments);

        return Ok(new { fileName, path = fullPath, attachments = task.Attachments });
    }

    // GET /api/tasks/{id}/attachment?path=...  — serve attachment image
    [HttpGet("{id}/attachment")]
    public IActionResult GetAttachment(string id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(Path.Combine("data", "task-attachments", id)))
            return BadRequest(new { error = "Invalid path" });

        if (!System.IO.File.Exists(path))
            return NotFound();

        var contentType = path.EndsWith(".png") ? "image/png" : path.EndsWith(".jpg") || path.EndsWith(".jpeg") ? "image/jpeg" : "image/png";
        return PhysicalFile(Path.GetFullPath(path), contentType);
    }

    // DELETE /api/tasks/{id}/attachment?path=...  — remove attachment
    [HttpDelete("{id}/attachment")]
    public async Task<IActionResult> DeleteAttachment(string id, [FromQuery] string path)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null) return NotFound(new { error = "Task not found" });

        if (string.IsNullOrEmpty(path) || !path.StartsWith(Path.Combine("data", "task-attachments", id)))
            return BadRequest(new { error = "Invalid path" });

        task.Attachments.Remove(path);
        await _tasks.UpdateAttachmentsAsync(id, task.Attachments);

        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        return Ok(new { attachments = task.Attachments });
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

        // Copy attachments to agent's CWD and build paths for the prompt
        var attachmentPaths = new List<string>();
        if (task.Attachments.Count > 0)
        {
            var agentTmpDir = Path.Combine(agent.Cwd ?? AppContext.BaseDirectory, "tmp");
            Directory.CreateDirectory(agentTmpDir);

            foreach (var srcPath in task.Attachments)
            {
                if (!System.IO.File.Exists(srcPath)) continue;
                var destPath = Path.Combine(agentTmpDir, Path.GetFileName(srcPath));
                System.IO.File.Copy(srcPath, destPath, overwrite: true);
                attachmentPaths.Add(destPath);
            }
        }

        // Send attachment paths first — Claude Code attaches them on Enter
        try
        {
            foreach (var path in attachmentPaths)
            {
                await _agents.WriteInputAsync(req.AgentId, path + "\r");
                await Task.Delay(300); // let Claude process the attachment
            }

            // Then send the prompt — submits with the attached images
            if (!string.IsNullOrWhiteSpace(task.Prompt))
                await _agents.WriteInputAsync(req.AgentId, task.Prompt.TrimEnd() + "\r");
        }
        catch (KeyNotFoundException)
        {
            return BadRequest(new { error = "Agent session is not available" });
        }

        // Update task to in-progress
        await _tasks.SetInProgressAsync(id, agent.Id, agent.Name);

        var updated = await _tasks.GetByIdAsync(id);
        return Ok(updated);
    }
}
