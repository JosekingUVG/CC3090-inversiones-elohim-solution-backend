# Arquitectura del Backend

## Estructura de Carpetas

```
backend/
├── src/
│   ├── ElohimShop.API/              ← API Layer (Controllers V1, Middleware)
│   │   ├── Controllers/             ← Controladores REST unificados /api/v1/
│   │   ├── Middleware/              ← TenantValidationMiddleware, Auth Middleware
│   │   ├── Configuration/           ← Setup de servicios
│   │   ├── Program.cs               ← Startup
│   │   └── appsettings*.json        ← Configuración
│   │
│   ├── ElohimShop.Application/      ← Application Layer (Services & DTOs)
│   │   ├── Auth/                    ← Autenticación OAuth y JWT
│   │   ├── Platform/                ← Servicios de gestión de tiendas y plataforma
│   │   ├── Products/                ← Servicios de productos y categorías
│   │   ├── Pagos/                   ← Integración de pagos Stripe
│   │   ├── Reportes/                ← Generación de reportes y analítica
│   │   └── Common/                  ← TenantContext y utilities compartidas
│   │
│   ├── ElohimShop.Domain/           ← Domain Layer (Entities & Contracts)
│   │   ├── Entities/                ← Entidades de negocio (User, Product, Order, etc.)
│   │   └── Interfaces/              ← Contratos de repositorio y servicios
│   │
│   └── ElohimShop.Infrastructure/   ← Infrastructure Layer (Persistence & External)
│       ├── Persistence/             ← PlatformDbContext, ElohimShopDbContext
│       ├── Security/                ← Firma de JWT, Hashing
│       └── External/                ← Stripe, Cloudinary, SMTP
│
├── tests/
│   └── ElohimShop.Tests/            ← Pruebas unitarias e integración
│
├── docs/                            ← Documentación técnica oficial
└── ElohimShop.slnx                  ← Solution file (.NET 10)
```

---

## 🔐 Autenticación OAuth y Ámbitos de Usuario

El backend implementa autenticación basada **exclusivamente en OAuth 2.0 / OpenID Connect y JWT Token Bearer**.

Existen dos contextos / ámbitos de usuario claramente diferenciados:

```
                            ┌─────────────────────────────────────────┐
                            │    AUTENTICACIÓN EXCLUSIVA POR OAUTH    │
                            └────────────────────┬────────────────────┘
                                                 │
                  ┌──────────────────────────────┴──────────────────────────────┐
                  ▼                                                             ▼
┌───────────────────────────────────┐                         ┌───────────────────────────────────┐
│   PORTAL DE ADMINISTRACIÓN        │                         │   TIENDA EN ESPECÍFICO (TENANT)   │
│   (Staff / Administradores)       │                         │   (Clientes de Storefront)        │
├───────────────────────────────────┤                         ├───────────────────────────────────┤
│ • `tipoUsuario`: `administrador`  │                         │ • `tipoUsuario`: `cliente`        │
│ • Acceso al Portal Administrativo │                         │ • Ligado estrictamente a un       │
│ • Gestión global o por tienda     │                         │   `tienda_id` de tenant           │
└───────────────────────────────────┘                         │ • AISLAMIENTO TOTAL: no accede a  │
                                                              │   otros tenants ni al Portal Admin│
                                                              └───────────────────────────────────┘
```

> [!IMPORTANT]
> **Principio de Aislamiento Estricto de Registro:**
> Que un usuario se registre o autentique en la tienda de un tenant específico **NO significa que esté registrado para ese tenant en otra tienda, ni que tenga acceso al Portal de Administración**.
> Cada tenant representa un dominio de cliente cerrado y aislado.

---

## 🛡️ Flujo de una Solicitud HTTP con Validación Multi-Tenant

```
1. REQUEST RECIBIDO
   └─> GET /api/v1/productos
       Headers: Authorization: Bearer <OAuth_JWT>, X-Tenant-ID: <UUID>

2. TENANT VALIDATION MIDDLEWARE (Capas 1, 2 y 3)
   ├─> Capa 1: Resuelve Tenant (Header / Slug / Subdominio) y valida existencia/estado activo en BD.
   ├─> Capa 2 (Cross-Tenant Guard): Compara `tienda_id` del JWT con el Tenant de la petición.
   │   └─ Si difieren y no es SuperAdmin ──> 403 Forbidden (Bloqueo Inmediato).
   └─> Capa 3: Inyecta `TenantContext` fuertemente tipado en HttpContext.Items.

3. CONTROLLER V1 (`V1ControllerBase`)
   └─> Lee `TenantContext` validado y delega ejecución a la capa Application.

4. APPLICATION LAYER (`PlatformService` / `ProductService`)
   └─> Aplica lógica de negocio respetando el aislamiento tenant.

5. INFRASTRUCTURE & DB LAYER (`PlatformDbContext` / `ElohimShopDbContext`)
   └─> Ejecuta query optimizada en PostgreSQL.

6. RESPONSE
   └─> JSON → HTTP 200 OK
```

---

## Patrones de Diseño

1. **Clean Architecture (N-Capas)**: Estricta separación entre API, Application, Domain e Infrastructure.
2. **Multi-Tenant Context Injection**: `TenantContext` inyectado mediante middleware seguro antes de alcanzar los controladores.
3. **Repository / DbContext Multi-Tenant**: Consultas filtradas por `TiendaId` / `TenantId`.
