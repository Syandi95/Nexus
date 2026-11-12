# C# Solution (.sln) Architecture Plan & Standard Specifications (FROZEN v1.0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Membekukan arsitektur Solution C# WPF (.NET 8) final (`Nexus.sln`) secara utuh, mencakup 13 aspek standar teknis, 20 proyek terisolasi (10 proyek produksi + 10 proyek pengujian modular), diagram ketergantungan 1 arah (*Clean Layered Architecture*), serta konvensi folder dan namespace dari **Fase 1 hingga Fase 8** agar tidak ada rerestrukturisasi di tengah migrasi.

**Architecture:** Menerapkan *Clean Layered Architecture* & *MVVM Pattern* (Model-View-ViewModel). Seluruh logika domain dan infrastructure dipisahkan ke dalam proyek Class Library independen (`net8.0`), sementara host UI diletakkan di `Nexus.App.csproj` (`net8.0-windows`). Setiap proyek produksi memiliki proyek test padanannya sendiri (*Modular Test Projects*).

**Tech Stack:** C# 12 / .NET 8 SDK, WPF (.NET 8 Windows SDK), `CommunityToolkit.Mvvm` (8.2+), `Microsoft.Extensions.DependencyInjection` (8.0+), `System.Text.Json` (8.0+), xUnit / FluentAssertions.

---

## Global Constraints

* ❌ **DILARANG RESTRUCTURE DI TENGAH MIGRASI**: Struktur Solution yang dibekukan di sini menjadi acuan tetap hingga Fase 8.
* ❌ **DILARANG CIRCULAR DEPENDENCY**: Proyek layer bawah (Foundation, Contracts) TIDAK BOLEH merujuk ke proyek layer atas (Workspace, App Host).
* 📌 **TARGET FRAMEWORK STANDARD**: Proyek non-UI menggunakan TargetFramework `net8.0`. Proyek UI (App Host & View Components) menggunakan `net8.0-windows`.
* 📌 **MODULAR TEST PROJECTS**: Setiap proyek produksi wajib memiliki proyek pengujian xUnit padanannya sendiri (misal: `Nexus.Foundation.Tests`, `Nexus.Conversation.Tests`).
* 📌 **LOKASI UTAMA SOURCE CODE**: Seluruh proyek C# diletakkan di bawah `Nexus-CSharp/src/`.
* 📌 **NULLABLE REFERENCE TYPES ENABLED**: Seluruh proyek C# wajib mengaktifkan `<Nullable>enable</Nullable>` dan `<ImplicitUsings>enable</ImplicitUsings>`.

---

## 1. 13 ASPEK STANDAR ARSITEKTUR SOLUTION C#

### 1.1 Solution Structure (`Nexus.sln`)
File solusi utama diletakkan di `Nexus-CSharp/src/Nexus.sln`. Terbagi menjadi 5 Solution Folders utama di Visual Studio / IDE:
- `00_Contracts`: Model DTO, JSON Schema specifications (`Nexus.Contracts`, `Nexus.Contracts.Tests`).
- `01_Foundation`: Infrastructure core (`Nexus.Foundation`, `Nexus.Foundation.Tests`).
- `02_Modules`: Domain modules & services (`Nexus.Conversation`, `Nexus.AI.EditingLite`, `Nexus.FileUtilities`, `Nexus.AI.Llama`, `Nexus.Engine.ImageSubsystem`, `Nexus.PdfTools`, `Nexus.AI.Workspace` dan test masing-masing).
- `03_Host`: Shell WPF application executable (`Nexus.App`, `Nexus.App.Tests`).
- `04_Tests`: Solution Folder penampung proyek-proyek test modular.

### 1.2 Project Structure (`.csproj`)
Setiap proyek C# menggunakan SDK-style project format modern:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12.0</LangVersion>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

### 1.3 Target Framework (.NET 8)
- Proyek Class Library (Core, Infrastructure, Services, Contracts, Tests): `<TargetFramework>net8.0</TargetFramework>`.
- Proyek UI Application & WPF Views: `<TargetFramework>net8.0-windows</TargetFramework>` dengan `<UseWPF>true</UseWPF>`.

### 1.4 Namespace Convention
Mengikuti standar struktur hierarkis `Nexus.<Layer>.<Subsystem>`:
- `Nexus.Contracts.Models` & `Nexus.Contracts.Tests`
- `Nexus.Foundation.Config` & `Nexus.Foundation.Tests`
- `Nexus.Conversation.Storage` & `Nexus.Conversation.Tests`
- `Nexus.AI.EditingLite` & `Nexus.AI.EditingLite.Tests`
- `Nexus.FileUtilities` & `Nexus.FileUtilities.Tests`
- `Nexus.AI.Llama` & `Nexus.AI.Llama.Tests`
- `Nexus.Engine.ImageSubsystem` & `Nexus.Engine.ImageSubsystem.Tests`
- `Nexus.PdfTools` & `Nexus.PdfTools.Tests`
- `Nexus.AI.Workspace` & `Nexus.AI.Workspace.Tests`
- `Nexus.App` & `Nexus.App.Tests`

### 1.5 Folder Convention
```
Nexus-CSharp/
├── docs/
│   ├── constitution/
│   ├── benchmarks/
│   ├── adr/
│   ├── contracts/
│   └── plans/
└── src/
    ├── 00_Contracts/
    │   ├── Nexus.Contracts/
    │   └── Nexus.Contracts.Tests/
    ├── 01_Foundation/
    │   ├── Nexus.Foundation/
    │   └── Nexus.Foundation.Tests/
    ├── 02_Modules/
    │   ├── Nexus.Conversation/ & Nexus.Conversation.Tests/
    │   ├── Nexus.AI.EditingLite/ & Nexus.AI.EditingLite.Tests/
    │   ├── Nexus.FileUtilities/ & Nexus.FileUtilities.Tests/
    │   ├── Nexus.AI.Llama/ & Nexus.AI.Llama.Tests/
    │   ├── Nexus.Engine.ImageSubsystem/ & Nexus.Engine.ImageSubsystem.Tests/
    │   ├── Nexus.PdfTools/ & Nexus.PdfTools.Tests/
    │   └── Nexus.AI.Workspace/ & Nexus.AI.Workspace.Tests/
    └── 03_Host/
        ├── Nexus.App/                          # Executable Nexus.exe Host
        └── Nexus.App.Tests/
```

---

## 2. DAFTAR 20 PROYEK C# & ALASAN PEMISAHAN RASIONAL

| ID | Nama Proyek Produksi C# | Proyek Test Modular Padanan | Target Framework | Fase Dibuat | Alasan Pemisahan Arsitektur |
| :---: | :--- | :--- | :---: | :---: | :--- |
| **P-01** | `Nexus.Contracts.csproj` | `Nexus.Contracts.Tests.csproj` | `net8.0` | Fase 0B / 1 | DTO imutabel (`record`) & skema JSON tanpa dependency internal. |
| **P-02** | `Nexus.Foundation.csproj` | `Nexus.Foundation.Tests.csproj` | `net8.0` | **Fase 1** | Infrastruktur dasar (Config, CrashLogger, EngineState, Utilities). |
| **P-03** | `Nexus.Conversation.csproj` | `Nexus.Conversation.Tests.csproj` | `net8.0` | Fase 2 | Mengisolasi domain riwayat percakapan (Storage, Index, Manager). |
| **P-04** | `Nexus.AI.EditingLite.csproj` | `Nexus.AI.EditingLite.Tests.csproj` | `net8.0` | Fase 3A | Mengisolasi modul *stateless* AI Editing (Workflow, Proposal, Validator). |
| **P-05** | `Nexus.FileUtilities.csproj` | `Nexus.FileUtilities.Tests.csproj` | `net8.0` | Fase 3B | Mengisolasi pengolahan file (CleanDownloads & RapikanFile). |
| **P-06** | `Nexus.AI.Llama.csproj` | `Nexus.AI.Llama.Tests.csproj` | `net8.0` | Fase 4 | Mengisolasi HTTP SSE Client & streaming I/O ke LlamaServer. |
| **P-07** | `Nexus.Engine.ImageSubsystem.csproj` | `Nexus.Engine.ImageSubsystem.Tests.csproj` | `net8.0` | Fase 5 | Menggabungkan AIEngine, ImagePipeline, & RemoveBG (Cyclic fix). |
| **P-08** | `Nexus.PdfTools.csproj` | `Nexus.PdfTools.Tests.csproj` | `net8.0` | Fase 6 | Mengisolasi pemrosesan PDF & Magick.NET rasterizer. |
| **P-09** | `Nexus.AI.Workspace.csproj` | `Nexus.AI.Workspace.Tests.csproj` | `net8.0-windows` | Fase 7 | 5 ViewModels/Services hasil decoupling `AIWorkspace.ps1` (SRP). |
| **P-10** | `Nexus.App.csproj` | `Nexus.App.Tests.csproj` | `net8.0-windows` | Fase 8 | Executable host `Nexus.exe` (WPF Shell, Views, DI Bootstrapper). |

---

## TASK DECOMPOSITION FOR SCAFFOLDING & FASE 1

### Task 1: Create Solution File & Phase 1 Project Scaffolding
- Create `Nexus-CSharp/src/Nexus.sln`
- Create `Nexus-CSharp/src/00_Contracts/Nexus.Contracts/Nexus.Contracts.csproj`
- Create `Nexus-CSharp/src/00_Contracts/Nexus.Contracts.Tests/Nexus.Contracts.Tests.csproj`
- Create `Nexus-CSharp/src/01_Foundation/Nexus.Foundation/Nexus.Foundation.csproj`
- Create `Nexus-CSharp/src/01_Foundation/Nexus.Foundation.Tests/Nexus.Foundation.Tests.csproj`
- Add projects to `Nexus.sln` and establish references (`Nexus.Foundation` -> `Nexus.Contracts`).
- Verify `dotnet build Nexus-CSharp/src/Nexus.sln` compiles with 0 errors.

### Task 2: Implement Phase 1 Foundation Core C# Services
- Implement `IConfigService` & `ConfigService` (`Nexus.Foundation/Config/`)
- Implement `ILoggerService` & `CrashLoggerService` (`Nexus.Foundation/Logging/`)
- Implement `IEngineStateService` & `EngineStateService` (`Nexus.Foundation/State/`)
- Implement `IFileSystemService` & `FileSystemService` (`Nexus.Foundation/Utils/`)

### Task 3: Implement Phase 1 Unit Tests & Verify Regression Gate
- Write xUnit unit tests in `Nexus.Foundation.Tests` for Config, Logging, EngineState, and FileSystem services.
- Execute `dotnet test Nexus-CSharp/src/01_Foundation/Nexus.Foundation.Tests/Nexus.Foundation.Tests.csproj`.
- Relocate PowerShell foundation scripts (`Foundation/*.ps1`) to `Deprecated/Foundation/` as per Stage 2 Deprecation procedure.

---

## DEFINITION OF DONE (DoD) FASE 1

Fase 1 dinyatakan **DONE** apabila:
1. `Nexus.Foundation.dll` terkompilasi 100% tanpa compiler warnings/errors (`net8.0`).
2. Unit tests di `Nexus.Foundation.Tests` lulus 100%.
3. File `Foundation/*.ps1` lama dipindahkan ke `Deprecated/Foundation/`.
