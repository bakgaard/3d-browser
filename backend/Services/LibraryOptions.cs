namespace PatreonBrowser.Api.Services;

/// <summary>
/// Configures where the miniature library lives. The app reads files from container paths
/// but must hand Windows host paths to the Explorer helper — hence the dual roots.
/// </summary>
public class LibraryOptions
{
    public const string SectionName = "Library";

    /// <summary>Container-side path where the primary library is mounted (read-only).</summary>
    public string Root { get; set; } = "/data/patreon";

    /// <summary>Windows path of the primary library, used to build Explorer paths.</summary>
    public string HostRoot { get; set; } = @"E:\Patreon";

    /// <summary>Container path where thumbnails are cached (writable volume).</summary>
    public string ThumbnailCache { get; set; } = "/data/thumbs";

    /// <summary>
    /// Resolution for top-level .lnk shortcut artists, keyed by the shortcut's base name
    /// (file name without ".lnk"). Lets shortcuts on other drives be scanned when mounted.
    /// </summary>
    public Dictionary<string, ShortcutMount> Shortcuts { get; set; } = new();
}

public class ShortcutMount
{
    /// <summary>Windows path the shortcut points at (for Explorer).</summary>
    public string HostPath { get; set; } = "";
    /// <summary>Container path where that folder is mounted (for reading), or empty if not mounted.</summary>
    public string ContainerPath { get; set; } = "";
}
