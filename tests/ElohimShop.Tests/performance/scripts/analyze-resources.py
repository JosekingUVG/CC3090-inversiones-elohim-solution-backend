#!/usr/bin/env python3
import argparse
import csv
import re
from collections import defaultdict
from pathlib import Path


UNITS = {"B": 1 / 1024 / 1024, "KiB": 1 / 1024, "MiB": 1, "GiB": 1024, "TiB": 1024 * 1024,
         "kB": 1 / 1000, "MB": 1, "GB": 1000, "TB": 1000 * 1000}


def number(value: str) -> float:
    match = re.search(r"-?[0-9]+(?:\.[0-9]+)?", value or "")
    return float(match.group()) if match else 0.0


def memory_mib(value: str) -> float:
    used = (value or "").split("/")[0].strip()
    match = re.match(r"([0-9.]+)\s*([A-Za-z]+)", used)
    return float(match.group(1)) * UNITS.get(match.group(2), 1) if match else 0.0


def average(values: list[float]) -> float:
    return sum(values) / len(values) if values else 0.0


def read_host_rows(path: Path):
    """Admite CSV correcto y capturas antiguas con coma decimal del locale."""
    with path.open(newline="", encoding="utf-8") as source:
        rows = csv.reader(source)
        header = next(rows)
        for values in rows:
            if len(values) == len(header):
                yield dict(zip(header, values))
            elif len(values) == 14:
                repaired = values[:2] + [f"{values[i]}.{values[i + 1]}" for i in range(2, 14, 2)]
                yield dict(zip(header, repaired))
            else:
                raise ValueError(f"Fila host inválida en {path}: se esperaban 8 columnas y llegaron {len(values)}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Resume baseline, carga y recuperación de recursos")
    parser.add_argument("result_dir", type=Path)
    parser.add_argument("--active-prefix", choices=("load", "stress"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    phases = ["baseline", args.active_prefix, "recovery"]
    docker_values: dict[tuple[str, str], dict[str, list[float]]] = defaultdict(lambda: defaultdict(list))
    host_values: dict[str, dict[str, list[float]]] = defaultdict(lambda: defaultdict(list))

    for phase in phases:
        docker_file = args.result_dir / f"{phase}-docker.csv"
        if docker_file.exists():
            with docker_file.open(newline="", encoding="utf-8") as source:
                for row in csv.DictReader(source):
                    key = (phase, row["container"])
                    docker_values[key]["cpu"].append(number(row["cpu_percent"]))
                    docker_values[key]["memory"].append(memory_mib(row["memory_usage"]))
        host_file = args.result_dir / f"{phase}-host.csv"
        if host_file.exists():
            for row in read_host_rows(host_file):
                host_values[phase]["cpu"].append(float(row["cpu_percent"]))
                host_values[phase]["memory"].append(float(row["memory_used_mib"]))
                host_values[phase]["swap"].append(float(row["swap_used_mib"]))

    rows = []
    containers = sorted({container for _, container in docker_values})
    for container in containers:
        baseline_cpu = average(docker_values[("baseline", container)]["cpu"])
        baseline_memory = average(docker_values[("baseline", container)]["memory"])
        for phase in phases:
            cpu = average(docker_values[(phase, container)]["cpu"])
            memory = average(docker_values[(phase, container)]["memory"])
            rows.append({"scope": "docker", "name": container, "phase": phase,
                         "avg_cpu_percent": f"{cpu:.2f}", "avg_memory_mib": f"{memory:.2f}",
                         "avg_swap_mib": "", "delta_cpu_points": f"{cpu-baseline_cpu:.2f}",
                         "delta_memory_mib": f"{memory-baseline_memory:.2f}"})

    baseline_host_cpu = average(host_values["baseline"]["cpu"])
    baseline_host_memory = average(host_values["baseline"]["memory"])
    for phase in phases:
        cpu = average(host_values[phase]["cpu"])
        memory = average(host_values[phase]["memory"])
        rows.append({"scope": "host", "name": "host", "phase": phase,
                     "avg_cpu_percent": f"{cpu:.2f}", "avg_memory_mib": f"{memory:.2f}",
                     "avg_swap_mib": f"{average(host_values[phase]['swap']):.2f}",
                     "delta_cpu_points": f"{cpu-baseline_host_cpu:.2f}",
                     "delta_memory_mib": f"{memory-baseline_host_memory:.2f}"})

    fieldnames = ["scope", "name", "phase", "avg_cpu_percent", "avg_memory_mib", "avg_swap_mib",
                  "delta_cpu_points", "delta_memory_mib"]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    with args.output.open(encoding="utf-8") as result:
        print(result.read(), end="")


if __name__ == "__main__":
    main()
