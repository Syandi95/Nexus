# [Fase 7] Home & Settings Core C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan modul dashboard Home (`Modules/Home.ps1`) dan Pengaturan (`Modules/Settings.ps1`) ke C# Library `Nexus.Core.HomeSettings.csproj` dan xUnit test `Nexus.Core.HomeSettings.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan paritas fitur 100%, 0 warning/error, dan 0 regresi.

**Architecture:** Menerapkan Clean Architecture dengan 2 C# Services (`HomeDashboardService`, `SettingsManagerService`) yang mengisolasi pengelolaan riwayat aktivitas dashboard, status runtime engine, serta manajemen preferensi tema dan folder.

**Tech Stack:** C# 12 / .NET 8, `System.IO`, `System.Threading.Tasks`, xUnit, `Moq`.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `Modules/Home.ps1` dan `Modules/Settings.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA**.
* 📌 **STATUS MODUL POWERSHELL**: Skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **STANDAR DIBEKUKAN**: Build PASS (0 Warning, 0 Error), Test PASS 100%, Migration Matrix, Regression Matrix, dan Performance Benchmark empiris (bukan estimasi).

---

## 1. MIGRATION MATRIX (7 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Update-DashboardActivity` | `IHomeDashboardService.UpdateActivityAsync(...)` | `UpdateActivityAsync_PersistsActivityToConfig` | Candidate for Deprecation |
| `Get-HomeDashboardActivity` | `IHomeDashboardService.GetLastActivity()` | `GetLastActivity_ReturnsConfiguredActivity` | Candidate for Deprecation |
| `Set-HomeEngineStatus` | `IHomeDashboardService.SetEngineStatus(status, state)` | `SetEngineStatus_UpdatesRuntimeState` | Candidate for Deprecation |
| `Set-HomeFolderStatus` | `IHomeDashboardService.SetFolderStatus(path)` | `SetFolderStatus_UpdatesActiveFolderPath` | Candidate for Deprecation |
| `Sync-SettingsThemeRadios` | `ISettingsManagerService.GetTheme()` | `GetTheme_ReturnsThemeFromConfig` | Candidate for Deprecation |
| `Apply-SettingsTheme` | `ISettingsManagerService.SaveSettingsAsync(...)` | `SaveSettingsAsync_UpdatesConfigThemeAndPath` | Candidate for Deprecation |
| `Reset-Settings` | `ISettingsManagerService.ResetToDefaultAsync()` | `ResetToDefaultAsync_RestoresDefaultConfig` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.Core.HomeSettings` & `Nexus.Core.HomeSettings.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.Core.HomeSettings/Nexus.Core.HomeSettings.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.Core.HomeSettings.Tests/Nexus.Core.HomeSettings.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement Home Dashboard Service (`IHomeDashboardService`)
- Implement `HomeDashboardService` in `Nexus.Core.HomeSettings/Services/` for dashboard activity history and runtime status tracking.

### Task 3: Implement Settings Manager Service (`ISettingsManagerService`)
- Implement `SettingsManagerService` for managing theme options, download folder preferences, and configuration persistence via `IConfigService`.

### Task 4: Implement xUnit Unit Tests & Verify Quality Gate
- Write unit tests in `Nexus.Core.HomeSettings.Tests`.
- Execute `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 warnings and 0 errors, and record empirical performance metrics.
