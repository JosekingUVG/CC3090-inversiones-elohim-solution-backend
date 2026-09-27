# Resultado de seguridad — SEG-01

## Estado del resultado

Este documento refleja la evidencia real obtenida contra la API en vivo del entorno local con la corrección aplicada para el guard de tenant y slug.

## Objetivo

Verificar que un cliente A no pueda:

- leer ni modificar el carrito de un cliente A2 dentro del mismo tenant;
- eliminar artículos ajenos;
- operar sobre un carrito de otra tienda con `X-Tenant-ID` o `X-Tenant-Slug` incorrecto;
- exponer datos o alterar el estado del propietario después del intento.

## Evidencia ejecutada

Comando ejecutado:

```bash
cd /home/josel/CC3090-inversiones-elohim-solution
. backend/tests/security/.venv/bin/activate
python -m pytest backend/tests/security/test_seg01_aislamiento.py -q --junitxml=backend/tests/security/reports/seg01.xml
```

Resultado observado:

```text
4 passed in 1.87s
```

El archivo JUnit generado es:

- `backend/tests/security/reports/seg01.xml`

## Tabla de resultados

| Caso | Descripción | Resultado real | Observación |
|---|---|---|---|
| SEG01-01 | A1 intenta modificar item de A2 | `PASSED` | El intento ajeno fue bloqueado y el artículo del propietario permaneció intacto. |
| SEG01-02 | A1 intenta eliminar item de A2 | `PASSED` | El intento ajeno fue bloqueado y el artículo del propietario permaneció intacto. |
| SEG01-03 | A1 intenta consultar o escribir el tenant B | `PASSED` | La API rechazó acceso con `401` o `403`, sin exposición de datos del tenant B. |
| SEG01-04 | A1 intenta usar `X-Tenant-Slug` ajeno | `PASSED` | La API rechazó el acceso usando el slug ajeno y no devolvió datos de B. |

## Contrato real observado

Durante la validación en vivo, la API devolvió principalmente:

- `401 Unauthorized` cuando faltaba o no era válido el contexto de autenticación/tenant.
- `403 Forbidden` cuando el usuario autenticado intentaba acceder a un tenant o cart ajeno.
- `404` en casos de recurso inexistente o no perteneciente al usuario, pero no como evidencia de una fuga de datos.

Esto confirma que la protección se materializa en el backend con rechazos explícitos y sin cambio de estado del propietario.

## Resultado formal

> SEG-01 **pasó**: se ejecutaron 4 casos, se bloquearon 4 accesos ajenos y no se detectaron modificaciones ni exposiciones no autorizadas. Los controles positivos funcionaron correctamente.
