#!/usr/bin/env python3
"""
Evaluation harness for AgentForge.

Runs every scenario in scenarios.json through the agent CLI, scores the result
against deterministic checks (and an optional LLM judge), and writes a report.
Exit code is non-zero if any scenario fails, so this drops straight into CI.

Usage:
  python run_evals.py                         # all scenarios
  python run_evals.py --only arithmetic-exact # one scenario
  python run_evals.py --agent-cmd "path/to/agentforge"
"""
from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from judge import llm_judge

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_AGENT_CMD = f"dotnet run --project {REPO_ROOT / 'src' / 'AgentForge.Cli'} --"


def run_agent(agent_cmd: str, task: str, workdir: Path, trace_path: Path, max_steps: int) -> dict:
    command = shlex.split(agent_cmd) + [
        "--task", task,
        "--json",
        "--auto-approve",
        "--workdir", str(workdir),
        "--scripts", str(REPO_ROOT / "python" / "tools"),
        "--trace", str(trace_path),
        "--max-steps", str(max_steps),
    ]
    started = time.perf_counter()
    completed = subprocess.run(command, capture_output=True, text=True, cwd=REPO_ROOT)
    elapsed = time.perf_counter() - started

    # The CLI prints exactly one JSON line on stdout; dotnet may add build noise before it.
    result = None
    for line in reversed(completed.stdout.strip().splitlines()):
        line = line.strip()
        if line.startswith("{"):
            try:
                result = json.loads(line)
                break
            except json.JSONDecodeError:
                continue

    if result is None:
        result = {
            "final_answer": "",
            "steps": 0,
            "stop_reason": "HarnessError",
            "input_tokens": 0,
            "output_tokens": 0,
            "error": completed.stderr[-2000:],
        }

    result["elapsed_s"] = round(elapsed, 2)
    result["tools_used"] = tools_from_trace(trace_path)
    return result


def tools_from_trace(trace_path: Path) -> list[str]:
    if not trace_path.exists():
        return []
    used = []
    for line in trace_path.read_text(encoding="utf-8").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        payload = event.get("payload", {})
        if payload.get("type") == "tool_call":
            used.append(payload.get("Name") or payload.get("name"))
    return used


def evaluate(check: dict, result: dict) -> tuple[bool, str]:
    kind, value = check["type"], check["value"]
    answer = result.get("final_answer", "")

    if kind == "contains":
        ok = value.lower() in answer.lower()
        return ok, f"contains '{value}'"
    if kind == "regex":
        ok = re.search(value, answer) is not None
        return ok, f"matches /{value}/"
    if kind == "max_steps":
        ok = result.get("steps", 999) <= value
        return ok, f"steps {result.get('steps')} <= {value}"
    if kind == "tool_used":
        ok = value in result.get("tools_used", [])
        return ok, f"used tool '{value}'"
    if kind == "not_tool_used":
        ok = value not in result.get("tools_used", [])
        return ok, f"did not use tool '{value}'"
    if kind == "llm_judge":
        verdict = llm_judge(task=result.get("task", ""), answer=answer, rubric=value)
        if verdict is None:
            return True, "llm judge skipped (no ANTHROPIC_API_KEY)"
        return verdict.passed, f"judge: {verdict.reason}"

    return False, f"unknown check type '{kind}'"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scenarios", default=str(Path(__file__).with_name("scenarios.json")))
    parser.add_argument("--agent-cmd", default=DEFAULT_AGENT_CMD)
    parser.add_argument("--only", help="run a single scenario id")
    parser.add_argument("--max-steps", type=int, default=8)
    parser.add_argument("--out", default=str(Path(__file__).with_name("results")))
    args = parser.parse_args()

    scenarios = json.loads(Path(args.scenarios).read_text(encoding="utf-8"))
    if args.only:
        scenarios = [s for s in scenarios if s["id"] == args.only]
        if not scenarios:
            print(f"no scenario with id '{args.only}'", file=sys.stderr)
            return 2

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    report_lines = [f"# AgentForge evaluation report ({stamp})", "", "| Scenario | Result | Steps | Tokens | Time | Details |", "|---|---|---|---|---|---|"]
    all_results = []
    failures = 0

    for scenario in scenarios:
        print(f"running {scenario['id']} ...", end=" ", flush=True)
        trace_path = out_dir / f"{stamp}-{scenario['id']}.jsonl"
        result = run_agent(args.agent_cmd, scenario["task"], REPO_ROOT, trace_path, args.max_steps)
        result["task"] = scenario["task"]

        outcomes = [evaluate(check, result) for check in scenario["checks"]]
        passed = all(ok for ok, _ in outcomes)
        failures += 0 if passed else 1
        print("PASS" if passed else "FAIL")

        details = "; ".join(("ok: " if ok else "FAIL: ") + desc for ok, desc in outcomes)
        tokens = result.get("input_tokens", 0) + result.get("output_tokens", 0)
        report_lines.append(
            f"| {scenario['id']} | {'PASS' if passed else 'FAIL'} | {result.get('steps')} | {tokens} | {result.get('elapsed_s')}s | {details} |"
        )
        all_results.append({"scenario": scenario, "result": result, "passed": passed, "checks": outcomes})

    total = len(scenarios)
    report_lines += ["", f"**{total - failures}/{total} passed.**"]
    (out_dir / f"{stamp}-report.md").write_text("\n".join(report_lines), encoding="utf-8")
    (out_dir / f"{stamp}-results.json").write_text(json.dumps(all_results, indent=2), encoding="utf-8")

    print("\n".join(report_lines))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
