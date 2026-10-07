using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PartsPortal.Api.Options;
using PartsPortal.Contracts.Versioning;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly IOptions<ClientVersionOptions> _versionOptions;

    public SystemController(IOptions<ClientVersionOptions> versionOptions)
    {
        _versionOptions = versionOptions;
    }

    [HttpGet("client-version")]
    [ProducesResponseType<ClientVersionInfoDto>(StatusCodes.Status200OK)]
    public ActionResult<ClientVersionInfoDto> GetClientVersion()
    {
        var options = _versionOptions.Value;

        return Ok(new ClientVersionInfoDto(
            options.LatestVersion,
            options.MinimumSupportedVersion,
            options.DownloadUrl,
            options.ReleaseNotes));
    }
}
