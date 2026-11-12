# Build Mode Response Adapter Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate the decoupled Build Mode response adapter, update the mode-aware prompt builder, update the orchestrator to route responses in Build mode, and verify all integration and unit tests pass.

**Architecture:** Route Build mode assistant responses through the new `ConvertFrom-AIBuildResponse` adapter. If a valid proposal is returned, create a new file edit session and show the Diff UI. Clean up legacy tests to reflect the new decoupled vocabulary and pipeline.

**Tech Stack:** PowerShell, Pester (for testing), WPF (for UI, indirectly through controls).

## Global Constraints
- Target PowerShell version: PowerShell 5.1 / Core.
- Frozen vocabulary: Use `Path:`, `Old Text:`, and `New Text:` for Build mode.

---

### Task 1: Update Prompt Builder Vocabulary & EnableEditing
**Files:**
- Modify: `AI/AIContext.ps1`
- Modify: `Modules/AIWorkspace.ps1`
- Test: `tests/AIContextEditInstructions.Tests.ps1`
- Test: `tests/AIContextBuildSafety.Tests.ps1`

- [ ] **Step 1: Write the failing test**
  Modify `tests/AIContextEditInstructions.Tests.ps1` to assert the new prompt vocabulary.
  Modify `tests/AIContextBuildSafety.Tests.ps1` to assert the safety instructions use the new vocabulary.
- [ ] **Step 2: Run test to verify it fails**
  Run: `powershell -File tests/AIContextEditInstructions.Tests.ps1` and `powershell -File tests/AIContextBuildSafety.Tests.ps1`
  Expected: FAIL (missing new prompt blocks/vocabulary)
- [ ] **Step 3: Write minimal implementation**
  Update `AI/AIContext.ps1`'s system context instructions to use `Path:`, `Old Text:`, `New Text:`.
  Update `Modules/AIWorkspace.ps1` to call `Invoke-AIGenerate` with `-EnableEditing:($script:AIRequestMode -eq 'Build')`.
- [ ] **Step 4: Run test to verify it passes**
  Run: `powershell -File tests/AIContextEditInstructions.Tests.ps1` and `powershell -File tests/AIContextBuildSafety.Tests.ps1`
  Expected: PASS
- [ ] **Step 5: Commit**
  Run: `git commit -am "feat: update prompt builder vocabulary and enable editing switch"`

### Task 2: Implement coordinating function ConvertFrom-AIBuildResponse
**Files:**
- Modify: `AI/Editing/ResponseAdapter.ps1`

- [ ] **Step 1: Write the failing test**
  We will write a test for `ConvertFrom-AIBuildResponse` inside `tests/AIWorkspaceResponseAdapter.Tests.ps1` to check correct orchestration of Split, Validate, and Build.
- [ ] **Step 2: Run test to verify it fails**
  Run: `powershell -Command "Invoke-Pester tests/AIWorkspaceResponseAdapter.Tests.ps1"`
  Expected: FAIL (function not found/throws)
- [ ] **Step 3: Write minimal implementation**
  Define `ConvertFrom-AIBuildResponse` in `AI/Editing/ResponseAdapter.ps1`.
- [ ] **Step 4: Run test to verify it passes**
  Run: `powershell -Command "Invoke-Pester tests/AIWorkspaceResponseAdapter.Tests.ps1"`
  Expected: PASS
- [ ] **Step 5: Commit**
  Run: `git commit -am "feat: implement ConvertFrom-AIBuildResponse coordinating function"`

### Task 3: Integrate adapter inside Orchestrator Finalize-AIAssistantMessage
**Files:**
- Modify: `Modules/AIWorkspace.ps1`
- Test: `tests/AIWorkspaceIntegration.Tests.ps1`
- Test: `tests/AIPlanBuildRouting.Tests.ps1`

- [ ] **Step 1: Write the failing test**
  Update `tests/AIWorkspaceIntegration.Tests.ps1` and `tests/AIPlanBuildRouting.Tests.ps1` to assert that the new parser and decoupled target logic are used.
- [ ] **Step 2: Run test to verify it fails**
  Run: `powershell -File tests/AIWorkspaceIntegration.Tests.ps1` and `powershell -File tests/AIPlanBuildRouting.Tests.ps1`
  Expected: FAIL
- [ ] **Step 3: Write minimal implementation**
  Dot-source `AI/Editing/ResponseAdapter.ps1` in `Modules/AIWorkspace.ps1` after other editing modules.
  Update `Finalize-AIAssistantMessage` to route Build mode assistant response text through `ConvertFrom-AIBuildResponse`.
  Ensure `AIEditTargetPath` is only set in Build mode.
- [ ] **Step 4: Run test to verify it passes**
  Run: `powershell -File tests/AIWorkspaceIntegration.Tests.ps1` and `powershell -File tests/AIPlanBuildRouting.Tests.ps1`
  Expected: PASS
- [ ] **Step 5: Commit**
  Run: `git commit -am "feat: integrate decoupled response adapter inside orchestrator"`
