# AgentForge

A tool-using AI agent framework in C# with an embedded Python layer, built to show what production agentic code looks like: a small, auditable loop, typed tools with schemas, a human approval gate for anything with side effects, hard budgets, full tracing, and an evaluation harness that scores the agent's behaviour rather than trusting it.

No SDKs. The Anthropic Messages API is spoken directly over HTTP with `System.Text.Json`, because the wire format is small and worth understanding, and because a framework should not inherit its provider's release cadence.

## Architecture

```
  task ──► Agent loop ──► ILlmClient ──► Anthropic Messages API
             │  ▲
             │  │ tool results
             ▼  │
        ToolRegistry ──► IApprovalPolicy (human gate) ──► ITool.ExecuteAsync
             │
   ┌─────────┼──────────────┬──────────────┬─────────────────┐
 calculate  read_file    write_file     run_python        analyze_csv
 (parser)   (sandboxed)  (approval)     (approval,        (reviewed .py
                                          timeout)          as a typed tool)
```

**The loop** (`Agent.cs`) is about eighty lines and reads top to bottom: ask the model, run the tools it asked for, feed the results back, repeat until it answers without tool calls. Every exit path is explicit: completed, step limit, token budget, cancelled, error.

**Tools** implement one interface and declare a JSON schema for their input, built with a tiny typo-proof helper. A `RequiresApproval` flag marks anything with side effects.

**Approval** is a policy the loop consults before every side-effecting tool. The CLI shows the operator exactly what the model wants to run and waits for `y`. A denial is reported back to the model as a tool error with instructions not to retry, so the model adapts instead of looping.

**Budgets** are hard. `MaxSteps` caps model round-trips; `TokenBudget` caps total spend. Neither can be talked around by the model.

**Tracing** emits one JSON line per event (steps, tool calls with inputs, results with timings, final outcome). Traces are the raw material for evaluation, debugging and cost analysis, and the Python harness reads them directly.

## Where Python fits

Python is embedded in two deliberately different ways, because they answer different trust questions.

**`run_python`: arbitrary code, always approved by a human.** The model writes a script; it runs in the working directory with a wall-clock timeout and an output cap, and the operator sees the code before it executes. Maximum capability, maximum oversight.

**`analyze_csv`: reviewed code, no approval needed.** A specific script in `python/tools/` is exposed as a first-class tool with its own schema. Input arrives as JSON on stdin, output leaves as JSON on stdout, the script enforces its own sandbox, and because the code is fixed and reviewed it runs without a prompt. This is the pattern for turning an existing Python data toolbox into safe agent capabilities without giving the model a shell.

## Evaluation harness

`python/evals/run_evals.py` runs scenarios through the CLI and scores them:

| Check | What it proves |
|---|---|
| `contains`, `regex` | The answer is correct |
| `max_steps` | The agent is efficient, not just eventually right |
| `tool_used` | It reached for the calculator instead of guessing at arithmetic |
| `not_tool_used` | It did not run code when asked to do something destructive |
| `llm_judge` | Rubric-based grading for things regexes cannot express, via an optional model judge |

Every run produces a Markdown report and a JSON results file, and the process exit code is non-zero on any failure, so the harness drops straight into CI as a regression gate for prompt and model changes. The harness is standard library only.

## Running it

Requires .NET 8, Python 3.10+, and `ANTHROPIC_API_KEY` in the environment.

```bash
dotnet test

# Interactive: side-effect tools will ask for approval
dotnet run --project src/AgentForge.Cli -- \
  --task "Summarise samples/sales.csv and tell me which region had the highest revenue" \
  --trace traces/run.jsonl

# Evaluations
cd python/evals
python run_evals.py
python run_evals.py --only arithmetic-exact
```

Set `AGENTFORGE_MODEL` (or pass `--model`) to use a different model id; the default is a current Sonnet-class model.

Sample interactive run:

```
[step 1] ToolUse (412 in / 63 out)
  -> analyze_csv {"path":"samples/sales.csv"}
  <- analyze_csv (184ms) {"rows": 9, "columns": ["region", "month", "units", "revenue"], ...
[step 2] ToolUse (781 in / 110 out)
  -> run_python {"code":"import csv\nfrom collections import defaultdict\n..."}

  The agent wants to run 'run_python' with input:
    {"code": "import csv ..."}
  Allow? [y/N] y
  <- run_python (96ms) West: 72000.0
[step 3] EndTurn (1104 in / 58 out)
[done] Completed in 3 step(s), 2528 tokens

samples/sales.csv has 9 rows across 3 regions with total revenue of 150,000. West had the highest revenue at 72,000.
```

## Testing without a model

The agent loop is tested with a scripted `FakeLlmClient`, so the tests are deterministic, instant and free. They pin down the behaviours that matter in production: tool results are fed back to the model with the right ids, unknown tools and tool exceptions become error results rather than crashes, denied approvals block the side effect and inform the model, and the step limit ends a runaway loop. The calculator parser and the file sandbox have their own tests, including path traversal.

## Project structure

```
src/
  AgentForge.Core/                 Agent loop, models, tool contract, approval, tracing
  AgentForge.Providers.Anthropic/  ILlmClient over the Messages API (raw HTTP, retries)
  AgentForge.Tools/                Calculator, sandboxed files, run_python, script tools
  AgentForge.Cli/                  Interactive runner with console approval
tests/
  AgentForge.Tests/                Deterministic loop and tool tests
python/
  tools/analyze_csv.py             Reviewed script exposed as a typed tool
  evals/run_evals.py               Scenario runner and scorer
  evals/judge.py                   Optional LLM-as-judge
  evals/scenarios.json             The eval suite
samples/sales.csv                  Data for the examples and evals
```

## Extending it

The two seams are `ILlmClient` (add a provider) and `ITool` (add a capability). Natural next steps: an MCP server adapter so tools can be shared with other agent hosts, conversation memory across runs, parallel tool execution when the model issues independent calls, and streaming output in the CLI.

## License

MIT
