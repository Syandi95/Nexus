# NEXUS MIGRATION CONSTITUTION v1.0
**STATUS: OFFICIALLY FROZEN (PEDOMAN RESMI DIBEKUKAN)**  
*Dokumen Pedoman Resmi & Kontrak Migrasi Bertahap (Progressive Migration)*  
*Transformasi Total dari PowerShell 5.1 + WPF Menuju Aplikasi C# WPF (.NET 8) Native Penuh*

---

> **CATATAN TATA KELOLA ARSITEKTUR v1.0**: Dokumen ini telah secara resmi dibekukan sebagai acuan utama seluruh proses migrasi Nexus. Setiap perubahan atau revisi di masa mendatang WAJIB melalui mekanisme **Architectural Decision Record (ADR)** yang didukung oleh bukti teknis empiris pada source code.

---

## 1. MUKADDIMAH & PRINSIP UTAMA (THE 10 FROZEN BASELINES)

Dokumen ini merupakan pedoman arsitektur resmi yang stabil dalam seluruh proses migrasi Nexus. Konstitusi ini didesain untuk menjaga konsistensi jangka panjang dan **hanya dapat direvisi melalui Keputusan Arsitektur Resmi (Architectural Decision Record / ADR) yang didukung oleh bukti teknis empiris pada source code.**

**Tujuan akhir Nexus adalah menjadi aplikasi C# WPF (.NET 8) native sepenuhnya.** PowerShell hanya digunakan sebagai platform transisi sementara selama proses migrasi berlangsung dan akan dihapus total secara bertahap setelah pembuktian stabilitas.

Setiap keputusan teknis, refactoring, maupun pull request wajib tunduk pada 10 Prinsip Baseline yang telah dibekukan berikut:

1. **NO BIG-BANG REWRITE**: Migrasi dilakukan secara bertahap (progressive) per modul/service. Dilarang melakukan perombakan total secara sekaligus yang menghentikan operasional aplikasi.
2. **PHASE-BY-PHASE ISOLATION**: Setiap fase wajib berdiri sendiri sebagai unit fungsional yang dapat dijalankan dan diuji secara independen sebelum fase berikutnya dimulai.
3. **MANDATORY VERIFIABILITY**: Setiap fase wajib dapat diuji (testable) dan dapat langsung digunakan (usable) secara nyata oleh sistem sebelum dinyatakan selesai.
4. **STRICT CRITICAL PATH & BUG REGRESSION GATE**: Seluruh alur utama (*critical path*) dan riwayat bug wajib memiliki pengujian regresi (*Pester Transisi* dan *C# Unit Tests*) yang wajib lulus 100% tanpa kompromi sebelum suatu fase dibekukan (freeze).
5. **TWO-STAGE POWERSHELL DEPRECATION & REMOVAL**: Setelah modul C# baru lulus pengujian regresi, skrip PowerShell padanannya **TIDAK langsung dihapus instan**, melainkan dipindahkan ke folder `Deprecated/`. Penghapusan permanen dari repositori HANYA dilakukan setelah modul C# tersebut melewati minimal 1 (satu) siklus penggunaan nyata tanpa ditemukannya bug kritis berdasarkan bukti stabilitas empiris.
6. **ARCHITECTURAL SINGLE RESPONSIBILITY PRINCIPLE (NO GOD OBJECT)**: Dilarang keras memindahkan skrip God Object ke C# Class tunggal. Aturan ini tidak didasarkan pada batas jumlah baris kode (LOC) arbitrer, melainkan pada prinsip arsitektur: **Setiap ViewModel atau Service wajib hanya memiliki satu tanggung jawab utama (Single Responsibility Principle)**. Apabila sebuah ViewModel mulai menangani beberapa domain terpisah (seperti streaming HTTP, percakapan, attachment, file diff, dan UI presentation sekaligus), maka ViewModel tersebut **WAJIB dipecah** menjadi beberapa Service atau ViewModel independen.
7. **FOUNDATION & QUICK-WINS FIRST**: Fondasi dasar (`Foundation`), sistem percakapan (`Conversation`), dan modul stateless (`AI Editing Lite`, `CleanDownloads`, `RapikanFile`) menjadi prioritas utama migrasi awal.
8. **AI WORKSPACE IS THE FINAL BOTTLENECK**: Modul `AIWorkspace` (3,004 LOC) tetap menjadi fase terakhir pemindahan UI setelah seluruh service pendukung (LlamaProvider, Editing, Conversation, File Services) selesai dimigrasikan dan di-decouple di C#.
9. **UNIFIED IMAGE SUBSYSTEM**: `ImageTools.ps1` dan `RemoveBackground.ps1` diperlakukan sebagai **Satu Subsystem Gambar Tunggal (Image Subsystem)** di C# akibat *cyclic dependency* empiris yang telah dibuktikan pada source code.
10. **SOLE HOST DESTINATION (`Nexus.exe`)**: Target akhir migrasi adalah aplikasi executable tunggal C# WPF (`Nexus.exe`). PowerShell tidak lagi menjadi bagian dari arsitektur akhir Nexus setelah seluruh fase migrasi selesai.

---

## 2. ATURAN INTEGRITAS KODE & DEPRECATION PROCEDURE

### 2.1 Pola Transisi Sementara (Temporary Interop Scaffold)
Selama masa migrasi bertahap, modul yang baru selesai dimigrasikan ke C# dikompilasi menjadi **Class Library Assembly (.NET DLL)** dan dipanggil oleh skrip host transisi PowerShell hingga seluruh modul berpindah 100% ke C#:

```powershell
# Standar Pemuatan C# Assembly pada Modul Transisi Sementara
$assemblyPath = Join-Path $PSScriptRoot "Engine\Nexus.Core.dll"
if (Test-Path $assemblyPath) {
    [void][System.Reflection.Assembly]::LoadFrom($assemblyPath)
} else {
    throw "Fatal: Required C# Assembly 'Nexus.Core.dll' not found."
}
```

### 2.2 Prosedur Resmi Deprecation dan Removal (The 4-Stage Lifecycle)
Prosedur penanganan file skrip PowerShell lama wajib mengikuti alur 4 tahap resmi berikut:

```
[Tahap 1: C# Verification] ──> [Tahap 2: Move to Deprecated/] ──> [Tahap 3: Real-World Cycle] ──> [Tahap 4: Permanent Purge]
 (Unit & Regression Pass)       (Relokasi Skrip PowerShell)        (Verifikasi Stabilitas)       (Hapus Permanen di Git)
```

1. **Tahap 1: C# Implementation & Verification**: Modul C# dibuat dan wajib lulus seluruh *Critical Path Unit Tests* serta *Bug Regression Tests*.
2. **Tahap 2: Relokasi ke `Deprecated/`**: Skrip PowerShell lama padanan modul dipindahkan dari lokasi aktif (`Modules/`, `Foundation/`, `AI/`) ke folder `Deprecated/<OriginalFolder>/`. Kode aktif tidak lagi mengeksekusi skrip lama.
3. **Tahap 3: Penggunaan Nyata (Real-World Usage Cycle)**: Modul C# baru diuji dalam minimal 1 (satu) siklus penggunaan nyata operasional sistem untuk membuktikan tidak ada regresi tersembunyi.
4. **Tahap 4: Penghapusan Permanen Berbasis Bukti (Permanent Purge)**: Skrip di `Deprecated/` dihapus secara permanen dari repositori git HANYA setelah ada bukti empiris stabilitas dari Tahap 3.

### 2.3 Larangan Pola Kode (Forbidden Patterns)
* ❌ **Forbidden**: Menulis logika bisnis baru di PowerShell. Seluruh logika baru wajib ditulis di C#.
* ❌ **Forbidden**: Menggunakan `$script:` atau `$global:` scope variable untuk menyimpan state bisnis baru.
* ❌ **Forbidden**: Menggunakan string-based UI element lookup (`$window.FindName("BtnName")`). Seluruh UI C# wajib menggunakan Data Binding MVVM (`INotifyPropertyChanged`).
* ❌ **Forbidden**: Meng-hardcode nilai warna HSL/Hex di dalam logika kode C#. Seluruh warna visual wajib mengacu pada `ResourceDictionary` XAML (`Themes/`).
* ❌ **Forbidden**: Menggabungkan beberapa domain logika ke dalam 1 ViewModel (Pelanggaran Single Responsibility Principle).
* ❌ **Forbidden**: Menghapus file PowerShell secara langsung tanpa melalui tahap `Deprecated/`.

---

## 3. ROADMAP MIGRASI BERTAHAP & KONTRAK ANTAR FASE

```
┌─────────────────────────────────────────────────────────────────────────┐
│                 ROADMAP MIGRASI BERTAHAP (PROGRESSIVE) NEXUS            │
└─────────────────────────────────────────────────────────────────────────┘
   Fase 0A: Formal Performance Baseline Benchmark (PowerShell v1.7)
   Fase 0B: Contract & Schema Freeze (JSON Specifications)
      │
   Fase 1 : Foundation Core C# Library (Config, Logger, EngineState)
      │
   Fase 2 : Conversation System C# Library (Storage, Index, Manager)
      │
   Fase 3A: Quick Win #1 - AI Editing Lite C# Library (Stateless)
   Fase 3B: Quick Win #2 - File Utility Modules C# (CleanDownloads & RapikanFile)
      │
   Fase 4 : Llama Server Provider C# Service (HttpClient SSE Streamer)
      │
   Fase 5 : Unified Image Subsystem C# (AIEngine.cs + ImagePipeline + RemoveBG)
      │
   Fase 6 : PDF Tools C# Module (Magick.NET Integration)
      │
   Fase 7 : AI Workspace Decoupled ViewModels & Services C# (Bottleneck)
      │
   Fase 8 : C# WPF Shell Host (`Nexus.exe`) & Pemindahan Host ke Deprecated/
```

---

### KONTRAK DETIL PER FASE

#### **Fase 0A: Formal Performance Baseline Benchmark**
* **Aksi Prasyarat Awal**: Sebelum kode C# pertama ditulis, lakukan pengukuran performa resmi terhadap aplikasi Nexus v1.7 PowerShell eksisting.
* **Metrik Terukur**:
  * Cold Startup Latency (ms)
  * Memory Idle Footprint (MB RAM)
  * UI Frame Response & Dispatcher Latency (ms)
* **Output / Deliverable**: Dokumen resmi `docs/benchmarks/powershell_v1.7_baseline.json` sebagai acuan tunggal Exit Criteria.

#### **Fase 0B: Contract & Schema Freeze**
* **Cakupan**: `nexus_data/config.json`, `nexus_data/conversations/*.json`, `AI/last_session.json`, `metadata.json`.
* **Output / Deliverable**: C# Data Transfer Objects (DTO) Records & JSON Schema Specifications.

#### **Fase 1: Foundation Core C# Library**
* **Cakupan**: [`Foundation/ConfigManager.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/ConfigManager.ps1), [`CrashLogger.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/CrashLogger.ps1), [`EngineState.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/EngineState.ps1), [`XamlLoader.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/XamlLoader.ps1).
* **Output / Deliverable**: `Nexus.Foundation.dll` (C# Library).
* **Prosedur Deprecation**: Relokasi skrip `.ps1` di `Foundation/` ke `Deprecated/Foundation/` setelah `Nexus.Foundation.dll` lulus uji regresi.

#### **Fase 2: Conversation System C# Library**
* **Cakupan**: [`Modules/ConversationStorage.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/ConversationStorage.ps1), [`ConversationIndex.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/ConversationIndex.ps1), [`ConversationManager.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/ConversationManager.ps1).
* **Output / Deliverable**: `Nexus.Conversation.dll`.

#### **Fase 3A: Quick Win #1 - AI Editing Lite C# Library**
* **Cakupan**: [`AI/EditingLite/*.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/AI/EditingLite/) (Workflow, Proposal, Validator, FileReader, FileWriter).
* **Output / Deliverable**: `Nexus.AI.EditingLite.dll`.

#### **Fase 3B: Quick Win #2 - File Utility Modules C#**
* **Cakupan**: [`Modules/CleanDownloads.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/CleanDownloads.ps1), [`Modules/RapikanFile.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/RapikanFile.ps1).
* **Output / Deliverable**: `CleanDownloadsViewModel.cs`, `RapikanFileViewModel.cs` & `IFileSystemService`.

#### **Fase 4: Llama Server Provider C# Service**
* **Cakupan**: [`AI/Providers/LlamaServerProvider.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/AI/Providers/LlamaServerProvider.ps1), [`AIContext.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/AI/AIContext.ps1), [`AISessionManager.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/AI/AISessionManager.ps1).
* **Output / Deliverable**: `LlamaClientService.cs` (C# SSE HTTP Streamer).

#### **Fase 5: Unified Image Subsystem C#**
* **Cakupan**: [`Engine/AIEngine/AIEngine.cs`](file:///c:/Users/Syand/3D%20Objects/Nexus/Engine/AIEngine/AIEngine.cs), [`Foundation/ImagePipeline.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/ImagePipeline.ps1), [`Modules/ImageTools.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/ImageTools.ps1), [`Modules/RemoveBackground.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/RemoveBackground.ps1).
* **Output / Deliverable**: `Nexus.Engine.ImageSubsystem.dll` & `RemoveBackgroundViewModel.cs`.

#### **Fase 6: PDF Tools Module C#**
* **Cakupan**: [`Modules/PdfTools.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/PdfTools.ps1).
* **Output / Deliverable**: `PdfToolsViewModel.cs` & Magick.NET C# Service.

#### **Fase 7: AI Workspace Decoupled Porting (Kontrak Arsitektur SRP)**
* **Cakupan**: [`Modules/AIWorkspace.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Modules/AIWorkspace.ps1) (3,004 LOC God Object).
* **KONTRAK ARSITEKTUR WAJIB (SRP DECOUPLING)**:
  Skrip `AIWorkspace.ps1` **WAJIB dipecah** menjadi 5 kelas C# terpisah dengan 1 tanggung jawab murni:
  1. `AIWorkspaceViewModel.cs`: Murni sebagai Layout Coordinator & Tab Navigation.
  2. `ChatMessageListViewModel.cs`: Murni mengelola daftar pesan chat & visual bubble rendering.
  3. `AttachmentManagerService.cs`: Murni mengelola file/folder attachment & token counting.
  4. `DiffViewerService.cs`: Murni mengelola pencitraan code diff & eksekusi patch file.
  5. `LlamaClientService.cs`: Murni mengelola koneksi HTTP SSE streaming ke server LLM.
* **Prasyarat**: Fase 2, 3A, dan 4 Selesai & Frozen.

#### **Fase 8: C# WPF Shell Host (`Nexus.exe`) & Pemindahan Host ke Deprecated/**
* **Cakupan**: [`ManajemenFile.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/ManajemenFile.ps1), [`Foundation/ModuleHost.ps1`](file:///c:/Users/Syand/3D%20Objects/Nexus/Foundation/ModuleHost.ps1), [`Views/MainWindow.xaml`](file:///c:/Users/Syand/3D%20Objects/Nexus/Views/MainWindow.xaml).
* **Output / Deliverable**: Executable Application C# WPF Native (`Nexus.exe`).
* **Aksi Transisi**: Pemindahan skrip `ManajemenFile.ps1` ke `Deprecated/` dan pengaktifan `Nexus.exe` sebagai satu-satunya host aplikasi.

---

## 4. DEFINITION OF DONE (DoD) & STANDAR KUALITAS

Setiap fase **WAJIB** memenuhi seluruh poin kriteria berikut sebelum dapat diproses ke fase berikutnya:

1. **Compilation Gate**: Kode C# terkompilasi 100% tanpa warning kritis (Zero Compiler Warnings on WarningLevel 4).
2. **Pester Test Suite Pass**: Seluruh test suite Pester PowerShell eksisting ([`tests/`](file:///c:/Users/Syand/3D%20Objects/Nexus/tests) - 43 file, 6,127 LOC) yang menguji modul terkait lulus 100% pada fase transisi.
3. **Critical Path & Bug Regression Coverage**:
   - Seluruh alur kritis (*critical path*) logika bisnis C# wajib memiliki unit test otomatis.
   - Seluruh bug yang pernah ditemukan dalam riwayat pengujian/isu wajib memiliki *dedicated regression test* khusus untuk menjamin bug tidak muncul kembali.
   - Kualitas pengujian difokuskan pada ketahanan alur kritis dan pencegahan regresi bug, bukan pada persentase coverage arbitrer.
4. **Zero Script State Drift**: Modul C# baru tidak lagi bergantung pada variabel `$script:` atau `$global:` milik PowerShell.
5. **Architectural Single Responsibility Enforcement**: Setiap ViewModel C# terverifikasi hanya menangani 1 domain tanggung jawab utama.
6. **Relokasi ke Deprecated/**: Skrip PowerShell lama padanan modul dipindahkan ke folder `Deprecated/` (bukan dihapus instan).
7. **Memory & Thread Safety Audit**: Dites tidak mengalami memory leak (terutama pada Bitmap image resources dan HttpClient instances) dan berjalan pada background Thread Pool (`Task.Run`) tanpa memblokir STA Dispatcher UI Thread.
8. **Code Review Gate**: Kode memenuhi standar arsitektur MVVM (Model-View-ViewModel) C# murni.

---

## 5. CHECKLIST BEBEKUAN (FREEZE CHECKLIST)

Sebelum suatu fase dinyatakan **FROZEN (DIBEKUKAN)** dan tidak boleh diubah lagi:

- [ ] Seluruh antarmuka publik (`public interface`) pada C# DLL fase ini telah didokumentasikan dan tidak memiliki *breaking changes*.
- [ ] File skrip PowerShell lama padanan modul ini telah dipindahkan ke folder **`Deprecated/`**.
- [ ] Pengujian regresi otomatis (*Regression Suite*) telah dijalankan dan hasilnya diarsip.
- [ ] DLL dikompilasi dalam mode `Release` dan diletakkan pada folder output terstandar `Engine/Bin/Release/`.
- [ ] Dokumentasi perubahan API telah diperbarui di log migrasi.

---

## 6. REGRESSION CHECKLIST (PENGUJIAN REGRESI)

Setiap penyelesaian fase wajib mengeksekusi pengujian regresi berikut:

1. **Automation Test Execution**:
   ```powershell
   # Eksekusi Pester Suite Uji Integrasi Transisi (selama skrip host sementara masih ada)
   Invoke-Pester -Path .\tests\ -Verbosity Detailed
   ```
2. **C# Native Test Execution**:
   ```bash
   # Eksekusi Test Suite xUnit/NUnit C# Native (Critical Path & Bug Regression Tests)
   dotnet test Engine/Nexus.Tests/Nexus.Tests.csproj
   ```
3. **UI Interoperability Check**:
   - Uji perpindahan modul visual di host UI tanpa melempar NullReferenceException.
   - Uji tema (Light vs Dark) berganti secara dinamis pada modul yang baru dipindahkan.
4. **Data Integrity Check**:
   - Uji integritas file JSON `config.json` dan `conversations/*.json` pasca diproses oleh service C# baru (dipastikan tidak ada corruption / key loss).

---

## 7. RISK REGISTER & STRATEGI MITIGASI

| ID | Risiko Teknis | Dampak | Probabilitas | Strategi Mitigasi Terstruktur |
| :---: | :--- | :---: | :---: | :--- |
| **R-01** | UI Thread Block saat I/O SSE Streaming | High | Medium | Menggunakan `IAsyncEnumerable<string>` & `await Task.Yield()` pada C# HttpClient streaming untuk menjamin STA Thread tidak freeze. |
| **R-02** | Cyclic Dependency Image Subsystem crash saat Interop Transisi | High | Medium | Menggabungkan `ImageTools` & `RemoveBackground` pada Fase 5 sebagai 1 unit C# Library tunggal (`Nexus.Engine.ImageSubsystem.dll`). |
| **R-03** | Memory Leak pada Bitmap Preview Gambar | High | Low | Menerapkan `IDisposable` pattern pada `System.Drawing.Bitmap` dan `System.Windows.Media.Imaging.BitmapSource` di `AIEngine.cs`. |
| **R-04** | Penghapusan Prematur Skrip PowerShell | Medium | Low | Enforce Prosedur 4-Tahap (Bab 2.2): Pindahkan ke `Deprecated/` terlebih dahulu, hapus permanen hanya setelah 1 siklus penggunaan nyata tanpa bug kritis. |
| **R-05** | Regression pada File Parser AI Editing Lite | High | Low | Menjalankan `tests/AIEditingLite.Tests.ps1` (155 LOC Pester) secara paralel dengan xUnit C# Test. |

---

## 8. EXIT CRITERIA & PERFORMANCE BENCHMARKING (KRITERIA KELULUSAN FINAL)

Proses migrasi aplikasi Nexus dari PowerShell 5.1 ke C# WPF dinyatakan **SELESAI KELUAR (FINAL EXIT SUCCESS)** apabila:

1. **Host Tunggal Mandiri**: Executable utama `Nexus.exe` (C# WPF Native .NET 8) berjalan secara mandiri tanpa memerlukan launcher `ManajemenFile.ps1` atau dependency PowerShell apapun.
2. **Pembersihan Permanen Berbasis Bukti (Deprecated Purge Complete)**: Seluruh file skrip PowerShell `.ps1` dan `.psm1` di folder `Deprecated/` telah dihapus secara permanen dari repositori setelah terbukti stabil selama minimal 1 siklus penggunaan nyata (Tahap 4 Bab 2.2).
3. **Fungsionalitas Penuh**: Seluruh 8 modul utama (Home, CleanDownloads, RapikanFile, Settings, PdfTools, AIWorkspace, ImageTools, RemoveBackground) berfungsi normal 100% di C# WPF Native.
4. **Peningkatan Performa Terukur Terhadap Baseline Resmi (Fase 0A)**:
   - Aplikasi C# WPF `Nexus.exe` diuji dan dibandingkan secara langsung terhadap **Baseline Performa Resmi PowerShell v1.7** yang dicatat pada Fase 0A (`docs/benchmarks/powershell_v1.7_baseline.json`).
   - Seluruh evaluasi performa aplikasi C# WPF `Nexus.exe` diukur secara langsung terhadap data empiris pada baseline tersebut. Target numerik performa akhir (startup latency, RAM footprint, dan UI frame responsiveness) akan ditetapkan secara eksplisit begitu dokumen baseline Fase 0A selesai disusun, sehingga evaluasi performa mutlak berbasis data objektif dan bukan interpretasi kualitatif.
5. **Test Suite C# Pass 100%**: Seluruh critical path unit tests dan bug regression tests di C# **PASS 100%**.

---

**DOKUMEN INI SECHARA RESMI DIBEKUKAN SEBAGAI NEXUS MIGRATION CONSTITUTION v1.0**  
*Tercatat dan Dibekukan pada Workspace Nexus.*
