using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReaderController : ControllerBase
{
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

        var stream = System.IO.File.OpenRead(full);
        return File(stream, contentType);
    }
}
