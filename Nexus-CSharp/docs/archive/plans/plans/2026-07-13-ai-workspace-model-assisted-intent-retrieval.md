# AI Workspace Model-Assisted Intent Retrieval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace regex-authoritative Build intent routing with a strict local-model intent pass, bounded Top-3 source evidence validation, and the existing ExactRaw proposal workflow.

**Architecture:** The existing local model performs a small intent-only streaming pass containing one fixed system instruction and the current user instruction, followed by strict five-field validation. Existing Candidate Ranking retrieves files; at most three candidate files are read sequentially to find actual source evidence, and only one Phase 1-validated final target proceeds to ExactRaw and the existing proposal pass.

**Tech Stack:** Windows PowerShell 5.1, existing `Invoke-AIGenerate`/`Receive-AITokens`/`Stop-AIGenerate` lifecycle, existing Workspace Symbol Index and Candidate Ranking v2/v2.1, frozen Phase 1 v1.1 and Safe Editing Engine.

## Global Constraints

- Do not change `AI/Editing/AttachmentManager.ps1`, Phase 1 v1.1, Candidate Ranking v2/v2.1, Workspace Index schema, `AI/AI.psm1`, `AI/AIContext.ps1`, provider, parser, Safe Editing Engine, context budget, WPF layout, or Apply/Cancel/Rollback.
- Intent input contains only the current user instruction plus the fixed intent system instruction: no history, Project Map, source, attachment, ExactRaw, or proposal context.
- Intent output has exactly five string fields: `target`, `location`, `requestedChange`, `newValue`, and `condition`; each may be empty.
- Retrieval requires non-empty `target` and at least one non-empty `requestedChange` or `newValue`; otherwise return `ClarificationRequired`.
- Regex is forbidden as natural-language intent authority or fallback. Regex remains allowed for schema/forbidden-content checks and bounded source evidence matching.
- Intent cannot choose a file/scope and cannot become an exact Phase 1 hint. Only actual source evidence from a Top-3 validation read may be passed to Phase 1.
- Candidate retrieval remains Top 5; source validation remains sequential Top 3; only the final selected file/scope enters ExactRaw.
- Do not add a second model, embedding, vector database, dependency, typo dictionary, fuzzy framework, or full-project prompt.
- Do not commit until Git provenance is safe.
- The public generation API exposes neither `temperature` nor `max_tokens`. Do not change that contract. Use the existing server-configured sampling default, request concise JSON in the prompt, stop the intent stream once the existing token estimator exceeds 256, and reject any completed output estimated above 256 tokens. Record this limitation explicitly in verification.

---

### Task 1: Strict Intent Prompt and Result Contract

**Files:**
- Modify: `AI/Editing/AutomaticTargetResolution.ps1`
- Modify: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`

**Interfaces:**
- Produces: `New-AIAutomaticIntentRequest -Instruction <string>` returning exactly two messages (fixed system prompt and current user instruction), with no other context.
- Produces: `ConvertFrom-AIAutomaticIntentResult -Text <string>` returning `{ Success; Reason; Intent; ClarificationMessage }`.
- Produces intent object with exactly `target`, `location`, `requestedChange`, `newValue`, `condition` string properties.
- Removes `Get-AIAutomaticTargetHints` natural-language regex parsing from production authority; no regex fallback replaces a failed model intent.

- [ ] **Step 1: Add RED tests for the request boundary**

Add tests that call `New-AIAutomaticIntentRequest` and assert the message collection contains only:

```powershell
@(
    @{ role = 'system'; content = '<fixed intent JSON contract>' },
    @{ role = 'user'; content = $instruction }
)
```

Assert serialized messages contain none of the supplied hostile sentinel values representing history, Project Map, source, attachment, ExactRaw, or proposal content. Assert the fixed prompt forbids file/path/symbol/line selection and asks for JSON only.

- [ ] **Step 2: Add RED tests for strict schema and readiness**

Test these exact outcomes:

```powershell
ConvertFrom-AIAutomaticIntentResult '{"target":"Reset-PdfSelection","location":"","requestedChange":"fix","newValue":"","condition":""}'
# Success=True; direct identifier accepted

ConvertFrom-AIAutomaticIntentResult '{"target":"Modules\\Settings.ps1","location":"","requestedChange":"change","newValue":"white","condition":""}'
# Success=False; Reason=forbidden_path

ConvertFrom-AIAutomaticIntentResult '{"target":"Reset","location":"Settings","requestedChange":"","newValue":"","condition":""}'
# Success=False for retrieval; Reason=change_clarification_required; human clarification present

ConvertFrom-AIAutomaticIntentResult '{"target":"","location":"Settings","requestedChange":"change color","newValue":"white","condition":""}'
# Success=False for retrieval; Reason=target_clarification_required
```

Also reject additional/missing fields, non-string values, nested objects/arrays, Markdown/code fences, `aiedit`, multiline source code, absolute or relative paths, line-selection instructions, patch/proposal text, invalid JSON, and output estimated above 256 tokens.

- [ ] **Step 3: Run RED and verify the failure source**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
```

Expected: new assertions fail because the intent request/schema APIs do not exist and the regex extractor remains authoritative; all previously frozen suites remain untouched.

- [ ] **Step 4: Implement the minimal strict contract**

Implement fixed-field validation by parsing with `ConvertFrom-Json`, inspecting the exact property set, checking every value is `[string]`, and applying forbidden-content checks. Do not attempt to repair JSON or infer missing fields. Use the existing `Measure-AIMessageBudgetTokens` command when available, otherwise the same conservative `Ceiling(length / 4)` estimator already used by AI core. Return specific stable reasons.

The model prompt may normalize unambiguous typo/abbreviation but must emit an empty ambiguous field. The code validates model output; it never interprets the original natural-language instruction.

- [ ] **Step 5: Run GREEN and parse check**

Run the ATR suite above and parse `AutomaticTargetResolution.ps1` using `System.Management.Automation.Language.Parser`. Expected: all Task 1 tests pass, zero parse errors.

---

### Task 2: Bounded Actual-Source Evidence Validation

**Files:**
- Modify: `AI/Editing/AutomaticTargetResolution.ps1`
- Modify: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`

**Interfaces:**
- Changes `Invoke-AIAutomaticTargetValidation` to consume validated `Intent`, not regex-derived task hints.
- Produces metadata-only evidence per candidate: actual matched value, type, line/offset, score/reason; no raw body/snippet.
- Preserves the existing four verdicts: `Viable`, `Ambiguous`, `NotViable`, `Stale`.
- Preserves original Phase 1 stable reason in every result.

- [ ] **Step 1: Add RED bounded-read and evidence tests**

Create synthetic Top-5 candidate fixtures with injected sequential `ReadSource` callbacks. Assert:

- only candidates 1-3 are read;
- candidate 4/5 are never read;
- candidate raw values are released before the next read and absent from returned objects;
- source evidence is derived from current raw candidate text, never copied from the intent string;
- `Reset to Default` intent can identify actual `Reset ke Default`, `BtnResetSettings`, `Foreground`, and `RadioLight` evidence through normalized token co-occurrence;
- no evidence candidate is invented when no actual match exists;
- actual `Reset-PdfSelection` remains accepted as direct identifier evidence;
- Phase 1 receives actual evidence text/identifier and never the English intent phrase as an exact hint.

- [ ] **Step 2: Run RED**

Run the ATR suite. Expected: evidence tests fail because validation currently passes regex-produced intent text directly to Phase 1.

- [ ] **Step 3: Implement minimal candidate-local evidence matching**

Within `AutomaticTargetResolution.ps1`, tokenize validated intent fields and candidate metadata using lowercase letter/digit tokens. Search only the current Top-3 raw candidate for bounded actual values in existing PowerShell/XAML forms: function/symbol identifier, `x:Name`, `Text`/`Content`, property/theme identifier, and import/dot-source. Deduplicate and bound retained evidence. Do not create a general language parser, fuzzy distance algorithm, translation dictionary, source cache, or new index.

Choose one strong unambiguous actual evidence item for legacy single-scope Phase 1, or two necessary co-related actual evidence items for the v1.1 bridge. Missing/conflicting evidence returns `target_evidence_not_found` or `ambiguous_target_evidence`. Phase 1 remains the syntax/viability authority.

- [ ] **Step 4: Run GREEN and frozen retrieval regressions**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV2.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV21.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
```

Expected: zero failures; Phase 1 remains `34/34`.

---

### Task 3: Two-Pass AIWorkspace Orchestration

**Files:**
- Modify: `Modules/AIWorkspace.ps1`
- Modify: `tests/AIWorkspaceAutomaticTargetResolution.Tests.ps1`
- Modify: `tests/AIPlanBuildRouting.Tests.ps1` only if its isolated routing harness must load the new intent helpers.

**Interfaces:**
- Adds minimal internal request stages `IntentExtraction` and `TargetResolution`; these are orchestration state, not new UI modes.
- Intent pass calls existing `Invoke-AIGenerate` with exactly the Task 1 messages, `EnableEditing:$false`, and no chat history/context payload.
- Proposal pass remains the existing Build generation call after one final ExactRaw payload exists.
- `Finalize-AIAssistantMessage` consumes internal intent output without saving/rendering it and resumes routing only after provider generation state is clear.

- [ ] **Step 1: Add RED lifecycle tests**

Using injected generation/polling commands, assert:

- Build folder route starts intent generation before candidate retrieval;
- intent generation receives only the fixed system prompt and current instruction;
- accumulated intent output is stopped/rejected when estimated size exceeds 256 tokens;
- intent JSON/internal placeholder never appears in `AIChatHistory`, conversation persistence, or rendered bubble content;
- proposal generation cannot start while `Test-AIGenerating` is true;
- after intent stream completion, internal placeholder removal, strict validation, and freshness recheck, target resolution resumes;
- cancel, new/switch/delete conversation, mode change, Project Root change, Dirty transition, or provider error between passes clears state and prevents retrieval/ExactRaw/proposal;
- incomplete intent creates human `ClarificationRequired` text;
- zero viable exposes its specific stable reason instead of `Target yang aman tidak ditemukan.`

- [ ] **Step 2: Run RED**

Run ATR and Plan/Build routing suites. Expected: new stage/lifecycle assertions fail; existing tests remain unchanged.

- [ ] **Step 3: Implement minimal orchestration**

Add one metadata-only pending intent state containing original instruction, request mode snapshot, project-root generation/freshness identity, stage, and token-estimator count. Do not store history, raw source, ExactRaw, proposal, or candidate bodies.

At Build send with zero attachments, invoke the intent pass instead of calling `Get-AIWorkspaceBuildRoute` immediately. During polling, accumulate only the internal intent buffer. If its estimate exceeds 256 tokens, call existing `Stop-AIGenerate`, discard the buffer, clear state, and report `intent_output_too_large`. On clean completion, require `Test-AIGenerating` false before strict validation and routing. Remove the placeholder before any render/persistence action.

Pass the validated intent into existing candidate retrieval/Task 2 validation. On exactly one viable target, perform final freshness check and existing ExactRaw handoff, then start the normal proposal generation. Preserve Diff/Apply/Cancel/Rollback unchanged.

- [ ] **Step 4: Run GREEN and PowerShell parse checks**

Run ATR, Plan/Build, and AI Workspace integration suites, then parse both production files with the PowerShell 5.1 parser. Expected: zero failures and zero parse errors.

---

### Task 4: Whole-Branch Verification and Runtime Acceptance

**Files:**
- Test only: existing test scripts; create no production file.
- Optional: `tests/AIWorkspaceAutomaticTargetResolution.Runtime.ps1` only if an existing runtime harness cannot exercise the two-pass route.

**Interfaces:**
- Consumes the completed Task 1-3 behavior.
- Produces verification evidence; no feature expansion or cleanup.

- [ ] **Step 1: Run final spec and quality review**

Review for forbidden regex intent parsing, context leakage, provider overlap, raw persistence, extra source reads, generic zero-viable text, and modifications to frozen files. Fix only Critical/Important implementation defects and re-review.

- [ ] **Step 2: Run all regression gates**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceAutomaticTargetResolution.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipelineV11.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIBuildContextPipeline.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIPlanBuildRouting.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV2.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceCandidateRankingV21.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntelligenceLite.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIWorkspaceIntegration.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AttachmentTruncationMetadata.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIContextBuildSafety.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIContextEditInstructions.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\LlamaServerProviderSyncHash.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIEditingLiteTests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\AIFileEditorCompatibility.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Run-AIFileEditorIntegrationTests.ps1
git diff --check
```

Expected frozen counts include Phase 1 v1.1 `34/34`, legacy Phase 1 `14/14`, Candidate Ranking v2 `31/31`, Candidate Ranking v2.1 `15/15`, Workspace Intelligence Lite `70/70`, core `12/12`, compatibility `5/5`, and integration `PASS`.

- [ ] **Step 3: Verify frozen-file provenance and parse**

Compare pre-task SHA-256 values for every frozen file, parse all changed `.ps1` files with Windows PowerShell 5.1, and inspect changed paths. Expected: hashes identical, parse PASS, only scoped files changed.

- [ ] **Step 4: Run runtime acceptance**

With the existing model active and a selected Project Root, verify:

- `tmbl reset jd ptih` produces sufficient intent and begins bounded retrieval;
- `jadikan putih di Settings` asks what target is meant;
- `Reset ke Default di Settings` asks what change is requested;
- `hapus tombol Conversation` proceeds with empty `newValue`;
- ambiguous typo leaves the relevant field empty and requests clarification;
- English intent resolves actual Indonesian source evidence when uniquely viable;
- intent JSON never appears as a bubble/history entry;
- exactly one target reaches ExactRaw/proposal/diff;
- Apply/Cancel/Rollback remain functional;
- `crash.log` gains no new entry.

Record intent latency and whether the 256-token host-side cap was reached. Record that temperature remains the existing server-configured default because the frozen public API exposes no sampling parameter.

- [ ] **Step 5: Report status without committing**

Report RED/GREEN per task, review result, regression counts, runtime evidence, output-cap behavior, and frozen hashes. Do not commit. Mark PASS only after automated and runtime gates pass; otherwise report the precise BLOCKED or runtime-pending state.
