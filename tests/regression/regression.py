#!/usr/bin/env python3
"""Batería mínima de regresión para RNF5, RNF9 y RNF12.

Ejecutar con: python3 tests/regression/regression.py

Los casos usan datos y rutas de ejemplo para mantener este primer borrador
simple. Antes de incluirlos en CI deben configurarse credenciales, URLs y la
limpieza de los datos de prueba.
"""

from __future__ import annotations

import concurrent.futures
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
import urllib.request


BASE_URL = os.getenv("REGRESSION_BASE_URL", "http://localhost:5000").rstrip("/")


def api(method: str, path: str, body: dict | None = None) -> tuple[int, dict]:
    """Cliente HTTP mínimo. Pendiente: agregar token y X-Tenant-ID reales."""
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"} if data else {}
    request = urllib.request.Request(BASE_URL + path, data=data, headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=10) as response:
        return response.status, json.loads(response.read() or b"{}")


class RegressionTests(unittest.TestCase):
    def test_reg01_retention_of_backups(self) -> None:
        """RNF5: los archivos de 30 días o menos no se eliminan."""
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            now = time.time()
            files = {
                "backup-1.dump": 1,
                "backup-29.dump": 29,
                "backup-30.dump": 30,
                "backup-31.dump": 31,
            }
            for name, age in files.items():
                file = folder / name
                file.write_text("respaldo de prueba")
                timestamp = now - age * 24 * 60 * 60
                os.utime(file, (timestamp, timestamp))

            # Versión sencilla del limpiador. Sustituir por el limpiador real
            # cuando el respaldo automático esté implementado.
            for file in folder.glob("*.dump"):
                age_days = (now - file.stat().st_mtime) / (24 * 60 * 60)
                if age_days > 30:
                    file.unlink()

            self.assertTrue((folder / "backup-1.dump").exists())
            self.assertTrue((folder / "backup-29.dump").exists())
            self.assertTrue((folder / "backup-30.dump").exists())
            self.assertFalse((folder / "backup-31.dump").exists())

    @unittest.skip("Pendiente: configurar usuarios, producto y tenant de prueba.")
    def test_reg02_concurrent_sales_do_not_oversell(self) -> None:
        """RNF9: dos compras simultáneas no deben vender más de una unidad."""
        product_id = "PRODUCT_ID_DE_PRUEBA"
        branch_id = "SUCURSAL_ID_DE_PRUEBA"

        def buy() -> tuple[int, dict]:
            return api(
                "POST",
                "/api/v1/reservaciones",
                {"productoId": product_id, "sucursalId": branch_id, "cantidad": 1},
            )

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            results = list(executor.map(lambda _: buy(), range(2)))

        successful = [result for result in results if result[0] in (200, 201)]
        self.assertEqual(len(successful), 1)

        _, product = api("GET", f"/api/v1/productos/{product_id}")
        self.assertEqual(product["stockTotal"], 0)

    @unittest.skip("Pendiente: configurar dos sesiones autenticadas y producto de prueba.")
    def test_reg03_sessions_show_latest_update_after_reload(self) -> None:
        """RNF12: una sesión debe ver el último cambio confirmado al recargar."""
        product_id = "PRODUCT_ID_DE_PRUEBA"

        status, _ = api(
            "PUT",
            f"/api/v1/productos/{product_id}",
            {"nombre": "Producto versión 1", "precioMayoreo": 10, "precioDetalle": 15},
        )
        self.assertEqual(status, 200)

        _, session_b_view = api("GET", f"/api/v1/productos/{product_id}")
        self.assertEqual(session_b_view["nombre"], "Producto versión 1")

        status, _ = api(
            "PUT",
            f"/api/v1/productos/{product_id}",
            {"nombre": "Producto versión 2", "precioMayoreo": 10, "precioDetalle": 15},
        )
        self.assertEqual(status, 200)

        _, session_a_view = api("GET", f"/api/v1/productos/{product_id}")
        self.assertEqual(session_a_view["nombre"], "Producto versión 2")


if __name__ == "__main__":
    unittest.main(verbosity=2)
