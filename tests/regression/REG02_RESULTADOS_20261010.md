# REG-02 — Resultado local del 10 de octubre de 2026

**Estado: PENDIENTE por configuración del entorno de prueba.**

- Caso: `test_reg02_concurrent_sales_do_not_oversell`
- Script ejecutado: `python3 backend/tests/regression/regression.py`
- Resultado observado: la prueba fue `skipped` con el mensaje: `Pendiente: configurar usuarios, producto y tenant de prueba.`

| Comprobación | Esperado | Obtenido |
|---|---:|---:|
| Dos compras simultáneas | 1 compra exitosa y 1 rechazada | No ejecutado |
| Inventario final | 0 sin negativos | No ejecutado |
| Resultado global | Pendiente | Pendiente |

## Alcance

Esta prueba valida RNF9 en el escenario mínimo: dos compras concurrentes sobre una sola unidad disponible. El objetivo es detectar sobreventa y asegurar que solo una operación se confirme.

## Observación

La prueba no se pudo ejecutar en este entorno porque faltan credenciales, tenant y producto de prueba configurados, además de la ruta real de la API para confirmar la venta con datos reales.
