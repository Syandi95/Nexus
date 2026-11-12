# AI Workspace v2 Phase 2 Workspace Intelligence Lite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve and confirm one PowerShell/XAML edit target from a bounded in-memory Project Root index before handing the confirmed file to the frozen Phase 1 Exact Context pipeline.

**Architecture:** A new isolated script owns chunked discovery, metadata-only indexing, ranking, confirmation, and Workspace Health. AI Workspace uses the existing folder button and chat/status surfaces, preserves Project Root across conversations, and never invokes Exact Context before one candidate is confirmed.

**Tech Stack:** Windows PowerShell 5.1, WPF Dispatcher, existing Nexus AI Workspace and Phase 1 exact-context functions.

## Global Constraints

- Phase 1 Build Context Pipeline and all named frozen components remain unchanged.
- Only `.ps1` and `.xaml` source files are indexed.
- Maximum individual file size is 1 MB.
- Total discovery uses injectable source-byte, symbol, and elapsed-time budgets; benchmark determines final values.
- Candidate confidence numbers remain internal; UX shows High, Medium, or Low.
- Every candidate requires user-visible confirmation before Exact Context.
- No WPF additions, persistent cache, watcher, embedding, vector database, dependency graph, background scanner, or multi-file Build.
- Discovery Read may read one `.ps1`/`.xaml` file transiently only for symbol extraction and must discard its content immediately.
- Freshness Read may read only `RelativePath`, `LastWriteTimeUtc`, and `FileLength`; it must never read source content.
- Exact Build Read is allowed only after one candidate is confirmed; only Phase 1 `ExactRaw` may enter Build context or supply proposal text.
- A Dirty workspace blocks Build and requires explicit user approval before discovery rebuild; there is no automatic re-index.
- IDE-style project models, repository body indexes, AST frameworks, relationship graphs, and background monitors belong outside Phase 2.
- `WorkspaceIndexVersion` must match before an in-memory index is reused; mismatch requires discovery rebuild.
- `DiscoveryGenerationId` is per discovery and prevents stale publication after root/rebuild changes; it is not the index schema version.
- Every failure is closed: stop Build, select no alternate file, read no alternate target, create no partial proposal, and perform no automatic fallback.
- `ChunkMilliseconds` is a cooperative target between atomic file operations, not a hard deadline; overruns are recorded and prevent another file starting in the same chunk.
- `BudgetReached` results are partial diagnostics only and cannot be used by any Build/candidate/confirmation/exact-context path; budgets never increase automatically.

---

### Task 1: Establish RED contracts and benchmark harness

**Files:**
- Create: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`
- Create: `tests/AIWorkspaceIntelligenceLite.Benchmark.ps1`

**Interfaces:**
- Consumes: temporary PowerShell/XAML project fixtures and injectable budget objects.
- Produces: failing contracts for discovery lifecycle, budgets, index schema, ranking, confirmation, health, and Phase 1 gate.

- [ ] Write fixtures containing supported symbols, excluded folders, hidden/reparse cases where supported, oversized files, many small files, ambiguous symbols, and a confirmed exact target.
- [ ] Assert root-first discovery, same-root reuse, changed-root rebuild, and stale-generation rejection.
- [ ] Assert matching `WorkspaceIndexVersion` permits reuse and mismatched version requires discovery rebuild.
- [ ] Assert `DiscoveryGenerationId` changes independently of `WorkspaceIndexVersion` and stale generations cannot publish.
- [ ] Assert byte/symbol/time budgets independently stop discovery with `BudgetReached` and no fixed file-count authority.
- [ ] Assert index entries expose only `Path`, `Symbol`, `SymbolType`, `Line`, and `LastWriteTime`.
- [ ] Assert candidate UX uses High/Medium/Low and contains no numerical confidence.
- [ ] Assert one candidate still creates pending confirmation and does not call Exact Context.
- [ ] Assert natural confirmation forms select exactly one immutable candidate.
- [ ] Assert Symbol Index never enters ExactRaw content, request text, proposal, or persistence APIs.
- [ ] Assert Discovery Read content is absent from index, conversation, session, cache, and temporary-file writes.
- [ ] Assert Freshness Read uses filesystem metadata only and source content read count remains zero.
- [ ] Assert a changed freshness snapshot marks Dirty, blocks Build, and does not rebuild until explicit approval.
- [ ] Assert Exact Build Read is impossible before one candidate is confirmed and reads only the confirmed file.
- [ ] Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\AIWorkspaceIntelligenceLite.Tests.ps1` and verify RED because Phase 2 functions do not exist.

### Task 2: Implement metadata extraction and resource budgets

**Files:**
- Create: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`

**Interfaces:**
- Produces `New-AIWorkspaceDiscoveryBudget`, `Get-AIWorkspacePowerShellSymbols`, `Get-AIWorkspaceXamlSymbols`, and metadata-only entries.

- [ ] Add RED tests for each required PowerShell/XAML symbol and exact five-field schema.
- [ ] Implement pure extraction functions accepting `Path`, `Content`, and `LastWriteTime`; return metadata only.
- [ ] Implement an injectable budget object with `MaxSourceBytes`, `MaxSymbols`, `MaxElapsedMilliseconds`, `MaxFileBytes=1048576`, and `ChunkMilliseconds`.
- [ ] Run focused tests; extraction and budget tests must pass while orchestration remains RED.

### Task 3: Implement chunked bounded discovery and Workspace Health

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Benchmark.ps1`

**Interfaces:**
- Produces `Start-AIWorkspaceDiscovery`, `Invoke-AIWorkspaceDiscoveryChunk`, and `Get-AIWorkspaceHealth`.

- [ ] Add RED tests proving excluded directories/reparse points are skipped, cooperative yield stops before another file after an overrun, and root generation prevents stale publication/rescheduling.
- [ ] Implement explicit-stack directory traversal so exclusions occur before descent; admit only `.ps1/.xaml` and files no larger than 1 MB.
- [ ] Implement chunk state containing root, generation, pending paths, index, counters, budget, stopwatch, and status.
- [ ] Treat one file as atomic; check `ChunkMilliseconds` before the next file and after each operation, record `MaxObservedChunkMilliseconds`, `ChunkOverrunCount`, and `LongestFileMilliseconds`, then reschedule with existing Dispatcher `BeginInvoke`.
- [ ] Validate active generation, root, and index version before processing, rescheduling, index publication, and active-health publication.
- [ ] Check source-byte, symbol, and elapsed budgets before each file, after extraction, and before final publication; a breach must finish `BudgetReached`, never `Complete`.
- [ ] Keep BudgetReached metadata diagnostic-only and reject it from candidate/Build paths without automatic budget increase.
- [ ] Publish index/health atomically only for the active generation.
- [ ] Persist only the in-memory freshness manifest fields `RelativePath`, `LastWriteTimeUtc`, and `FileLength`; do not retain Discovery Read content.
- [ ] Run benchmark fixtures and record source bytes, files, symbols, elapsed time, maximum chunk duration, and memory delta; do not freeze production defaults yet.

### Task 4: Implement metadata-only freshness and Dirty gate

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`

**Interfaces:**
- Produces `Test-AIWorkspaceFreshness` returning `Clean`, `Dirty`, or `Unavailable` without source reads.

- [ ] Add RED tests that modify file length, modification time, add/remove a relative path, and instrument source reads at zero.
- [ ] Implement metadata enumeration using only relative path, UTC modification time, and length with the same exclusions as discovery.
- [ ] Compare the current metadata set to the in-memory discovery manifest; on any difference return Dirty without changing the Symbol Index.
- [ ] Require explicit rebuild approval to start a new Discovery Read; never invoke discovery from the freshness function.
- [ ] Run focused tests and verify Dirty blocks candidate use and no content is read.

### Task 5: Implement candidate ranking and mandatory confirmation

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`

**Interfaces:**
- Produces `Find-AIWorkspaceCandidates`, `Format-AIWorkspaceCandidateMessage`, and `Resolve-AIWorkspaceCandidateConfirmation`.

- [ ] Add RED tests for no candidate, one candidate, multiple candidates, tied candidates, and number/file/relative-path/folder/symbol confirmation.
- [ ] Implement deterministic index-only scoring and retain `InternalScore` only inside candidate objects.
- [ ] Map score bands to High/Medium/Low and format candidate messages without numbers representing confidence.
- [ ] Require confirmation for one or many candidates; resolve only an exact single match from the pending snapshot.
- [ ] Run focused tests and confirm no candidate-selection path reads a source file.

### Task 6: Integrate folder lifecycle and Build orchestration

**Files:**
- Modify: `Modules/AIWorkspace.ps1`
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`
- Test: `tests/AIPlanBuildRouting.Tests.ps1`

**Interfaces:**
- Consumes existing `BtnAIAttachFolder`, chat/status functions, and frozen Phase 1 resolver/loader.
- Produces workspace-level root/index/health and conversation-level pending confirmation.

- [ ] Add RED source/behavior tests for dot-source, folder handler state, same-root reuse, health display, Build pending flow, and lifecycle cleanup.
- [ ] Change the folder handler to start/reuse discovery and publish Workspace Health through `LblAIStatus` plus existing system/chat output.
- [ ] Branch Build routing: explicit file attachment retains the frozen Phase 1 path; Project Root without file attachment uses Workspace Intelligence; neither permits multi-file Build.
- [ ] On initial Workspace Build instruction, rank and display candidates, store pending snapshot, clear request mode/target, and return before generation.
- [ ] On confirmation, read only the confirmed target, verify root containment and modification time, call Phase 1 resolver/validator, then Phase 1 Exact Context Loader and construct the existing ExactRaw payload shape.
- [ ] Before confirmation handoff, run metadata-only Freshness Read; Dirty clears confirmation, blocks Build, and asks for explicit rebuild approval.
- [ ] Instrument the Exact Build Read test seam and assert exactly one confirmed file is read while every other workspace file remains unread.
- [ ] Clear pending candidates on root/mode/conversation changes while preserving root/index/health across conversation transitions.
- [ ] Run Phase 2 and routing tests until GREEN.

### Task 7: Full regression and runtime verification

**Files:**
- Verify only unless a fresh in-scope failure proves a defect.

**Interfaces:**
- Produces final evidence, benchmark recommendation, and PASS/BLOCKED status.

- [ ] Parse changed scripts with Windows PowerShell 5.1.
- [ ] Run Phase 2 tests and benchmark; report measured data and proposed budget defaults for separate approval if values remain unfrozen.
- [ ] Run Phase 1 Build Context, AI Workspace, Plan/Build routing, attachment truncation, prompt safety, Core 12/12, Compatibility 5/5, and Integration PASS.
- [ ] Launch Nexus using the verified fully quoted `-File` command.
- [ ] Manually verify same-root reuse, changed-root rebuild, responsive discovery, Workspace Health, no/one/many candidates, natural confirmation, ExactRaw proposal, Apply, Rollback, and fail-closed changed target.
- [ ] Inspect scope: only the new Lite file, AI Workspace, tests, and approved docs may differ; no frozen file may change.
