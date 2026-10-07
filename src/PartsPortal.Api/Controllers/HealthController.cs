using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Contracts.Health;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiHealthDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiHealthDto>> Get(CancellationToken cancellationToken)
    {
        var db = HttpContext.RequestServices.GetService<PartsPortalDbContext>();
        var databaseStatus = "not-configured";

        if (db is not null)
        {
            try
            {
                databaseStatus = await db.Database.CanConnectAsync(cancellationToken)
                    ? "reachable"
                    : "unreachable";
            }
            catch
            {
                databaseStatus = "unreachable";
            }
        }

        return Ok(new ApiHealthDto(
            "PartsPortal.Api",
            "healthy",
            DateTimeOffset.UtcNow,
            databaseStatus));
    }
}
