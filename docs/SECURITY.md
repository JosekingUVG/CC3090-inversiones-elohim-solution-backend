# Modelo de Seguridad Backend y Aislamiento Multi-Tenant

Este documento establece los principios de seguridad, autorización y control de acceso aplicados en el backend de la plataforma.

---

## 🔑 1. Autenticación Exclusiva por OAuth y Ámbitos de Usuario

El backend utiliza exclusivamente **OAuth 2.0 / OpenID Connect (JWT Bearer Tokens)** para la verificación de identidad. Se definen dos ámbitos de usuario completamente independientes:

```
                               ┌──────────────────────────────────────────────┐
                               │       AUTENTICACIÓN EXCLUSIVA OAUTH          │
                               └──────────────────────┬───────────────────────┘
                                                      │
                       ┌──────────────────────────────┴──────────────────────────────┐
                       ▼                                                            ▼
    ┌──────────────────────────────────────┐                     ┌──────────────────────────────────────┐
    │  USUARIOS DEL PORTAL ADMINISTRATIVO │                     │    USUARIOS DE TENANT ESPECÍFICO     │
    │  (Staff / Administradores)           │                     │    (Clientes de Tienda Storefront)   │
    ├──────────────────────────────────────┤                     ├──────────────────────────────────────┤
    │ • Tipo: `administrador` / `staff`    │                     │ • Tipo: `cliente`                    │
    │ • Roles: `superadmin`, `admin`,      │                     │ • Ligado estrictamente a `tienda_id` │
    │   `cajero`, `logistica`              │                     │ • Sin acceso a otros tenants ni      │
    │ • Acceso al Panel de Portal Admin    │                     │   al Portal Administrativo           │
    └──────────────────────────────────────┘                     └──────────────────────────────────────┘
```

### Reglas de Aislamiento de Ámbito:
1. **Separación Portal vs Tenant:** Un usuario registrado como cliente en una tienda de un tenant específico **NO pertenece al Portal de Administración** ni posee permisos administrativos.
2. **Aislamiento Inter-Tenant:** Que un usuario esté registrado como cliente en la tienda del **Tenant A** **NO significa que esté registrado ni que pueda autenticarse en el Tenant B**. Cada tenant es una frontera de autenticación independiente para sus clientes.
3. **Rol SuperAdmin:** Solo los usuarios autenticados con claim de `superadmin` tienen la capacidad de operar de forma transversal sobre múltiples tenants.

---

## 🛡️ 2. Validación de Headers y Protecciones Multi-Tenant

Para prevenir ataques de suplantación de contexto o accesos no autorizados entre tiendas (Cross-Tenant Data Leakage), el middleware `TenantValidationMiddleware` ejecuta una validación de seguridad de 3 capas en cada HTTP Request:

### Capa 1: Resolución y Existencia de Tenant
1. Extrae el Tenant mediante `X-Tenant-ID`, `X-Tenant-Slug` o Subdominio Host.
2. Verifica en la base de datos (`PlatformDbContext`) que la tienda exista y su estado sea activo.
3. Si el Tenant no existe o está desactivado, interrumpe el ciclo con `404 Not Found` / `403 Forbidden`.

### Capa 2: Validation de Token OAuth / JWT (Cross-Tenant Guard)
Para peticiones autenticadas (`Authorization: Bearer <token>`):
1. Extrae los claims del token OAuth (`tienda_id`, `tipo_usuario`, `rol`, `es_super_admin`).
2. **SuperAdmin Bypass:** Si el token posee `es_super_admin = true` o `rol = "superadmin"`, se le permite operar en el tenant solicitado.
3. **Validación Estricta de Tenant:** Para usuarios regulares (`cliente` o `staff`), si el `tienda_id` grabado en el token OAuth difiere del Tenant resuelto en la petición HTTP (`X-Tenant-ID` / `X-Tenant-Slug`), la solicitud es **bloqueada inmediatamente con status `403 Forbidden`**.

```json
{
  "error": "Acceso denegado: el token de autenticación no corresponde a la tienda o tenant solicitado."
}
```

### Capa 3: Inyección de Contexto Seguro
Una vez validada la petición, el middleware crea un objeto `TenantContext` fuertemente tipado en `HttpContext.Items["TenantContext"]`, garantizando que todos los servicios y controladores lean un identificador de tenant 100% auditado y verificado.

---

## 🔒 3. Autorización y Protección de Endpoints

Todos los controladores y endpoints del backend aplican protección de acceso mediante atributos declarativos `[Authorize]` y comprobaciones de rol:

| Módulo / Controller | Nivel de Acceso | Reglas de Validación |
|---------------------|-----------------|----------------------|
| `AuthController` | Público / Autenticado | OAuth Login, Register, Logout, Password Recovery |
| `UsuariosV1Controller` | Admin / SuperAdmin | Solo Staff con rol `administrador` o `superadmin` |
| `ProductosV1Controller` | Público (GET) / Staff (Escritura) | Solo Staff autenticado puede crear/editar/eliminar |
| `SucursalesV1Controller` | Público (GET) / Staff (Escritura) | Solo Staff autenticado puede modificar sucursales |
| `InventariosController` | Staff / Admin | Solo Staff autenticado del tenant |
| `CarritoV1Controller` | Cliente Autenticado | Validado contra el `usuarioId` del token |
| `ReservacionesV1Controller` | Cliente / Staff | Cliente ve sus compras; Staff ve control general |
| `PagosController` | Cliente / Webhook | Webhook de Stripe validado por firma `Stripe-Signature` |
| `MetodoPagoController` | Cliente Autenticado | Tarjetas guardadas aisladas por usuario |
| `ReportesV1Controller` | Staff / Admin | Solo Staff con rol `administrador` o `superadmin` |
| `TiendasController` | Público (GET) / Admin (Escritura) | Configuración visual e integraciones |
| `MediaController` | Staff / Admin | Firmas de Cloudinary aisladas por tenant |

---

## 🔐 4. Buenas Prácticas de Secretos e Integraciones

1. **Tokens JWT:** Firmados usando HMAC-SHA256 con `JWT_KEY` almacenada en variables de entorno seguras.
2. **Integraciones por Tenant (Stripe / Cloudinary / SMTP):** Almacenadas por tienda en `CredencialesIntegraciones`.
3. **Webhooks Stripe:** Verificados mediante el secreto del webhook (`STRIPE_WEBHOOK_SECRET`) para prevenir suplantación de eventos de pago.
