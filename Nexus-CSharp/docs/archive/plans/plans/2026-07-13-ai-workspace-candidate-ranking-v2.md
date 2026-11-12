# AI Workspace Candidate Ranking v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace symbol-occurrence candidate output with deterministic, bounded, file-level candidates while preserving mandatory confirmation and Phase 1 ExactRaw authority.

**Architecture:** Continue using the in-memory Symbol Index. Filter exact excluded path segments, score occurrence evidence, group by normalized file path, aggregate at most five unique evidence items with diminishing contribution, then apply visibility limits before creating the immutable pending snapshot. No source read is added.

**Tech Stack:** Windows PowerShell 5.1, existing PowerShell test scripts, .NET collections.

## Global Constraints

- Do not add PageRank, NetworkX, FZF, ripgrep, graphs, dependencies, or source reads.
- Candidate presentation is file-level and requires explicit user confirmation.
- ExactRaw Phase 1 remains the sole Build context after confirmation.
- Do not modify AttachmentManager, Phase 1, parser, engine, provider, AIContext, context budget, or WPF layout.

---

### Task 1: Candidate Ranking v2 RED contract

**Files:**
- Create: `tests/AIWorkspaceCandidateRankingV2.Tests.ps1`

**Interfaces:**
- Consumes: `Find-AIWorkspaceCandidates`, `Format-AIWorkspaceCandidateMessage`, `Resolve-AIWorkspaceCandidateConfirmation`.
- Produces: executable assertions for the 28 frozen scenarios.

- [ ] Add fixtures covering file grouping, evidence deduplication, diminishing contribution, generic/test/docs penalties, exact segment exclusions, top-N visibility, immutable pending equivalence, confirmation, and ExactRaw gating.
- [ ] Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV2.Tests.ps1`.
- [ ] Verify RED is caused by occurrence-level output and missing v2 fields/policies.

### Task 2: Exact-segment exclusion and index version

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceCandidateRankingV2.Tests.ps1`

**Interfaces:**
- Produces: case-insensitive exact-segment exclusion shared by discovery, freshness, and candidate defense.

- [ ] Change `WorkspaceIndexVersion` from `workspace-index-v1` to `workspace-index-v2`.
- [ ] Add the frozen excluded segment set and apply it to discovery, freshness, and candidate admission.
- [ ] Verify `Archive/` and `Backup/` are rejected while filenames `ArchiveManager.ps1` and `BackupService.ps1` remain eligible.

### Task 3: File-level aggregation and bounded presentation

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Modify: `Modules/AIWorkspace.ps1` only if pending handoff requires a compatibility adjustment.
- Test: `tests/AIWorkspaceCandidateRankingV2.Tests.ps1`
- Test: `tests/AIWorkspaceIntelligenceLite.Tests.ps1`
- Test: `tests/AIWorkspaceIntegration.Tests.ps1`

**Interfaces:**
- Produces: one immutable candidate per normalized path, `MatchedSymbols` capped at 10, `AdditionalMatchCount`, representative evidence, deterministic score/confidence, and visible list capped at five or three.

- [ ] Score filename once per file, path once per file, and symbol/type per unique `(Symbol, SymbolType, Line)` evidence.
- [ ] Down-weight generic or more-than-five-file identifiers.
- [ ] Aggregate only five best evidence scores using `score / sqrt(position)` after the best item.
- [ ] Apply tests/docs penalty unless explicitly targeted by query.
- [ ] Hide Low when High/Medium exists; otherwise retain at most three Low candidates.
- [ ] Format one line per file and resolve natural symbol confirmation only against displayed `MatchedSymbols`.
- [ ] Run focused GREEN tests, full regression, PowerShell 5.1 parse, `git diff --check`, and the Reset-query runtime harness.

