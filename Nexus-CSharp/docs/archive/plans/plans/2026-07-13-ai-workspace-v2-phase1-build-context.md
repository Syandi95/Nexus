# AI Workspace v2 Phase 1 Build Context Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure Build Mode generates proposals only from validated exact raw source context while Plan Mode keeps the existing Adaptive Context behavior.

**Architecture:** Attachment processing performs one raw read and retains it transiently for discovery. Two internal functions in `AttachmentManager.ps1` resolve and validate a target, then extract an exact substring without rereading or normalizing source. `AIWorkspace.ps1` fails closed unless a Build payload is `ExactRaw`.

**Tech Stack:** Windows PowerShell 5.1, existing PowerShell/WPF Nexus modules, script-based regression tests.

## Global Constraints

- Maximum one `Get-Content -Raw` per file per request.
- Plan Mode and the Adaptive Context Engine remain unchanged.
- Build never falls back from failed exact context to Adaptive Preview.
- Context budget is frozen; oversized exact scopes fail closed.
- Do not change `AI/EditingLite/*`, `AI/AIFileEditor.ps1`, `AI/AI.psm1`, `AI/AIContext.ps1`, provider, parser contract, existing validator, public editing API, WPF, or `ManajemenFile.ps1`.
- Do not implement Phase 2 Workspace Intelligence.

---

### Task 1: Establish the Phase 1 RED contract

**Files:**
- Create: `tests/AIBuildContextPipeline.Tests.ps1`
- Inspect: `AI/Editing/AttachmentManager.ps1`
- Inspect: `Modules/AIWorkspace.ps1`

**Interfaces:**
- Consumes: existing `New-AIEditingContextPayload`, `Get-SmartAttachmentPreview`, and `Send-AIChatMessage` source contracts.
- Produces: failing behavioral assertions for `Resolve-AIBuildContextTarget`, `Get-AIExactBuildContext`, and Build payload gating.

- [ ] **Step 1: Write a PowerShell 5.1 test harness**

Create temporary small/large PowerShell and XAML fixtures with explicit CRLF and LF variants. Dot-source `AttachmentManager.ps1`, initialize its attachment state through existing functions, and add assertions covering:

```powershell
Assert-Phase1 'Plan remains AdaptivePreview' ($plan.Files[0].ContextMode -eq 'AdaptivePreview')
Assert-Phase1 'Build small file is ExactRaw' ($build.Files[0].ContextMode -eq 'ExactRaw')
Assert-Phase1 'exact content preserves source' ($build.Files[0].Content -ceq $rawSource)
Assert-Phase1 'structural map is not final Build context' ($build.Files[0].Content -notmatch '^=== Structure Map:')
Assert-Phase1 'missing target fails closed' (-not $missing.Success)
Assert-Phase1 'ambiguous target fails closed' (-not $ambiguous.Success)
```

Add source assertions proving the loader has no `Get-Content` call and Build checks `ContextMode -eq 'ExactRaw'` before generation.

- [ ] **Step 2: Run the new test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\AIBuildContextPipeline.Tests.ps1
```

Expected: FAIL because the resolver, exact loader, `ContextMode`, and Build fail-closed gate do not exist.

- [ ] **Step 3: Record the exact failing assertions**

Capture the Passed/Failed count and confirm failures represent missing Phase 1 behavior, not fixture syntax or import errors.

### Task 2: Add one-read metadata and retain Plan behavior

**Files:**
- Modify: `AI/Editing/AttachmentManager.ps1:694-945`
- Test: `tests/AIBuildContextPipeline.Tests.ps1`

**Interfaces:**
- Consumes: attachment path, user instruction, existing limits.
- Produces: preview objects with transient `RawSource` and `ContextMode = 'AdaptivePreview'` while preserving all existing fields.

- [ ] **Step 1: Add failing one-read and Plan assertions**

Assert that one payload creation invokes the raw read once per file, Plan content/metadata remain Adaptive, and existing truncation semantics remain unchanged.

- [ ] **Step 2: Run the focused test and verify RED**

Expected: FAIL on missing `RawSource`/`ContextMode` metadata.

- [ ] **Step 3: Implement the minimal one-read payload change**

Change `Get-SmartAttachmentPreview` to accept an optional already-loaded raw source or centralize the single raw read in `New-AIEditingContextPayload`, then pass that string into discovery. Add:

```powershell
ContextMode  = 'AdaptivePreview'
RawSource    = $text
DiscoveryMode = $previewMode
TargetKind   = $null
ContextLines = $null
Confidence   = 0.0
```

Do not alter Adaptive selection, truncation, or budget algorithms.

- [ ] **Step 4: Run focused and truncation tests**

Run the Phase 1 test and `tests/AttachmentTruncationMetadata.Tests.ps1`. Expected: new one-read/Plan assertions PASS; resolver assertions remain RED.

### Task 3: Implement and validate the Target Resolver

**Files:**
- Modify: `AI/Editing/AttachmentManager.ps1`
- Test: `tests/AIBuildContextPipeline.Tests.ps1`

**Interfaces:**
- Consumes: `-Path`, `-UserQuery`, `-RawSource`, and Adaptive discovery metadata.
- Produces: `Resolve-AIBuildContextTarget` result with `Success`, `Ambiguous`, `Confidence`, `TargetKind`, `StartLine`, `EndLine`, `DiscoveryMode`, and `Reason`.

- [ ] **Step 1: Add failing resolver tests**

Cover a unique PowerShell function, duplicate identifier ambiguity, missing identifier, small entire-file target, large-file discovery, and named XAML control with its balanced element scope.

- [ ] **Step 2: Run tests and verify RED**

Expected: resolver-specific failures because the function is undefined.

- [ ] **Step 3: Implement minimal resolution and validation**

Reuse `Get-FileStructureMap` and existing extracted keywords. Select only one sufficiently confident candidate. Validate target existence, ambiguity, confidence threshold, ordered in-range lines, non-empty scope, and frozen budget fit. For XAML, maintain an opening/closing tag stack over raw line boundaries and resolve the smallest balanced named element containing the match. Return failure objects instead of throwing for ordinary unresolved targets.

- [ ] **Step 4: Run resolver tests**

Expected: unique function and XAML cases PASS; missing and ambiguous cases fail closed.

### Task 4: Implement the Exact Context Loader

**Files:**
- Modify: `AI/Editing/AttachmentManager.ps1`
- Test: `tests/AIBuildContextPipeline.Tests.ps1`

**Interfaces:**
- Consumes: `-RawSource` and a validated resolver target.
- Produces: `Get-AIExactBuildContext` result containing exact `Content`, `ContextLines`, `TargetKind`, and `Confidence` with no filesystem read.

- [ ] **Step 1: Add failing exactness tests**

Test complete function scope, balanced XAML scope, CRLF, LF, quotes, indentation, blank lines, trailing whitespace, and exact ordinal substring membership.

- [ ] **Step 2: Run tests and verify RED**

Expected: exact loader assertions FAIL.

- [ ] **Step 3: Implement offset-based extraction**

Build a line-boundary table from regex matches over the existing raw string. Convert validated inclusive line bounds into start/end character offsets and call `Substring`. Do not reconstruct content and do not call `Get-Content`.

- [ ] **Step 4: Run exactness tests**

Expected: all exactness assertions PASS byte-for-character, including newline style.

### Task 5: Route Build through ExactRaw and fail closed

**Files:**
- Modify: `AI/Editing/AttachmentManager.ps1`
- Modify: `Modules/AIWorkspace.ps1:1001-1069`
- Test: `tests/AIBuildContextPipeline.Tests.ps1`
- Test: `tests/AIWorkspaceIntegration.Tests.ps1`
- Test: `tests/AIPlanBuildRouting.Tests.ps1`

**Interfaces:**
- Consumes: Plan/Build request snapshot and one attachment payload.
- Produces: Plan `AdaptivePreview` payload or Build `ExactRaw` payload; unresolved Build stops before `Invoke-AIGenerate`.

- [ ] **Step 1: Add failing routing tests**

Assert Plan never calls resolver/loader, valid Build reaches generation with `ExactRaw`, and missing/ambiguous/oversized/structural-only targets do not call generation, parser, or create `$script:AIPendingEdit`.

- [ ] **Step 2: Run tests and verify RED**

Expected: Build fail-closed assertions FAIL on the current common payload path.

- [ ] **Step 3: Implement minimal Build payload conversion**

Add a mode-aware internal payload entry point in `AttachmentManager.ps1` or pass `-BuildMode` to the existing payload function. For Build, run resolver validation and loader using `RawSource`, replace `Content`, and set:

```powershell
ContextMode   = 'ExactRaw'
DiscoveryMode = $target.DiscoveryMode
TargetKind    = $target.TargetKind
ContextLines  = "$($target.StartLine)-$($target.EndLine)"
Confidence    = $target.Confidence
Truncated     = $false
IncludedChars = $exact.Content.Length
```

In `Send-AIChatMessage`, stop before generation unless the single Build file has `ContextMode = 'ExactRaw'`. Display a normal system explanation from the resolver reason and clear request target/snapshot without touching last applied rollback state.

- [ ] **Step 4: Run focused routing and workspace tests**

Expected: Phase 1, Plan/Build routing, and AI Workspace tests PASS.

### Task 6: Full regression and runtime verification

**Files:**
- Verify only; no production changes unless a fresh failure proves an in-scope defect.

**Interfaces:**
- Consumes: completed Phase 1 implementation.
- Produces: gate evidence and PASS/BLOCKED status.

- [ ] **Step 1: Parse changed PowerShell files with Windows PowerShell 5.1**

Expected: no parser errors.

- [ ] **Step 2: Run all automated gates**

Run Phase 1 tests, Plan/Build routing, AI Workspace, attachment truncation, Build prompt safety, core, compatibility, and integration suites. Expected:

```text
Core Passed=12 Failed=0
Compatibility Passed=5 Failed=0
Integration PASS
```

- [ ] **Step 3: Run focused runtime smoke**

Verify Fast Chat Plan remains normal, Plan attachment still supplies context, valid Build exact context can produce one proposal, Apply/Rollback remain operational, and unresolved Build produces an explanation without generation or pending session.

- [ ] **Step 4: Inspect final scope**

Confirm only allowed Phase 1 files and tests changed, no context budget constants changed, no forbidden file changed, and no Phase 2 component was added.

- [ ] **Step 5: Report evidence**

Provide changed files, RED/GREEN results, metadata behavior, one-read proof, regression outputs, runtime result, and final PASS/BLOCKED status.

