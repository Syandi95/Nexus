# Image Tools v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Gates A and B are cleared for Development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add AI Upscale to one Image Tools workspace while preserving the mature Remove Background behavior and keeping Balanced Remove Background as an unchanged regression baseline.

**Architecture:** The existing Remove Background view, preview, progress, output, process lifecycle, and controller are the implemented Image Tools foundation. Gate A authorizes an in-place migration of those two files into the Image Tools host, limited to mode hosting, settings presentation, routing, dispatch, and AI Upscale integration; it does not authorize a Remove Background refactor. Remove Background functions remain the unchanged default branch, while AI Upscale adapts through additive mode dispatch and the same workspace. Gate B approves the dynamic float32 RealESR General WDN x4 v3 ONNX model for Development; AI Upscale v1 reads one manually authored, package-local `metadata.json` and does not invoke, modify, extract, or duplicate the Remove Background metadata generator.

**Tech Stack:** Windows PowerShell 5.1, WPF XAML loaded by Nexus ModuleHost, C# .NET Framework 4.8, Microsoft ONNX Runtime CPU, PNG output, existing Nexus theme ResourceDictionaries.

## Plan Status

`GATE A APPROVED — GATE B CLEARED (DEVELOPMENT) — READY FOR EXECUTING PLANS`

Executing Plans may begin using the approved local ONNX artifact. Gate A still forbids any Remove Background behavior change, and Gate B does not waive Release Checklist requirements.

## Global Constraints

- Module visible name: `Image Tools`.
- Exactly one workspace, one preview, one settings panel, one primary process button, one progress manager, one output manager, one process manager, one controller, and one ImagePipeline.
- Modes `Remove Background` and `AI Upscale` remain visible at all times.
- During processing, the active mode remains active and the other mode uses `IsEnabled = False`; never `Hidden` or `Collapsed`.
- Switching mode preserves preview, zoom, preview size, and layout; only settings content and primary action change.
- Remove Background behavior, pipeline results, compare, zoom, preview, progress, output, and lifecycle remain regression-locked.
- The approved changes to `RemoveBackgroundView.xaml` and `RemoveBackground.ps1` are migration work only, not a Remove Background refactor.
- Existing Remove Background workflow, lifecycle, inference pipeline, result, preview, progress, output, process, and save behavior must remain identical.
- If implementation requires changing any Remove Background behavior, stop and request approval before editing.
- AI Upscale default model is `RealESRGAN_General_x4_v3`, ONNX Runtime CPU, default scale `4x`.
- Image Tools exposes integer target scales `1x` through `10x`; this UI/pipeline range is independent of the model's native `4x` output contract.
- Scale pipeline is frozen: `1x` bypasses inference; `2x` through `10x` use exactly one native `4x` inference; `2x`/`3x` downsample the native result, `4x` uses it directly, and `5x` through `10x` resize it once to the exact requested dimensions. Repeated inference is forbidden in v1.
- Final upscale dimensions are exactly `input width * targetScale` by `input height * targetScale`; the deterministic final resize uses high-quality bicubic interpolation.
- Advanced settings v1 contains only `Ganti Model AI`.
- Slider labels show values only in their titles; no numeric tick labels below sliders.
- Slider values must come from provider-supported values.
- No batch, queue, plugin system, cache, telemetry, analytics, auto update, scheduler, dependency injection, factory, event bus, or service locator.
- No external process provider is implemented for v1 because the selected engine is ONNX; the optional provider remains absent until an executable/crash-isolation requirement exists.
- No model download, unapproved model, invented metadata, fake inference, or placeholder engine is allowed.
- Development uses only `Models\Upscale\RealESR General WDN x4 v3\real_esrgan_general_x4v3.onnx`, SHA-256 `6782881C3F7B90F691B77F703AB7118E2EBE80D3CF9AA303EE5F87361E339924`.
- AI Upscale v1 uses a manually authored `Models\Upscale\RealESR General WDN x4 v3\metadata.json` as part of the approved model package.
- AI Upscale only reads that sidecar. It must not call or change `Set-RemoveBackgroundModelSlot`, create a generator, extract a Metadata Core helper, or duplicate generator logic.
- Metadata Core is a separate post-`STABLE/FROZEN` milestone and is outside this plan.
- The engine API must remain model-generic: public execution contracts use task-neutral names and `targetScale`; no public method or parameter may contain `RealESRGAN`.
- Provenance, attribution, conversion manifest, and complete license packaging are Release Checklist requirements, not Development blockers.
- `C:\Users\Syand\3D Objects\Proyek Upscaler` is excluded from Image Tools v1: do not audit it, copy from it, reference its code, or add a prototype-review stage.
- Engine implementation evidence is limited to the frozen Nexus architecture, the approved Upscayl audit findings, and the validated local `RealESRGAN_General_x4_v3` package.
- No refactor, file move, or folder restructure is allowed.

---

## Read-Only Source Findings

1. `Views\Modules\RemoveBackgroundView.xaml` is the only implemented image workspace. It owns the only preview, zoom toolbar, compare controls, settings panel, progress, and primary action.
2. `Modules\RemoveBackground.ps1` directly owns controller state, import, preview geometry, progress, process lifecycle, output, save, and direct `[Nexus.AI.AIEngine]` calls. There is no external mode or pipeline seam.
3. `ManajemenFile.ps1` registers only `RemoveBackground` and loads only `RemoveBackgroundView.xaml` for this feature area.
4. `Engine\AIEngine\AIEngine.cs` accepts only model packages under `Models\RemoveBackground`, validates segmentation-shaped tensors, and writes `Nexus-RemoveBackground-*.png` through transparent-alpha postprocessing.
5. The approved local model exists at `Models\Upscale\RealESR General WDN x4 v3\real_esrgan_general_x4v3.onnx`. Audit evidence: opset 20; standard `ai.onnx` operators only; input `input` float32 NCHW `[-1,3,-1,-1]`; output `output` float32 NCHW with verified 4x spatial dimensions; explicit ONNX Runtime CPU 1.27.0 load and in-memory inference PASS.
6. A new parallel `ImageToolsView.xaml` would duplicate the current workspace. Wrapping the existing view and injecting controls at runtime would create two view roots, mutate the frozen UI indirectly, and violate the separate-XAML architecture.

## Freeze Baseline

These hashes are the planning baseline and must be re-read immediately before execution:

```text
Modules\RemoveBackground.ps1
SHA-256 6EAE5090DBB5F21347847A333CE5C81BBF591A67A2DC5BF4199774A35C51B4A1

Views\Modules\RemoveBackgroundView.xaml
SHA-256 9427271E1231D1E4FD8E96A2C851322BA195FA6C33F3D80CD1EF4BFE578A48AF
```

The hashes are evidence of the starting point, not authorization to edit either file.

### Existing Baseline Failures

The user approved the following pre-existing regression fingerprint on 2026-07-19. Image Tools v1 must not fix, remove, rename, or add to these failures:

```text
RemoveBackground.Tests.ps1: FAIL 6/73
- View has no hardcoded hex colors
- View hides technical engine terms
- PowerShell calls only the AIEngine boundary
- Checkerboard is the compare surface background
- Slider DragDelta updates compare ratio and clip
- New image resets compare to full Before

RemoveBackgroundWorkflow.Tests.ps1: FAIL 2/23
- Workflow enters indeterminate processing state
- Transparent result exposes checkerboard surface

RemoveBackgroundEngine.Tests.ps1: PASS 24/24
```

After every implementation task, re-run all three suites. Success means the exact failure names and counts remain unchanged, Engine remains PASS 24/24, and `New Regression = 0`. Any fingerprint drift is a freeze violation and an immediate stop.

---

## Approval Gate A: Approved Migration Exception

**Status:** `APPROVED WITH LIMITS`

The user explicitly permits these two files to participate in the migration from Remove Background to Image Tools:

- `Views\Modules\RemoveBackgroundView.xaml`
  - Allowed only to become the Image Tools host, add the always-visible `Remove Background` and `AI Upscale` mode buttons, expose mode binding/state, and make the existing single settings panel mode-driven.
  - Existing Remove Background preview tree, toolbar, compare, empty state, status area, progress, output area, primary process location, and layout behavior remain frozen.
- `Modules\RemoveBackground.ps1`
  - Allowed only for mode state, mode routing, dispatch, and additive AI Upscale integration.
  - Existing Remove Background workflow, lifecycle, inference pipeline, result construction, preview, progress, output, process, and save functions must not be rewritten or behaviorally altered.
  - The Remove Background dispatcher branch must call the same existing functions with the same arguments and state transitions.

This approval does not permit a wrapper view, second controller, duplicate workflow, renamed/moved Remove Background files, or opportunistic cleanup. Any discovered need to change Remove Background behavior is an immediate hard stop and requires a new user decision.

---

## Approval Gate B: Approved Local Upscale Package

**Status:** `CLEARED (DEVELOPMENT)`

Gate B is cleared by the validated local RealESR General WDN x4 v3 ONNX artifact and the accepted technical audit. The discarded prototype remains excluded from the workflow.

The only approved model artifact is:

```text
Models\Upscale\RealESR General WDN x4 v3\
  real_esrgan_general_x4v3.onnx
```

Accepted Development contract: SHA-256 `6782881C3F7B90F691B77F703AB7118E2EBE80D3CF9AA303EE5F87361E339924`; opset 20; float32 NCHW input `input` with dynamic batch/height/width; float32 dynamic output `output`; output dimensions exactly 4x the input; no custom operator/domain; Microsoft ONNX Runtime CPU 1.27.0 load and in-memory inference PASS. For AI Upscale v1 only, a manually authored `metadata.json` records this validated contract and the frozen preprocessing/postprocessing facts. It is read-only at runtime and is not produced by a generator.

The package-local metadata contract is:

```json
{
  "schemaVersion": 1,
  "modelId": "real-esrgan-general-x4-v3",
  "displayName": "RealESRGAN General WDN x4 v3",
  "officialModelName": "RealESRGAN_General_x4_v3",
  "task": "image-upscale",
  "modelFile": "real_esrgan_general_x4v3.onnx",
  "sha256": "6782881c3f7b90f691b77f703ab7118e2ebe80d3cf9aa303ee5f87361e339924",
  "input": {
    "name": "input",
    "dataType": "float32",
    "layout": "NCHW",
    "shape": [-1, 3, -1, -1],
    "colorSpace": "RGB",
    "mean": [0.0, 0.0, 0.0],
    "std": [1.0, 1.0, 1.0],
    "resize": "none"
  },
  "output": {
    "name": "output",
    "dataType": "float32",
    "layout": "NCHW",
    "shape": [-1, 3, -1, -1],
    "colorSpace": "RGB",
    "activation": "clamp-0-1",
    "nativeScale": 4,
    "resize": "bicubic"
  },
  "runtime": {
    "engine": "Microsoft.ML.OnnxRuntime",
    "minimumVersion": "1.27.0",
    "provider": "CPU",
    "directML": false,
    "opset": 20
  }
}
```

### Release Requirements

The following are not Development blockers, but must be complete before distribution:

- exact source PTH identity and checksum;
- conversion tool, version, parameters, and manifest;
- applicable license files and attribution;
- release provenance metadata;
- a release-readiness review confirming those records match the shipped ONNX artifact.

---

## Conditional File Map

The following map is active for Executing Plans under the cleared Development gate.

### Existing files requiring changes

- `Views\Modules\RemoveBackgroundView.xaml`
  - Migrate the existing view into the Image Tools host by adding two mode buttons and mode-specific controls inside the existing single settings panel.
  - Do not create another preview, settings panel, progress bar, or primary action.
- `Modules\RemoveBackground.ps1`
  - Keep existing Remove Background functions as the unchanged default route.
  - Add one mode field, one routing boundary, dispatcher functions, and additive AI Upscale integration without rewriting Remove Background behavior.
- `Engine\AIEngine\AIEngine.cs`
  - Add an upscale task path to the existing ONNX provider while leaving the Remove Background `LoadModel`/`Run` behavior unchanged.
- `Engine\AIEngine\ModelMetadata.cs`
  - Add only metadata fields proven necessary by the approved upscale graph, such as `scale` and verified output layout.
- `Engine\AIEngine.dll`
  - Rebuild/deploy from the reviewed C# source.
- `Views\MainWindow.xaml`
  - Change only the visible sidebar label from `Remove Background` to `Image Tools`; retain the internal navigation key unless a proven runtime requirement demands otherwise.
- `ManajemenFile.ps1`
  - Dot-source/register the same module under the existing cached ModuleHost entry and change only visible module title/tooltip text to `Image Tools`.

### Approved model package

- Use unchanged: `Models\Upscale\RealESR General WDN x4 v3\real_esrgan_general_x4v3.onnx`
- Create once from the frozen JSON contract above: `Models\Upscale\RealESR General WDN x4 v3\metadata.json`
- Runtime reads and validates this file but never generates or rewrites it.
- Do not create a Development-only license placeholder; licensing and provenance remain Release Requirements.

### New tests

- `tests\ImageToolsArchitecture.Tests.ps1`
  - Enforce one workspace/preview/settings/progress/primary action and frozen Remove Background behavior.
- `tests\ImageToolsWorkflow.Tests.ps1`
  - Exercise mode switching, processing lock, upscale inference, preview continuity, and PNG output.
- `tests\ImageToolsEngine.Tests.ps1`
  - Verify graph metadata, CPU load, x4 dimensions, valid PNG pixels, cancellation, and unchanged Remove Background engine behavior.

### Files explicitly not created

- `Views\Modules\ImageToolsView.xaml`
- `Views\Modules\AIUpscaleView.xaml`
- `Modules\AIUpscale.ps1`
- Separate preview/progress/output/process manager files
- External process provider files

Creating any of those would violate the one-workspace or YAGNI constraints.

---

## Conditional Implementation Tasks

### Task 0: Record Cleared Gate B and Frozen Remove Background Behavior

**Files:** Read-only.

**Interfaces:**
- Consumes: approved Gate A contract, local model package, current source hashes.
- Produces: an execution baseline; `BLOCKED` only on model drift, technical runtime regression, or a required Remove Background behavior change.

- [ ] **Step 1: Record the approved Gate A boundary**

Execution accepts the existing approval only for:

```text
Views\Modules\RemoveBackgroundView.xaml
Modules\RemoveBackground.ps1
```

Before editing, record that `RemoveBackgroundView.xaml` is limited to host/mode/settings presentation and `RemoveBackground.ps1` is limited to routing/dispatcher/AI Upscale integration. Any planned edit outside those categories is `BLOCKED`.

- [ ] **Step 2: Recompute the frozen hashes**

Run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath `
  'C:\Users\Syand\3D Objects\Nexus\Modules\RemoveBackground.ps1', `
  'C:\Users\Syand\3D Objects\Nexus\Views\Modules\RemoveBackgroundView.xaml'
```

Expected: hashes match the Freeze Baseline above, or any drift is audited before continuing.

- [ ] **Step 3: Capture behavior-preservation baselines**

Before editing, use the PowerShell AST to hash the exact bodies of `Import-RemoveBackgroundImage`, `Start-RemoveBackgroundProcessing`, `Stop-RemoveBackgroundProcessing`, `Save-RemoveBackgroundResult`, `Update-RemoveBackgroundZoom`, `Update-RemoveBackgroundCompareClip`, `Set-RemoveBackgroundPreviewSurface`, `Activate-RemoveBackgroundModule`, and `Deactivate-RemoveBackgroundModule`. Also hash the XAML subtree rooted at `RemoveBackgroundPreview`. Store these values only in the execution log. The final verification must show that every listed function body and the preview subtree remain byte-identical; if mode integration would require changing one, stop before editing and report the reason.

- [ ] **Step 4: Verify the local upscale package**

Run:

```powershell
$modelPath = 'C:\Users\Syand\3D Objects\Nexus\Models\Upscale\RealESR General WDN x4 v3\real_esrgan_general_x4v3.onnx'
if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) {
    throw 'BLOCKED: approved model is missing'
}
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $modelPath).Hash
if ($hash -ne '6782881C3F7B90F691B77F703AB7118E2EBE80D3CF9AA303EE5F87361E339924') {
    throw "BLOCKED: approved model hash drifted: $hash"
}
```

Expected: the approved model exists and its hash exactly matches the cleared Gate B artifact.

- [ ] **Step 5: Audit the graph through the existing ONNX Runtime dependency**

Re-run graph inspection, ONNX Runtime CPU load, and a small dynamic in-memory inference using only the approved local package and existing Nexus dependencies. Expected: opset 20; float32 dynamic NCHW `input`; float32 dynamic `output`; exact 4x output; no custom operator; CPU load and inference PASS. Do not inspect `C:\Users\Syand\3D Objects\Proyek Upscaler`. Provenance and license completeness are not part of this Development revalidation; they remain Release Requirements.

### Task 1: Write Architecture Guard Tests First

**Files:**
- Create: `tests\ImageToolsArchitecture.Tests.ps1`

**Interfaces:**
- Consumes: current XAML/module/navigation source.
- Produces: failing assertions that define the one-workspace contract before implementation.

- [ ] **Step 1: Add exact source assertions**

The test must assert:

```powershell
Assert-Rule 'One preview' (([regex]::Matches($viewText, 'x:Name="RemoveBackgroundPreview"')).Count -eq 1)
Assert-Rule 'One primary action' (([regex]::Matches($viewText, 'x:Name="BtnRemoveBackgroundPrimary"')).Count -eq 1)
Assert-Rule 'Both modes always exist' ($viewText -match 'BtnImageToolsRemoveBackground' -and $viewText -match 'BtnImageToolsUpscale')
Assert-Rule 'No second Image Tools view' (-not (Test-Path (Join-Path $root 'Views\Modules\ImageToolsView.xaml')))
Assert-Rule 'No standalone Upscale view' (-not (Test-Path (Join-Path $root 'Views\Modules\AIUpscaleView.xaml')))
Assert-Rule 'No standalone Upscale module' (-not (Test-Path (Join-Path $root 'Modules\AIUpscale.ps1')))
Assert-Rule 'One controller registration' (([regex]::Matches($mainText, 'Register-NexusModuleView[^\r\n]+RemoveBackground')).Count -eq 1)
```

It must also record the pre-execution Remove Background behavior matrix by invoking the existing `tests\RemoveBackground.Tests.ps1`, `tests\RemoveBackgroundEngine.Tests.ps1`, and `tests\RemoveBackgroundWorkflow.Tests.ps1` unchanged.

- [ ] **Step 2: Run the new test and prove RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImageToolsArchitecture.Tests.ps1
```

Expected: FAIL only because the two mode controls and Image Tools routing are not implemented. Existing Remove Background suites must match the approved Existing Baseline Failures exactly.

### Task 2: Add the ONNX Upscale Path Additively

**Files:**
- Modify: `Engine\AIEngine\AIEngine.cs`
- Modify: `Engine\AIEngine\ModelMetadata.cs`
- Rebuild: `Engine\AIEngine.dll`
- Create: `Models\Upscale\RealESR General WDN x4 v3\metadata.json`
- Create: `tests\ImageToolsEngine.Tests.ps1`

**Interfaces:**
- Consumes: verified upscale graph plus the frozen package-local metadata contract.
- Produces: additive, model-generic `RunImageTransform(string inputPath, int targetScale)` returning the existing `EngineResult`; existing Remove Background `LoadModel` and `Run` remain compatible.

- [ ] **Step 1: Write failing engine tests**

Tests must prove:

```text
Load approved RealESRGAN_General_x4_v3 package on CPU
Reject any model other than the approved local artifact and checksum
Reject checksum or graph metadata mismatch
RunImageTransform rejects targetScale outside 1..10
RunImageTransform at 1x bypasses ONNX inference and returns an exact-size PNG
RunImageTransform at every integer scale 2..10 returns Success and an existing PNG
Every output width = input width * targetScale
Every output height = input height * targetScale
4x output uses the native model dimensions without a final resize
2x/3x and 5x..10x perform exactly one final bicubic resize after one native inference
Output preserves RGB content and valid alpha semantics
Cancel returns Cancelled without orphan output
Existing Balanced Remove Background workflow remains PASS
Set-RemoveBackgroundModelSlot source remains byte-identical
```

- [ ] **Step 2: Run engine tests and prove RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImageToolsEngine.Tests.ps1
```

Expected: FAIL because `RunImageTransform` and the package-local metadata do not exist.

- [ ] **Step 3: Implement only the verified graph contract**

Add this task-neutral public boundary without changing existing public signatures:

```csharp
public EngineResult RunImageTransform(string inputPath, int targetScale)
```

The method dispatches from the loaded metadata `task`, not from a RealESRGAN-specific name. It validates integer `targetScale` in `1..10`; at `1x` it bypasses inference and writes an exact-size temporary PNG. For `2x..10x`, it creates normalized RGB tensor values in `[0,1]`, runs the existing ONNX Runtime CPU session exactly once, clamps the float RGB output to `[0,1]`, and applies at most one high-quality bicubic resize to exact target dimensions unless `targetScale == nativeScale`. It uses existing cancellation/error/result conventions. Do not add tiling, GPU selection, threads, cache, recursive inference, or external process handling.

Extend `ModelMetadata` only with task-neutral fields required by this contract: top-level `schemaVersion`, `modelFile`, and `sha256`; output `layout`, `colorSpace`, and `nativeScale`. The Remove Background validation branch must continue reading its existing fields and behavior unchanged.

- [ ] **Step 4: Build and run RED-to-GREEN engine verification**

Run:

```powershell
dotnet build .\Engine\AIEngine\AIEngine.csproj --configuration Debug
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImageToolsEngine.Tests.ps1
```

Expected: build PASS and all engine tests PASS, including existing Remove Background regression.

### Task 3: Extend the Existing Single Workspace In Place

**Files:**
- Modify: `Views\Modules\RemoveBackgroundView.xaml`
- Modify: `Modules\RemoveBackground.ps1`
- Create: `tests\ImageToolsWorkflow.Tests.ps1`

**Interfaces:**
- Consumes: existing view controls and `AIEngine.RunImageTransform`.
- Produces: mode state `RemoveBackground|Upscale`; same preview/progress/output/process lifecycle for both modes.

- [ ] **Step 1: Write failing workflow tests**

Tests must assert:

```text
Both mode buttons are visible in Empty, ImageLoaded, Processing, Result, and Error states
Remove Background is default mode
Switching mode preserves image source, zoom, pan, preview dimensions, background selection, and layout
Only one settings panel, preview, progress bar, and primary action exists
Processing disables only the inactive mode without collapsing it
Remove Background settings show model, input size, output format, output folder, advanced model change
AI Upscale settings show model, integer scale slider 1x..10x defaulting to 4x, output folder, advanced model change
Scale value appears only in the settings title and remains synchronized with the slider
AI Upscale primary action text is Upscale Gambar
Remove Background primary action text is Hapus Background
Upscale result appears in the existing After layer
Save uses the existing single output manager and writes a valid PNG
```

- [ ] **Step 2: Run workflow tests and prove RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImageToolsWorkflow.Tests.ps1
```

Expected: FAIL because mode controls/routing do not exist.

- [ ] **Step 3: Add the two mode buttons to the existing view**

Add exactly these public names above the current preview while retaining their layout slot when disabled:

```xml
<Button x:Name="BtnImageToolsRemoveBackground" Content="Remove Background" IsEnabled="True"/>
<Button x:Name="BtnImageToolsUpscale" Content="AI Upscale" IsEnabled="True"/>
```

Use existing DynamicResource styles. Do not add another preview, panel, progress bar, output control, or process button.

- [ ] **Step 4: Add one mode field and route existing handlers**

The controller state is exactly:

```powershell
$script:ImageToolsMode = 'RemoveBackground'
```

Mode switch handlers may update only settings visibility/content and primary action content. They must not clear or replace Before/After sources, zoom, pan, compare state, preview surface, output folder, or model session. During processing, the inactive mode is disabled and remains visible.

The existing primary button keeps exactly one click handler. Replace only its inline routing block with a call to this additive dispatcher:

```powershell
function Invoke-ImageToolsPrimaryAction {
    if ($script:ImageToolsMode -eq 'RemoveBackground') {
        switch ($script:RemoveBackgroundState) {
            'Processing'  { Stop-RemoveBackgroundProcessing }
            'Result'      { Save-RemoveBackgroundResult }
            'ImageLoaded' { Start-RemoveBackgroundProcessing }
            'Error'       { Start-RemoveBackgroundProcessing }
        }
        return
    }

    switch ($script:RemoveBackgroundState) {
        'Processing'  { Stop-ImageToolsUpscaleProcessing }
        'Result'      { Save-ImageToolsUpscaleResult }
        'ImageLoaded' { Start-ImageToolsUpscaleProcessing }
        'Error'       { Start-ImageToolsUpscaleProcessing }
    }
}
```

The `RemoveBackground` branch above is an exact call-for-call copy of the current handler behavior. No second click handler is permitted.

- [ ] **Step 5: Reuse the existing managers**

Keep the baseline function bodies captured in Task 0 byte-identical. AI Upscale may call existing functions when their behavior is task-neutral, including preview geometry, temporary result ownership, mouse/zoom state, async cancellation fields, and output-folder control access. Where presentation or output naming is task-specific, add a mode dispatcher or an Upscale strategy beside the existing function; the Remove Background branch must call the unchanged existing function. Do not insert mode branches into the frozen Remove Background function bodies.

The only approved new task-specific workflow names are:

```powershell
Set-ImageToolsMode
Invoke-ImageToolsPrimaryAction
Start-ImageToolsUpscaleProcessing
Stop-ImageToolsUpscaleProcessing
Set-ImageToolsUpscaleProgressPresentation
Save-ImageToolsUpscaleResult
```

If these additive functions cannot integrate while the Task 0 Remove Background function-body hashes and preview-subtree hash remain unchanged, stop before widening the edit.

- [ ] **Step 6: Run workflow and Remove Background regression tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImageToolsWorkflow.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackground.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackgroundEngine.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackgroundWorkflow.Tests.ps1
```

Expected: Image Tools workflow PASS; Remove Background suites match the approved Existing Baseline Failures exactly.

### Task 4: Rename the Visible Module Without Rebuilding Navigation

**Files:**
- Modify: `Views\MainWindow.xaml`
- Modify: `ManajemenFile.ps1`

**Interfaces:**
- Consumes: existing internal `RemoveBackground` ModuleHost key.
- Produces: visible label/title/tooltip `Image Tools`; cached module identity remains stable.

- [ ] **Step 1: Write visible-name assertions in the architecture test**

Assert the sidebar text, tooltip map, and title switch return `Image Tools`, while there remains exactly one cached registry entry for the workspace.

- [ ] **Step 2: Run architecture test and prove RED**

Expected: FAIL on visible naming only.

- [ ] **Step 3: Change only visible strings**

Keep the internal navigation tag and registry key `RemoveBackground` for compatibility. Change only visible `Remove Background` module-name strings to `Image Tools`; do not change ModuleHost.

- [ ] **Step 4: Run architecture test and prove GREEN**

Expected: all architecture assertions PASS.

### Task 5: Request Code Review

**Files:** Read-only review of every changed file.

**Required sub-skill:** `superpowers:requesting-code-review`.

- [ ] **Step 1: Review scope and architecture**

Reviewer must check:

```text
No second view/module/preview/settings/progress/output/process component
No duplicate event handler
No Remove Background behavior change
No new provider abstraction beyond the existing AIEngine ONNX boundary
No unsupported slider value
No technical advanced settings
No hidden/collapsed inactive mode during processing
No changes outside the conditional file map
```

- [ ] **Step 2: Resolve only verified review findings**

Use `superpowers:receiving-code-review` before applying review feedback. Any requested Remove Background refactor or scope expansion requires new user approval.

### Task 6: Verification Before Completion

**Required sub-skill:** `superpowers:verification-before-completion`.

- [ ] **Step 1: Run syntax, XAML, build, and all scoped tests fresh**

Run PowerShell parser checks for changed `.ps1`, load changed XAML with both Light and Dark ResourceDictionaries, build `AIEngine.csproj`, then run Image Tools and all unchanged Remove Background tests.

- [ ] **Step 2: Run the real application from `ManajemenFile.bat`**

Manual matrix:

```text
Empty state
Import image
Remove Background mode and real result
Switch to AI Upscale without preview/zoom/layout reset
AI Upscale real results at 1x, 2x, 4x, and 10x with exact dimensions
Mode lock during processing
Cancel
Save PNG
Open saved output
Light -> Dark -> Light
Return to Remove Background and rerun
```

- [ ] **Step 3: Audit duplication and frozen behavior**

Search/count one preview, primary action, progress bar, settings panel, ModuleHost registration, controller, and engine provider boundary. Compare Remove Background behavioral test results to the baseline. Any regression is FAIL.

Recompute every Task 0 function-body hash and the `RemoveBackgroundPreview` subtree hash. Expected: all unchanged. A mismatch is FAIL even if UI smoke tests pass.

### Task 7: Freeze

**Files:** No new freeze document unless explicitly requested.

- [ ] **Step 1: Record final hashes in the completion report**

Report hashes for all changed source, binary, model metadata, and model artifact files.

- [ ] **Step 2: Declare status**

Declare Development `STABLE/FROZEN` only if code review has no unresolved findings, all verification is fresh PASS, real Balanced Remove Background and AI Upscale 1x..10x workflows pass, and PNG output is valid. Otherwise report `BLOCKED` or `FAIL` with exact evidence. Development freeze does not declare distribution readiness; release remains blocked until every Release Requirement above is satisfied. Metadata Core remains a separate post-freeze milestone.

---

## Self-Review

- Spec coverage: all UI, architecture, provider, workflow, verification, review, and freeze requirements are mapped.
- Duplication check: the plan explicitly forbids a second view/module/preview/settings/progress/output/process component.
- Simplicity check: ONNX-only v1; no external provider implementation, DI, factory, plugin, cache, queue, or technical settings.
- Type/contract check: the only new engine boundary named in the plan is model-generic `RunImageTransform(string inputPath, int targetScale)` returning the existing `EngineResult`.
- Scale check: UI target scale is integer 1x..10x, native model scale is 4x, and v1 performs no more than one inference and one deterministic final resize.
- Gate B check: the approved artifact, checksum, graph contract, CPU compatibility, and native 4x behavior are fixed; the manually authored v1 metadata must match those facts, while provenance and licensing remain Release Requirements.
- Metadata check: AI Upscale never invokes or changes `Set-RemoveBackgroundModelSlot`, never generates metadata, and never duplicates generator logic; Metadata Core remains outside this plan.
- Prototype exclusion check: the plan contains no prototype audit task and explicitly forbids using `Proyek Upscaler` as evidence or implementation source.
- Freeze check: Gate A authorizes only migration-host XAML plus routing/dispatcher/AI Upscale integration; every captured Remove Background behavior function and preview subtree remains an unchanged baseline.
