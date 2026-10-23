"""Fail closed on scanner errors, incomplete evidence or project-blocking alerts."""
import argparse
import json
from pathlib import Path

from common import write_json


def evaluate(output, policy, scope):
    errors, blocking, limitations = [], [], []
    for profile in policy["profiles"]:
        try:
            load = lambda name: json.loads((output / f"{name}-{profile}.json").read_text())
            execution, preparation = load("execution"), load("preparation")
            coverage, report = load("coverage"), load("report")
            if execution["exitCode"] not in policy["acceptedScannerExitCodes"]:
                errors.append(f"{profile}: error del escáner ({execution['exitCode']})")
            if not preparation["complete"]:
                errors.append(f"{profile}: preparación incompleta")
            if coverage.get("profile") != profile:
                errors.append(f"{profile}: identidad de cobertura incorrecta")
            for key in ("activeScanStarted", "activeScanCompleted", "passiveScanCompleted", "historyCollected"):
                if coverage.get(key) is not True:
                    errors.append(f"{profile}: {key} incompleto")
            scans = coverage.get("scans", [])
            if not scans or any(int(scan["progress"]) != 100 for scan in scans):
                errors.append(f"{profile}: escaneo activo sin terminar")
            if not coverage.get("activeRules"):
                errors.append(f"{profile}: sin reglas activas registradas")
            if coverage.get("activeMessageCount", 0) <= 0:
                errors.append(f"{profile}: sin solicitudes del escáner activo")
            actual = {(item["method"], item["path"]): item for item in coverage["baseline"]}
            observed = {(item["method"], item["path"], item["status"]) for item in coverage["observed"]
                        if item["count"] > 0}
            active_observed = {(item["method"], item["path"]) for item in coverage.get("activeObserved", [])
                               if item["count"] > 0}
            for required in scope["profiles"][profile]:
                key = (required["method"], required["path"])
                item = actual.get(key, {})
                if item.get("status") != required["status"] or item.get("businessAssertionPassed") is not True:
                    errors.append(f"{profile}: baseline requerido ausente/fallido: {' '.join(key)}")
                if (*key, required["status"]) not in observed:
                    errors.append(f"{profile}: operación no observada por ZAP: {' '.join(key)}")
                if key not in active_observed:
                    limitations.append({"profile": profile, "method": key[0], "path": key[1],
                                        "reason": "Sin mensajes activos coincidentes; solo evidencia de baseline/importación/pasivo. "
                                                  "API-Minimal puede no atacar operaciones sin parámetros."})
            html = (output / f"report-{profile}.html").read_text()
            if "<html" not in html.lower():
                raise ValueError("Reporte HTML inválido")
            if not isinstance(report.get("site"), list) or not report["site"]:
                raise ValueError("Reporte JSON sin sitios")
            for site in report["site"]:
                if not isinstance(site.get("alerts"), list):
                    raise ValueError("Reporte JSON sin lista de alertas")
                for alert in site["alerts"]:
                    risk, plugin = int(alert["riskcode"]), str(alert["pluginid"])
                    if risk not in (0, 1, 2, 3):
                        raise ValueError("Severidad desconocida")
                    if risk in policy["failOnRiskCodes"] or plugin in policy["failOnPluginIds"]:
                        blocking.append({"profile": profile, "pluginId": plugin,
                                         "riskCode": risk, "name": alert.get("name", alert.get("alert"))})
        except (OSError, ValueError, KeyError, TypeError) as error:
            errors.append(f"{profile}: evidencia ausente o inválida ({type(error).__name__})")
    result = {"passed": not errors and not blocking, "errors": errors, "blockingAlerts": blocking,
              "coverageLimitations": limitations}
    write_json(output / "gate.json", result)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    result = evaluate(args.output, json.loads((root / "policy.json").read_text()),
                      json.loads((root / "scope.json").read_text()))
    print(json.dumps(result, indent=2, ensure_ascii=False))
    raise SystemExit(0 if result["passed"] else 1)
