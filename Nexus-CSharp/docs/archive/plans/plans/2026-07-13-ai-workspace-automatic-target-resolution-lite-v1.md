# AI Workspace Automatic Target Resolution Lite v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace normal technical file confirmation with deterministic Top-3 viability validation and automatic selection of exactly one safe target.

**Architecture:** Add one pure PowerShell component for hints, sequential validation, and decision policy. Keep Candidate Ranking and Phase 1 frozen; `AIWorkspace.ps1` only orchestrates state and performs the final selected-file Exact Build Read. Validation raw source is transient and never enters state or model context.

**Tech Stack:** Windows PowerShell 5.1, existing Workspace Symbol Index/Candidate Ranking, frozen Phase 1 v1.1 resolver/loader, script-based tests.

## Global Constraints

- Create only `AI/Editing/AutomaticTargetResolution.ps1` as a production component.
- Modify only `Modules/AIWorkspace.ps1` for integration.
- Do not modify `AI/Editing/AttachmentManager.ps1`, `AI/Editing/WorkspaceIntelligenceLite.ps1`, Candidate Ranking, EditingLite, AIFileEditor, AI.psm1, AIContext, provider, parser, context budget, WPF layout, or Apply/Rollback.
- Candidate retrieval calls existing `Find-AIWorkspaceCandidates` with the original instruction and retains at most five returned files.
- Validate at most three candidates, sequentially; candidate four and five receive zero reads.
- Validation result and pending state contain metadata only.
- Only the final selected file receives Exact Build Read and can become model context.
- No additional LLM call, dependency, scan, watcher, cache, embedding, graph, or background worker.
- Conversation/Plan remains `NoTarget` from `context_budget_exceeded`; never special-case it to pass.

---

### Task 1: Deterministic task hints

**Files:**
- Create: `AI/Editing/AutomaticTargetResolution.ps1`
- Create: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`

**Interfaces:**
- Produces `Get-AIAutomaticTargetHints -Instruction <string>`.
- Returns `Success`, `Reason`, `Action`, `SourceHint`, and `AnchorHint`.

- [ ] **Step 1: Write the RED hint tests**

Create the standard Nexus script-test harness and assert:

```powershell
$moveId = Get-AIAutomaticTargetHints -Instruction 'Pindahkan Conversation ke sebelah Plan'
Assert-ATR 'Indonesian move' ($moveId.Success -and $moveId.Action -eq 'Move' -and $moveId.SourceHint -eq 'Conversation' -and $moveId.AnchorHint -eq 'Plan')

$moveEn = Get-AIAutomaticTargetHints -Instruction 'Move Conversation beside Plan'
Assert-ATR 'English move equivalent' ($moveEn.Action -eq $moveId.Action -and $moveEn.SourceHint -eq $moveId.SourceHint -and $moveEn.AnchorHint -eq $moveId.AnchorHint)

$theme = Get-AIAutomaticTargetHints -Instruction 'Ganti warna text Reset to Default saat light mode'
Assert-ATR 'modify extracts source and context anchor' ($theme.Success -and $theme.Action -eq 'Modify' -and $theme.SourceHint -and $theme.AnchorHint)

$direct = Get-AIAutomaticTargetHints -Instruction 'Perbaiki Reset-PdfSelection'
Assert-ATR 'direct function is a valid single-scope hint' ($direct.Success -and $direct.Action -eq 'Modify' -and $direct.SourceHint -eq 'Reset-PdfSelection' -and -not $direct.AnchorHint)

$unclear = Get-AIAutomaticTargetHints -Instruction 'Tolong kerjakan ini'
Assert-ATR 'unclear request fails closed' (-not $unclear.Success -and $unclear.Reason -eq 'insufficient_hints')
```

- [ ] **Step 2: Run RED**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
```

Expected: FAIL because `Get-AIAutomaticTargetHints` does not exist; the harness itself parses.

- [ ] **Step 3: Implement the smallest deterministic extractor**

Use one ordered verb table and one connector split. Strip only leading UI modifier words needed by fixtures (`tombol`, `button`, `warna`, `color`, `text`, `teks`) while retaining multiword visible labels such as `Reset to Default`. Rename replacement text is not an anchor. Return `insufficient_hints` rather than guessing.

- [ ] **Step 4: Run Task 1 GREEN**

Run the same test file. Expected: all hint assertions PASS and no filesystem read appears in `Get-AIAutomaticTargetHints`.

- [ ] **Step 5: Commit Task 1**

```powershell
git add AI\Editing\AutomaticTargetResolution.ps1 tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
git commit -m "feat: add automatic target hints"
```

### Task 2: Bounded sequential validation and automatic decision

**Files:**
- Modify: `AI/Editing/AutomaticTargetResolution.ps1`
- Modify: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`

**Interfaces:**
- Produces `Invoke-AIAutomaticTargetValidation -Root -Instruction -TaskHints -Candidates -Manifest [-GetFileMetadata] [-ReadSource] [-ResolveTarget]`.
- Produces `Resolve-AIAutomaticTarget -Instruction -TaskHints -Candidates -ViabilityResults -Metrics`.
- Verdicts: `Viable`, `Ambiguous`, `NotViable`, `Stale`.
- Decisions: `AutoSelected`, `ClarificationRequired`, `NoTarget`, `Stale`.

- [ ] **Step 1: Add RED retrieval and validation assertions**

Use five synthetic candidate files and callbacks that append paths to `List[string]` objects. Assert:

```powershell
Assert-ATR 'retrieval is bounded to Top 5' (@($ranked | Select-Object -First 5).Count -le 5)
Assert-ATR 'retrieval performs zero source reads' ($readPaths.Count -eq 0)

$validation = Invoke-AIAutomaticTargetValidation -Root $root -Instruction $instruction -TaskHints $hints -Candidates $ranked -Manifest $manifest -GetFileMetadata $metadataCallback -ReadSource $readCallback -ResolveTarget $resolverCallback
Assert-ATR 'only Top 3 are validated' ($validation.ValidatedCount -le 3 -and $readPaths.Count -le 3)
Assert-ATR 'candidate four and five are unread' (-not $readPaths.Contains($ranked[3].Path) -and -not $readPaths.Contains($ranked[4].Path))
Assert-ATR 'validation retains no raw source' (-not ($validation | ConvertTo-Json -Depth 8).Contains($rawSentinel))
```

Add fixtures for first NotViable/second Viable, two viable, Phase 1 ambiguity, zero viable, stale metadata, and `context_budget_exceeded`. Confirm metrics expose candidate count, validated count, elapsed milliseconds, and read bytes without raw content.

- [ ] **Step 2: Run RED**

Expected: hint tests remain PASS; validation/decision assertions FAIL because both functions are missing.

- [ ] **Step 3: Implement sequential validation**

Normalize Root once. Copy at most five candidate metadata records and iterate only `Select-Object -First 3`. For each file, compare manifest `RelativePath`, `LastWriteTimeUtc`, and `FileLength`, reject reparse/outside-root targets, read once, call Phase 1, create a metadata-only result, and set the local raw variable to `$null` in `finally`.

For bridge hints call:

```powershell
Resolve-AIBuildContextTarget -Path $path -UserQuery $Instruction -RawSource $raw -DiscoveryMode 'automatic-validation' -MaxChars 4000 -SourceHint $TaskHints.SourceHint -AnchorHint $TaskHints.AnchorHint
```

For a valid single-scope hint call the frozen legacy path without either optional hint:

```powershell
Resolve-AIBuildContextTarget -Path $path -UserQuery $TaskHints.SourceHint -RawSource $raw -DiscoveryMode 'automatic-validation' -MaxChars 4000
```

- [ ] **Step 4: Implement decision precedence**

Return `Stale` on any stale result; `ClarificationRequired` on any ambiguous result or more than one viable result; `AutoSelected` on exactly one viable result with no ambiguity; otherwise `NoTarget`. Copy the selected Phase 1 target metadata but no raw source. Map messages to functional language and never emit score, offset, symbol type, or a filename-selection instruction.

- [ ] **Step 5: Run Task 2 GREEN**

Expected: tests 1–20 PASS, Top-3 read order matches ranking order, ranking score never selects a NotViable file, and `context_budget_exceeded` produces `NoTarget`.

- [ ] **Step 6: Commit Task 2**

```powershell
git add AI\Editing\AutomaticTargetResolution.ps1 tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
git commit -m "feat: validate and resolve automatic targets"
```

### Task 3: Replace normal technical confirmation routing

**Files:**
- Modify: `Modules/AIWorkspace.ps1:25-27,452-555,1099-1151`
- Modify: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`
- Verify only: `AI/Editing/WorkspaceIntelligenceLite.ps1`

**Interfaces:**
- Adds internal `Clear-AIAutomaticTargetState` and `New-AIAutomaticClarificationState`.
- Adds internal `Get-AIAutomaticSelectedBuildContext -Root -Decision -FreshnessSnapshot [-GetFileMetadata] [-ReadSource] [-LoadExactContext]`.
- `Get-AIWorkspaceBuildRoute` continues returning `Continue`, `Message`, `ContextPayload`, `TargetPath`, and `OriginalInstruction` for `Send-AIChatMessage` compatibility.

- [ ] **Step 1: Add routing RED assertions**

Load/extract the routing functions with injected retrieval, metadata, read, resolver, and exact-loader counters. Assert:

- AutoSelected never formats or requests filename confirmation;
- only the selected final path receives one Exact Build Read and one exact-loader call;
- clarification stores metadata only and generation/proposal/session/diff counters remain zero;
- NoTarget clears state and all downstream counters remain zero;
- Stale performs zero Exact Build Reads;
- a natural clarification reruns hints, retrieval, and validation using the original instruction plus the functional reply;
- selected ExactRaw produces the existing one-file Build context payload;
- `Send-AIChatMessage` still requires successful one-file `ExactRaw` before generation;
- existing diff-before-Apply and Apply/Cancel/Rollback source contracts remain present.

- [ ] **Step 2: Run RED**

Expected: Task 1–2 tests remain PASS; routing assertions FAIL because AIWorkspace still populates `AIWorkspacePendingCandidates` and formats technical candidate confirmation.

- [ ] **Step 3: Dot-source and add minimal state lifecycle**

Dot-source `AutomaticTargetResolution.ps1` immediately after `WorkspaceIntelligenceLite.ps1`. Add one `$script:AIAutomaticTargetState` metadata-only variable. Clear it on mode change, new/open/delete conversation, Project Root change/rebuild, Dirty transition, NoTarget, Stale, and successful selection. Do not clear last-applied rollback state.

- [ ] **Step 4: Replace the normal route**

In `Get-AIWorkspaceBuildRoute`, retain existing workspace/discovery/freshness gates and existing `Find-AIWorkspaceCandidates`. Replace pending technical confirmation with hints -> Top-3 validation -> decision. Keep the old pending-candidate functions defined but do not populate them in the normal route.

On clarification, return `Continue=$false` and the functional message. On the next input, combine it with `OriginalInstruction`, then rerun the complete automatic path rather than mapping the reply to a filename.

- [ ] **Step 5: Implement final selected-file handoff**

`Get-AIAutomaticSelectedBuildContext` rechecks selected-file timestamp and length, reads only that path, calls frozen `Get-AIExactBuildContext -RawSource $raw -Target $Decision.SelectedTarget`, constructs the existing one-file `ExactRaw` payload, and clears `$raw` in `finally`. Any changed metadata returns Stale and no payload.

- [ ] **Step 6: Run Task 3 GREEN**

Expected: tests 1–31 PASS; normal workspace Build never emits `Format-AIWorkspaceCandidateMessage`; AutoSelected reaches the existing generation path; all fail-closed routes stop before proposal/session/diff.

- [ ] **Step 7: Commit Task 3**

```powershell
git add Modules\AIWorkspace.ps1 tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
git commit -m "feat: route Build through automatic target resolution"
```

### Task 4: Review, regression, runtime acceptance, and freeze

**Files:**
- Optionally create: `tests/AIWorkspaceAutomaticTargetResolution.Runtime.ps1`
- Verify only: all frozen files and existing regression suites.

**Interfaces:**
- Produces review evidence, regression output, runtime verdict, and freeze status.

- [ ] **Step 1: Request spec-compliance review**

Use `superpowers:requesting-code-review`. Reject any suggestion that changes Phase 1, Candidate Ranking, context budget, WPF, provider, parser, or editing engine. Fix all Critical/Important findings within the two production files and rerun affected tests.

- [ ] **Step 2: Run PowerShell 5.1 parse and static safety checks**

Parse the new component, AIWorkspace, and test. Assert the new component contains no provider, prompt, session, diff, write, cache, background, or persistence calls. Assert the validation result serialized to JSON contains no raw sentinel.

- [ ] **Step 3: Run all regression gates**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipeline.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV2.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV21.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntelligenceLite.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIPlanBuildRouting.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntegration.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AttachmentTruncationMetadata.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIContextBuildSafety.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIEditingLiteTests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIFileEditorCompatibility.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIFileEditorIntegrationTests.ps1
git diff --check
```

Expected: automatic tests and every frozen regression PASS; Phase 1 v1.1 remains `34/34`, legacy Phase 1 `14/14`, Core `12/12`, Compatibility `5/5`, Integration `PASS`.

- [ ] **Step 4: Run repository/runtime acceptance**

Exercise the four approved cases. Record candidates retrieved, paths validated, bytes/time, decision, final Exact Build path, and whether generation/diff occurred. Conversation/Plan must remain `NoTarget/context_budget_exceeded`. If interactive WPF control is unavailable, report `AUTOMATED VERIFIED / MANUAL RUNTIME PENDING` rather than claiming runtime PASS.

- [ ] **Step 5: Verify frozen scope and freeze**

Compare frozen-file hashes/status against the pre-task baseline. The only production changes may be `AI/Editing/AutomaticTargetResolution.ps1` and `Modules/AIWorkspace.ps1`. Invoke `superpowers:verification-before-completion`; freeze Lite v1 only from fresh passing evidence.
