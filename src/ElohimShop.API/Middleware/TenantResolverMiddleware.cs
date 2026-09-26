using System.Security.Claims;
using System.Threading.Tasks;
using ElohimShop.Application.Admin;
using ElohimShop.Application.Common;
using ElohimShop.Infrastructure.Persistence;
using ElohimShop.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;


namespace ElohimShop.API.Middleware;

public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, PlatformDbContext dbContext, IConfiguration configuration)
    {
        string? candidateTenantId = null;
        string? candidateSlug = null;

        // 1. Extraer identificador de tenant por cabeceras o subdominio
        if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantIdHeader) && !string.IsNullOrWhiteSpace(tenantIdHeader))
        {
            candidateTenantId = tenantIdHeader.ToString().Trim();
        }
        else if (context.Request.Headers.TryGetValue("X-Tenant-Slug", out var slugHeader) && !string.IsNullOrWhiteSpace(slugHeader))
        {
            candidateSlug = slugHeader.ToString().Trim().ToLowerInvariant();
        }
        else
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length > 2)
            {
                var subdomain = parts[0].ToLowerInvariant();
                if (subdomain is not ("www" or "api" or "admin" or "localhost"))
                {
                    candidateSlug = subdomain;
                }
            }
            else if (parts.Length == 2 && parts[1] == "localhost")
            {
                var subdomain = parts[0].ToLowerInvariant();
                if (subdomain is not ("www" or "api" or "admin"))
                {
                    candidateSlug = subdomain;
                }
            }
        }

        // 2. Resolver la entidad Tienda en PlatformDbContext
        ElohimShop.Domain.Platform.Tienda? tienda = null;

        if (!string.IsNullOrEmpty(candidateTenantId))
        {
            tienda = await dbContext.Tiendas
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == candidateTenantId);
        }
        else if (!string.IsNullOrEmpty(candidateSlug))
        {
            tienda = await dbContext.Tiendas
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == candidateSlug);
        }

        // Si se especificó una tienda en el request pero no se encuentra en BD o está inactiva
        if ((!string.IsNullOrEmpty(candidateTenantId) || !string.IsNullOrEmpty(candidateSlug)) && tienda is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"La tienda solicitada no existe o no está disponible.\"}");
            return;
        }

        if (tienda is not null && string.Equals(tienda.Estado, "inactivo", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"La tienda solicitada se encuentra inactiva.\"}");
            return;
        }

        // 3. Cross-Tenant OAuth Guard (Verificación de aislamiento para usuarios autenticados)
        if (tienda is not null && context.User.Identity?.IsAuthenticated == true)
        {
            var userTenantId = context.User.FindFirstValue("tienda_id");
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue("email");
            var rol = context.User.FindFirstValue("rol") ?? context.User.FindFirstValue("rol_staff");
            var esSuperAdminClaim = context.User.FindFirstValue("es_super_admin");

            var isSuperAdmin = string.Equals(rol, "superadmin", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(esSuperAdminClaim, "true", StringComparison.OrdinalIgnoreCase) ||
                               SuperAdminHelper.IsSuperAdminEmail(email, configuration["SuperAdmin:Email"]);

            if (!isSuperAdmin && !string.IsNullOrEmpty(userTenantId) && !string.Equals(userTenantId, tienda.Id, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"Acceso denegado: el token de autenticación no corresponde a la tienda o tenant solicitado.\"}");
                return;
            }

            var tenantContext = new TenantContext
            {
                TenantId = tienda.Id,
                Slug = tienda.Slug,
                Nombre = tienda.Nombre,
                IsSuperAdminOverride = isSuperAdmin
            };
            context.Items["TenantContext"] = tenantContext;
            context.Items["ResolvedTenantId"] = tienda.Id;
        }
        else if (tienda is not null)
        {
            var tenantContext = new TenantContext
            {
                TenantId = tienda.Id,
                Slug = tienda.Slug,
                Nombre = tienda.Nombre,
                IsSuperAdminOverride = false
            };
            context.Items["TenantContext"] = tenantContext;
            context.Items["ResolvedTenantId"] = tienda.Id;
        }

        await _next(context);
    }
}
