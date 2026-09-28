using ElohimShop.Application.Platform;
using Microsoft.AspNetCore.Mvc;

namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/tiendas")]
public class TiendasController : V1ControllerBase
{
    private readonly IPlatformService _platformService;

    public TiendasController(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var tiendas = await _platformService.ListarTiendasAsync(cancellationToken);
        return Ok(tiendas);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearTiendaRequest request, CancellationToken cancellationToken)
    {
        var tienda = await _platformService.CrearTiendaAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ValidarSlug), new { slug = tienda.Slug }, tienda);
    }

    [HttpGet("valida-slug/{slug}")]
    public async Task<IActionResult> ValidarSlug(string slug, CancellationToken cancellationToken)
    {
        var disponible = await _platformService.SlugDisponibleAsync(slug, cancellationToken);
        return Ok(new { slug, disponible });
    }

    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> ObtenerPorIdOSlug(string idOrSlug, CancellationToken cancellationToken)
    {
        var tienda = await _platformService.ObtenerTiendaPorIdOSlugAsync(idOrSlug, cancellationToken);
        if (tienda == null)
        {
            return NotFound(new { error = "Tienda no encontrada." });
        }
        return Ok(tienda);
    }

    [HttpPut("actualizar")]
    public async Task<IActionResult> Actualizar([FromBody] ActualizarTiendaRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        try
        {
            var tienda = await _platformService.ActualizarTiendaAsync(request, cancellationToken);
            return Ok(tienda);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("configuracion-visual")]
    public async Task<IActionResult> ActualizarConfiguracionVisual([FromBody] ActualizarConfiguracionVisualRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var tienda = await _platformService.ActualizarConfiguracionVisualAsync(request, cancellationToken);
        return Ok(tienda);
    }

    [HttpPost("configuracion/draft")]
    public async Task<IActionResult> GuardarConfiguracionDraft(
        [FromBody] GuardarConfiguracionDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (GetTenantId() is null) return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        try
        {
            return Ok(await _platformService.GuardarConfiguracionDraftAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("configuracion/historial")]
    public async Task<IActionResult> ObtenerHistorialConfiguracion(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (GetTenantId() is null) return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        try
        {
            return Ok(await _platformService.ObtenerHistorialConfiguracionAsync(limit, offset, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("configuracion/restore/{version:int}")]
    public async Task<IActionResult> RestaurarConfiguracion(int version, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null) return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        try
        {
            return Ok(await _platformService.RestaurarConfiguracionVersionAsync(version, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("integraciones")]
    public async Task<IActionResult> GuardarIntegraciones([FromBody] GuardarIntegracionesRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var tienda = await _platformService.GuardarIntegracionesAsync(request, cancellationToken);
        return Ok(tienda);
    }

    [HttpGet("integraciones")]
    public async Task<IActionResult> ObtenerIntegraciones(CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var integraciones = await _platformService.ObtenerIntegracionesAsync(cancellationToken);
        return Ok(integraciones);
    }
}