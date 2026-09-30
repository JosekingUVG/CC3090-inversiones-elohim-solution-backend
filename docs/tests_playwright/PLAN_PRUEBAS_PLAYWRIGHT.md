# Arquitectura propuesta de pruebas E2E con Playwright para DM Hub

## Objetivo

Implementar pruebas funcionales **E2E de caja negra** sobre DM Hub utilizando Playwright.

Las pruebas se ejecutarán contra la aplicación desplegada en un servidor Nginx y deberán interactuar con el sistema principalmente como lo haría un usuario real:

* Navegación mediante navegador.
* Clics.
* Formularios.
* Tablas.
* Filtros.
* Modales.
* Mensajes de éxito/error.
* Cambios visibles de estado.
* Persistencia después de recargar.
* Control de acceso según rol.

No se probarán directamente endpoints REST/API, ya que estos serán evaluados independientemente utilizando Swagger u otra herramienta especializada.

---

# 1. Estructura propuesta

```text
tests/
│
├── auth.setup.ts
│
├── dashboard.spec.ts
├── sucursales.spec.ts
├── reservaciones.spec.ts
├── pagos.spec.ts
├── kanban.spec.ts
├── clientes.spec.ts
├── productos.spec.ts
├── reportes.spec.ts
├── usuarios.spec.ts
├── constructor.spec.ts
└── configuracion.spec.ts
```

Opcionalmente:

```text
tests/
├── helpers/
│   ├── login.ts
│   ├── navigation.ts
│   └── test-data.ts
│
└── fixtures/
    └── auth.fixture.ts
```

La idea es mantener **un `.spec.ts` por cada apartado principal del sidebar**.

---

# 2. Configuración general de Playwright

## `playwright.config.ts`

Configurar como mínimo:

* `baseURL` apuntando al servidor Nginx.
* Ejecución en Chromium.
* Screenshots cuando falle una prueba.
* Video cuando falle una prueba.
* Trace cuando falle o al primer reintento.
* Timeout apropiado para el servidor.
* Reintentos en CI si fuera necesario.
* Reporter HTML.

Ejemplo conceptual:

```ts
reporter: [
  ['html', { open: 'always' }]
]
```

Con esto, al terminar:

```bash
npx playwright test
```

Playwright genera y abre automáticamente el reporte HTML.

> La configuración del reporter se realiza en `playwright.config.ts`; no es necesario crear un `.json` exclusivamente para esto.

---

# 3. Autenticación

## `auth.setup.ts`

Evitar realizar login manualmente en cada test.

### Tests iniciales

* [ ] Login con credenciales válidas.
* [ ] Login con contraseña incorrecta.
* [ ] Login con usuario inexistente.
* [ ] Campos obligatorios vacíos.
* [ ] Contraseña oculta visualmente.
* [ ] Redirección al Dashboard después del login.
* [ ] Usuario autenticado puede recargar la página sin perder sesión.
* [ ] Cerrar sesión elimina el acceso al panel.
* [ ] Usuario sin sesión no puede entrar directamente a rutas administrativas.

Después del login correcto, guardar:

```text
storageState
```

para reutilizar la sesión en los demás `.spec.ts`.

---

# 4. Dashboard

## `dashboard.spec.ts`

### Navegación

* [ ] Dashboard carga correctamente.
* [ ] El sidebar se encuentra visible.
* [ ] Se muestran los 11 apartados administrativos.
* [ ] Cada opción del sidebar permite navegar correctamente.

### Métricas

Verificar que aparecen las tarjetas correspondientes a:

* [ ] Ventas del día.
* [ ] Ingresos totales.
* [ ] Productos activos.
* [ ] Stock crítico.

### Gráficas

* [ ] Se renderiza "Ventas por hora".
* [ ] Se renderiza "Productos más vendidos".
* [ ] Las gráficas contienen información visible.
* [ ] La página no presenta errores visuales al cargar las gráficas.

### Stock crítico

* [ ] Se muestra el listado de productos con stock crítico.
* [ ] Se muestra cantidad disponible.
* [ ] Se muestra el número de productos en estado crítico.

### Últimas ventas

* [ ] Se muestra el listado.
* [ ] Cada venta muestra identificador.
* [ ] Se muestra cliente.
* [ ] Se muestra monto.
* [ ] Se muestra estado.

### Integración visual

* [ ] Una operación realizada en el sistema termina reflejándose en las métricas correspondientes del Dashboard.

---

# 5. Sucursales

## `sucursales.spec.ts`

### Listado

* [ ] La página carga correctamente.
* [ ] Se muestran las sucursales existentes.
* [ ] Se muestra información básica de cada sucursal.

### Crear sucursal

* [ ] Crear sucursal con datos válidos.
* [ ] Nombre obligatorio.
* [ ] Dirección obligatoria.
* [ ] Validar formato del teléfono si existe validación.
* [ ] Cancelar creación no guarda información.
* [ ] La nueva sucursal aparece en el listado.

### Editar

* [ ] Editar nombre.
* [ ] Editar dirección.
* [ ] Editar teléfono.
* [ ] Los cambios permanecen después de recargar.

### Eliminar

* [ ] Eliminar sucursal.
* [ ] Cancelar confirmación de eliminación.
* [ ] Confirmar eliminación.
* [ ] La sucursal deja de aparecer.

### Integración

* [ ] Una sucursal nueva aparece posteriormente al distribuir stock de productos.

---

# 6. Reservaciones

## `reservaciones.spec.ts`

### Listado

* [ ] Página carga correctamente.
* [ ] Se muestran reservaciones existentes.
* [ ] Se muestra ID de reservación.
* [ ] Se muestra cliente.
* [ ] Se muestra estado.
* [ ] Se muestra información relacionada con la compra.

### Búsqueda y filtros

* [ ] Buscar reservación existente.
* [ ] Buscar reservación inexistente.
* [ ] Filtrar por estado, si está disponible.
* [ ] Filtrar por sucursal, si está disponible.
* [ ] Limpiar filtros restaura resultados.

### Detalle

* [ ] Abrir una reservación.
* [ ] Visualizar productos asociados.
* [ ] Visualizar cliente.
* [ ] Visualizar estado actual.
* [ ] Visualizar total.

### Estados

* [ ] Verificar visualmente los diferentes estados disponibles.
* [ ] Cambio de estado se refleja en el listado cuando corresponda.

---

# 7. Pagos

## `pagos.spec.ts`

### Listado

* [ ] Página carga correctamente.
* [ ] Se muestran pagos registrados.
* [ ] Se muestra monto.
* [ ] Se muestra estado.
* [ ] Se muestra referencia del pedido/transacción.
* [ ] Se muestra método de pago.

### Búsqueda

* [ ] Buscar transacción existente.
* [ ] Buscar transacción inexistente.

### Estados

* [ ] Pago aprobado se muestra correctamente.
* [ ] Pago pendiente se muestra correctamente.
* [ ] Pago rechazado se muestra correctamente, si aplica.

### Detalle

* [ ] Abrir información de una transacción.
* [ ] El monto coincide con el mostrado en el listado.
* [ ] La información del pedido asociado es visible.

### Integración funcional

* [ ] Pago completado produce el cambio visual esperado en el pedido.
* [ ] Pago completado termina reflejándose en Kanban.
* [ ] Pago completado termina reflejándose en Reportes/Dashboard.

---

# 8. Tablero Kanban

## `kanban.spec.ts`

### Renderizado

* [ ] Página carga correctamente.
* [ ] Existe columna "Pendiente de pago".
* [ ] Existe columna "Pago verificado".
* [ ] Existe columna "Despachado".

### Tarjetas

* [ ] Los pedidos aparecen como tarjetas.
* [ ] Cada tarjeta muestra ID.
* [ ] Cada tarjeta muestra información relevante del pedido.

### Cambio de estado

* [ ] Mover pedido de "Pendiente de pago" a "Pago verificado".
* [ ] Mover pedido de "Pago verificado" a "Despachado".
* [ ] El pedido permanece en el nuevo estado después de recargar.

Si utiliza drag & drop:

* [ ] Drag & drop funciona correctamente.
* [ ] Soltar en una columna válida actualiza el pedido.

### Búsqueda

* [ ] Buscar pedido mediante ID.
* [ ] ID existente muestra el pedido.
* [ ] ID inexistente muestra estado vacío.

### Filtros

* [ ] Filtrar pedidos por sucursal.
* [ ] Cambiar de sucursal actualiza las tarjetas.
* [ ] Limpiar filtro vuelve a mostrar todos los pedidos correspondientes.

### Contadores

* [ ] Cada columna muestra su contador.
* [ ] Cambio de estado actualiza los contadores.
* [ ] Los montos mostrados cambian al mover pedidos cuando corresponda.

---

# 9. Clientes

## `clientes.spec.ts`

### Listado

* [ ] Página carga correctamente.
* [ ] Se muestran clientes registrados.
* [ ] Se muestra nombre.
* [ ] Se muestra correo.
* [ ] Se muestra información disponible del cliente.

### Búsqueda

* [ ] Buscar cliente por nombre.
* [ ] Buscar cliente por correo.
* [ ] Buscar cliente inexistente.

### Detalle

* [ ] Abrir cliente.
* [ ] Mostrar información general.
* [ ] Mostrar historial de compras si está disponible.
* [ ] Mostrar reservaciones asociadas si está disponible.

### Consistencia

* [ ] Cliente asociado a una venta aparece correctamente.
* [ ] Información permanece después de recargar.

---

# 10. Productos

## `productos.spec.ts`

Este debería ser uno de los archivos con mayor cobertura.

### Listado

* [ ] Página carga correctamente.
* [ ] Se muestran productos.
* [ ] Se muestra nombre.
* [ ] Se muestra SKU.
* [ ] Se muestra precio.
* [ ] Se muestra stock.

### Crear producto

* [ ] Crear producto con información válida.
* [ ] Nombre obligatorio.
* [ ] SKU obligatorio.
* [ ] Precio detalle válido.
* [ ] Precio mayoreo válido.
* [ ] Categoría seleccionable.
* [ ] Stock mínimo válido.
* [ ] Descripción aceptada.
* [ ] Imagen URL válida cuando corresponda.
* [ ] Producto puede publicarse para clientes.

### Validaciones

* [ ] Precio negativo rechazado.
* [ ] Stock negativo rechazado.
* [ ] Campos obligatorios generan mensajes de validación.
* [ ] SKU duplicado es rechazado si el sistema lo restringe.

### Stock por sucursal

* [ ] Aparecen las sucursales existentes.
* [ ] Se puede asignar stock.
* [ ] Se puede distribuir stock entre sucursales.
* [ ] No permite distribución inválida según las reglas de negocio visibles.
* [ ] Los cambios permanecen después de recargar.

### Editar

* [ ] Editar nombre.
* [ ] Editar precio.
* [ ] Editar descripción.
* [ ] Editar stock.
* [ ] Editar publicación.
* [ ] Los cambios aparecen en el listado.

### Eliminar

* [ ] Cancelar eliminación.
* [ ] Confirmar eliminación.
* [ ] Producto desaparece del listado.

### Stock crítico

* [ ] Producto con poco stock aparece como crítico.
* [ ] Al modificar el stock deja de aparecer como crítico cuando corresponda.

---

# 11. Reportes

## `reportes.spec.ts`

### Carga

* [ ] Página carga correctamente.
* [ ] Métricas principales visibles.
* [ ] Gráficas visibles.

### Métricas

Comprobar visualización de las disponibles:

* [ ] Ingresos totales.
* [ ] Cantidad de transacciones.
* [ ] Ticket promedio.
* [ ] Productos vendidos.
* [ ] Margen de ganancia.
* [ ] Otras métricas implementadas.

### Filtros

* [ ] Filtrar por fecha.
* [ ] Filtrar por sucursal.
* [ ] Filtrar por producto.
* [ ] Filtrar por método de pago cuando esté disponible.
* [ ] Combinación de filtros.
* [ ] Limpiar filtros.

### Gráficas

* [ ] Gráficas cambian al aplicar filtros.
* [ ] Tooltip aparece al interactuar con puntos/barras cuando corresponda.
* [ ] Períodos sin información no rompen la interfaz.

### Exportación

Según lo que esté implementado:

* [ ] Exportar CSV.
* [ ] Exportar Excel.
* [ ] Exportar PDF.
* [ ] Se inicia la descarga.
* [ ] El archivo descargado no está vacío.

---

# 12. Usuarios

## `usuarios.spec.ts`

### Listado

* [ ] Página carga correctamente.
* [ ] Usuarios existentes visibles.
* [ ] Rol de cada usuario visible.
* [ ] Estado del usuario visible.

### Crear/invitar usuario

* [ ] Crear usuario válido.
* [ ] Nombre obligatorio.
* [ ] Correo obligatorio.
* [ ] Validación de formato de correo.
* [ ] Contraseña inicial obligatoria.
* [ ] Seleccionar rol.

### Roles

Probar como mínimo:

* [ ] Administrador.
* [ ] Vendor/Vendedor.
* [ ] Cajero.

### Editar

* [ ] Editar información de usuario.
* [ ] Cambiar rol.
* [ ] Cambios permanecen después de recargar.

### Desactivar/revocar

* [ ] Revocar acceso.
* [ ] Usuario desactivado no puede iniciar sesión.
* [ ] Reactivar usuario si la interfaz lo permite.

### Autorización

Este grupo es especialmente importante para caja negra.

#### Administrador

* [ ] Puede acceder a Configuración.
* [ ] Puede administrar usuarios.
* [ ] Puede administrar productos.
* [ ] Puede consultar Reportes.

#### Vendor

* [ ] Puede acceder a funcionalidades permitidas.
* [ ] No puede modificar configuración global.
* [ ] No puede acceder mediante URL directa a áreas prohibidas.

#### Cajero

* [ ] Puede procesar operaciones permitidas.
* [ ] No puede crear productos.
* [ ] No puede modificar precios.
* [ ] No puede acceder mediante URL directa a módulos administrativos restringidos.

---

# 13. Constructor Tienda

## `constructor.spec.ts`

### Carga

* [ ] Constructor carga correctamente.
* [ ] Preview de la tienda visible.
* [ ] Controles de personalización visibles.

### Colores

* [ ] Cambiar color principal.
* [ ] Preview cambia visualmente.
* [ ] Guardar configuración.
* [ ] Recargar página mantiene el color.

### Secciones

Según las disponibles actualmente:

* [ ] Hero.
* [ ] Categorías destacadas.
* [ ] Productos en promoción.
* [ ] Newsletter.
* [ ] Footer.

### Edición

* [ ] Modificar contenido de una sección.
* [ ] Preview refleja modificación.
* [ ] Guardar modificación.
* [ ] Recargar conserva modificación.

### Imágenes

* [ ] Configurar imagen mediante URL.
* [ ] Imagen válida aparece.
* [ ] URL inválida no rompe el constructor.

### Persistencia

* [ ] Guardar configuración.
* [ ] Salir del módulo.
* [ ] Volver a entrar.
* [ ] Cambios permanecen.

### Storefront

* [ ] Los cambios guardados aparecen en la vista pública del cliente.

---

# 14. Configuración

## `configuracion.spec.ts`

Dividir los tests internamente utilizando `test.describe()`.

## Información de tienda

* [ ] Página carga correctamente.
* [ ] Modificar nombre oficial.
* [ ] Modificar slug/subdominio.
* [ ] Configurar logo.
* [ ] Guardar.
* [ ] Información permanece después de recargar.

## Stripe

* [ ] Sección de Stripe visible.
* [ ] Campo Public Key disponible.
* [ ] Campo Private/Secret Key disponible.
* [ ] Campos sensibles aparecen protegidos cuando corresponda.
* [ ] Guardar configuración válida.
* [ ] Manejo visual de configuración inválida.

No comprobar directamente la API de Stripe desde estos tests.

## Cloudinary

* [ ] Campo Cloud Name.
* [ ] Campo API Key.
* [ ] Campo API Secret.
* [ ] Guardar configuración.
* [ ] Credenciales sensibles protegidas visualmente.
* [ ] Manejo de datos inválidos.

## SMTP

* [ ] Campo usuario.
* [ ] Campo contraseña.
* [ ] Contraseña oculta.
* [ ] Guardar configuración.
* [ ] Validación de campos obligatorios.
* [ ] Manejo visual de configuración inválida.

---

# 15. Pruebas transversales

Además de los archivos correspondientes a cada módulo, algunos comportamientos deben comprobarse en todos los apartados.

## Navegación

* [ ] Ninguna opción del sidebar produce 404.
* [ ] Ninguna navegación deja pantalla completamente vacía.
* [ ] Sidebar mantiene navegación consistente.
* [ ] Logout funciona desde cualquier apartado.

## Autenticación

* [ ] Rutas privadas requieren autenticación.
* [ ] Sesión expirada devuelve al login.
* [ ] Logout impide reutilizar páginas privadas.

## Autorización

* [ ] Ocultar acciones no autorizadas.
* [ ] Acceso mediante URL directa también debe estar restringido.
* [ ] Roles diferentes muestran funcionalidades diferentes.

## Formularios

* [ ] Campos obligatorios.
* [ ] Valores inválidos.
* [ ] Cancelación.
* [ ] Guardado.
* [ ] Mensajes de éxito.
* [ ] Mensajes de error.
* [ ] Doble clic en "Guardar" no genera duplicados cuando corresponda.

## Persistencia

Para operaciones CRUD importantes:

```text
Crear
  ↓
verificar UI
  ↓
recargar navegador
  ↓
verificar nuevamente
```

Esto permite comprobar el comportamiento desde caja negra sin consultar directamente la base de datos o la API.

---

# 16. Flujos E2E especialmente importantes

Además de probar módulos individualmente, conviene implementar algunos escenarios que atraviesen varios módulos.

## Flujo 1 — Producto e inventario

```text
Crear sucursal
→ Crear producto
→ Asignar stock
→ Verificar producto
→ Verificar stock
→ Verificar Dashboard
```

## Flujo 2 — Pedido

```text
Cliente genera pedido/reservación
→ aparece Reservación
→ aparece en Kanban
→ verificar estado
→ cambiar estado
→ verificar persistencia
```

## Flujo 3 — Pago

```text
Pedido pendiente
→ pago confirmado
→ Kanban: Pago verificado
→ inventario actualizado
→ Dashboard actualizado
→ Reportes actualizados
```

## Flujo 4 — Despacho

```text
Pedido pagado
→ cambiar a Despachado
→ recargar
→ continúa Despachado
→ Dashboard/Reportes reflejan operación
```

## Flujo 5 — Permisos

```text
Admin crea Cajero
→ cerrar sesión
→ iniciar sesión como Cajero
→ acceder a funciones permitidas
→ intentar acceder a Productos
→ intentar acceder a Configuración
→ verificar acceso denegado
```

---

# 17. Prioridades

No todos los tests tienen la misma importancia.

## P0 — Críticos

Implementar primero:

* Login/logout.
* Protección de rutas.
* Crear/editar producto.
* Gestión de stock.
* Reservaciones.
* Cambio de estados Kanban.
* Registro/visualización de pagos.
* Roles y permisos.
* Persistencia de operaciones.

## P1 — Importantes

Después:

* Dashboard.
* Filtros.
* Reportes.
* Sucursales.
* Clientes.
* Exportaciones.
* Constructor visual.

## P2 — Complementarios

Finalmente:

* Validaciones menores.
* Estados vacíos.
* Búsquedas inexistentes.
* Comportamientos visuales.
* Casos límite.

---

# 18. Regla para mantener las pruebas como caja negra

Los tests deberían preferir:

```ts
page.getByRole()
page.getByLabel()
page.getByText()
page.getByPlaceholder()
```

y utilizar `data-testid` únicamente cuando no exista un selector semántico estable.

Evitar que los tests dependan de:

```text
implementación interna
clases CSS generadas
estructura interna de componentes
funciones del frontend
base de datos
servicios internos
endpoints privados
```

La pregunta principal de cada test debe ser:

> "¿Esto podría comprobarlo un usuario utilizando únicamente el navegador?"

Si la respuesta es sí, encaja bien dentro de esta suite de caja negra.

---

# 19. Suite final propuesta

```text
tests/
├── auth.setup.ts
├── dashboard.spec.ts
├── sucursales.spec.ts
├── reservaciones.spec.ts
├── pagos.spec.ts
├── kanban.spec.ts
├── clientes.spec.ts
├── productos.spec.ts
├── reportes.spec.ts
├── usuarios.spec.ts
├── constructor.spec.ts
└── configuracion.spec.ts
```

Con esta separación, cada `.spec.ts` representa directamente una funcionalidad del sidebar y los tests permanecen fáciles de localizar, ejecutar y mantener.
