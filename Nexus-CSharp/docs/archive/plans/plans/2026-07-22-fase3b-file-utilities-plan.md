# [Fase 3B] Quick Win #2: File Utilities C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan logika pengolahan file (`CleanDownloads.ps1` dan `RapikanFile.ps1`) ke C# Library `Nexus.FileUtilities.csproj` dan xUnit test `Nexus.FileUtilities.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan pemrosesan async I/O modern, eksekusi paralel yang aman, paritas fitur 100%, dan Performance Gate $\ge$ 5x lebih cepat dibanding PowerShell.

**Architecture:** Menerapkan Clean Architecture dengan 4 C# Services terdecouple (`CleanDownloadsService`, `RapikanFileService`, `FileManifestService`, `FileMetadataService`) yang memisahkan pemindaian paralel dari pemindahan serial file dengan jaminan *Atomic Undo Manifest*.

**Tech Stack:** C# 12 / .NET 8, `System.IO`, `System.Threading.Tasks`, `Parallel.ForEachAsync`, `System.Text.Json`, xUnit.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `Modules/CleanDownloads.ps1` dan `RapikanFile.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA** di folder `Modules/`.
* 📌 **STATUS MODUL POWERSHELL**: Seluruh skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **FREEZE HANYA SETELAH QUALITY GATE**: Freeze dilakukan HANYA setelah `dotnet build`, `dotnet test`, Performance Gate, dan seluruh Quality Gate lulus 100%.

---

## 1. ANALISIS I/O ASYNC, PARALEL VS SERIAL

### 1.1 Operasi Async (`async` / `await`):
1. `ScanDirectoryAsync`: Pemindaian direktori non-blocking via `Directory.EnumerateFiles`.
2. `PreScanDirectoryAsync`: Perhitungan statistik kategori secara async.
3. `ExecuteCleanAsync`: Eksekusi pengolahan file CleanDownloads.
4. `ExecuteOrganizeAsync`: Eksekusi 3-fase pengolahan file RapikanFile.
5. `SaveManifestAsync` & `UndoOperationAsync`: Penulisan dan pemulihan manifest async.

### 1.2 Operasi Paralel vs Serial:
* ⚡ **Aman Diparalelkan (`Parallel.ForEachAsync`)**:
  - Pemindaian atribut metadata file (ukuran byte, ekstensi, timestamp).
  - Klasifikasi ekstensi & regex kata kunci (misal `ChatGPT`, `Gemini`, `spanduk`).
  - Pemindahan file fisik antar subdirektori tujuan yang berbeda.
* 🔒 **Harus Tetap Serial (Sequential Thread-Safe)**:
  - Pembuatan struktur direktori subfolder tujuan (`Directory.CreateDirectory`).
  - Penulisan file `undo_manifest.json` (mencegah kondisi *race condition* I/O).
  - Pembaruan stack riwayat undo internal (`ManifestHistory`).

---

## 2. MIGRATION MATRIX (14 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Get-DestinationFolderClean` | `ICleanDownloadsService.GetDestinationSubfolder(file)` | `GetDestinationSubfolder_MapsExtensionsAndKeywords` | Candidate for Deprecation |
| `Scan-CleanDownloads` | `ICleanDownloadsService.ScanDirectoryAsync(rootPath)` | `ScanDirectoryAsync_CategorizesFilesAndSizes` | Candidate for Deprecation |
| `Execute-CleanDownloads` | `ICleanDownloadsService.ExecuteCleanAsync(options)` | `ExecuteCleanAsync_MovesFilesAndWritesManifest` | Candidate for Deprecation |
| `Undo-CleanDownloads` | `ICleanDownloadsService.UndoCleanAsync(manifestPath)` | `UndoCleanAsync_RestoresFilesToOriginalLocation` | Candidate for Deprecation |
| `Get-RapikanDestinationFolder`| `IRapikanFileService.GetCategoryDestination(file)` | `GetCategoryDestination_MapsAll10Categories` | Candidate for Deprecation |
| `Start-RapikanPreScan` | `IRapikanFileService.PreScanDirectoryAsync(folder)` | `PreScanDirectoryAsync_ComputesCategoryStats` | Candidate for Deprecation |
| `Start-RapikanProcess` | `IRapikanFileService.ExecuteOrganizeAsync(options)`| `ExecuteOrganizeAsync_3PhasePipelineExecution` | Candidate for Deprecation |
| `Undo-RapikanProcess` | `IRapikanFileService.UndoOrganizeAsync(manifest)` | `UndoOrganizeAsync_RestoresAllOrganizedFiles` | Candidate for Deprecation |
| `Save-UndoManifest` | `IFileManifestService.SaveManifestAsync(manifest)` | `SaveManifestAsync_WritesAtomicJson` | Candidate for Deprecation |
| `Load-UndoManifest` | `IFileManifestService.LoadManifestAsync(path)` | `LoadManifestAsync_ParsesJsonManifest` | Candidate for Deprecation |
| `Get-AgeText` | `IFileMetadataService.GetAgeText(lastWrite)` | `GetAgeText_FormatsRelativeDateStrings` | Candidate for Deprecation |
| `Format-FileSize` | `IFileMetadataService.FormatFileSize(bytes)` | `FormatFileSize_FormatsBytesToKbMbGb` | Candidate for Deprecation |
| `Lock-Engine` | `IEngineStateService.SetBusy(true)` | `SetBusy_PreventsConcurrentOperations` | Candidate for Deprecation |
| `Unlock-Engine` | `IEngineStateService.SetBusy(false)` | `SetBusy_ReleasesEngineLock` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.FileUtilities` & `Nexus.FileUtilities.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.FileUtilities/Nexus.FileUtilities.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.FileUtilities.Tests/Nexus.FileUtilities.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement File Metadata & Manifest Services
- Implement `IFileMetadataService` & `IFileManifestService` in `Nexus.FileUtilities/Services/`.

### Task 3: Implement CleanDownloads Service (Scanning, Moving, Undo)
- Implement `ICleanDownloadsService` & `CleanDownloadsService` with parallel extension mapping.

### Task 4: Implement RapikanFile Service (3-Phase Pipeline, Pre-Scan, Undo)
- Implement `IRapikanFileService` & `RapikanFileService` with 3-phase execution pipeline.

### Task 5: Implement xUnit Unit Tests & Verify Performance Gate
- Write 15+ xUnit unit tests in `Nexus.FileUtilities.Tests`.
- Execute `dotnet build` & `dotnet test`.
- Verify Performance Gate ($\ge$ 5x faster file scanning/classification benchmark).
