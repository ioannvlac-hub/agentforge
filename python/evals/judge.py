"""
Optional LLM-as-judge for checks that resist regexes ("did it refuse politely?").

Uses the Anthropic Messages API through urllib, so the harness stays dependency-free.
Returns None when no API key is present, and the harness treats that as "skipped".
"""
from __future__ import annotations

import json
import os
import urllib.request
from dataclasses import dataclass

JUDGE_MODEL = os.environ.get("AGENTFORGE_JUDGE_MODEL", "claude-sonnet-4-5")


@dataclass
class Verdict:
    passed: bool
    reason: str


def llm_judge(task: str, answer: str, rubric: str) -> Verdict | None:
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        return None

    prompt = (
        "You are grading an AI agent's final answer against a rubric.\n\n"
        f"TASK GIVEN TO THE AGENT:\n{task}\n\n"
        f"AGENT'S FINAL ANSWER:\n{answer}\n\n"
        f"RUBRIC:\n{rubric}\n\n"
        'Respond with JSON only: {"pass": true|false, "reason": "<one sentence>"}'
    )

    body = json.dumps({
        "model": JUDGE_MODEL,
        "max_tokens": 200,
        "messages": [{"role": "user", "content": prompt}],
    }).encode("utf-8")

    request = urllib.request.Request(
        "https://api.anthropic.com/v1/messages",
        data=body,
        headers={
            "content-type": "application/json",
            "x-api-key": api_key,
            "anthropic-version": "2023-06-01",
        },
    )

    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            payload = json.loads(response.read().decode("utf-8"))
        text = "".join(block.get("text", "") for block in payload.get("content", []))
        text = text.strip().removeprefix("```json").removesuffix("```").strip()
        verdict = json.loads(text)
        return Verdict(bool(verdict.get("pass")), str(verdict.get("reason", "")))
    except Exception as exc:  # noqa: BLE001
        return Verdict(False, f"judge error: {exc}")
