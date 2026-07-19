using PatreonBrowser.Api.Models;

namespace PatreonBrowser.Api.Services;

/// <summary>Builds container (read) and Windows host (Explorer) paths from stored relative paths.</summary>
public class PathService
{
    /// <summary>Container absolute path to read a file/dir, given the owning artist and a relpath.</summary>
    public string ToContainerPath(Artist artist, string relPath)
        => CombineForward(artist.ContainerRoot, relPath);

    /// <summary>Windows absolute path for Explorer, given the owning artist and a relpath.</summary>
    public string ToHostPath(Artist artist, string relPath)
    {
        var tail = relPath.Replace('/', '\\').TrimStart('\\');
        var root = artist.HostRoot.TrimEnd('\\');
        return string.IsNullOrEmpty(tail) ? root : $"{root}\\{tail}";
    }

    private static string CombineForward(string root, string relPath)
    {
        var tail = relPath.Replace('\\', '/').TrimStart('/');
        var r = root.TrimEnd('/');
        return string.IsNullOrEmpty(tail) ? r : $"{r}/{tail}";
    }
}
