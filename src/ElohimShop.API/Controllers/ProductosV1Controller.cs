using ElohimShop.Application.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/productos")]
public class ProductosV1Controller : V1ControllerBase
{
    private readonly IPlatformService _platformService;

    public ProductosV1Controller(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var productos = await _platformService.ListarProductosAsync(cancellationToken);
        return Ok(productos);
    }

    [HttpGet("categorias")]
    public async Task<IActionResult> ObtenerCategorias(
        [FromServices] ElohimShop.Infrastructure.Persistence.PlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var categorias = await dbContext.Categorias
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new
            {
                id = c.Id,
                nombreCategoria = c.Nombre,
                descripcion = c.Descripcion
            })
            .ToListAsync(cancellationToken);

        return Ok(categorias);
    }


    [HttpGet("{id}")]
    public async Task<IActionResult> Obtener(string id, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var producto = await _platformService.ObtenerProductoAsync(id, cancellationToken);
        return producto is null ? NotFound(new { error = "Producto no encontrado." }) : Ok(producto);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearProductoRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var producto = await _platformService.CrearProductoAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, producto);
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> CrearBulk([FromBody] IReadOnlyCollection<CrearProductoBulkInput> requests, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var productos = await _platformService.CrearProductosBulkAsync(requests, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, productos);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(string id, [FromBody] ActualizarProductoRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var producto = await _platformService.ActualizarProductoAsync(id, request, cancellationToken);
        return producto is null ? NotFound(new { error = "Producto no encontrado." }) : Ok(producto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Eliminar(string id, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID." });
        }

        var eliminado = await _platformService.EliminarProductoAsync(id, cancellationToken);
        return eliminado ? NoContent() : NotFound(new { error = "Producto no encontrado." });
    }
}