# Plan de pruebas de integración y regresión

## 1. Propósito

Definir **tres pruebas de integración y tres de regresión**, sencillas y repetibles, para RNF5 (respaldos), RNF9 (inventario) y RNF12 (sincronización de información entre sesiones).

Se sigue la estructura del [plan de JMeter](../tests_Jmeter/PLAN_PRUEBAS_VOLUMEN_JMETER.md): propósito, alcance, herramientas, preparación, escenarios, criterios y evidencias. Este documento define el plan y enlaza las evidencias disponibles. INT-01 e INT-02 tienen una ejecución local aprobada; los demás casos permanecen pendientes.

## 2. Objetivos y selección

- **Integración:** verificar que componentes conectados produzcan el resultado esperado, por ejemplo, venta → inventario → catálogo.
- **Regresión:** repetir un comportamiento previamente aprobado después de un cambio y detectar si dejó de funcionar. Requiere una primera ejecución correcta como referencia.
- Una prueba de integración también puede reutilizarse como regresión.

| Tipo | Caso | RNF |
|---|---|---|
| Integración | INT-01 — Respaldo automático y restauración | RNF5 |
| Integración | INT-02 — Venta e inventario del catálogo | RNF9 |
| Integración | INT-03 — Actualización visible en otra sesión | RNF12 |
| Regresión | REG-01 — Retención de respaldos | RNF5 |
| Regresión | REG-02 — Ventas concurrentes | RNF9 |
| Regresión | REG-03 — Actualizaciones sucesivas entre sesiones | RNF12 |

RNF12 se verifica mediante lectura entre sesiones y actualizaciones sucesivas. RNF11 se mantiene en el plan Playwright existente; disponer de ese plan no equivale a tener resultados aprobados.

## 3. Alcance

**Incluido:** PostgreSQL de pruebas, proceso de respaldo y limpieza, flujo que confirma una venta y descuenta inventario, catálogo y edición de productos desde sesiones independientes del mismo tenant.

**Fuera del alcance:** pagos reales, pruebas de carga, diseño responsivo y accesibilidad. RNF12 cubre sincronización al recargar; actualización automática en tiempo real y conflictos de escrituras simultáneas quedan fuera de este conjunto mínimo.

## 4. Herramientas sugeridas

| Herramienta | Para qué se utiliza |
|---|---|
| `pg_dump`, `pg_restore` y consultas SQL | Generar/restaurar un respaldo PostgreSQL y comparar datos conocidos. Usar formato compatible con `pg_restore`. |
| Script de respaldo/limpieza y programador de tareas | Ejecutar el proceso automático real y verificar su política de retención. Pendientes de implementar si todavía no existen. |
| Python 3 + HTTP estándar | INT-02: ejecutar venta por API y contrastar inventario/catálogo con PostgreSQL, sin dependencias adicionales. |
| Playwright | Enviar solicitudes a la API, iniciar dos compras concurrentes y abrir dos contextos independientes en Chromium para comprobar sincronización entre sesiones. Guardar reporte, capturas y trazas de fallos. |

Se reutiliza la orientación del [plan Playwright](../tests_playwright/PLAN_PRUEBAS_PLAYWRIGHT.md). Los casos API de este documento se implementarán aparte de sus casos E2E de interfaz. JMeter no es necesario para estos seis casos: no se busca generar carga.

## 5. Preparación y protocolo común

1. Usar BD, almacenamiento y tenant de pruebas aislados. Registrar commit, fecha, versiones y URL del entorno.
2. Preparar dos usuarios autorizados del mismo tenant, una sucursal y un producto publicado con precio e inventario conocidos. Restablecer los datos antes de cada caso.
3. Identificar el flujo real que descuenta existencias y el contrato de API vigente. No escribir directamente en la BD para simular la venta bajo prueba.
4. Proponer **un respaldo diario**, retención mínima de **30 días completos** y desfase de catálogo **≤ 2 s**. Acordar estos criterios antes de ejecutar; la frecuencia diaria y los 2 s son propuestas.
5. Para RNF9, medir desde la respuesta de confirmación persistida hasta la primera lectura correcta del catálogo; consultar cada 100 ms durante un máximo de 2 s. Registrar el desfase observado, sujeto a esa resolución de muestreo.
6. Para RNF12, proponer un límite de **2 s desde el inicio de la recarga hasta mostrar el último dato confirmado**, pendiente de acuerdo. Usar dos contextos con cookies y almacenamiento independientes. Recargar inmediatamente después del guardado, sin limpiar ni desactivar cachés. La emulación de sesiones no acredita dispositivos físicos.
7. Ejecutar integración una vez por escenario. Para regresión, guardar una ejecución aprobada y repetir después del cambio con los mismos datos y criterios. Un fallo inicial es un defecto o funcionalidad pendiente, no una regresión demostrada.

## 6. INT-01 — Respaldo automático y restauración

Implementación local e instrucciones: [INT-01 con PostgreSQL de Compose](../../tests/backups/README.md). Usa los datos existentes del volumen configurado; no requiere llenarlo previamente.

### Qué se quiere hacer

Comprobar la integración programador → respaldo → almacenamiento → restauración.

### Flujo

1. Preparar una BD sin otras escrituras, con un producto, inventario y una reservación con detalle. Guardar IDs, valores y conteos esperados.
2. Configurar temporalmente una ejecución próxima en el programador de pruebas y esperar el respaldo. Conservar también la configuración de frecuencia diaria prevista.
3. Restaurar el archivo en otra BD vacía con `pg_restore` y consultar las entidades preparadas.

### Criterios de aceptación

- El programador ejecuta el respaldo sin intervención manual y el proceso termina sin errores.
- La restauración termina correctamente; coinciden IDs, conteos, cantidades y relaciones del conjunto preparado.

**Evidencia:** configuración y log del programador, fecha/identificador del archivo, salida de restauración y comparación SQL. Este caso prueba recuperación; no acredita por sí solo 30 días de retención.

## 7. INT-02 — Venta e inventario del catálogo

Implementación e instrucciones: [INT-02 por API y PostgreSQL](../../tests/integration/README.md). Se consulta `/api/v1/productos/{id}`, la ruta actual del detalle del catálogo atendida por `PlatformService`.

### Qué se quiere hacer

Comprobar que una venta confirmada actualice la BD y la lectura del catálogo.

### Flujo

1. Preparar un producto con 10 unidades en una sola sucursal, sin otras operaciones concurrentes.
2. Confirmar una compra de 3 unidades mediante el flujo de la aplicación, sin cobro real.
3. Consultar inventario y catálogo hasta observar el resultado o alcanzar el límite.

### Criterios de aceptación

- Quedan exactamente 7 unidades; la operación se registra una sola vez.
- El catálogo refleja la cantidad o disponibilidad que exponga su contrato en ≤ 2 s. Si solo muestra disponibilidad, verificar las 7 unidades mediante inventario.

**Evidencia:** ID de operación, valores inicial/final, respuestas y desfase medido.

## 8. INT-03 — Actualización visible en otra sesión

### Qué se quiere hacer

Comprobar la integración sesión A → API → persistencia → sesión B, verificando RNF12 con un cambio de nombre de producto.

### Flujo

1. Abrir dos sesiones independientes del mismo tenant. En A abrir la edición del producto; en B abrir su detalle en el catálogo y verificar el nombre «Producto inicial».
2. En A cambiar el nombre a «Producto actualizado» y esperar la confirmación de guardado.
3. Recargar inmediatamente B y comprobar el nombre mostrado. Consultar también el valor persistido de forma independiente.

### Criterios de aceptación

- B muestra «Producto actualizado» en ≤ 2 s desde el inicio de la recarga, sin cerrar sesión ni limpiar caché.
- El ID se conserva y el valor coincide con el persistido; no se crea un producto duplicado.

**Evidencia:** ID del producto, capturas antes/después, respuesta de guardado y tiempo hasta mostrar el dato correcto.

## 9. REG-01 — Retención de respaldos

Implementación inicial junto con REG-02 y REG-03: [regression.py](../../tests/regression/regression.py). Los casos REG-02 y REG-03 quedan pendientes de configurar con credenciales y datos de prueba.

### Qué se quiere hacer

Comprobar que los cambios no hagan que la limpieza elimine respaldos antes de tiempo.

### Flujo

1. En un directorio exclusivo de pruebas, preparar archivos con edades de 1, 29, exactamente 30 y 31 días respecto de una hora fija UTC.
2. Usar el dato temporal que realmente lee el limpiador: metadatos, nombre o fecha de modificación. Controlar el reloj de referencia sin cambiar el reloj del equipo.
3. Ejecutar el mismo limpiador usado por el respaldo automático y comparar el inventario de archivos.

### Criterios de aceptación

- Se conservan todos los archivos con edad ≤ 30 días y se elimina el de 31 días, según la política propuesta de borrar únicamente edades > 30 días.
- El resultado coincide con la referencia aprobada. Conservar más de 30 días cumple el RNF, aunque una limpieza que no borra incumpliría la política propuesta.

**Evidencia:** reloj de referencia, configuración y archivos antes/después. La simulación verifica la política; para acreditar conservación real se necesita además historial de respaldos durante al menos 30 días. Si falta, registrar esa evidencia operativa como pendiente.

## 10. REG-02 — Ventas concurrentes

### Qué se quiere hacer

Detectar sobreventa con el escenario mínimo: dos compradores y una sola unidad disponible.

### Flujo

1. Preparar un producto con 1 unidad y dos compradores con operaciones distintas, listos para confirmar.
2. Lanzar las dos confirmaciones en paralelo mediante Playwright, sin esperar la primera para iniciar la segunda. Usar el punto que realmente descuenta/reserva stock y comprobar en las trazas que las solicitudes se solapan.
3. Consultar ambas operaciones, inventario y catálogo. Repetir tres veces, restableciendo los datos.

### Criterios de aceptación

- En cada repetición se confirma exactamente una compra; la otra se rechaza por falta de existencias mediante el resultado previsto por el contrato.
- Inventario final 0, nunca negativo, y catálogo sin disponibilidad en ≤ 2 s desde la confirmación exitosa.

**Evidencia:** resultados de ambas solicitudes, registros persistidos, stock y desfase por repetición. Es una comprobación básica de concurrencia, no una prueba de capacidad.

## 11. REG-03 — Actualizaciones sucesivas entre sesiones

### Qué se quiere hacer

Detectar si un cambio del sistema hace que una sesión conserve información antigua. Se utilizan cambios secuenciales para mantener sencilla la prueba.

### Flujo

1. Abrir el mismo producto en dos sesiones autorizadas A y B, con nombre «Producto inicial».
2. En A guardar «Producto versión 1». Recargar B y comprobar que muestra ese nombre antes de editar.
3. En B guardar «Producto versión 2». Recargar A y comprobar el nuevo valor.
4. Recargar ambas sesiones y verificar que mantienen «Producto versión 2», igual al dato persistido. Ejecutar antes y después del cambio de código con los mismos datos iniciales.

### Criterios de aceptación

- Cada sesión lectora refleja el último guardado confirmado en ≤ 2 s desde el inicio de su recarga.
- Ambas terminan con el mismo ID y nombre «Producto versión 2», sin recuperar valores anteriores ni generar duplicados.
- El comportamiento coincide con la referencia aprobada. No se exige sincronización sin recargar.

**Evidencia:** commit de referencia y actual, secuencia de guardados, valores en A/B, dato persistido y tiempos medidos.

## 12. Plan de acción y registro de resultados

| Paso | Acción | Entregable |
|---|---|---|
| 1 | Acordar criterios y preparar entorno/datos | Configuración y datos iniciales |
| 2 | Implementar respaldo programado y limpiador si faltan | Scripts y programación verificables |
| 3 | Preparar los casos API/web y ejecutar INT-01 a INT-03 | Evidencias de integración |
| 4 | Ejecutar REG-01 a REG-03; corregir fallos iniciales | Referencia aprobada |
| 5 | Repetir regresión después de cambios y antes de entregar | Comparación antes/después |

| Caso | Commit / fecha | Resultado obtenido | Evidencia | Estado |
|---|---|---|---|---|
| INT-01 | a74c290 + script local / 2026-10-10 | 29 tablas, 487 223 filas; restauración y hashes correctos | [Resultado local](../../tests/backups/INT01_RESULTADOS_20261010.md) | Aprobada (alcance local) |
| INT-02 | ef49036 + script local / 2026-10-10 | Stock 10 → 7; catálogo en 6,086 ms; una venta, sin descuento duplicado | [Resultado local](../../tests/integration/INT02_RESULTADOS_20261010.md) | Aprobada (alcance local) |
| INT-03 | | | | Pendiente |
| REG-01 | | | | Pendiente |
| REG-02 | | | | Pendiente |
| REG-03 | | | | Pendiente |

Estados: **Pendiente**, **Bloqueada** (indicar dependencia), **Aprobada** o **Fallida**. Conservar scripts, reporte y evidencias sin credenciales. Declarar RNF12 cubierto para los cambios y sesiones probados al recargar, sin afirmar sincronización automática ni resolución de conflictos concurrentes; RNF5 debe distinguir política validada de historial real disponible.
