# Image Processing Shared Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This plan supersedes only the process-architecture portion of `2026-07-19-image-tools-v1.md`; it does not authorize Image Tools UI work.

**Goal:** Move image-processing lifecycle ownership from Remove Background into one generic pipeline consumed through the same contract by Remove Background and AI Upscale, while preserving every observable Remove Background behavior.

**Architecture:** `Foundation/ImagePipeline.ps1` becomes the single owner of pipeline state, asynchronous process management, progress-stage sequencing, cancellation, engine lifetime, temporary results, output copy, and deactivate cleanup. It sees only generic provider, task-descriptor, and ViewAdapter contracts. `Modules/ImageTools.ps1` supplies one ONNX provider adapter and registers task descriptors; concrete WPF presentation remains outside Foundation in workspace presenter functions. `Modules/RemoveBackground.ps1` becomes a behavioral consumer that supplies the existing model selection, presentation data, preview behavior, and filename policy without owning pipeline infrastructure. The existing ModuleHost key, view, controls, and user workflow remain unchanged.

**Tech Stack:** Windows PowerShell 5.1, WPF DispatcherTimer, Nexus ModuleHost, C# .NET Framework 4.8 `Nexus.AI.AIEngine`, Microsoft ONNX Runtime CPU, Pester-free PowerShell assertion scripts.

## Plan Status

`READY FOR EXECUTING PLANS — BEHAVIORAL FREEZE — NO UI WORK`

## Global Constraints

- Freeze type is `Behavioral Freeze`, not source-code freeze.
- Internal Remove Background changes are allowed only to transfer architectural ownership into the shared pipeline.
- Remove Background inference pixels, state transitions, workflow, visible UI, shortcuts, progress timing/presentation, cancellation, preview, save, output naming, and lifecycle behavior must remain identical.
- Do not modify `Views/Modules/RemoveBackgroundView.xaml`, theme resources, shortcuts, engine inference/postprocessing, model artifacts, or metadata.
- There must be exactly one pipeline, process manager, progress manager, cancellation manager, preview handoff, temporary-result owner, output manager, and engine-lifetime owner.
- Remove Background is a task consumer and compatibility adapter; it must not retain parallel async/process/output ownership after migration.
- AI Upscale uses the same `Register-ImagePipelineTask` and `Start-ImagePipeline` contracts as Remove Background.
- Pipeline code must not contain `RemoveBackground`, `RealESRGAN`, control names, localized UI messages, preset names, filename suffixes, or task-specific engine method names.
- The ViewAdapter contract is task-blind and control-blind. Its callbacks accept generic event objects only; callback signatures must not include `TaskId`, preset/model names, control names, or task-specific options.
- Concrete WPF control lookup and mutation belongs to workspace presenter functions outside `Foundation/ImagePipeline.ps1` and outside the ViewAdapter object. The ViewAdapter may call those generic presenter functions but may not contain control lookup, task switches, or task-specific branches itself.
- The pipeline knows only the provider contract. Task-specific engine dispatch belongs to the provider adapter.
- Do not create a second preview, settings panel, progress bar, output path control, worker, timer, cancellation token, or engine cache.
- Do not refactor Metadata Core. Do not call, change, extract, or duplicate `Set-RemoveBackgroundModelSlot`.
- Do not implement Image Tools mode buttons or settings UI in this plan. UI integration resumes only after this pipeline reaches `STABLE/FROZEN`.
- Existing AI Upscale development package remains `Models/Upscale/RealESR General WDN x4 v3` with manual `metadata.json`; no download or generator is permitted.
- AI Upscale scale contract remains: integer `1x..10x`, `1x` bypass, one native `4x` inference for `2x..10x`, then at most one bicubic resize.
- The repository is mostly untracked; do not commit unless Git provenance becomes safe. Record hashes and verification output instead.

---

## Current Architecture Audit

### Current ownership

`Modules/RemoveBackground.ps1` currently owns all layers at once:

- controller state and WPF control access;
- input validation and bitmap import;
- preview, compare, zoom, pan, and background presentation;
- model-slot selection and dependency validation;
- engine creation/reuse/disposal;
- PowerShell worker and `DispatcherTimer` polling;
- progress-stage sequencing;
- cancellation and deactivate suppression;
- temporary result ownership;
- user save/output path and last-saved path.

The architectural choke points are:

- `Start-RemoveBackgroundProcessing` — model request, worker, provider invocation, progress, result handoff, cleanup;
- `Stop-RemoveBackgroundProcessing` — cancellation owner;
- `Clear-RemoveBackgroundTemporaryResult` — temporary-result owner;
- `Save-RemoveBackgroundResult` — output manager;
- `Deactivate-RemoveBackgroundModule` — cancellation suppression and engine lifetime;
- `$script:RemoveBackgroundAsync*`, `$script:RemoveBackgroundEngine`, `$script:RemoveBackgroundResultPath`, `$script:RemoveBackgroundLastSavedPath`, and `$script:RemoveBackgroundProgressStage` — duplicated ownership risk.

`Engine/AIEngine/AIEngine.cs` already exposes the provider capabilities required by this plan:

```csharp
public void LoadModel(string directory)
public EngineResult Run(string inputPath, string preset)
public EngineResult RunImageTransform(string inputPath, int targetScale)
public void Cancel()
public void Dispose()
```

This plan does not change those methods. The provider adapter hides their task-specific dispatch from the pipeline.

### Dependency Matrix

| Concern | Current owner | Target owner | Task-specific input | Observable guard |
|---|---|---|---|---|
| State machine | `RemoveBackground.ps1` | `ImagePipeline` context | View adapter maps state to current controls | Exact state sequence and button states |
| Worker/runspace | `Start-RemoveBackgroundProcessing` | `Start-ImagePipeline` | Provider request | One worker and one timer maximum |
| Engine creation/reuse | Remove Background start/deactivate | ONNX provider + pipeline context | Model directory | Existing reuse/disposal timing |
| Progress stage polling | Remove Background timer | `ImagePipeline` | View adapter presentation map | 150 ms polling; same stages/messages/values |
| Completion delay | Remove Background completion timer | `ImagePipeline` | View adapter `Done` callback | Existing 260 ms delay |
| Cancellation | Remove Background async state | `Stop-ImagePipeline` | Provider `Cancel` | Same disabled primary action and terminal state |
| Deactivate suppression | `Deactivate-RemoveBackgroundModule` | `Deactivate-ImagePipeline` | View adapter state fallback | No stale UI result; deferred disposal |
| Temporary output | Remove Background script path | Pipeline context | Result path from provider | Reprocess/clear/deactivate deletion parity |
| Preview handoff | Timer completion block | Generic ViewAdapter event + one workspace presenter outside Foundation | Generic capabilities and output path | Same After source, compare ratio, geometry |
| Save/copy | `Save-RemoveBackgroundResult` | `Save-ImagePipelineResult` | Task output-path factory | Same suffix, collision policy, status, last path |
| Input bitmap and geometry | Remove Background UI functions | One workspace presenter outside Foundation | Generic input/result events | Pixel source, zoom, compare, pan unchanged |
| Model selection | Remove Background slots | Remove Background task descriptor | Preset/model directory | Same default and selected model |
| Upscale model/scale | Not routed by UI | Upscale task descriptor | Approved directory and `targetScale` | Same generic pipeline request contract |
| Metadata | Existing package mechanisms | Unchanged | Manual Upscale metadata | No generator or Metadata Core change |

### Generic pipeline boundary

Create one context with these exact public fields:

```powershell
[pscustomobject]@{
    State          = 'Empty'
    InputPath      = $null
    ResultPath     = $null
    LastSavedPath  = $null
    ActiveTaskId   = $null
    ActiveOptions  = @{}
    ProgressStage  = $null
    Engine         = $null
    AsyncPowerShell= $null
    AsyncResult    = $null
    AsyncTimer     = $null
    AsyncState     = $null
    IsDeactivated  = $false
    Provider       = $Provider
    ViewAdapter    = $ViewAdapter
    Tasks          = @{}
}
```

Public pipeline commands are frozen as:

```powershell
New-ImagePipeline
Register-ImagePipelineTask
Set-ImagePipelineInput
Clear-ImagePipelineInput
Start-ImagePipeline
Stop-ImagePipeline
Save-ImagePipelineResult
Activate-ImagePipeline
Deactivate-ImagePipeline
Get-ImagePipelineSnapshot
```

Every task descriptor uses the same shape:

```powershell
[pscustomobject]@{
    Id                = 'remove-background'
    BuildRequest      = $BuildRequest
    CreateOutputPath  = $CreateOutputPath
    Capabilities      = @{ Compare = $true; Transparent = $true }
    Presentation      = $Presentation
}
```

`BuildRequest` must accept `(InputPath, Options)` and return the provider request. `CreateOutputPath` must accept `(InputPath, OutputDirectory, Options)` and return one collision-free final path. The pipeline stores the accepted options in `ActiveOptions` so processing and save use the same immutable scale/preset selection.

Every provider uses the same shape:

```powershell
[pscustomobject]@{
    Create  = $CreateEngine
    Execute = $ExecuteRequest
    Cancel  = $CancelEngine
    Dispose = $DisposeEngine
}
```

The single ViewAdapter is only a generic event sink. It uses these exact callback signatures:

```powershell
[pscustomobject]@{
    SetState       = { param($StateEvent) }
    SetProgress    = { param($ProgressEvent) }
    SetStatus      = { param($StatusEvent) }
    PresentResult  = { param($ResultEvent) }
    ClearResult    = { param($ClearEvent) }
    PresentSaved   = { param($SavedEvent) }
}
```

Generic event schemas are frozen as:

```powershell
$StateEvent    = [pscustomobject]@{ State = 'Processing'; Presentation = $presentation }
$ProgressEvent = [pscustomobject]@{ Stage = 'LoadingModel'; Value = 18; Message = $message; Detail = $detail }
$StatusEvent   = [pscustomobject]@{ Kind = 'Error'; Message = $message; Detail = $detail }
$ResultEvent   = [pscustomobject]@{ OutputPath = $path; ExecutionTimeMs = 0; Capabilities = $capabilities }
$ClearEvent    = [pscustomobject]@{ PreviousResultPath = $path; ResetInput = $true }
$SavedEvent    = [pscustomobject]@{ Path = $path; Message = $message; Detail = $detail }
```

Task descriptors provide the presentation data and generic capabilities before the event reaches the ViewAdapter. The adapter does not receive `TaskId` and does not choose presentation by task. Concrete functions such as `Set-ImageWorkspaceState`, `Set-ImageWorkspaceProgress`, and `Present-ImageWorkspaceResult` may know WPF controls, but they live in the module presenter layer and consume only the generic event objects above.

`Modules/ImageTools.ps1` constructs the adapter without control or task knowledge:

```powershell
function New-ImageToolsViewAdapter {
    [pscustomobject]@{
        SetState      = { param($event) Set-ImageWorkspaceState -Event $event }
        SetProgress   = { param($event) Set-ImageWorkspaceProgress -Event $event }
        SetStatus     = { param($event) Set-ImageWorkspaceStatus -Event $event }
        PresentResult = { param($event) Present-ImageWorkspaceResult -Event $event }
        ClearResult   = { param($event) Clear-ImageWorkspaceResult -Event $event }
        PresentSaved  = { param($event) Present-ImageWorkspaceSaved -Event $event }
    }
}
```

The six `*-ImageWorkspace*` presenter functions live outside Foundation and are the only layer allowed to resolve or mutate WPF controls. They must not own worker, timer, engine, cancellation, temporary-file, or output-copy state.

---

## Existing Baseline Failures

These are approved pre-existing failures. Their identities and counts must remain exact after every task:

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

After every task, run the fingerprint command from Task 0. Any added, removed, or renamed failure is an immediate stop.

---

### Task 0: Capture Behavioral Golden Master

**Files:**
- Create: `tests/ImagePipelineBehavior.Tests.ps1`
- Read-only: `Modules/RemoveBackground.ps1`
- Read-only: `Views/Modules/RemoveBackgroundView.xaml`

**Interfaces:**
- Consumes: current Remove Background runtime behavior and approved failure fingerprint.
- Produces: behavioral assertions used after every extraction step; no implementation change.

- [ ] **Step 1: Record source and artifact hashes**

Run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath `
  '.\Modules\RemoveBackground.ps1',
  '.\Views\Modules\RemoveBackgroundView.xaml',
  '.\Engine\AIEngine.dll',
  '.\Models\Upscale\RealESR General WDN x4 v3\metadata.json'
```

Expected: files exist. The XAML hash remains fixed for every task in this plan.

- [ ] **Step 2: Write characterization assertions before refactoring**

`tests/ImagePipelineBehavior.Tests.ps1` must load the real view/module and assert:

```text
Empty: drop zone visible; preview collapsed; primary disabled
ImageLoaded: preview visible; Hapus Background enabled; result absent
Processing: progress visible; primary text Batalkan; settings and replacement locked
Progress: LoadingModel=18, ProcessingImage=58, PreparingResult=86, Done=100
Progress polling interval=150 ms; completion delay=260 ms
Success: After visible; compare ratio=0.5; state Result; primary text Simpan PNG
Cancel: state ImageLoaded; status Proses dibatalkan; temporary result absent
Deactivate: result suppressed; engine disposed only after worker terminal cleanup
Save: unique -no-bg PNG path; LastSavedPath equals copied file
Clear: input/result/last saved reset; original file untouched
```

- [ ] **Step 3: Run characterization and existing suites**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\ImagePipelineBehavior.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackground.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackgroundEngine.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\tests\RemoveBackgroundWorkflow.Tests.ps1
```

Expected: characterization PASS; existing suites match the approved fingerprint exactly.

### Task 1: Introduce the Generic Pipeline Contract Without Routing Production

**Files:**
- Create: `Foundation/ImagePipeline.ps1`
- Create: `tests/ImagePipeline.Tests.ps1`
- Modify: `ManajemenFile.ps1` only to dot-source the foundation before image modules

**Interfaces:**
- Consumes: provider/view/task shapes defined above.
- Produces: pipeline construction, task registration, input state, and snapshot APIs; production Remove Background still uses its old path.

- [ ] **Step 1: Write failing contract tests**

Tests must prove:

```powershell
$pipeline = New-ImagePipeline -ApplicationRoot $root -Provider $fakeProvider -ViewAdapter $fakeView
Register-ImagePipelineTask -Pipeline $pipeline -Task $task
Set-ImagePipelineInput -Pipeline $pipeline -Path $input
$snapshot = Get-ImagePipelineSnapshot -Pipeline $pipeline
Assert-Rule 'Task registered once' ($snapshot.TaskIds -contains 'test-task')
Assert-Rule 'Duplicate task rejected' $duplicateRejected
Assert-Rule 'Input produces ImageLoaded' ($snapshot.State -eq 'ImageLoaded')
Assert-Rule 'No WPF or task names in foundation' ($foundationText -notmatch 'RemoveBackground|RealESRGAN|Btn|Img|Lbl|Cbo|Thumb')
Assert-Rule 'ViewAdapter callbacks are task blind' ($foundationText -notmatch 'ViewAdapter\.[A-Za-z]+[^\r\n]*TaskId')
```

- [ ] **Step 2: Prove RED**

Run `tests/ImagePipeline.Tests.ps1`. Expected: FAIL because `Foundation/ImagePipeline.ps1` does not exist.

- [ ] **Step 3: Implement only construction, registration, input, and snapshot**

Use `PSCustomObject` and functions; do not introduce classes, DI, factory, event bus, or service locator. `Set-ImagePipelineInput` updates only `InputPath`, `State`, and the view adapter state callback.

- [ ] **Step 4: Dot-source foundation in load order**

Add exactly one line before `Modules/RemoveBackground.ps1` is loaded:

```powershell
. "$PSScriptRoot\Foundation\ImagePipeline.ps1"
```

- [ ] **Step 5: Run task tests and regression checkpoint**

Expected: `ImagePipeline.Tests.ps1` PASS; characterization PASS; approved baseline fingerprint unchanged; XAML hash unchanged.

### Task 2: Move Worker, Provider, and Cancellation Ownership

**Files:**
- Modify: `Foundation/ImagePipeline.ps1`
- Create: `Modules/ImageTools.ps1`
- Modify: `Modules/RemoveBackground.ps1`
- Modify: `ManajemenFile.ps1` to dot-source `Modules/ImageTools.ps1`
- Modify: `tests/ImagePipeline.Tests.ps1`
- Modify: `tests/ImagePipelineBehavior.Tests.ps1`

**Interfaces:**
- Consumes: registered task descriptor, provider, view adapter, and valid input.
- Produces: `Start-ImagePipeline`, `Stop-ImagePipeline`, `Activate-ImagePipeline`, and `Deactivate-ImagePipeline` with one worker/engine/cancellation owner.

- [ ] **Step 1: Add failing worker/cancellation tests with a fake provider**

Prove:

```text
Start rejects Empty state and unknown task
Only one operation can run
Provider Create occurs once and engine is reused
Provider Execute receives the task-built request
Stop calls Provider.Cancel once
Deactivate sets SuppressResult and DisposeWhenComplete
Suppressed success output is deleted
Worker terminal cleanup clears AsyncPowerShell/Result/Timer/State
No second timer or cancellation object exists
```

- [ ] **Step 2: Prove RED**

Expected: tests fail because process functions are absent.

- [ ] **Step 3: Implement the single asynchronous manager**

Move the existing behavior into `Start-ImagePipeline` without task names. Preserve these exact mechanics:

```powershell
$timer.Interval = [TimeSpan]::FromMilliseconds(150)
$asyncState = [hashtable]::Synchronized(@{
    Engine = $null
    CancelRequested = $false
    DisposeWhenComplete = $false
    SuppressResult = $false
    ProgressStage = 'LoadingModel'
})
```

The worker invokes only `$Pipeline.Provider.Create` and `$Pipeline.Provider.Execute`. Cancellation invokes only `$Pipeline.Provider.Cancel`; cleanup invokes only `$Pipeline.Provider.Dispose`.

- [ ] **Step 4: Add the ONNX provider adapter**

`Modules/ImageTools.ps1` creates one provider whose `Execute` consumes the same request shape for both tasks:

```powershell
switch ($Request.Operation) {
    'remove-background' { $Engine.LoadModel($Request.ModelDirectory); $Engine.Run($Request.InputPath, $Request.Options.Preset) }
    'image-upscale'     { $Engine.LoadModel($Request.ModelDirectory); $Engine.RunImageTransform($Request.InputPath, $Request.Options.TargetScale) }
    default             { throw "Unsupported image operation: $($Request.Operation)" }
}
```

This switch is provider-internal. The pipeline public API remains unchanged for future tasks.

- [ ] **Step 5: Convert Remove Background start/stop/deactivate into compatibility adapters**

Keep the public function names used by current handlers, but their only process responsibilities become:

```powershell
function Start-RemoveBackgroundProcessing {
    $quality = Get-RemoveBackgroundControl 'CboRemoveBackgroundQuality'
    $preset = if ($quality -and $quality.SelectedItem) { [string]$quality.SelectedItem.Content } else { 'Balanced' }
    Start-ImagePipeline -Pipeline $script:ImageToolsPipeline -TaskId 'remove-background' -Options @{ Preset = $preset }
}
function Stop-RemoveBackgroundProcessing {
    Stop-ImagePipeline -Pipeline $script:ImageToolsPipeline
}
```

Model-slot resolution remains in the Remove Background task descriptor. `Deactivate-RemoveBackgroundModule` delegates lifecycle ownership to `Deactivate-ImagePipeline` and retains only mouse-capture/view cleanup.

- [ ] **Step 6: Delete legacy ownership only after GREEN**

Remove the old `$script:RemoveBackgroundAsyncPowerShell`, `AsyncResult`, `AsyncTimer`, `AsyncState`, and engine-lifetime writes after tests prove the pipeline owns them. Do not leave mirrored compatibility state.

- [ ] **Step 7: Run regression checkpoint**

Expected: pipeline tests PASS; characterization PASS; real Remove Background workflow reaches Result; approved baseline fingerprint unchanged; XAML hash unchanged.

### Task 3: Move Progress and Result/Preview Handoff

**Files:**
- Modify: `Foundation/ImagePipeline.ps1`
- Modify: `Modules/ImageTools.ps1`
- Modify: `Modules/RemoveBackground.ps1`
- Modify: `tests/ImagePipeline.Tests.ps1`
- Modify: `tests/ImagePipelineBehavior.Tests.ps1`

**Interfaces:**
- Consumes: worker progress/result and one shared view adapter.
- Produces: one progress manager and one result-to-preview handoff for every task.

- [ ] **Step 1: Add failing stage/result tests**

Assert exact ordered callbacks:

```text
SetState(Processing)
SetProgress(LoadingModel)
SetProgress(ProcessingImage)
SetProgress(PreparingResult)
PresentResult
SetState(Result)
SetProgress(Done) after 260 ms
```

Assert cancelled and failed results do not call `PresentResult`.

- [ ] **Step 2: Move stage sequencing into pipeline**

The pipeline owns stage deduplication and timers. It calls `$Pipeline.ViewAdapter.SetProgress.Invoke($ProgressEvent)` using a generic event built from the active task descriptor; Foundation contains no values/messages and the ViewAdapter performs no task selection. The Remove Background descriptor retains the exact presentation table:

```text
LoadingModel=18 / Memuat model...
ProcessingImage=58 / Memproses gambar...
PreparingResult=86 / Menyiapkan hasil...
Done=100 / Selesai
```

- [ ] **Step 3: Move result ownership into pipeline**

On success, set `$Pipeline.ResultPath` before calling `$Pipeline.ViewAdapter.PresentResult.Invoke($ResultEvent)`. The event contains only output path, execution time, and generic capabilities. The workspace presenter outside Foundation preserves bitmap creation, After visibility, compare ratio `0.5`, `Result` state presentation, geometry synchronization, completion detail, and transparent-preview behavior byte-for-byte in effect.

- [ ] **Step 4: Remove legacy progress/result ownership**

Remove `$script:RemoveBackgroundProgressStage` and `$script:RemoveBackgroundResultPath` only after all callers use pipeline state. `Get-RemoveBackgroundDiagnosticState` may expose pipeline values under the existing property names for compatibility.

- [ ] **Step 5: Run regression checkpoint**

Expected: exact progress timing/messages/values; result/compare parity; existing fingerprint unchanged; XAML hash unchanged.

### Task 4: Move Temporary Result and Output Ownership

**Files:**
- Modify: `Foundation/ImagePipeline.ps1`
- Modify: `Modules/ImageTools.ps1`
- Modify: `Modules/RemoveBackground.ps1`
- Modify: `tests/ImagePipeline.Tests.ps1`
- Modify: `tests/ImagePipelineBehavior.Tests.ps1`

**Interfaces:**
- Consumes: pipeline result path, output directory, and task `CreateOutputPath` callback.
- Produces: one temporary-result manager and one output manager shared by all tasks.

- [ ] **Step 1: Add failing output ownership tests**

Prove reprocess, clear, suppressed completion, and deactivate delete temporary results exactly once. Prove save validates the directory, copies the current result, records `LastSavedPath`, never changes the input, and delegates unique naming to the task.

- [ ] **Step 2: Implement generic clear and save**

`Clear-ImagePipelineInput` deletes only the owned temporary result and resets pipeline state. `Save-ImagePipelineResult` calls:

```powershell
$target = & $task.CreateOutputPath $Pipeline.InputPath $OutputDirectory $Pipeline.ActiveOptions
Copy-Item -LiteralPath $Pipeline.ResultPath -Destination $target -ErrorAction Stop
$Pipeline.LastSavedPath = $target
& $Pipeline.ViewAdapter.PresentSaved ([pscustomobject]@{ Path = $target; Message = $task.Presentation.Saved.Message; Detail = $target })
```

- [ ] **Step 3: Preserve Remove Background naming and visible behavior**

The Remove Background descriptor delegates to the unchanged collision policy of `Get-RemoveBackgroundDefaultOutputPath`; visible save status remains `PNG transparan berhasil disimpan.`, Open Folder visibility remains identical, and `Save-RemoveBackgroundResult` becomes a compatibility call into the pipeline.

- [ ] **Step 4: Remove legacy result/output variables**

Remove `$script:RemoveBackgroundLastSavedPath` and direct temporary-file deletion after diagnostic/open-folder callers read the pipeline snapshot. Do not retain mirrored state.

- [ ] **Step 5: Run regression checkpoint**

Expected: save/output/clear/deactivate parity; original checksum unchanged; existing fingerprint unchanged; XAML hash unchanged.

### Task 5: Register Remove Background as a Pure Task Consumer

**Files:**
- Modify: `Modules/ImageTools.ps1`
- Modify: `Modules/RemoveBackground.ps1`
- Modify: `tests/ImagePipelineArchitecture.Tests.ps1`

**Interfaces:**
- Consumes: generic pipeline/provider/view contracts.
- Produces: one registered `remove-background` descriptor and no legacy manager ownership.

- [ ] **Step 1: Write failing ownership assertions**

Assert:

```text
Exactly one New-ImagePipeline call
Exactly one AsyncPowerShell/AsyncResult/AsyncTimer owner, all in Foundation/ImagePipeline.ps1
ViewAdapter callbacks accept only generic event objects and contain no control lookup or task switch
All WPF control names remain outside Foundation and outside the ViewAdapter object
No RemoveBackgroundAsync* variables
No RemoveBackgroundEngine lifecycle variable
No direct BeginInvoke, DispatcherTimer, Cancel, Dispose, Copy-Item, or temporary-result Remove-Item in RemoveBackground.ps1 process functions
Remove Background descriptor uses BuildRequest and CreateOutputPath
Set-RemoveBackgroundModelSlot body and call sites remain unchanged
```

Use the PowerShell AST to extract only `New-ImageToolsViewAdapter`, then enforce:

```powershell
Assert-Rule 'Adapter constructor has no WPF or task branch' (
    $adapterFunctionText -notmatch 'FindName|Get-RemoveBackgroundControl|remove-background|image-upscale|RealESRGAN|switch'
)
```

- [ ] **Step 2: Build the Remove Background descriptor**

`BuildRequest` resolves the same preset and slot mapping and returns:

```powershell
[pscustomobject]@{
    Operation      = 'remove-background'
    ModelDirectory = $modelDirectory
    InputPath      = $InputPath
    Options        = @{ Preset = $preset }
}
```

- [ ] **Step 3: Prove single ownership**

Run architecture searches and runtime diagnostics. Expected: one pipeline context, worker, timer, engine reference, cancellation state, result path, last-saved path, and output copy path.

- [ ] **Step 4: Run regression checkpoint**

Expected: behavioral golden master PASS; real Remove Background PASS; approved baseline fingerprint unchanged; XAML hash unchanged.

### Task 6: Register AI Upscale Through the Identical Pipeline Contract

**Files:**
- Modify: `Modules/ImageTools.ps1`
- Modify: `tests/ImagePipeline.Tests.ps1`
- Modify: `tests/ImageToolsEngine.Tests.ps1` only if required to invoke through the pipeline

**Interfaces:**
- Consumes: approved manual metadata package and existing `RunImageTransform` provider capability.
- Produces: registered `image-upscale` task descriptor using the same pipeline start/progress/cancel/result/output lifecycle. No UI routing is added.

- [ ] **Step 1: Write failing same-contract tests**

Create one valid input and start both task IDs through:

```powershell
Start-ImagePipeline -Pipeline $pipeline -TaskId $taskId -Options $options
```

Assert both requests pass through the same provider, worker, timer, progress callbacks, cancellation state, result owner, and save function.

- [ ] **Step 2: Register the Upscale descriptor**

Its `BuildRequest` returns:

```powershell
[pscustomobject]@{
    Operation      = 'image-upscale'
    ModelDirectory = Join-Path $ApplicationRoot 'Models\Upscale\RealESR General WDN x4 v3'
    InputPath      = $InputPath
    Options        = @{ TargetScale = [int]$Options.TargetScale }
}
```

Validate `TargetScale` as integer `1..10` before starting. The descriptor contains no inference or resize implementation.

- [ ] **Step 3: Add Upscale output naming policy**

Use the frozen name `$baseName-upscaled-$($TargetScale)x.png`; collisions append `-1`, `-2`, and so on before `.png`. The task callback owns only this naming policy; the generic output manager performs the copy.

- [ ] **Step 4: Run real provider tests**

Verify through the pipeline, not direct engine calls only:

```text
1x exact-size PNG
4x native model output
10x exact-size PNG after one native inference and one resize
Cancel returns retryable input state
Temporary outputs are owned and deleted by the same pipeline
```

- [ ] **Step 5: Run regression checkpoint**

Expected: AI Upscale pipeline PASS; Remove Background golden master and existing fingerprint unchanged; XAML/UI unchanged.

### Task 7: Code Review, Runtime Verification, and Architecture Freeze

**Files:** No new documentation. Record evidence in the execution report.

**Interfaces:**
- Consumes: completed shared pipeline and both registered tasks.
- Produces: reviewed `STABLE/FROZEN` shared-pipeline baseline or exact failure report.

- [ ] **Step 1: Request code review**

Use `superpowers:requesting-code-review`. Reviewer must check behavioral parity, single ownership, provider isolation, task-generic naming, absence of metadata changes, absence of UI changes, cleanup on every terminal branch, and no task-specific tokens in Foundation.

- [ ] **Step 2: Run complete automated verification**

Run PowerShell parse checks, pipeline tests, Image Tools engine tests, behavioral golden master, and all three existing Remove Background suites. Expected:

```text
ImagePipeline.Tests.ps1 PASS
ImagePipelineArchitecture.Tests.ps1 PASS
ImagePipelineBehavior.Tests.ps1 PASS
ImageToolsEngine.Tests.ps1 PASS
RemoveBackgroundEngine.Tests.ps1 PASS 24/24
RemoveBackground.Tests.ps1 FAIL exact approved 6/73
RemoveBackgroundWorkflow.Tests.ps1 FAIL exact approved 2/23
New Regression = 0
```

- [ ] **Step 3: Run real application from `ManajemenFile.bat`**

Without changing the UI, verify:

```text
Empty -> import -> processing -> result -> save -> open folder
Progress stages and timing unchanged
Compare, zoom, pan, checkerboard/white/black unchanged
Cancel remains retryable
Deactivate during processing suppresses stale result
Return to module reuses the valid cached view/engine lifecycle
Light -> Dark -> Light unchanged
```

Invoke AI Upscale through the pipeline integration harness at `1x`, `4x`, and `10x`; verify output dimensions and PNG validity. Do not add temporary UI controls.

- [ ] **Step 4: Audit dependency matrix and duplicate owners**

Search for worker/timer/cancellation/result/output ownership. Expected: all generic ownership exists once in `Foundation/ImagePipeline.ps1`; task descriptors and view presentation contain no duplicated manager implementation.

- [ ] **Step 5: Record hashes and freeze**

Record hashes for pipeline, ImageTools controller, Remove Background adapter, main load order, engine binary/source, metadata, tests, and unchanged XAML. Declare `SHARED IMAGE PIPELINE: STABLE/FROZEN` only when review is clear and all evidence above is fresh.

---

## Stop Conditions

Stop immediately and request a new architecture decision if:

- behavioral parity requires changing XAML, visible text, shortcuts, progress values/timing, cancellation, output naming, inference, or preview behavior;
- any approved baseline failure changes identity or count;
- a second worker, timer, cancellation state, preview handoff, output copy, or engine owner is required;
- task-specific logic must enter `Foundation/ImagePipeline.ps1`;
- AI Upscale cannot use the same task/provider/pipeline contract;
- Metadata Core or `Set-RemoveBackgroundModelSlot` would need modification;
- real Remove Background output pixels or original-file checksum differ.

## Self-Review

- Scope coverage: audits current ownership, defines generic/task/view boundaries, supplies dependency matrix, stages extraction, integrates both tasks, and freezes with runtime evidence.
- Behavioral-freeze coverage: every user-visible and lifecycle behavior has an explicit golden-master guard.
- Duplication coverage: ownership searches and fake-provider tests enforce one pipeline/process/progress/cancel/preview/output path.
- Type consistency: every task uses `New/Register/Set/Clear/Start/Stop/Save/Activate/Deactivate/Get-ImagePipelineSnapshot`; provider, task, and view-adapter shapes are identical throughout.
- UI coverage: XAML is explicitly out of scope and hash-guarded.
- Metadata coverage: manual Upscale metadata remains read-only; Metadata Core remains excluded.
- Completeness scan: every public interface and stop condition is explicit.
