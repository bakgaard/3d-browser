using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Dtos;
using PatreonBrowser.Api.Models;
using PatreonBrowser.Api.Services;

namespace PatreonBrowser.Api.Controllers;

[ApiController]
[Route("api/objects")]
public class ObjectsController(AppDbContext db, PathService paths) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ObjectDetailDto>> Get(int id)
    {
        var o = await db.Objects
            .Include(x => x.Release).ThenInclude(r => r.Artist)
            .Include(x => x.Renders)
            .Include(x => x.ObjectCategories).ThenInclude(oc => oc.Category)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (o is null) return NotFound();

        return ToDetailDto(o);
    }

    // ---- Custom title -----------------------------------------------------------------------

    [HttpPut("{id:int}/name")]
    public async Task<ActionResult<ObjectDetailDto>> SetCustomName(int id, [FromBody] SetCustomNameRequest req)
    {
        var o = await LoadFullAsync(id);
        if (o is null) return NotFound();

        var name = req.CustomName?.Trim();
        o.CustomName = string.IsNullOrEmpty(name) ? null : name;
        await db.SaveChangesAsync();
        return ToDetailDto(o);
    }

    // ---- Primary render ---------------------------------------------------------------------

    [HttpPut("{id:int}/primary-render")]
    public async Task<ActionResult<ObjectDetailDto>> SetPrimaryRender(int id, [FromBody] SetPrimaryRenderRequest req)
    {
        var o = await LoadFullAsync(id);
        if (o is null) return NotFound();
        if (o.Renders.All(r => r.Id != req.RenderId))
            return BadRequest("That render does not belong to this object.");

        o.PrimaryRenderId = req.RenderId;
        await db.SaveChangesAsync();
        return ToDetailDto(o);
    }

    // ---- Merge ("these are the same object") ------------------------------------------------

    [HttpPost("{id:int}/merge")]
    public async Task<ActionResult<ObjectDetailDto>> Merge(int id, [FromBody] MergeRequest req)
    {
        // Resolve the target to its canonical root (never merge into an alias).
        var target = await db.Objects.Include(o => o.Renders).Include(o => o.ObjectCategories)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (target is null) return NotFound();
        while (target!.CanonicalObjectId is { } canon)
            target = await db.Objects.Include(o => o.Renders).Include(o => o.ObjectCategories)
                .FirstAsync(o => o.Id == canon);

        var sourceIds = req.SourceIds.Where(s => s != target.Id).Distinct().ToList();
        if (sourceIds.Count == 0) return ToDetailDto(await LoadFullAsync(target.Id) ?? target);

        var sources = await db.Objects.Include(o => o.Renders).Include(o => o.ObjectCategories)
            .Where(o => sourceIds.Contains(o.Id)).ToListAsync();

        var targetCatIds = target.ObjectCategories.Select(oc => oc.CategoryId).ToHashSet();

        foreach (var s in sources)
        {
            foreach (var r in s.Renders)
                r.MiniObjectId = target.Id;

            foreach (var oc in s.ObjectCategories.ToList())
            {
                if (targetCatIds.Add(oc.CategoryId))
                    db.ObjectCategories.Add(new ObjectCategory { MiniObjectId = target.Id, CategoryId = oc.CategoryId });
                db.ObjectCategories.Remove(oc);
            }

            target.ModelFileCount += s.ModelFileCount;
            s.CanonicalObjectId = target.Id;
            s.PrimaryRenderId = null;
        }

        // Re-point any objects that were previously merged into one of these sources (merge chains).
        var chained = await db.Objects
            .Where(o => o.CanonicalObjectId != null && sourceIds.Contains(o.CanonicalObjectId.Value))
            .ToListAsync();
        foreach (var c in chained) c.CanonicalObjectId = target.Id;

        await db.SaveChangesAsync();
        return ToDetailDto(await LoadFullAsync(target.Id) ?? target);
    }

    // ---- Categories -------------------------------------------------------------------------

    [HttpPost("{id:int}/categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryRefDto>>> AddCategory(
        int id, [FromBody] AddCategoryRequest req)
    {
        var obj = await db.Objects.FirstOrDefaultAsync(o => o.Id == id);
        if (obj is null) return NotFound();

        Category? cat;
        if (req.CategoryId is { } cid)
        {
            cat = await db.Categories.FindAsync(cid);
            if (cat is null) return BadRequest("Unknown categoryId.");
        }
        else if (!string.IsNullOrWhiteSpace(req.Name))
        {
            cat = await CategoryHelper.FindOrCreateAsync(db, req.Name);
            await db.SaveChangesAsync();
        }
        else
        {
            return BadRequest("Provide categoryId or name.");
        }

        var linked = await db.ObjectCategories
            .AnyAsync(oc => oc.MiniObjectId == id && oc.CategoryId == cat.Id);
        if (!linked)
        {
            db.ObjectCategories.Add(new ObjectCategory { MiniObjectId = id, CategoryId = cat.Id });
            await db.SaveChangesAsync();
        }

        return Ok(await CurrentCategories(id));
    }

    [HttpDelete("{id:int}/categories/{categoryId:int}")]
    public async Task<IActionResult> RemoveCategory(int id, int categoryId)
    {
        var link = await db.ObjectCategories
            .FirstOrDefaultAsync(oc => oc.MiniObjectId == id && oc.CategoryId == categoryId);
        if (link is null) return NoContent();
        db.ObjectCategories.Remove(link);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private Task<MiniObject?> LoadFullAsync(int id) =>
        db.Objects
            .Include(x => x.Release).ThenInclude(r => r.Artist)
            .Include(x => x.Renders)
            .Include(x => x.ObjectCategories).ThenInclude(oc => oc.Category)
            .FirstOrDefaultAsync(x => x.Id == id);

    private ObjectDetailDto ToDetailDto(MiniObject o)
    {
        var artist = o.Release.Artist;
        var hostPath = paths.ToHostPath(artist, o.FolderRelPath);
        return new ObjectDetailDto(
            o.Id, o.CustomName ?? o.Name,
            o.CustomName, o.Name,
            artist.Id, artist.Name,
            o.ReleaseId, o.Release.Name,
            o.FolderRelPath, hostPath,
            o.ModelFileCount,
            o.PrimaryRenderId,
            o.ObjectCategories.OrderBy(oc => oc.Category.Name)
                .Select(oc => new CategoryRefDto(oc.CategoryId, oc.Category.Name)).ToList(),
            o.Renders.OrderBy(r => r.FileName)
                .Select(r => new RenderDto(r.Id, r.FileName, r.RelPath)).ToList());
    }

    private async Task<IReadOnlyList<CategoryRefDto>> CurrentCategories(int id) =>
        await db.ObjectCategories
            .Where(oc => oc.MiniObjectId == id)
            .OrderBy(oc => oc.Category.Name)
            .Select(oc => new CategoryRefDto(oc.CategoryId, oc.Category.Name))
            .ToListAsync();
}
