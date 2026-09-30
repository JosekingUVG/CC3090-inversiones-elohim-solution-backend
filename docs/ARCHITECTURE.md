# Arquitectura del Backend

## Estructura de Carpetas

```
backend/
├── src/
│   ├── ElohimShop.API/              ← API Layer (Controllers)
│   │   ├── Controllers/             ← Controladores REST
│   │   ├── Middleware/              ← Custom middleware
│   │   ├── Configuration/           ← Setup de servicios
│   │   ├── Program.cs               ← Startup
│   │   └── appsettings*.json        ← Configuración
│   │
│   ├── ElohimShop.Application/      ← Application Layer (Services)
│   │   ├── Admin/                   ← Servicios de administración
│   │   ├── Auth/                    ← Autenticación y autorización
│   │   ├── Catalog/                 ← Gestión de catálogo
│   │   ├── Products/                ← Servicios de productos
│   │   ├── Pagos/                   ← Integración de pagos
│   │   ├── Reservacion/             ← Gestión de pedidos
│   │   ├── Reportes/                ← Generación de reportes
│   │   ├── Platform/                ← Servicios de plataforma
│   │   └── Common/                  ← Utilities compartidas
│   │
│   ├── ElohimShop.Domain/           ← Domain Layer (Entities)
│   │   ├── Entities/                ← Modelos de negocio
│   │   │   ├── User.cs
│   │   │   ├── Product.cs
│   │   │   ├── Order.cs
│   │   │   ├── Payment.cs
│   │   │   └── ...
│   │   ├── Interfaces/              ← Contratos
│   │   ├── Specifications/          ← Business rules
│   │   └── Platform/                ← Configuraciones multi-tenant
│   │
│   └── ElohimShop.Infrastructure/   ← Infrastructure Layer (Data)
│       ├── Persistence/             ← DbContext, migrations
│       │   ├── ApplicationDbContext.cs
│       │   ├── Migrations/
│       │   └── Configurations/
│       ├── Repositories/            ← Repository pattern
│       ├── Admin/                   ← Admin-specific repos
│       ├── Auth/                    ← Auth-specific repos
│       ├── Products/                ← Product-specific repos
│       ├── Pagos/                   ← Payment integration
│       ├── Security/                ← JWT, encryption
│       └── External/                ← Integraciones externas
│
├── tests/
│   └── ElohimShop.Tests/
│       ├── Auth/                    ← Tests de autenticación
│       ├── Carrito/                 ← Tests del carrito
│       ├── Usuario/                 ← Tests de usuarios
│       └── Reservacion/             ← Tests de órdenes
│
├── docs/
│   ├── README.md                    ← Este índice
│   ├── SETUP.md                     ← Instalación
│   ├── ARCHITECTURE.md              ← Este documento
│   ├── API.md                       ← Referencia API
│   ├── DATABASE.md                  ← Modelo de datos
│   ├── NETWORK.md                   ← Configuración Docker
│   └── TESTING.md                   ← Testing (en desarrollo)
│
├── ElohimShop.slnx                  ← Solution file
├── docker-compose.yml               ← Configuración Docker (raíz)
├── Dockerfile                       ← Imagen Docker
├── entrypoint.sh                    ← Script de inicio
└── .env                             ← Variables de entorno
```

---

## Patrones de Diseño

### 1. **Layered Architecture (N-Capas)**

- **Separación de responsabilidades** entre capas
- **Independencia** de implementación tecnológica
- **Testabilidad** mejorada

### 2. **Repository Pattern**

Cada entidad tiene un repositorio que encapsula acceso a datos:

```csharp
// Ejemplo: ProductRepository
public interface IProductRepository
{
    Task<Product> GetByIdAsync(Guid id);
    Task<IEnumerable<Product>> GetAllAsync(Guid tenantId);
    Task AddAsync(Product product);
    Task UpdateAsync(Product product);
    Task DeleteAsync(Guid id);
}
```

### 3. **Dependency Injection**

Se usa el contenedor DI nativo de ASP.NET Core:

```csharp
// Program.cs
builder.Services
    .AddScoped<IProductService, ProductService>()
    .AddScoped<IProductRepository, ProductRepository>();
```

### 4. **DTOs (Data Transfer Objects)**

Separan el modelo de datos (Domain) del modelo de comunicación (API):

```csharp
// Domain Entity
public class Product { /* ... */ }

// DTO para API
public class ProductDTO 
{
    public Guid Id { get; set; }
    public string Nombre { get; set; }
    public decimal Precio { get; set; }
}
```

### 5. **Multi-Tenancy**

Todos los datos están segregados por `TenantId`:

```csharp
public class Product
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // ← Clave de tenant
    public string Nombre { get; set; }
}
```

---

## Flujo de una Solicitud HTTP

```
1. REQUEST RECIBIDO
   └─> GET /api/v1/productos?tenantId=123
   
2. MIDDLEWARE PIPELINE
   ├─> Autenticación (JWT)
   ├─> Autorización (Roles)
   ├─> Logging
   └─> CORS
   
3. CONTROLLER
   └─> ProductosV1Controller.GetProductos()
   
4. APPLICATION LAYER
   ├─> ProductService.GetProductosAsync()
   ├─> Validaciones de negocio
   └─> Mapeo a DTOs
   
5. DOMAIN LAYER
   └─> Verificar reglas de negocio
   
6. INFRASTRUCTURE LAYER
   ├─> ProductRepository.GetAllAsync()
   ├─> Query a PostgreSQL via EF Core
   └─> Retornar resultados
   
7. RESPONSE
   └─> JSON → HTTP 200 OK
```

---

## Autenticación y Autorización

### JWT (JSON Web Tokens)

```
┌────────────────┐
│ POST /login    │
└────────┬───────┘
         │
    ┌────▼────────────────────┐
    │ Validar credenciales     │
    │ Crear JWT token          │
    └────┬───────────────────┐
         │                   │
    ┌────▼──────┐      ┌────▼────────┐
    │  Header   │      │  Payload    │
    │ {alg,typ} │      │ {sub,iat,   │
    │           │      │  exp,roles} │
    └───────────┘      └─────────────┘
         │
    ┌────▼──────────────────┐
    │ Firma (HS256)          │
    └───────────────────────┘
         │
    ┌────▼──────────────────────┐
    │ Retornar JWT a cliente    │
    └───────────────────────────┘
```

### Roles y Permisos

| Rol | Permisos | Acceso |
|-----|----------|--------|
| **SuperAdmin** | Todos | Todos los tenants |
| **Admin** | Gestión completa | Su tenant |
| **Cajero** | Procesamiento de órdenes | Sucursales asignadas |
| **Cliente** | Comprar, Ver historial | Sus órdenes |

---

## Principales Servicios (Application Layer)

### AuthService
- Login/Logout
- Registro de usuarios
- Recuperación de contraseña
- Validación de tokens

### ProductService
- CRUD de productos
- Gestión de categorías
- Filtrado y búsqueda
- Control de precios

### InventoryService
- Actualización de stock
- Reserva de productos
- Reporte de disponibilidad

### OrderService (Reservaciones)
- Creación de órdenes
- Cambio de estado
- Cálculo de totales

### PaymentService
- Integración Stripe
- Webhook handling
- Validación de pagos

### ReportService
- Generación de reportes
- Exportación Excel/CSV
- Análisis de ventas

---

## Integraciones Externas

### Stripe (Pagos)
```
Aplicación → Stripe API → Procesamiento de tarjeta
            ↓
           Webhook → Confirmación de pago
```

### Cloudinary (Media)
```
Frontend → Backend → Cloudinary API → URL de imagen
```

---

## Testing

### Estructura de Tests

```
tests/ElohimShop.Tests/
├── Auth/
│   ├── LoginTests.cs
│   └── RegisterTests.cs
├── Carrito/
│   └── CarritoTests.cs
└── Usuario/
    └── UsuarioTests.cs
```

### Tipos de Tests

1. **Unit Tests**: Servicios individuales
2. **Integration Tests**: Servicios + BD en memoria
3. **API Tests**: Endpoints HTTP completos

---

## Performance & Escalabilidad

### Optimizaciones Actuales

- Lazy loading en EF Core
- Pagination en endpoints
- Caching de catálogo
- Connection pooling en PostgreSQL

### Recomendaciones Futuras

- Redis para caché distribuido
- Async/await en todas las operaciones
- API rate limiting
- Database read replicas

---

## Documentos Relacionados

- [SETUP.md](SETUP.md) - Cómo levantar el proyecto
- [API.md](API.md) - Referencia de endpoints
- [DATABASE.md](DATABASE.md) - Modelo de datos
- [NETWORK.md](NETWORK.md) - Configuración Docker

---

**Última actualización**: 2026-07-25