using ElohimShop.API.Configuration;
using ElohimShop.API.Middleware;
using ElohimShop.Infrastructure.Persistence;
using ElohimShop.Infrastructure.Pagos;
using ElohimShop.Application.Auth;
using ElohimShop.Application.Products;
using ElohimShop.Application.Catalog;
using ElohimShop.Application.Reservacion;
using ElohimShop.Application.Pagos;
using ElohimShop.Application.Admin;
using ElohimShop.Application.Reportes;
using ElohimShop.Application.Platform;
using ElohimShop.Infrastructure.Auth;
using ElohimShop.Infrastructure.Products;
using ElohimShop.Infrastructure.Catalog;
using ElohimShop.Infrastructure.Security;
using ElohimShop.Infrastructure.Admin;
using ElohimShop.Infrastructure.Reportes;
using ElohimShop.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Reflection;
using DotNetEnv;

var envCargado = false;
foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
{
    var dir = new DirectoryInfo(startPath);
    while (dir is not null)
    {
        var envPath = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
            envCargado = true;
            break;
        }

        dir = dir.Parent;
    }

    if (envCargado)
    {
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "ElohimShop API",
        Version = "v1",
        Description =
            "API de autenticación y operaciones para clientes de Elohim Shop. " +
            "Pagos Stripe: usar **POST /api/v1/pagos/webhook** (sin JWT) con el payload crudo de Stripe para sincronizar el campo `pagado` de la reservación; " +
            "**GET /api/v1/pagos/{paymentIntentId}/status** también reconcilia la BD si el pago ya está `succeeded` en Stripe."
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Description = "Ingrese el token JWT con el esquema Bearer. Ejemplo: Bearer {token}"
    });

    options.AddSecurityRequirement(_ => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.AddHttpContextAccessor();

// CORS Configuration - must be before any middleware
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IReservacionService, ReservacionService>();
builder.Services.AddScoped<IPagosService, StripePagosService>();
builder.Services.AddScoped<IStripeWebhookHandler, StripeWebhookHandler>();
builder.Services.AddScoped<IMetodosPagoUsuarioService, MetodosPagoUsuarioService>();
builder.Services.AddScoped<IPasswordHashing, PasswordHashingService>();
builder.Services.AddScoped<IAdminUsuarioService, AdminUsuarioService>();
builder.Services.AddScoped<IReportesService, ReportesService>();
builder.Services.AddScoped<IPlatformService, PlatformService>();

builder.Services.Configure<StripePaymentOptions>(
    builder.Configuration.GetSection(StripePaymentOptions.SectionName));

// Register DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ElohimShopDbContext>(options =>
    options.UseNpgsql(connectionString)
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<ITenantProvider, TenantProvider>();

// JWT Configuration
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
    ?? builder.Configuration["JWT_KEY"]
    ?? jwtSection["Key"]
    ?? throw new InvalidOperationException("JWT Key no configurada. Configure la variable de entorno JWT_KEY.");
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? builder.Configuration["JWT_ISSUER"]
    ?? jwtSection["Issuer"]
    ?? "ElohimShop";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
    ?? builder.Configuration["JWT_AUDIENCE"]
    ?? jwtSection["Audience"]
    ?? "ElohimShop.Clients";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var revocationService = context.HttpContext.RequestServices
                    .GetRequiredService<ITokenRevocationService>();
                var jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

                if (string.IsNullOrWhiteSpace(jti))
                {
                    context.Fail("Token sin identificador.");
                    return;
                }

                if (await revocationService.IsTokenRevokedAsync(jti, context.HttpContext.RequestAborted))
                {
                    context.Fail("Token revocado.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var platformDbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    var elohimShopDbContext = scope.ServiceProvider.GetRequiredService<ElohimShopDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("PlatformDbBootstrapper");

    await PlatformDatabaseBootstrapper.EnsureCreatedAsync(platformDbContext, logger, app.Lifetime.ApplicationStopping);
    await DatabaseSchemaBootstrapper.EnsureSchemaAsync(elohimShopDbContext, logger, app.Lifetime.ApplicationStopping);

    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedDataVal = Environment.GetEnvironmentVariable("SEED_DATA") ?? config["SEED_DATA"];
    var seedData = string.Equals(seedDataVal, "true", StringComparison.OrdinalIgnoreCase);

    await PlatformDemoDataSeeder.SeedAsync(platformDbContext, seedData, logger, app.Lifetime.ApplicationStopping);
    await DemoDataSeeder.SeedAsync(elohimShopDbContext, seedData, logger, app.Lifetime.ApplicationStopping);
}

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ElohimShop API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseMiddleware<TenantResolverMiddleware>();
app.UseMiddleware<BetterAuthSessionMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
