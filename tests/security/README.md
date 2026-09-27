# Seguridad — SEG-01 (aislamiento de carrito y tenant)

Esta carpeta reúne la batería de pruebas automatizadas para el caso #9 del plan de seguridad: `SEG-01`.

## Requisitos

- Python 3.11+
- API de ElohimShop ejecutándose en `http://localhost:5000`
- Dependencias de pytest y requests

## Instalación

```bash
cd /home/josel/CC3090-inversiones-elohim-solution/backend/tests/security
python3 -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

## Ejecución

```bash
cd /home/josel/CC3090-inversiones-elohim-solution/backend/tests/security
source .venv/bin/activate
export SECURITY_BASE_URL=http://localhost:5000
pytest -v --junitxml=reports/seg01.xml -m seg01
```

## Estructura

```text
backend/tests/security/
├── README.md
├── requirements.txt
├── conftest.py
├── test_seg01_aislamiento.py
├── reports/
└── SEG01_RESULTADOS_20260927.md
```

## Nota importante

Estas pruebas están pensadas para ejecutarse contra la API con autenticación real, `X-Tenant-ID` y JWT válidos. Deben correr sobre un entorno de prueba aislado, no con datos productivos ni con credenciales reales.

## Salida esperada

El comando genera un reporte JUnit XML en `backend/tests/security/reports/seg01.xml` y devuelve resultado por caso de prueba:

- `PASSED` para los controles positivos válidos.
- `FAILED` cuando un cliente intenta operar sobre un recurso ajeno o con un tenant incorrecto.
- `XFAIL` solo si se decide registrar una diferencia documental del contrato real del backend.

> Si la API no está levantada en el puerto 5000, la batería fallará durante la preparación de datos y no debe ejecutarse en producción sin preparación previa.
