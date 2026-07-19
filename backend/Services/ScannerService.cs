using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Models;

namespace PatreonBrowser.Api.Services;

/// <summary>
/// Walks the mounted Patreon library and builds Artist -> Release -> Object -> Render rows.
///
/// Grouping ("smart auto-grouping"): within a release, every non-auxiliary immediate sub-folder
/// becomes one object (all renders/models beneath it roll up, including its own Supported/Lychee
/// sub-folders). Files that sit loose in the release root — plus files inside auxiliary folders
/// (Supported, Lychee, Renders, ...) — are grouped into objects by filename prefix (the token
/// before the first underscore, else the whole name). Flat releases therefore group naturally
/// (all "Flokir_*" files -> one "Flokir" object).
///
/// The scan is idempotent: objects match on a stable <see cref="MiniObject.Key"/> and renders on
/// their unique relative path, so IDs and user-added categories survive re-scans, and nothing is
/// deleted — a re-scan only adds/updates.
/// </summary>
public partial class ScannerService(
    IServiceScopeFactory scopeFactory,
    IOptions<LibraryOptions> options,
    ScanState state,
    ILogger<ScannerService> logger)
{
    private readonly LibraryOptions _opt = options.Value;

    private static readonly HashSet<string> RenderExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    private static readonly HashSet<string> ModelExt = new(StringComparer.OrdinalIgnoreCase)
        { ".stl", ".lys", ".chitubox", ".lyt", ".cbddlp", ".photon", ".3mf", ".ctb",
          ".obj", ".fbx", ".blend", ".lymesh", ".fw3d", ".phz", ".pws", ".mix", ".ztl" };

    private static readonly HashSet<string> AuxExact = new(StringComparer.OrdinalIgnoreCase)
        { "supported", "presupported", "pre-supported", "unsupported", "stl", "stls",
          "parts", "preview", "previews", "render", "renders", "images", "photos",
          "pictures", "lychee", "chitubox" };

    private static readonly string[] AuxContains =
        { "supported", "lychee", "chitubox", "_stl" };

    /// <summary>Case-insensitive on artist name, case-sensitive on the release relative path.</summary>
    private static readonly IEqualityComparer<(string Artist, string Rel)> TupleNameComparer =
        new ArtistRelComparer();

    private sealed class ArtistRelComparer : IEqualityComparer<(string Artist, string Rel)>
    {
        public bool Equals((string Artist, string Rel) x, (string Artist, string Rel) y)
            => string.Equals(x.Artist, y.Artist, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Rel, y.Rel, StringComparison.Ordinal);

        public int GetHashCode((string Artist, string Rel) o)
            => HashCode.Combine(o.Artist.ToLowerInvariant(), o.Rel);
    }

    [GeneratedRegex(@"^\s*(\d{4})[._\-](\d{1,2})")]
    private static partial Regex ReleaseDateRegex();

    /// <summary>Runs a full scan on a background thread-safe DbContext scope.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await ScanInternalAsync(db, ct);
            state.Finish($"Scan complete: {state.ObjectsFound} objects, {state.RendersFound} renders " +
                         $"({state.ObjectsAdded} new objects, {state.RendersAdded} new renders).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan failed");
            state.Warn($"Scan failed: {ex.Message}");
            state.Finish("Scan failed. See warnings.");
        }
    }

    private async Task ScanInternalAsync(AppDbContext db, CancellationToken ct)
    {
        if (!Directory.Exists(_opt.Root))
        {
            state.Warn($"Library root not found in container: {_opt.Root}. Is the volume mounted?");
            return;
        }

        // Preload existing entities so re-scans update in place and preserve categories.
        var artists = await db.Artists.ToDictionaryAsync(a => a.Name, StringComparer.OrdinalIgnoreCase, ct);
        var releases = await db.Releases.Include(r => r.Artist).ToListAsync(ct);
        var releaseIndex = releases.ToDictionary(
            r => (r.Artist.Name, r.RelPath),
            r => r,
            TupleNameComparer);
        var objects = await db.Objects.ToDictionaryAsync(o => o.Key, ct);
        var objectsById = objects.Values.ToDictionary(o => o.Id);
        var renders = await db.Renders.ToDictionaryAsync(r => r.ContainerPath, ct);

        foreach (var root in EnumerateArtistRoots())
        {
            ct.ThrowIfCancellationRequested();
            state.Phase = root.Name;

            if (!artists.TryGetValue(root.Name, out var artist))
            {
                artist = new Artist { Name = root.Name };
                db.Artists.Add(artist);
                artists[root.Name] = artist;
            }
            artist.ContainerRoot = root.ContainerRoot;
            artist.HostRoot = root.HostRoot;
            artist.IsShortcut = root.IsShortcut;
            Interlocked.Increment(ref state.ArtistsScanned);

            foreach (var rel in EnumerateReleases(root))
            {
                ct.ThrowIfCancellationRequested();

                if (!releaseIndex.TryGetValue((root.Name, rel.RelPath), out var release))
                {
                    release = new Release { Artist = artist, RelPath = rel.RelPath };
                    db.Releases.Add(release);
                    releaseIndex[(root.Name, rel.RelPath)] = release;
                }
                release.Name = rel.Name;
                (release.Code, release.Year, release.Month) = ParseReleaseDate(rel.Name);
                Interlocked.Increment(ref state.ReleasesFound);

                foreach (var bucket in GroupObjects(root.ContainerRoot, rel))
                {
                    ct.ThrowIfCancellationRequested();

                    if (!objects.TryGetValue(bucket.Key, out var obj))
                    {
                        obj = new MiniObject { Key = bucket.Key, CreatedAt = DateTime.UtcNow };
                        obj.Release = release;
                        db.Objects.Add(obj);
                        objects[bucket.Key] = obj;
                        Interlocked.Increment(ref state.ObjectsAdded);
                    }
                    obj.Release = release;
                    obj.Name = bucket.Name;
                    obj.FolderRelPath = bucket.FolderRelPath;
                    obj.ModelFileCount = bucket.ModelCount;
                    obj.UpdatedAt = DateTime.UtcNow;
                    Interlocked.Increment(ref state.ObjectsFound);

                    // If this object was merged into another, attach any NEW renders to the
                    // canonical object so the merge survives re-scans.
                    var renderTarget = obj;
                    if (obj.CanonicalObjectId is { } canonId && objectsById.TryGetValue(canonId, out var canon))
                        renderTarget = canon;

                    foreach (var rf in bucket.Renders)
                    {
                        Interlocked.Increment(ref state.RendersFound);
                        if (renders.TryGetValue(rf.ContainerPath, out var existing))
                        {
                            existing.FileName = rf.FileName;
                            existing.RelPath = rf.RelPath;
                            existing.Extension = rf.Extension;
                            existing.FileSize = rf.Size;
                            continue;
                        }
                        var render = new Render
                        {
                            MiniObject = renderTarget,
                            FileName = rf.FileName,
                            ContainerPath = rf.ContainerPath,
                            RelPath = rf.RelPath,
                            Extension = rf.Extension,
                            FileSize = rf.Size,
                        };
                        db.Renders.Add(render);
                        renders[rf.ContainerPath] = render;
                        Interlocked.Increment(ref state.RendersAdded);
                    }
                }

                // Save incrementally per release to keep memory/transaction size reasonable.
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // ---- Filesystem traversal ---------------------------------------------------------------

    private readonly record struct ArtistRoot(
        string Name, string ContainerRoot, string HostRoot, bool IsShortcut);

    private IEnumerable<ArtistRoot> EnumerateArtistRoots()
    {
        foreach (var dir in SafeGetDirectories(_opt.Root))
        {
            var name = Path.GetFileName(dir);
            yield return new ArtistRoot(name, dir, JoinHost(_opt.HostRoot, name), false);
        }

        // Top-level .lnk shortcuts -> resolve via configured mounts.
        foreach (var lnk in SafeGetFiles(_opt.Root).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileNameWithoutExtension(lnk);
            if (_opt.Shortcuts.TryGetValue(name, out var mount)
                && !string.IsNullOrWhiteSpace(mount.ContainerPath)
                && Directory.Exists(mount.ContainerPath))
            {
                yield return new ArtistRoot(name, mount.ContainerPath, mount.HostPath, true);
            }
            else
            {
                state.Warn($"Shortcut '{name}' not scanned — configure Library:Shortcuts and mount its target " +
                           $"(points at {(mount?.HostPath is { Length: > 0 } h ? h : "unknown")}).");
            }
        }
    }

    private readonly record struct ReleaseInfo(string Name, string RelPath, string ContainerPath);

    private IEnumerable<ReleaseInfo> EnumerateReleases(ArtistRoot root)
    {
        var subdirs = SafeGetDirectories(root.ContainerRoot);
        if (subdirs.Count == 0)
        {
            // Artist with files directly inside it: treat the artist folder as a single release.
            yield return new ReleaseInfo(root.Name, "", root.ContainerRoot);
            yield break;
        }
        foreach (var dir in subdirs)
        {
            var name = Path.GetFileName(dir);
            yield return new ReleaseInfo(name, name, dir);
        }
    }

    // ---- Object grouping --------------------------------------------------------------------

    private sealed class Bucket
    {
        public string Key = "";
        public string Name = "";
        public string FolderRelPath = "";
        public int ModelCount;
        public readonly List<RenderFile> Renders = new();
    }

    private readonly record struct RenderFile(
        string ContainerPath, string RelPath, string FileName, string Extension, long Size);

    private IEnumerable<Bucket> GroupObjects(string artistRoot, ReleaseInfo rel)
    {
        var files = SafeEnumerateFiles(rel.ContainerPath);
        var childBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        var loose = new List<(string full, string name, string ext, long size)>();

        foreach (var full in files)
        {
            var ext = Path.GetExtension(full);
            var isRender = RenderExt.Contains(ext);
            var isModel = ModelExt.Contains(ext);
            if (!isRender && !isModel) continue;

            var relToRelease = Path.GetRelativePath(rel.ContainerPath, full);
            var segments = relToRelease.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(full);
            long size = SafeSize(full);

            if (segments.Length <= 1 || IsAux(segments[0]))
            {
                loose.Add((full, name, ext, size));
                continue;
            }

            var immediate = segments[0];
            if (!childBuckets.TryGetValue(immediate, out var b))
            {
                var folderRel = Combine(rel.RelPath, immediate);
                b = new Bucket
                {
                    Name = immediate,
                    FolderRelPath = folderRel,
                    Key = $"{artistRoot}|{folderRel}",
                };
                childBuckets[immediate] = b;
            }
            AddFile(b, artistRoot, full, name, ext, isRender, size);
        }

        foreach (var b in childBuckets.Values)
            yield return b;

        // Loose + rolled-up-aux files: group by filename prefix.
        foreach (var group in loose.GroupBy(f => PrefixKey(f.name)))
        {
            var first = group.First();
            var display = DisplayPrefix(first.name);
            var b = new Bucket
            {
                Name = display,
                FolderRelPath = rel.RelPath,
                Key = $"{artistRoot}|{rel.RelPath}::{group.Key}",
            };
            foreach (var f in group)
                AddFile(b, artistRoot, f.full, f.name, f.ext, RenderExt.Contains(f.ext), f.size);
            yield return b;
        }
    }

    private static void AddFile(Bucket b, string artistRoot, string full, string name, string ext, bool isRender, long size)
    {
        if (isRender)
        {
            var rel = NormalizeRel(Path.GetRelativePath(artistRoot, full));
            var container = NormalizeRel(full);
            b.Renders.Add(new RenderFile(container, rel, name, ext.ToLowerInvariant(), size));
        }
        else
        {
            b.ModelCount++;
        }
    }

    private bool IsAux(string dirName)
    {
        if (AuxExact.Contains(dirName)) return true;
        var lower = dirName.ToLowerInvariant();
        return AuxContains.Any(t => lower.Contains(t));
    }

    private static string PrefixKey(string fileName)
    {
        var noExt = Path.GetFileNameWithoutExtension(fileName);
        var us = noExt.IndexOf('_');
        var prefix = us > 0 ? noExt[..us] : noExt;
        return prefix.Trim().ToLowerInvariant();
    }

    private static string DisplayPrefix(string fileName)
    {
        var noExt = Path.GetFileNameWithoutExtension(fileName);
        var us = noExt.IndexOf('_');
        return (us > 0 ? noExt[..us] : noExt).Trim();
    }

    private static (string? code, int? year, int? month) ParseReleaseDate(string name)
    {
        var m = ReleaseDateRegex().Match(name);
        if (!m.Success) return (null, null, null);
        var year = int.Parse(m.Groups[1].Value);
        var month = int.Parse(m.Groups[2].Value);
        if (month is < 1 or > 12) return (null, year, null);
        return ($"{year:D4}.{month:D2}", year, month);
    }

    // ---- Path helpers -----------------------------------------------------------------------

    private static string Combine(string a, string b)
        => string.IsNullOrEmpty(a) ? b : $"{a}/{b}";

    private static string NormalizeRel(string p)
        => p.Replace('\\', '/');

    private static string JoinHost(string root, string name)
        => $"{root.TrimEnd('\\')}\\{name}";

    private List<string> SafeGetDirectories(string path)
    {
        try { return Directory.GetDirectories(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (Exception ex) { state.Warn($"Cannot list '{path}': {ex.Message}"); return new(); }
    }

    private List<string> SafeGetFiles(string path)
    {
        try { return Directory.GetFiles(path).ToList(); }
        catch (Exception ex) { state.Warn($"Cannot list files in '{path}': {ex.Message}"); return new(); }
    }

    private IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories); }
        catch (Exception ex) { state.Warn($"Cannot walk '{path}': {ex.Message}"); return Enumerable.Empty<string>(); }
    }

    private static long SafeSize(string full)
    {
        try { return new FileInfo(full).Length; } catch { return 0; }
    }
}
