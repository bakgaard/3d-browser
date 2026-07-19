using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Dtos;

namespace PatreonBrowser.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<SearchResultDto> Search(
        [FromQuery] string? q,
        [FromQuery] int? artistId,
        [FromQuery] int? releaseId,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 48)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Objects.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(o =>
                EF.Functions.ILike(o.Name, pattern) ||
                EF.Functions.ILike(o.Release.Name, pattern) ||
                EF.Functions.ILike(o.Release.Artist.Name, pattern));
        }
        if (artistId is { } aid) query = query.Where(o => o.Release.ArtistId == aid);
        if (releaseId is { } rid) query = query.Where(o => o.ReleaseId == rid);
        if (categoryId is { } cid) query = query.Where(o => o.ObjectCategories.Any(oc => oc.CategoryId == cid));

        // Only surface objects that have at least one render image (model-only objects are hidden),
        // and never show objects that were merged into another (canonical alias).
        query = query.Where(o => o.Renders.Any() && o.CanonicalObjectId == null);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(o => o.Release.Artist.Name)
            .ThenByDescending(o => o.Release.Year).ThenByDescending(o => o.Release.Month)
            .ThenBy(o => o.Release.Name).ThenBy(o => o.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new ObjectSummaryDto(
                o.Id, o.CustomName ?? o.Name,
                o.Release.ArtistId, o.Release.Artist.Name,
                o.ReleaseId, o.Release.Name,
                // Prefer the chosen primary render; otherwise the first by filename.
                o.PrimaryRenderId ?? o.Renders.OrderBy(r => r.FileName).Select(r => (int?)r.Id).FirstOrDefault(),
                o.Renders.Count,
                o.ObjectCategories
                    .OrderBy(oc => oc.Category.Name)
                    .Select(oc => new CategoryRefDto(oc.CategoryId, oc.Category.Name))
                    .ToList()))
            .ToListAsync();

        return new SearchResultDto(total, page, pageSize, items);
    }
}
