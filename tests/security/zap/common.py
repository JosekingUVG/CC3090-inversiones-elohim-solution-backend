"""Small standard-library helpers shared by preparation, hooks and the gate."""
import json
import re
from pathlib import Path
from urllib.parse import urlsplit


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")


def local_target(value):
    url = urlsplit(value)
    if (url.scheme != "http" or url.hostname not in ("localhost", "127.0.0.1")
            or url.username or url.password or url.query or url.fragment
            or url.path not in ("", "/") or not url.port):
        raise ValueError("ZAP_BASE_URL debe ser http://localhost:PUERTO o http://127.0.0.1:PUERTO")
    return value.rstrip("/")


def render(value, ids):
    if isinstance(value, str):
        return value.format_map(ids)
    if isinstance(value, list):
        return [render(item, ids) for item in value]
    if isinstance(value, dict):
        return {key: render(item, ids) for key, item in value.items()}
    return value


def matches(value, expected):
    if isinstance(expected, dict):
        return isinstance(value, dict) and all(
            key in value and matches(value[key], item) for key, item in expected.items())
    if isinstance(expected, list):
        return isinstance(value, list) and all(
            any(matches(item, wanted) for item in value) for wanted in expected)
    return value == expected


def assert_response(body, spec):
    if "equals" in spec and body != spec["equals"]:
        raise AssertionError("La respuesta no coincide con el resultado esperado")
    if "contains" in spec and not matches(body, spec["contains"]):
        raise AssertionError("La respuesta no contiene los datos sintéticos esperados")
    if "type" in spec:
        expected_type = {"list": list, "dict": dict}[spec["type"]]
        if not isinstance(body, expected_type):
            raise AssertionError("Tipo de respuesta inesperado")


def scrub(text, secrets=()):
    for secret in sorted((s for s in secrets if s), key=len, reverse=True):
        text = text.replace(secret, "[REDACTED]")
    # JWTs returned in an error/alert must not enter published artifacts.
    return re.sub(r"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED-JWT]", text)
