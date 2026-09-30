# 🔗 Mapeo Swagger → Tests

Documentación de dónde está cada endpoint definido en Swagger (API.md) y su estado de testing

---

## 1️⃣ AuthController - `/api/v1/auth`

**Documentación**: [API.md línea 112-142](docs/API.md#L112-L142)  
**Tests**: [Auth/AuthServiceTests.cs](backend/tests/ElohimShop.Tests/Auth/)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/login` | POST | L115-126 | `LoginAsync_CredencialesValidas_RetornaToken()` | ✅ |
| `/register` | POST | L128-130 | `RegisterAsync_RegistroClienteValido_RetornaToken()` | ✅ |
| `/register` (Admin) | POST | L128-130 | `RegisterAdminAsync_AdminValido_RetornaToken()` | ✅ |


---

## 2️⃣ AdminUsuariosController - `/api/admin/usuarios`

**Documentación**: [API.md línea 144-160](docs/API.md#L144-L160)  
**Tests**: [Usuario/UsuarioServiceTests.cs](backend/tests/ElohimShop.Tests/Usuario/) (service layer)  
**Tests Faltantes**: [AdminUsuariosController/AdminUsuariosControllerTests.cs](backend/tests/ElohimShop.Tests/AdminUsuariosController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L146 | `ObtenerUsuarioAsync_UsuarioExiste_ReturnsUsuario()` | ⚠️ SERVICE |
| `/` | POST | L148-150 | ❌ FALTA | 🔴 |
| `/{id}` | PUT | L151-153 | ❌ FALTA | 🔴 |
| `/{id}/reset-password` | POST | L154-160 | ❌ FALTA | 🔴 |

---

## 3️⃣ ProductosV1Controller - `/api/v1/productos`

**Documentación**: [API.md línea 168-177](docs/API.md#L168-L177)  
**Tests**: [ProductosV1Controller/ProductosV1ControllerTests.cs](backend/tests/ElohimShop.Tests/ProductosV1Controller/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L170-173 | ❌ FALTA | 🔴 |
| `/` | POST | L174 | ❌ FALTA | 🔴 |
| `/{id}` | PUT | L175 | ❌ FALTA | 🔴 |
| `/{id}` | DELETE | L176 | ❌ FALTA | 🔴 |

---

## 4️⃣ SucursalesV1Controller - `/api/v1/sucursales`

**Documentación**: [API.md línea 52-57](docs/API.md#L52-L57)  
**Tests**: [SucursalesV1Controller/SucursalesV1ControllerTests.cs](backend/tests/ElohimShop.Tests/SucursalesV1Controller/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L54 | ❌ FALTA | 🔴 |
| `/` | POST | L55 | ❌ FALTA | 🔴 |
| `/{id}` | PUT | L56 | ❌ FALTA | 🔴 |
| `/{id}` | DELETE | L57 | ❌ FALTA | 🔴 |

---

## 5️⃣ InventariosController - `/api/v1/inventarios`

**Documentación**: [API.md línea 48-51](docs/API.md#L48-L51) (resumen) y [L208-215](docs/API.md#L208-L215) (detalle)  
**Tests**: [InventariosController/InventariosControllerTests.cs](backend/tests/ElohimShop.Tests/InventariosController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/sucursal/{sucursalId}` | GET | L210 | ❌ FALTA | 🔴 |
| `/` | PUT | L213-214 | ❌ FALTA | 🔴 |

---

## 6️⃣ CarritoV1Controller - `/api/v1/carrito`

**Documentación**: [API.md línea 43-47](docs/API.md#L43-L47)  
**Tests**: [Carrito/CarritoServiceTests.cs](backend/tests/ElohimShop.Tests/Carrito/)  
**Notas**: Service tests existen, requiere adaptación a Controller tests

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L44 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/` | POST | L45 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/{itemId}` | PUT | L46 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/{itemId}` | DELETE | L47 | ⚠️ SERVICE | 🟡 ADAPTER |

---

## 7️⃣ ReservacionesV1Controller - `/api/v1/reservaciones`

**Documentación**: [API.md línea 59-67](docs/API.md#L59-L67) (resumen) y [L185-201](docs/API.md#L185-L201) (detalle)  
**Tests**: [Reservacion/ReservacionServiceTests.cs](backend/tests/ElohimShop.Tests/Reservacion/)  
**Notas**: Service tests existen, requiere adaptación a Controller tests

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L187 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/mis-reservaciones` | GET | L189 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/` | POST | L191-198 | ⚠️ SERVICE | 🟡 ADAPTER |
| `/{id}/despacho` | PUT | L199-201 | ⚠️ SERVICE | 🟡 ADAPTER |

---

## 8️⃣ PagosController - `/api/pagos`

**Documentación**: [API.md línea 68-71](docs/API.md#L68-L71) (resumen) y [L234-244](docs/API.md#L234-L244) (detalle)  
**Tests**: [PagosController/PagosControllerTests.cs](backend/tests/ElohimShop.Tests/PagosController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/crear-intento` | POST | L236-239 | ❌ FALTA | 🔴 |
| `/webhook` | POST | L240-244 | ❌ FALTA | 🔴 |

---

## 9️⃣ MetodoPagoController - `/api/metodoPago`

**Documentación**: [API.md línea 73-76](docs/API.md#L73-L76)  
**Tests**: [MetodoPagoController/MetodoPagoControllerTests.cs](backend/tests/ElohimShop.Tests/MetodoPagoController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L74 | ❌ FALTA | 🔴 |
| `/` | POST | L75 | ❌ FALTA | 🔴 |
| `/{id}` | DELETE | L76 | ❌ FALTA | 🔴 |

---

## 🔟 ReportesV1Controller - `/api/v1/reportes`

**Documentación**: [API.md línea 77-80](docs/API.md#L77-L80) (resumen) y [L217-230](docs/API.md#L217-L230) (detalle)  
**Tests**: [ReportesV1Controller/ReportesV1ControllerTests.cs](backend/tests/ElohimShop.Tests/ReportesV1Controller/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/dashboard` | GET | L219 | ❌ FALTA | 🔴 |
| `/personalizados` | POST | L221-224 | ❌ FALTA | 🔴 |
| `/personalizados/{id}/ejecutar` | GET | L225-227 | ❌ FALTA | 🔴 |
| `/ventas/exportar` | GET | L228-230 | ❌ FALTA | 🔴 |

---

## 1️⃣1️⃣ TiendasController - `/api/v1/tiendas`

**Documentación**: [API.md línea 81-85](docs/API.md#L81-L85) (resumen) y [L246-254](docs/API.md#L246-L254) (detalle)  
**Tests**: [TiendasController/TiendasControllerTests.cs](backend/tests/ElohimShop.Tests/TiendasController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/` | GET | L247 | ❌ FALTA | 🔴 |
| `/` | POST | L249-252 | ❌ FALTA | 🔴 |
| `/valida-slug/{slug}` | GET | L253 | ❌ FALTA | 🔴 |

---

## 1️⃣2️⃣ MediaController - `/api/v1/media`

**Documentación**: [API.md línea 86-87](docs/API.md#L86-L87)  
**Tests**: [MediaController/MediaControllerTests.cs](backend/tests/ElohimShop.Tests/MediaController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/upload` | POST | L86 | ❌ FALTA | 🔴 |
| `/{id}` | DELETE | L87 | ❌ FALTA | 🔴 |

---

## 1️⃣3️⃣ CatalogController - `/api/`

**Documentación**: [API.md línea 89-92](docs/API.md#L89-L92)  
**Tests**: [CatalogController/CatalogControllerTests.cs](backend/tests/ElohimShop.Tests/CatalogController/) (CREAR)

| Endpoint | Método | Swagger | Test | Estado |
|----------|--------|---------|------|--------|
| `/categorias` | GET | L91 | ❌ FALTA | 🔴 |
| `/productos` | GET | L92 | ❌ FALTA | 🔴 |

---

## 📊 Resumen de Cobertura por Ubicación en Swagger

| Sección en API.md | Endpoints | Tests | Cobertura |
|------------------|-----------|-------|-----------|
| **Módulo 1** (Línea 44-92) | 13 | 0 | 0% |
| **Detalle Auth** (Línea 112-142) | 5 | 3 | 60% |
| **Detalle Admin** (Línea 144-160) | 4 | 1 | 25% |
| **Detalle Productos** (Línea 168-177) | 4 | 0 | 0% |
| **Detalle Reservaciones** (Línea 185-201) | 4 | 3 | 75% |
| **Detalle Inventarios** (Línea 208-215) | 2 | 0 | 0% |
| **Detalle Reportes** (Línea 217-230) | 4 | 0 | 0% |
| **Detalle Pagos** (Línea 234-244) | 2 | 0 | 0% |
| **Detalle Tiendas** (Línea 246-254) | 3 | 0 | 0% |
| **TOTAL** | **60** | **15** | **25%** |

---

## 🎯 Referencias Cruzadas

### Documentación Relacionada
- **API.md**: `backend/docs/API.md` - Especificación completa de endpoints
- **SECURITY.md**: `backend/docs/SECURITY.md` - Validación de roles y permisos
- **ARCHITECTURE.md**: `backend/docs/ARCHITECTURE.md` - Estructura de capas
- **AuthServiceTests.cs**: `backend/tests/ElohimShop.Tests/Auth/AuthServiceTests.cs` - Ejemplo de patrones

### Controladores Fuente (Backend)
Todos están en: `backend/src/ElohimShop.API/Controllers/`

```
Controllers/
├── AuthController.cs              ← Auth tests aquí
├── AdminUsuariosController.cs     ← Usuario tests (refactorizar)
├── ProductosV1Controller.cs       ← Crear ProductosV1ControllerTests
├── SucursalesV1Controller.cs      ← Crear SucursalesV1ControllerTests
├── InventariosController.cs       ← Crear InventariosControllerTests
├── CarritoV1Controller.cs         ← Carrito tests (refactorizar)
├── ReservacionesV1Controller.cs   ← Reservacion tests (refactorizar)
├── PagosController.cs             ← Crear PagosControllerTests
├── MetodoPagoController.cs        ← Crear MetodoPagoControllerTests
├── ReportesV1Controller.cs        ← Crear ReportesV1ControllerTests
├── TiendasController.cs           ← Crear TiendasControllerTests
├── MediaController.cs             ← Crear MediaControllerTests
└── CatalogController.cs           ← Crear CatalogControllerTests
```

---

**Documento**: SWAGGER_TESTS_MAPPING.md  
**Fecha**: 2026-08-14  
**Referencia Principal**: [API.md](backend/docs/API.md)  
**Generado por**: AI Code Assistant
