using System.ComponentModel.DataAnnotations;

namespace PatreonBrowser.Api.Models;

/// <summary>Top-level folder under the Patreon library (the creator/studio).</summary>
public class Artist
{
    public int Id { get; set; }
    [MaxLength(512)] public string Name { get; set; } = "";

    /// <summary>Container-side absolute path used to READ files (e.g. /data/patreon/Artisan Guild).</summary>
    [MaxLength(1024)] public string ContainerRoot { get; set; } = "";

    /// <summary>Windows host absolute path used to OPEN in Explorer (e.g. E:\Patreon\Artisan Guild).</summary>
    [MaxLength(1024)] public string HostRoot { get; set; } = "";

    /// <summary>True when this artist came from a .lnk shortcut pointing at another drive.</summary>
    public bool IsShortcut { get; set; }

    public List<Release> Releases { get; set; } = new();
}

/// <summary>First-level sub-folder under an artist (a monthly release / drop).</summary>
public class Release
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    [MaxLength(512)] public string Name { get; set; } = "";
    /// <summary>Path relative to the artist root, forward-slashed (e.g. "2020.09 - Werewolfs").</summary>
    [MaxLength(1024)] public string RelPath { get; set; } = "";

    /// <summary>Parsed "YYYY.MM" code when the folder name starts with a date, else null.</summary>
    [MaxLength(16)] public string? Code { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }

    public List<MiniObject> Objects { get; set; } = new();
}

/// <summary>A single miniature/model grouping. The unit the user searches for and categorises.</summary>
public class MiniObject
{
    public int Id { get; set; }
    public int ReleaseId { get; set; }
    public Release Release { get; set; } = null!;

    /// <summary>Name derived by the scanner from the folder/file names.</summary>
    [MaxLength(512)] public string Name { get; set; } = "";

    /// <summary>User-supplied title that overrides <see cref="Name"/> for display when set.</summary>
    [MaxLength(512)] public string? CustomName { get; set; }

    /// <summary>Which render to show as the thumbnail. Falls back to the first render when null.</summary>
    public int? PrimaryRenderId { get; set; }

    /// <summary>
    /// When set, this object has been merged into another (the canonical object). Its renders were
    /// moved there; it is hidden from the UI. Kept so re-scans don't resurrect it as a separate item.
    /// </summary>
    public int? CanonicalObjectId { get; set; }

    /// <summary>Stable unique identity (relative to artist root). Re-scans match on this so IDs + categories survive.</summary>
    [MaxLength(1200)] public string Key { get; set; } = "";

    /// <summary>Directory to open in Explorer, relative to the artist root, forward-slashed.</summary>
    [MaxLength(1024)] public string FolderRelPath { get; set; } = "";

    public int ModelFileCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Render> Renders { get; set; } = new();
    public List<ObjectCategory> ObjectCategories { get; set; } = new();
}

/// <summary>A render image (jpg/png/gif/...) belonging to an object.</summary>
public class Render
{
    public int Id { get; set; }
    public int MiniObjectId { get; set; }
    public MiniObject MiniObject { get; set; } = null!;

    [MaxLength(512)] public string FileName { get; set; } = "";
    /// <summary>Absolute container path used to read the file. Globally unique (the scan key).</summary>
    [MaxLength(1400)] public string ContainerPath { get; set; } = "";
    /// <summary>Path relative to the artist root, forward-slashed. For display/breadcrumbs.</summary>
    [MaxLength(1200)] public string RelPath { get; set; } = "";
    [MaxLength(16)] public string Extension { get; set; } = "";
    public long FileSize { get; set; }

    public List<RenderRegion> Regions { get; set; } = new();
}

/// <summary>A free-form tag (human, elf, orc, ...). Unique by lower-cased name.</summary>
public class Category
{
    public int Id { get; set; }
    [MaxLength(128)] public string Name { get; set; } = "";
    /// <summary>Lower-cased name used for uniqueness/lookups.</summary>
    [MaxLength(128)] public string NameKey { get; set; } = "";

    public List<ObjectCategory> ObjectCategories { get; set; } = new();
}

/// <summary>Join: object &lt;-&gt; category (object-level tagging).</summary>
public class ObjectCategory
{
    public int MiniObjectId { get; set; }
    public MiniObject MiniObject { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

/// <summary>
/// Phase-2 feature (schema present now): a rectangular region drawn on a render, tagged with a
/// category, marking "what is in this part of the image". Coordinates are normalised 0..1.
/// </summary>
public class RenderRegion
{
    public int Id { get; set; }
    public int RenderId { get; set; }
    public Render Render { get; set; } = null!;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    [MaxLength(512)] public string? Note { get; set; }
}
