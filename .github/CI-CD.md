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

Se ejecuta en pushes, pull requests y manualmente. Clona el repositorio del
backend en el commit bajo prueba (el merge temporal en un PR), con checkout
recursivo de sus propios submódulos si los hubiera. No clona el repositorio raíz
ni los repositorios de frontend y docs.

Ejecuta la suite xUnit en `tests/ElohimShop.Tests/ElohimShop.Tests.csproj` con
.NET 10 y publica resultados TRX y cobertura como `backend-test-results`.
Copia `.env.example` y genera un Compose temporal para construir y levantar
únicamente PostgreSQL y backend con datos de prueba. Comprueba Swagger por HTTP
en `http://localhost:5000/swagger/v1/swagger.json` y verifica que ambos
contenedores estén en ejecución y sin reinicios. No levanta Nginx ni configura TLS.

Después ejecuta todos los scripts `tests/integration/int*.py`, indicando el
Compose temporal mediante `INTEGRATION_COMPOSE_FILE`, y publica su evidencia
como `backend-integration-test-evidence`.

En pushes a `main`, exporta la imagen validada `elohim-backend:ci` como el
artefacto `backend-image`, con retención de tres días, para su posterior
publicación por CD. Siempre intenta mostrar logs y eliminar los contenedores y
volúmenes temporales.

Las pruebas existentes incluyen mocks y EF en memoria. La comprobación de
Swagger verifica disponibilidad; la cobertura funcional contra el entorno
levantado depende de los scripts de integración ejecutados.

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
