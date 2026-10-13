#!/usr/bin/env python3
"""INT-03: una actualización confirmada en la sesión A debe aparecer en B."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request
import uuid


COMPOSE_FILE = os.getenv("INTEGRATION_COMPOSE_FILE")


def compose_command():
    """Devuelve el comando Compose del entorno actual, local o CI."""
    project_directory = Path(os.getenv("INTEGRATION_COMPOSE_DIRECTORY", Path.cwd())).resolve()
    command = ["docker", "compose", "--project-directory", str(project_directory)]
    if COMPOSE_FILE:
        compose_file = Path(COMPOSE_FILE)
        if not compose_file.is_absolute():
            compose_file = project_directory / compose_file
        command.extend(["-f", str(compose_file)])
    return command


def check(condition, message):
    if not condition:
        raise AssertionError(message)


class Int03:
    def __init__(self, base_url, report):
        self.base_url = base_url.rstrip("/")
        self.report = report
        self.tenant_id = None

    def request(self, method, path, body=None, token=None, timeout=15):
        headers = {"Accept": "application/json"}
        if self.tenant_id:
            headers["X-Tenant-ID"] = self.tenant_id
        if token:
            headers["Authorization"] = f"Bearer {token}"
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"

        request = urllib.request.Request(self.base_url + path, data=data, headers=headers, method=method)
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                payload = json.load(response)
                status = response.status
        except urllib.error.HTTPError as error:
            self.report["http"].append({"method": method, "path": path, "status": error.code})
            raise RuntimeError(f"{method} {path}: HTTP {error.code}") from None

        self.report["http"].append({
            "method": method,
            "path": path,
            "status": status,
            "duration_ms": round((time.perf_counter() - started) * 1000, 3),
        })
        return payload

    def execute(self):
        tag = f"int03-{uuid.uuid4().hex[:12]}"
        initial_name = f"{tag}-inicial"
        updated_name = f"{tag}-actualizado"
        password = secrets.token_urlsafe(24) + "aA1!"
        email = f"{tag}@example.com"

        registered = self.request("POST", "/api/v1/auth/register", {
            "correo": email,
            "nombre": tag,
            "contrasena": password,
            "tipoUsuario": "administrador",
            "rol": "administrador",
            "direccion": "INT-03 local",
            "telefono": "+50200000000",
        })
        registration_token = registered.get("token") or registered.get("accessToken")
        check(registration_token, "Registro de administrador sin token")

        stores = self.request("GET", "/api/v1/tiendas", token=registration_token)
        check(len(stores) == 1 and stores[0].get("id"), "No se encontró la tienda de prueba")
        self.tenant_id = stores[0]["id"]
        self.report["tenant_id"] = self.tenant_id

        # Dos logins producen los dos contextos autenticados independientes A y B.
        session_a = self.request("POST", "/api/v1/auth/login", {"correo": email, "contrasena": password})
        session_b = self.request("POST", "/api/v1/auth/login", {"correo": email, "contrasena": password})
        token_a = session_a.get("token") or session_a.get("accessToken")
        token_b = session_b.get("token") or session_b.get("accessToken")
        check(token_a and token_b, "Una de las sesiones no recibió token")

        branches = self.request("GET", "/api/v1/sucursales", token=token_a)
        check(len(branches) == 1 and branches[0].get("id"), "No se encontró sucursal para la prueba")
        branch_id = branches[0]["id"]
        product = self.request("POST", "/api/v1/productos", {
            "nombre": initial_name,
            "sku": tag,
            "descripcion": "Producto de prueba INT-03",
            "precioMayoreo": 10,
            "precioDetalle": 15,
            "publicado": True,
            "stockMinimo": 0,
            "stockSucursales": [{"sucursalId": branch_id, "stock": 1}],
        }, token_a)
        product_id = product["id"]
        path = f"/api/v1/productos/{product_id}"
        self.report.update(product_id=product_id, branch_id=branch_id,
                           initial_name=initial_name, updated_name=updated_name)

        before = self.request("GET", path, token=token_b)
        check(before["nombre"] == initial_name, "La sesión B no muestra el valor inicial")

        updated = self.request("PUT", path, {
            "nombre": updated_name,
            "sku": tag,
            "descripcion": "Producto de prueba INT-03",
            "precioMayoreo": 10,
            "precioDetalle": 15,
            "publicado": True,
            "stockMinimo": 0,
        }, token_a)
        confirmed_at = time.perf_counter()
        check(updated["nombre"] == updated_name, "La sesión A no confirmó el cambio")

        observations = []
        while True:
            remaining = 2 - (time.perf_counter() - confirmed_at)
            check(remaining > 0, "La sesión B no mostró la actualización en <= 2 s")
            observed = self.request("GET", path, token=token_b, timeout=remaining)
            elapsed = time.perf_counter() - confirmed_at
            observations.append({"elapsed_ms": round(elapsed * 1000, 3), "nombre": observed["nombre"]})
            if observed["nombre"] == updated_name:
                break
            time.sleep(min(0.1, max(0, 2 - elapsed)))

        self.report["observations"] = observations
        self.report["read_delay_ms"] = observations[-1]["elapsed_ms"]
        check(self.report["read_delay_ms"] <= 2000, "Actualización leída fuera del límite")

        # Verificación independiente de persistencia en PostgreSQL del Compose local o CI.
        query = (
            "SELECT json_build_object('id', id, 'nombre', nombre) "
            "FROM \"Producto\" WHERE id = "
            + "'" + product_id.replace("'", "''") + "';"
        )
        command = compose_command() + [
            "exec", "-T", "db",
            "sh", "-ec", "exec psql -X -v ON_ERROR_STOP=1 -At -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\"",
        ]
        result = subprocess.run(command, input=query.encode(), stdout=subprocess.PIPE, check=True)
        persisted = json.loads(result.stdout)
        self.report["database"] = persisted
        check(persisted == {"id": product_id, "nombre": updated_name},
              "El valor leído no coincide con el valor persistido")
        self.report["status"] = "PASSED"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5000")
    args = parser.parse_args()
    check(args.base_url.rstrip("/") in ("http://localhost:5000", "http://127.0.0.1:5000"),
          "INT-03 requiere la API local de Compose para verificar PostgreSQL")

    os.umask(0o077)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    folder = Path(__file__).parent / "artifacts" / stamp
    folder.mkdir(parents=True)
    report = {
        "case": "INT-03",
        "started_utc": stamp,
        "status": "FAILED",
        "base_url": args.base_url,
        "threshold_ms": 2000,
        "poll_interval_ms": 100,
        "http": [],
        "cleanup": "Tenant and product are retained as test evidence; no existing data is changed.",
    }
    try:
        Int03(args.base_url, report).execute()
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        (folder / "result.json").write_text(json.dumps(report, indent=2))
        print(f"INT-03: {report['status']}; evidencia: {folder / 'result.json'}", flush=True)
        if report["status"] == "PASSED":
            print(f"Sesión B actualizada en {report['read_delay_ms']} ms")


if __name__ == "__main__":
    main()
