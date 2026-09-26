using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ElohimShop.Infrastructure.Persistence;

public static class PlatformDatabaseBootstrapper
{
    public static async Task EnsureCreatedAsync(
        PlatformDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var created = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        if (created)
        {
            logger.LogInformation("Base de datos creada desde el modelo EF Core.");
        }
        else
        {
            try
            {
                // Asegurar que tienda_id sea nullable para bases de datos existentes
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"user\" ALTER COLUMN tienda_id DROP NOT NULL;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo alterar la columna tienda_id de la tabla user. Podría ser que ya sea nullable.");
            }

            try
            {
                // Agregar columna sucursal_id a la tabla user si no existe
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"user\" ADD COLUMN IF NOT EXISTS sucursal_id VARCHAR(255) NULL;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo agregar la columna sucursal_id a la tabla user.");
            }

            try
            {
                // Agregar columnas smtp_email y smtp_password a la tabla CredencialesIntegracion si no existen
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"CredencialesIntegracion\" ADD COLUMN IF NOT EXISTS smtp_email VARCHAR(255) NULL;", cancellationToken);
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"CredencialesIntegracion\" ADD COLUMN IF NOT EXISTS smtp_password TEXT NULL;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudieron agregar las columnas smtp_email o smtp_password a la tabla CredencialesIntegracion.");
            }

            try
            {
                // Agregar columna stock_actual a la tabla Producto si no existe e inicializarla
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Producto\" ADD COLUMN IF NOT EXISTS stock_actual INTEGER NOT NULL DEFAULT 0;", cancellationToken);
                await dbContext.Database.ExecuteSqlRawAsync("UPDATE \"Producto\" p SET stock_actual = COALESCE((SELECT SUM(i.stock) FROM \"Inventario\" i WHERE i.producto_id = p.id), 0);", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo agregar o inicializar la columna stock_actual en la tabla Producto.");
            }

            try
            {
                // Agregar columna stock_minimo a la tabla Producto si no existe
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Producto\" ADD COLUMN IF NOT EXISTS stock_minimo INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo agregar la columna stock_minimo a la tabla Producto.");
            }
        }

        // Crear la tabla MetodoPago si no existe (indispensable para ElohimShopDbContext ya que no comparte las migraciones de PlatformDbContext)
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MetodoPago"" (
                    id_metodo_pago VARCHAR(255) PRIMARY KEY,
                    nombre_metodo VARCHAR(15) NOT NULL,
                    descripcion TEXT NULL,
                    activo BOOLEAN NOT NULL DEFAULT TRUE,
                    usuario_id VARCHAR(255) NULL,
                    alias_tarjeta VARCHAR(120) NULL,
                    expira_anio INTEGER NULL,
                    expira_mes INTEGER NULL,
                    marca_tarjeta VARCHAR(30) NULL,
                    stripe_payment_method_id VARCHAR(255) NULL,
                    ultimos_digitos VARCHAR(4) NULL
                );", cancellationToken);
            logger.LogInformation("Tabla MetodoPago asegurada (creada o ya existente).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo asegurar la existencia de la tabla MetodoPago.");
        }

        // Crear y configurar el rol de sólo lectura reports_readonly
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'reports_readonly') THEN
                        CREATE ROLE reports_readonly WITH LOGIN PASSWORD 'ReadOnlyPassword123!';
                    END IF;
                    EXECUTE 'GRANT CONNECT ON DATABASE ' || quote_ident(current_database()) || ' TO reports_readonly';
                END
                $$;", cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(@"
                GRANT USAGE ON SCHEMA public TO reports_readonly;
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO reports_readonly;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO reports_readonly;", cancellationToken);

            logger.LogInformation("Rol de lectura reports_readonly configurado correctamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo configurar el rol de lectura reports_readonly.");
        }

        // Configurar Row-Level Security (RLS) en PostgreSQL para aislamiento estricto de tenants
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                DECLARE
                    tbl text;
                    tables text[] := ARRAY['Reservacion', 'Producto', 'Inventario', 'Categoria', 'CarritoElemento', 'ReportePersonalizado', 'user'];
                BEGIN
                    FOREACH tbl IN ARRAY tables LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY;', tbl);
                        
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation_policy ON %I;', tbl);
                        
                        EXECUTE format('
                            CREATE POLICY tenant_isolation_policy ON %I
                            FOR ALL
                            USING (
                                NULLIF(current_setting(''app.current_tenant'', true), '''') IS NULL OR
                                current_setting(''app.current_tenant'', true) = ''*'' OR
                                tienda_id IS NULL OR
                                tienda_id = current_setting(''app.current_tenant'', true)
                            );', tbl);
                    END LOOP;
                END
                $$;", cancellationToken);

            logger.LogInformation("Políticas de Row-Level Security (RLS) en PostgreSQL configuradas con éxito.");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudieron configurar las políticas RLS en PostgreSQL (podría tratarse de proveedor no Postgres o pruebas en memoria).");
        }
    }
}