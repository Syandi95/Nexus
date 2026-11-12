# AI Workspace Candidate Ranking v2.1 Intent-Aware Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic post-aggregation intent compatibility that reranks existing file candidates for contextual UI/theme/settings queries.

**Architecture:** Keep Candidate Ranking v2 base matching and file aggregation unchanged. Classify a small set of query intents, compare them only with metadata already grouped for candidates whose base aggregate is positive, then multiply that aggregate before existing confidence, sorting, visibility, and confirmation stages.

**Tech Stack:** Windows PowerShell 5.1, .NET regex/collections, existing script-based test harnesses.

## Global Constraints

- Intent only reranks candidates with base score greater than zero and cannot create candidates.
- Candidate remains one file; no extractor or index schema change.
- No source read, dependency, embedding, graph, fuzzy framework, or Phase 3 feature.
- Direct-target protection requires a complete filename, relative path, or symbol at whole phrase/token boundaries.
- Plain `Reset` is not Settings evidence.
- Reasons must derive from actual symbol/path metadata.
- Multipliers start at `1.60`, `1.25`, and `0.55`; fixture evidence may calibrate them, and `1.00` is neutral for no intent/direct target.
- Confirmation, immutable pending snapshot, bounded output, and ExactRaw Phase 1 remain unchanged.
- Do not modify AttachmentManager, ContextAssembler, AIContext, AI.psm1, parser, Safe Editing Engine, provider, context budget, WPF, or ManajemenFile.

---

### Task 1: Intent-aware RED contract

**Files:**
- Create: `tests/AIWorkspaceCandidateRankingV21.Tests.ps1`

**Interfaces:**
- Consumes: `Find-AIWorkspaceCandidates`, `Resolve-AIWorkspaceCandidateConfirmation`, `Invoke-AIWorkspaceConfirmedBuildContext`.
- Produces: failing assertions for `Get-AIWorkspaceQueryIntentProfile` and intent-aware ranking behavior.

- [ ] Add synthetic entries for `BtnResetSettings`, `Foreground`, `ThemeManager`, Reset-only functions, XAML text/content, background controls, direct function targeting, and no-intent baselines.
- [ ] Assert bilingual trigger equivalence, intent non-creation, actual-metadata reasons, 5/3 bounds, visible/pending equality, and zero Exact Context reads before confirmation.
- [ ] Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV21.Tests.ps1`.
- [ ] Verify RED reports missing intent classification/reranking, not fixture or parse errors.

### Task 2: Minimal post-aggregation implementation

**Files:**
- Modify: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Test: `tests/AIWorkspaceCandidateRankingV21.Tests.ps1`

**Interfaces:**
- Produces: `Get-AIWorkspaceQueryIntentProfile -QueryTokens <string[]>` returning active intent names.
- Produces: `Get-AIWorkspaceIntentCompatibility -Instruction <string> -ActiveIntents <string[]> -Group <object>` returning `Multiplier`, `MatchedIntents`, `DirectTarget`, and metadata-backed `Reasons`.

- [ ] Implement exact-token query classification for the four frozen categories.
- [ ] Implement whole-target protection and metadata-only category evidence; exclude plain `Reset` from Settings evidence.
- [ ] In `Find-AIWorkspaceCandidates`, reject non-positive base aggregate, apply compatibility multiplier, add at most three actual-evidence reason fragments, then continue existing round/confidence/sort flow.
- [ ] Run the v2.1 test and verify GREEN.
- [ ] Run Candidate Ranking v2 and Workspace Intelligence Lite tests to verify the integration preserves v2 behavior.

### Task 3: Review, regression, and runtime evidence

**Files:**
- Review: `AI/Editing/WorkspaceIntelligenceLite.ps1`
- Review: `tests/AIWorkspaceCandidateRankingV21.Tests.ps1`

**Interfaces:**
- Consumes: the completed v2.1 implementation.
- Produces: spec/quality findings, regression evidence, and actual repository ranking evidence.

- [ ] Review every frozen rule against the diff; verify no filesystem read exists in intent helpers and no out-of-scope file changed.
- [ ] Evaluate review findings against repository contracts before correcting any Critical/Important issue; rerun focused tests after each correction.
- [ ] Run PowerShell 5.1 parse, Candidate Ranking v2/v2.1, Phase 2, Phase 1, routing, AI Workspace, truncation, prompt safety, Core, Compatibility, Integration, and `git diff --check` gates.
- [ ] Build the current in-memory workspace index once and run `ubah warna text tombol Reset`; verify Theme/Settings evidence outranks Reset-only functions, output is bounded, pending matches visible candidates, confirmation remains mandatory, and no Exact Context read occurs before confirmation.
