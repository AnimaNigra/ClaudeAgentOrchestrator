using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

/// <summary>
/// Prohlížeč e-mailů ze záložky Tools. Čtyři endpointy, dva páry: GET čte
/// z disku podle cesty, POST přijímá nahraný soubor — jinak dělají totéž.
///
/// Přílohy jsou bezstavové: server si mezi požadavky nic neukládá, takže
/// u nahraného souboru klient při stahování pošle soubor znovu (spec §4.1).
/// </summary>
[ApiController]
[Route("api/tools/email")]
public class ToolsController : ControllerBase
{
    private static readonly string[] EmailExtensions = { ".eml", ".msg" };

    /// <summary>Kestrel má výchozí strop těla požadavku ~28,6 MB, což je míň
    /// než náš 100MB limit — bez tohohle atributu by upload tiše selhal na
    /// souborech, které přes cestu projdou (spec §4.2).</summary>
    private const long MaxUploadBytes = LocalFilePath.MaxFileBytes;

    [HttpGet("parse")]
    public IActionResult ParseByPath([FromQuery] string? path)
    {
        var validated = LocalFilePath.Validate(path, EmailExtensions);
        if (Reject(validated) is { } bad) return bad;

        try
        {
            using var stream = System.IO.File.OpenRead(validated.FullPath);
            return Ok(EmailParser.Parse(stream));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ParseFailure(ex) });
        }
    }

    [HttpPost("parse")]
    [RequestSizeLimit(104_857_600)]
    public IActionResult ParseUpload(IFormFile? file)
    {
        if (RejectUpload(file) is { } bad) return bad;

        try
        {
            using var stream = ToSeekable(file!);
            return Ok(EmailParser.Parse(stream));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ParseFailure(ex) });
        }
    }

    [HttpGet("attachment")]
    public IActionResult AttachmentByPath([FromQuery] string? path, [FromQuery] int index)
    {
        var validated = LocalFilePath.Validate(path, EmailExtensions);
        if (Reject(validated) is { } bad) return bad;

        try
        {
            using var stream = System.IO.File.OpenRead(validated.FullPath);
            return Attachment(stream, index);
        }
        catch (IndexOutOfRangeException)
        {
            return NotFound(new { error = $"Příloha s indexem {index} neexistuje." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ParseFailure(ex) });
        }
    }

    [HttpPost("attachment")]
    [RequestSizeLimit(104_857_600)]
    public IActionResult AttachmentUpload(IFormFile? file, [FromForm] int index)
    {
        if (RejectUpload(file) is { } bad) return bad;

        try
        {
            using var stream = ToSeekable(file!);
            return Attachment(stream, index);
        }
        catch (IndexOutOfRangeException)
        {
            return NotFound(new { error = $"Příloha s indexem {index} neexistuje." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ParseFailure(ex) });
        }
    }

    private FileContentResult Attachment(Stream stream, int index)
    {
        var format = EmailParser.SniffFormat(stream);
        var (bytes, fileName, contentType) =
            EmailParser.ExtractAttachmentBytes(stream, format, index);
        return File(bytes, contentType, fileDownloadName: fileName);
    }

    /// <summary>IFormFile.OpenReadStream() nemusí být seekovatelný, ale
    /// SniffFormat i MsgReader ho potřebují. Soubor je stropem omezený
    /// na 100 MB, takže kopie do paměti je přijatelná.</summary>
    private static MemoryStream ToSeekable(IFormFile file)
    {
        var ms = new MemoryStream();
        using (var src = file.OpenReadStream()) src.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    private IActionResult? Reject(LocalFilePathResult result)
    {
        if (result.Ok) return null;
        return result.Error == PathError.NotFound
            ? NotFound(new { error = result.Message })
            : BadRequest(new { error = result.Message });
    }

    private IActionResult? RejectUpload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Nebyl odeslán žádný soubor." });

        var ext = Path.GetExtension(file.FileName);
        if (!EmailExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Nepodporovaná přípona souboru. Očekává se .eml nebo .msg." });

        if (file.Length > MaxUploadBytes)
            return BadRequest(new { error = $"Soubor je příliš velký (max {MaxUploadBytes / 1024 / 1024} MB)." });

        return null;
    }

    private static string ParseFailure(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Length > 500) msg = msg[..500] + "…";
        return $"Nepodařilo se přečíst zprávu: {msg}";
    }
}
