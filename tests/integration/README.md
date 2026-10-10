# INT-02 — Venta e inventario del catálogo

## Herramienta y entorno

Python 3, usando su cliente HTTP estándar, ejecuta el flujo real de la API. PostgreSQL se consulta al final mediante `psql` del servicio `db` de Docker Compose. No requiere dependencias Python adicionales ni navegador: este caso es integración API/BD, por lo que se simplifica la propuesta de Playwright del plan.

La API debe corresponder al código actual y usar la BD local del volumen `dmhub_postgres_data`. Para iniciar desde la raíz:

```bash
SEED_DATA=false docker compose up -d --build backend
python3 backend/tests/integration/int02.py
```

Esperar a que la API termine su arranque antes de ejecutar el script. El script admite únicamente `localhost:5000` o `127.0.0.1:5000` porque la verificación SQL apunta a la BD local de Compose.

## Datos y flujo

Cada ejecución registra un administrador y un cliente sintéticos en una tienda nueva identificada con `int02-...`, usando credenciales aleatorias que no se guardan. Se conserva el conjunto de prueba para inspeccionar la evidencia; los datos de otras tiendas no se modifican. Los reintentos generan conjuntos nuevos.

1. Crear un producto publicado con 10 unidades en una sucursal.
2. Añadir 3 al carrito del cliente y crear una reservación pendiente, sin Stripe.
3. Verificar que la reservación pendiente todavía mantiene stock 10.
4. El administrador marca el pago como `pagado` por `PATCH /api/v1/reservaciones/{id}/estado`.
5. Consultar el detalle público hasta observar stock 7, y verificar listado e inventario.
6. Consultar las compras para comprobar una sola venta pagada con un detalle de 3 unidades.
7. Repetir la confirmación del mismo pago y verificar que el stock sigue en 7.
8. Consultar PostgreSQL, solo lectura, para contrastar stock global/sucursal, venta y detalle.

## Criterios y medición

- **10 − 3 = 7** en inventario de sucursal, catálogo y `Producto.stock_actual`.
- Una sola venta pagada y un detalle de 3 unidades en la tienda de prueba.
- Primera lectura correcta del detalle en **≤ 2 000 ms** desde la recepción de la confirmación de pago. Límite propuesto por el plan.
- Sondeo cada 100 ms entre lecturas incorrectas; incluye latencia de lectura HTTP, y se detiene al cumplirse el plazo. Es una observación local, no un promedio ni una prueba de carga.
- Repetir la confirmación no genera un segundo descuento.

El detalle `/api/v1/productos/{id}` es el que consume `frontend/src/lib/api/productos.ts`; tanto este como el listado `/api/v1/productos` utilizan `PlatformService`. `CatalogService` no tiene una ruta expuesta en los controladores actuales. El límite temporal se mide en el detalle; listado e inventario se verifican después por consistencia.

## Evidencia y límites

`artifacts/<fecha UTC>/result.json` registra IDs, solicitudes sin tokens ni contraseñas, tiempos, observaciones y valores SQL. El script devuelve un código distinto de cero si falla y conserva el resultado parcial. La carpeta está excluida de Git.

La prueba comprueba la confirmación manual de pago local; no realiza cobros ni evalúa Stripe, interfaz, cachés del navegador o ventas concurrentes. Estas últimas corresponden a REG-02. Un pase de INT-02 acredita este escenario de RNF9, no su cobertura completa.
