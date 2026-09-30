#!/usr/bin/env python3
import argparse
import csv
import math
import re
import sys
from collections import defaultdict
from pathlib import Path


def percentile(values: list[int], percent: float) -> int:
    ordered = sorted(values)
    return ordered[max(0, math.ceil(percent * len(ordered)) - 1)]


def as_bool(value: str) -> bool:
    return value.strip().lower() == "true"


def main() -> int:
    parser = argparse.ArgumentParser(description="Resume un JTL CSV de JMeter")
    parser.add_argument("jtl", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mode", choices=("load", "stress"), required=True)
    args = parser.parse_args()

    grouped: dict[str, list[dict[str, str]]] = defaultdict(list)
    with args.jtl.open(newline="", encoding="utf-8") as source:
        reader = csv.DictReader(source)
        required = {"timeStamp", "elapsed", "label", "responseCode", "success"}
        missing = required.difference(reader.fieldnames or [])
        if missing:
            raise SystemExit(f"JTL sin columnas requeridas: {', '.join(sorted(missing))}")
        for row in reader:
            grouped[row["label"]].append(row)

    fieldnames = [
        "label", "samples", "average_ms", "p95_ms", "p99_ms", "throughput_rps",
        "errors", "error_percent", "timeouts", "classification"
    ]
    summaries = []
    failed = False

    for label, rows in sorted(grouped.items()):
        elapsed = [int(row["elapsed"]) for row in rows]
        starts = [int(row["timeStamp"]) for row in rows]
        finish = max(start + duration for start, duration in zip(starts, elapsed))
        seconds = max((finish - min(starts)) / 1000, 0.001)
        errors = sum(not as_bool(row["success"]) for row in rows)
        timeouts = sum(
            "timeout" in (row.get("responseMessage") or "").lower()
            or (row.get("responseCode") or "").startswith("Non HTTP response")
            for row in rows
        )
        error_percent = 100 * errors / len(rows)
        p95 = percentile(elapsed, 0.95)
        p99 = percentile(elapsed, 0.99)

        classification = "informativo"
        if args.mode == "load":
            classification = "aceptable"
            if error_percent >= 1 or p99 > 2000 or timeouts:
                classification = "no_aceptable"
            if label in {"Login", "Carrito"} and p95 > 1000:
                classification = "no_aceptable"
            failed = failed or classification == "no_aceptable"
        else:
            classification = "aceptable"
            if error_percent >= 20 or timeouts:
                classification = "saturacion"
            elif error_percent > 5 or p95 > 2000:
                classification = "degradacion"

        summaries.append({
            "label": label,
            "samples": len(rows),
            "average_ms": f"{sum(elapsed) / len(elapsed):.2f}",
            "p95_ms": p95,
            "p99_ms": p99,
            "throughput_rps": f"{len(rows) / seconds:.2f}",
            "errors": errors,
            "error_percent": f"{error_percent:.3f}",
            "timeouts": timeouts,
            "classification": classification,
        })

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(summaries)

    writer = csv.DictWriter(sys.stdout, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(summaries)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())

