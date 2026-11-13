# Nexus-CSharp Workspace
**Workspace Resmi Migrasi Aplikasi Nexus dari PowerShell 5.1 + WPF Menuju C# WPF (.NET 8) Native**

---

## 1. Ikhtisar Workspace

Workspace `Nexus-CSharp` ini dibuat khusus untuk menampung seluruh artefak migrasi, dokumen konstitusi, hasil benchmark, spesifikasi kontrak DTO/JSON, ADR (Architectural Decision Records), serta kode sumber C# WPF (.NET 8) tanpa mencampuri repositori utama PowerShell v1.7.

---

## 2. Struktur Folder Workspace

```
Nexus-CSharp/
├── README.md                                  # Dokumentasi tata kelola workspace ini
├── docs/
│   ├── constitution/
│   │   └── migration_constitution.md        # Nexus Migration Constitution v1.0 (FROZEN)
│   ├── benchmarks/
│   │   ├── README.md                        # Petunjuk eksekusi runner benchmark
│   │   ├── powershell_v1.7_baseline.json   # Data numerik resmi baseline PowerShell v1.7 (FROZEN)
│   │   └── scripts/                         # Skrip pengumpul data benchmark otomatis
│   │       ├── Measure-SystemMetrics.ps1
│   │       ├── Measure-NexusBaseline.ps1
│   │       ├── Measure-UIResponsiveness.ps1
│   │       └── Measure-AIMetrics.ps1
│   ├── adr/                                 # Catatan Keputusan Arsitektur (Architectural Decision Records)
│   ├── contracts/                           # Spesifikasi Kontrak DTO & Skema JSON (Fase 0B)
│   └── plans/                               # Rencana implementasi teknis per fase
└── src/                                     # Ruang kerja solusi & proyek C# WPF (.NET 8)
```

---

## 3. Peta Referensi Path Terintegrasi

| Jenis Artefak | Path Lokasi Terkini (`Nexus-CSharp`) |
| :--- | :--- |
| **Konstitusi Migrasi** | `docs/constitution/migration_constitution.md` |
| **Baseline Benchmark v1.7** | `docs/benchmarks/powershell_v1.7_baseline.json` |
| **Script Collector Benchmark**| `docs/benchmarks/scripts/` |
| **Kontrak Skema JSON (Fase 0B)**| `docs/contracts/` |
| **Catatan ADR Resmi** | `docs/adr/` |
| **Rencana Implementasi** | `docs/plans/` |
| **Source Code C#** | `src/` |

---

## 4. Status Fase Migrasi & Arsitektur Saat Ini

- [x] **Fase 0A (Formal Performance Baseline Benchmark)**: SELESAI & DIBEKUKAN (`powershell_v1.7_baseline.json`).
- [x] **Fase 0B (Contract & Schema Freeze)**: SELESAI (`00_Contracts/Nexus.Contracts`).
- [x] **Fase 1–5 (Arsitektur 4-Lapis & Fitur Inti C# .NET 8)**: SELESAI & AKTIF
  - `00_Contracts`: Interface & DTO murni lintas modul.
  - `01_Foundation`: Konfigurasi portabel, logging, dan registry.
  - `02_Modules`: Fitur independen (AI Llama, PDF Utilities, Image Subsystem/OCR, Safe-write Editing, Color Management).
  - `03_App`: Aplikasi WPF Desktop (`Nexus.App`) & suite pengujian integrasi (`Nexus.Integration.Tests`).
- [x] **High-Fidelity Document Conversion**: Native C# `NexusConverterEngine` terintegrasi via out-of-process CLI.

---

## 5. Design System Governance & Anti-Duplication Rules

Aturan ini bersifat **permanen** dan wajib dipatuhi oleh seluruh developer serta AI agent yang bekerja pada project NEXUS.

### 5.1 Sebelum Membuat Style / Resource Baru

**SEBELUM MEMBUAT STYLE/RESOURCE BARU:**
1. Search seluruh repository dengan nama/fungsi.
2. Cari existing Style.
3. Cari BasedOn yang dapat dipakai.
4. Cari canonical component.
5. Cari local duplicate.
6. Hanya buat baru jika tidak ada yang cocok.

> **PENTING: NAMA BARU BUKAN ALASAN MEMBUAT STYLE BARU.**

Contoh pemetaan kebutuhan:
- **Need Button** $\rightarrow$ Search `NexusPrimaryButtonStyle`, `NexusSecondaryButtonStyle`, `NexusIconButtonStyle` di `Themes/Components/Buttons.xaml`
- **Need TextBox** $\rightarrow$ Search `NexusTextBoxStyle` di `Themes/Components/InputControls.xaml`
- **Need ComboBox** $\rightarrow$ Search `NexusComboBoxStyle` di `Themes/Components/InputControls.xaml`
- **Need Radius / Spacing / Height** $\rightarrow$ Search token di `Themes/Tokens/SharedMetrics.xaml`
- **Need Progress / Feedback** $\rightarrow$ Search `NexusProgressBarStyle` di `Themes/Components/Feedback.xaml`

---

### 5.2 Canonical Source of Truth

Setiap domain UI memiliki satu lokasi implementasi resmi (*Single Source of Truth*):

| Domain | Canonical Source Path | Keterangan |
| :--- | :--- | :--- |
| **Primitive Colors** | `src/03_App/Nexus.App/Themes/Tokens/PrimitiveTokens.xaml` | Definisi warna mentah / palette dasar |
| **Metrics / Typography** | `src/03_App/Nexus.App/Themes/Tokens/SharedMetrics.xaml` | Radius, spacing, font sizes, control heights |
| **Semantic Brushes** | `src/03_App/Nexus.App/Themes/Semantic/DarkSemanticTheme.xaml` | Dark/Light theme brushes dinamis |
| **Buttons** | `src/03_App/Nexus.App/Themes/Components/Buttons.xaml` | Primary, Secondary, Subtle, Icon, Danger |
| **Inputs & Selection** | `src/03_App/Nexus.App/Themes/Components/InputControls.xaml` | TextBox, ComboBox, CheckBox, RadioButton, Slider |
| **Feedback** | `src/03_App/Nexus.App/Themes/Components/Feedback.xaml` | ProgressBar, InlineMessage, ValidationMessage |
| **Menus** | `src/03_App/Nexus.App/Themes/Components/Menus.xaml` | Menu, ContextMenu |
| **Overlays** | `src/03_App/Nexus.App/Themes/Components/Overlays.xaml` | ToolTip, Dialog border |
| **Surfaces & Cards** | `src/03_App/Nexus.App/Themes/Components/Surfaces.xaml` | CardStyle, ScrollViewerStyle |
| **Layout & Navigation** | `src/03_App/Nexus.App/Themes/Components/Layout.xaml` | Page region, toolbar region, navigation item |
| **Compatibility Aliases** | `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml` | Thin aliases untuk legacy consumer yang masih aktif |
| **Global Converters** | `src/03_App/Nexus.App/App.xaml` | Value converters resmi tingkat aplikasi |

---

### 5.3 Aturan Klasifikasi & Penanganan Duplikasi

- **EXACT DUPLICATE** $\rightarrow$ **SATUKAN**: Tidak boleh ada dua implementasi dengan properti identik di dua file berbeda.
- **SAME-NAME COLLISION** $\rightarrow$ **SATU IMPLEMENTASI**: Dilarang mengandalkan ResourceDictionary merge order untuk menimpa definisi dengan key yang sama.
- **LEGACY YANG MASIH DIPAKAI** $\rightarrow$ **THIN ALIAS**: Buat alias tipis menggunakan `BasedOn="{StaticResource CanonicalComponent}"`.
- **DEAD RESOURCE** $\rightarrow$ **HAPUS**: Resource tanpa consumer (*direct, indirect, test, reflection = 0*) wajib dibersihkan.
- **INTENTIONAL VARIANT** $\rightarrow$ **PERTAHANKAN**: Hanya jika memiliki perbedaan fungsional / behavior nyata yang didokumentasikan.
- **DIFFERENT IMPLEMENTATION** $\rightarrow$ **JANGAN DIPAKSA MENYATU**: Evaluasi behavior aktual sebelum melakukan refactor.

> Jangan menilai duplikasi hanya berdasarkan nama style. Wajib bandingkan secara mendalam: `TargetType`, `BasedOn`, `Setters`, `ControlTemplate`, `Triggers`, dependencies, dan visual responsibility.

---

### 5.4 Aturan Local View Resource (`Views/*.xaml`)

File `Views/*.xaml` dan `MainWindow.xaml` **dilarang mendefinisikan ulang canonical global style/resource**.

Local resource dalam View hanya diizinkan apabila:
1. Bersifat spesifik terhadap view tersebut (*view-specific layout logic*).
2. Memiliki kebutuhan interaksi lokal yang tidak dapat di-generalisir.
3. Bukan merupakan salinan atau variasi minor dari style global.

**Pola Arsitektur yang Benar:**
```
Canonical Style (Components/*.xaml)
       ▲
       │ BasedOn
Thin Local / Legacy Alias (Hanya bila dibutuhkan)
```

**Pola Arsitektur yang DILARANG:**
```
Canonical Style + Copy Template di View + Copy Template di Legacy + Copy Template di SharedStyles
```

---

### 5.5 Aturan Legacy Compatibility Layer

`Themes/Compatibility/LegacyStyleAliases.xaml` adalah **compatibility layer transisi**, BUKAN tempat implementasi penuh kedua.

Target Arsitektur:
```
Legacy Consumer
       │
       ▼
Legacy Style Name
       │
       ▼ BasedOn
Canonical Style (Themes/Components/*.xaml)
```

Jika suatu legacy alias sudah tidak memiliki consumer aktif (di View, code-behind, maupun governance contracts), resource tersebut tidak boleh dipertahankan hanya demi mempertahankan nama lama.

---

### 5.6 Aturan Runtime Resource Resolution

Kelulusan build (`dotnet build = 0 error`) tidak menjamin bahwa resource visual yang sampai ke runtime sudah benar.

Untuk seluruh resource kritis, rantai resolusi wajib dibuktikan:
```
Source File ➔ MergedDictionaries Order ➔ BasedOn Chain ➔ ControlTemplate ➔ Active Runtime Value
```

Jika source code tampak benar namun tampilan runtime berbeda:
**STOP!** Cari kemungkinan *local resource override*, *style key collision*, atau urutan *merge dictionary* yang saling menimpa.

---

### 5.7 Aturan Pengujian Tata Kelola (Governance Tests)

Suite pengujian `DesignSystemGovernanceTests` dan `NexusUIFoundationTests` bertindak sebagai penjaga permanen arsitektur design system NEXUS.

- **Dilarang memperlemah assertion** hanya agar kode lama atau perubahan instan lulus pengujian.
- **Dilarang menghapus atau menonaktifkan test**.
- Jika arsitektur canonical diperbarui secara sah dan terdokumentasi, **perbarui contract test secara minimal** untuk memvalidasi arsitektur baru tersebut.
- Pengujian harus memverifikasi bahwa:
  1. Standard style aliases me-resolve ke canonical component styles.
  2. Tidak ada duplicate implementation di file yang tidak berhak (`SharedStyles.xaml`).
  3. Resolusi runtime menghasilkan template dan property yang deterministik.

---

### 5.8 Alur Kerja Agent (Standard Agent Workflow)

Setiap AI agent yang bekerja pada project NEXUS wajib mematuhi siklus eksekusi berikut:

```
MASALAH ➔ BUKTI EMPIRIS ➔ KEPUTUSAN ➔ IMPLEMENTASI ➔ BUILD ➔ FOCUSED TESTS ➔ GOVERNANCE TESTS ➔ RUNTIME VERIFICATION ➔ FREEZE
```

> **ATURAN WAJIB REPOSITORI:**  
> **"SEBELUM MEMBUAT STYLE/RESOURCE BARU, WAJIB SEARCH EXISTING IMPLEMENTATION."**

---

### 5.9 Prinsip Kesederhanaan NEXUS (Anti-Proliferation)

Project NEXUS dikembangkan oleh beberapa agent pada linimasa berbeda, sehingga duplikasi historis pernah terjadi. Mulai saat ini:
- **DUPLICATE LOGIC $\rightarrow$ JANGAN DIBUAT**
- **DUPLICATE STYLE $\rightarrow$ JANGAN DIBUAT**
- **DUPLICATE RESOURCE $\rightarrow$ JANGAN DIBUAT**
- **Existing yang cocok $\rightarrow$ REUSE**
- **Existing canonical $\rightarrow$ BASED ON**
- **Legacy yang masih diperlukan $\rightarrow$ THIN ALIAS**

Tidak ada alasan membuat nama-nama seperti `NexusModern2`, `NexusStandard2`, `NexusNewButton`, atau wrapper baru jika style kanonik yang ada telah memenuhi kebutuhan fungsional.

---

## 6. Native C# Document Conversion Engine (NexusConverterEngine)

Modul konversi dokumen pada NEXUS dibangun secara mandiri menggunakan **100% C# native (.NET 8)** tanpa ketergantungan pada pustaka biner *proprietary* tertutup pihak ketiga.

### 6.1 Cakupan Format & Fitur
- **PDF to Word (`.docx`)**: Rekonstruksi tata letak, paragraf, alignment teks, tabel bergaris/borderless, WordArt editable, dan penataan gambar embedded berbasis OpenXML (`DocumentFormat.OpenXml`).
- **PDF to Excel (`.xlsx`)**: Konversi tabel PDF ke spreadsheet native (mendukung mode single-sheet `--single-sheet` atau multi-sheet) berbasis `ClosedXML` / OpenXML.
- **PDF to PowerPoint (`.pptx`)**: Konversi halaman PDF menjadi slide presentasi native OpenXML.
- **Extract Images**: Ekstraksi langsung citra tertanam (*embedded images*) dari stream PDF masukan ke format `.png` tanpa re-rasterisasi halaman.
- **PDF to Images / Rendering**: Render visual halaman PDF ke JPG/PNG dengan DPI dan kualitas terkalibrasi melalui `Docnet.Core` + `SkiaSharp`.

### 6.2 Arsitektur Out-of-Process CLI (`IPdfOfficeToolService`)
Untuk menjaga stabilitas runtime dan mencegah konflik versi grafis (*SkiaSharp Decoupling*), engine konversi dipisahkan sebagai proses CLI mandiri:
- **Aplikasi Desktop (`Nexus.App`)**: Menggunakan SkiaSharp v4.x untuk rendering UI WPF dan visualizer halaman.
- **Converter Engine (`NexusConverterEngine`)**: Menggunakan SkiaSharp v2.88.8 yang terkalibrasi khusus untuk fidelitas font Office, OpenXML vector rendering, dan ekstraksi gambar embedded PDF.
- **Lokasi Distribusi Portabel**: `<EngineRootFolder>\Convert from PDF\NexusConverterEngine.dll`.
- **Perintah CLI Standar**:
  ```powershell
  dotnet "<EngineRoot>\Convert from PDF\NexusConverterEngine.dll" <docx|xlsx|pptx|images> <input.pdf> <output> [--single-sheet] --control-stdin
  ```
- **Atomic Staging & Cancellation**: Hasil konversi di-generate pada direktori *staging* terisolasi (`.nexus-convert-*`) pada volume tujuan sebelum dipindahkan (*atomic rename*) ke direktori akhir. Pembatalan dikirim secara kooperatif via stream `StandardInput` (`cancel\n`).


