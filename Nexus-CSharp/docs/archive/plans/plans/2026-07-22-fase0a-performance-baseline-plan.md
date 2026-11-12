# [Fase 0A] Formal Performance Baseline Benchmark Implementation Plan (REVISED)

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Menyusun rencana pengukuran benchmark performa resmi aplikasi Nexus PowerShell v1.7 yang lengkap, dapat diaudit, dan 100% dapat direproduksi (*reproducible*) sebagai acuan baseline tunggal Exit Criteria migrasi C# WPF (.NET 8) Native (Nexus Migration Constitution v1.0).

**Architecture:** Pengukuran dilakukan secara otomatis dan semi-otomatis menggunakan skrip pembantu PowerShell (`Measure-NexusBaseline.ps1`), `System.Diagnostics.Stopwatch`, `System.Diagnostics.Process`, WPF `DispatcherTimer`, dan logging JSON terstandar tanpa mengubah kode sumber (*zero source code modification*).

**Tech Stack:** PowerShell 5.1, .NET Framework 4.8 / System.Diagnostics, WPF Dispatcher Interop, JSON Schema (`System.Text.Json` / `ConvertFrom-Json`).

---

## Global Constraints & User Adjustments

* ❌ **DILARANG MENGUBAH SOURCE CODE**: Jangan mengubah file `.ps1`, `.xaml`, atau `.cs` aplikasi Nexus yang ada di `Modules/`, `Foundation/`, `AI/`, atau `Engine/`.
* ❌ **DILARANG REFACTORING & OPTIMISASI**: Tidak boleh memperbaiki bug, mengoptimalkan query, atau mengubah perilaku aplikasi saat ini.
* ❌ **DILARANG LAKUKAN IMPLEMENTASI C#**: Pengukuran murni pada versi PowerShell v1.7 eksisting.
* 📌 **STANDAR LOKASI ARSIP BENCHMARK**: Seluruh artefak rencana dan hasil benchmark wajib disimpan di `docs/benchmarks/`.
* 📌 **REPRODUSIBILITAS AKURAT**: Seluruh skrip pengukur wajib mencatat metadata lingkungan hardware dan sistem operasi secara otomatis.
* 🎯 **ATURAN BASELINE V1.7 RELEVANSI**: Seluruh metrik murni merepresentasikan komponen yang berjalan pada Nexus PowerShell v1.7 saat ini. Jika suatu komponen tidak tersedia/belum di-wire di v1.7, catat nilainya sebagai `"N/A"` dan **jangan dipaksakan diukur**.
* 🎯 **MEDIAN SEBAGAI METRIK UTAMA STARTUP**: `startup.median_ms` ditetapkan sebagai **metrik utama evaluasi performa startup** untuk menjamin ketahanan terhadap outlier. Metrik Mean, Min, Max, dan Standard Deviation disimpan sebagai data pendukung.

---

## Structure Map & File Manifest

```
docs/benchmarks/
├── README.md                                  # Dokumentasi cara menjalankan runner & interpretasi data
├── powershell_v1.7_baseline.json             # File luaran resmi (Output JSON Baseline Target Schema)
└── scripts/
    ├── Measure-NexusBaseline.ps1             # Main Automated Benchmark Collector Runner
    ├── Measure-SystemMetrics.ps1              # Environmental Hardware & OS Metrics Sampler
    ├── Measure-UIResponsiveness.ps1           # WPF STA Dispatcher & Module Switch Sampler
    └── Measure-AIMetrics.ps1                  # LlamaServer SSE TTFT & Token Speed Sampler
```

---

## DAFTAR METRIK PERFORMA RESMI BASELINE (28 METRIK TERDEFINISI)

| Kategori Benchmark | Nama Metrik | Satuan | Peran Metrik | Deskripsi & Batas Toleransi Validasi |
| :--- | :--- | :---: | :---: | :--- |
| **Environment** | `env.os_build` | String | Context | Versi dan Build Windows OS |
| **Environment** | `env.powershell_version` | String | Context | Versi PowerShell runtime (`$PSVersionTable.PSVersion`) |
| **Environment** | `env.dotnet_runtime` | String | Context | Versi CLR / .NET Framework runtime |
| **Environment** | `env.cpu_model` | String | Context | Nama Processor dan jumlah core/threads |
| **Environment** | `env.ram_total_gb` | Float | Context | Total Memori Fisik RAM (GB) |
| **Environment** | `env.storage_type` | String | Context | Tipe Media Penyimpanan (NVMe / SATA SSD / HDD) |
| **Environment** | `env.gpu_model` | String | Context | Nama GPU & Dedicated VRAM (MB) |
| **Environment** | `env.display_resolution` | String | Context | Resolusi Layar & Scaling DPI (%) |
| **Startup** | `startup.median_ms` | ms | **PRIMARY** | Median waktu startup hangat dari 10 kali pengulangan (Acuan Utama) |
| **Startup** | `startup.cold_latency_ms` | ms | Supporting | Latency startup dingin (first launch tanpa cache OS) |
| **Startup** | `startup.warm_latency_ms` | ms | Supporting | Latency startup hangat rata-rata |
| **Startup** | `startup.min_ms` | ms | Supporting | Latency startup tercepat dari 10 run |
| **Startup** | `startup.max_ms` | ms | Supporting | Latency startup terlambat dari 10 run |
| **Startup** | `startup.std_dev_ms` | ms | Supporting | Deviasi standar (variansi) waktu startup dari 10 run |
| **Memory** | `memory.idle_working_set_mb` | MB | **PRIMARY** | Working Set RAM aplikasi saat idle 30 detik di Home |
| **Memory** | `memory.peak_working_set_mb` | MB | Supporting | Peak Working Set RAM tertinggi selama full navigation cycle |
| **Memory** | `memory.private_bytes_mb` | MB | Supporting | Total Private Memory (`PrivateMemorySize64`) |
| **Memory** | `memory.commit_charge_mb` | MB | Supporting | Total Commit Size Virtual (`VirtualMemorySize64`) |
| **UI Responsiveness**| `ui.dispatcher_latency_ms` | ms | **PRIMARY** | Delta delay pada STA WPF Event Dispatcher Queue |
| **UI Responsiveness**| `ui.module_switch_avg_ms` | ms | Supporting | Rata-rata latency perpindahan antarmodul (35x run) |
| **UI Responsiveness**| `ui.theme_switch_ms` | ms | Supporting | Latency penggantian tema visual (Light <-> Dark) |
| **UI Responsiveness**| `ui.conversation_render_ms`| ms | Supporting | Latency render percakapan 50 pesan pada UI Chat |
| **AI Workspace** | `ai.ttft_ms` | ms | **PRIMARY** | Time To First Token (latency dari Send s/d chunk 1) |
| **AI Workspace** | `ai.total_response_ms` | ms | Supporting | Rata-rata total waktu penyelesaian generasi teks |
| **AI Workspace** | `ai.tokens_per_second` | tok/s | Supporting | Kecepatan keluaran token rata-rata |
| **AI Workspace** | `ai.cancel_latency_ms` | ms | Supporting | Latency pembatalan streaming saat tombol Stop ditekan |
| **PDF Tools** | `pdf.render_page_ms` | ms/page| Supporting | Latency konversi halaman PDF ke Image (N/A jika modul off) |
| **Image Tools** | `image.rmbg_infer_ms` | ms | Supporting | Latency inferensi RMBG-2.0 / MVANet ONNX C# Engine |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Environment & Hardware Metrics Collector Script Specification

**Files:**
- Create: `docs/benchmarks/scripts/Measure-SystemMetrics.ps1`
- Test Output: `docs/benchmarks/scripts/test_env_output.json`

**Interfaces:**
- Consumes: WMI / CIM Queries (`Get-CimInstance Win32_OperatingSystem`, `Win32_Processor`, `Win32_PhysicalMemory`, `Win32_VideoController`, `Win32_DiskDrive`).
- Produces: Hashtable / PS Custom Object berformat JSON yang mendefinisikan 8 metrik lingkungan hardware & OS.

- [ ] **Step 1: Spesifikasikan skrip pembaca WMI/CIM Hardware**

  ```powershell
  # docs/benchmarks/scripts/Measure-SystemMetrics.ps1
  function Get-NexusEnvironmentBaseline {
      $os = Get-CimInstance Win32_OperatingSystem
      $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
      $ram = Get-CimInstance Win32_PhysicalMemory | Measure-Object -Property Capacity -Sum
      $gpu = Get-CimInstance Win32_VideoController | Select-Object -First 1
      $disk = Get-CimInstance Win32_DiskDrive | Select-Object -First 1
      
      [PSCustomObject]@{
          os_build            = "$($os.Caption) (Build $($os.BuildNumber))"
          powershell_version  = $PSVersionTable.PSVersion.ToString()
          dotnet_runtime      = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
          cpu_model           = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) Cores, $($cpu.NumberOfLogicalProcessors) Threads)"
          ram_total_gb        = [math]::Round($ram.Sum / 1GB, 2)
          storage_type        = "$($disk.Model) ($($disk.MediaType))"
          gpu_model           = "$($gpu.Name) ($([math]::Round($gpu.AdapterRAM / 1MB, 0)) MB VRAM)"
          display_resolution  = "$([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width)x$([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height)"
      }
  }
  ```

- [ ] **Step 2: Jalankan skrip penguji dan pastikan tidak ada error WMI**

  Run: `powershell -ExecutionPolicy Bypass -File docs/benchmarks/scripts/Measure-SystemMetrics.ps1`  
  Expected: JSON Hashtable valid berisi seluruh 8 data spesifikasi sistem.

- [ ] **Step 3: Commit spesifikasi skrip environment**

  ```bash
  git add docs/benchmarks/scripts/Measure-SystemMetrics.ps1
  git commit -m "docs(benchmark): add hardware and environment baseline collector script"
  ```

---

### Task 2: Startup & Memory Benchmark Collector Specification (Median Priority)

**Files:**
- Create: `docs/benchmarks/scripts/Measure-NexusBaseline.ps1`
- Test Output: `docs/benchmarks/powershell_v1.7_baseline.json`

**Interfaces:**
- Consumes: `System.Diagnostics.Process`, `System.Diagnostics.Stopwatch`, `ManajemenFile.ps1` launch lifecycle.
- Produces: Metrik `startup.*` (dengan Median sebagai primary) dan `memory.*` pada schema JSON.

- [ ] **Step 1: Spesifikasikan metode pengukuran Startup (Median, Mean, Min, Max, StdDev)**

  * **Cold Startup Method**: Flush OS file cache, lalu ukur waktu eksekusi dari pembentukan proses PowerShell hingga window `MainWindow` mencapai state `IsActive == true`. Iterasi: **1 kali**.
  * **Warm Startup Method**: Eksekusi peluncuran aplikasi `ManajemenFile.ps1` secara berturut-turut setelah proses ditutup. Iterasi: **10 kali pengulangan**.
  * **Kalkulasi Median & Supporting Data**:
    ```powershell
    $warmTimes = @($warmRuns | Sort-Object)
    $median = if ($warmTimes.Count % 2 -eq 0) {
        ($warmTimes[$warmTimes.Count / 2 - 1] + $warmTimes[$warmTimes.Count / 2]) / 2
    } else {
        $warmTimes[[math]::Floor($warmTimes.Count / 2)]
    }
    ```

- [ ] **Step 2: Spesifikasikan metode pengukuran Memory Profile (Idle, Peak, Working Set, Private, Commit)**

  ```powershell
  # Cuplikan Logika Sampling Memori di Measure-NexusBaseline.ps1
  function Measure-NexusMemoryProfile {
      param([int]$ProcessId)
      $proc = Get-Process -Id $ProcessId
      
      # Idle Memory (diukur setelah aplikasi berdiam 30 detik di Home Module)
      Start-Sleep -Seconds 30
      $proc.Refresh()
      
      [PSCustomObject]@{
          idle_working_set_mb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
          peak_working_set_mb = [math]::Round($proc.PeakWorkingSet64 / 1MB, 2)
          private_bytes_mb    = [math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
          commit_charge_mb    = [math]::Round($proc.VirtualMemorySize64 / 1MB, 2)
      }
  }
  ```

- [ ] **Step 3: Commit spesifikasi skrip startup & memory**

  ```bash
  git add docs/benchmarks/scripts/Measure-NexusBaseline.ps1
  git commit -m "docs(benchmark): add startup median and memory profiling specifications"
  ```

---

### Task 3: UI Responsiveness & Dispatcher Latency Sampler Specification

**Files:**
- Create: `docs/benchmarks/scripts/Measure-UIResponsiveness.ps1`

**Interfaces:**
- Consumes: WPF `System.Windows.Threading.DispatcherTimer`, `ModuleHost.ps1` events, `ThemeManager.ps1` events.
- Produces: `ui.dispatcher_latency_ms`, `ui.module_switch_avg_ms`, `ui.theme_switch_ms`, `ui.conversation_render_ms`.

- [ ] **Step 1: Spesifikasikan pengukur STA Dispatcher Queue Latency**

  * **Tujuan**: Mengukur keterlambatan (*lag*) eksekusi antrean event pada UI thread WPF saat aplikasi menerima input user.
  * **Metode**: Mendaftarkan `DispatcherTimer` dengan Interval 10ms (Priority = `Background`). Menghitung selisih (*delta*) antara waktu yang dijadwalkan dengan waktu aktual timer tick dieksekusi.
  * **Iterasi**: 100 ticks selama 10 detik.
  * **Formula**: `Dispatcher_Latency = ActualTickTime - (PreviousTickTime + ScheduledInterval)`

- [ ] **Step 2: Spesifikasikan pengukur Module Switch Latency & Theme Switch Latency**

  * **Module Switch Method**: Eksekusi rantai navigasi otomatis: `Home` -> `CleanDownloads` -> `RapikanFile` -> `PdfTools` -> `RemoveBackground` -> `Settings` -> `AIWorkspace`. Diukur menggunakan Stopwatch dari klik tombol navigasi hingga `ModuleHost.Content` selesai memuat `ViewRoot`. Iterasi: **5 siklus penuh (35 kali switch)**.
  * **Theme Switch Method**: Eksekusi panggilan `Set-NexusTheme -Theme "Dark"` lalu `Set-NexusTheme -Theme "Light"`. Diukur waktu eksekusi aplikasi ResourceDictionary mengganti brushes. Iterasi: **10 kali pengulangan**.

- [ ] **Step 3: Commit spesifikasi UI responsiveness**

  ```bash
  git add docs/benchmarks/scripts/Measure-UIResponsiveness.ps1
  git commit -m "docs(benchmark): add WPF dispatcher latency and UI navigation benchmark specification"
  ```

---

### Task 4: AI Workspace, Image Subsystem, & PDF Tools Benchmark Specification (with N/A Fallback)

**Files:**
- Create: `docs/benchmarks/scripts/Measure-AIMetrics.ps1`

**Interfaces:**
- Consumes: `LlamaServerProvider.ps1` HTTP SSE Stream, `AIEngine.cs` ONNX Engine, Magick.NET PDF Converter.
- Produces: `ai.*`, `image.*`, `pdf.*` baseline metrics (or `"N/A"` if unavailable).

- [ ] **Step 1: Spesifikasikan AI Workspace SSE Benchmark (TTFT, TPS, Total Time, Cancel Latency)**

  * **Handling Component Availability**: Jika LlamaServer local (`http://127.0.0.1:8080`) tidak aktif/tidak merespons, catat metrik `ai.*` sebagai `"N/A"` dan **jangan gagalkan runner**.
  * **Prompt Standardized Test Set**: Menggunakan 3 sampel prompt tetap (Short 50 tokens, Medium 200 tokens, Code Generation 500 tokens).
  * **TTFT (Time To First Token)**: Diukur dari milidetik pengiriman payload POST request hingga chunk data SSE pertama (`data: {"content": ...}`) diterima socket StreamReader.
  * **Iterasi**: **5 kali per prompt test set**.

- [ ] **Step 2: Spesifikasikan Image Tools & Remove Background ONNX Benchmark**

  * **Input Test Assets**: 3 sampel gambar uji standar (1024x1024 PNG Portret, 1920x1080 PNG Kompleks, 4K PNG Lanskap).
  * **RMBG-2.0 / MVANet Inferencing**: Memanggil `[Nexus.AI.AIEngine]::Run()` di C# DLL. Diukur waktu eksekusi murni inferensi model ONNXRuntime (`InferenceSession.Run()`).
  * **Iterasi**: **10 kali per sampel gambar**.

- [ ] **Step 3: Spesifikasikan PDF Tools Conversion Benchmark**

  * **Handling Component Availability**: Jika Magick.NET / Ghostscript DLL tidak terinstal di lingkungan baseline, catat `pdf.render_page_ms` sebagai `"N/A"`.
  * **Input Test Assets**: Document PDF standar 10 halaman (teks + gambar).
  * **Iterasi**: **3 kali pengulangan dokumen penuh**.

- [ ] **Step 4: Commit spesifikasi AI & Media benchmarks**

  ```bash
  git add docs/benchmarks/scripts/Measure-AIMetrics.ps1
  git commit -m "docs(benchmark): add AI Workspace SSE, ONNX Image, and PDF benchmark specifications with N/A handling"
  ```

---

### Task 5: JSON Schema Baseline Format & Benchmark Directory Setup

**Files:**
- Create: `docs/benchmarks/README.md`
- Create: `docs/benchmarks/powershell_v1.7_baseline.json`

**Interfaces:**
- Consumes: Luaran dari Task 1-4.
- Produces: Format JSON resmi `powershell_v1.7_baseline.json` yang siap diisi oleh eksekusi benchmark.

- [ ] **Step 1: Buat file JSON Schema Skeleton `powershell_v1.7_baseline.json`**

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "Nexus PowerShell v1.7 Official Performance Baseline",
  "metadata": {
    "app_version": "v1.7",
    "architecture": "PowerShell 5.1 + WPF",
    "constitution_ref": "Nexus Migration Constitution v1.0",
    "timestamp_utc": null,
    "executed_by": null,
    "primary_startup_metric": "startup.median_ms",
    "status": "DRAFT_SPECIFICATION"
  },
  "environment": {
    "os_build": null,
    "powershell_version": null,
    "dotnet_runtime": null,
    "cpu_model": null,
    "ram_total_gb": null,
    "storage_type": null,
    "gpu_model": null,
    "display_resolution": null
  },
  "metrics": {
    "startup": {
      "median_ms": { "value": null, "unit": "ms", "role": "PRIMARY", "iterations": 10, "description": "Primary evaluation metric: Median warm startup latency" },
      "cold_latency_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 1, "description": "Time to first MainWindow render without OS cache" },
      "warm_latency_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 10, "description": "Average warm start render latency" },
      "min_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 10, "description": "Minimum warm start latency" },
      "max_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 10, "description": "Maximum warm start latency" },
      "std_dev_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 10, "description": "Standard deviation of warm startup" }
    },
    "memory": {
      "idle_working_set_mb": { "value": null, "unit": "MB", "role": "PRIMARY", "duration_sec": 30, "description": "RAM Working Set after 30s idle at Home" },
      "peak_working_set_mb": { "value": null, "unit": "MB", "role": "SUPPORTING", "description": "Maximum RAM Working Set observed during navigation cycle" },
      "private_bytes_mb": { "value": null, "unit": "MB", "role": "SUPPORTING", "description": "Private memory committed by process" },
      "commit_charge_mb": { "value": null, "unit": "MB", "role": "SUPPORTING", "description": "Virtual memory committed size" }
    },
    "ui_responsiveness": {
      "dispatcher_latency_ms": { "value": null, "unit": "ms", "role": "PRIMARY", "iterations": 100, "description": "WPF STA Dispatcher event queue delay" },
      "module_switch_avg_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 35, "description": "Average latency per module navigation switch" },
      "theme_switch_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 10, "description": "ResourceDictionary theme swapping latency" },
      "conversation_render_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 5, "description": "Latency to render 50-message chat history" }
    },
    "ai_workspace": {
      "ttft_ms": { "value": null, "unit": "ms", "role": "PRIMARY", "iterations": 15, "description": "Time To First Token from SSE stream (N/A if server offline)" },
      "total_response_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 15, "description": "Total duration for 200 token completion" },
      "tokens_per_second": { "value": null, "unit": "tokens/sec", "role": "SUPPORTING", "iterations": 15, "description": "Generation speed in tokens per second" },
      "cancel_latency_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 5, "description": "Latency from Stop click to socket abort" }
    },
    "media_processing": {
      "pdf_render_page_ms": { "value": null, "unit": "ms/page", "role": "SUPPORTING", "iterations": 30, "description": "PDF page rasterization latency (N/A if library uninstalled)" },
      "image_rmbg_infer_ms": { "value": null, "unit": "ms", "role": "SUPPORTING", "iterations": 30, "description": "ONNX RMBG-2.0 inference latency per image" }
    },
    "stability": {
      "unhandled_exceptions_count": { "value": 0, "role": "SUPPORTING", "description": "Count of unhandled exceptions logged in diagnostic_crash.log" },
      "memory_leak_delta_mb": { "value": null, "unit": "MB", "role": "SUPPORTING", "iterations": 50, "description": "Working Set RAM growth after 50 consecutive module switches" }
    }
  }
}
```

- [ ] **Step 2: Tulis dokumentasi petunjuk eksekusi di `docs/benchmarks/README.md`**

- [ ] **Step 3: Commit struktur folder dan dokumentasi benchmark**

  ```bash
  git add docs/benchmarks/README.md docs/benchmarks/powershell_v1.7_baseline.json
  git commit -m "docs(benchmark): initialize benchmark documentation and updated JSON schema skeleton"
  ```

---

## DEFINITION OF DONE (DoD) FASE 0A

Fase 0A dinyatakan **DONE (SELESAI & LENGKAP)** apabila seluruh kriteria berikut terpenuhi:

1. **Rencana Pengukuran Terkomit**: Rencana implementasi Fase 0A tersusun rapi di `docs/superpowers/plans/2026-07-22-fase0a-performance-baseline-plan.md`.
2. **Median Menjadi Metrik Utama Startup**: `startup.median_ms` ditegaskan sebagai metrik utama evaluasi startup.
3. **Penyimpanan Fallback N/A**: Komponen yang belum aktif pada baseline v1.7 secara aman dicatat sebagai `"N/A"`.
4. **28 Metrik Terdefinisi Mutlak**: Setiap metrik memiliki *Tujuan*, *Metode Pengukuran*, *Alat*, *Jumlah Iterasi*, *Format JSON*, dan *Kriteria Validasi* tanpa ada item TBD/TODO.
5. **Struktur Folder `docs/benchmarks/` Siap**: Folder `docs/benchmarks/` dan sub-folder `scripts/` terinisialisasi di repositori.
6. **JSON Schema Terdefinisi**: File `docs/benchmarks/powershell_v1.7_baseline.json` siap menampung luaran data numerik hasil eksekusi.
7. **Zero Code Modification**: Tidak ada satu pun baris kode aplikasi di `Modules/`, `Foundation/`, `AI/`, `Engine/`, atau `ManajemenFile.ps1` yang diubah atau di-refactor.
