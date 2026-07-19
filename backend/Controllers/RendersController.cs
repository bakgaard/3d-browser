using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Services;

namespace PatreonBrowser.Api.Controllers;

[ApiController]
[Route("api/renders")]
public class RendersController(AppDbContext db, ThumbnailService thumbs) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> File(int id)
    {
        var r = await db.Renders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        if (!System.IO.File.Exists(r.ContainerPath)) return NotFound("Source image missing.");

        Response.Headers.CacheControl = "public, max-age=86400";
        return PhysicalFile(r.ContainerPath, ContentTypeFor(r.ContainerPath), enableRangeProcessing: true);
    }

    [HttpGet("{id:int}/thumbnail")]
    public async Task<IActionResult> Thumbnail(int id, CancellationToken ct)
    {
        var r = await db.Renders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();

        var thumbPath = await thumbs.GetOrCreateAsync(id, r.ContainerPath, ct);
        if (thumbPath is null) return NotFound("Thumbnail unavailable.");

        Response.Headers.CacheControl = "public, max-age=86400";
        return PhysicalFile(thumbPath, "image/jpeg");
    }

    private static string ContentTypeFor(string path) =>
        ContentTypes.TryGetContentType(path, out var ct) ? ct : "application/octet-stream";
}
