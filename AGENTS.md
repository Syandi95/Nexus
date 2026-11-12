# NEXUS Architecture & Engineering Guidelines for AI Agents

Dokumen ini adalah panduan permanen arsitektur, standar kode, dan prosedur operasional untuk seluruh agen AI yang bekerja pada codebase **NEXUS**. // [Diverifikasi oleh Nexus Live Diff]

---

## 1. Struktur Folder Portabel (Engine, Models, dan nexus_data/config.json)

Aplikasi Nexus dirancang untuk bersifat **portabel**. Pengguna bebas memindahkan folder aplikasi ke drive atau PC lain tanpa ketergantungan path absolut yang kaku.

### Layout Folder Standar
```text
<NexusRoot>/
├── Engine/
│   ├── Convert from PDF/
│   │   ├── NexusConverterEngine.dll
│   │   ├── NexusConverterEngine.deps.json
│   │   ├── NexusConverterEngine.runtimeconfig.json
│   │   ├── runtimes/
│   │   └── RELEASE-MANIFEST.json
│   └── LlamaServer/
├── Models/
│   ├── AI Workspace/
│   │   └── PP-OCRv6/
│   ├── RemoveBackground/
│   └── Inpaint/
└── Nexus-CSharp/
    └── src/
        └── 03_App/Nexus.App/
            └── bin/{Debug|Release}/net8.0-windows/
                ├── nexus_data/
                │   └── config.json
                └── Nexus.App.exe
```

### Konfigurasi & Resolusi Path Portabel
1. **Nilai Default `config.json`**:
   - `"EngineRootFolder": ".\\Engine"`
   - `"ModelRootFolder": ".\\Models"`
   - Pengguna dapat menyesuaikan lokasi folder ini melalui UI *Settings*, dan perubahan disimpan ke `config.json`.
2. **Mekanisme Resolusi 3-Lapis (`SettingsManagerService`)**:
   - **Lapis 1**: Gunakan path dari `config.json` jika direktori tersebut ada (`Directory.Exists`) dan berisi modul yang dibutuhkan (misal: `Convert from PDF\NexusConverterEngine.dll`).
   - **Lapis 2**: Jika tidak ditemukan (misal aplikasi dipindahkan ke PC/drive lain), gunakan fallback ke `<AppBaseDir>\Engine`.
   - **Lapis 3 (Environment Pengembang/Build)**: Jika aplikasi dijalankan langsung dari dalam subdirektori build (`bin\Debug` atau `bin\Release`), aplikasi secara otomatis menelusuri folder induk (*parent directories*) hingga menemukan `<NexusRoot>\Engine` dan `<NexusRoot>\Models`.

### Aturan Mutlak Portabilitas `Engine` & `Models` serta Standar Build Windows-Only:
1. **DILARANG KERAS Menyalin atau Memindahkan `Engine` & `Models` ke Dalam `bin` atau `obj`**:
   - Folder `D:\NEXUS\Engine` dan `D:\NEXUS\Models` adalah satu-satunya sumber (*Single Source of Truth*) untuk binary konverter, `llama-server.exe`, dan model AI (GGUF/ONNX).
   - Agen **DILARANG KERAS** menyalin, memindahkan, atau menduplikasi isi `D:\NEXUS\Engine` maupun `D:\NEXUS\Models` ke dalam `bin\Debug`, `bin\Release`, atau `obj\`. Biarkan `SettingsManagerService` mengambilnya langsung dari `<NexusRoot>\Engine` dan `<NexusRoot>\Models`.
2. **Fokus Eksklusif Platform Windows Desktop (`net8.0-windows` / `win-x64`)**:
   - Proyek NEXUS adalah aplikasi desktop Windows murni. Agen **DILARANG** menambahkan target kompilasi, pustaka runtime, atau skrip untuk macOS (`osx`), Linux (`linux`), Android, atau iOS.
3. **Larangan Duplikasi Output Build & Perlindungan Riwayat Chat (`nexus_data`)**:
   - Dilarang membuat folder build kustom seperti `bin-codex`, `obj-temp`, dll.
   - Dilarang menghapus folder `nexus_data\conversations\` dan `nexus_data\config.json` di dalam `bin\Debug\net8.0-windows\` maupun `bin\Release\net8.0-windows\` saat melakukan pembersihan build, karena di sanalah riwayat percakapan dan pengaturan pengguna disimpan.

---

## 2. Larangan Keras Hardcode Teks / Dokumen dalam Engine

Engine konversi dokumen (`NexusConverterEngine` dan `NexusHighFidelityConverter.cs`) **DILARANG KERAS** memuat string spesifik dokumen atau *heuristic tailoring* yang ditujukan hanya untuk dokumen tertentu.

### Larangan Khusus:
- Dilarang menaruh kata kunci jabatan/teks sampel seperti `"Direktur"`, `"Manager"`, `"Kepala"`, `"NIP"`, `"NIM"`, `"Masalah|Akar Masalah|Solusi"`, dsb.
- Dilarang membuat cabang `if/else` berdasarkan nama file PDF atau teks unik dari dokumen pengguna.
- Logika klasifikasi tabel, bentuk visual (*shapes*), paragraf, dan tipografi harus mengandalkan:
  - Analisis struktur visual dan geometri layout (*bounding box*, koordinat koordinat sel, tab stops).
  - Model inferensi font (pengukuran metrik teks, rasio spasi, penentuan heading/body secara semantik).
  - Pola token generik berbasis ekspresi reguler struktural (bukan isi teks semantik privat pengguna).

---

## 3. Aturan Kompilasi Single-Node MSBuild / .NET

Lingkungan Windows dengan sandbox mengisolasi *named-pipe IPC* antar-proses. Jika MSBuild mencoba menjalankan kompilasi multi-node secara paralel, proses kompilasi dapat berhenti (*hanging*) atau gagal diam-diam (*exit failure*) tanpa pesan error yang deskriptif.

### Aturan Kompilasi:
Setiap pemanggilan `dotnet build` atau `dotnet test` pada solution atau project C# **WAJIB** menyertakan parameter berikut:
```powershell
& "C:\Users\Syand\.dotnet\dotnet.exe" build <PathToCsprojOrSln> -c Release --no-restore -m:1 -p:BuildInParallel=false
```

### Kebersihan Pohon Source & Directory.Build.props:
- Folder arsip build seperti `obj-codex-*` dan `bin-*` tidak boleh disimpan di dalam pohon `Nexus-CSharp/src`. Pindahkan arsip ke folder eksternal seperti `D:\NEXUS\Archives\`.
- File `D:\NEXUS\Nexus-CSharp\Directory.Build.props` dipertahankan untuk mengisolasi eksklusi snapshot dan membungkam peringatan compiler yang relevan:
  ```xml
  <Project>
    <PropertyGroup>
      <DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**;obj-*/**</DefaultItemExcludes>
      <NoWarn>$(NoWarn);CA1416;CS0067</NoWarn>
    </PropertyGroup>
  </Project>
  ```
- Seluruh build Release pada `Nexus.App.csproj` harus menghasilkan **0 Warning, 0 Error**.

---

## 4. Pemisahan Proses CLI NexusConverterEngine.dll (SkiaSharp Decoupling)

Aplikasi desktop `Nexus.App` dan engine konversi `NexusConverterEngine.dll` memiliki ketergantungan pustaka grafis (*native graphics library*) yang berbeda versi dan tidak kompatibel jika dimuat dalam proses yang sama:
- **Nexus.App**: Memakai SkiaSharp modern (v4.x) untuk rendering UI WPF dan visualizer halaman.
- **NexusConverterEngine**: Menggunakan SkiaSharp v2.88.8 yang terkalibrasi khusus untuk fidelitas font Office, OpenXML vector rendering, dan ekstraksi gambar embedded PDF.

### Prinsip Integrasi Out-of-Process:
1. **Dilarang Menambahkan `<ProjectReference>` Langsung**:
   - `Nexus.App.csproj` **TIDAK BOLEH** mereferensikan `NexusConverterEngine.csproj` atau `Nexus.ConvertFromPdf.csproj`.
2. **Eksekusi Melalui `IPdfOfficeToolService`**:
   - `Nexus.App` berinteraksi dengan engine konversi secara eksklusif melalui pemanggilan proses CLI mandiri:
     ```powershell
     dotnet <EngineRoot>\Convert from PDF\NexusConverterEngine.dll <format> <inputFile> <outputFile> --control-stdin
     ```
3. **Atomic Staging & OpenXML Validation**:
   - File hasil konversi di-generate pada direktori staging terisolasi (`.nexus-convert-*`) pada volume tujuan, lalu dipindahkan (*atomic rename*) ke folder output akhir.
   - Pembatalan (*cancellation*) dikirimkan via stream `StandardInput` ("cancel\n") untuk memastikan pembersihan *unmanaged resources* secara anggun.

---

## 5. Peta Struktur Repositori Baku (Canonical Repository Map)

Seluruh agen AI (OpenCode, Codex, Antigravity, Cursor, dll.) **WAJIB** mematuhi peta direktori di bawah ini dan menempatkan setiap berkas sesuai fungsinya:

```text
D:\NEXUS\                                  <-- ROOT EKOSISTEM (HANYA 4 FOLDER & LAUNCHER)
├── AGENTS.md                              <-- Konstitusi & Peta Repositori untuk seluruh AI Agent
├── GEMINI.md                              <-- Pedoman perilaku Karpathy & rujukan ke AGENTS.md
├── global.json                            <-- Pengunci versi .NET SDK (8.0.424)
├── Dev_Run.cmd / Dev_Run.vbs              <-- Launcher mode pengembang
├── Jalankan_NEXUS.cmd / Jalankan_NEXUS.vbs<-- Launcher mode pengguna
├── Engine\                                <-- Binary eksternal (LlamaServer b11057, NexusConverterEngine)
├── Models\                                <-- Model GGUF & ONNX (Gemma-4-E4B, Qwen3-4B, PP-OCRv6, Inpaint)
├── scratch\                               <-- HANYA untuk fixture pengujian yang di-pin kode:
│   ├── GateI-ReviewedWorkspace\           <-- Fixture uji AgentWorkspaceWorkflow (Gate I)
│   ├── I2B-ReviewedContracts\             <-- Fixture uji kontrak Gate I2-B
│   └── recovery-first-20261017\
│       └── reviewed-i2b-v2\               <-- Fixture uji isolasi recovery Gate I2-B
└── Nexus-CSharp\                          <-- REPOSITORI KODE SUMBER UTAMA C# (.NET 8)
    ├── Directory.Build.props              <-- Konfigurasi isolasi MSBuild
    ├── Directory.Build.targets            <-- Target MSBuild solusi
    ├── README.md                          <-- Dokumentasi utama repositori
    ├── tools\                             <-- Skrip utilitas pengembangan resmi
    ├── docs\                              <-- PUSAT DOKUMENTASI TERSTRUKTUR
    │   ├── NEXUS_ENGINEERING_POLICY.md    <-- Kebijakan standar rekayasa perangkat lunak
    │   ├── architecture\                  <-- Dokumen desain & audit arsitektur aktif
    │   ├── roadmaps\                      <-- Peta jalan pengembangan (Gate M, Agent Roadmap)
    │   ├── specs\                         <-- Spesifikasi fitur, UI, dan matriks paritas
    │   ├── constitution\                  <-- Konstitusi migrasi solusi
    │   ├── contracts\                     <-- Skema JSON & kontrak antar-modul
    │   ├── design-system\                 <-- Baseline tata kelola token UI (migration-baseline.md)
    │   ├── benchmarks\                    <-- Referensi tolok ukur performa
    │   └── archive\                       <-- Arsip historis
    │       ├── NEXUS_GATE_MILESTONES_MASTER_HISTORY.md
    │       ├── gates\                     <-- Arsip laporan Gate yang sudah selesai
    │       └── plans\                     <-- Arsip rencana implementasi masa lalu
    └── src\                               <-- POHON KODE SUMBER C# (4 LAPIS ARSITEKTUR)
        ├── Nexus.sln                      <-- File Solution utama
        ├── Assets\                        <-- Aset statis aplikasi (MyImage.jpg)
        ├── 00_Contracts\                  <-- Lapis 0: Interface, DTO, & Kontrak murni (Nexus.Contracts)
        ├── 01_Foundation\                 <-- Lapis 1: Config, Logging, Registry, & Fondasi (Nexus.Foundation)
        ├── 02_Modules\                    <-- Lapis 2: Modul fitur independen & unit test masing-masing:
        │   ├── Nexus.AI.EditingLite\      <-- Mesin safe-write, diff, & verifikasi source plan
        │   ├── Nexus.AI.Llama\            <-- Klien LlamaServer, streaming chat, & AI Check judge
        │   ├── Nexus.Conversation\        <-- Manajemen riwayat & indeks percakapan
        │   ├── Nexus.ConvertFromPdf\      <-- Engine konversi PDF ke Office berfidelitas tinggi
        │   ├── Nexus.Core.HomeSettings\   <-- Pengaturan aplikasi & manajemen katalog model
        │   ├── Nexus.Engine.ColorManagement\
        │   ├── Nexus.Engine.ImageSubsystem\ <-- Pipeline gambar, OCR PP-OCRv6, Inpaint, RemoveBg
        │   ├── Nexus.FileUtilities\       <-- Utilitas manajemen berkas
        │   └── Nexus.PdfUtilities\        <-- Pembaca & pemroses PDF
        └── 03_App\                        <-- Lapis 3: Aplikasi Desktop WPF & Pengujian Integrasi:
            ├── Nexus.App\                 <-- Proyek utama WPF (Views, ViewModels, Services, Themes)
            └── Nexus.Integration.Tests\   <-- Suite pengujian integrasi, UI governance, & kontrak Gate
```

---

## 6. Aturan Disiplin Multi-Agen (Anti-Sprawl & Kebersihan Workspace)

Untuk mencegah penumpukan berkas dan pembengkakan ukuran disk saat dikerjakan bergantian oleh berbagai agen (OpenCode, Codex, Antigravity, Cursor, dll.), setiap agen **WAJIB** mematuhi 5 larangan keras berikut:

1. **DILARANG Membuat Berkas .md, .log, .txt, .json, atau .png Baru di Root D:\NEXUS maupun D:\NEXUS\Nexus-CSharp**:
   - Laporan penyelesaian tugas/Gate baru wajib diletakkan di `D:\NEXUS\Nexus-CSharp\docs\archive\gates\`.
   - Dokumen arsitektur/spesifikasi/roadmap baru wajib dimasukkan ke `docs\architecture\`, `docs\specs\`, atau `docs\roadmaps\`.
2. **DILARANG Membuat Folder Backup atau Clone Proyek di Dalam D:\NEXUS atau Nexus-CSharp**:
   - Dilarang keras membuat folder seperti `backup_*`, `_ai-backup`, `verification-*`, `tmp`, `Audit`, atau menduplikasi pohon `src\`.
3. **DILARANG Menyimpan Bukti Uji Coba (Evidence / Dump / Screenshot) di Dalam docs\ atau D:\NEXUS**:
   - Apabila pengujian membutuhkan direktori output sementara, gunakan `$env:TEMP\nexus-test-runs` dan hapus kembali segera setelah pengujian selesai.
4. **DILARANG Menambahkan Proyek Console Eksperimen di Luar Struktur 4 Lapis src\**:
   - Jangan membuat proyek lepas seperti `Phase5Runner` di dalam `src\`. Seluruh pengujian harus ditulis sebagai xUnit test di dalam proyek `*.Tests` yang sesuai.
5. **Kewajiban Bersih-Bersih Pasca-Eksekusi (Clean-Up Before Finish)**:
   - Sebelum mengakhiri sesi kerja, agen wajib menghapus seluruh skrip sementara (`*.ps1`, `*.py`, `*.log`) yang dibuat selama sesi tersebut sehingga git status dan direktori root tetap bersih.

### Aturan Mutlak Portabilitas `Engine` & `Models` serta Standar Build Windows-Only:
1. **DILARANG KERAS Menyalin atau Memindahkan `Engine` & `Models` ke Dalam `bin` atau `obj`**:
   - Folder `D:\NEXUS\Engine` dan `D:\NEXUS\Models` adalah satu-satunya sumber (*Single Source of Truth*) untuk binary konverter, `llama-server.exe`, dan model AI (GGUF/ONNX).
   - Agen **DILARANG KERAS** menyalin, memindahkan, atau menduplikasi isi `D:\NEXUS\Engine` maupun `D:\NEXUS\Models` ke dalam `bin\Debug`, `bin\Release`, atau `obj\`. Biarkan `SettingsManagerService` mengambilnya langsung dari `<NexusRoot>\Engine` dan `<NexusRoot>\Models`.
2. **Fokus Eksklusif Platform Windows Desktop (`net8.0-windows` / `win-x64`)**:
   - Proyek NEXUS adalah aplikasi desktop Windows murni. Agen **DILARANG** menambahkan target kompilasi, pustaka runtime, atau skrip untuk macOS (`osx`), Linux (`linux`), Android, atau iOS.
3. **Larangan Duplikasi Output Build & Perlindungan Riwayat Chat (`nexus_data`)**:
   - Dilarang membuat folder build kustom seperti `bin-codex`, `obj-temp`, dll.
   - Dilarang menghapus folder `nexus_data\conversations\` dan `nexus_data\config.json` di dalam `bin\Debug\net8.0-windows\` maupun `bin\Release\net8.0-windows\` saat melakukan pembersihan build, karena di sanalah riwayat percakapan dan pengaturan pengguna disimpan.

---

*Panduan ini dibuat dan diverifikasi pada 31 Oktober 2026 sebagai standar baku pengembangan NEXUS.*
