using Inventory.Api.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Endpoints used by the handheld app's sync engine.</summary>
[ApiController]
[Route("api/inventory/sync")]
public class SyncController(SyncService sync) : ControllerBase
{
    /// <summary>Upload queued offline mutations. Safe to call repeatedly with the same batch.</summary>
    [HttpPost("push")]
    public async Task<ActionResult<PushResponse>> Push(PushRequest request, CancellationToken ct)
    {
        if (request.Mutations.Count > 200)
            return BadRequest("Send at most 200 mutations per push");
        return await sync.PushAsync(request, ct);
    }

    /// <summary>Download stock rows changed since the given checkpoint.</summary>
    [HttpGet("pull")]
    public Task<PullResponse> Pull([FromQuery] string storeId, [FromQuery] string? since, CancellationToken ct)
        => sync.PullAsync(storeId, since, ct);
}
