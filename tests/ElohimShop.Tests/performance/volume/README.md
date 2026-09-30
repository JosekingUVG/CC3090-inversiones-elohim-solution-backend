# VOL-01 — Catálogo creciente, JMeter 5.6.3 + Java 26.0.2.1

Mismo patrón que carga y estrés: archivos `.jmx`, datos CSV en `performance/data/` y componentes nativos de JMeter. No requiere Python, Groovy, plugins ni otra versión de Java.

- `catalog-prepare.jmx`: prepara los productos y sus inventarios mediante la API. Ejecutar una sola vez por nivel.
- `catalog-volume.jmx`: mide listado y detalle con un usuario, 200 iteraciones y pausa de un segundo después de cada par. Ejecutar tres veces por nivel.
- `../data/volume.example.csv`: plantilla de configuración.
- `../data/volume-users.csv`: credenciales de pruebas generadas automáticamente.

## 1. Inicio automático, sin tenant previo

Desde `backend/tests/ElohimShop.Tests/performance/`:

```bash
bash scripts/run-volume.sh prepare
bash scripts/run-volume.sh run
```

Si `data/volume.csv` no existe o todavía tiene `ID_TENANT_PRUEBAS`, `prepare` ejecuta primero `setup-volume.sh`. Este registra un administrador exclusivo de pruebas: la API crea su tienda y sucursal principal. Después crea la segunda sucursal y guarda automáticamente:

- `data/volume.csv`: IDs reales, 1 000 productos y 200 iteraciones por defecto.
- `data/volume-users.csv`: credenciales de la nueva cuenta; no reemplaza `data/users.csv` de carga y estrés.
- `data/volume-setup.json`: estado de recuperación y credenciales de bootstrap, protegido con permisos 600 y excluido de Git.

El bootstrap usa Bash, curl y jq, disponibles en el equipo; las pruebas siguen ejecutándose con JMeter nativo, sin Python ni Groovy. No envía correos ni invoca integraciones de pago. Conserva el estado si falla para recuperar la misma cuenta y evitar crear otra tienda accidentalmente. No borrar el estado sin revisar los recursos que ya se crearon.

Si el CSV ya contiene IDs reales, se reutilizan y no se crea otro tenant. La preparación de productos exige catálogo vacío. Si ya se preparó el nivel, ejecutar solo `run`. La selección de credenciales prioriza `USERS_FILE` explícito; después `volume-users.csv` si existe y, por último, `users.csv`.

Para crear únicamente tenant y sucursales:

```bash
bash scripts/setup-volume.sh
```

Para elegir el volumen inicial antes del primer bootstrap:

```bash
PRODUCT_COUNT=1000 ITERATIONS=200 bash scripts/run-volume.sh prepare
```

La segunda orden (`run`) calienta, mide y crea su reporte HTML. El script imprime la ruta de `index.html`; si falla el calentamiento, genera un HTML de diagnóstico y cancela la medición. p95 se revisa en el dashboard.

Para abrir la GUI en Java 26 con el tema Metal:

```bash
bash scripts/run-volume.sh gui-prepare
bash scripts/run-volume.sh gui-run
```

Ejecutar antes `setup-volume.sh` si todavía faltan los IDs. Cerrar una GUI antigua que siga usando Darklaf. `chmod 600` protege las credenciales; el bootstrap ya aplica esos permisos, sin que tengas que ejecutar ese comando manualmente.

### Configuración manual opcional

`data/volume.example.csv` sirve para reutilizar un tenant vacío existente, reemplazando los IDs de ejemplo. Formato:

```csv
tenant_id,product_count,iterations,branch_a,branch_b
ID_REAL,1000,200,SUCURSAL_REAL_A,SUCURSAL_REAL_B
```

Los IDs de sucursales deben ser distintos y pertenecer al tenant. Para usar credenciales existentes: `USERS_FILE="$PWD/data/users.csv" bash scripts/run-volume.sh prepare`. Debe ser una cuenta autorizada para crear productos allí.

El CSV contiene configuración, no 1 000 filas. El contador nativo de JMeter genera los nombres y SKU. `products.csv` de estrés contiene únicamente `case_id/default`, no un catálogo para insertar.

## 2. Preparar el catálogo desde la GUI

Abrir `volume/catalog-prepare.jmx` en JMeter (File → Open). Las rutas CSV predeterminadas son `../data/volume.csv` y `../data/volume-users.csv`, relativas al directorio del `.jmx`.

1. En **HTTP Request Defaults**, verificar protocolo, host y puerto. Predeterminados: `http`, `localhost`, `5000`.
2. Revisar ambos elementos **CSV Data Set Config**.
3. Iniciar con el botón verde.

El flujo es: login → comprobar catálogo vacío → comprobar sucursales distintas y existentes → crear `product_count` productos → comprobar total final.

Cada producto tiene precio mayorista 10, precio al detalle 15 y stock 10/20 en las dos sucursales. Se comprueban HTTP 201, SKU, tenant, dos inventarios y stock total 30. Un error detiene la prueba. La preparación **no es la medición de volumen**.

Para depurar puede agregarse temporalmente **View Results Tree**. La preparación maneja credenciales y token: no compartir capturas ni guardar cuerpos de login. Si se interrumpe, no volver a ejecutar sobre el catálogo parcial: restaurar el snapshot vacío del entorno de pruebas antes de repetir. No hay borrado automático.

## 3. Medir desde la GUI

Abrir `volume/catalog-volume.jmx` y revisar HTTP defaults y el CSV. El flujo se repite según `iterations`:

1. `GET /api/v1/productos`: HTTP 200, JSON válido, conteo esperado y tenant correcto.
2. El **JSON Extractor** selecciona al azar un ID del listado; se verifica que aparezca una sola vez.
3. `GET /api/v1/productos/{id}`: HTTP 200, ID, tenant, precios e inventarios esperados.
4. **Flow Control Action** pausa un segundo, sin generar una muestra HTTP adicional.

**JSON JMESPath Assertion** es una aserción nativa visible en el árbol de JMeter para comprobar campos JSON. No es un script Groovy ni un programa externo.

Se muestrean IDs al azar, con posibles repeticiones; no implica consultar individualmente todos los productos. Las aserciones detectan conteos incorrectos, datos de otro tenant y duplicados del ID muestreado. **No certifican la unicidad de todos los IDs ni la ausencia de reemplazos compensados por duplicados no muestreados**. La auditoría completa de IDs del plan queda como comprobación adicional de base de datos antes y después de las corridas; no declararla aprobada solo por ver 0 errores en JMeter.

Para un ensayo corto, copiar el CSV con `iterations=5`, o iniciar JMeter con `-Jiterations=5`. Para medición formal usar CLI, sin View Results Tree.

## 4. Ejecución formal en terminal

Desde `performance/`, usar rutas absolutas para los CSV:

```bash
mkdir -p results/volume/V1-run1 reports/volume
```

Preparar una sola vez, si todavía no se hizo desde GUI:

```bash
jmeter -n -t volume/catalog-prepare.jmx \
  -Jvolume_file="$PWD/data/volume.csv" \
  -Jusers_file="$PWD/data/volume-users.csv" \
  -l results/volume/V1-run1/prepare.jtl \
  -j results/volume/V1-run1/prepare.log
```

Confirmar cero errores y el total final correcto. **Un código de salida 0 de JMeter no garantiza que pasaron las aserciones.**

Calentar por separado, con 10 iteraciones:

```bash
jmeter -n -t volume/catalog-volume.jmx \
  -Jvolume_file="$PWD/data/volume.csv" -Jiterations=10 \
  -l results/volume/V1-run1/warmup.jtl \
  -j results/volume/V1-run1/warmup.log
```

Medir con las 200 iteraciones del CSV:

```bash
jmeter -n -t volume/catalog-volume.jmx \
  -Jvolume_file="$PWD/data/volume.csv" \
  -l results/volume/V1-run1/results.jtl \
  -j results/volume/V1-run1/jmeter.log \
  -e -o reports/volume/V1-run1
```

Si host o puerto difieren, añadir `-Jprotocol=http -Jhost=HOST -Jport=PUERTO` a todos los comandos. El timeout de respuesta es 30 segundos, configurable con `-Jresponse_timeout`; `-Jpause_ms` modifica la pausa, inicialmente 1000 ms.

Los directorios HTML deben ser nuevos o estar vacíos; los `.jtl` tampoco deben reutilizarse porque JMeter puede acumular muestras. Para la segunda y tercera corrida cambiar `V1-run1` por `V1-run2` y `V1-run3`, repetir calentamiento y medición, **sin preparar nuevamente**.

## 5. Interpretar resultados

Abrir `reports/volume/V1-run1/index.html`. Revisar por separado:

| Etiqueta | Muestras | Error % | Percentil 95 |
|---|---:|---:|---:|
| `Catalogo - listado` | 200 | 0 % | ≤ 2 000 ms |
| `Catalogo - detalle` | 200 | 0 % | ≤ 1 000 ms |

El total formal debe ser 400; las 20 solicitudes de calentamiento están en otro archivo. La preparación tampoco se incluye. Si la prueba se detiene por una aserción, la corrida queda incompleta y no pasa.

Los umbrales p95 se revisan en el dashboard; no hay un evaluador externo. Comprobar además integridad completa, CPU, RAM, disco y ausencia de reinicios según el plan. La monitorización puede hacerse con los scripts existentes de `../scripts/` y `docker stats`; no está integrada en estos `.jmx`.

## 6. Cambiar de nivel

| Nivel | `product_count` | Inventarios esperados | `iterations` | GET medidos por corrida |
|---|---:|---:|---:|---:|
| V1 | 1 000 | 2 000 | 200 | 400 |
| V2 | 10 000 | 20 000 | 200 | 400 |
| V3 | 50 000 | 100 000 | 200 | 400 |
| V4 | 100 000 | 200 000 | 200 | 400 |

Restaurar un catálogo vacío, cambiar `product_count` en el CSV y preparar el nuevo nivel. No interpretar V2 como agregar 10 000 sobre los 1 000 existentes. Conservar snapshots por nivel permite repetir mediciones sin volver a generar todo.

La preparación es secuencial por API y puede tardar con volúmenes grandes. Tanto JMeter como la API procesan el listado completo en memoria; detener si el host se queda sin recursos. No convertir un fallo del generador en una conclusión sobre la capacidad del backend.

## Validación de compatibilidad

Verificados con JMeter 5.6.3 y Java 26.0.2.1 contra una API simulada local: creación de cinco productos con SKU únicos, tres iteraciones con seis GET, rechazo de catálogo no vacío y detección de stock alterado. No se cargaron datos en la aplicación real. La capacidad de tu backend sigue pendiente de las corridas reales.

## Diagnóstico de errores

- **Connection refused:** el host/puerto indicado no acepta conexiones; verificar publicación de puertos y disponibilidad.
- **404 al login con “La tienda solicitada no existe o no está disponible”:** revisar `tenant_id`. `ID_TENANT_PRUEBAS` es un marcador de ejemplo, no un tenant válido. El script lo rechaza antes de ejecutar.
- **`Property HTTPsampler.Arguments is unset`:** faltaba la colección vacía de argumentos en HTTP Defaults. Corregida en ambos planes; reabrir la versión actual.
- **`NoClassDefFoundError: java/applet/Applet` en Darklaf:** abrir con Metal mediante los comandos anteriores. Es un problema del tema visual con Java 26, independiente de los contenedores.
- **No aparece HTML:** el modo GUI y un comando que solo incluye `-l` no generan dashboard automáticamente. Usar el script o `-e -o` en CLI.
