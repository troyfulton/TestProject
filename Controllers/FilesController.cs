using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    private readonly FileService _files;
    private readonly FeaturesOptions _features;

    public FilesController(FileService files, IOptions<FeaturesOptions> features)
    {
        _files = files;
        _features = features.Value;
    }

    [HttpGet]
    public ActionResult<DirectoryListingDto> List(
        [FromQuery] string? path,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken)
    {
        return Ok(_files.List(path, page, pageSize, sort, direction, cancellationToken));
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? path)
    {
        EnsureFeature(_features.EnableDownload, "Download");
        var (absolutePath, fileName) = _files.ResolveDownload(path);
        return PhysicalFile(absolutePath, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    [HttpPost("folders")]
    public ActionResult<DirectoryEntryDto> CreateFolder([FromBody] CreateFolderRequest request, CancellationToken cancellationToken)
    {
        EnsureFeature(_features.EnableCreateFolder, "Create folder");
        var created = _files.CreateFolder(request.Path, request.Name, cancellationToken);
        return Created($"/api/files?path={Uri.EscapeDataString(created.Path)}", created);
    }

    [HttpPost("delete")]
    public IActionResult Delete([FromBody] DeleteRequest request, CancellationToken cancellationToken)
    {
        EnsureFeature(_features.EnableDelete, "Delete");
        _files.Delete(request.Path, request.Recursive, cancellationToken);
        return NoContent();
    }

    [HttpPost("move")]
    public ActionResult<DirectoryEntryDto> Move([FromBody] MoveCopyRequest request, CancellationToken cancellationToken)
    {
        EnsureFeature(_features.EnableMove, "Move");
        return Ok(_files.Move(request.SourcePath, request.DestinationDirectory, request.DestinationName, cancellationToken));
    }

    [HttpPost("copy")]
    public async Task<ActionResult<DirectoryEntryDto>> Copy([FromBody] MoveCopyRequest request, CancellationToken cancellationToken)
    {
        EnsureFeature(_features.EnableCopy, "Copy");
        var result = await _files.CopyAsync(request.SourcePath, request.DestinationDirectory, request.DestinationName, cancellationToken);
        return Ok(result);
    }

    private static void EnsureFeature(bool enabled, string name)
    {
        if (!enabled)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                $"{name} is disabled.",
                "feature_disabled");
        }
    }
}
