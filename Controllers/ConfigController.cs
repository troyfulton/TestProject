using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Models;

namespace TestProject.Controllers;

[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    private readonly FeaturesOptions _features;
    private readonly BrowsingOptions _browsing;
    private readonly SearchOptions _search;
    private readonly UploadsOptions _uploads;

    public ConfigController(
        IOptions<FeaturesOptions> features,
        IOptions<BrowsingOptions> browsing,
        IOptions<SearchOptions> search,
        IOptions<UploadsOptions> uploads)
    {
        _features = features.Value;
        _browsing = browsing.Value;
        _search = search.Value;
        _uploads = uploads.Value;
    }

    /// <summary>
    /// Returns public client configuration. Never includes filesystem roots or secrets.
    /// </summary>
    [HttpGet]
    public ActionResult<ClientConfigDto> Get()
    {
        return Ok(new ClientConfigDto
        {
            Features = new FeaturesClientConfig
            {
                EnableUpload = _features.EnableUpload && _uploads.Enabled,
                EnableDownload = _features.EnableDownload,
                EnableSearch = _features.EnableSearch && _search.Enabled,
                EnableCreateFolder = _features.EnableCreateFolder,
                EnableDelete = _features.EnableDelete,
                EnableMove = _features.EnableMove,
                EnableCopy = _features.EnableCopy,
                EnableDialogMode = _features.EnableDialogMode,
                EnableGenerateTestData = _features.EnableGenerateTestData
            },
            Browsing = new BrowsingClientConfig
            {
                DefaultPageSize = _browsing.DefaultPageSize,
                MaxPageSize = _browsing.MaxPageSize,
                DefaultSort = _browsing.DefaultSort
            },
            Search = new SearchClientConfig
            {
                Enabled = _features.EnableSearch && _search.Enabled,
                MaxResults = _search.MaxResults
            },
            Uploads = new UploadsClientConfig
            {
                Enabled = _features.EnableUpload && _uploads.Enabled,
                MaxFileSizeGB = _uploads.MaxFileSizeGB,
                ChunkSizeMB = _uploads.ChunkSizeMB,
                MaxRetries = _uploads.MaxRetries,
                RetryInitialDelaySeconds = _uploads.RetryInitialDelaySeconds,
                ResumeEnabled = _uploads.ResumeEnabled,
                VerifyChunkChecksum = _uploads.VerifyChunkChecksum
            }
        });
    }
}
