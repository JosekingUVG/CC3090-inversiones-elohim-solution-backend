using ElohimShop.Application.Platform;
using ElohimShop.Infrastructure.Persistence;
using ElohimShop.Infrastructure.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ElohimShop.Tests.Security;

public class SqlSecurityTests
{
    private readonly PlatformDbContext _dbContext;
    private readonly Mock<ITenantProvider> _tenantProviderMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly PlatformService _service;

    public SqlSecurityTests()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _tenantProviderMock = new Mock<ITenantProvider>();
        _tenantProviderMock.Setup(t => t.GetTenantId()).Returns("tenant-test-123");

        _dbContext = new PlatformDbContext(options, _tenantProviderMock.Object);
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _service = new PlatformService(_dbContext, _tenantProviderMock.Object, _httpContextAccessorMock.Object);
    }

    [Theory]
    [InlineData("DELETE FROM \"Reservacion\" WHERE 1=1;")]
    [InlineData("UPDATE \"Producto\" SET precio_detalle = 0;")]
    [InlineData("DROP TABLE \"Reservacion\";")]
    [InlineData("TRUNCATE TABLE \"Inventario\";")]
    [InlineData("INSERT INTO \"Producto\" (nombre) VALUES ('hacked');")]
    [InlineData("ALTER TABLE \"user\" DROP COLUMN password;")]
    public async Task EjecutarRawReporteAsync_ComandosModificacion_BloqueadoConExcepcion(string queryInsegura)
    {
        var request = new EjecutarRawReporteRequest(queryInsegura);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EjecutarRawReporteAsync(request, CancellationToken.None));

        Assert.Contains("Solo se permiten consultas de lectura", ex.Message);
    }

    [Theory]
    [InlineData("SELECT * FROM \"Producto\"; DROP TABLE \"Reservacion\";")]
    [InlineData("SELECT * FROM \"Inventario\"; DELETE FROM \"user\";")]
    [InlineData("SELECT 1; UPDATE \"Producto\" SET stock_actual = 999;")]
    public async Task EjecutarRawReporteAsync_InyeccionMultisentencia_BloqueadoConExcepcion(string queryMultisentencia)
    {
        var request = new EjecutarRawReporteRequest(queryMultisentencia);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EjecutarRawReporteAsync(request, CancellationToken.None));

        Assert.Contains("No se permiten múltiples sentencias SQL", ex.Message);
    }

    [Theory]
    [InlineData("SELECT * FROM \"CredencialesIntegracion\"")]
    [InlineData("SELECT stripe_secret_key FROM \"CredencialesIntegracion\" WHERE tienda_id = @tenant_id")]
    [InlineData("SELECT * FROM \"session\"")]
    [InlineData("SELECT * FROM \"account\"")]
    [InlineData("SELECT * FROM \"verification\"")]
    public async Task EjecutarRawReporteAsync_AccesoTablasSensibles_BloqueadoConExcepcion(string querySensible)
    {
        var request = new EjecutarRawReporteRequest(querySensible);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EjecutarRawReporteAsync(request, CancellationToken.None));

        Assert.Contains("No está permitido consultar tablas sensibles del sistema ni credenciales de seguridad", ex.Message);
    }

    [Theory]
    [InlineData("SELECT id, nombre, precio_detalle FROM \"Producto\" WHERE tienda_id = @tenant_id")]
    [InlineData("SELECT id, monto_total, estado_pago FROM \"Reservacion\"")]
    [InlineData("WITH resumen AS (SELECT producto_id, SUM(cantidad) as total FROM \"DetalleReservacion\" GROUP BY producto_id) SELECT * FROM resumen")]
    public void ValidarSqlSeleccion_ConsultasValidas_PermitidasSinExcepcion(string queryValida)
    {
        var request = new EjecutarRawReporteRequest(queryValida);
        
        // No debe lanzar ninguna excepción en la etapa de validación de sintaxis y seguridad SQL
        var exception = Record.Exception(() =>
        {
            var method = typeof(PlatformService).GetMethod("ValidarSqlSeleccion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { queryValida });
        });

        Assert.Null(exception);
    }
}
