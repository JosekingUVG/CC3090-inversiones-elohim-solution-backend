# Documentación de la API Backend

La API del sistema está construida sobre ASP.NET Core 10 siguiendo una arquitectura REST unificada bajo el prefijo `/api/v1/`.

---

## 🔐 Modelo de Autenticación OAuth y Aislamiento de Usuarios

La autenticación de la plataforma opera exclusivamente mediante **OAuth 2.0 / Google OpenID Connect y JWT Token Bearer**, manteniendo una separación estricta de dominios de usuario:

### 1. Usuarios del Portal de Administración (Staff / Admins)
- **Ámbito:** Acceso al Portal de Administración central y funciones de gestión de tienda.
- **Autenticación:** OAuth (Google OAuth / Credenciales OAuth de Plataforma).
- **Roles:** `superadmin`, `administrador`, `cajero`, `logistica`.
- **Alcance:** Acceso delimitado por permisos y asignación a tiendas/plataforma.

### 2. Usuarios de una Tienda en Específico (Clientes de Tenant)
- **Ámbito:** Acceso exclusivo al storefront y catálogo de un tenant en particular.
- **Autenticación:** OAuth configurado para el contexto del tenant especificando el header o parámetro de tienda (`X-Tenant-ID` / `X-Tenant-Slug`).
- **Aislamiento Estricto:**
  > [!IMPORTANT]
  > Que un usuario esté registrado y autenticado en el Tenant A **NO significa que esté registrado para ese Tenant B ni que tenga acceso al Portal de Administración**.
  > Cada tienda (tenant) constituye un límite de autenticación cerrado e independiente para los clientes.

---

## 🌐 Headers Requeridos y Validación Multi-Tenant

Para todas las peticiones a la API:

| Header | Requerido | Descripción |
|--------|-----------|-------------|
| `Authorization` | En endpoints autenticados | `Bearer <JWT_TOKEN_OAUTH>` |
| `X-Tenant-ID` | Sí (o `X-Tenant-Slug`) | GUID único del Tenant |
| `X-Tenant-Slug` | Alternativa a `X-Tenant-ID` | Slug legible de la tienda |

> [!SECURITY]
> **Cross-Tenant Guard:** El middleware `TenantValidationMiddleware` valida automáticamente que el `tienda_id` del token OAuth/JWT coincida con el Tenant de la petición. Si no coinciden, la petición es denegada con `403 Forbidden` (excepto para usuarios con rol `superadmin`).

---

## 📋 Controladores y Endpoints Unificados (`/api/v1`)

### 1. Autenticación (`AuthController`)
Prefijo: `/api/v1/auth`

- `POST /login` — Autenticación OAuth de usuario (retorna JWT y claims de ámbito).
- `POST /google` — Autenticación con Google OAuth ID Token (`tipoUsuario`: `administrador` | `cliente`).
- `POST /register` — Registro de cliente para tenant específico o staff (Admin).
- `POST /logout` — Invalidation del token en sesión activa.
- `POST /forgot-password` — Solicitud de código de recuperación por correo SMTP del tenant.
- `POST /recover-with-code` — Recuperación de contraseña mediante código de validación.
- `POST /change-password` — Cambio de contraseña (requiere token autenticado).

### 2. Usuarios de Plataforma y Administración (`UsuariosV1Controller`)
Prefijo: `/api/v1/usuarios` | Acceso: Admin / SuperAdmin

- `GET /` — Listar usuarios del tenant actual.
- `POST /invitar` — Invitar / crear usuario staff para el tenant.
- `PUT /{id}/rol` — Cambiar rol o permisos de usuario.
- `PUT /{id}/estado` — Activar o desactivar usuario.
- `DELETE /{id}` — Eliminar usuario.
- `POST /{id}/reset-password` — Generar 8 códigos de recuperación alternativos offline.

### 3. Productos y Catálogo (`ProductosV1Controller`)
Prefijo: `/api/v1/productos` | Acceso: Público (lectura), Admin (escritura)

- `GET /` — Listar productos del tenant (paginación, filtros).
- `GET /{id}` — Obtener detalle de un producto específico.
- `GET /categorias` — Listar categorías de productos del tenant.
- `POST /` — Crear producto (Admin).
- `POST /bulk` — Carga masiva de productos (Admin).
- `PUT /{id}` — Actualizar producto (Admin).
- `DELETE /{id}` — Eliminar producto (Admin).

### 4. Sucursales (`SucursalesV1Controller`)
Prefijo: `/api/v1/sucursales` | Acceso: Público (lectura), Admin (escritura)

- `GET /` — Listar sucursales activas del tenant.
- `POST /` — Crear nueva sucursal (Admin).
- `PUT /{id}` — Actualizar sucursal (Admin).
- `DELETE /{id}` — Eliminar sucursal (Admin).

### 5. Inventarios (`InventariosController`)
Prefijo: `/api/v1/inventarios` | Acceso: Staff / Admin

- `GET /sucursal/{sucursalId}` — Consultar stock por sucursal.
- `PUT /` — Ajuste de stock de producto en sucursal.
- `GET /bajo-stock` — Listado de alertas de stock crítico.

### 6. Carrito de Compras (`CarritoV1Controller`)
Prefijo: `/api/v1/carrito` | Acceso: Cliente Autenticado del Tenant

- `GET /` — Obtener elementos del carrito del cliente.
- `POST /articulos` — Agregar artículo al carrito.
- `PUT /articulos/{id}` — Actualizar cantidad de un artículo.
- `DELETE /articulos/{id}` — Eliminar artículo del carrito.

### 7. Reservaciones y Compras (`ReservacionesV1Controller`)
Prefijo: `/api/v1/reservaciones` | Acceso: Cliente / Staff

- `POST /` — Crear reservación / compra (Cliente).
- `GET /mis-compras` — Consultar historial de compras del cliente autenticado.
- `GET /control-staff` — Listar reservaciones de la tienda para gestión (Staff).
- `PATCH /{id}/estado` — Cambiar estado de pago/despacho (Staff).

### 8. Pasarela de Pagos Stripe (`PagosController`)
Prefijo: `/api/v1/pagos` | Acceso: Cliente / Webhook Público

- `POST /create-intent` — Crear Payment Intent en Stripe para una reservación.
- `GET /{paymentIntentId}/status` — Consultar estado del pago.
- `POST /webhook` — Webhook de notificación asíncrona de Stripe.

### 9. Métodos de Pago Guardados (`MetodoPagoController`)
Prefijo: `/api/v1/metodos-pago` | Acceso: Cliente Autenticado

- `GET /` — Listar tarjetas guardadas del cliente.
- `POST /` — Registrar nueva tarjeta tokenizada en Stripe.
- `POST /contra-entrega` — Preparar método de pago contra entrega.
- `DELETE /{id}` — Eliminar tarjeta guardada.

### 10. Reportes y Analítica (`ReportesV1Controller`)
Prefijo: `/api/v1/reportes` | Acceso: Staff / Admin

- `GET /productos` — Reporte de ventas e ingresos por producto.
- `GET /empleados` — Reporte de rendimiento por empleado.
- `GET /metodos-pago` — Distribución de ventas por método de pago.
- `POST /ejecutar-raw` — Ejecutar consulta SQL personalizada segura (Admin).
- `POST /guardar` — Guardar reporte personalizado.
- `GET /` — Listar reportes personalizados guardados.
- `GET /{id}/correr` — Ejecutar reporte guardado.

### 11. Tiendas e Integraciones (`TiendasController`)
Prefijo: `/api/v1/tiendas` | Acceso: Público / Admin

- `GET /` — Listar tiendas registradas.
- `GET /{idOrSlug}` — Obtener información de una tienda por ID o Slug.
- `POST /` — Crear nueva tienda.
- `PUT /actualizar` — Actualizar información básica de la tienda.
- `GET /integraciones` — Obtener credenciales de integración (Stripe, Cloudinary, SMTP).
- `POST /integraciones` — Guardar configuración de integraciones (Admin).
- `PUT /configuracion-visual` — Guardar configuración del maquetador visual de la tienda (Admin).

### 12. Media y Almacenamiento (`MediaController`)
Prefijo: `/api/v1/media` | Acceso: Staff / Admin

- `GET /cloudinary-signature` — Generar firma autorizada para subida directa a Cloudinary.