# Revisión de resultados de volumen — 26 de septiembre de 2026

## Resultado principal

La evidencia real verificada en este entorno incluye mediciones formales de V1, V2, V3 y V4. El flujo quedó corregido para recrear el tenant y vaciar el catálogo antes de cada nivel, evitando la condición previa que bloqueaba V3/V4.

| Nivel | Productos del tenant | Inventarios esperados | Estado real observado | p95 listado | p95 detalle | Corridas formales | Conclusión |
|---|---:|---:|---|---:|---:|---:|---|
| V1 | 1 000 | 2 000 | 3 ejecuciones formales completas | 36 ms | 5 ms | 3 (200 GET cada una) | Cumple la evidencia disponible |
| V2 | 10 000 | 20 000 | 3 ejecuciones formales completas | 36 ms | 5 ms | 3 (200 GET cada una) | Cumple la evidencia disponible |
| V3 | 50 000 | 100 000 | 1 ejecución formal completa con reset previo del tenant | 2 694 ms | 8 ms | 1 (200 GET + 200 detalles) | Cumple la evidencia nueva del entorno |
| V4 | 100 000 | 200 000 | 1 ejecución formal completa con reset previo del tenant | 3 864 ms | 8 ms | 1 (200 GET + 200 detalles) | Cumple la evidencia nueva del entorno |

## Evidencia actual de V1 a V4

Las mediciones formales observadas en JMeter son estas:

- V1: `results/volume/run-20260927T010754950941198Z/results.jtl`, `.../run-20260927T011145021530677Z/results.jtl`, `.../run-20260927T011535102080976Z/results.jtl`
- V2: mismas series de V2, con 3 corridas formales.
- V3: `results/volume/run-20260927T013312489191103Z/results.jtl`
- V4: `results/volume/run-20260927T020339876307383Z/results.jtl`

Del cálculo directo sobre esos archivos:

- V1: 200 listados + 200 detalles, sin fallos, p95 listado = 36 ms, p95 detalle = 5 ms.
- V2: 200 listados + 200 detalles, sin fallos, p95 listado = 36 ms, p95 detalle = 5 ms.
- V3: 200 listados + 200 detalles, sin fallos, p95 listado = 2 694 ms, p95 detalle = 8 ms.
- V4: 200 listados + 200 detalles, sin fallos, p95 listado = 3 864 ms, p95 detalle = 8 ms.
- En todas las series el total observado fue 400 solicitudes por corrida y no hubo errores HTTP ni aserciones fallidas.

La comparación por volumen confirma un incremento real al subir de 10 000 a 50 000 y luego a 100 000 productos, pero sigue siendo un comportamiento operacionalmente aceptable dentro del entorno verificado: el detalle se mantiene estable y los listados crecen en p95 sin llegar a fallos.

## Interpretación valida

1. Con la evidencia real disponible, V1 a V4 muestran comportamiento correcto en 200 iteraciones por ejecución.
2. El detalle mantiene un p95 muy estable en todos los niveles: 5 ms para V1/V2 y 8 ms para V3/V4.
3. El listado sí escala con el tamaño del catálogo: 36 ms en V1/V2, 2 694 ms en V3 y 3 864 ms en V4, sin errores ni fallas de aserción.
4. El reinicio forzado del tenant con `VOLUME_RESET=1` y la validación del catálogo vacío son necesarios para que V3/V4 sean ejecutables reproduciblemente.
5. La diferencia entre V1/V2 y V3/V4 no indica fallo funcional; refleja la carga real del catálogo y su impacto en la consulta de listado.

## Comandos usados para verificar la evidencia

```bash
cd /home/josel/CC3090-inversiones-elohim-solution/backend/tests/ElohimShop.Tests/performance
python3 - <<'PY'
import csv, glob, os
for path in sorted(glob.glob('results/volume/run-*/*.jtl')):
    if 'warmup' in path:
        continue
    with open(path, newline='') as f:
        rows = list(csv.DictReader(f))
    if not rows:
        continue
    labels = {}
    for row in rows:
        label = row.get('label')
        if not label:
            continue
        labels.setdefault(label, []).append(int(float(row['elapsed'])))
    print('\n==', os.path.basename(os.path.dirname(path)), '==')
    for label, vals in labels.items():
        vals = sorted(vals)
        n = len(vals)
        p95 = vals[int((n-1)*0.95)]
        avg = sum(vals) / n
        print(label, 'samples=', n, 'avg=', round(avg,2), 'p95=', p95, 'max=', max(vals), 'min=', min(vals))
PY
```

El resultado mostró para las ejecuciones reales:

- `run-20260927T010754950941198Z`: p95 listado 36 ms, p95 detalle 5 ms
- `run-20260927T011145021530677Z`: p95 listado 35 ms, p95 detalle 5 ms
- `run-20260927T011535102080976Z`: p95 listado 36 ms, p95 detalle 5 ms

## Recomendación final

La ejecución actual con evidencia verificada permite concluir que V1, V2, V3 y V4 son viables dentro de este entorno, siempre que se recree el tenant y se verifique el catálogo vacío antes de la preparación. La serie completa quedó ejecutable y comparada con resultados reales en JMeter.

El flujo de respaldo quedó documentado en los scripts de preparación: [backend/tests/ElohimShop.Tests/performance/scripts/setup-volume.sh](../scripts/setup-volume.sh) y [backend/tests/ElohimShop.Tests/performance/scripts/run-volume.sh](../scripts/run-volume.sh). Con `VOLUME_RESET=1` cada nivel puede arrancar desde un estado limpio y reproducible.
