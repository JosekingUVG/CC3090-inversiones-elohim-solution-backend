using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using ElohimShop.Application.Admin;
using ElohimShop.Infrastructure.Persistence;
using ElohimShop.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ElohimShop.Infrastructure.Auth;

public class BetterAuthSessionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public BetterAuthSessionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, PlatformDbContext dbContext)
    {
        string? token = null;

        // 1. Try to get token from Authorization header (Bearer token)
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Bearer ".Length).Trim();
        }

        // 2. Try to get token from Cookies if not found in headers
        if (string.IsNullOrWhiteSpace(token))
        {
            if (context.Request.Cookies.TryGetValue("better-auth.session_token", out var cookieToken))
            {
                token = cookieToken;
            }
            else if (context.Request.Cookies.TryGetValue("__secure-better-auth.session_token", out var secureCookieToken))
            {
                token = secureCookieToken;
            }
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            // Query DB ignoring filters to find the session and its user
            var session = await dbContext.Sessions
                .IgnoreQueryFilters()
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Token == token);

            if (session != null && session.ExpiresAt > DateTime.UtcNow && session.User != null)
            {
                var user = session.User;
                string? effectiveTiendaId = user.TiendaId;

                // Validate tenant access
                string? requestedTenantId = null;
                if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantIdHeader) && !string.IsNullOrWhiteSpace(tenantIdHeader))
                {
                    requestedTenantId = tenantIdHeader.ToString();
                }
                else if (context.Request.Headers.TryGetValue("X-Tenant-Slug", out var slugHeader) && !string.IsNullOrWhiteSpace(slugHeader))
                {
                    var requestedSlug = slugHeader.ToString().Trim();
                    var requestedStore = await dbContext.Tiendas
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Slug == requestedSlug, cancellationToken: CancellationToken.None);

                    requestedTenantId = requestedStore?.Id;
                }

                if (!string.IsNullOrWhiteSpace(requestedTenantId))
                {
                    var esSuperAdmin = string.Equals(user.RolStaff, "superadmin", StringComparison.OrdinalIgnoreCase) ||
                                       SuperAdminHelper.IsSuperAdminEmail(user.Email, _configuration["SuperAdmin:Email"]);

                    if (!string.Equals(user.TiendaId, requestedTenantId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (user.TipoUsuario == "cliente")
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsJsonAsync(new { error = "No tienes acceso a esta tienda." });
                            return;
                        }

                        // Check if they have a user record in the target store
                        var targetUser = await dbContext.Users
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(u => u.Email == user.Email && u.TiendaId == requestedTenantId);

                        if (targetUser != null)
                        {
                            user = targetUser;
                            effectiveTiendaId = targetUser.TiendaId;
                        }
                        else
                        {
                            if (!esSuperAdmin)
                            {
                                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsJsonAsync(new { error = "No tienes acceso a esta tienda." });
                                return;
                            }

                            effectiveTiendaId = requestedTenantId;
                        }
                    }
                }

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id),
                    new(JwtRegisteredClaimNames.Sub, user.Id),
                    new(ClaimTypes.Email, user.Email),
                    new(ClaimTypes.Name, user.Name),
                    new("tienda_id", effectiveTiendaId ?? string.Empty),
                    new("tipo_usuario", user.TipoUsuario),
                    new("tipoUsuario", user.TipoUsuario)
                };

                if (user.TipoUsuario == "staff")
                {
                    var rol = user.RolStaff ?? "cajero";
                    claims.Add(new Claim("rol", rol));
                    claims.Add(new Claim("rol_staff", rol));
                    claims.Add(new Claim(ClaimTypes.Role, rol));
                }
                else if (user.TipoUsuario == "cliente")
                {
                    claims.Add(new Claim("rol", "cliente"));
                    claims.Add(new Claim(ClaimTypes.Role, "cliente"));
                }

                var identity = new ClaimsIdentity(claims, "BetterAuthSession");
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await _next(context);
    }
}
