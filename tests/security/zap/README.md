# ZAP contra el backend temporal

Estos scripts ejecutan **escaneo activo**, crean usuarios/datos y modifican el
backend. Usar exclusivamente PostgreSQL/API descartables. No apuntar al backend
de desarrollo con datos que se quieran conservar. Solo se aceptan destinos HTTP
en localhost/127.0.0.1 con puerto explícito. Docker requiere Linux para `--network host`.

## Ejecución local aislada

Requisitos: Docker Engine + Compose v2, Python 3.10+ y Bash. Python del host usa
únicamente su biblioteca estándar; el cliente de ZAP está incluido en su imagen.
Desde la raíz del repositorio **backend**:

```bash
docker pull "$(cat tests/security/zap/image.txt)"
docker compose -p zap-local -f tests/security/zap/compose.test.yml up -d --build --wait
curl --fail --retry 30 --retry-all-errors --retry-delay 2 \
  http://localhost:15000/swagger/v1/swagger.json --output /dev/null

ZAP_BASE_URL=http://localhost:15000 bash tests/security/zap/run.sh

# Ejecutar también si falla el escaneo:
docker compose -p zap-local -f tests/security/zap/compose.test.yml down --volumes --remove-orphans
```

El script muestra la ubicación de los artefactos (`/tmp/zap-artifacts-*` por
defecto). `ZAP_OUTPUT_DIR` puede seleccionar otra carpeta **vacía**. Los secretos
se guardan aparte bajo `/tmp/zap-private-*`, con un directorio padre 0700, y se
eliminan al terminar. Los hijos montados son escribibles por el usuario `zap` del
contenedor. Los contenedores llevan nombres únicos y se eliminan incluso ante
error/timeout/SIGTERM. Una terminación forzosa del runner no permite garantizar
limpieza; el entorno de CI debe ser efímero.

La API y PostgreSQL pertenecen al workflow/Compose, **no al script**: el llamador
debe eliminarlos siempre. No se toca el entorno de desarrollo en el puerto 5000
al usar el Compose de este directorio. PostgreSQL usa almacenamiento temporal.

## Qué se comprueba

Cada perfil (`admin`, `client`) tiene una tienda nueva para evitar contaminación
entre escaneos. La preparación registra administrador y cliente, inicia ambas
sesiones, crea sucursal y producto con 100 unidades, crea un método contra entrega,
agrega dos unidades al carrito, crea una reservación pendiente y confirma su pago
como administrador. Comprueba que el stock queda en 98, repone un artículo en el
carrito y guarda un reporte `SELECT 1 AS zap_probe`.

**Contrato real de V1:** `POST /reservaciones` recibe `sucursalId` y opcionalmente
`stripeIntentId`, no `metodoPagoId`. El método contra entrega se crea y comprueba
por separado. No se afirma un enlace que esta API no implementa.
`GET /metodos-pago` lista únicamente métodos con identificador Stripe: para el
cliente nuevo se espera una lista vacía. Contra entrega se verifica con su POST.

`scope.json` selecciona 15 operaciones de administrador y 5 de cliente, con
status y datos esperados. Cada solicitud válida se envía **mediante ZAP**, con
Bearer y `X-Tenant-ID`, antes de importar OpenAPI. Se comprueba la respuesta y
después se ejecuta el análisis activo sobre la especificación filtrada. Las
cabeceras solo se añaden a la API del origen local.

Se incluyen productos, sucursales, inventarios (lectura/ajuste), carrito
(lectura/actualización), método contra entrega, reservaciones consultables y
reportes (incluida ejecución SQL). La preparación de Auth, creación de productos,
sucursal y reservación ocurre fuera de ZAP y **no cuenta como escaneo activo**.

Limitaciones explícitas de esta primera selección:

- Auth: registro/login funcional en preparación; no hay escaneo activo anónimo.
  Logout y recuperación necesitan sesiones/fixtures separados; Google requiere
  proveedor de pruebas.
- Media/Cloudinary y Pagos/Stripe no se escanean: requieren configuración,
  proveedor de pruebas y, para webhook, firma válida.
- No se escanean todas las escrituras ni eliminaciones. Añadirlas exige preparar
  fixtures y aserciones específicas, no solo ampliar la lista de rutas.
- Los ataques pueden invalidar los datos tras los baselines. Que el análisis
  termine no prueba que cada regla haya alcanzado cada flujo de negocio.
- No prueba TLS, proxy inverso ni configuración de producción (API Development).
- No sustituye las pruebas explícitas de roles, propiedad de recursos o aislamiento
  entre tiendas; conservar `tests/security/test_seg01_aislamiento.py` y ampliarlo.

Toda operación de Swagger no seleccionada queda enumerada en `scope-*.json`
con su motivo. El JSON original y el filtrado se conservan para comparar cambios.

## Política y evidencia

`policy.json` bloquea severidad High (`riskcode=3`) y los IDs listados, aun con
severidad inferior. Informational/Low/Medium no listados se reportan sin bloquear.
No hay excepciones implícitas ni `continue-on-error`. El código 2 del API scan
(warnings) solo se acepta cuando la evaluación de evidencia y hallazgos pasa.

La política incluye una excepción explícita autorizada por el responsable del
proyecto tras su comprobación manual: regla `40018`, método `POST`, ruta exacta
`/api/v1/reportes/ejecutar-raw`, parámetro `querySql`. La alerta se sigue generando
y permanece en HTML/JSON; el evaluador la registra en `gate.json.acceptedAlerts`
con el motivo. No se excluye el endpoint del escaneo ni otras reglas. Si una
misma alerta agrupa instancias en otras rutas, esas instancias siguen bloqueando.
La excepción documenta la decisión del proyecto, no una nueva demostración
automática de ausencia de vulnerabilidades.

También falla ante error del escáner, timeout, reportes ausentes/malformados,
preparación incompleta, baseline 401/403 o dato de negocio incorrecto, operación
obligatoria no observada, análisis activo inconcluso o cola pasiva pendiente.
No se exige que los payloads maliciosos devuelvan 2xx.

Artefactos:

- `report-{admin,client}.html/json`: reportes ZAP, con secretos conocidos/JWT redactados.
- `coverage-*.json`: baselines, aserciones, conteos por operación/status, reglas,
  progreso y finalización. `activeObserved` distingue los mensajes del escáner
  activo del resto del historial. No contiene cuerpos ni cabeceras.
- `preparation-*.json`: solicitudes preparatorias y aserciones funcionales.
- `openapi-*.json`, `scope-*.json`: inventario y exclusiones.
- `execution-*.json`, `scanner-*.log`, `manifest.json`, `gate.json`: ejecución y decisión.

`gate.json.coverageLimitations` enumera operaciones sin mensajes activos
coincidentes. El control exige baseline funcional para todas las seleccionadas
y tráfico activo en cada perfil, pero no exige que API-Minimal ataque cada ruta
sin parámetros. Esas rutas no se presentan como atacadas: conservan evidencia
funcional/pasiva. Ampliar la política de escaneo y las aserciones si se requiere
cobertura activa obligatoria para cada una de ellas.

El escaneo no es determinista en tiempos/IDs: imagen por digest, complementos sin
actualización automática (`-silent`), configuración versionada y datos sintéticos
permiten repetir el procedimiento. `coverage-*.json` registra versión y reglas.
Para actualizar la imagen, descargar `ghcr.io/zaproxy/zaproxy:stable`, obtener su
RepoDigest con `docker image inspect` y reemplazar `image.txt` tras validar.

## Integración en CI

`.github/workflows/ci.yml` ejecuta las pruebas del evaluador y el escaneo después
de las pruebas de integración y antes de exportar la imagen. El job `validate`
tiene 90 minutos y ZAP tiene 35 minutos (900 segundos por perfil).

Se publica un resumen PASS/FAIL con cantidades de errores, hallazgos bloqueantes,
excepciones aplicadas y limitaciones. El artefacto `backend-zap-security` incluye
HTML/JSON, logs y cobertura, con retención de siete días y `if: always()`.

Un fallo del control impide exportar/subir `backend-image`. El CD existente
exige un CI exitoso. No se usa `continue-on-error`.

La limpieza propia del runner se complementa con un paso `always()` que elimina
solo contenedores con la etiqueta `elohim.zap.run` de la ejecución/intento actual
(`ZAP_RUN_ID`). Se conservan los pasos `always()` de logs y eliminación del
Compose, incluidos sus volúmenes. No publicar `/tmp/zap-private-*` ni el
directorio privado de trabajo de ZAP.

## Pruebas del control sin Docker

```bash
python3 -m unittest discover -s tests/security/zap -p 'test_*.py' -v
```

Prueban que errores, timeout, 401, cobertura incompleta, reportes inválidos y
hallazgos bloqueantes no se conviertan en éxito. No sustituyen una ejecución real.

Referencias: [API scan](https://www.zaproxy.org/docs/docker/api-scan/),
[scan hooks](https://www.zaproxy.org/docs/docker/scan-hooks/),
[Replacer](https://www.zaproxy.org/docs/desktop/addons/replacer/).

## Validación realizada el 2026-10-23

Se construyó el backend del checkout y se ejecutó el Compose aislado en :15000
con PostgreSQL nuevo. Con ZAP 2.17.0 y el digest de `image.txt`:

| Perfil | Baselines válidos | Mensajes activos | Operaciones con mensajes activos coincidentes |
| --- | ---: | ---: | ---: |
| Administrador | 15/15 | 2245 | 9 |
| Cliente | 5/5 | 632 | 1 |

Ambos análisis activos y pasivos terminaron y generaron HTML/JSON. Las diez
operaciones sin mensajes activos coincidentes figuran como limitación; no se
equiparan veinte baselines correctos con veinte operaciones atacadas.

Con la política original, el control devolvió **FAIL**, sin errores de ejecución, por la alerta High
`40018`, `SQL Injection - PostgreSQL`, en `POST /api/v1/reportes/ejecutar-raw`,
parámetro `querySql`. Necesita triage: la ruta admite SQL por diseño y el hallazgo
automático no demuestra por sí solo un acceso indebido. En esa ejecución no se aplicó una excepción. Posteriormente el responsable
autorizó la excepción específica descrita en la política actual.

Las 14 pruebas offline pasaron. Se eliminaron los contenedores ZAP, backend y
PostgreSQL de prueba; el entorno de desarrollo existente permaneció activo.
Los artefactos locales de esta ejecución quedaron en `/tmp/zap-validation-05`
(temporales, no versionados).
