using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PatreonBrowser.Api.Services;

/// <summary>Generates and disk-caches JPEG thumbnails (long edge 400px) for render images.</summary>
public class ThumbnailService
{
    private const int LongEdge = 400;
    private readonly string _cacheDir;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(IOptions<LibraryOptions> options, ILogger<ThumbnailService> logger)
    {
        _cacheDir = options.Value.ThumbnailCache;
        _logger = logger;
        try { Directory.CreateDirectory(_cacheDir); } catch { /* created lazily */ }
    }

    /// <summary>
    /// Returns the path to a cached thumbnail for the given render, generating it if needed.
    /// Returns null if the source image is missing or cannot be decoded.
    /// </summary>
    public async Task<string?> GetOrCreateAsync(int renderId, string sourcePath, CancellationToken ct)
    {
        var cachePath = Path.Combine(_cacheDir, $"{renderId}.jpg");

        if (File.Exists(cachePath) && IsFresh(cachePath, sourcePath))
            return cachePath;

        if (!File.Exists(sourcePath))
            return null;

        try
        {
            using var image = await Image.LoadAsync(sourcePath, ct);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(LongEdge, LongEdge),
                Mode = ResizeMode.Max, // preserve aspect ratio, fit within LongEdge x LongEdge
            }));
            Directory.CreateDirectory(_cacheDir);
            var tmp = cachePath + ".tmp";
            await image.SaveAsJpegAsync(tmp, new JpegEncoder { Quality = 82 }, ct);
            File.Move(tmp, cachePath, overwrite: true);
            return cachePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create thumbnail for {Path}", sourcePath);
            return null;
        }
    }

    private static bool IsFresh(string cachePath, string sourcePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(sourcePath);
        }
        catch { return true; }
    }
}
