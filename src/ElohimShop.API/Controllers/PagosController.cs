using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ElohimShop.Application.Pagos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace ElohimShop.API.Controllers;

/// <summary>
/// Pagos con Stripe: creación de PaymentIntent, consulta de estado y webhook para marcar reservas como pagadas.
/// </summary>
[ApiController]
[Route("api/v1/pagos")]
public class PagosController : ControllerBase

{
    private readonly IPagosService _pagosService;
    private readonly IStripeWebhookHandler _stripeWebhookHandler;

    /// <summary>Inicializa el controlador de pagos.</summary>
    public PagosController(IPagosService pagosService, IStripeWebhookHandler stripeWebhookHandler)
    {
        _pagosService = pagosService;
        _stripeWebhookHandler = stripeWebhookHandler;
    }

    /// <summary>
    /// Webhook de Stripe (sin JWT). Cuerpo raw JSON + header Stripe-Signature.
    /// Sincroniza <c>pagado</c> en la reservación ante <c>payment_intent.succeeded</c>.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> WebhookStripe(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        string json;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        var firma = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(firma))
        {
            return BadRequest(new { error = "Falta el header Stripe-Signature." });
        }

        try
        {
            await _stripeWebhookHandler
                .ProcesarEventoRawAsync(json, firma, cancellationToken)
                .ConfigureAwait(false);
            return Ok(new { recibido = true });
        }
        catch (StripeException ex)
        {
            return BadRequest(new { error = ex.Message ?? "Firma de webhook inválida." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>Crea un PaymentIntent para una reservación del cliente autenticado.</summary>
    [HttpPost("create-intent")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentIntentCreadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CrearIntent([FromBody] CrearPaymentIntentDto dto, CancellationToken cancellationToken)
    {
        var tipoUsuario = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        if (tipoUsuario != "cliente")
        {
            return Forbid();
        }

        var clienteId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(clienteId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        try
        {
            var resultado = await _pagosService.CrearPaymentIntentAsync(clienteId, dto, cancellationToken).ConfigureAwait(false);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Devuelve el estado del pago en Stripe. Si el estado es <c>succeeded</c> y la reserva aún no está marcada como pagada,
    /// actualiza la base de datos (respaldo ante retrasos del webhook).
    /// </summary>
    [HttpGet("{paymentIntentId}/status")]
    [Authorize]
    [ProducesResponseType(typeof(PagoEstadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Estado(string paymentIntentId, CancellationToken cancellationToken)
    {
        var tipoUsuario = User.FindFirstValue("tipo_usuario") ?? User.FindFirstValue("tipoUsuario");
        var esAdministrador = tipoUsuario == "administrador";

        var clienteId = esAdministrador
            ? null
            : User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!esAdministrador && string.IsNullOrWhiteSpace(clienteId))
        {
            return Unauthorized(new { error = "Token inválido." });
        }

        try
        {
            var estado = await _pagosService
                .ObtenerEstadoPagoAsync(paymentIntentId, clienteId, esAdministrador, cancellationToken)
                .ConfigureAwait(false);

            if (estado is null)
            {
                return NotFound(new { error = "Pago no encontrado." });
            }

            return Ok(estado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }
}
