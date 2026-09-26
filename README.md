# Backend — Elohim Shop

API ASP.NET Core 10 + PostgreSQL. Arquitectura en capas (`Domain` → `Application` → `Infrastructure` → `API`).

## 🔐 Autenticación y Aislamiento Multi-Tenant

El backend utiliza **exclusivamente autenticación OAuth 2.0 / OpenID Connect (JWT Token Bearer)** con separación estricta de dos ámbitos:

1. **Usuarios del Portal de Administración (Staff / Admins):** Acceso al panel administrativo central y gestión de tienda.
2. **Usuarios de Tienda en Específico (Clientes):** Acceso al storefront de un tenant en particular.
   - **Aislamiento Total:** Estar registrado como cliente en el Tenant A **no otorga registro ni acceso en el Tenant B, ni acceso al Portal de Administración**.

---

## 🛠️ Requisitos

- .NET SDK 10
- Docker Desktop (recomendado)

## 🚀 Levantar con Docker (recomendado)

Desde la raíz del monorepo:

```bash
docker compose up -d --build
```

- API: `http://localhost:5000`  
- Swagger: `http://localhost:5000/swagger`  
- Postgres: puerto host `5433`

Variables útiles en `.env` / `docker-compose.yml`:

| Variable | Descripción |
|----------|-------------|
| `SEED_DATA` | `true` en `backend/.env` carga `DemoDataSeeder` (productos, ventas, reservaciones, usuarios demo) |
| `SEED_DEMO_DATA` | Alias de `SEED_DATA` (compatibilidad) |
| `SUPER_ADMIN_EMAIL` / `SUPER_ADMIN_PASSWORD` | Super administrador inicial de la plataforma |
| `SEED_USER_EMAIL` / `SEED_USER_PASSWORD` | Usuario administrador personalizado inicial (se enlazará al primer tenant activo) |

## 🗄️ Esquema de base de datos

**Fuente de verdad:** `../db/elohim_db.sql`  

El contenedor backend aplica SQL en el arranque (`entrypoint.sh`), no migraciones EF.

Para base nueva con Docker:

```bash
docker compose down -v
docker volume rm elohim_postgres_data   # si existe
docker compose up -d --build
```

## 💻 Desarrollo local (sin Docker)

```bash
cd backend
dotnet restore ElohimShop.slnx
dotnet build ElohimShop.slnx
dotnet run --project src/ElohimShop.API/ElohimShop.API.csproj
```

## 📚 Documentación Técnica Oficial

- **[docs/API.md](docs/API.md)** — Referencia completa de endpoints V1, controladores y seguridad OAuth.
- **[docs/SECURITY.md](docs/SECURITY.md)** — Modelo de seguridad, aislamiento multi-tenant y middleware `TenantValidationMiddleware`.
- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — Diseño de arquitectura en capas, patrones y flujo HTTP.
- **Swagger** — `http://localhost:5000/swagger` (Environment: Development)
