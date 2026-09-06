# Pruebas de rendimiento JMeter

Esta carpeta implementa el plan `PLAN_PRUEBAS_RENDIMIENTO_JMETER.md` sin plugins externos:

- `load/auth-cart-load.jmx`: NF-01, login una vez por hilo y consultas repetidas al carrito.
- `stress/products-stress.jmx`: NF-02, ocho grupos independientes de 5 a 200 usuarios.
- `scripts/run-load.sh` y `run-stress.sh`: ejecución CLI, reporte HTML y análisis.
- `scripts/monitor-resources.sh`: métricas CSV de Docker y del host Linux.
- `scripts/analyze-jtl.py`: p95, p99, throughput y errores por sampler/escalón.

## Requisitos

- API y PostgreSQL activos (por defecto, `docker compose up -d db backend`).
- Java 17+ y Apache JMeter 5.6.x disponibles como `jmeter`, o definir `JMETER_BIN`.
- Docker CLI para capturar métricas de contenedores.
- Python 3 para resumir el JTL.

Si no se desea instalar JMeter, puede ejecutarse en Docker (la primera vez descarga la imagen):

```bash
JMETER_BIN=./scripts/jmeter-docker.sh USERS=5 RAMP_UP=5 DURATION=30 BASELINE_SECONDS=15 RECOVERY_SECONDS=15 ./scripts/run-load.sh
```

El adaptador usa la red del host, por lo que esta alternativa está orientada a Docker sobre Linux.

Evitar ejecutar JMeter 5.6.3 con Java 26: el motor Groovy incluido no es compatible. El adaptador Docker mantiene una JVM compatible dentro del contenedor; si se usa JMeter local, se recomienda Java 17 o 21. Los planes actuales ya no usan Groovy, pero una JVM soportada evita problemas con otros componentes o plugins.

## Datos de prueba

Copiar el ejemplo y modificarlo localmente:

```bash
cp data/users.example.csv data/users.csv
chmod 600 data/users.csv
```

`users.csv` está ignorado por Git. Sus columnas son `correo,contrasena,tenant_slug`. Las filas deben existir en la base; con `SEED_DATA=true`, el ejemplo usa el cliente demo sembrado por el backend. Una sola fila puede reciclarse para una prueba técnica, aunque para una simulación realista conviene preparar 50 clientes distintos.

También se puede omitir el CSV y pasar un único usuario mediante `-Jemail`, `-Jpassword` y `-Jtenant_slug`.

## Prueba corta (recomendada primero)

Desde esta carpeta:

```bash
JMETER_BIN=jmeter USERS=5 RAMP_UP=5 DURATION=30 BASELINE_SECONDS=15 RECOVERY_SECONDS=15 ./scripts/run-load.sh
```

## NF-01 completa

```bash
USERS=50 RAMP_UP=60 DURATION=300 BASELINE_SECONDS=180 RECOVERY_SECONDS=180 ./scripts/run-load.sh
```

Propiedades disponibles: `BASE_URL` (predeterminado `http://localhost:5000`), `USERS_FILE`, `TENANT_ID`, `CONNECT_TIMEOUT`, `RESPONSE_TIMEOUT`, `THINK_MIN_MS`, `THINK_RANGE_MS` y `MONITOR_INTERVAL`.

## NF-02 escalonada

La fase estable por escalón se controla con `STEP_DURATION`; `STEP_RAMP_UP` se suma a esa duración y `STEP_GAP` (5 segundos por defecto) separa los grupos para que las últimas solicitudes no se solapen. El plan usa los niveles `5,10,25,50,75,100,150,200` y la concurrencia no se acumula.

```bash
STEP_DURATION=120 STEP_RAMP_UP=10 BASELINE_SECONDS=180 RECOVERY_SECONDS=180 ./scripts/run-stress.sh
```

Para una validación corta de la mecánica:

```bash
STEP_DURATION=15 STEP_RAMP_UP=2 BASELINE_SECONDS=10 RECOVERY_SECONDS=10 ./scripts/run-stress.sh
```

## Resultados

Cada script crea un directorio con fecha bajo `results/load` o `results/stress` y su dashboard bajo `reports/...`. Los archivos principales son:

- `results.jtl`: muestras HTTP y aserciones.
- `summary.csv`: throughput, promedio, p95, p99 y errores por etiqueta.
- `resource-summary.csv`: promedios y deltas de CPU/RAM contra el baseline.
- `docker-*.csv`: CPU, memoria, red y bloque por contenedor y fase.
- `host-*.csv`: CPU, memoria y swap del host y fase.
- `jmeter.log`: diagnóstico del generador.
- `index.html` en el directorio de reporte: dashboard navegable.

Durante una ejecución, otra terminal puede mostrar métricas en vivo:

```bash
docker stats
```

Para abrir el reporte, usar el navegador con la ruta `reports/.../index.html`. Las ejecuciones formales deben hacerse siempre en CLI; la GUI de JMeter se reserva para depuración.

## Interpretación y limitaciones

El analizador marca NF-01 como fallo si el error total es `>= 1%`, si el p95 de login/carrito supera 1000 ms o si algún p99 supera 2000 ms. NF-02 clasifica cada escalón como `aceptable`, `degradacion` o `saturacion` usando p95, errores y timeouts.

El endpoint real `GET /api/v1/productos` actualmente devuelve la lista completa y no declara parámetros de paginación o filtros. Por eso este plan no agrega parámetros que el controlador ignoraría. Cuando el controlador los implemente, debe ampliarse `products.csv` y el sampler.

Si JMeter comparte el host con Docker, las cifras representan la capacidad conjunta del generador y el sistema bajo prueba. Para resultados comparables, ejecutar JMeter desde otra computadora apuntando `BASE_URL` al host del backend.
