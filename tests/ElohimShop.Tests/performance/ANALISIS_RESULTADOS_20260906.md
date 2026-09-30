# Análisis de pruebas JMeter del 6 de septiembre de 2026

## Conclusión

Las dos ejecuciones confirman que la API respondió, pero no deben registrarse como pruebas formalmente aprobadas. JMeter 5.6.3 se ejecutó sobre Java 26 y su motor Groovy falló con `Unsupported class file major version 70`.

- En NF-01, las 6,715 solicitudes de carrito respondieron HTTP 200, pero la aserción Groovy las marcó como fallidas.
- En NF-02, el cálculo Groovy de los retrasos falló y los ocho grupos arrancaron casi simultáneamente. La prueba aplicó aproximadamente 615 usuarios concurrentes durante dos minutos, no ocho escalones consecutivos.
- Los planes se corrigieron para usar aserciones nativas de JMeter y retrasos enviados como propiedades numéricas, sin Groovy.
- El monitor del host también se corrigió para forzar locale `C`; los CSV anteriores usaron coma decimal y ya son admitidos por el analizador.

Después de la corrección se ejecutaron pruebas de humo. NF-01 completó 8 muestras con 0 errores. NF-02 completó 14,792 muestras con 0 errores y arrancó los grupos en orden; después se añadió una separación configurable de 5 segundos para evitar el pequeño solapamiento de solicitudes que todavía estaban terminando justo en el límite entre grupos.

## NF-01: login y carrito

Configuración observada: 50 usuarios, ramp-up de 60 segundos y duración aproximada de 5 minutos.

| Operación | Muestras | HTTP 200 | Promedio | p95 | p99 | Throughput |
|---|---:|---:|---:|---:|---:|---:|
| Login | 50 | 50 | 46.80 ms | 59 ms | 94 ms | 0.86 req/s |
| Carrito | 6,715 | 6,715 | 3.76 ms | 5 ms | 6 ms | 22.57 req/s |

No hubo respuestas HTTP distintas de 200 ni timeouts. El 100 % de errores mostrado para carrito proviene exclusivamente de la aserción Groovy incompatible. Los tiempos HTTP cumplen ampliamente los umbrales propuestos, pero la corrida debe repetirse con el plan corregido antes de declararla aprobada.

### Recursos NF-01

| Recurso | Baseline | Carga | Delta | Recuperación |
|---|---:|---:|---:|---:|
| CPU backend | 0.01 % | 10.10 % | +10.09 puntos | 0.01 % |
| RAM backend | 133.77 MiB | 136.41 MiB | +2.64 MiB | 129.76 MiB |
| CPU PostgreSQL | 0.76 % | 3.68 % | +2.92 puntos | 0.59 % |
| RAM PostgreSQL | 22.67 MiB | 28.40 MiB | +5.73 MiB | 21.79 MiB |
| CPU host | 5.90 % | 10.96 % | +5.06 puntos | 8.53 % |
| RAM host | 8,098.57 MiB | 8,767.84 MiB | +669.27 MiB | 7,927.40 MiB |

El backend y PostgreSQL recuperaron CPU y memoria. El host no usó swap de forma significativa. NF-01 no muestra indicios de saturación del servidor.

## NF-02: productos

Esta ejecución no fue escalonada. Todos los grupos comenzaron entre las 22:38:30 y las 22:38:38 UTC y terminaron alrededor de las 22:40:42 UTC. Por tanto, los resultados por etiqueta reflejan grupos que competían simultáneamente por el mismo sistema.

| Grupo etiquetado | Muestras | Promedio | p95 | p99 | Throughput | HTTP no-200 |
|---|---:|---:|---:|---:|---:|---:|
| 5 usuarios | 932 | 604.34 ms | 828 ms | 908 ms | 7.73 req/s | 0 |
| 10 usuarios | 2,027 | 572.69 ms | 832 ms | 918 ms | 16.79 req/s | 0 |
| 25 usuarios | 4,755 | 608.21 ms | 832 ms | 912 ms | 39.36 req/s | 0 |
| 50 usuarios | 9,239 | 625.78 ms | 835 ms | 920 ms | 76.30 req/s | 0 |
| 75 usuarios | 13,446 | 646.17 ms | 836 ms | 925 ms | 110.49 req/s | 0 |
| 100 usuarios | 17,437 | 665.77 ms | 840 ms | 917 ms | 142.97 req/s | 0 |
| 150 usuarios | 26,339 | 665.56 ms | 837 ms | 924 ms | 213.20 req/s | 0 |
| 200 usuarios | 38,004 | 614.99 ms | 832 ms | 919 ms | 308.13 req/s | 0 |

En conjunto hubo 112,179 respuestas HTTP 200, cero timeouts, p95 total de aproximadamente 821 ms, p99 de 930 ms, máximo de 1,246 ms y throughput agregado de 855.43 req/s. Los 932 errores mostrados para el grupo de 5 usuarios son fallos de su aserción Groovy, no errores HTTP.

Estos datos muestran que la API siguió respondiendo bajo una ráfaga combinada alta, pero no permiten declarar que 200 usuarios sea el último escalón aceptable ni localizar degradación o saturación.

### Recursos durante la ventana de carga real de NF-02

El `resource-summary.csv` original promedia los 16 minutos que el monitor esperaba que duraran los escalones, aunque la carga defectuosa terminó en unos 131 segundos. Por eso subestima la CPU. La tabla siguiente se recalculó usando solamente la ventana delimitada por el primer y el último timestamp del JTL.

| Recurso | Promedio | Máximo |
|---|---:|---:|
| CPU backend | 233.71 % | 256.26 % |
| RAM backend | 302.11 MiB | 315.50 MiB |
| CPU PostgreSQL | 246.20 % | 276.42 % |
| RAM PostgreSQL | 227.39 MiB | 228.90 MiB |
| CPU host | 97.28 % | 98.25 % |
| RAM host | 9,440.36 MiB (60.23 %) | 9,568.54 MiB (61.05 %) |

La CPU del host permaneció por encima del límite de parada recomendado de 90 %. El resultado quedó condicionado por la capacidad de la computadora y posiblemente por el propio JMeter. PostgreSQL recuperó su memoria después de la carga. El backend quedó cerca de 292.51 MiB durante la recuperación, unos 150 MiB sobre el baseline; una sola observación corta no demuestra una fuga, pero debe revisarse en la repetición correcta.

## Repetición recomendada

1. Usar el adaptador Docker de JMeter o ejecutar JMeter local con Java 17 o 21.
2. Ejecutar primero NF-01 con 5 usuarios durante 30 segundos y confirmar cero errores funcionales.
3. Ejecutar NF-02 corta con 15 segundos por escalón y comprobar en el JTL que los intervalos no se superponen.
4. Ejecutar la prueba formal tres veces solo después de esas verificaciones.
5. Detener NF-02 si la CPU del host permanece sobre 90 %; para medir el servidor con precisión, mover JMeter a otra computadora.

## Archivos que deben versionarse

Versionar los planes `.jmx`, scripts, README, este análisis, `users.example.csv`, `products.csv`, `.gitignore` y los `.gitkeep` de `results` y `reports`.

No versionar `users.csv`, archivos `.jtl`, dashboards HTML completos, métricas CSV de ejecuciones, logs de JMeter ni `__pycache__`. Los resultados formales importantes deben publicarse como artefactos de CI o conservarse fuera de Git; este documento resume la evidencia relevante en un archivo pequeño y auditable.
