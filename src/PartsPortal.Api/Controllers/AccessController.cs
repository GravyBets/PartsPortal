using Microsoft.AspNetCore.Mvc;
using PartsPortal.Api.Security;
using PartsPortal.Contracts.Access;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController : ControllerBase
{
    private readonly AccessRepository _access;

    public AccessController(AccessRepository access)
    {
        _access = access;
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserAccessDto>> Me(
        CancellationToken cancellationToken = default)
    {
        var employeeId = Request.Headers[AccessRepository.UserHeaderName]
            .FirstOrDefault()?
            .Trim();

        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return BadRequest(
                $"Required header '{AccessRepository.UserHeaderName}' was not supplied.");
        }

        return Ok(
            await _access.GetCurrentAsync(
                employeeId,
                cancellationToken));
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserAccessDto>>> Users(
        CancellationToken cancellationToken = default)
    {
        var employeeId = Request.Headers[AccessRepository.UserHeaderName]
            .FirstOrDefault()?
            .Trim();

        if (string.IsNullOrWhiteSpace(employeeId) ||
            !await _access.CanManageUsersAsync(
                employeeId,
                cancellationToken))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Administrator access is required.");
        }

        return Ok(
            await _access.GetUsersAsync(cancellationToken));
    }

    [HttpDelete("users/{employeeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUser(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var currentEmployeeId =
            Request.Headers[AccessRepository.UserHeaderName]
                .FirstOrDefault()?
                .Trim();

        if (string.IsNullOrWhiteSpace(currentEmployeeId) ||
            !await _access.CanManageUsersAsync(
                currentEmployeeId,
                cancellationToken))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Administrator access is required.");
        }

        try
        {
            var deleted = await _access.DeleteUserAsync(
                employeeId,
                cancellationToken);

            return deleted ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPut("users/{employeeId}")]
    public async Task<ActionResult<UserAccessDto>> SaveUser(
        string employeeId,
        SaveUserAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentEmployeeId =
            Request.Headers[AccessRepository.UserHeaderName]
                .FirstOrDefault()?
                .Trim();

        if (string.IsNullOrWhiteSpace(currentEmployeeId) ||
            !await _access.CanManageUsersAsync(
                currentEmployeeId,
                cancellationToken))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Administrator access is required.");
        }

        if (!string.Equals(
                employeeId?.Trim(),
                request.EmployeeId?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                "Route employee ID must match request employee ID.");
        }

        try
        {
            return Ok(
                await _access.SaveUserAsync(
                    request,
                    cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}
