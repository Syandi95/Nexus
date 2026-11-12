# [Fase 6] PDF Utilities C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan modul utilitas PDF (`Modules/PdfTools.ps1`) ke C# Library `Nexus.PdfUtilities.csproj` dan xUnit test `Nexus.PdfUtilities.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan paritas fitur 100%, pembacaan halaman, konversi gambar (JPEG/PNG), merge, dan split PDF, 0 warning/error, dan 0 regresi.

**Architecture:** Menerapkan Clean Architecture dengan 3 C# Services (`PdfEngineService`, `PdfConversionService`, `PdfMergeSplitService`) yang mengisolasi penanganan dokumen PDF, konversi visual halaman, serta manipulasi merge & split file PDF.

**Tech Stack:** C# 12 / .NET 8, `System.IO`, `System.Threading.Tasks`, xUnit, `Moq`.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `Modules/PdfTools.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA**.
* 📌 **STATUS MODUL POWERSHELL**: Skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **STANDAR DIBEKUKAN**: Build PASS (0 Warning, 0 Error), Test PASS 100%, Migration Matrix, Regression Matrix, dan Performance Benchmark empiris (bukan estimasi).

---

## 1. MIGRATION MATRIX (6 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Initialize-PdfCraftEngine` | `IPdfEngineService.Initialize()` | `Initialize_EnsuresEngineLoaded` | Candidate for Deprecation |
| `OpenPdf` / `Get-PdfPageCount` | `IPdfEngineService.GetPageCount(path)` | `GetPageCount_ReturnsCorrectCount` | Candidate for Deprecation |
| `Start-PdfConversionAsync` | `IPdfConversionService.ConvertAsync(...)` | `ConvertAsync_RendersPagesToImages` | Candidate for Deprecation |
| `Select-PdfFormatTab` / `Select-PdfDpi` | `PdfConversionOptions` DTO | `PdfConversionOptions_SetsDpiAndFormat` | Candidate for Deprecation |
| `Merge-PdfFilesAsync` | `IPdfMergeSplitService.MergePdfAsync(...)` | `MergePdfAsync_CombinesMultiplePdfs` | Candidate for Deprecation |
| `Split-PdfFileAsync` | `IPdfMergeSplitService.SplitPdfAsync(...)` | `SplitPdfAsync_SplitsDocumentIntoPages` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.PdfUtilities` & `Nexus.PdfUtilities.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.PdfUtilities/Nexus.PdfUtilities.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.PdfUtilities.Tests/Nexus.PdfUtilities.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement PDF Engine Service (`IPdfEngineService`)
- Implement `PdfEngineService` in `Nexus.PdfUtilities/Services/` for PDF document inspection, page count retrieval, and stream management.

### Task 3: Implement PDF Conversion Service (`IPdfConversionService`)
- Implement `PdfConversionService` for page rendering and conversion to JPEG/PNG images with custom DPI and quality options.

### Task 4: Implement PDF Merge & Split Service (`IPdfMergeSplitService`)
- Implement `PdfMergeSplitService` for merging multiple PDF files into one and splitting single PDFs into page files.

### Task 5: Implement xUnit Unit Tests & Verify Quality Gate
- Write unit tests in `Nexus.PdfUtilities.Tests`.
- Execute `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 warnings and 0 errors, and record empirical performance metrics.
