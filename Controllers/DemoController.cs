using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

/// <summary>
/// Creates the demonstration file tree used to exercise browsing, paging, and search.
/// </summary>
[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly DemoDataService _demo;
    private readonly FeaturesOptions _features;

    public DemoController(DemoDataService demo, IOptions<FeaturesOptions> features)
    {
        _demo = demo;
        _features = features.Value;
    }

    /// <summary>
    /// Creates missing demo folders and files. Existing entries are not overwritten.
    /// </summary>
    [HttpPost("seed")]
    public async Task<ActionResult<DemoSeedResult>> Seed(CancellationToken cancellationToken)
    {
        if (!_features.EnableGenerateTestData)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Generate test data is disabled.",
                "feature_disabled");
        }

        var result = await _demo.SeedAsync(cancellationToken);
        return Ok(result);
    }
}
