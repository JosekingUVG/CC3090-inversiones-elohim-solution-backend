# INT-03 — Resultado local del 10 de octubre de 2026

**Estado: APROBADA para actualización visible entre dos sesiones de API.**

- Backend de referencia: `58c5147`.
- API: `http://localhost:5000`; PostgreSQL del volumen `dmhub_postgres_data`.
- Ejecución formal: `python3 backend/tests/integration/int03.py`.
- Ejecución de la batería de CI: `INT-02` e `INT-03` aprobadas consecutivamente.

| Comprobación | Esperado | Obtenido |
|---|---:|---:|
| Sesión B obtiene el nombre inicial | Sí | Sí |
| A actualiza el nombre | HTTP 200 | HTTP 200 |
| B lee el último valor confirmado | ≤ 2 000 ms | **4,710 ms** |
| Valor leído por B | Nombre actualizado | Coincide |
| Valor persistido en PostgreSQL | Nombre actualizado | Coincide |

La ejecución conserva su tienda y producto sintéticos para inspección. La evidencia JSON está en `artifacts/20261010T145043369918Z/result.json` y queda excluida de Git porque pertenece al entorno local.

## Alcance

La prueba abre dos sesiones autenticadas independientes del mismo administrador, no dos navegadores físicos. Mide la primera lectura correcta de la sesión B después de la actualización confirmada de A. No prueba actualización automática sin una nueva lectura, caché del navegador ni escrituras simultáneas; esos casos quedan fuera de INT-03.
