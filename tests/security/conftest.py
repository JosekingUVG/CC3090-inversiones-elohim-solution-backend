import os
from dataclasses import dataclass
from typing import Any
from uuid import uuid4

import pytest
import requests


BASE_URL = os.getenv("SECURITY_BASE_URL", "http://localhost:5000").rstrip("/")
DEFAULT_TIMEOUT = 15


@dataclass
class UserSession:
    email: str
    password: str
    tenant_id: str
    tenant_slug: str
    token: str
    role: str


@dataclass
class TenantContext:
    tenant_a_id: str
    tenant_a_slug: str
    tenant_b_id: str
    tenant_b_slug: str
    admin_a: UserSession
    admin_b: UserSession
    customer_a: UserSession
    customer_a2: UserSession
    customer_b: UserSession
    catalog: dict[str, Any]


def api_json(method: str, url: str, *, headers: dict[str, str] | None = None, json_body: dict[str, Any] | None = None, timeout: int = DEFAULT_TIMEOUT) -> tuple[int, Any]:
    response = requests.request(method=method.upper(), url=url, headers=headers or {}, json=json_body, timeout=timeout)
    try:
        payload = response.json()
    except ValueError:
        payload = {"raw": response.text}
    return response.status_code, payload


def resolve_tenant_for_token(base_url: str, token: str) -> tuple[str, str]:
    status, payload = api_json(
        "GET",
        f"{base_url}/api/v1/tiendas",
        headers={"Authorization": f"Bearer {token}", "Accept": "application/json"},
    )
    if status != 200:
        raise RuntimeError(f"No se pudo resolver el tenant desde /api/v1/tiendas para el token: HTTP {status}, payload={payload}")

    stores = payload if isinstance(payload, list) else []
    if not stores:
        raise RuntimeError(f"La respuesta de /api/v1/tiendas no devolvió tiendas para el token. payload={payload}")

    first_store = stores[0]
    tenant_id = str(first_store.get("id") or first_store.get("tenantId") or first_store.get("tiendaId") or "")
    tenant_slug = str(first_store.get("slug") or "")
    if not tenant_id:
        raise RuntimeError(f"No se encontró el tenant id en la respuesta de /api/v1/tiendas: {payload}")
    return tenant_id, tenant_slug


def register_admin(base_url: str, email: str, password: str, name: str, role: str = "administrador") -> UserSession:
    status, payload = api_json(
        "POST",
        f"{base_url}/api/v1/auth/register",
        headers={"Accept": "application/json"},
        json_body={
            "correo": email,
            "nombre": name,
            "contrasena": password,
            "tipoUsuario": "administrador",
            "rol": role,
            "direccion": "Sucursal principal",
            "telefono": "+50200000000",
        },
    )
    if status not in (200, 201):
        raise RuntimeError(f"No se pudo registrar administrador {email}: HTTP {status}, payload={payload}")

    token = payload.get("token") or payload.get("accessToken")
    if not token:
        raise RuntimeError(f"La respuesta de registro no devolvió token para {email}: {payload}")

    tenant_id, tenant_slug = resolve_tenant_for_token(base_url, token)
    return UserSession(email=email, password=password, tenant_id=tenant_id, tenant_slug=tenant_slug, token=token, role="administrador")


def register_customer(base_url: str, tenant_id: str, email: str, password: str, name: str) -> UserSession:
    status, payload = api_json(
        "POST",
        f"{base_url}/api/v1/auth/register",
        headers={"X-Tenant-ID": tenant_id, "Accept": "application/json"},
        json_body={
            "correo": email,
            "nombre": name,
            "contrasena": password,
            "tipoUsuario": "cliente",
            "tipoCliente": "particular",
        },
    )
    if status not in (200, 201):
        raise RuntimeError(f"No se pudo registrar cliente {email}: HTTP {status}, payload={payload}")

    token = payload.get("token") or payload.get("accessToken")
    if not token:
        raise RuntimeError(f"La respuesta de registro no devolvió token para {email}: {payload}")
    return UserSession(email=email, password=password, tenant_id=tenant_id, tenant_slug="", token=token, role="cliente")


def login_user(base_url: str, email: str, password: str) -> UserSession:
    status, payload = api_json(
        "POST",
        f"{base_url}/api/v1/auth/login",
        headers={"Accept": "application/json"},
        json_body={"correo": email, "contrasena": password},
    )
    if status != 200:
        raise RuntimeError(f"No se pudo iniciar sesión para {email}: HTTP {status}, payload={payload}")

    token = payload.get("token") or payload.get("accessToken")
    if not token:
        raise RuntimeError(f"La respuesta de login no devolvió token para {email}: {payload}")

    tenant_id = str(payload.get("tiendaId") or payload.get("tenantId") or payload.get("tienda_id") or "")
    tenant_slug = str(payload.get("slug") or "")
    if not tenant_id or not tenant_slug:
        tenant_id, tenant_slug = resolve_tenant_for_token(base_url, token)

    role = str(payload.get("tipoUsuario") or payload.get("role") or "cliente")
    return UserSession(email=email, password=password, tenant_id=tenant_id, tenant_slug=tenant_slug, token=token, role=role)


def get_first_sucursal_id(base_url: str, tenant_id: str, token: str) -> str:
    status, payload = api_json(
        "GET",
        f"{base_url}/api/v1/sucursales",
        headers={
            "Authorization": f"Bearer {token}",
            "X-Tenant-ID": tenant_id,
            "Accept": "application/json",
        },
    )
    if status != 200:
        raise RuntimeError(f"No se pudo listar sucursales para tenant {tenant_id}: HTTP {status}, payload={payload}")

    sucursales = payload if isinstance(payload, list) else []
    if not sucursales:
        raise RuntimeError(f"El tenant {tenant_id} no tiene sucursales disponibles para crear productos. payload={payload}")

    sucursal_id = sucursales[0].get("id") or ""
    if not sucursal_id:
        raise RuntimeError(f"La sucursal devuelta por /api/v1/sucursales no incluye id: payload={payload}")
    return str(sucursal_id)


def create_product(base_url: str, tenant_id: str, token: str, name: str) -> dict[str, Any]:
    sucursal_id = get_first_sucursal_id(base_url, tenant_id, token)
    status, payload = api_json(
        "POST",
        f"{base_url}/api/v1/productos",
        headers={
            "Authorization": f"Bearer {token}",
            "X-Tenant-ID": tenant_id,
            "Accept": "application/json",
        },
        json_body={
            "nombre": name,
            "precioMayoreo": 10.0,
            "precioDetalle": 15.0,
            "categoriaId": None,
            "sku": f"seg01-{uuid4().hex[:8]}",
            "descripcion": "Producto de prueba SEG-01",
            "imagenUrl": None,
            "publicado": True,
            "stockMinimo": 0,
            "stockSucursales": [{"sucursalId": sucursal_id, "stock": 10}],
        },
    )
    if status not in (200, 201):
        raise RuntimeError(f"No se pudo crear producto {name}: HTTP {status}, payload={payload}")
    return payload


def add_cart_item(base_url: str, tenant_id: str, token: str, product_id: str, quantity: int = 1) -> dict[str, Any]:
    status, payload = api_json(
        "POST",
        f"{base_url}/api/v1/carrito/articulos",
        headers={
            "Authorization": f"Bearer {token}",
            "X-Tenant-ID": tenant_id,
            "Accept": "application/json",
        },
        json_body={"productoId": product_id, "cantidad": quantity},
    )
    if status not in (200, 201):
        raise RuntimeError(f"No se pudo añadir item al carrito: HTTP {status}, payload={payload}")
    return payload


def get_cart(base_url: str, token: str, tenant_id: str | None = None, tenant_slug: str | None = None) -> tuple[int, Any]:
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/json"}
    if tenant_id:
        headers["X-Tenant-ID"] = tenant_id
    if tenant_slug:
        headers["X-Tenant-Slug"] = tenant_slug
    return api_json("GET", f"{base_url}/api/v1/carrito", headers=headers)


@pytest.fixture(scope="session")
def seg01_context() -> TenantContext:
    admin_a = register_admin(BASE_URL, f"seg01-admin-a-{uuid4().hex[:8]}@example.com", "Pass123!", "Admin A")
    admin_b = register_admin(BASE_URL, f"seg01-admin-b-{uuid4().hex[:8]}@example.com", "Pass123!", "Admin B")
    if not admin_a.tenant_id or not admin_b.tenant_id:
        raise RuntimeError("Los administradores no devolvieron el tenant en la respuesta de registro.")

    tenant_a_id = admin_a.tenant_id
    tenant_b_id = admin_b.tenant_id
    tenant_a_slug = admin_a.tenant_slug
    tenant_b_slug = admin_b.tenant_slug

    admin_a = login_user(BASE_URL, admin_a.email, admin_a.password)
    admin_b = login_user(BASE_URL, admin_b.email, admin_b.password)
    admin_a.tenant_id = tenant_a_id
    admin_b.tenant_id = tenant_b_id
    admin_a.tenant_slug = tenant_a_slug
    admin_b.tenant_slug = tenant_b_slug

    customer_a = register_customer(BASE_URL, tenant_a_id, f"seg01-a1-{uuid4().hex[:8]}@example.com", "Pass123!", "A1")
    customer_a2 = register_customer(BASE_URL, tenant_a_id, f"seg01-a2-{uuid4().hex[:8]}@example.com", "Pass123!", "A2")
    customer_b = register_customer(BASE_URL, tenant_b_id, f"seg01-b1-{uuid4().hex[:8]}@example.com", "Pass123!", "B1")

    product_a1 = create_product(BASE_URL, tenant_a_id, admin_a.token, f"Seg01-A1-{uuid4().hex[:8]}")
    product_a2 = create_product(BASE_URL, tenant_a_id, admin_a.token, f"Seg01-A2-{uuid4().hex[:8]}")
    product_b1 = create_product(BASE_URL, tenant_b_id, admin_b.token, f"Seg01-B1-{uuid4().hex[:8]}")

    item_a1 = add_cart_item(BASE_URL, tenant_a_id, customer_a.token, product_a1["id"], quantity=1)
    item_a2 = add_cart_item(BASE_URL, tenant_a_id, customer_a2.token, product_a2["id"], quantity=1)
    item_b1 = add_cart_item(BASE_URL, tenant_b_id, customer_b.token, product_b1["id"], quantity=1)

    return TenantContext(
        tenant_a_id=tenant_a_id,
        tenant_a_slug=tenant_a_slug,
        tenant_b_id=tenant_b_id,
        tenant_b_slug=tenant_b_slug,
        admin_a=admin_a,
        admin_b=admin_b,
        customer_a=customer_a,
        customer_a2=customer_a2,
        customer_b=customer_b,
        catalog={
            "product_a1": product_a1["id"],
            "product_a2": product_a2["id"],
            "product_b1": product_b1["id"],
            "item_a1": item_a1["id"],
            "item_a2": item_a2["id"],
            "item_b1": item_b1["id"],
        },
    )
