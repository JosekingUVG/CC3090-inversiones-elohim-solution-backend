using ElohimShop.Application.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/usuarios")]
public class UsuariosV1Controller : V1ControllerBase
{
    private readonly IPlatformService _platformService;

    public UsuariosV1Controller(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para ver los usuarios." });
        }

        var usuarios = await _platformService.ListarUsuariosAsync(cancellationToken);
        return Ok(usuarios);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Obtener(string id, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para ver este usuario." });
        }

        var usuario = await _platformService.ObtenerUsuarioAsync(id, cancellationToken);
        return usuario is null ? NotFound(new { error = "Usuario no encontrado." }) : Ok(usuario);
    }

    [HttpPost("invitar")]
    public async Task<IActionResult> Invitar([FromBody] InvitarPlatformUsuarioRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para invitar colaboradores." });
        }

        try
        {
            var usuario = await _platformService.InvitarUsuarioAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, usuario);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/rol")]
    public async Task<IActionResult> CambiarRol(string id, [FromBody] CambiarRolPlatformUsuarioRequest request, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para cambiar roles." });
        }

        var usuario = await _platformService.CambiarRolUsuarioAsync(id, request, cancellationToken);
        return usuario is null ? NotFound(new { error = "Usuario no encontrado." }) : Ok(usuario);
    }

    [HttpPut("{id}/estado")]
    public async Task<IActionResult> CambiarEstado(string id, [FromBody] bool activo, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para cambiar el estado del usuario." });
        }

        var usuario = await _platformService.CambiarEstadoUsuarioAsync(id, activo, cancellationToken);
        return usuario is null ? NotFound(new { error = "Usuario no encontrado." }) : Ok(usuario);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Eliminar(string id, CancellationToken cancellationToken)
    {
        if (GetTenantId() is null)
        {
            return BadRequest(new { error = "Se requiere el header X-Tenant-ID o estar autenticado." });
        }

        if (!EsAdministrador())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permisos para eliminar colaboradores." });
        }

        var eliminado = await _platformService.EliminarUsuarioAsync(id, cancellationToken);
        return eliminado ? NoContent() : NotFound(new { error = "Usuario no encontrado." });
    }

    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerarCodigosRecuperacion(
        string id,
        [FromServices] ElohimShop.Infrastructure.Persistence.ElohimShopDbContext dbContext,
        [FromServices] ElohimShop.Infrastructure.Persistence.PlatformDbContext platformDbContext,
        CancellationToken cancellationToken)
    {
        if (!EsAdministrador())
        {
            return Forbid();
        }

        var platformUser = await platformDbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (platformUser is null)
        {
            return NotFound(new { error = "Usuario no encontrado en la plataforma." });
        }

        var elohimUser = await dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Correo == platformUser.Email.Trim().ToLower(), cancellationToken);

        if (elohimUser is null)
        {
            var account = await platformDbContext.Accounts
                .FirstOrDefaultAsync(a => a.UserId == platformUser.Id && a.ProviderId == "credential", cancellationToken);
            
            var passwordHash = account?.Password ?? string.Empty;

            if (string.Equals(platformUser.TipoUsuario, "cliente", StringComparison.OrdinalIgnoreCase))
            {
                elohimUser = ElohimShop.Domain.Entities.Usuario.CrearCliente(
                    platformUser.Email,
                    platformUser.Name,
                    passwordHash,
                    "particular",
                    telefono: platformUser.Telefono);
            }
            else
            {
                elohimUser = ElohimShop.Domain.Entities.Usuario.CrearAdministrador(
                    platformUser.Email,
                    platformUser.Name,
                    passwordHash,
                    platformUser.RolStaff ?? "cajero",
                    telefono: platformUser.Telefono);
            }

            dbContext.Usuarios.Add(elohimUser);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var codigosAnteriores = await dbContext.CodigosRecuperacion
            .Where(c => c.UsuarioId == elohimUser.Id && !c.Usado)
            .ToListAsync(cancellationToken);

        foreach (var cod in codigosAnteriores)
        {
            cod.Consumir();
        }

        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codigosPlanos = new List<string>();

        for (int i = 0; i < 8; i++)
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);
            var codigo = new System.Text.StringBuilder();
            foreach (var b in bytes)
            {
                codigo.Append(chars[b % chars.Length]);
            }
            var codigoPlano = codigo.ToString();
            codigosPlanos.Add(codigoPlano);

            var hash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(codigoPlano)));

            dbContext.CodigosRecuperacion.Add(
                ElohimShop.Domain.Entities.CodigoRecuperacion.Crear(elohimUser.Id, hash, diasValidez: 365));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            usuarioId = id,
            correo = elohimUser.Correo,
            nombre = elohimUser.Nombre,
            codigos = codigosPlanos
        });
    }
}

