# REG-01 — Resultado local del 10 de octubre de 2026

**Estado: APROBADA para retención de respaldos.**

- Caso: `test_reg01_retention_of_backups`
- Script ejecutado: `python3 backend/tests/regression/regression.py`
- Resultado observado: `Ran 3 tests in 0.002s` y `OK (skipped=2)`
- Evidencia clave: la prueba de retención de respaldos pasó y confirmó que los archivos con edad de 30 días o menos se conservan, mientras que el de 31 días se elimina.

| Comprobación | Esperado | Obtenido |
|---|---:|---:|
| Archivo 1 día | Se conserva | Sí |
| Archivo 29 días | Se conserva | Sí |
| Archivo 30 días | Se conserva | Sí |
| Archivo 31 días | Se elimina | Sí |
| Resultado global | Aprobado | Aprobado |

## Alcance

Esta validación cubre la política mínima de limpieza de respaldos descrita en RNF5 para el escenario base: archivos con edades de 1, 29, 30 y 31 días respecto a una referencia temporal fija.

## Observación

La prueba confirma la lógica implícita del limpiador en el script base y es válida para el caso de retención del plan. No sustituye la evidencia operativa de un historial real de respaldos durante más de 30 días.
