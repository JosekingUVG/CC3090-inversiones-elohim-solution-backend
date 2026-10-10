# INT-02 — Resultado local del 10 de octubre de 2026

**Estado: APROBADA para venta e inventario del catálogo por API.**

- Backend de referencia: `ef49036`, compilado en Docker; script de prueba agregado en el árbol de trabajo.
- API: `http://localhost:5000`; PostgreSQL del volumen `dmhub_postgres_data`.
- Comando: `python3 backend/tests/integration/int02.py`.
- Inicio: `2026-10-10 14:28:40 UTC` (08:28:40 en Guatemala).
- Producto sintético: `int02-bb9ddd1637d5`.

| Comprobación | Esperado | Obtenido |
|---|---|---|
| Stock inicial | 10 | 10 |
| Stock después de reservar, antes de pagar | 10 | 10 |
| Unidades de la venta | 3 | 3 |
| Inventario de sucursal y stock global en BD | 7 / 7 | 7 / 7 |
| Stock en detalle y listado de catálogo | 7 / 7 | 7 / 7 |
| Lectura correcta del detalle tras confirmar pago | ≤ 2 000 ms | **6,086 ms** |
| Ventas / pagadas / detalles en BD | 1 / 1 / 1 | 1 / 1 / 1 |
| Stock después de repetir confirmación | 7 | 7 |

## Evidencia

[Resultado JSON local](artifacts/20261010T142840226948Z/result.json), excluido de Git: contiene las 17 solicitudes HTTP, sus estados/tiempos, identificadores y consulta SQL de verificación. No contiene tokens ni contraseñas.

- Tienda: `cb8860d6-fdfd-486b-84ff-e12fea1310c0`.
- Producto: `673e0361-e42c-4a6b-9873-0834132c8640`.
- Reservación: `8a9a8640-aee7-44f4-9ddb-e8c73106cea9`.

## Alcance

Una ejecución, sin carga ni concurrencia. El tiempo incluye la primera consulta del detalle después de recibir la confirmación, no es un promedio. Se utilizó confirmación manual por administrador, sin Stripe ni cobros reales. La prueba valida la API consumida por el frontend, no el renderizado o la caché del navegador.

Se conservaron la tienda y sus registros sintéticos para inspección. Backend y PostgreSQL quedaron ejecutándose. No se modificó código de negocio; se agregaron la prueba y su documentación. REG-02 continúa pendiente para cubrir ventas concurrentes de RNF9.
