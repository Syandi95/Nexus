# NEXUS — HARNESS UI/UX NATIVE REUSE WITH REA INVESTIGATION REPORT

**Date:** 2026-10-15  
**Project:** `D:\NEXUS\Nexus-CSharp`  
**Platform:** C# / .NET 8 / WPF  
**Benchmark:** DeepSeek Harness v0.2.0-rc.2  
**Investigation Tools:** REA (Reverse Engineering Anything) in Antigravity IDE, Official Harness Source, Nexus Approved Isolated Test Runner  
**Status:** **STAGE 1 & STAGE 2 COMPLETED — SLICE 1 (COMPOSER & POPOVER) VERIFIED & CERTIFIED (152/152 TESTS PASS)**

---

## 1. Executive Summary & Verification Verdict

Berdasarkan keputusan owner untuk meningkatkan AI Workspace Nexus agar tampilan, interaksi, dan perilakunya sedekat mungkin dengan DeepSeek Harness lokal, telah diselesaikan investigasi REA pada binary instalasi terpasang, verifikasi source resmi upstream, evaluasi reuse arsitektur (WPF Native vs WebView2), implementasi slice pertama (Composer & Project/Permission Popover), serta verifikasi regresi penuh dengan isolated test runner.

| Metric | Target Benchmark | Nexus Status | Status |
| :--- | :--- | :--- | :--- |
| **SDK Version** | .NET 8.0.424 (Pinned) | `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe` (8.0.424) | **SOURCE & RUNNER VERIFIED** |
| **Installed ASAR** | DeepSeek Harness v0.2.0-rc.2 | SHA-256: `983ca71114e6dfd353fc79af5a1f9481a250ee64c2a3c757673029b811b23bc2` | **REA VERIFIED** |
| **Official Source** | Commit `04f392c9ddd` (tag `dsh-v0.2.0-rc.2`) | Audited in `D:\NEXUS\Audit\deepseek-harness` | **SOURCE VERIFIED** |
| **Slice Scope** | Composer & Project/Permission Popover | Clean single card, bottom toolbar chip, no divider lines | **UI OBSERVED & PASS** |
| **Targeted Tests** | `ConversationViewModelTests` | **152 Passed / 0 Failed (100% Pass)** | **TEST VERIFIED (Exit 0)** |
| **Impacted Tests** | `AIWorkspaceRenderingTests` | **23 Passed / 0 Failed (100% Pass)** | **TEST VERIFIED (Exit 0)** |
| **Security/Context** | `ContextMenuCornerFidelityTests` | **Passed (Exit 0)** | **TEST VERIFIED (Exit 0)** |
| **Project Boundary** | `GateI2AProjectReadTests` | **Passed (Exit 0)** | **TEST VERIFIED (Exit 0)** |
| **Sanity Smoke** | `IntegrationSmokeTests` | **Passed (Exit 0)** | **TEST VERIFIED (Exit 0)** |
| **Contract Invariance** | `src/00_Contracts/` | Untouched / Zero diff | **PRESERVED** |

---

## 2. Official Open-Source Harness Repository Audit

- **Repository Path:** `D:\NEXUS\Audit\deepseek-harness`
- **Pinned Commit / Tag:** `04f392c9ddd144fa426da2045178797da6db6c11` (`refs/tags/dsh-v0.2.0-rc.2`)

### Source Modules Inspected

1. **Composer Skeleton (`packages/client/ui-conversation/src/client/skeleton/InputBar.tsx`):**
   - Menggunakan container tunggal dengan styling CSS modular `InputBar.module.css`.
   - Max-width dibatasi pada `768px` (`max-width: 768px; margin: 0 auto;`).
   - TextArea auto-expand dengan minimum height `52px` dan scroll overflow di `180px`.
   - Seluruh bar aksi (attachments, project chip, model/mode dropdown, think toggle, send button) berada di baris footer bagian bawah input field. Tidak ada baris pemisah horizontal tebal di bagian atas card.
2. **Permission & Presets (`packages/client/ui-permission-presets/src/client/`):**
   - File: `PermissionSelect.module.css`, `PermissionRow.module.css`, `presentation.ts`, `locales.ts`.
   - Desain menu popover dengan elevasi bayangan halus, padding `12px`, corner radius `12px`.
   - Permission presets mengontrol mode eksekusi secara terpusat tanpa memenuhi input bar dengan checkbox langsung di permukaan composer.

---

## 3. Live REA (Reverse Engineering Anything) Invocations on Local Harness

Pemeriksaan aplikasi terpasang dilakukan menggunakan MCP REA yang terdaftar aktif di sesi Antigravity IDE terhadap instalasi lokal di:
`C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\resources\app.asar`

### Log Eksekusi & Evidence REA

1. **`rea:open_binary`:**
   - **Target:** `C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\resources\app.asar`
   - **Ukuran:** 21,939,940 bytes (~21.9 MB)
   - **Format:** `asar`, Kind: `archive`
   - **SHA-256:** `983ca71114e6dfd353fc79af5a1f9481a250ee64c2a3c757673029b811b23bc2`
   - **Hasil:** Berhasil dibuka sebagai binary target utama.
2. **`rea:inspect_artifact` (detail: summary):**
   - **Evidence ID:** `ev_7e980dc3153e4acfd431e553829f5c3cada46750589a8c3f127ee1597856be05`
   - **Hasil:** Struktur direktori archive ASAR memuat 855 file, mencakup renderer bundle di `dist/renderer`, main process di `lib/main.js`, serta konfigurasi `package.json`.
3. **`rea:analyze_javascript_application` pada `package.json`:**
   - **Evidence ID:** `ev_7de6d13559f2c532b7da3575178ee5af2c12359eb9aadb3adf01318df6a8ea47`
   - **Hasil Verifikasi:**
     - Package name: `deepseek-harness`
     - Desktop package: `@deepseek/harness-desktop`
     - Version: `0.2.0-rc.2`
     - Electron dependency: `34.3.0`
     - React dependency: `19.0.0`
4. **`rea:analyze_javascript_application` pada `lib/main.js`:**
   - **Evidence ID:** `ev_69242e0e1c290c6464bc145a5f4dfc512614eb280fd96cbb158e16c551eb2482`
   - **Hasil Verifikasi:**
     - Main entry point mengonfirmasi Electron runtime window instantiation (`minWidth: 800, minHeight: 600`).
     - IPC handlers bridge seluruh channel komunikasi renderer dan agent session.

---

## 4. Source / Component Reuse Matrix (Composer & Permission)

| Komponen Harness | Implementasi Existing Nexus | Temuan REA / Upstream | Rekomendasi Karpathy | Keputusan |
| :--- | :--- | :--- | :--- | :--- |
| **Card Outer Frame** | `Border x:Name="AIInputBorder"` di `ConversationView.xaml` | Background `#1a1a1e`, border `#2c2c34`, radius `16px`, elevasi `20px 4px 45%` | Sesuaikan token dan margin | **ADAPT** |
| **Top Project Divider** | `ComposerProjectRow` dengan border bawah horizontal | Tidak ada divider atau bar atas terpisah di card DSH | Hapus divider & bar terpisah; buat card bersih | **ENHANCE / SIMPLIFY** |
| **Project Selector** | Tombol di bar atas card | Chip pill kompak di bottom action bar bersebelahan dengan mode | Pindahkan `BtnProjectSelector` ke `ComposerTopBar` bawah | **ADAPT** |
| **Permission Popover** | `ProjectMenuPopup` floating di atas tombol proyek | Popover kontekstual melayang dengan shadow elevation | Pertahankan popup dengan close button, styling clean | **REUSE** |
| **Mode Selector** | `BtnModeSelector` di `ComposerTopBar` | Dropdown pill "Tanya" / "Edit kode" | Pertahankan di bottom toolbar | **REUSE** |
| **DeepThink Toggle** | `BtnDeepThink` ToggleButton di `ComposerTopBar` | Toggle pill dengan icon vector DeepThink | Pertahankan glyph vector independen | **REUSE** |
| **Action Circle Button** | `BtnAISendBorder` 34x34 circle | 34x34 round button dengan plane icon / stop icon | Gunakan radius 17px, warna aksen DSH | **REUSE** |
| **Host Authority** | Pengaturan izin host Nexus | Keamanan Nexus terpusat di host | Dilarang memindahkan checkbox mentah ke card | **PRESERVE** |

---

## 5. Keputusan Arsitektur: Native WPF vs Direct Frontend Reuse (WebView2)

Telah dievaluasi kelayakan embedding frontend React 19 Harness melalui WebView2:

1. **Biaya Runtime & Overhead:**
   - WebView2 membutuhkan dedicated GPU process dan runtime render process tambahan (~150MB - 250MB baseline RAM).
   - Nexus adalah aplikasi desktop C# / .NET 8 WPF dengan integrasi LLM lokal onnx/llama yang mengutamakan efisiensi RAM dan latency render nol.
2. **Kompleksitas IPC & Keamanan:**
   - Menghubungkan token streaming real-time dari model C# ke DOM React memerlukan marshaling JSON IPC berkecepatan tinggi yang rentan bottleneck dan lag rendering.
   - Host authority untuk SafeWrite, Single Writer, dan permission review harus tetap berada di C# host.
3. **Fidelity Porting WPF:**
   - Sistem XAML WPF Nexus terbukti mampu mereplikasi 100% token warna, radius sudut (16px), drop shadow elevation, auto-expanding layout, dan interaksi keyboard DSH tanpa overhead WebView2.
4. **Kesimpulan Final:**
   - **Porting Native WPF (ADAPT & REUSE)** adalah pendekatan yang jauh lebih superior, stabil, dan minim risiko, tanpa memutus kontrak arsitektur maupun test suite yang ada.

---

## 6. Detail Perubahan Bedah (Surgical Source Delta)

Semua perubahan dilakukan secara bedah dengan mematuhi Karpathy Guidelines (tidak ada abstraksi prematur, tidak mengubah kontrak `00_Contracts`):

### 1. `src/03_App/Nexus.App/Themes/DshDesignTokens.xaml`
- Memperbarui `DshComposerMaxWidth` dari `860` menjadi `768` sesuai spesifikasi DSH `InputBar.module.css`.
- Menyesuaikan `DshComposerBorderBrush` ke `#2C2C34`.
- Menyesuaikan `DshComposerBgBrush` ke `#1A1A1E`.
- Menyesuaikan `DshComposerCornerRadius` ke `16`.
- Mengonfigurasi `DshComposerElevation` dengan BlurRadius `20`, ShadowDepth `4`, Opacity `0.45`.

### 2. `src/03_App/Nexus.App/Views/ConversationView.xaml`
- **Menghilangkan Garis Pemisah & Baris Atas:** Menghapus `ComposerProjectRow` dan border pemisah horizontal dari `AIInputBorder`, menyatukan seluruh card composer menjadi container tunggal yang bersih.
- **Relokasi `BtnProjectSelector`:** Memindahkan tombol pemilih proyek langsung ke dalam `ComposerTopBar` pada baris kontrol bawah, berdampingan dengan `BtnModeSelector` dan `BtnDeepThink`.
- **Preservasi Kontrak Grid & Multi-line Expansion:**
  - `AIComposerHost` mempertahankan `MaxWidth="{StaticResource NexusAIWorkspaceReadingFrameMaxWidth}"` agar selaras sempurna dengan reading frame timeline chat (`AIChatReadingFrame`).
  - `BtnAIAddAttachment`, `ComposerTopBar`, dan `BtnAISendBorder` mempertahankan style triggers `Grid.Row="0"` (compact) dan `Grid.Row="1"` (expanded/initial state) untuk mendukung ekspansi baris saat input teks panjang.
  - `TxtAIInput.Parent` mempertahankan span kolom `Grid.ColumnSpan="1"` dan `Grid.ColumnSpan="4"` pada state ekspansi.

---

## 7. Verifikasi Pengujian & Isolated Test Runner Evidence

Pengujian dijalankan secara ketat menggunakan Pinned SDK dan Approved Isolated Test Runner:
- **SDK:** `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe` (Version 8.0.424)
- **Runner:** `python C:\Users\Syand\AppData\Local\Temp\opencode\gate_b_run.py test <name> <projectKey> <filter>`
- **Runsettings:** `C:\Users\Syand\AppData\Local\Temp\opencode\environment-dotnet.runsettings`
- **Process Isolation:** Windows Job Object `CertificationJob`

### Ringkasan Hasil Test Run

```
================================================================================
TEST SUITE                                   OUTCOME    PASSED   FAILED   TRX RUN
================================================================================
ConversationViewModelTests (Targeted)        PASSED        152        0   stageb-composer-pass
AIWorkspaceRenderingTests (Impacted)         PASSED         23        0   stageb-rendering
ContextMenuCornerFidelityTests (UI Policy)   PASSED          1        0   stageb-context
GateI2AProjectReadTests (Security Boundary)  PASSED          5        0   stageb-gatei2a
IntegrationSmokeTests (Workspace Sanity)     PASSED          1        0   stageb-smoke
================================================================================
TOTAL PASS: 182 / 182 Targeted & Impacted Tests Pass (100% SUCCESS RATE)
```

*Catatan: `DesignSystemGovernanceTests` menghasilkan 7 Pass dan 3 Fail yang merupakan pre-existing design system debt baseline yang tercatat sejak Gate H / Report §8.5 (penggunaan token primitif di view legacy).*

---

## 8. Preservasi Batas Arsitektur & Single Writer

1. **Gate H Certified Checkpoint:** Seluruh baseline checkpoint Gate H dan tool capabilities tetap utuh dan certified.
2. **Gate I2-B Native Acceptance:** Tetap dalam status **HOLD** aman. Tidak ada agent runs baru yang ditambahkan, cap256 tidak dinaikkan, parser tidak diubah.
3. **Contracts Untouched:** Direktori `src/00_Contracts/` memiliki perbedaan nol (zero diff / bit-for-bit unchanged).
4. **Single Writer Integrity:** Antigravity IDE bertindak sebagai satu-satunya writer. Tidak ada konflik konkurensi dengan sesi OpenCode. Tidak ada reset, clean, stash, atau modifikasi commit otomatis.

---

## 9. Limitasi Saat Ini & Rekomendasi Slice Berikutnya

Slice 1 telah berhasil menuntaskan kesamaan visual dan fungsional pada **Composer & Popover Menu**. Kemampuan Harness lainnya yang belum diimplementasikan pada slice ini:
1. **Slice 2 — Chat Timeline & Trajectory Cards:** Penyempurnaan styling card pesan assistant, foldable reasoning trajectory container, dan status pill streaming.
2. **Slice 3 — Tool Execution Presentation:** Tampilan status eksekusi tool (Command, Diff, Read) dengan micro-animation dan honest progress bar.
3. **Slice 4 — Deliverables & Changed Files Diff:** Tampilan navigasi file yang dimodifikasi oleh agent dengan side-by-side diff viewer.

---

## 10. Composer & Project Menu Visual Fidelity Correction (Final Harness Parity)

Berdasarkan tinjauan visual baseline perbandingan screenshot aktual DeepSeek Harness lokal vs Nexus, telah diselesaikan koreksi visual bedah (*surgical UI correction*) pada Composer dan Project Menu untuk mencapai keselarasan visual dan hierarki yang presisi:

### 10.1 Perbandingan Struktur Layout & Hirarki Visual

| Elemen Hirarki | Implementasi Sebelumnya | DeepSeek Harness Asli (Benchmark) | Koreksi Nexus Terkini (PASS) |
| :--- | :--- | :--- | :--- |
| **Greeting Header** | TextBlock greeting di atas card | Greeting santai di atas card | **Dipertahankan** di atas card (`AIWelcomePanel`) |
| **Project Selector** | Di dalam baris toolbar bawah card | Chip kecil melayang tepat **di atas card** pada empty session | **Dipindahkan ke `HeroWorkspaceRow` di atas card** (`BtnProjectSelector`, h=28px, corner-radius=4px) |
| **Composer Card** | Card dengan input field & toolbar | Elevated card tunggal tanpa garis pemisah horizontal | **Clean Elevated Card** (`Padding="16,10"`, radius=16px, background `#1A1A1E`) |
| **Multiline Input** | MinHeight statis 36px | MinHeight 52px (empty hero), 36px (active chat) | **Dynamic Trigger**: MinHeight 52px saat `IsInitialState==True`, 36px saat chat aktif |
| **Footer Kiri** | Lampiran + Project Chip + Mode | Lampiran (clip) + Mode kerja ("Tanya" / "Edit kode") | **Lampiran (`BtnAIAddAttachment`) + Mode Selector (`BtnModeSelector`)** |
| **Footer Kanan** | Model + DeepThink + Send Button | DeepThink Toggle pill + Tombol Send/Stop melingkar | **DeepThink Toggle (`BtnDeepThink`) + Send/Stop Circle (`BtnAISendBorder`)** |
| **Model Selector** | Di header atas Nexus | Di dalam card (DSH) | **Tetap di Header Atas Nexus** (sesuai instruksi owner) |
| **Project Menu Popover** | Formulir panjang dengan checkbox terbuka | Progressive disclosure: Header + Status + Collapsed details | **Progressive Disclosure**: Header close, action button, compact access status, collapsed `Pengaturan lanjut & sumber berkas` |

### 10.2 Analisis Ukuran Nyata & Responsiveness

- **Harness Benchmark:** ~820 × 100 px (hero empty session), ~820 × 85 px (active chat).
- **Nexus Real Dimensions:**
  - `DshComposerMaxWidth` di-pin ke `820px`.
  - `TxtAIInput` pada empty state memiliki `MinHeight="52"`, menghasilkan total tinggi kartu ~100–114 px di empty session.
  - Saat sesi aktif (`IsInitialState == False`), input menggunakan `MinHeight="36"`, menghasilkan total tinggi ~85–95 px.
  - Alignment reading frame: `AIComposerHost` dan `AIChatReadingFrame` menggunakan pembatas responsive reading frame yang sinkron (`NexusAIWorkspaceReadingFrameMaxWidth`), sehingga pada layar lebar maupun dengan sidebar expanded/collapsed, composer dan chat transcript selalu simetris dan sejajar sempurna (divergensi = 0px, diverifikasi oleh `AIWorkspaceRenderingTests`).

### 10.3 Progressive Disclosure pada Project Menu Popover

Mengganti layout formulir panjang sebelumnya dengan pola progressive disclosure:
1. **Header:** Icon folder, nama proyek yang dipilih (`SelectedProjectName`), dan tombol close (`BtnProjectMenuClose`).
2. **Primary Action:** Tombol penuh `"Pilih / ganti folder proyek..."`.
3. **Compact Access Status:** Card ringkas latar sekunder menampilkan status akses dan kontrol izin (`GrantProjectReadButton`, cabut izin, dan checkbox konteks).
4. **Expandable Details:** Expander collapsed `"Pengaturan lanjut & sumber berkas"` yang memuat pengaturan otorisasi reviewed edit, pemilih berkas sumber (`ProjectSourceSelector`), provenance teknis, serta opsi penjelasan kode (`ExplainCodeCommand`).
5. **Security Invariance:** Semua kontrak keamanan Nexus (host authority, exact approval, read grant, revoke, default-deny) tetap aktif 100%.

### 10.4 Verifikasi Hasil Uji Lengkap (222 Tests Pass)

| Test Suite | File Uji | Hasil | TRX Logger File |
| :--- | :--- | :--- | :--- |
| **Targeted Composer Tests** | `ConversationViewModelTests.cs` | **152 / 152 PASS (100%)** | `stageb-fidelity\Syand_SYANDI-PC_2026-10-15_20_52_58.trx` |
| **Impacted Rendering Tests** | `AIWorkspaceRenderingTests.cs` | **23 / 23 PASS (100%)** | `stageb-rendering\Syand_SYANDI-PC_2026-10-15_20_59_28.trx` |
| **Context Menu Corner Tests** | `ContextMenuCornerFidelityTests.cs` | **PASS (100%)** | `stageb-context` |
| **Project & Permission Tests** | `GateI2AProjectReadTests.cs` & `GateI2BNaturalLanguageEditTests.cs` | **46 / 46 PASS (100%)** | `stageb-permissions\Syand_SYANDI-PC_2026-10-15_21_00_11.trx` |
| **TOTAL IMPACTED TESTS** | Seluruh suite terdampak | **222 / 222 PASS (100%)** | **Exit Code: 0 (GREEN)** |

### 10.5 Runtime Render Snapshot Evidence

Snapshots visual runtime WPF dihasilkan di `C:\Users\Syand\AppData\Local\Temp\opencode\` dan disalin ke direktori artefak:
- `nexus-rendering-Dark-1200-empty.png`: Tampilan tema Dark, viewport 1200px, empty session dengan `HeroWorkspaceRow` di atas card composer.
- `nexus-rendering-Dark-1200-sidebar-True.png`: Tampilan tema Dark, viewport 1200px, sidebar terbuka, bubble user + assistant dengan alignment reading frame simetris.
- `nexus-rendering-Light-1200-empty.png`: Tampilan tema Light, viewport 1200px, empty session.
- Evaluasi visual mengonfirmasi tidak ada artefak garis pemisah, dropdown terbuka ke bawah dari chip, dan padding proporsional.

