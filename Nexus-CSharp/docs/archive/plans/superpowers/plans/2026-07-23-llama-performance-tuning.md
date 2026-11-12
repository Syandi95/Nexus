# Llama Performance Tuning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make only `-c 8192`, `-np 1`, and `-tb 8` the Nexus defaults, benchmark them against the recorded baseline, then evaluate `--reasoning-budget 64` without persisting it.

**Architecture:** Retain the existing `App` to `LlamaServerManager` lifecycle and change only startup arguments. Stage 2 runs the existing binary as a temporary benchmark process; it does not change source, UI, payload, transport, or lifecycle architecture.

**Tech Stack:** .NET 8, WPF, xUnit, llama-server b9860, PowerShell runtime sampling.

## Global Constraints

- Stage 1 changes only `-c 8192`, `-np 1`, and `-tb 8`.
- Do not change `-t 4`, `-b 128`, `-ub 64`, `-ngl 0`, `-fa on`, or `--jinja`.
- Stage 2 must not become a source or default configuration change.
- Do not change architecture, UI, Fresh Chat, payload, streaming, or token sanitizer.

---

### Task 1: Lock Stage 1 defaults with RED tests

**Files:**
- Modify: `src/02_Modules/Nexus.AI.Llama.Tests/LlamaServerManagerTests.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs`

**Interfaces:**
- Consumes: `LlamaServerManager.BuildStartArguments(...)`, `App.StartAiServerAsync(...)`
- Produces: exact assertions for `-tb 8`, `-np 1`, and startup context `8192`

- [ ] Change the existing argument assertion to require `-t 4 -tb 8 -b 128 -ub 64 -fa on -np 1` and reject `-tb 4`.
- [ ] Change startup mock assertions from context `32768` to `8192`.
- [ ] Run the two focused projects and verify failures show the three old defaults.

### Task 2: Implement only Stage 1 defaults

**Files:**
- Modify: `src/02_Modules/Nexus.AI.Llama/Services/LlamaServerManager.cs`
- Modify: `src/03_App/Nexus.App/App.xaml.cs`

**Interfaces:**
- Consumes: existing manager startup argument builder
- Produces: startup command containing `-c 8192 -np 1 -tb 8`

- [ ] Change `GetTuningArguments()` from `-tb 4` to `-tb 8` and append `-np 1`; leave every other option byte-for-byte equivalent.
- [ ] Change the App startup context argument from `32768` to `8192`.
- [ ] Run focused tests and verify GREEN.
- [ ] Run the full solution test suite serially and build with zero warnings/errors.

### Task 3: Benchmark Stage 1

**Files:**
- Modify: none

**Interfaces:**
- Consumes: live `/health`, `/props`, `/slots`, process counters, streaming SSE usage/timings
- Produces: comparable TTFT, token/s, CPU, and RAM results

- [ ] Restart Nexus through its normal lifecycle and capture the exact command line.
- [ ] Run three deterministic 128-token streaming requests using the same prompt and 250 ms process sampling used for baseline.
- [ ] Compare median TTFT, warm token/s, total CPU percentage, process working set, and private memory with baseline.
- [ ] Mark Stage 1 positive only if health is stable and there is no material regression in TTFT or throughput.

### Task 4: Run isolated Stage 2 reasoning experiment

**Files:**
- Modify: none

**Interfaces:**
- Consumes: Stage 1 command plus temporary `--reasoning-budget 64`
- Produces: first visible-content time and deterministic Plan/Build responses for comparison

- [ ] Capture Stage 1 no-budget responses for one Plan prompt and one Build prompt at temperature zero.
- [ ] Close Nexus, verify its owned server is stopped, then launch the same command temporarily with only `--reasoning-budget 64` added.
- [ ] Run the same Plan and Build prompts and record raw TTFT, first `delta.content`, throughput, completion length, and response text.
- [ ] Evaluate Plan for ordered reasoning, risk, verification, rollback, and no invented execution; evaluate Build for compilable implementation, validation, tests, and no claimed tool execution.
- [ ] Stop the experimental process and restart Nexus normally.
- [ ] Do not persist the reasoning option unless evidence shows response quality is retained.

### Task 5: Final verification and decision

**Files:**
- Modify: none

**Interfaces:**
- Consumes: test, build, runtime, and benchmark evidence
- Produces: Stage 1 freeze decision and Stage 2 recommendation

- [ ] Verify final live command line contains Stage 1 defaults and does not contain `--reasoning-budget`.
- [ ] Verify port 8080 and `/health`.
- [ ] Report complete before/after tables and any limitations without implementing Stage 3.
