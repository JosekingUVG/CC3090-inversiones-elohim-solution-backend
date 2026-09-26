using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElohimShop.Application.Platform;
using ElohimShop.Domain.Platform;
using ElohimShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ElohimShop.Infrastructure.Platform;

public class PlatformService : IPlatformService
{
    private static readonly Regex UnsafeSqlPattern = new(@"\b(drop|delete|update|insert|alter|truncate|grant|revoke|create|exec|execute|call)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForbiddenTablesPattern = new(@"\b(session|account|verification|CredencialesIntegracion)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly PlatformDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlatformService(PlatformDbContext dbContext, ITenantProvider tenantProvider, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IReadOnlyList<TiendaDto>> ListarTiendasAsync(CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var email = httpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
        var rol = httpContext?.User?.FindFirst("rol")?.Value ?? httpContext?.User?.FindFirst("rol_staff")?.Value;
        var esSuperAdmin = string.Equals(rol, "superadmin", StringComparison.OrdinalIgnoreCase);

        var query = _dbContext.Tiendas.AsNoTracking();

        if (!esSuperAdmin && !string.IsNullOrWhiteSpace(email))
        {
            var userStores = await _dbContext.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email == email)
                .Select(u => u.TiendaId)
                .Where(tId => tId != null)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(t => userStores.Contains(t.Id));
        }
        else if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<TiendaDto>();
        }

        var tiendas = await query.ToListAsync(cancellationToken);

        var dtos = new List<TiendaDto>();
        foreach (var tienda in tiendas)
        {
            dtos.Add(await MapTiendaAsync(tienda.Id, cancellationToken));
        }
        return dtos;
    }

    public async Task<TiendaDto?> ObtenerTiendaPorIdOSlugAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var normalizedIdOrSlug = idOrSlug.Trim().ToLowerInvariant();
        var tienda = await _dbContext.Tiendas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == idOrSlug || t.Slug == normalizedIdOrSlug, cancellationToken);

        if (tienda == null) return null;

        return await MapTiendaAsync(tienda.Id, cancellationToken);
    }

    public async Task<TiendaDto> CrearTiendaAsync(CrearTiendaRequest request, CancellationToken cancellationToken)
    {
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        if (await _dbContext.Tiendas.IgnoreQueryFilters().AnyAsync(x => x.Slug == normalizedSlug, cancellationToken))
        {
            throw new InvalidOperationException("El slug ya está registrado.");
        }

        var tienda = new Tienda
        {
            Nombre = request.Nombre.Trim(),
            Slug = normalizedSlug,
            ConfiguracionVisual = "{}"
        };

        _dbContext.Tiendas.Add(tienda);
        _dbContext.CredencialesIntegraciones.Add(new CredencialesIntegracion { TiendaId = tienda.Id });

        var httpContext = _httpContextAccessor.HttpContext;
        var email = httpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (existingUser != null)
            {
                var newUser = new ElohimShop.Domain.Platform.User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = existingUser.Name,
                    Email = existingUser.Email,
                    EmailVerified = existingUser.EmailVerified,
                    Image = existingUser.Image,
                    TiendaId = tienda.Id,
                    TipoUsuario = "staff",
                    RolStaff = "administrador",
                    Telefono = existingUser.Telefono,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Estado = true
                };
                _dbContext.Users.Add(newUser);

                var existingAccount = await _dbContext.Accounts
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.UserId == existingUser.Id && a.ProviderId == "credential", cancellationToken);

                if (existingAccount != null)
                {
                    var newAccount = new Account
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = newUser.Id,
                        ProviderId = "credential",
                        AccountId = existingUser.Email,
                        Password = existingAccount.Password,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _dbContext.Accounts.Add(newAccount);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapTiendaAsync(tienda.Id, cancellationToken);
    }

    public async Task<TiendaDto> ActualizarTiendaAsync(ActualizarTiendaRequest request, CancellationToken cancellationToken)
    {
        var tienda = await GetTenantStoreAsync(cancellationToken);

        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        // Si el slug cambió, validar que el nuevo slug sea único en todo el sistema
        if (!string.Equals(tienda.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
        {
            var slugExiste = await _dbContext.Tiendas
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Slug == normalizedSlug, cancellationToken);

            if (slugExiste)
            {
                throw new InvalidOperationException("El slug ya está en uso por otra tienda.");
            }

            tienda.Slug = normalizedSlug;
        }

        tienda.Nombre = request.Nombre.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await MapTiendaAsync(tienda.Id, cancellationToken);
    }

    public Task<bool> SlugDisponibleAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        return _dbContext.Tiendas.IgnoreQueryFilters().AllAsync(x => x.Slug != normalizedSlug, cancellationToken);
    }

    public async Task<TiendaDto> ActualizarConfiguracionVisualAsync(ActualizarConfiguracionVisualRequest request, CancellationToken cancellationToken)
    {
        var tienda = await GetTenantStoreAsync(cancellationToken);
        tienda.ConfiguracionVisual = request.ConfiguracionVisual.GetRawText();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await MapTiendaAsync(tienda.Id, cancellationToken);
    }

    public async Task<TiendaDto> GuardarIntegracionesAsync(GuardarIntegracionesRequest request, CancellationToken cancellationToken)
    {
        var tienda = await GetTenantStoreAsync(cancellationToken);
        var credenciales = await _dbContext.CredencialesIntegraciones.FirstOrDefaultAsync(x => x.TiendaId == tienda.Id, cancellationToken)
            ?? new CredencialesIntegracion { TiendaId = tienda.Id };

        credenciales.StripeSecretKey = request.StripeSecretKey;
        credenciales.StripePublicKey = request.StripePublicKey;
        credenciales.CloudinaryCloudName = request.CloudinaryCloudName;
        credenciales.CloudinaryApiKey = request.CloudinaryApiKey;
        credenciales.CloudinaryApiSecret = request.CloudinaryApiSecret;
        credenciales.SmtpEmail = request.SmtpEmail;
        credenciales.SmtpPassword = request.SmtpPassword;

        if (_dbContext.Entry(credenciales).State == EntityState.Detached)
        {
            _dbContext.CredencialesIntegraciones.Add(credenciales);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await MapTiendaAsync(tienda.Id, cancellationToken);
    }

    public async Task<MediaSignatureResponse> GenerarFirmaMediaAsync(MediaSignatureRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var credenciales = await _dbContext.CredencialesIntegraciones.FirstOrDefaultAsync(x => x.TiendaId == tenantId, cancellationToken);
        if (credenciales is null ||
            string.IsNullOrWhiteSpace(credenciales.CloudinaryApiSecret) ||
            string.IsNullOrWhiteSpace(credenciales.CloudinaryApiKey) ||
            string.IsNullOrWhiteSpace(credenciales.CloudinaryCloudName))
        {
            throw new InvalidOperationException("Las credenciales de Cloudinary no están configuradas.");
        }

        var timestamp = request.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        var parameters = new List<string>
        {
            $"public_id={request.PublicId}",
            $"timestamp={timestamp}"
        };

        if (!string.IsNullOrWhiteSpace(request.Folder))
        {
            parameters.Add($"folder={request.Folder}");
        }

        parameters.Sort();
        var payload = string.Join("&", parameters) + credenciales.CloudinaryApiSecret;
        
        var signatureBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return new MediaSignatureResponse(
            Signature: Convert.ToHexString(signatureBytes).ToLowerInvariant(),
            Timestamp: timestamp,
            ApiKey: credenciales.CloudinaryApiKey,
            CloudName: credenciales.CloudinaryCloudName);
    }

    public Task<bool> EliminarMediaAsync(string publicId, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(publicId));
    }

    public async Task<IReadOnlyList<ProductoDto>> ListarProductosAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Productos
            .AsNoTracking()
            .OrderBy(x => x.Nombre)
            .Select(x => new ProductoDto(
                x.Id,
                x.TiendaId,
                x.CategoriaId,
                x.Nombre,
                x.Descripcion,
                x.Sku,
                x.PrecioMayoreo,
                x.PrecioDetalle,
                x.ImagenUrl,
                x.Publicado,
                x.StockMinimo,
                x.FechaCreacion,
                x.Inventarios.Sum(i => i.Stock),
                x.Inventarios.Select(i => new InventarioDto(
                    i.Id,
                    i.TiendaId,
                    i.SucursalId,
                    i.ProductoId,
                    i.Stock,
                    x.Nombre,
                    i.Sucursal != null ? i.Sucursal.Nombre : null
                )).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductoDto?> ObtenerProductoAsync(string id, CancellationToken cancellationToken)
    {
        return await _dbContext.Productos
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProductoDto(
                x.Id,
                x.TiendaId,
                x.CategoriaId,
                x.Nombre,
                x.Descripcion,
                x.Sku,
                x.PrecioMayoreo,
                x.PrecioDetalle,
                x.ImagenUrl,
                x.Publicado,
                x.StockMinimo,
                x.FechaCreacion,
                x.Inventarios.Sum(i => i.Stock),
                x.Inventarios.Select(i => new InventarioDto(
                    i.Id,
                    i.TiendaId,
                    i.SucursalId,
                    i.ProductoId,
                    i.Stock,
                    x.Nombre,
                    i.Sucursal != null ? i.Sucursal.Nombre : null
                )).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProductoDto> CrearProductoAsync(CrearProductoRequest request, CancellationToken cancellationToken)
    {
        var tiendaId = RequireTenantId();
        var stockActual = request.StockSucursales?.Sum(s => s.Stock) ?? 0;
        var producto = new Producto
        {
            TiendaId = tiendaId,
            CategoriaId = request.CategoriaId,
            Nombre = request.Nombre.Trim(),
            Descripcion = request.Descripcion,
            Sku = request.Sku,
            PrecioMayoreo = request.PrecioMayoreo,
            PrecioDetalle = request.PrecioDetalle,
            ImagenUrl = request.ImagenUrl,
            Publicado = request.Publicado,
            StockActual = stockActual,
            StockMinimo = request.StockMinimo
        };

        _dbContext.Productos.Add(producto);

        if (request.StockSucursales != null)
        {
            foreach (var sStock in request.StockSucursales)
            {
                _dbContext.Inventarios.Add(new Inventario
                {
                    TiendaId = tiendaId,
                    SucursalId = sStock.SucursalId,
                    ProductoId = producto.Id,
                    Stock = sStock.Stock
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ObtenerProductoAsync(producto.Id, cancellationToken) ?? throw new InvalidOperationException("No se pudo crear el producto.");
    }

    public async Task<IReadOnlyList<ProductoDto>> CrearProductosBulkAsync(IReadOnlyCollection<CrearProductoBulkInput> requests, CancellationToken cancellationToken)
    {
        var tiendaId = RequireTenantId();
        var creadosIds = new List<string>();

        // Obtener la primera sucursal para asociar el stock actual
        var primeraSucursal = await _dbContext.Sucursales
            .FirstOrDefaultAsync(s => s.TiendaId == tiendaId, cancellationToken);

        foreach (var req in requests)
        {
            var producto = new Producto
            {
                TiendaId = tiendaId,
                CategoriaId = req.CategoriaId,
                Nombre = req.Nombre.Trim(),
                Descripcion = req.Descripcion,
                Sku = req.Sku,
                PrecioMayoreo = req.PrecioMayoreo,
                PrecioDetalle = req.PrecioDetalle,
                ImagenUrl = req.ImagenUrl,
                Publicado = req.Publicado,
                StockActual = req.StockActual,
                StockMinimo = req.StockMinimo
            };

            _dbContext.Productos.Add(producto);
            creadosIds.Add(producto.Id);

            if (primeraSucursal != null && req.StockActual > 0)
            {
                _dbContext.Inventarios.Add(new Inventario
                {
                    TiendaId = tiendaId,
                    SucursalId = primeraSucursal.Id,
                    ProductoId = producto.Id,
                    Stock = req.StockActual
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Volver a consultar para devolver ProductoDto completo con inventarios y nombres de sucursal
        return await _dbContext.Productos
            .AsNoTracking()
            .Where(x => creadosIds.Contains(x.Id))
            .OrderBy(x => x.Nombre)
            .Select(x => new ProductoDto(
                x.Id,
                x.TiendaId,
                x.CategoriaId,
                x.Nombre,
                x.Descripcion,
                x.Sku,
                x.PrecioMayoreo,
                x.PrecioDetalle,
                x.ImagenUrl,
                x.Publicado,
                x.StockMinimo,
                x.FechaCreacion,
                x.Inventarios.Sum(i => i.Stock),
                x.Inventarios.Select(i => new InventarioDto(
                    i.Id,
                    i.TiendaId,
                    i.SucursalId,
                    i.ProductoId,
                    i.Stock,
                    x.Nombre,
                    i.Sucursal != null ? i.Sucursal.Nombre : null
                )).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductoDto?> ActualizarProductoAsync(string id, ActualizarProductoRequest request, CancellationToken cancellationToken)
    {
        var producto = await _dbContext.Productos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (producto is null)
        {
            return null;
        }

        producto.Nombre = request.Nombre.Trim();
        producto.CategoriaId = request.CategoriaId;
        producto.Descripcion = request.Descripcion;
        producto.Sku = request.Sku;
        producto.PrecioMayoreo = request.PrecioMayoreo;
        producto.PrecioDetalle = request.PrecioDetalle;
        producto.ImagenUrl = request.ImagenUrl;
        producto.Publicado = request.Publicado;
        producto.StockMinimo = request.StockMinimo;

        if (request.StockSucursales != null)
        {
            var existingInventories = await _dbContext.Inventarios
                .Where(x => x.ProductoId == id && x.TiendaId == producto.TiendaId)
                .ToListAsync(cancellationToken);

            foreach (var sStock in request.StockSucursales)
            {
                var existing = existingInventories.FirstOrDefault(x => x.SucursalId == sStock.SucursalId);
                if (existing != null)
                {
                    existing.Stock = sStock.Stock;
                }
                else
                {
                    var newInv = new Inventario
                    {
                        TiendaId = producto.TiendaId,
                        SucursalId = sStock.SucursalId,
                        ProductoId = id,
                        Stock = sStock.Stock
                    };
                    _dbContext.Inventarios.Add(newInv);
                    existingInventories.Add(newInv);
                }
            }

            producto.StockActual = existingInventories.Sum(x => x.Stock);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ObtenerProductoAsync(id, cancellationToken);
    }

    public async Task<bool> EliminarProductoAsync(string id, CancellationToken cancellationToken)
    {
        var producto = await _dbContext.Productos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (producto is null)
        {
            return false;
        }

        producto.Eliminado = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<InventarioDto>> ObtenerInventarioSucursalAsync(string sucursalId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Inventarios
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId && x.SucursalId == sucursalId)
            .Select(x => new InventarioDto(
                x.Id,
                x.TiendaId,
                x.SucursalId,
                x.ProductoId,
                x.Stock,
                x.Producto!.Nombre,
                x.Sucursal!.Nombre))
            .ToListAsync(cancellationToken);
    }

    public async Task<InventarioDto> AjustarInventarioAsync(AjustarInventarioRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var inventario = await _dbContext.Inventarios.FirstOrDefaultAsync(
            x => x.TiendaId == tenantId && x.SucursalId == request.SucursalId && x.ProductoId == request.ProductoId,
            cancellationToken);

        if (inventario is null)
        {
            inventario = new Inventario
            {
                TiendaId = tenantId,
                SucursalId = request.SucursalId,
                ProductoId = request.ProductoId,
                Stock = request.Stock
            };
            _dbContext.Inventarios.Add(inventario);
        }
        else
        {
            inventario.Stock = request.Stock;
        }

        var producto = await _dbContext.Productos.FirstOrDefaultAsync(x => x.Id == request.ProductoId && x.TiendaId == tenantId, cancellationToken);
        if (producto != null)
        {
            var existingInventories = await _dbContext.Inventarios
                .Where(x => x.ProductoId == request.ProductoId && x.TiendaId == tenantId)
                .ToListAsync(cancellationToken);

            var existing = existingInventories.FirstOrDefault(x => x.SucursalId == request.SucursalId);
            if (existing != null)
            {
                existing.Stock = request.Stock;
            }
            else
            {
                existingInventories.Add(inventario);
            }
            producto.StockActual = existingInventories.Sum(x => x.Stock);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new InventarioDto(inventario.Id, inventario.TiendaId, inventario.SucursalId, inventario.ProductoId, inventario.Stock);
    }

    public async Task<IReadOnlyList<CarritoElementoDto>> ObtenerCarritoAsync(string usuarioId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.CarritoElementos
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId && x.UsuarioId == usuarioId)
            .Include(x => x.Producto)
            .OrderByDescending(x => x.FechaAdicion)
            .Select(x => new CarritoElementoDto(
                x.Id,
                x.TiendaId,
                x.UsuarioId,
                x.ProductoId,
                x.Cantidad,
                x.FechaAdicion,
                x.Producto!.Nombre,
                x.Producto!.PrecioDetalle,
                x.Producto!.PrecioDetalle * x.Cantidad))
            .ToListAsync(cancellationToken);
    }

    public async Task<CarritoElementoDto> AgregarArticuloAsync(string usuarioId, AgregarCarritoRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var item = await _dbContext.CarritoElementos.FirstOrDefaultAsync(
            x => x.TiendaId == tenantId && x.UsuarioId == usuarioId && x.ProductoId == request.ProductoId,
            cancellationToken);

        if (item is null)
        {
            item = new CarritoElemento
            {
                TiendaId = tenantId,
                UsuarioId = usuarioId,
                ProductoId = request.ProductoId,
                Cantidad = request.Cantidad,
                FechaAdicion = DateTime.UtcNow
            };
            _dbContext.CarritoElementos.Add(item);
        }
        else
        {
            item.Cantidad += request.Cantidad;
            item.FechaAdicion = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await MapCarritoAsync(item.Id, cancellationToken) ?? throw new InvalidOperationException("No se pudo agregar el artículo al carrito.");
    }

    public async Task<CarritoElementoDto?> ActualizarArticuloAsync(string usuarioId, string id, ActualizarCarritoRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var item = await _dbContext.CarritoElementos.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuarioId && x.TiendaId == tenantId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        item.Cantidad = request.Cantidad;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        return await MapCarritoAsync(item.Id, cancellationToken);
    }

    public async Task<bool> EliminarArticuloAsync(string usuarioId, string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var item = await _dbContext.CarritoElementos.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuarioId && x.TiendaId == tenantId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        _dbContext.CarritoElementos.Remove(item);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Si el artículo ya fue eliminado concurrentemente, consideramos la operación como exitosa.
            return true;
        }
        return true;
    }

    public async Task<CrearIntentoPagoResponse> CrearIntentoPagoAsync(string usuarioId, CrearIntentoPagoRequest request, CancellationToken cancellationToken)
    {
        var carrito = await ObtenerCarritoAsync(usuarioId, cancellationToken);
        if (carrito.Count == 0)
        {
            throw new InvalidOperationException("El carrito está vacío.");
        }

        var montoTotal = carrito.Sum(item => item.Subtotal ?? 0m);
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        return new CrearIntentoPagoResponse(paymentIntentId, $"secret_{paymentIntentId}", montoTotal, "gtq");
    }

    public async Task<ReservacionDto> CrearReservacionAsync(string usuarioId, CrearReservacionRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var carrito = await _dbContext.CarritoElementos
            .Include(x => x.Producto)
            .Where(x => x.TiendaId == tenantId && x.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        if (carrito.Count == 0)
        {
            throw new InvalidOperationException("El carrito está vacío.");
        }

        var reservacion = new Reservacion
        {
            TiendaId = tenantId,
            SucursalId = request.SucursalId,
            UsuarioId = usuarioId,
            MontoTotal = carrito.Sum(item => ObtenerPrecio(item) * item.Cantidad),
            EstadoPago = string.IsNullOrWhiteSpace(request.StripeIntentId) ? "pendiente" : "pagado",
            EstadoDespacho = "procesando",
            StripeIntentId = request.StripeIntentId,
            FechaReserva = DateTime.UtcNow
        };

        foreach (var item in carrito)
        {
            reservacion.Detalles.Add(new DetalleReservacion
            {
                ProductoId = item.ProductoId,
                Cantidad = item.Cantidad,
                PrecioCobrado = ObtenerPrecio(item)
            });
        }

        _dbContext.Reservaciones.Add(reservacion);
        _dbContext.CarritoElementos.RemoveRange(carrito);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await MapReservacionAsync(reservacion.Id, cancellationToken) ?? throw new InvalidOperationException("No se pudo crear la reservación.");
    }

    public async Task<IReadOnlyList<ReservacionDto>> ObtenerMisComprasAsync(string usuarioId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var reservaciones = await _dbContext.Reservaciones
            .AsNoTracking()
            .Include(x => x.Detalles)
            .ThenInclude(x => x.Producto)
            .Where(x => x.TiendaId == tenantId && x.UsuarioId == usuarioId)
            .OrderByDescending(x => x.FechaReserva)
            .ToListAsync(cancellationToken);

        return reservaciones.Select(MapReservacion).ToList();
    }

    public async Task<IReadOnlyList<ReservacionDto>> ObtenerReservacionesStaffAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var reservaciones = await _dbContext.Reservaciones
            .AsNoTracking()
            .Include(x => x.Detalles)
            .ThenInclude(x => x.Producto)
            .Where(x => x.TiendaId == tenantId)
            .OrderByDescending(x => x.FechaReserva)
            .ToListAsync(cancellationToken);

        return reservaciones.Select(MapReservacion).ToList();
    }

    public async Task<ReservacionDto?> CambiarEstadoReservacionAsync(string id, CambiarEstadoReservacionRequest request, string actingUserId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();

        // 1. Validar que el usuario/cajero pertenezca al tenant del pedido
        var actingUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actingUserId, cancellationToken);

        if (actingUser is not null)
        {
            var esSuperAdmin = string.Equals(actingUser.TipoUsuario, "superadmin", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(actingUser.RolStaff, "superadmin", StringComparison.OrdinalIgnoreCase);

            if (!esSuperAdmin && !string.IsNullOrWhiteSpace(actingUser.TiendaId) &&
                !string.Equals(actingUser.TiendaId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("El cajero o usuario no pertenece a la tienda correspondiente a este pedido.");
            }
        }

        var reservacion = await _dbContext.Reservaciones
            .Include(x => x.Detalles)
            .ThenInclude(x => x.Producto)
            .FirstOrDefaultAsync(x => x.Id == id && x.TiendaId == tenantId, cancellationToken);

        if (reservacion is null)
        {
            return null;
        }

        static int ObtenerRangoEstadoPago(string? estado) => estado?.ToLowerInvariant().Trim() switch
        {
            "pendiente" => 1,
            "pagado" => 2,
            "cancelado" => 99,
            _ => 1
        };

        static int ObtenerRangoEstadoDespacho(string? estado) => estado?.ToLowerInvariant().Trim() switch
        {
            "pendiente" or "procesando" => 1,
            "despachado" or "entregado" or "completado" => 2,
            "cancelado" => 99,
            _ => 1
        };

        var nuevoEstadoPago = string.IsNullOrWhiteSpace(request.EstadoPago) ? null : request.EstadoPago.Trim().ToLowerInvariant();
        var nuevoEstadoDespacho = string.IsNullOrWhiteSpace(request.EstadoDespacho) ? null : request.EstadoDespacho.Trim().ToLowerInvariant();

        // 2. Validación Forward-Only (No permitir regresar a estados anteriores)
        if (nuevoEstadoPago is not null && nuevoEstadoPago != "cancelado")
        {
            int rangoActualPago = ObtenerRangoEstadoPago(reservacion.EstadoPago);
            int rangoNuevoPago = ObtenerRangoEstadoPago(nuevoEstadoPago);

            if (rangoNuevoPago < rangoActualPago)
            {
                throw new InvalidOperationException($"No está permitido regresar el estado de pago de '{reservacion.EstadoPago}' a '{nuevoEstadoPago}'. Violación de reglas de negocio.");
            }
        }

        if (nuevoEstadoDespacho is not null && nuevoEstadoDespacho != "cancelado")
        {
            int rangoActualDespacho = ObtenerRangoEstadoDespacho(reservacion.EstadoDespacho);
            int rangoNuevoDespacho = ObtenerRangoEstadoDespacho(nuevoEstadoDespacho);

            if (rangoNuevoDespacho < rangoActualDespacho)
            {
                throw new InvalidOperationException($"No está permitido regresar el estado de despacho de '{reservacion.EstadoDespacho}' a '{nuevoEstadoDespacho}'. Violación de reglas de negocio.");
            }
        }

        // 3. Manejo de Cancelación y Reversión a Inventario / Stock
        bool seCancela = (nuevoEstadoPago == "cancelado" || nuevoEstadoDespacho == "cancelado");
        bool yaEstabaCancelado = (reservacion.EstadoPago == "cancelado" || reservacion.EstadoDespacho == "cancelado");

        if (seCancela && !yaEstabaCancelado)
        {
            foreach (var detail in reservacion.Detalles)
            {
                var inv = await _dbContext.Inventarios.FirstOrDefaultAsync(i =>
                    i.SucursalId == reservacion.SucursalId &&
                    i.ProductoId == detail.ProductoId, cancellationToken);
                if (inv is not null)
                {
                    inv.Stock += detail.Cantidad;
                }

                var prod = await _dbContext.Productos.FirstOrDefaultAsync(p => p.Id == detail.ProductoId, cancellationToken);
                if (prod is not null)
                {
                    prod.StockActual += detail.Cantidad;
                }
            }

            reservacion.EstadoPago = "cancelado";
            reservacion.EstadoDespacho = "cancelado";
        }
        else
        {
            if (nuevoEstadoPago is not null)
            {
                reservacion.EstadoPago = nuevoEstadoPago;
            }

            if (nuevoEstadoDespacho is not null)
            {
                reservacion.EstadoDespacho = nuevoEstadoDespacho;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapReservacion(reservacion);
    }

    public async Task<SqlExecutionResult> EjecutarRawReporteAsync(EjecutarRawReporteRequest request, CancellationToken cancellationToken)
    {
        ValidarSqlSeleccion(request.QuerySql);

        var tenantId = RequireTenantId();
        var querySql = request.QuerySql;

        var mainConnectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No se pudo obtener la cadena de conexión de la base de datos.");

        Npgsql.NpgsqlConnection connection;
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(mainConnectionString)
            {
                Username = "reports_readonly",
                Password = "ReadOnlyPassword123!"
            };
            connection = new Npgsql.NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            // Fallback a conexión principal si reports_readonly no está configurado
            connection = new Npgsql.NpgsqlConnection(mainConnectionString);
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // 1. Inyectar tenant context a nivel de sesión PostgreSQL para forzar Row-Level Security (RLS)
            try
            {
                await using var setTenantCmd = connection.CreateCommand();
                setTenantCmd.CommandText = "SET LOCAL app.current_tenant = @tenantId;";
                setTenantCmd.Parameters.Add(new Npgsql.NpgsqlParameter("tenantId", tenantId));
                await setTenantCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Ignorar si el proveedor no soporta la variable de sesión (ej. SQLite en pruebas)
            }

            // 2. Preparar comando asignando parámetros fuertemente tipados
            await using var command = connection.CreateCommand();
            command.CommandText = querySql;
            command.Parameters.Add(new Npgsql.NpgsqlParameter("tenant_id", tenantId));
            command.Parameters.Add(new Npgsql.NpgsqlParameter("tienda_id", tenantId));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var colName = reader.GetName(i);
                    if (row.ContainsKey(colName))
                    {
                        colName = $"{colName}_{i}";
                    }
                    row[colName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return new SqlExecutionResult(rows);
        }
        catch (Npgsql.PostgresException ex)
        {
            var hint = string.IsNullOrWhiteSpace(ex.Hint) ? "" : $" (Sugerencia: {ex.Hint})";
            var detail = string.IsNullOrWhiteSpace(ex.Detail) ? "" : $" Detalle: {ex.Detail}";
            throw new InvalidOperationException($"Error SQL ({ex.SqlState}): {ex.MessageText}{detail}{hint}");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async Task<ReportePersonalizadoDto> GuardarReporteAsync(GuardarReporteRequest request, string? creadoPor, CancellationToken cancellationToken)
    {
        ValidarSqlSeleccion(request.QuerySql);

        var reporte = new ReportePersonalizado
        {
            TiendaId = RequireTenantId(),
            Nombre = request.Nombre.Trim(),
            Descripcion = request.Descripcion,
            QuerySql = request.QuerySql.Trim(),
            CreadoPor = creadoPor,
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.ReportesPersonalizados.Add(reporte);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapReporte(reporte);
    }

    public async Task<IReadOnlyList<ReportePersonalizadoDto>> ListarReportesAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.ReportesPersonalizados
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId)
            .OrderByDescending(x => x.FechaCreacion)
            .Select(x => new ReportePersonalizadoDto(x.Id, x.TiendaId, x.Nombre, x.Descripcion, x.QuerySql, x.CreadoPor, x.FechaCreacion))
            .ToListAsync(cancellationToken);
    }

    public async Task<SqlExecutionResult> CorrerReporteAsync(string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var reporte = await _dbContext.ReportesPersonalizados.FirstOrDefaultAsync(x => x.Id == id && x.TiendaId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Reporte no encontrado.");

        return await EjecutarRawReporteAsync(new EjecutarRawReporteRequest(reporte.QuerySql), cancellationToken);
    }

    private async Task<TiendaDto> MapTiendaAsync(string tiendaId, CancellationToken cancellationToken)
    {
        var tienda = await _dbContext.Tiendas.AsNoTracking().FirstAsync(x => x.Id == tiendaId, cancellationToken);
        var credenciales = await _dbContext.CredencialesIntegraciones.AsNoTracking().FirstOrDefaultAsync(x => x.TiendaId == tiendaId, cancellationToken);
        return new TiendaDto(
            tienda.Id,
            tienda.Nombre,
            tienda.Slug,
            tienda.Estado,
            tienda.ConfiguracionVisual,
            tienda.FechaCreacion,
            credenciales is null ? null : new CredencialesIntegracionDto(
                credenciales.TiendaId,
                credenciales.StripePublicKey,
                credenciales.CloudinaryCloudName,
                credenciales.CloudinaryApiKey));
    }

    private async Task<CarritoElementoDto?> MapCarritoAsync(string itemId, CancellationToken cancellationToken)
    {
        return await _dbContext.CarritoElementos
            .AsNoTracking()
            .Include(x => x.Producto)
            .Where(x => x.Id == itemId)
            .Select(x => new CarritoElementoDto(
                x.Id,
                x.TiendaId,
                x.UsuarioId,
                x.ProductoId,
                x.Cantidad,
                x.FechaAdicion,
                x.Producto!.Nombre,
                x.Producto!.PrecioDetalle,
                x.Producto!.PrecioDetalle * x.Cantidad))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ReservacionDto MapReservacion(Reservacion reservacion)
    {
        var detalles = reservacion.Detalles
            .Select(x => new DetalleReservacionDto(
                x.Id,
                x.ReservacionId,
                x.ProductoId,
                x.Cantidad,
                x.PrecioCobrado,
                x.Subtotal,
                x.Producto?.Nombre))
            .ToList();

        return new ReservacionDto(
            reservacion.Id,
            reservacion.TiendaId,
            reservacion.SucursalId,
            reservacion.UsuarioId,
            reservacion.MontoTotal,
            reservacion.EstadoPago,
            reservacion.EstadoDespacho,
            reservacion.StripeIntentId,
            reservacion.FechaReserva,
            detalles);
    }

    public async Task<IReadOnlyList<SucursalDto>> ListarSucursalesAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Sucursales
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId)
            .OrderBy(x => x.Nombre)
            .Select(x => new SucursalDto(
                x.Id,
                x.TiendaId,
                x.Nombre,
                x.Direccion,
                x.Telefono,
                x.FechaCreacion))
            .ToListAsync(cancellationToken);
    }

    public async Task<SucursalDto?> ObtenerSucursalAsync(string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Sucursales
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId && x.Id == id)
            .Select(x => new SucursalDto(
                x.Id,
                x.TiendaId,
                x.Nombre,
                x.Direccion,
                x.Telefono,
                x.FechaCreacion))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SucursalDto> CrearSucursalAsync(CrearSucursalRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var sucursal = new Sucursal
        {
            Id = Guid.NewGuid().ToString(),
            TiendaId = tenantId,
            Nombre = request.Nombre.Trim(),
            Direccion = request.Direccion,
            Telefono = request.Telefono,
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.Sucursales.Add(sucursal);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SucursalDto(
            sucursal.Id,
            sucursal.TiendaId,
            sucursal.Nombre,
            sucursal.Direccion,
            sucursal.Telefono,
            sucursal.FechaCreacion);
    }

    public async Task<SucursalDto?> ActualizarSucursalAsync(string id, ActualizarSucursalRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var sucursal = await _dbContext.Sucursales
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId && x.Id == id, cancellationToken);
        if (sucursal is null)
        {
            return null;
        }

        sucursal.Nombre = request.Nombre.Trim();
        sucursal.Direccion = request.Direccion;
        sucursal.Telefono = request.Telefono;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SucursalDto(
            sucursal.Id,
            sucursal.TiendaId,
            sucursal.Nombre,
            sucursal.Direccion,
            sucursal.Telefono,
            sucursal.FechaCreacion);
    }

    public async Task<bool> EliminarSucursalAsync(string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var sucursal = await _dbContext.Sucursales
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId && x.Id == id, cancellationToken);
        if (sucursal is null)
        {
            return false;
        }

        _dbContext.Sucursales.Remove(sucursal);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<PlatformUsuarioDto>> ListarUsuariosAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PlatformUsuarioDto(
                x.Id,
                x.Name,
                x.Email,
                x.EmailVerified,
                x.Image,
                x.TipoUsuario,
                x.RolStaff,
                x.Estado,
                x.CreatedAt,
                x.SucursalId,
                x.Sucursal != null ? x.Sucursal.Nombre : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<PlatformUsuarioDto?> ObtenerUsuarioAsync(string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.TiendaId == tenantId && x.Id == id)
            .Select(x => new PlatformUsuarioDto(
                x.Id,
                x.Name,
                x.Email,
                x.EmailVerified,
                x.Image,
                x.TipoUsuario,
                x.RolStaff,
                x.Estado,
                x.CreatedAt,
                x.SucursalId,
                x.Sucursal != null ? x.Sucursal.Nombre : null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PlatformUsuarioDto> InvitarUsuarioAsync(InvitarPlatformUsuarioRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var email = request.Email.Trim().ToLowerInvariant();

        var existe = await _dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(x => x.TiendaId == tenantId && x.Email == email, cancellationToken);
        if (existe)
        {
            throw new InvalidOperationException("El usuario ya existe en esta tienda.");
        }

        var user = new ElohimShop.Domain.Platform.User
        {
            Id = Guid.NewGuid().ToString(),
            TiendaId = tenantId,
            Name = request.Name.Trim(),
            Email = email,
            EmailVerified = false,
            TipoUsuario = request.TipoUsuario,
            RolStaff = request.RolStaff,
            SucursalId = request.SucursalId,
            Estado = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);

        var password = !string.IsNullOrWhiteSpace(request.Contrasena) ? request.Contrasena : "Elohim123*";
        var hashedPassword = ElohimShop.Infrastructure.Security.PasswordHashing.Hash(password);

        var account = new Account
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ProviderId = "credential",
            AccountId = email,
            Password = hashedPassword,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Accounts.Add(account);

        await _dbContext.SaveChangesAsync(cancellationToken);

        string? sucursalNombre = null;
        if (!string.IsNullOrEmpty(user.SucursalId))
        {
            var suc = await _dbContext.Sucursales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == user.SucursalId, cancellationToken);
            sucursalNombre = suc?.Nombre;
        }

        return new PlatformUsuarioDto(
            user.Id,
            user.Name,
            user.Email,
            user.EmailVerified,
            user.Image,
            user.TipoUsuario,
            user.RolStaff,
            user.Estado,
            user.CreatedAt,
            user.SucursalId,
            sucursalNombre);
    }

    public async Task<PlatformUsuarioDto?> CambiarRolUsuarioAsync(string id, CambiarRolPlatformUsuarioRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId && x.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.TipoUsuario = request.TipoUsuario;
        user.RolStaff = request.RolStaff;
        user.SucursalId = request.SucursalId;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        string? sucursalNombre = null;
        if (!string.IsNullOrEmpty(user.SucursalId))
        {
            var suc = await _dbContext.Sucursales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == user.SucursalId, cancellationToken);
            sucursalNombre = suc?.Nombre;
        }

        return new PlatformUsuarioDto(
            user.Id,
            user.Name,
            user.Email,
            user.EmailVerified,
            user.Image,
            user.TipoUsuario,
            user.RolStaff,
            user.Estado,
            user.CreatedAt,
            user.SucursalId,
            sucursalNombre);
    }

    public async Task<PlatformUsuarioDto?> CambiarEstadoUsuarioAsync(string id, bool activo, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var user = await _dbContext.Users
            .Include(x => x.Sucursal)
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId && x.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.Estado = activo;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PlatformUsuarioDto(
            user.Id,
            user.Name,
            user.Email,
            user.EmailVerified,
            user.Image,
            user.TipoUsuario,
            user.RolStaff,
            user.Estado,
            user.CreatedAt,
            user.SucursalId,
            user.Sucursal?.Nombre);
    }

    private async Task<ReservacionDto?> MapReservacionAsync(string reservacionId, CancellationToken cancellationToken)
    {
        var reservacion = await _dbContext.Reservaciones
            .AsNoTracking()
            .Include(x => x.Detalles)
            .ThenInclude(x => x.Producto)
            .FirstOrDefaultAsync(x => x.Id == reservacionId, cancellationToken);

        return reservacion is null ? null : MapReservacion(reservacion);
    }

    private static ReportePersonalizadoDto MapReporte(ReportePersonalizado reporte)
    {
        return new ReportePersonalizadoDto(
            reporte.Id,
            reporte.TiendaId,
            reporte.Nombre,
            reporte.Descripcion,
            reporte.QuerySql,
            reporte.CreadoPor,
            reporte.FechaCreacion);
    }

    private async Task<Tienda> GetTenantStoreAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        return await _dbContext.Tiendas.FirstAsync(x => x.Id == tenantId, cancellationToken);
    }

    private string RequireTenantId()
    {
        var tenantId = _tenantProvider.GetTenantId();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Se requiere el header X-Tenant-ID.");
        }

        return tenantId;
    }

    private static decimal ObtenerPrecio(CarritoElemento item)
    {
        return item.Producto is null
            ? 0m
            : (item.Cantidad >= 10 ? item.Producto.PrecioMayoreo : item.Producto.PrecioDetalle);
    }

    public async Task<bool> EliminarUsuarioAsync(string id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var user = await _dbContext.Users
            .Include(u => u.Reservaciones)
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId && x.Id == id, cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (user.Reservaciones.Any())
        {
            user.Estado = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var accounts = _dbContext.Accounts.IgnoreQueryFilters().Where(x => x.UserId == id);
        _dbContext.Accounts.RemoveRange(accounts);

        var sessions = _dbContext.Sessions.IgnoreQueryFilters().Where(x => x.UserId == id);
        _dbContext.Sessions.RemoveRange(sessions);

        var carritos = _dbContext.CarritoElementos.IgnoreQueryFilters().Where(x => x.UsuarioId == id);
        _dbContext.CarritoElementos.RemoveRange(carritos);

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ValidarSqlSeleccion(string querySql)
    {
        if (string.IsNullOrWhiteSpace(querySql))
        {
            throw new InvalidOperationException("La consulta SQL no puede estar vacía.");
        }

        var normalized = querySql.Trim();

        // Eliminar comentarios SQL (-- y /* ... */) para la validación de seguridad
        var cleanSql = Regex.Replace(normalized, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        cleanSql = Regex.Replace(cleanSql, @"--.*$", " ", RegexOptions.Multiline).Trim();

        if (string.IsNullOrWhiteSpace(cleanSql))
        {
            throw new InvalidOperationException("La consulta SQL no puede estar vacía.");
        }

        // Permitir SELECT, WITH (Common Table Expressions) y EXPLAIN
        if (!cleanSql.StartsWith("select", StringComparison.OrdinalIgnoreCase) &&
            !cleanSql.StartsWith("with", StringComparison.OrdinalIgnoreCase) &&
            !cleanSql.StartsWith("explain", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solo se permiten consultas de lectura (SELECT, WITH o EXPLAIN).");
        }

        // Eliminar cadenas literales ('...') para evitar falsos positivos con palabras clave dentro de textos
        var sqlWithoutLiterals = Regex.Replace(cleanSql, @"'(''|[^'])*'", "''").TrimEnd(';', ' ');

        // Prevenir la ejecución de múltiples sentencias separadas por punto y coma (;)
        if (sqlWithoutLiterals.Contains(';'))
        {
            throw new InvalidOperationException("No se permiten múltiples sentencias SQL en una sola consulta.");
        }

        // Validar comandos inseguros de modificación de datos o estructura
        if (UnsafeSqlPattern.IsMatch(sqlWithoutLiterals))
        {
            throw new InvalidOperationException("La consulta contiene comandos no permitidos (solo se admiten sentencias de lectura).");
        }

        // Validar acceso no autorizado a tablas sensibles del sistema
        if (ForbiddenTablesPattern.IsMatch(sqlWithoutLiterals))
        {
            throw new InvalidOperationException("No está permitido consultar tablas sensibles del sistema ni credenciales de seguridad.");
        }
    }

    public async Task<CredencialesIntegracionDtoFull> ObtenerIntegracionesAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var creds = await _dbContext.CredencialesIntegraciones
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TiendaId == tenantId, cancellationToken);
            
        return new CredencialesIntegracionDtoFull(
            tenantId,
            creds?.StripeSecretKey,
            creds?.StripePublicKey,
            creds?.CloudinaryCloudName,
            creds?.CloudinaryApiKey,
            creds?.CloudinaryApiSecret,
            creds?.SmtpEmail,
            creds?.SmtpPassword
        );
    }
}