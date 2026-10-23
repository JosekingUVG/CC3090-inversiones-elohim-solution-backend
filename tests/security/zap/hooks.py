"""Packaged API-scan hooks. No credentials or response bodies enter coverage.json."""
import json
import re
from collections import Counter
from pathlib import Path
from urllib.parse import urlsplit

from common import assert_response, write_json

WORK = Path("/zap/wrk")
state = {"baseline": [], "activeScanCompleted": False, "passiveScanCompleted": False,
         "historyCollected": False}
session = None


def save():
    write_json(WORK / "coverage.json", state)


def probe(zap, operation):
    url = session["base"] + operation["url"]
    body = json.dumps(operation["body"]) if "body" in operation else ""
    headers = [f'{operation["method"]} {url} HTTP/1.1',
               f'Host: {urlsplit(url).netloc}', 'Accept: application/json',
               f'Authorization: Bearer {session["token"]}', f'X-Tenant-ID: {session["tenant"]}']
    if body:
        headers.extend(['Content-Type: application/json', f'Content-Length: {len(body.encode())}'])
    result = zap.core.send_request("\r\n".join(headers) + "\r\n\r\n" + body, followredirects=False)
    if not isinstance(result, list) or not result:
        raise RuntimeError("ZAP no devolvió evidencia de la solicitud válida")
    message = result[0]
    status = int(message["responseHeader"].splitlines()[0].split()[1])
    entry = {"method": operation["method"], "path": operation["path"], "status": status,
             "messageId": message.get("id"), "businessAssertionPassed": False}
    state["baseline"].append(entry)
    save()
    if status != operation["status"]:
        raise RuntimeError(f'Baseline {operation["method"]} {operation["path"]}: HTTP {status}')
    assert_response(json.loads(message["responseBody"]), operation["assert"])
    entry["businessAssertionPassed"] = True
    save()


def zap_started(zap, target):
    global session
    session = json.loads((WORK / "session.json").read_text())
    state.update(profile=session["profile"], version=zap.core.version, addons=zap.autoupdate.installed_addons)
    save()
    # Only selected paths enter the context, and credentials only go to this origin.
    context_name = "zap-" + session["profile"]
    zap.context.new_context(context_name)
    for operation in session["operations"]:
        pattern = re.escape(session["base"] + operation["url"]) + r"(?:\?.*)?$"
        zap.context.include_in_context(context_name, pattern)
    for name, value in (("Authorization", "Bearer " + session["token"]), ("X-Tenant-ID", session["tenant"])):
        result = zap.replacer.add_rule(name, "true", "REQ_HEADER", "false", name, replacement=value,
                                      url=re.escape(session["base"]) + r"/api/v1(?:/.*)?$")
        if result != "OK":
            raise RuntimeError("No se pudo configurar la cabecera " + name)
    # Before import: importing write operations can change the fixture's state.
    for operation in session["operations"]:
        probe(zap, operation)


def zap_active_scan(zap, target, policy):
    state["activeScanStarted"] = True
    state["scanPolicy"] = policy
    state["activeRules"] = zap.ascan.scanners(scanpolicyname=policy)
    save()
    return zap, target, policy


def zap_active_scan_wrap(result):
    state["activeScanCompleted"] = True
    save()


def zap_pre_shutdown(zap):
    state["passiveScanCompleted"] = int(zap.pscan.records_to_scan) == 0
    state["scans"] = zap.ascan.scans
    active_ids = {str(message_id) for scan in state["scans"]
                  for message_id in zap.ascan.messages_ids(scan["id"])}
    state["activeMessageCount"] = len(active_ids)
    counts = Counter()
    active_counts = Counter()
    offset = 0
    while True:
        messages = zap.core.messages(baseurl=session["base"], start=offset, count=500)
        if not isinstance(messages, list):
            raise RuntimeError("Historial ZAP inválido")
        if not messages:
            break
        for message in messages:
            request = message["requestHeader"].splitlines()[0].split()
            response = message["responseHeader"].splitlines()[0].split()
            # Queries and bodies can contain injected secrets; retain only matched templates.
            path = urlsplit(request[1]).path
            for operation in session["operations"]:
                if request[0] == operation["method"] and path == operation["url"]:
                    counts[(operation["method"], operation["path"], response[1])] += 1
                    if str(message["id"]) in active_ids:
                        active_counts[(operation["method"], operation["path"], response[1])] += 1
        offset += len(messages)
    state["observed"] = [{"method": method, "path": path, "status": int(status), "count": count}
                         for (method, path, status), count in sorted(counts.items())]
    state["activeObserved"] = [{"method": method, "path": path, "status": int(status), "count": count}
                               for (method, path, status), count in sorted(active_counts.items())]
    state["historyCollected"] = True
    state["limitation"] = ("Baselines y mensajes observados no prueban que cada regla haya atacado cada operación; "
                           "los payloads activos pueden invalidar estado. Roles/aislamiento requieren pruebas explícitas.")
    save()
