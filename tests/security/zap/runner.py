"""Run pinned ZAP containers, preserve evidence and remove only owned resources."""
import json
import os
import re
import secrets
import shutil
import signal
import socket
import subprocess
import tempfile
import uuid
from pathlib import Path

from common import local_target, scrub, write_json
from evaluate import evaluate
from prepare import prepare

ROOT = Path(__file__).resolve().parent


def proxy_port():
    # Avoid ZAP's default ephemeral range: polling/outbound sockets can occupy it
    # while Java starts. Bind-check a non-ephemeral port instead of connect_ex.
    for _ in range(100):
        port = 18080 + secrets.randbelow(10000)
        with socket.socket() as sock:
            try:
                sock.bind(("127.0.0.1", port))
            except OSError:
                continue
            return port
    raise RuntimeError("No se encontró un puerto local disponible para ZAP")


def main():
    os.umask(0o077)
    base = local_target(os.environ.get("ZAP_BASE_URL", "http://localhost:5000"))
    policy = json.loads((ROOT / "policy.json").read_text())
    scope = json.loads((ROOT / "scope.json").read_text())
    image = (ROOT / "image.txt").read_text().strip()
    if not re.fullmatch(r"ghcr\.io/zaproxy/zaproxy@sha256:[0-9a-f]{64}", image):
        raise ValueError("image.txt debe fijar una imagen oficial por digest")
    timeout = int(os.environ.get("ZAP_PROFILE_TIMEOUT_SECONDS", "900"))
    if timeout < 30:
        raise ValueError("El timeout mínimo por perfil es de 30 segundos")
    output = Path(os.environ.get("ZAP_OUTPUT_DIR") or tempfile.mkdtemp(prefix="zap-artifacts-" )).resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("ZAP_OUTPUT_DIR debe estar vacío: no se permite reutilizar evidencia antigua")
    print(f"Artefactos ZAP: {output}", flush=True)
    write_json(output / "manifest.json", {"image": image, "baseUrl": base, "profiles": policy["profiles"],
                                          "timeoutPerProfileSeconds": timeout})
    shutil.copyfile(ROOT / "policy.json", output / "policy.json")
    shutil.copyfile(ROOT / "scope.json", output / "scope.json")
    owned = set()

    def stop(signum, frame):
        raise KeyboardInterrupt()

    signal.signal(signal.SIGTERM, stop)
    with tempfile.TemporaryDirectory(prefix="zap-private-") as private:
        # Parent stays 0700. Only the mounted child is writable by Docker's zap user.
        try:
            subprocess.run(["docker", "image", "inspect", image], check=True, stdout=subprocess.DEVNULL,
                           stderr=subprocess.DEVNULL, timeout=30)
        except subprocess.CalledProcessError:
            subprocess.run(["docker", "pull", image], check=True, timeout=300)
        try:
            for profile in policy["profiles"]:
                work = Path(private) / profile
                work.mkdir(mode=0o700)
                code, secret_values = 3, []
                name = "backend-zap-" + profile + "-" + uuid.uuid4().hex[:10]
                try:
                    secret_values = prepare(base, profile, work, output, scope)
                    for filename in ("common.py", "hooks.py"):
                        shutil.copyfile(ROOT / filename, work / filename)
                    # This child is only reachable via its private ancestor or the explicit bind mount.
                    work.chmod(0o777)
                    for path in work.iterdir():
                        path.chmod(0o644)
                    owned.add(name)
                    command = ["docker", "run", "--rm", "--name", name, "--network", "host",
                               "--volume", f"{work}:/zap/wrk:rw", "--env", "PYTHONPATH=/zap/wrk",
                               "--env", "JAVA_TOOL_OPTIONS=-Djava.awt.headless=true",
                               image, "zap-api-scan.py", "-P", str(proxy_port()),
                               "-t", "/zap/wrk/openapi.json", "-f", "openapi",
                               "--hook", "/zap/wrk/hooks.py", "-r", "report.html", "-J", "report.json",
                               "-T", "3", "-z", "-silent -host 127.0.0.1 -config ascan.threadPerHost=2"]
                    print(f"ZAP {profile}: preparación correcta; iniciando análisis activo", flush=True)
                    with (work / "scanner.log").open("w") as log:
                        code = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=timeout).returncode
                except subprocess.TimeoutExpired:
                    code = 124
                except KeyboardInterrupt:
                    code = 130
                    raise
                except Exception as error:
                    # Never print exception bodies from HTTP/SDK calls containing session credentials.
                    write_json(output / f"error-{profile}.json", {"stage": "prepare-or-scan", "type": type(error).__name__,
                               "message": scrub(str(error), secret_values)})
                    print(f"ZAP {profile}: error ({type(error).__name__}); se bloqueará el control", flush=True)
                finally:
                    if name in owned:
                        subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL,
                                       stderr=subprocess.DEVNULL, timeout=30)
                        owned.discard(name)
                    write_json(output / f"execution-{profile}.json", {"exitCode": code})
                    for filename, dest in (("report.html", f"report-{profile}.html"),
                                           ("report.json", f"report-{profile}.json"),
                                           ("coverage.json", f"coverage-{profile}.json"),
                                           ("scanner.log", f"scanner-{profile}.log")):
                        path = work / filename
                        if path.exists():
                            (output / dest).write_text(scrub(path.read_text(errors="replace"), secret_values))
                    print(f"ZAP {profile}: código {code}", flush=True)
        finally:
            for name in owned:
                subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, timeout=30)
            result = evaluate(output, policy, scope)
    print(f"Control ZAP: {'PASS' if result['passed'] else 'FAIL'}; {output / 'gate.json'}", flush=True)
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
