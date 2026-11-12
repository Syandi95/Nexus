# [Fase 5] Image Subsystem C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan dan menyatukan subsistem gambar (`Foundation/ImagePipeline.ps1`, `Modules/ImageTools.ps1`, `Modules/RemoveBackground.ps1`) ke C# Library `Nexus.Engine.ImageSubsystem.csproj` dan xUnit test `Nexus.Engine.ImageSubsystem.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` untuk menyelesaikan ketergantungan siklis (*cyclic dependency*) dengan paritas fitur 100%, 0 warning/error, dan 0 regresi.

**Architecture:** Menerapkan Clean Architecture dengan 3 C# Services terdecouple (`ImagePipelineService`, `OnnxModelManager`, `RemoveBackgroundService`) yang mengisolasi state transition pipeline, Protobuf ONNX opset parser, serta model slot configuration (`MVANet`, `BiRefNet_lite`, `RMBG-2.0`).

**Tech Stack:** C# 12 / .NET 8, `System.IO`, `System.Text.Json`, `System.Threading.Tasks`, xUnit, `Moq`.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `Foundation/ImagePipeline.ps1`, `Modules/ImageTools.ps1`, dan `Modules/RemoveBackground.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA**.
* 📌 **STATUS MODUL POWERSHELL**: Seluruh skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **PENYELESAIAN CYCLIC DEPENDENCY**: Pemisahan antarmuka `IImagePipelineService` dan `IRemoveBackgroundService` wajib memastikan tidak ada siklus dependensi antar proyek.
* 📌 **STANDAR DIBEKUKAN**: Build PASS (0 Warning, 0 Error), Test PASS 100%, Migration Matrix, Regression Matrix, dan Performance Benchmark empiris (bukan estimasi).

---

## 1. MIGRATION MATRIX (9 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `New-ImagePipeline` | `IImagePipelineService.CreatePipeline(root)` | `CreatePipeline_InitializesEmptyState` | Candidate for Deprecation |
| `Register-ImagePipelineTask` | `IImagePipelineService.RegisterTask(task)` | `RegisterTask_AddsTaskToRegistry` | Candidate for Deprecation |
| `Set-ImagePipelineInput` | `IImagePipelineService.SetInput(path)` | `SetInput_UpdatesStateToImageLoaded` | Candidate for Deprecation |
| `Test-ImagePipelineSamePath` | `IImagePipelineService.IsSamePath(left, right)` | `IsSamePath_ComparesPathsNormalized` | Candidate for Deprecation |
| `Clear-ImagePipelineOwnedResult`| `IImagePipelineService.ClearResult()` | `ClearResult_CleansTemporaryOutputFiles`| Candidate for Deprecation |
| `Get-RemoveBackgroundOnnxOpset` | `IOnnxModelManager.DetectOpsetVersion(path)` | `DetectOpsetVersion_ParsesOnnxProtobuf` | Candidate for Deprecation |
| `Load-RemoveBackgroundModelSlots`| `IOnnxModelManager.LoadModelSlotsAsync()` | `LoadModelSlotsAsync_ParsesJsonMapping` | Candidate for Deprecation |
| `Save-RemoveBackgroundModelSlots`| `IOnnxModelManager.SaveModelSlotsAsync(slots)` | `SaveModelSlotsAsync_WritesJsonMapping` | Candidate for Deprecation |
| `RemoveBackgroundInference` | `IRemoveBackgroundService.ExecuteInferenceAsync(...)` | `ExecuteInferenceAsync_GeneratesAlphaMask` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.Engine.ImageSubsystem` & `Nexus.Engine.ImageSubsystem.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.Engine.ImageSubsystem/Nexus.Engine.ImageSubsystem.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/Nexus.Engine.ImageSubsystem.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement Image Pipeline Service (`IImagePipelineService`)
- Implement `ImagePipelineService` in `Nexus.Engine.ImageSubsystem/Services/` for managing pipeline lifecycle and state transitions.

### Task 3: Implement ONNX Model Manager Service (`IOnnxModelManager`)
- Implement `OnnxModelManager` for Protobuf ONNX opset parsing and slots mapping JSON (`Lite`, `Balanced`, `High Quality`).

### Task 4: Implement RemoveBackground Service (`IRemoveBackgroundService`)
- Implement `RemoveBackgroundService` for background removal execution and output image compositing.

### Task 5: Implement xUnit Unit Tests & Verify Quality Gate
- Write unit tests in `Nexus.Engine.ImageSubsystem.Tests`.
- Execute `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 warnings and 0 errors, and record empirical performance metrics.
