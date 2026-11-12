# [Fase 4] Llama Provider Integration C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan modul integrasi AI Llama (`AI/Providers/LlamaServerProvider.ps1`) ke C# Library `Nexus.AI.Llama.csproj` dan xUnit test `Nexus.AI.Llama.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan manajemen proses sidecar `llama-server.exe`, HTTP SSE Streaming non-blocking (`IAsyncEnumerable<T>`), paritas fitur 100%, 0 warning/error, dan Performance Benchmark terukur.

**Architecture:** Menerapkan Clean Architecture dengan 2 C# Services utama (`LlamaServerManager` untuk siklus hidup proses sidecar & health check, serta `LlamaClientService` berbasis `HttpClient` & `System.Text.Json` untuk streaming SSE `IAsyncEnumerable<LlamaStreamChunk>`).

**Tech Stack:** C# 12 / .NET 8, `System.Net.Http`, `IAsyncEnumerable<T>`, `System.Diagnostics.Process`, xUnit, `Moq`.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `AI/Providers/LlamaServerProvider.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA**.
* 📌 **STATUS MODUL POWERSHELL**: Skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **STANDAR DIBEKUKAN**: Build PASS (0 Warning, 0 Error), Test PASS 100%, Migration Matrix, Regression Matrix, dan Performance Benchmark empiris (bukan estimasi).
* 📌 **FREEZE HANYA SETELAH QUALITY GATE**: Freeze dilakukan HANYA setelah seluruh Quality Gate lulus.

---

## 1. MIGRATION MATRIX (10 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Initialize-LlamaProvider` | `ILlamaServerManager.Initialize(engineDir, port)` | `Initialize_SetsServerUrlAndPath` | Candidate for Deprecation |
| `Get-RecommendedBackend` | `ILlamaServerManager.DetectBackend()` | `DetectBackend_DetectsCudaOrCpu` | Candidate for Deprecation |
| `Get-AILlamaTuningArgs` | `ILlamaServerManager.GetTuningArguments()` | `GetTuningArguments_ReturnsThreadAndBatchArgs` | Candidate for Deprecation |
| `Get-LlamaStartArgs` | `ILlamaServerManager.BuildStartArguments(...)` | `BuildStartArguments_FormatsLlamaCli` | Candidate for Deprecation |
| `Test-LlamaServerReady` | `ILlamaServerManager.IsServerHealthyAsync()` | `IsServerHealthyAsync_PollsHealthEndpoint` | Candidate for Deprecation |
| `Start-LlamaServer` | `ILlamaServerManager.StartServerAsync(...)` | `StartServerAsync_SpawnsSidecarProcess` | Candidate for Deprecation |
| `Stop-LlamaServer` | `ILlamaServerManager.StopServerAsync()` | `StopServerAsync_TerminatesSidecarProcess` | Candidate for Deprecation |
| `Invoke-AIGenerate` | `ILlamaClientService.StreamChatCompletionsAsync(...)`| `StreamChatCompletionsAsync_ParsesSseEvents` | Candidate for Deprecation |
| `Get-AIStreamChunk` | `IAsyncEnumerable<LlamaStreamChunk>` | `StreamChatCompletionsAsync_YieldsChunks` | Candidate for Deprecation |
| `Stop-AIGenerate` | `ILlamaClientService.CancelStreaming()` | `CancelStreaming_CancelsCancellationToken` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.AI.Llama` & `Nexus.AI.Llama.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.AI.Llama/Nexus.AI.Llama.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.AI.Llama.Tests/Nexus.AI.Llama.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement Llama Server Manager Service (`ILlamaServerManager`)
- Implement `LlamaServerManager` for backend detection, argument construction, process spawning, and health polling (`/health`).

### Task 3: Implement Llama SSE Streaming Client Service (`ILlamaClientService`)
- Implement `LlamaClientService` with `HttpClient` consuming `/v1/chat/completions` and streaming chunks via `IAsyncEnumerable<LlamaStreamChunk>`.

### Task 4: Implement xUnit Unit Tests & Verify Performance Benchmark
- Write unit tests using `MockHttpMessageHandler` and mock process runners.
- Run `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 warnings and 0 errors, and record empirical performance metrics.
