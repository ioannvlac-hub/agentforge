#!/usr/bin/env python3
"""
Reviewed script exposed to the agent as the `analyze_csv` tool.

Contract: JSON on stdin ({"path": "relative/file.csv"}), JSON on stdout.
Standard library only, so it runs anywhere python3 does.
"""
import csv
import json
import statistics
import sys
from pathlib import Path


def is_number(value: str) -> bool:
    try:
        float(value)
        return True
    except ValueError:
        return False


def analyze(path: Path) -> dict:
    with path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))

    if not rows:
        return {"rows": 0, "columns": [], "numeric": {}}

    columns = list(rows[0].keys())
    numeric = {}

    for column in columns:
        values = [row[column] for row in rows if row[column] not in (None, "")]
        if values and all(is_number(v) for v in values):
            numbers = [float(v) for v in values]
            numeric[column] = {
                "count": len(numbers),
                "min": min(numbers),
                "max": max(numbers),
                "mean": round(statistics.fmean(numbers), 4),
                "sum": round(sum(numbers), 4),
            }

    return {"rows": len(rows), "columns": columns, "numeric": numeric}


def main() -> int:
    try:
        request = json.load(sys.stdin)
        relative = request.get("path")
        if not relative:
            raise ValueError("path is required")

        root = Path.cwd().resolve()
        target = (root / relative).resolve()
        if root not in target.parents and target != root:
            raise PermissionError(f"{relative} escapes the working directory")
        if not target.is_file():
            raise FileNotFoundError(f"{relative} not found")

        print(json.dumps(analyze(target)))
        return 0
    except Exception as exc:  # noqa: BLE001 - report every failure to the agent
        print(f"{type(exc).__name__}: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
