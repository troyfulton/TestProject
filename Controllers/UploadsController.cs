using Microsoft.AspNetCore.Mvc;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly UploadService _uploads;

    public UploadsController(UploadService uploads)
    {
        _uploads = uploads;
    }

    [HttpPost]
    public async Task<ActionResult<UploadSessionDto>> Create(
        [FromBody] CreateUploadRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _uploads.CreateSessionAsync(request, cancellationToken);
        return Created($"/api/uploads/{session.UploadId}", session);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UploadSessionDto>> Get(string id, CancellationToken cancellationToken)
    {
        return Ok(await _uploads.GetSessionAsync(id, cancellationToken));
    }

    [HttpPut("{id}/chunk")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
    public async Task<ActionResult<UploadSessionDto>> UploadChunk(
        string id,
        [FromQuery] long offset,
        [FromHeader(Name = "X-Chunk-Checksum")] string? checksum,
        CancellationToken cancellationToken)
    {
        var session = await _uploads.WriteChunkAsync(id, offset, checksum, Request.Body, cancellationToken);
        return Ok(session);
    }

    [HttpPost("{id}/complete")]
    public async Task<ActionResult<UploadCompletedDto>> Complete(
        string id,
        [FromBody] CompleteUploadRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _uploads.CompleteAsync(id, request?.Sha256, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id, CancellationToken cancellationToken)
    {
        await _uploads.CancelAsync(id, cancellationToken);
        return NoContent();
    }
}
