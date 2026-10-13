# INT-01 — Respaldo automático y restauración

## Herramienta y alcance

Python 3 programa la ejecución; las herramientas de PostgreSQL 16 del servicio `db` generan y restauran el respaldo. No requiere instalar paquetes Python ni clientes PostgreSQL en el host.

Compose guarda PostgreSQL en `dmhub_postgres_data`. Se respalda la **base lógica dentro de ese volumen** mediante `pg_dump`, no copiando los archivos de un servidor en funcionamiento. Se usan los datos existentes, sin seed ni modificaciones de sus registros. La restauración se realiza en una base temporal con nombre único dentro del mismo servidor; solo esa base temporal se elimina al terminar.

## Ejecutar desde la raíz del repositorio

```bash
docker compose up -d db
python3 backend/tests/backups/int01.py --delay 5
```

Ejecutar cuando PostgreSQL esté listo y **sin aplicaciones u otros clientes escribiendo**. El script compara la fuente antes y después del respaldo; esa comprobación no sustituye un snapshot compartido si existen escrituras concurrentes. El volumen anterior `cc3090-inversiones-elohim-solution_postgres_data`, si existe, no se usa ni se elimina: se respeta el volumen del Compose actual.

El temporizador dispara automáticamente la prueba tras cinco segundos y el proceso termina. Para repetir cada 24 horas mientras el proceso permanezca activo:

```bash
python3 backend/tests/backups/int01.py --daily
```

La repetición diaria también restaura y valida cada copia. Este programador local se detiene al cerrar el proceso o ante un fallo; no instala un servicio ni sobrevive a reinicios. No se deja una ejecución diaria activa como parte del ensayo puntual.

## Criterios de aceptación

- El temporizador inicia el respaldo sin otra intervención.
- `pg_dump` y `pg_restore` terminan sin errores. La restauración recrea esquema y restricciones dentro de una transacción.
- Coinciden nombres de tablas de usuario, conteos y SHA-256 de todas sus filas ordenadas, incluida su multiplicidad.
- La fuente permanece igual entre las comprobaciones inicial y posterior al respaldo.

Una base vacía puede pasar recuperación técnica, pero se registra su cobertura limitada. No se verifican roles/permisos, comportamiento de la aplicación ni retención de 30 días. Los conteos y hashes se calculan en memoria por tabla: esta prueba está destinada a una base local de tamaño manejable.

## Evidencias

Cada ejecución genera `artifacts/<fecha UTC>/` con `database.dump`, `restore.log`, `source.json`, `restored.json` y `result.json`. El resultado indica `PASSED` o `FAILED`; un fallo devuelve código de salida distinto de cero. Los respaldos contienen datos locales y la carpeta está excluida de Git. No hay limpieza automática de copias: RNF5/REG-01 y su historial real de retención siguen pendientes.
