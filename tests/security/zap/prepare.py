"""Create a separate synthetic tenant per scan profile; never reuse development data."""
import copy
import json
import secrets
import uuid
import urllib.error
import urllib.request
from pathlib import Path

from common import local_target, render, write_json


class Api:
    def __init__(self, base):
        self.base = local_target(base)
        self.tenant = None
        self.evidence = []
        # Do not inherit a workstation proxy or follow redirects out of the target.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def request(self, method, path, body=None, token=None):
        headers = {"Accept": "application/json"}
        if self.tenant:
            headers["X-Tenant-ID"] = self.tenant
        if token:
            headers["Authorization"] = "Bearer " + token
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(body).encode()
        request = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=30) as response:
                payload = response.read()
                self.evidence.append({"method": method, "path": path, "status": response.status})
                return json.loads(payload) if payload else None
        except urllib.error.HTTPError as error:
            self.evidence.append({"method": method, "path": path, "status": error.code})
            raise RuntimeError(f"Preparación: {method} {path}: HTTP {error.code}") from None


def filtered_openapi(original, operations, base):
    document = copy.deepcopy(original)
    if not str(document.get("openapi", "")).startswith("3."):
        raise ValueError("Se requiere OpenAPI 3.x")
    document["servers"] = [{"url": base}]
    document["paths"] = {}
    for operation in operations:
        path, method = operation["path"], operation["method"].lower()
        source = original["paths"].get(path, {})
        if method not in source:
            raise ValueError(f"Operación requerida ausente en OpenAPI: {method.upper()} {path}")
        item = document["paths"].setdefault(path, {})
        item[method] = copy.deepcopy(source[method])
        item[method].pop("servers", None)
        if "parameters" in source:
            item["parameters"] = copy.deepcopy(source["parameters"])
        # Concrete path examples improve import; probes still verify real business data.
        templates = path.strip("/").split("/")
        concrete = operation["url"].strip("/").split("/")
        examples = {a[1:-1]: b for a, b in zip(templates, concrete)
                    if a.startswith("{") and a.endswith("}")}
        for parameter in item.get("parameters", []) + item[method].get("parameters", []):
            if parameter.get("in") == "path" and parameter.get("name") in examples:
                parameter["example"] = examples[parameter["name"]]
        if "body" in operation:
            media = item[method].get("requestBody", {}).get("content", {}).get("application/json")
            if media is not None:
                media["example"] = operation["body"]
    return document


def prepare(base, profile, work, output, scope):
    api = Api(base)
    tag = "zap-" + profile + "-" + uuid.uuid4().hex[:10]
    password = secrets.token_urlsafe(24) + "aA1!"
    email = tag + "@example.com"
    evidence_path = output / f"preparation-{profile}.json"
    evidence = {"profile": profile, "complete": False, "requests": api.evidence}
    try:
        admin = api.request("POST", "/api/v1/auth/register", {
            "correo": email, "nombre": tag, "contrasena": password,
            "tipoUsuario": "administrador", "rol": "administrador",
            "direccion": "CI ZAP", "telefono": "+50200000000"})
        stores = api.request("GET", "/api/v1/tiendas", token=admin["token"])
        if len(stores) != 1:
            raise AssertionError("Se esperaba exactamente una tienda nueva")
        api.tenant = stores[0]["id"]
        admin = api.request("POST", "/api/v1/auth/login", {"correo": email, "contrasena": password})
        client_email = tag + "-client@example.com"
        api.request("POST", "/api/v1/auth/register", {
            "correo": client_email, "nombre": tag + " cliente", "contrasena": password,
            "tipoUsuario": "cliente", "tipoCliente": "particular"})
        client = api.request("POST", "/api/v1/auth/login", {
            "correo": client_email, "contrasena": password})
        branch = api.request("POST", "/api/v1/sucursales", {
            "nombre": tag, "direccion": "Sucursal CI", "telefono": "+50200000000"}, admin["token"])
        product = api.request("POST", "/api/v1/productos", {
            "nombre": tag, "sku": tag, "precioMayoreo": 10, "precioDetalle": 15,
            "publicado": True, "stockMinimo": 0,
            "stockSucursales": [{"sucursalId": branch["id"], "stock": 100}]}, admin["token"])
        payment = api.request("POST", "/api/v1/metodos-pago/contra-entrega", token=client["token"])
        api.request("POST", "/api/v1/carrito/articulos", {
            "productoId": product["id"], "cantidad": 2}, client["token"])
        # V1 has no metodoPagoId: an absent StripeIntentId creates a pending reservation.
        reservation = api.request("POST", "/api/v1/reservaciones", {
            "sucursalId": branch["id"]}, client["token"])
        if reservation["estadoPago"] != "pendiente" or not reservation["detalles"]:
            raise AssertionError("Reservación sin detalles o sin estado pendiente")
        paid = api.request("PATCH", f'/api/v1/reservaciones/{reservation["id"]}/estado', {
            "estadoPago": "pagado"}, admin["token"])
        if paid["estadoPago"] != "pagado":
            raise AssertionError("No se confirmó la reservación")
        product_after = api.request("GET", f'/api/v1/productos/{product["id"]}', token=admin["token"])
        if product_after["stockTotal"] != 98:
            raise AssertionError("Inventario no refleja la venta sintética")
        item = api.request("POST", "/api/v1/carrito/articulos", {
            "productoId": product["id"], "cantidad": 1}, client["token"])
        report = api.request("POST", "/api/v1/reportes/guardar", {
            "nombre": tag, "descripcion": "Reporte sintético ZAP", "querySql": "SELECT 1 AS zap_probe"}, admin["token"])
        ids = {"tenant_id": api.tenant, "branch_id": branch["id"], "product_id": product["id"],
               "reservation_id": reservation["id"], "item_id": item["id"], "report_id": report["id"],
               "payment_id": payment["idMetodoPago"]}
        operations = []
        for operation in scope["profiles"][profile]:
            resolved = {key: render(value, ids) if key != "path" else value
                        for key, value in operation.items()}
            operations.append(resolved)
        original = api.request("GET", "/swagger/v1/swagger.json")
        filtered = filtered_openapi(original, operations, base)
        selected = {(o["method"], o["path"]) for o in operations}
        omitted = [{"method": method.upper(), "path": path,
                    "reason": scope["excludedGroups"].get(path.split("/")[3], scope["defaultExclusion"])}
                   for path, item_spec in original["paths"].items()
                   for method in item_spec if method in ("get", "post", "put", "patch", "delete", "head", "options")
                   and (method.upper(), path) not in selected]
        session = {"profile": profile, "base": base, "token": (admin if profile == "admin" else client)["token"],
                   "tenant": api.tenant, "operations": operations}
        write_json(work / "session.json", session)
        write_json(work / "openapi.json", filtered)
        write_json(output / f"openapi-{profile}.json", filtered)
        write_json(output / f"openapi-original-{profile}.json", original)
        write_json(output / f"scope-{profile}.json", {"selected": operations, "omitted": omitted})
        evidence.update(complete=True, ids=ids, assertions=["login-admin", "login-client", "reservation-pending",
                        "reservation-paid", "stock-100-to-98"],
                        limitation="Método contra entrega creado; V1 no enlaza metodoPagoId a la reservación.")
        return [password, admin["token"], client["token"]]
    finally:
        write_json(evidence_path, evidence)
