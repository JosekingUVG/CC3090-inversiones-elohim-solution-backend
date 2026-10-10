# CI y CD del backend

Estos workflows pertenecen al repositorio del backend. Hay que publicar ambos
archivos en su rama `main` para habilitar el flujo completo.

## Configuración en GitHub

En Settings → Secrets and variables → Actions, configurar:

| Tipo | Nombre | Valor |
| --- | --- | --- |
| Secret | `DOCKERHUB_USERNAME` | Usuario de Docker Hub |
| Secret | `DOCKERHUB_TOKEN` | Token de Docker Hub con permiso de escritura en el repositorio de destino |
| Variable | `DOCKERHUB_IMAGE` | Nombre completo, por ejemplo `tuusuario/elohim-backend` |

Crear el repositorio de destino en Docker Hub antes de publicar. No guardar
credenciales en YAML ni en archivos `.env` versionados.

## CI

Se ejecuta en pushes, pull requests y manualmente. Descarga el `main` del
repositorio principal y sus submódulos de forma recursiva. Luego reemplaza el
backend por el commit que disparó el workflow (el merge de prueba en un PR).
Frontend y docs conservan las versiones registradas en el repositorio principal.
Los repositorios auxiliares deben ser accesibles para el checkout; si son privados,
se necesita configurar una credencial de lectura con acceso a ellos.

Ejecuta toda la suite xUnit existente con .NET 10, exporta resultados TRX y
cobertura, y construye y levanta PostgreSQL y backend. Después ejecuta todos
los scripts `tests/integration/int*.py`, que hoy incluyen `INT-02`, y publica
su evidencia como el artefacto `backend-integration-test-evidence`.
Usa `.env.example` y un certificado autofirmado efímero exclusivamente para CI.
Comprueba Swagger, las páginas de frontend y docs y su acceso por Nginx;
también verifica que ningún contenedor haya reiniciado. Siempre intenta mostrar
logs y eliminar los contenedores y volúmenes temporales.

Las pruebas existentes incluyen mocks y EF en memoria. Las comprobaciones HTTP
son pruebas de disponibilidad, no una suite funcional completa de los flujos de
negocio contra PostgreSQL.

## CD

Solo publica después de un `Backend CI` exitoso originado por un push a `main`
del mismo repositorio. Descarga la imagen del artefacto de esa ejecución exacta;
no vuelve a compilar ni ejecuta scripts del repositorio con las credenciales.

Publica `DOCKERHUB_IMAGE:sha-<commit completo>`. No actualiza `latest`, evitando
que una ejecución antigua que termine tarde sustituya una versión más reciente.
Los artefactos de imagen se conservan tres días: después de ese plazo hay que
volver a ejecutar el CI original para publicar nuevamente.

Este CD publica únicamente backend; no publica frontend, docs, PostgreSQL ni
Nginx y no despliega a un servidor. Configurar `validate` como check requerido
en las reglas de protección de `main` para bloquear merges con CI fallido.
