# Nexus PowerShell v1.7 Baseline Performance Benchmarks

Dokumen ini berisi dokumentasi petunjuk eksekusi dan struktur baseline performa resmi aplikasi **Nexus PowerShell v1.7** yang dihasilkan pada **Fase 0A (Formal Performance Baseline Benchmark)** sesuai pedoman **Nexus Migration Constitution v1.0**.

---

## 1. Struktur Folder Benchmark

```
docs/benchmarks/
├── README.md                                  # Dokumentasi petunjuk eksekusi benchmark ini
├── powershell_v1.7_baseline.json             # Dokumen luaran resmi data baseline terukur
└── scripts/
    ├── Measure-SystemMetrics.ps1              # Pengumpul spesifikasi hardware & OS lingkungan
    ├── Measure-NexusBaseline.ps1             # Collector Startup Latency (Median) & Memory Profile
    ├── Measure-UIResponsiveness.ps1           # Collector WPF Dispatcher Latency & Navigation Lag
    └── Measure-AIMetrics.ps1                  # Collector AI SSE Stream & Media Subsystem (dengan N/A fallback)
```

---

## 2. Cara Menjalankan Kolektor Benchmark

Untuk mengulangi atau mereproduksi pengumpulan data benchmark secara otomatis:

```powershell
# 1. Jalankan pengumpulan metrik Hardware & OS Environment
powershell -ExecutionPolicy Bypass -File .\docs\benchmarks\scripts\Measure-SystemMetrics.ps1

# 2. Jalankan pengumpulan Startup Latency (10x warm run) & Memory Profile
powershell -ExecutionPolicy Bypass -File .\docs\benchmarks\scripts\Measure-NexusBaseline.ps1 -WarmRuns 10

# 3. Jalankan pengumpulan WPF STA Dispatcher Queue Latency
powershell -ExecutionPolicy Bypass -File .\docs\benchmarks\scripts\Measure-UIResponsiveness.ps1

# 4. Jalankan pengumpulan AI SSE & Media Subsystem (atau catat N/A jika offline)
powershell -ExecutionPolicy Bypass -File .\docs\benchmarks\scripts\Measure-AIMetrics.ps1
```

---

## 3. Metrik Utama Evaluasi Exit Criteria (Acuan Fase 8)

* **Startup Latency**: `startup.median_ms` (Metrik Utama: **84.73 ms**)
* **Memory Profile**: `memory.idle_working_set_mb` (Metrik Utama: **94.43 MB**)
* **UI Responsiveness**: `ui.dispatcher_latency_ms` (Metrik Utama: **2.45 ms**)

Seluruh evaluasi performa pada Fase 8 (Executable C# WPF `Nexus.exe`) wajib membandingkan metriknya secara langsung terhadap angka terukur pada file `powershell_v1.7_baseline.json`.
