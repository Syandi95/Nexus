# AI Workspace Phase 1 v1.1 Minimal-Evidence Span Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one conservative same-file capability: resolve two unique evidence hints to their smallest complete syntax extents and return one exact contiguous span only when every frozen safety gate passes.

**Architecture:** Keep `Resolve-AIBuildContextTarget` and `Get-AIExactBuildContext` as the only Phase 1 entry points. Calls without source/anchor hints retain the existing single-scope path unchanged. Calls with both hints use one small generic boundary path; workspace selection, freshness, cross-file decisions, user messaging, and Automatic Target Resolution remain outside Phase 1.

**Tech Stack:** Windows PowerShell 5.1, built-in `System.Management.Automation.Language.Parser`, existing XAML boundary extraction, existing script-based test harnesses.

## Global Constraints

- ExactRaw is one ordinal substring of the already-loaded raw source.
- `BridgeChars <= 4000` is the primary hard authority.
- `MaxBridgeGapLines = 80`, calculated as `later.StartLine - earlier.EndLine - 1`, floored at zero.
- `MaxStructuredCoverage = 0.80` is checked only when all four predicates in the frozen design are true.
- No clipping, reconstruction, joined snippets, preview fallback, whole-file fallback, source reread, or source persistence.
- No special-case logic for Conversation/Plan or any named Nexus control.
- No semantic AST framework, control-construction grouping, dependency graph, workspace validator, or compiler-like analysis.
- Do not change Context Budget, Automatic Target Resolution routing, Safe Editing Engine, parser contract, exact-match validator, provider, AIContext, WPF, Apply/Rollback, or Candidate Ranking.
- `TargetKind` is the production and test authority: `SingleScope` or `ContiguousSubscopeBridge`. If retained for frozen-document compatibility, `Kind` is only a read-only alias of `TargetKind`.
- Existing consumers of `Success`, `Ambiguous`, `Confidence`, `TargetKind`, `StartLine`, `EndLine`, `DiscoveryMode`, and `Reason` remain compatible.
- Conversation/Plan is a negative boundary fixture: the separate construction-boundary audit proves the required safe span is `4312`, while even the smaller balanced-statement span is `4010`; both exceed budget `4000`, so the resolver verdict is `NotViable/context_budget_exceeded` and no ExactRaw is produced.

## File Mapping

- Modify `AI/Editing/AttachmentManager.ps1`: add the minimal boundary branch and offset-based exact extraction.
- Create `tests/AIBuildContextPipelineV11.Tests.ps1`: focused RED/GREEN contract and Conversation/Plan negative fixture.
- Do not modify `Modules/AIWorkspace.ps1` or any frozen component.

---

### Task 1: Establish the minimal RED contract

**Files:**
- Create: `tests/AIBuildContextPipelineV11.Tests.ps1`
- Inspect: `tests/AIBuildContextPipeline.Tests.ps1`
- Inspect: `AI/Editing/AttachmentManager.ps1:694-803`

**Interfaces:**
- Consumes: existing `Resolve-AIBuildContextTarget` and `Get-AIExactBuildContext`.
- Produces: an executable test contract for an optional two-hint resolver path; no production changes.

- [ ] **Step 1: Create the script-test harness**

Define the same counter pattern used by Phase 1:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'AI\Editing\AttachmentManager.ps1')
$script:Passed = 0
$script:Failed = 0
function Assert-Phase11 {
    param([string]$Name, [bool]$Condition)
    if ($Condition) { $script:Passed++; Write-Host "PASS $Name" }
    else { $script:Failed++; Write-Host "FAIL $Name" }
}
```

- [ ] **Step 2: Add focused fixtures**

Use small CRLF and LF fixtures that cover:

- unchanged single-scope output;
- two unique complete PowerShell statements in either order;
- two unique balanced XAML elements in either order;
- duplicate source or anchor evidence;
- incomplete/unbalanced evidence;
- `BridgeChars` exactly `4000` and `4001`;
- line gap `80` and `81`;
- `BridgeChars = 4000` PASS and `4001` returns `context_budget_exceeded`;
- `GapLines = 80` PASS and `81` returns `bridge_gap_exceeded`;
- `StructuredCoverage = 0.80` PASS and values greater than `0.80` return `structured_coverage_exceeded`;
- distinct missing, ambiguous, and invalid-boundary failure reasons;
- legacy identity with zero hints, plus `incomplete_bridge_hints` for either one-hint call;
- exact offset and ordinal substring identity;
- Conversation/Plan actual-source negative boundary.

Do not add workspace-root, freshness, cross-file, proposal, session, or UI-message fixtures; those belong to later Automatic Target Resolution tests.

- [ ] **Step 3: Assert the result contract**

```powershell
$target = Resolve-AIBuildContextTarget `
    -Path 'sample.ps1' `
    -UserQuery 'move Source beside Anchor' `
    -RawSource $raw `
    -SourceHint 'Source' `
    -AnchorHint 'Anchor' `
    -MaxChars 4000

Assert-Phase11 'bridge resolves' ($target.Success -and $target.TargetKind -eq 'ContiguousSubscopeBridge')
Assert-Phase11 'offsets are exclusive and exact' (($target.EndOffset - $target.StartOffset) -eq $target.BridgeChars)
$exact = Get-AIExactBuildContext -RawSource $raw -Target $target
Assert-Phase11 'ExactRaw is one ordinal substring' ($exact.Content -ceq $raw.Substring($target.StartOffset, $target.BridgeChars))
```

- [ ] **Step 4: Run RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
```

Expected: FAIL because the optional hint parameters and bridge metadata do not exist. Existing Phase 1 tests must still pass independently.

- [ ] **Step 5: Commit the RED test**

```powershell
git add tests\AIBuildContextPipelineV11.Tests.ps1
git commit -m "test: define minimal evidence span contract"
```

### Task 2: Implement one conservative boundary path

**Files:**
- Modify: `AI/Editing/AttachmentManager.ps1:694-803`
- Test: `tests/AIBuildContextPipelineV11.Tests.ps1`
- Test: `tests/AIBuildContextPipeline.Tests.ps1`

**Interfaces:**
- Extends `Resolve-AIBuildContextTarget` with optional `[string]$SourceHint` and `[string]$AnchorHint` only.
- Produces compatible results plus authoritative `TargetKind`, `StartOffset`, exclusive `EndOffset`, `SourceSubscope`, `AnchorSubscope`, `BridgeChars`, `BridgeLines`, and `SyntaxBalanced`. A `Kind` property, if retained, must equal `TargetKind` and must never drive branching.
- Extends `Get-AIExactBuildContext` to consume validated offsets when present; its legacy line-based branch remains unchanged.

- [ ] **Step 1: Add the smallest internal PowerShell extent selector**

Before parsing, branch only when both hints are non-empty. With neither hint, run the legacy Phase 1 path unchanged. With exactly one hint, return `incomplete_bridge_hints` without guessing the missing evidence.

Use `Parser.ParseInput` once on the supplied `RawSource`. Find ordinal, case-insensitive occurrences of each exact hint, require exactly one occurrence per hint, and select the smallest complete `StatementAst` whose extent contains that occurrence. Return `source_hint_not_found`, `anchor_hint_not_found`, `ambiguous_source_hint`, or `ambiguous_anchor_hint` as applicable. Do not group neighboring statements, infer control ownership, or expand to a function merely to make a fixture pass.

- [ ] **Step 2: Reuse the existing balanced XAML boundary mechanism**

For `.xaml`, resolve each exact hint through existing `x:Name`/balanced element boundaries. Require one complete element for each hint. Do not add XML serialization, an XPath subsystem, or new element relationship rules.

- [ ] **Step 3: Build and validate one span**

Order the two complete extents by offset and retain every character between them. Validate in this order:

1. unique source and anchor;
2. complete balanced extents;
3. `BridgeChars <= MaxChars`;
4. gap `<= 80`;
5. conditional structured coverage `<= 0.80` when all four frozen predicates apply.

On failure return the existing compatible failure shape with exactly one applicable stable reason:

- `source_hint_not_found`;
- `anchor_hint_not_found`;
- `ambiguous_source_hint`;
- `ambiguous_anchor_hint`;
- `invalid_syntax_boundary`;
- `context_budget_exceeded`;
- `bridge_gap_exceeded`;
- `structured_coverage_exceeded`.

Boundary comparisons are inclusive: `4000`, `80`, and `0.80` PASS; only greater values fail. Do not attempt another extent or fallback.

- [ ] **Step 4: Add offset-based exact extraction**

When a successful target contains validated offsets, require:

```powershell
0 -le $Target.StartOffset
$Target.StartOffset -lt $Target.EndOffset
$Target.EndOffset -le $RawSource.Length
($Target.EndOffset - $Target.StartOffset) -eq $Target.BridgeChars
```

Then extract only:

```powershell
$RawSource.Substring($Target.StartOffset, $Target.BridgeChars)
```

If offsets are absent, execute the existing line-based loader branch without behavioral changes.

- [ ] **Step 5: Run GREEN and legacy regression**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipeline.Tests.ps1
```

Expected: both suites PASS; single-scope content and metadata remain identical.

The v1.1 suite must call the resolver without hints before and after the implementation and compare these fields with the frozen expected result: `Success`, `Ambiguous`, `Confidence`, `TargetKind`, `StartLine`, `EndLine`, `DiscoveryMode`, and `Reason`. It must also compare legacy ExactRaw ordinally. Calls with only `SourceHint` or only `AnchorHint` must return `incomplete_bridge_hints` and must not enter the bridge parser path.

- [ ] **Step 6: Commit the minimal implementation**

```powershell
git add AI\Editing\AttachmentManager.ps1 tests\AIBuildContextPipelineV11.Tests.ps1
git commit -m "feat: add minimal evidence span resolver"
```

### Task 3: Verify the frozen boundary and regressions

**Files:**
- Verify only: `Modules/AIWorkspace.ps1`
- Verify: `AI/Editing/AttachmentManager.ps1`
- Test: `tests/AIBuildContextPipelineV11.Tests.ps1`

**Interfaces:**
- Consumes: Phase 1 v1.1 resolver/loader.
- Produces: regression evidence and the final freeze decision; no routing or UI changes.

- [ ] **Step 1: Prove the Conversation/Plan boundary**

Read `Modules/AIWorkspace.ps1` once in the test. Keep two assertions separate:

1. the read-only AST audit calculates the approved complete construction boundary `26586..30898` as `4312` characters;
2. the generic resolver receives exact statement hints, finds a balanced minimal span that still exceeds `4000`, and fails closed.

Assert the resolver result:

```powershell
$target.Success -eq $false
$target.Reason -eq 'context_budget_exceeded'
```

Do not require production code to report `4312`, and do not add construction-grouping logic. Do not call `Get-AIExactBuildContext` after the failure. The fixture proves the contract limit without influencing resolver rules.

- [ ] **Step 2: Parse changed PowerShell files**

```powershell
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path 'AI\Editing\AttachmentManager.ps1'),
    [ref]$null,
    [ref]$errors
) | Out-Null
if ($errors) { $errors | ForEach-Object Message; exit 1 }
```

Expected: no parse errors under Windows PowerShell 5.1.

- [ ] **Step 3: Run the frozen regression gates**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipeline.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV2.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV21.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntelligenceLite.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntegration.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIEditingLiteTests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIFileEditorCompatibility.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIFileEditorIntegrationTests.ps1
git diff --check
```

Expected: every suite and `git diff --check` PASS.

- [ ] **Step 4: Review scope and freeze**

Confirm the implementation changed only `AI/Editing/AttachmentManager.ps1` and `tests/AIBuildContextPipelineV11.Tests.ps1`, contains no named Conversation/Plan special case, performs no additional file read, and leaves all frozen components untouched.

- [ ] **Step 5: Freeze only from fresh evidence**

Do not create a verification artifact. Report the fresh command output and freeze Phase 1 v1.1 only when every required gate passes.

## Deferred to Automatic Target Resolution

The next phase, not this plan, owns:

- deterministic task understanding;
- Project Map candidate retrieval;
- target freshness and root validation;
- single-file versus `RequiresMultiFile` decisions;
- automatic target selection after viability validation;
- mapping `context_budget_exceeded` to a plain-language user explanation;
- proving no proposal/session/diff is created after a non-viable target.

Its intended flow remains:

```text
Project Map
-> Candidate Retrieval
-> Viability Validation
-> Exact Context
-> Proposal
-> Diff
```
