using ElohimShop.Application.Platform;
using Microsoft.AspNetCore.Mvc;

namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/reportes")]
public class ReportesV1Controller : V1ControllerBase
{
    private readonly IPlatformService _platformService;

    public ReportesV1Controller(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpPost("ejecutar-raw")]
    public async Task<IActionResult> EjecutarRaw([FromBody] EjecutarRawReporteRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        try
        {
            var resultado = await _platformService.EjecutarRawReporteAsync(request, cancellationToken);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error al ejecutar la consulta: " + ex.Message });
        }
    }

    [HttpPost("guardar")]
    public async Task<IActionResult> Guardar([FromBody] GuardarReporteRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        var reporte = await _platformService.GuardarReporteAsync(request, GetUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, reporte);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        return Ok(await _platformService.ListarReportesAsync(cancellationToken));
    }

    [HttpGet("{id}/correr")]
    public async Task<IActionResult> Correr(string id, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        return Ok(await _platformService.CorrerReporteAsync(id, cancellationToken));
    }

    [HttpGet("productos")]
    public async Task<IActionResult> Productos(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? modo,
        [FromServices] ElohimShop.Application.Reportes.IReportesService reportesService,
        CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        var resultado = await reportesService.ObtenerProductosAsync(
            new ElohimShop.Application.Reportes.ReportesFiltroDto(desde, hasta, modo ?? "todos"),
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? modo,
        [FromServices] ElohimShop.Application.Reportes.IReportesService reportesService,
        CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        var resultado = await reportesService.ObtenerEmpleadosAsync(
            new ElohimShop.Application.Reportes.ReportesFiltroDto(desde, hasta, modo ?? "todos"),
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("metodos-pago")]
    public async Task<IActionResult> MetodosPago(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? modo,
        [FromServices] ElohimShop.Application.Reportes.IReportesService reportesService,
        CancellationToken cancellationToken)
    {
        if (GetTenantId() is null || !EsStaff())
        {
            return Forbid();
        }

        var resultado = await reportesService.ObtenerMetodosPagoAsync(
            new ElohimShop.Application.Reportes.ReportesFiltroDto(desde, hasta, modo ?? "todos"),
            cancellationToken);

        return Ok(resultado);
    }
}