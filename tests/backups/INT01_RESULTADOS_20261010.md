# INT-01 — Resultado local del 10 de octubre de 2026

**Estado: APROBADA para respaldo y restauración local programados.**

- Backend de referencia: `a74c290`, con script de prueba agregado en el árbol de trabajo.
- Entorno: servicio `db` de Docker Compose, imagen `postgres:16-alpine`, base `dmhub`, volumen `dmhub_postgres_data`.
- Ejecución: `python3 backend/tests/backups/int01.py --delay 5`.
- Inicio automático: `2026-10-10 14:20:55 UTC`, después de cinco segundos de temporizador.
- Respaldo generado: **18 177 045 bytes**.
- Verificación: **29 tablas y 487 223 filas**; conteos y hashes coincidentes entre fuente y restauración.
- La fuente coincidió antes y después del respaldo; backend/frontend estaban detenidos.
- `pg_restore` terminó sin errores, recreando esquema y restricciones. La base temporal se eliminó al finalizar.
- PostgreSQL quedó encendido; no quedó un proceso diario de respaldo activo.

## Evidencia local

Carpeta `artifacts/20261010T142055005422Z/`: `database.dump`, `restore.log`, `source.json`, `restored.json` y `result.json`. Excluida de Git porque el respaldo contiene los datos locales.

Hubo un primer ensayo fallido por una referencia incorrecta a un alias SQL en el comparador. Se corrigió y se repitió el caso completo; el resultado anterior permanece en `artifacts/20261010T142020894885Z/`.

## Límites

Se verificó recuperación lógica de la base existente, sin agregar datos sintéticos. No se comprobaron roles/permisos ni el funcionamiento de la aplicación contra la copia. El programador es un proceso Python local; la continuidad tras reinicios y el historial real de 30 días siguen pendientes. Este resultado no acredita RNF5 completo ni REG-01.
