using System.Collections.Concurrent;
using PatreonBrowser.Api.Dtos;

namespace PatreonBrowser.Api.Services;

/// <summary>Singleton holding live progress of the current/last scan. Thread-safe.</summary>
public class ScanState
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _warnings = new();

    public bool Running { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public string? Phase { get; set; }
    public int ArtistsScanned;
    public int ReleasesFound;
    public int ObjectsFound;
    public int RendersFound;
    public int ObjectsAdded;
    public int RendersAdded;
    public string? Message { get; set; }

    /// <summary>Attempts to mark a scan started. Returns false if one is already running.</summary>
    public bool TryStart()
    {
        lock (_gate)
        {
            if (Running) return false;
            Running = true;
            StartedAt = DateTime.UtcNow;
            FinishedAt = null;
            Phase = "Starting";
            Message = "Scanning...";
            ArtistsScanned = ReleasesFound = ObjectsFound = RendersFound = ObjectsAdded = RendersAdded = 0;
            _warnings.Clear();
            return true;
        }
    }

    public void Finish(string message)
    {
        lock (_gate)
        {
            Running = false;
            FinishedAt = DateTime.UtcNow;
            Phase = "Done";
            Message = message;
        }
    }

    public void Warn(string warning) => _warnings.Enqueue(warning);

    public ScanStatusDto Snapshot() => new(
        Running, StartedAt, FinishedAt, Phase,
        ArtistsScanned, ReleasesFound, ObjectsFound, RendersFound,
        ObjectsAdded, RendersAdded, _warnings.ToArray(), Message);
}
