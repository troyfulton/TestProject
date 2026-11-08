using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly SearchService _search;
    private readonly FeaturesOptions _features;

    public SearchController(SearchService search, IOptions<FeaturesOptions> features)
    {
        _search = search;
        _features = features.Value;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q,
        [FromQuery] string? path,
        [FromQuery] bool? recursive,
        [FromQuery] int? maxResults,
        CancellationToken cancellationToken)
    {
        if (!_features.EnableSearch)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Search is disabled.",
                "feature_disabled");
        }

        var result = await _search.SearchAsync(q, path, recursive, maxResults, cancellationToken);
        return Ok(result);
    }
}
