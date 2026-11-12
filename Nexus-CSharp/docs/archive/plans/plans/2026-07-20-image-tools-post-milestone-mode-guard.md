# Image Tools Post-Milestone Mode Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplify the Image Tools header, separate each mode's workspace state, and guard destructive mode changes with an in-workspace themed confirmation overlay.

**Architecture:** Keep the existing single view and `Set-ImageToolsMode` API. Add presentation-local pending-mode state and an overlay resolver in `Modules/RemoveBackground.ps1`; reuse `Clear-ImagePipelineInput` through the existing compatibility adapter to clear state, without changing the shared pipeline. Select the empty drop-zone brush from the active mode while preserving all engine, task registry, lifecycle, and output contracts.

**Tech Stack:** PowerShell 5.1, WPF XAML, existing Nexus ResourceDictionary, existing PowerShell assertion suites.

## Global Constraints

- No `MessageBox` or secondary `Window`.
- Do not modify `Foundation/ImagePipeline.ps1`, engines, model metadata, registry, lifecycle, or public engine contracts.
- Preserve preset, selected model, Upscale scale, output folder, and all source/saved files.
- Reject mode changes while processing.
- Cancel preserves all state; confirm clears the workspace before changing mode.
- Keep one view, one preview, one settings panel, and one shared pipeline.

---

### Task 1: Replace the frozen mode-switch test contract

**Files:**
- Modify: `Tests/ImageToolsArchitecture.Tests.ps1`
- Modify: `Tests/ImageToolsWorkflow.Tests.ps1`

**Interfaces:**
- Consumes: existing `Set-ImageToolsMode -Mode <RemoveBackground|Upscale>` and diagnostic pipeline snapshot.
- Produces: regression contract for `ImageToolsModeSwitchOverlay`, `Resolve-ImageToolsModeSwitch`, direct empty switching, cancel preservation, confirmed clearing, mode-specific empty surfaces, and processing rejection.

- [ ] **Step 1: Update architecture assertions**

Assert that the title/subtitle are absent, both mode buttons remain in `RemoveBackgroundHeader`, the named overlay and two action buttons exist exactly once, and neither native `MessageBox` nor a new Window is introduced by the mode-switch implementation.

- [ ] **Step 2: Update workflow assertions**

Replace the old state-preservation-on-switch assertions with: empty switches immediately; populated workspace opens the overlay without mutation; cancel preserves input/result/zoom/pan/mode; confirm clears input/result/preview/zoom/pan/compare/status and changes mode; settings values and output folder survive; processing rejects switching.

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests\ImageToolsArchitecture.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests\ImageToolsWorkflow.Tests.ps1
```

Expected: failures identify missing overlay/resolver and the obsolete header/mode-preservation behavior; no parse/setup error.

---

### Task 2: Implement the local overlay and destructive-switch guard

**Files:**
- Modify: `Views/Modules/RemoveBackgroundView.xaml`
- Modify: `Modules/RemoveBackground.ps1`
- Test: `Tests/ImageToolsArchitecture.Tests.ps1`
- Test: `Tests/ImageToolsWorkflow.Tests.ps1`

**Interfaces:**
- Consumes: `Clear-RemoveBackgroundImage`, `Clear-ImagePipelineInput`, `Set-ImageToolsMode`, `Get-ImageToolsPipeline`, existing theme brushes.
- Produces: `Show-ImageToolsModeSwitch`, `Resolve-ImageToolsModeSwitch -Confirm`, `$script:ImageToolsPendingMode`, and mode-specific empty drop-zone presentation.

- [ ] **Step 1: Simplify the XAML header**

Remove only the title/subtitle, retain `RemoveBackgroundHeader`, and place the two existing mode buttons directly at the top with compact margins. Do not move or duplicate preview/settings controls.

- [ ] **Step 2: Add the themed modal overlay**

Add `ImageToolsModeSwitchOverlay` as the last root child spanning all rows with a dimming layer, a centered themed card, the approved generic warning copy, `BtnImageToolsModeSwitchCancel`, and `BtnImageToolsModeSwitchConfirm`. Default visibility is `Collapsed`.

- [ ] **Step 3: Gate `Set-ImageToolsMode`**

Before changing a different mode, reject Processing; if pipeline input or result exists, store the destination and show the overlay without mutating state. `-PresentationOnly` continues to refresh presentation without prompting.

- [ ] **Step 4: Resolve the overlay**

Cancel hides the overlay and clears only pending destination. Confirm hides it, calls the existing clear adapter, then applies the pending mode. Preserve model/preset/scale/output controls and never delete original or saved files.

- [ ] **Step 5: Apply mode-specific empty background**

When Empty, set `RemoveBackgroundDropZone` to `RemoveBackgroundCheckerboardBrush` for Remove Background and `SurfaceSecondaryBrush` for Upscale. Do not alter the result-layer checkerboard or compare implementation.

- [ ] **Step 6: Bind controls once**

Register the overlay controls in the existing `ViewRoot.FindName()` cache and bind the two buttons once in module initialization. Do not add duplicate handlers.

- [ ] **Step 7: Run focused tests and verify GREEN**

Run the two Task 1 commands. Expected: both suites pass their revised Image Tools contract.

---

### Task 3: Regression, review, and completion verification

**Files:**
- Review only: all changed files.

**Interfaces:**
- Consumes: completed Tasks 1–2.
- Produces: evidence that the core frozen architecture and engines remain unchanged.

- [ ] **Step 1: Parse and XAML-load checks**

Parse `Modules/RemoveBackground.ps1` and load `Views/Modules/RemoveBackgroundView.xaml` as WPF XAML.

- [ ] **Step 2: Run the full Image Tools/Remove Background regression matrix**

Run Architecture, Workflow, ImagePipeline, Pipeline Behavior, ImageTools Engine, RemoveBackground Engine, and both known baseline-failure suites. Expected: all green suites remain green, baseline failure identities remain exact, and New Regression is zero.

- [ ] **Step 3: Audit protected files**

Verify hashes for `Foundation/ImagePipeline.ps1` and engine/model/metadata files are unchanged from the pre-implementation snapshot.

- [ ] **Step 4: Request code review**

Review the scoped diff against this plan, fix any Critical/Important issue with a new RED/GREEN cycle, and rerun affected regression suites.

- [ ] **Step 5: Final verification**

Re-run the proof commands fresh and report actual counts, baseline failures, changed files, and any remaining manual-only validation.
