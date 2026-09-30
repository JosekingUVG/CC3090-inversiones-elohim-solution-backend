import os

import pytest
import requests


BASE_URL = os.getenv("SECURITY_BASE_URL", "http://localhost:5000").rstrip("/")


def tenant_headers(token: str, tenant_id: str | None = None, tenant_slug: str | None = None) -> dict[str, str]:
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/json"}
    if tenant_id:
        headers["X-Tenant-ID"] = tenant_id
    if tenant_slug:
        headers["X-Tenant-Slug"] = tenant_slug
    return headers


@pytest.mark.security
@pytest.mark.seg01
def test_seg01_a1_no_puede_modificar_item_de_a2(seg01_context):
    item_a2_id = seg01_context.catalog["item_a2"]
    response = requests.put(
        f"{BASE_URL}/api/v1/carrito/articulos/{item_a2_id}",
        headers=tenant_headers(seg01_context.customer_a.token, seg01_context.tenant_a_id),
        json={"cantidad": 2},
        timeout=15,
    )

    assert response.status_code in (403, 404), response.text

    cart_response = requests.get(
        f"{BASE_URL}/api/v1/carrito",
        headers=tenant_headers(seg01_context.customer_a2.token, seg01_context.tenant_a_id),
        timeout=15,
    )
    cart_payload = cart_response.json()
    assert cart_response.status_code == 200, cart_payload
    assert any(item["id"] == item_a2_id for item in cart_payload), cart_payload


@pytest.mark.security
@pytest.mark.seg01
def test_seg01_a1_no_puede_eliminar_item_de_a2(seg01_context):
    item_a2_id = seg01_context.catalog["item_a2"]
    response = requests.delete(
        f"{BASE_URL}/api/v1/carrito/articulos/{item_a2_id}",
        headers=tenant_headers(seg01_context.customer_a.token, seg01_context.tenant_a_id),
        timeout=15,
    )

    assert response.status_code in (403, 404), response.text

    cart_response = requests.get(
        f"{BASE_URL}/api/v1/carrito",
        headers=tenant_headers(seg01_context.customer_a2.token, seg01_context.tenant_a_id),
        timeout=15,
    )
    cart_payload = cart_response.json()
    assert cart_response.status_code == 200, cart_payload
    assert any(item["id"] == item_a2_id for item in cart_payload), cart_payload


@pytest.mark.security
@pytest.mark.seg01
def test_seg01_a1_no_puede_leer_ni_modificar_tenant_b(seg01_context):
    item_b1_id = seg01_context.catalog["item_b1"]

    read_response = requests.get(
        f"{BASE_URL}/api/v1/carrito",
        headers=tenant_headers(seg01_context.customer_a.token, seg01_context.tenant_b_id),
        timeout=15,
    )
    assert read_response.status_code in (401, 403), read_response.text

    update_response = requests.put(
        f"{BASE_URL}/api/v1/carrito/articulos/{item_b1_id}",
        headers=tenant_headers(seg01_context.customer_a.token, seg01_context.tenant_b_id),
        json={"cantidad": 9},
        timeout=15,
    )
    assert update_response.status_code in (401, 403), update_response.text


@pytest.mark.security
@pytest.mark.seg01
def test_seg01_a1_no_puede_usar_slug_ajeno_para_acceder_a_b(seg01_context):
    item_b1_id = seg01_context.catalog["item_b1"]

    read_response = requests.get(
        f"{BASE_URL}/api/v1/carrito",
        headers=tenant_headers(seg01_context.customer_a.token, tenant_slug=seg01_context.tenant_b_slug),
        timeout=15,
    )
    assert read_response.status_code in (401, 403), read_response.text

    update_response = requests.put(
        f"{BASE_URL}/api/v1/carrito/articulos/{item_b1_id}",
        headers=tenant_headers(seg01_context.customer_a.token, tenant_slug=seg01_context.tenant_b_slug),
        json={"cantidad": 7},
        timeout=15,
    )
    assert update_response.status_code in (401, 403), update_response.text
