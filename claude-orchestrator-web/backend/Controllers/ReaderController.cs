using Microsoft.AspNetCore.Mvc;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Controllers;

public record WatchRequest(string Path);

[ApiController]
[Route("api/[controller]")]
public class ReaderController : ControllerBase
{
    private readonly FileWatcherService _watcher;

    public ReaderController(FileWatcherService watcher)
    {
        _watcher = watcher;
    }

    private static readonly HashSet<string> ContentExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".mdx", ".txt" };

    private static readonly HashSet<string> RawExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };

    [HttpGet("content")]
    public IActionResult GetContent([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Invalid path" });

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return BadRequest(new { error = "Invalid path" }); }

        var ext = Path.GetExtension(full);
        if (!ContentExtensions.Contains(ext))
            return BadRequest(new
            {
                error = "Unsupported extension",
                allowed = ContentExtensions.ToArray()
            });

        if (!System.IO.File.Exists(full))
            return NotFound(new { error = "File not found", path = full });

        try
        {
            var content = System.IO.File.ReadAllText(full);
            var mtime = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(full))
                .ToUnixTimeMilliseconds();
            return Ok(new { path = full, content, mtime });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("raw")]
    public IActionResult GetRaw([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Invalid path" });

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return BadRequest(new { error = "Invalid path" }); }

        var ext = Path.GetExtension(full);
        if (!RawExtensions.Contains(ext))
            return BadRequest(new
            {
                error = "Unsupported extension",
                allowed = RawExtensions.ToArray()
            });

        if (!System.IO.File.Exists(full))
            return NotFound(new { error = "File not found", path = full });

        var contentType = ext.ToLowerInvariant() switch
        {
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".webp" => "image/webp",
            ".svg"  => "image/svg+xml",
            _ => "application/octet-stream"
        };

        try
        {
            var stream = System.IO.File.OpenRead(full);
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("watch")]
    public IActionResult Watch([FromBody] WatchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "Invalid path" });

        string full;
        try { full = Path.GetFullPath(req.Path); }
        catch { return BadRequest(new { error = "Invalid path" }); }

        if (!System.IO.File.Exists(full))
            return NotFound(new { error = "File not found", path = full });

        _watcher.Watch(full);
        return Ok();
    }

    [HttpPost("unwatch")]
    public IActionResult Unwatch([FromBody] WatchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Path)) return Ok();
        string full;
        try { full = Path.GetFullPath(req.Path); }
        catch { return Ok(); }
        _watcher.Unwatch(full);
        return Ok();
    }
}
