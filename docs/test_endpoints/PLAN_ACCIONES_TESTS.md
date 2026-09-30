# Plan de Acción - Cobertura de Tests por Controlador
**Fecha**: 2026-08-14  
**Objetivo**: Implementar suite de tests para todos los endpoints del backend

---

## 📊 Estado Actual
- **Endpoints Totales**: ~60 distribuidos en 13 controladores principales
- **Tests Implementados**: 4 archivos de prueba (cubriendo ~25% de endpoints)
- **Tests Faltantes**: 9 controladores sin tests, 4 servicios parcialmente cubiertos

---

## 🎯 Estrategia de Implementación

### **FASE 1: Core Auth & Commerce (CRÍTICA)**
Endpoints de autenticación y flujo de compra esencial

#### 1️⃣ **AuthController** (`/api/v1/auth`)
**Archivo**: `Auth/AuthServiceTests.cs`  
**Estado**: ✅ COMPLETADO
- ✅ `POST /login` → `LoginAsync()`
- ✅ `POST /register` → `RegisterAsync()`
- ✅ `POST /register` (Admin) → `RegisterAdminAsync()`

**Acción**: Completar tests para logout, forgot-password, change-password

---

#### 2️⃣ **CarritoV1Controller** (`/api/v1/carrito`)
**Archivo**: `Carrito/CarritoServiceTests.cs`  
**Estado**: ✅ COMPLETADO

**Endpoints a cubrir**:
- `GET /` - Ver carrito del cliente
- `POST /` - Agregar producto
- `PUT /{itemId}` - Actualizar cantidad
- `DELETE /{itemId}` - Remover producto

**Acción**: Implementar tests para cada endpoint del controlador (adaptados de service tests)

---

#### 3️⃣ **ReservacionesV1Controller** (`/api/v1/reservaciones`)
**Archivo**: `Reservacion/ReservacionServiceTests.cs`  
**Estado**: ✅ COMPLETADO

**Endpoints a cubrir**:
- `GET /` - Listar reservaciones (Admin)
- `GET /mis-reservaciones` - Historial del cliente
- `POST /` - Crear orden
- `PUT /{id}/despacho` - Cambiar estado despacho

**Acción**: Implementar tests para cada endpoint (refactorizar desde service tests)

---

#### 4️⃣ **PagosController** (`/api/pagos`)
**Archivo**: `PagosController/PagosControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `POST /crear-intento` - Crear Stripe intent
- `POST /webhook` - Webhook de Stripe

**Acción**: Crear suite de tests desde cero

**Consideraciones**:
- Mock de Stripe SDK
- Validación de signatures de webhook
- Casos de error en integración

---

#### 5️⃣ **InventariosController** (`/api/v1/inventarios`)
**Archivo**: `InventariosController/InventariosControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /sucursal/{sucursalId}` - Ver stock por sucursal
- `PUT /` - Actualizar stock

**Acción**: Crear tests para gestión de inventario y reservas

---

### **FASE 2: Admin & Productos (ALTA PRIORIDAD)**
Endpoints críticos de administración

#### 6️⃣ **ProductosV1Controller** (`/api/v1/productos`)
**Directorio**: `ProductosV1Controller/` *(VACÍO)*  
**Archivo**: `ProductosV1Controller/ProductosV1ControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /` - Listar productos (public, paginado, filtrado)
- `POST /` - Crear producto (Admin)
- `PUT /{id}` - Editar producto (Admin)
- `DELETE /{id}` - Soft-delete producto (Admin)

**Acción**: Crear suite completa de tests CRUD

**Consideraciones**:
- Tests de paginación y filtros
- Validación de permisos (Admin solo)
- Soft-delete vs hard-delete

---

#### 7️⃣ **AdminUsuariosController** (`/api/admin/usuarios`)
**Directorio**: `AdminUsuariosController/` *(VACÍO)*  
**Archivo**: `AdminUsuariosController/AdminUsuariosControllerTests.cs` *(CREAR)*  
**Relacionado**: `Usuario/UsuarioServiceTests.cs` (service layer)  
**Estado**: ⚠️ SERVICE LAYER CUBIERTO, CONTROLLER TESTS FALTANTES

**Endpoints a cubrir**:
- `GET /` - Listar usuarios de tienda
- `POST /` - Crear usuario/staff
- `PUT /{id}` - Editar usuario
- `POST /{id}/reset-password` - Generar códigos recovery

**Acción**: Refactorizar service tests existentes a controller tests

---

#### 8️⃣ **SucursalesV1Controller** (`/api/v1/sucursales`)
**Directorio**: `SucursalesV1Controller/` *(VACÍO)*  
**Archivo**: `SucursalesV1Controller/SucursalesV1ControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /` - Listar sucursales
- `POST /` - Crear sucursal
- `PUT /{id}` - Editar sucursal
- `DELETE /{id}` - Eliminar sucursal

**Acción**: Crear suite de tests CRUD

---

### **FASE 3: Reportes & Configuración (MEDIA PRIORIDAD)**
Endpoints de análisis y configuración visual

#### 9️⃣ **ReportesV1Controller** (`/api/v1/reportes`)
**Directorio**: `ReportesV1Controller/` *(VACÍO)*  
**Archivo**: `ReportesV1Controller/ReportesV1ControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /dashboard` - Métricas de tienda
- `POST /personalizados` - Guardar plantilla SQL
- `GET /personalizados/{id}/ejecutar` - Ejecutar reporte
- `GET /ventas/exportar` - Exportar Excel

**Acción**: Crear tests para lógica de reportes

**Consideraciones**:
- Mock de SQL execution
- Validación de seguridad SQL injection
- Testing de exportación (sin generar archivos reales)

---

#### 🔟 **TiendasController** (`/api/v1/tiendas`)
**Directorio**: `TiendasController/` *(VACÍO)*  
**Archivo**: `TiendasController/TiendasControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /` - Listar tiendas
- `POST /` - Crear tienda
- `GET /valida-slug/{slug}` - Validar disponibilidad slug

**Acción**: Crear tests para tenant creation

---

### **FASE 4: Complementarios (BAJA PRIORIDAD)**
Endpoints menos críticos pero necesarios

#### 1️⃣1️⃣ **MetodoPagoController** (`/api/metodoPago`)
**Directorio**: `MetodoPagoController/` *(VACÍO)*  
**Archivo**: `MetodoPagoController/MetodoPagoControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /` - Listar tarjetas guardadas
- `POST /` - Guardar tarjeta (Stripe token)
- `DELETE /{id}` - Eliminar tarjeta

**Acción**: Crear tests de gestión de métodos de pago

---

#### 1️⃣2️⃣ **MediaController** (`/api/v1/media`)
**Directorio**: `MediaController/` *(VACÍO)*  
**Archivo**: `MediaController/MediaControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `POST /upload` - Subir a Cloudinary
- `DELETE /{id}` - Eliminar de Cloudinary

**Acción**: Crear tests de integración con Cloudinary

**Consideraciones**:
- Mock de Cloudinary SDK
- Validación de tipos de archivo
- Manejo de errores de upload

---

#### 1️⃣3️⃣ **CatalogController** (`/api/`)
**Directorio**: `CatalogController/` *(VACÍO)*  
**Archivo**: `CatalogController/CatalogControllerTests.cs` *(CREAR)*  
**Estado**: ❌ NO INICIADO

**Endpoints a cubrir**:
- `GET /categorias` - Listar categorías (público)
- `GET /productos` - Consultar catálogo (público, sin auth)

**Acción**: Crear tests públicos

---

## 📋 Orden Recomendado de Implementación

```
SEMANA 1:
  ├─ Completar AuthController (logout, forgot-password, change-password)
  ├─ Completar CarritoV1Controller tests
  └─ Completar ReservacionesV1Controller tests

SEMANA 2:
  ├─ Crear PagosController tests
  ├─ Crear InventariosController tests
  └─ Crear ProductosV1Controller tests

SEMANA 3:
  ├─ Crear AdminUsuariosController tests (refactorizar desde Usuario/*)
  ├─ Crear SucursalesV1Controller tests
  └─ Crear ReportesV1Controller tests

SEMANA 4:
  ├─ Crear TiendasController tests
  ├─ Crear MetodoPagoController tests
  ├─ Crear MediaController tests
  └─ Crear CatalogController tests

VALIDACIÓN FINAL:
  └─ Ejecutar suite completa
  └─ Validar cobertura (target: 80%+)
  └─ Revisar quality gates
```

---

## 📁 Estructura de Archivos a Crear

```
backend/tests/ElohimShop.Tests/
├── Auth/
│   ├── AuthServiceTests.cs ✅
│   └── [COMPLETAR MÉTODOS]
├── AdminUsuariosController/
│   └── AdminUsuariosControllerTests.cs [CREAR]
├── Carrito/
│   ├── CarritoServiceTests.cs ✅
│   └── [COMPLETAR TESTS]
├── CatalogController/
│   └── CatalogControllerTests.cs ✅
├── InventariosController/
│   └── InventariosControllerTests.cs [CREAR]
├── MediaController/
│   └── MediaControllerTests.cs [CREAR]
├── MetodoPagoController/
│   └── MetodoPagoControllerTests.cs [CREAR]
├── PagosController/
│   └── PagosControllerTests.cs [CREAR]
├── ProductosV1Controller/
│   └── ProductosV1ControllerTests.cs [CREAR]
├── Reportes/
│   └── ReportesV1ControllerTests.cs [CREAR]
├── Reservacion/
│   ├── ReservacionServiceTests.cs ✅
│   └── [COMPLETAR TESTS]
├── SucursalesV1Controller/
│   └── SucursalesV1ControllerTests.cs [CREAR]
├── TiendasController/
│   └── TiendasControllerTests.cs [CREAR]
└── Usuario/
    ├── UsuarioServiceTests.cs ✅
    └── [REFACTORIZAR A CONTROLLER TESTS]
```

---

## 🔍 Checklist de Validación por Archivo

Para cada archivo de tests, validar:

- [ ] Unit tests para casos exitosos (200, 201)
- [ ] Error tests (400, 401, 403, 404, 500)
- [ ] Validación de permisos (roles, tenant)
- [ ] Validación de entrada (DTOs inválidos)
- [ ] Tests de negocio (cálculos, reglas, restricciones)
- [ ] Cobertura de código ≥ 80%
- [ ] Documentación de arrange/act/assert
- [ ] Nombres descriptivos de tests (Patrón: Should_ExpectedBehavior_When_Condition)

---

## 🎯 Métricas de Éxito

| Métrica | Target | Progreso |
|---------|--------|----------|
| Tests Implementados | 13 archivos | 4/13 |
| Endpoints Cubiertos | ~60 | ~25 |
| Cobertura de Código | 80%+ | TBD |
| Tiempo de Suite | <30s | TBD |
| Pass Rate | 100% | TBD |

---

**Última actualización**: 2026-08-14  
**Responsable**: Equipo QA  
**Estado**: EN PROGRESO
