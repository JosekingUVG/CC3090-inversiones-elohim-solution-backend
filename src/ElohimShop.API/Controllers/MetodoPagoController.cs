using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ElohimShop.Application.Pagos;
using ElohimShop.Infrastructure.Pagos;
using ElohimShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElohimShop.API.Controllers;

[ApiController]
[Route("api/v1/metodos-pago")]
[Authorize]
public class MetodoPagoController : V1ControllerBase

{
    private readonly IMetodosPagoUsuarioService _metodosPago;
    private readonly PlatformDbContext _platformDb;
    private readonly StripePaymentOptions _stripeOptions;

    /// <summary>Con MapInboundClaims=false el subject viene como <c>sub</c>, no como NameIdentifier.</summary>
    private string? ClienteIdActual() =>
        User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    public MetodoPagoController(
        IMetodosPagoUsuarioService metodosPago,
        PlatformDbContext platformDb,
        IOptions<StripePaymentOptions> stripeOptions)
    {
        _metodosPago = metodosPago;
        _platformDb = platformDb;
        _stripeOptions = stripeOptions.Value;
    }

    /// <summary>Clave publicable y moneda para Stripe.js (usuario autenticado).</summary>
    [HttpGet("config-stripe")]
    [ProducesResponseType(typeof(ConfigStripeClienteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<ConfigStripeClienteDto> ConfigStripe()
    {
        var tenantId = GetTenantId();
        string? publishableKey = null;

        if (!string.IsNullOrEmpty(tenantId))
        {
            var creds = _platformDb.CredencialesIntegraciones
                .AsNoTracking()
                .FirstOrDefault(x => x.TiendaId == tenantId);
            if (creds != null && !string.IsNullOrEmpty(creds.StripePublicKey))
            {
                publishableKey = creds.StripePublicKey.Trim();
            }
        }

        if (string.IsNullOrEmpty(publishableKey))
        {
            publishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY")?.Trim()
                ?? _stripeOptions.PublishableKey?.Trim();
        }

        if (string.IsNullOrEmpty(publishableKey))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe no está configurado para el cliente (clave publicable)." });
        }

        var moneda = (Environment.GetEnvironmentVariable("STRIPE_DEFAULT_CURRENCY")?.Trim()
            ?? _stripeOptions.DefaultCurrency
            ?? "gtq").ToLowerInvariant();

        return Ok(new ConfigStripeClienteDto
        {
            PublishableKey = publishableKey,
            DefaultCurrency = moneda
        });
    }

    /// <summary>Garantiza un método de pago interno sin Stripe para reservas pagadas al retiro.</summary>
    [HttpPost("contra-entrega")]
    [ProducesResponseType(typeof(MetodoPagoGuardadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AsegurarContraEntrega(CancellationToken cancellationToken)
    {
        var tipo = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        if (tipo != "cliente")
        {
            return Forbid();
        }

        var usuarioId = ClienteIdActual();
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        var creado = await _metodosPago.AsegurarContraEntregaAsync(usuarioId, cancellationToken).ConfigureAwait(false);
        return Ok(creado);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MetodoPagoGuardadoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var tipo = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        if (tipo != "cliente")
        {
            return Forbid();
        }

        var usuarioId = ClienteIdActual();
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        var lista = await _metodosPago.ListarAsync(usuarioId, cancellationToken).ConfigureAwait(false);
        return Ok(lista);
    }

    [HttpPost]
    [ProducesResponseType(typeof(MetodoPagoGuardadoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Guardar([FromBody] GuardarMetodoPagoDto dto, CancellationToken cancellationToken)
    {
        var tipo = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        if (tipo != "cliente")
        {
            return Forbid();
        }

        var usuarioId = ClienteIdActual();
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        try
        {
            var creado = await _metodosPago.GuardarAsync(usuarioId, dto, cancellationToken).ConfigureAwait(false);
            return StatusCode(StatusCodes.Status201Created, creado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ya está guardado"))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Eliminar(string id, CancellationToken cancellationToken)
    {
        var tipo = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        if (tipo != "cliente")
        {
            return Forbid();
        }

        var usuarioId = ClienteIdActual();
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        try
        {
            await _metodosPago.EliminarAsync(usuarioId, id, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "Método de pago no encontrado." });
        }
    }
}
