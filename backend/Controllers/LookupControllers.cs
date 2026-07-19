using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Dtos;
using PatreonBrowser.Api.Models;

namespace PatreonBrowser.Api.Controllers;

[ApiController]
[Route("api/artists")]
public class ArtistsController(AppDbContext db) : ControllerBase
{
    // Counts and visibility reflect only objects that have renders (the ones shown in the UI).
    [HttpGet]
    public async Task<IReadOnlyList<ArtistDto>> Get() =>
        await db.Artists
            .Where(a => a.Releases.SelectMany(r => r.Objects).Any(o => o.Renders.Any()))
            .OrderBy(a => a.Name)
            .Select(a => new ArtistDto(
                a.Id, a.Name,
                a.Releases.Count(r => r.Objects.Any(o => o.Renders.Any())),
                a.Releases.SelectMany(r => r.Objects).Count(o => o.Renders.Any())))
            .ToListAsync();
}

[ApiController]
[Route("api/releases")]
public class ReleasesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ReleaseDto>> Get([FromQuery] int? artistId)
    {
        var q = db.Releases.AsQueryable();
        if (artistId is { } id) q = q.Where(r => r.ArtistId == id);
        // Only releases that contain at least one object with renders.
        q = q.Where(r => r.Objects.Any(o => o.Renders.Any()));
        return await q
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(r => r.Name)
            .Select(r => new ReleaseDto(
                r.Id, r.ArtistId, r.Artist.Name, r.Name,
                r.Code, r.Year, r.Month, r.Objects.Count(o => o.Renders.Any())))
            .ToListAsync();
    }
}

[ApiController]
[Route("api/categories")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CategoryDto>> Get() =>
        await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.ObjectCategories.Count))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CreateCategoryRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Name is required.");

        var cat = await CategoryHelper.FindOrCreateAsync(db, name);
        await db.SaveChangesAsync();
        var count = await db.ObjectCategories.CountAsync(oc => oc.CategoryId == cat.Id);
        return CreatedAtAction(nameof(Get), new CategoryDto(cat.Id, cat.Name, count));
    }

    /// <summary>
    /// Tags every visible object whose display title contains a category name. Idempotent
    /// (skips links that already exist). By default matches whole words so "orc" does not match
    /// "Sorceress"; pass ?wholeWord=false for plain substring matching.
    /// </summary>
    [HttpPost("auto-tag")]
    public async Task<ActionResult<AutoTagResultDto>> AutoTag([FromQuery] bool wholeWord = true)
    {
        var cats = await db.Categories.ToListAsync();
        var objs = await db.Objects
            .Where(o => o.CanonicalObjectId == null)
            .Select(o => new { o.Id, o.Name, o.CustomName })
            .ToListAsync();

        if (cats.Count == 0 || objs.Count == 0)
            return Ok(new AutoTagResultDto(0, cats.Count, objs.Count, []));

        var existing = (await db.ObjectCategories
                .Select(oc => new { oc.MiniObjectId, oc.CategoryId }).ToListAsync())
            .Select(x => (x.MiniObjectId, x.CategoryId)).ToHashSet();

        var matchers = cats.Select(c => new
        {
            Cat = c,
            Rx = new Regex(
                wholeWord ? $@"\b{Regex.Escape(c.Name)}\b" : Regex.Escape(c.Name),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        }).ToList();

        var perCat = cats.ToDictionary(c => c.Id, _ => 0);
        var added = 0;

        foreach (var o in objs)
        {
            var display = string.IsNullOrEmpty(o.CustomName) ? o.Name : o.CustomName;
            foreach (var m in matchers)
            {
                if (existing.Contains((o.Id, m.Cat.Id))) continue;
                if (!m.Rx.IsMatch(display)) continue;
                db.ObjectCategories.Add(new ObjectCategory { MiniObjectId = o.Id, CategoryId = m.Cat.Id });
                existing.Add((o.Id, m.Cat.Id));
                perCat[m.Cat.Id]++;
                added++;
            }
        }

        await db.SaveChangesAsync();

        var per = cats
            .Select(c => new CategoryTagCountDto(c.Name, perCat[c.Id]))
            .Where(p => p.Added > 0)
            .OrderByDescending(p => p.Added)
            .ToList();

        return Ok(new AutoTagResultDto(added, cats.Count, objs.Count, per));
    }
}

internal static class CategoryHelper
{
    public static async Task<Category> FindOrCreateAsync(AppDbContext db, string name)
    {
        var trimmed = name.Trim();
        var key = trimmed.ToLowerInvariant();
        var existing = await db.Categories.FirstOrDefaultAsync(c => c.NameKey == key);
        if (existing is not null) return existing;
        // Always store with a capitalised first letter; the rest is left as typed.
        var cat = new Category { Name = Capitalize(trimmed), NameKey = key };
        db.Categories.Add(cat);
        return cat;
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
