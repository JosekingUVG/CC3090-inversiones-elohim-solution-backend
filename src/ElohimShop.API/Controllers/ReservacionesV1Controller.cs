using ElohimShop.Application.Platform;
using Microsoft.AspNetCore.Mvc;

namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/reservaciones")]
public class ReservacionesV1Controller : V1ControllerBase
{
    private readonly IPlatformService _platformService;

    public ReservacionesV1Controller(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearReservacionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId) || GetTenantId() is null)
        {
            return Unauthorized(new { error = "Se requiere autenticación y tenant." });
        }

        var reservacion = await _platformService.CrearReservacionAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, reservacion);
    }

    [HttpGet("mis-compras")]
    public async Task<IActionResult> MisCompras(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId) || GetTenantId() is null)
        {
            return Unauthorized(new { error = "Se requiere autenticación y tenant." });
        }

        return Ok(await _platformService.ObtenerMisComprasAsync(userId, cancellationToken));
    }

    [HttpGet("control-staff")]
    public async Task<IActionResult> ControlStaff(CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        return Ok(await _platformService.ObtenerReservacionesStaffAsync(cancellationToken));
    }

    [HttpPatch("{id}/estado")]
    public async Task<IActionResult> CambiarEstado(string id, [FromBody] CambiarEstadoReservacionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId) || GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        try
        {
            var reservacion = await _platformService.CambiarEstadoReservacionAsync(id, request, userId, cancellationToken);
            return reservacion is null ? NotFound(new { error = "Reservación no encontrada." }) : Ok(reservacion);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}