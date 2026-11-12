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

## 4. Status Fase Migrasi Saat Ini

- [x] **Fase 0A (Formal Performance Baseline Benchmark)**: SELESAI & DIBEKUKAN (`powershell_v1.7_baseline.json`).
- [ ] **Fase 0B (Contract & Schema Freeze)**: BERJALAN (Penyiapan DTO C# & Skema JSON di `docs/contracts/`).

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

## 6. Experimental iLovePDF / Solid Documents Converter Dependencies

Bagian ini mendokumentasikan pustaka biner native yang dipentaskan (*staged*) secara terisolasi ke direktori eksperimen lokal `D:\NEXUS\Engine\iLovePDF-SolidConverters-Experimental` untuk keperluan riset kelayakan fitur konversi dokumen:
- **TOOL-01:** PDF to Word (`.docx`)
- **TOOL-02:** PDF to Excel (`.xlsx`)
- **TOOL-03:** PDF to PowerPoint (`.pptx`)
- **TOOL-05:** Extract Images dari PDF (ekstraksi citra tertanam, bukan re-rasterisasi halaman)

> [!IMPORTANT]
> **Pengecualian Mutlak & Komitmen Arsitektur:**
> 1. **TOOL-04 (PDF to JPG):** Dikeluarkan sepenuhnya dari scope eksperimen dependensi baru. Fitur PDF to JPG pada NEXUS tetap wajib menggunakan `Docnet.Core` + `SkiaSharp` tanpa perubahan engine, alur, atau refactoring.
> 2. **Ekspor PDF/PNG NEXUS:** Tetap dipertahankan tanpa perubahan apa pun.
> 3. **TOOL-06 (PDF/A):** Dikecualikan. Pustaka `PDFNetC.dll` dan kunci OEM komersial Apryse/PDFTron **tidak disalin**.
> 4. **Status Repositori Source Code:** Penambahan seksi README ini adalah satu-satunya perubahan dalam repositori NEXUS. Tidak ada source code, project file, atau package reference C# yang dimodifikasi.

### 6.1 Informasi Paket Sumber & Lokasi Staging

- **Paket Sumber:** Microsoft Store `ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_2.3.0.0_x64__0q01bhdkqq106`
- **Path Sumber Asal:** `C:\Program Files\WindowsApps\ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_2.3.0.0_x64__0q01bhdkqq106\iLovePDF`
- **Folder Tujuan Lokal:** `D:\NEXUS\Engine\iLovePDF-SolidConverters-Experimental`
- **Arsitektur Host:** Windows x64 (PE32+)

### 6.2 Manifest Detail Berkas yang Dipentaskan

| Nama File | Ukuran (Bytes) | Arsitektur | Versi File / Produk | SHA-256 Digest | Pemilik / Vendor Terbukti | Fungsi Teknis Terverifikasi | Status Penggunaan |
|---|---|---|---|---|---|---|---|
| `ConverterCoreLight.dll` | 272.920 | x64 | 10.0.19910.1 | `9AF05F36DB2DF6C0C080FE9B60A37B56C563CBFBFC4585BF20B5087E7F5646FA` | Solid Documents Limited | Procedural C API Wrapper untuk `pdfToWordConverter`, `pdfToExcelConverter`, `pdfToPowerPointConverter`, dan `PdfConverterExtractImages`. | **UNKNOWN** |
| `SolidCore.dll` | 1.213.464 | x64 | 10.0.19910.1 | `F02C1ED04FCC5770DCCFF11C67D74F07F8245C446C5776671D60E2FAA9A7B1C9` | Solid Documents Limited | Core engine rekonstruksi layout dokumen Solid Documents. | **UNKNOWN** |
| `SPal.dll` | 1.073.176 | x64 | 10.0.19910.1 | `BC6A3D57058C0FD0CB60A70A3FC6414321F8FC298314589C1BA1174C9CD9B5B8` | Solid Documents Limited | Platform Abstraction Layer (filesystem, memori, platform interop). | **UNKNOWN** |
| `dbCore.dll` | 139.800 | x64 | 10.0.19910.1 | `FF085EB815525C0861070AD25177D1ED4AF42237244E0CF83D531B8D99D77693` | Solid Documents Limited | Database internal objek dokumen dan pelacak riwayat model. | **UNKNOWN** |
| `IMAGETOOL.dll` | 1.353.752 | x64 | 10.0.19910.1 | `D51E759A512A8A2956104016849E7B8701B16402E43BDD074960B5FBBF71A97C` | Solid Documents Limited | Dekoder citra, manipulasi raster, dan ekstraksi gambar biner (TOOL-05). | **UNKNOWN** |
| `PDFLibTool.dll` | 6.548.504 | x64 | 10.0.19910.1 | `6222B54F9F3AE3189636D35A07708BDCD7690588BE57FFA735C324EA6F1D0F4B` | Solid Documents Limited | Parser struktur biner objek PDF dan resolusi font/vektor tingkat rendah. | **UNKNOWN** |
| `SolidLanguage.dll` | 6.681.624 | x64 | 10.0.19910.1 | `7F215B4776043FC7452CF12E32100D477BF299B97529071743A252FABB7ED49D` | Solid Documents Limited | Modul tipografi, pemenggalan kata, dan kamus multibahasa. | **UNKNOWN** |
| `DOCXFlt.flt` | 572.952 | x64 | 10.0.19910.1 | `08B902D4A5241A108F0859903C2F764A701E35EE0AF130CDB5E6D1743F4A05F4` | Solid Documents Limited | Filter penulisan format Microsoft Word OpenXML (`.docx`) via `GetFilter`. | **UNKNOWN** |
| `XlsxFlt.flt` | 455.192 | x64 | 10.0.19910.1 | `9916349B70ECF957DF960B81D3833FE8F3CB32B2C21A637FF7514960145E542B` | Solid Documents Limited | Filter penulisan format Microsoft Excel OpenXML (`.xlsx`) via `GetFilter`. | **UNKNOWN** |
| `CsvFlt.flt` | 90.648 | x64 | 10.0.19910.1 | `90D480662458BE57302ABDE3B9A776E1CC10DEF4FE2E0FC18D20F42D8C850D64` | Solid Documents Limited | Filter penulisan format tabel CSV via `GetFilter`. | **UNKNOWN** |
| `PptxFlt.flt` | 525.336 | x64 | 10.0.19910.1 | `697361375288CD93BB80CD5A7E72E2BDABFCDD7F807FD585FD5C1531819A4AF5` | Solid Documents Limited | Filter penulisan slide Microsoft PowerPoint OpenXML (`.pptx`) via `GetFilter`. | **UNKNOWN** |
| `PdfFlt.flt` | 8.495.640 | x64 | 10.0.19910.1 | `4151A73575E8FF41E0D031B8F9866DA271927032A95B0F133D84C5F0CADCBFD1` | Solid Documents Limited | Filter dekomposisi struktur konten PDF masukan via `GetFilter`. | **UNKNOWN** |
| `PdfMeta.dll` | 584.728 | x64 | 10.0.19910.1 | `D760F4DD16F0654DBDC4D91B5C1232FACA5F2041D93B4FD6A31ACA732EEE22C4` | Solid Documents Limited | Ekstraktor metadata XMP/DocInfo dokumen PDF. | **UNKNOWN** |
| `SecurePdfSDK.dll` | 1.082.392 | x64 | 10.0.19910.1 | `1EBEEAE33A2CCB988A4781650A0FA604E03FA43258C262CB6705B0287C955CF7` | Solid Documents Limited | Penanganan keamanan enkripsi PDF (Standard Security Handler / password). | **UNKNOWN** |
| `resources\commonwidths.dat` | 28.806 | N/A | N/A | `DFB768A6518A3F2F86CD1538A0EB24C6B3B3C18FDC334EB2889DF9E2C53521BE` | Solid Documents Limited | Tabel metrik lebar glif standar font. | **UNKNOWN** |
| `resources\GenericRGBProfile.icc` | 1.320 | N/A | N/A | `EB03DB58FF1F226C83103A11F30B5520F9B68A7CED67DAA78992723E3EA0411D` | Standard ICC | Profil warna RGB standar rendering citra. | **UNKNOWN** |
| `resources\SolidLists.pdf` | 2.997.212 | N/A | N/A | `94E418DDB812A72639A609F0A403AB064F94C8C5B2170231C5D4097239F0C0B7` | Solid Documents Limited | Data template deteksi pola list dan bullet point. | **UNKNOWN** |
| `resources\SolidOCR.pdf` | 17.777.808 | N/A | N/A | `C46BC59CE4F0952A24DD823D7A1000712C05C1F478C34C7B3EEE69D1F4285094` | Solid Documents Limited | Data referensi karakter untuk OCR dan text recovery. | **UNKNOWN** |
| `resources\CMap\*` (187 berkas) | 9.518.094 | N/A | N/A | Hash per berkas terverifikasi | Adobe / Solid Documents | Tabel character mapping Unicode untuk font CMap (Asia Timur & Simbol). | **UNKNOWN** |

### 6.3 Status Ketentuan Lisensi & Temuan Hukum

- **Teks Hak Cipta yang Ditemukan:** Seluruh biner memuat metadata sumber: `Copyright (C) 2000-2026 Solid Documents Limited. All rights reserved.`
- **Pemeriksaan Status Runtime:** Fungsi internal `SolidFramework_License_GetType()` mengembalikan nilai `4`, dan `SolidFramework_License_FreeOk()` bernilai `1` (mode free tier / evaluasi bawaan aktif). Namun, tidak ditemukan berkas lisensi publik (misalnya MIT, Apache, atau BSD) yang mengizinkan redistribusi bebas.
- **Ketentuan yang Belum Terverifikasi:** Syarat formal penggunaan API prosedural di luar aplikasi iLovePDF tanpa kunci lisensi komersial belum diketahui secara pasti (`UNKNOWN`).
- **Pernyataan Kepatuhan:**
  > Salinan berkas di atas ditempatkan semata-mata untuk eksperimen teknis pribadi (*personal research sandbox*). Fakta bahwa eksperimen bersifat pribadi **TIDAK** otomatis memberikan hak lisensi komersial atau izin redistribusi publik.
  > Dilarang keras melakukan cracking, menghilangkan proteksi biner, mengakali mekanisme aktivasi, mengekstrak kunci lisensi terenkripsi, atau memalsukan identitas aplikasi.


