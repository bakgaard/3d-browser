using Microsoft.AspNetCore.Mvc;
using PatreonBrowser.Api.Dtos;
using PatreonBrowser.Api.Services;

namespace PatreonBrowser.Api.Controllers;

[ApiController]
[Route("api/scan")]
public class ScanController(ScannerService scanner, ScanState state) : ControllerBase
{
    [HttpPost]
    public IActionResult Start()
    {
        if (!state.TryStart())
            return Accepted(state.Snapshot()); // already running

        // Fire-and-forget: the scanner creates its own DI scope + DbContext.
        _ = Task.Run(() => scanner.RunAsync());
        return AcceptedAtAction(nameof(Status), state.Snapshot());
    }

    [HttpGet("status")]
    public ScanStatusDto Status() => state.Snapshot();
}
