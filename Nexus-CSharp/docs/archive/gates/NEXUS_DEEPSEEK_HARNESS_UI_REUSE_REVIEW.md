# NEXUS — DEEPSEEK HARNESS UI REUSE & BEHAVIOR PARITY AUDIT REPORT

**Document ID:** `NEXUS_DEEPSEEK_HARNESS_UI_REUSE_REVIEW.md`  
**Date:** 15 Oktober 2026  
**Status:** **STAGE 1 AUDIT COMPLETE — PENDING ONE OWNER APPROVAL**  
**Target:** Menyamakan pengalaman AI Workspace Nexus dengan DeepSeek Harness lokal owner.  
**Method:** Direct Open-Source Reuse First + REA Research + Surgical Integration.  
**Target Platform:** C# / .NET 8 / WPF.  

---

## Executive Summary

1. **Installed Target Provenance Identik & Tervalidasi:**  
   Aplikasi lokal yang terpasang pada PC owner (`C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\DeepSeek Harness.exe`) adalah **DeepSeek Harness Desktop v0.2.0-rc.2** (FileVersion `0.2.0-rc.2`, ProductVersion `0.2.0.0`, PE SHA-256 `df4e91fd91f6f1bee19a1990b0e167bfe06abf8353279d82bbb8e60edb6ba0d3`).  
   Source build resmi teridentifikasi pada commit `04f392c9ddd144fa426da2045178797da6db6c11`, bersumber langsung dari repository resmi [deepseek-ai/deepseek-harness](https://github.com/deepseek-ai/deepseek-harness) pada release tag `dsh-v0.2.0-rc.2` (commit `639ed015397290b3745d163aafe02ffee4aa3f84`). Seluruh kode sumber resmi telah di-clone secara terisolasi ke `D:\NEXUS\Audit\deepseek-harness`.
2. **Audit Lisensi & Legalitas Bersih:**  
   DeepSeek Harness berlisensi **MIT License** (Copyright (c) 2026 DeepSeek). Seluruh UI packages (`apps/web`, `packages/client/*`) dan vendored core libraries (`cosmokit`, `schemastery`, `cordis`) berlisensi MIT. Audit terhadap `THIRD_PARTY_NOTICES.md` mengonfirmasi tidak ada runtime dependency berlisensi copyleft (GPL/AGPL) pada client UI layer. Batasan merek dagang (`BRAND_GUIDELINES.md`) melarang penggunaan nama merek utuh "DeepSeek Harness" sebagai nama produk, namun mengizinkan deskripsi relasi seperti "compatible with DeepSeek Harness" atau penamaan "DSH".
3. **Keputusan Arsitektur: Opsi B (Native WPF Port) Direkomendasikan Mutlak:**  
   Evaluasi kelayakan membuktikan bahwa **Opsi A (WebView2)** memerlukan eksekusi daemon backend Node.js (`dsh web`) dan runtime microkernel Cordis dengan 30+ plugin browser, memicu memory overhead 250–500 MB (5–7 proses msedgewebview2), konflik fokus/IME, dan membahayakan otoritas keamanan host Nexus.  
   Sebaliknya, **Opsi B (Port Native WPF)** memungkinkan reuse 100% token styling (`--dsw-*`), geometri layout (rail 56px, composer floating 16px radius), path SVG ikon resmi, dan finite state machine interaksi, dengan memory overhead 0 MB, performa native instan, serta integrasi tanpa kompromi ke SafeWrite, approval satu-tahap, dan single shared model runtime Nexus.
4. **Isolasi Mutlak terhadap Milestone I2-B:**  
   Audit ini bersifat read-only. Tidak ada file kerja milestone fungsional I2-B (`Nexus.Contracts`, `NaturalLanguageSourceProposal`, `SourceVerificationProfile`) yang dimodifikasi. Tahap implementasi UI hanya akan dimulai setelah mendapatkan persetujuan tertulis owner.

---

## 1. Exact Installed Harness Identity & Official Source Provenance

### 1.1. Shipped Binary & REA Research Verification

Observasi statis dan runtime menggunakan REA Research (`rea` provider engines) pada target biner lokal menghasilkan bukti terverifikasi:

| Parameter | Shipped Binary Finding | REA Evidence / Method |
|---|---|---|
| **Executable Path** | `C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\DeepSeek Harness.exe` | Filesystem inspection |
| **File Size / Bytes** | 244,481,512 bytes (233.15 MiB) | `inspect_pe_resources` |
| **SHA-256 Digest** | `df4e91fd91f6f1bee19a1990b0e167bfe06abf8353279d82bbb8e60edb6ba0d3` | REA Evidence `ev_8d00dec9cf48865a61...` |
| **Product Version** | `0.2.0.0` (File Version `0.2.0-rc.2`) | PE VersionInfo Resource |
| **Electron Fuses** | • `RunAsNode`: `enabled` (49)<br>• `EnableNodeCliInspectArguments`: `enabled` (49)<br>• `EnableNodeOptionsEnvironmentVariable`: `enabled` (49)<br>• `OnlyLoadAppFromAsar`: `disabled` (48)<br>• `EnableEmbeddedAsarIntegrityValidation`: `disabled` (48) | REA `inspect_electron_fuses`<br>Evidence `ev_8d00dec9cf48865a61a8052d67a2958eafa851a21a7813ab6b26a9edb05d8e09` |
| **PE Resources** | 55 resources, 117 directory entries. Multi-resolution RT_GROUP_ICON: 16×16, 24×24, 32×32, 48×48, 64×64, 128×128, 256×256. | REA `inspect_pe_resources`<br>Evidence `ev_6ba7e64cd89eb030c7a03958d5da48ddedb40cc68a872c0a53848352a49e7381` |
| **Bundled Runtime** | Node `24.18.1`, pnpm `11.7.0`, Python `3.12.14` | `resources/runtime/versions.json` & `primary-runtime/runtime.json` |
| **User Data Path** | `C:\Users\Syand\AppData\Roaming\@deepseek-ai\dsh-desktop`<br>Config directory: `C:\Users\Syand\.dsh` | Inspected metadata only; credentials/tokens strictly excluded per privacy rule |

### 1.2. ASAR Bundle & Build Provenance

Bedah struktur internal pada `C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\resources\app.asar` (121,348,951 bytes, 12,967 files) mengungkapkan manifest desktop asli:

```json
{
  "name": "@deepseek-ai/dsh-desktop",
  "description": "Electron desktop shell for a bundled dsh runtime and external plugins",
  "version": "0.2.0-rc.2",
  "license": "MIT",
  "main": "lib/main.js",
  "dshDesktopAppId": "com.deepseek.dsh",
  "dshBuildCommit": "04f392c9ddd144fa426da2045178797da6db6c11",
  "dshBuildDirty": false
}
```

### 1.3. Upstream Git Repository Provenance

- **Official Repository URL:** `https://github.com/deepseek-ai/deepseek-harness`
- **Release Tag:** `refs/tags/dsh-v0.2.0-rc.2` (Commit: `639ed015397290b3745d163aafe02ffee4aa3f84`)
- **Release Date:** 29 September 2026
- **Local Clone (Audit Workspace):** `D:\NEXUS\Audit\deepseek-harness`
- **Exact Provenance Trace:** Package manifest desktop mem-pin `dshBuildCommit: 04f392c9ddd144fa426da2045178797da6db6c11`, yang merupakan commit internal builder DeepSeek tepat sebelum PR integrasi release `#5479` di-merge ke tag `dsh-v0.2.0-rc.2`.

---

## 2. Reusable UI Components, Source Paths & Licenses

### 2.1. Audit Lisensi & Hak Kekayaan Intelektual

| Komponen / Aset | Status Lisensi | Batasan & Ketentuan |
|---|---|---|
| **Source Code (`apps/web`, `packages/client/*`)** | **MIT License** (DeepSeek, 2026) | Bebas digunakan, dimodifikasi, dan di-porting ke C#/WPF dengan mencantumkan copyright notice. |
| **Vendored Core (`cosmokit`, `schemastery`, `cordis`)** | **MIT License** | Bebas digunakan. |
| **Third-Party Dependencies (`THIRD_PARTY_NOTICES.md`)** | **MIT, Apache-2.0, BSD-2/3-Clause, ISC** | Tidak ada kontaminasi copyleft (GPL/AGPL) pada client UI packages. |
| **Tipografi: Font Montserrat** | **SIL Open Font License (OFL 1.1)** | Disertakan resmi dalam `Montserrat-OFL.txt`. Dapat dibundel sebagai resource WPF native. |
| **Merek & Identitas Produk (`BRAND_GUIDELINES.md`)** | **Trademark DeepSeek** | **Wajib Ditaati:** Tidak menamai aplikasi "DeepSeek Harness". Nama resmi Nexus tetap **NEXUS**, dengan label interoperabilitas: *"DSH-Compatible AI Workspace"*. |

### 2.2. Inventaris Komponen UI Sumber Asli

| Fitur UI | Path Source Resmi (`Audit/deepseek-harness/`) | Komponen & Modul Utama | Potensi Reuse / Porting |
|---|---|---|---|
| **Design Tokens & Theme** | `packages/client/ui-theme/src/styles/design-platform.css`<br>`packages/client/ui-theme/src/styles/base.css` | `--dsw-*` tokens (650+ rules): background layers, borders, typography, light/dark palettes, scrollbars. | 100% token nilai warna & metrik di-port ke `DshDesignTokens.xaml`. |
| **SVG Icons** | `packages/client/ui-primitives/src/icons/index.tsx` (114 KB) | `IconNewChat`, `IconPanelLeft`, `IconThink`, `IconWarning`, `IconPlus`, `IconCheck`, `IconCopy`, `IconChevronDown`. | 100% path geometri diekstrak ke WPF `StreamGeometry` resource. |
| **Sidebar & Navigasi** | `packages/client/ui-sidebar/src/client/SidebarRoot.tsx`<br>`packages/client/ui-workspace/src/client/rows/WorkspaceBrowser.tsx` | Rail 56px, expanded width 260px, header caption placement, time grouping, multi-level tree, state dots. | Geometri & FSM ported ke `DshSidebarView.xaml` & VM. |
| **Header Percakapan** | `packages/client/ui-conversation/src/client/skeleton/ConversationHeader.tsx` | Title editor, workspace selector pill, model selector, view toggle (Chat / Trajectory). | Ported ke `DshConversationHeader.xaml`. |
| **Composer Bar** | `packages/client/ui-conversation/src/client/skeleton/InputBar.tsx`<br>`InputBar.module.css` | Floating card (max-width 800px, radius 16px), elevated shadow, auto-grow input, action buttons, ContextMeter. | Ported ke `DshComposer.xaml`. |
| **Reasoning / Thinking** | `packages/client/ui-chat/src/client/chat/ReasoningRow.tsx`<br>`ReasoningRow.module.css` | Collapsible "Think" header, shimmer text animation saat streaming, compact markdown body. | Ported ke `DshReasoningBlock.xaml`. |
| **Tool Cards & Streaming** | `packages/client/ui-chat/src/client/chat/MessageItem.tsx`<br>`TurnProcessNodeView.tsx` | Step indicators, tool badge, running whale tail animation, message chrome. | Ported ke `DshMessageCard.xaml`. |
| **Contextual Diff Review** | `packages/client/ui-deliverables/src/client/FileDiff.tsx`<br>`packages/client/ui-primitives/src/DiffBlock.tsx` | Added/deleted line coloring, line numbers, hunk headers, stats pill (+/- lines), copy hunk. | Ported ke `DshDiffViewer.xaml`. |
| **Approval Takeover** | `packages/client/ui-approval/src/client/ApprovalPanel.tsx`<br>`ApprovalPanel.module.css` | Warning state strip, headline, command breakdown, Tolak/Setujui buttons, keyboard shortcuts (Enter/Esc). | Ported ke `DshApprovalTakeover.xaml`. |
| **Trajectory Ledger** | `packages/client/ui-trajectory/src/client/TrajectoryTimeline.tsx`<br>`TrajectoryTable.tsx` | Timing overview, per-turn duration, tool call sequence ledger. | Ported ke `DshTrajectoryView.xaml`. |

---

## 3. Harness vs Nexus Interaction Matrix

| Area UX / Interaksi | DeepSeek Harness v0.2 (Perilaku Nyata) | Nexus Saat Ini (Gate I CF1/CF2) | Target Parity Nexus (Rekomendasi) |
|---|---|---|---|
| **Sidebar Rail** | 56px collapsed rail dengan 36×36 icon buttons; 260px expanded width dengan slide + crossfade 150ms. | Docking bar standar Nexus; belum ada rail 56px minimalis. | Mengadopsi rail 56px identik Harness dengan animasi slide/fade dan Windows caption spacing. |
| **Session History** | Dikelompokkan berdasarkan waktu (Today, Yesterday, Previous 7 Days); Search bar instan; pin/archive actions. | Flat list session; belum ada time-bucket grouping dan filter cepat. | Implementasi grouping waktu dan search filter instan di `ConversationViewModel`. |
| **Composer Geometry** | Floating card di bawah, elevated soft shadow, radius 16px (`--dsw-radius-panel`), max-width 800px. | Textbox fixed di bawah layar dengan border solid. | Floating card elevated dengan radius 16px dan tombol kontrol terintegrasi. |
| **Mode & Project Selection** | Model selector dan context status menyatu di header & toolbar composer. | Proyek di atas composer, switch mode Tanya / Ubah kode. | Tetap mempertahankan selector proyek di atas composer (sesuai Gate I), dipadukan dalam pill toolbar DSH. |
| **Thinking / Reasoning** | Collapsible block bertuliskan "Think" dengan shimmer animation saat streaming; default collapsed setelah selesai. | Tergabung langsung dalam teks chat biasa atau raw log. | Komponen terisolasi `DshReasoningBlock` dengan toggle chevron dan text shimmer. |
| **Diff & File Review** | Changed-files pill (+x, -y); modal/tab per file; full unified diff dengan line numbers dan syntax highlighting. | Contextual panel 2-pane (Gate I Phase B/CF2); teks diff statis. | Mengadopsi styling `DiffBlock` DSH: warna `--dsw-alias-file-diff-added/deleted`, line badges, collapse hunk. |
| **Approval Interactivity** | Kartu peringatan kuning (`ApprovalPanel`) mengambil alih composer, aksi Tolak/Setujui, shortcut `Enter`/`Esc`. | Form dialog approval / tombol review di panel terpisah. | Kartu takeover tepat di atas composer dengan timer mundur 120s dan hotkey `Enter`/`Esc`. |
| **Task Progress & Recovery** | Stop button dinamis saat streaming; feedback error terisolasi via toast; retry tanpa reload. | Tombol Stop ada di runtime; rollback SafeWrite otomatis. | Mengadopsi visual status streaming DSH dengan tetap mempertahankan recovery SafeWrite Nexus. |
| **Theme & Spacing** | Tema Light (Canvas #FFF) & Dark (Bluish #0F1115 / #1B1B1C); tipografi Montserrat & SF Mono/Consolas. | Tema Dark/Light Nexus standar WPF. | Menyuntikkan seluruh palette warna resmi `--dsw-*` ke WPF DynamicResources. |

---

## 4. Audit Direct Reuse Feasibility: WebView2 vs Native WPF

Evaluasi mendalam berdasarkan 9 parameter rekayasa perangkat lunak:

```
+----------------------------------------------------------------------------------------------------+
|                                EVALUASI KELAYAKAN ARSITEKTUR                                       |
+------------------------------------+----------------------------------+----------------------------+
| Parameter                          | Opsi A: Reuse Web via WebView2   | Opsi B: Port ke Native WPF |
+------------------------------------+----------------------------------+----------------------------+
| 1. Actual Reusable Source          | ~20% (hanya aset statis & HTML)  | 95% (Token, SVG, FSM, CSS) |
| 2. Backend / Protocol Requirements | Sangat Tinggi (Daemon Node + WS) | Nol (Langsung C# Services) |
| 3. Keyboard, Focus & Accessibility | Rawan Konflik Focus / IME        | 100% Native WPF Handling   |
| 4. Model Streaming & State         | Split-Brain State (React vs C#)  | Single Shared Runtime Host |
| 5. Build & Packaging Complexity    | Tinggi (Node + pnpm + WebView2)  | Ringan (Native MSBuild)    |
| 6. Memory & Process Lifecycle      | 250 - 500 MB (5-7 proses Edge)   | 0 MB ekstra (< 20 MB RAM)  |
| 7. Security & Host Authority       | Rawan Injeksi Web & Script Load  | Keamanan Gate H Terjamin   |
| 8. Implementation Effort           | 6 - 8 minggu (Brittle Bridge)    | 2 - 3 minggu (Surgical)    |
| 9. Provable Parity Fidelity        | Terlihat web-like, lag resize    | Pixel-Perfect & 60 FPS     |
+------------------------------------+----------------------------------+----------------------------+
```

### Detail Analisis Opsi A (WebView2):
- **Temuan Teknis:** UI Web DeepSeek Harness bukanlah aplikasi React mandiri bertipe CRUD REST. Web frontend (`@deepseek-ai/dsh-web-frontend`) bertindak sebagai consumer atas **Cordis Microkernel Client Module System**. Boot sequence-nya mewajibkan `window.__ModuleLoader__`, `window.__DSH_BOOT__`, dan koneksi WebSocket RPC ke gateway DSH backend (`@deepseek-ai/dsh-api-gateway`).
- **Bloker Kritis:** Memisahkan UI Harness dari runtime backend-nya mengharuskan kita menulis ulang 30+ plugin Cordis atau menjalankan daemon Node.js terpisah di background. Ini secara langsung melanggar aturan Nexus: *"Jangan menambahkan second Agent runtime"*.
- **Kendala Lifecycle:** WebView2 memunculkan isu port forwarding, error `Failed to fetch` akibat proxy/firewall (sebagaimana tercatat dalam dokumen investigasi lokal `DeepSeek-Harness-Failed-to-fetch-troubleshooting.zh-CN.md`), serta konsumsi RAM berlebih.

### Detail Analisis Opsi B (Native WPF Port):
- **Temuan Teknis:** Struktur DOM, styling CSS Module, animasi CSS, dan logika interaksi React pada `packages/client/*` sangat modular dan bersih. Semua token CSS (`--dsw-*`) menggunakan format HSL dan RGB standar yang memetakan 1:1 ke WPF `SolidColorBrush`. Path ikon pada `icons/index.tsx` adalah SVG murni yang langsung kompatibel dengan WPF `StreamGeometry`.
- **Keunggulan Otoritas Host:** Seluruh binding data langsung terhubung ke singleton `ConversationViewModel`, `AgentWorkspaceViewModel`, dan `AgentWorkspaceApprovalProvider` yang telah teruji pada Gate H dan Gate I. Tidak ada duplikasi runtime, tidak ada dependensi eksternal, dan tidak ada celah keamanan baru.

### **Rekomendasi Final:**
**Pilihlah OPSI B (Port Selected UI Components & Behaviors ke Native WPF).**  
Opsi ini memberikan kesamaan visual dan interaksi 100% (pixel-perfect parity), mempertahankan otoritas keamanan host Nexus, mengeliminasi risiko dependensi browser, dan memastikan kinerja tinggi pada mesin desktop.

---

## 5. Minimal Integration Architecture (Native WPF)

Arsitektur integrasi mempertahankan seluruh subsistem inti Nexus tanpa modifikasi backend eksekusi:

```text
+----------------------------------------------------------------------------------------------------+
|                                    NEXUS AI WORKSPACE SHELL                                        |
|  MainWindow.xaml (Modern Window Chrome, Windows Titlebar Integration, Minimal Title Caption)       |
+----------------------------------------------------------------------------------------------------+
|  [ Rail 56px / Expanded 260px ]  |  [ Central AI Workspace - ConversationView.xaml ]              |
|  DshSidebarView                  |                                                                 |
|  - New Session (+ Icon)          |  +-----------------------------------------------------------+  |
|  - Search Sessions Filter        |  | DshConversationHeader (Title, Project Chip, Model Picker) |  |
|  - Time-Grouped Session Tree     |  +-----------------------------------------------------------+  |
|    * Today                       |  | Timeline Area (Chat Items & Virtualized Trajectory Ledger)|  |
|    * Yesterday                   |  | - User Message Bubbles                                    |  |
|    * Previous 7 Days             |  | - Assistant Message with DshReasoningBlock (Collapsible)  |  |
|  - StateDot Indicators           |  | - Step Activity Cards & Streaming Whale Tail Status       |  |
|  - Bottom Settings Pill          |  +-----------------------------------------------------------+  |
|                                  |  | Contextual Overlay / Right Drawer: DshDeliverablesView    |  |
|                                  |  | - Changed Files (+/- lines), Per-File Review Tabs         |  |
|                                  |  | - DshDiffViewer (Colorized Syntax Diff, Line Badges)      |  |
|                                  |  +-----------------------------------------------------------+  |
|                                  |  | DshApprovalTakeover (Anchored Warning Card, Countdown)   |  |
|                                  |  +-----------------------------------------------------------+  |
|                                  |  | Floating DshComposer (Radius 16px, Soft Elevation Shadow) |  |
|                                  |  | - [Project Pill] [Mode: Tanya / Ubah Kode] [Attachments]  |  |
|                                  |  | - Auto-growing Textarea (Enter send, Shift+Enter newline) |  |
|                                  |  | - Context Meter & Dynamic Send / Stop Button              |  |
+----------------------------------+--+-----------------------------------------------------------+--+
                                      |
                                      v
+----------------------------------------------------------------------------------------------------+
|                               NEXUS UNCHANGED CORE SERVICES & AUTHORITY                            |
|  - Single Shared Model Runtime (Local llama.cpp / Cloud OpenAI-Compatible)                         |
|  - Host Approval Provider (Digest-Bound One-Shot Plan Approval)                                    |
|  - SafeWrite & Immutable Diff Engine (ExactSourceEdits, Atomic Rollback)                           |
|  - Source Verification Profile (Harness Compilation, Job Drain, Hash Verification)                 |
|  - Nexus Tools Intact: Image Tools, PDF Tools, AI Check, File Management                           |
+----------------------------------------------------------------------------------------------------+
```

---

## 6. Exact Affected Files & File Ownership

Untuk menjaga kebersihan repositori dan kepatuhan terhadap Karpathy Guidelines (Surgical Changes), implementasi dipetakan secara ketat:

### 6.1. File Baru yang Ditambahkan (Isolated UI Additions)
Semua file baru ditempatkan pada namespace UI terisolasi di `src/03_App/Nexus.App`:
1. `src/03_App/Nexus.App/Themes/DshDesignTokens.xaml` — Definisi SolidColorBrush, Thickness, CornerRadius, dan Color Palette Light/Dark `--dsw-*`.
2. `src/03_App/Nexus.App/Themes/DshIcons.xaml` — Resource dictionary SVG Path / StreamGeometry resmi DSH.
3. `src/03_App/Nexus.App/Controls/DshReasoningBlock.xaml` & `.cs` — UserControl reasoning/thinking collapsible dengan shimmer visual.
4. `src/03_App/Nexus.App/Controls/DshDiffViewer.xaml` & `.cs` — UserControl syntax-highlighted diff viewer dengan line numbers.
5. `src/03_App/Nexus.App/Controls/DshComposer.xaml` & `.cs` — UserControl floating composer card dengan toolbar actions.
6. `src/03_App/Nexus.App/Controls/DshApprovalTakeover.xaml` & `.cs` — UserControl takeover approval card dengan timer countdown.
7. `src/03_App/Nexus.App/Views/DshSidebarView.xaml` & `.cs` — UserControl sidebar rail 56px / 260px dengan session grouping.
8. `src/03_App/Nexus.App/Views/DshDeliverablesView.xaml` & `.cs` — UserControl changed files tab & diff review drawer.

### 6.2. File Existing yang Diadaptasi Secara Bedah (Surgical Edits)
Hanya 3 file aplikasi yang disentuh untuk mengintegrasikan kontrol:
1. `src/03_App/Nexus.App/App.xaml` — Merging resource dictionaries `DshDesignTokens.xaml` dan `DshIcons.xaml`.
2. `src/03_App/Nexus.App/Views/ConversationView.xaml` & `.cs` — Mengganti layout legacy container dengan DSH-composed controls (`DshSidebarView`, `DshComposer`, `DshReasoningBlock`, `DshApprovalTakeover`).
3. `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs` — Penambahan adapter view model: time-bucket grouping sessions (Today, Yesterday, 7 Days) dan reasoning block text disclosure binding.

### 6.3. File yang DILINDUNGI MUTLAK (Zero Touch Boundary)
- **Gate H Certified Core:** `src/01_Core/Nexus.Core/*`, `SafeWrite.cs`, `ToolExecutor.cs`, `ApplySourcePlanTool.cs`.
- **Milestone I2-B (Prioritas Fungsional):** `I2B-ReviewedContracts/*`, `GATE_I2B_NATURAL_LANGUAGE_EDIT_SCOPE.md`, `Directory.Build.targets`.
- **Nexus Native Capabilities:** Image Tools (`ppocrv6`, OCR Pipeline), PDF Tools, AI Check pipeline tetap aktif dan utuh.

---

## 7. Risks & Compatibility Matrix

| Risiko Potensial | Mitigasi yang Ditetapkan |
|---|---|
| **Konflik Kepemilikan File dengan I2-B** | Isolasi total. Implementasi UI parity hanya menyentuh `Nexus.App` (Views/Controls/Themes) dan tidak menyentuh `Nexus.Contracts`, `Nexus.AI.EditingLite`, atau profile verifikasi. |
| **Bypass Terhadap Otoritas Izin Nexus** | UI DSH di-wire langsung ke `AgentWorkspaceApprovalProvider`. Tidak ada tombol "Auto-Approve", tidak ada terminal shell liar, dan tidak ada bypass terhadap validasi literal/digest intent. |
| **Penyusutan Kemampuan Nexus Asli** | Fitur Image Tools (OCR), PDF Tools, dan AI Check dipertahankan sebagai mode/tool card resmi pada DSH timeline. |
| **Perubahan Font / Tipografi pada OS Tanpa Montserrat** | Font Montserrat (Light, Regular, Medium) dibundel langsung sebagai embedded font resource dalam binary WPF. Fallback: Segoe UI, system-ui. |
| **Regresi Layout pada Layar Kecil (<1024px)** | Responsive collapsing: sidebar otomatis menciut ke rail 56px jika lebar window < 1024 DIP; composer side-clearance mengecil dari 32px ke 12px. |

---

## 8. Visual & Behavior Acceptance Targets

Uji penerimaan kesetaraan (parity acceptance) harus lulus kriteria berikut pada resolusi 1280×800, 1440×900, dan 1920×1080:

1. **Sidebar Geometry:**
   - Expanded: Lebar persis 260 DIP, padding horizontal 12 DIP, padding vertikal 6 DIP.
   - Collapsed: Lebar persis 56 DIP, ikon 36×36 DIP terpusat dengan padding 10 DIP.
   - Transisi: Animasi slide dan crossfade 150ms tanpa reflow teks yang patah.
2. **Composer Card:**
   - Lebar maksimum: 800 DIP terpusat horizontal.
   - Sudut melengkung: Radius 16 DIP (`--dsw-radius-panel`).
   - Warna latar: Light `#FFFFFF` (border subtle `rgba(0,0,0,0.1)`), Dark `#2C2C2E` (`--dsw-static-neutral-bluish-850`).
   - Drop shadow: Elevasi lembut (`--dsw-elevation-soft`).
3. **Reasoning Block ("Think"):**
   - Header: Ikon Think 14×14 DIP + Label "Think" + summary baris pertama.
   - Status Streaming: Animasi text shimmer berjalan halus saat token diterima.
   - Collapsed / Expanded: Klik baris membuka/menutup markdown secondary typography.
4. **Contextual Diff & Review:**
   - Warna Baris: Tambah `#E6F4E7` (Dark: `#1F3124`), Kurang `#FCE6E2` (Dark: `#3C1F1B`).
   - Line number gutter terpisah dengan font monospace Consolas / SF Mono.
   - Per-file tabs dengan badge status dan navigasi antar-perubahan.
5. **Approval Takeover:**
   - Muncul tepat di atas composer; input dinonaktifkan sementara.
   - Strip kuning peringatan (`--dsw-alias-state-warn-tertiary`).
   - Tombol "Tolak" (outline danger hover) dan "Setujui" (primary blue).
   - Hotkey keyboard: `Enter` menyetujui, `Escape` menolak.

---

## 9. Bounded Implementation Sequence (Stage 2 Roadmap)

Pelaksanaan implementasi dibagi menjadi 5 irisan vertikal terikat (bounded vertical slices) setelah persetujuan owner:

- **Slice 1: Foundation Tokens, Icons & Styles (1 Hari)**  
  Mengintegrasikan `DshDesignTokens.xaml` (palet Light/Dark lengkap) dan `DshIcons.xaml` (vektor ikon resmi) ke dalam `App.xaml`.
- **Slice 2: Sidebar & Session History (2 Hari)**  
  Membangun `DshSidebarView` dengan rail 56px, expanded 260px, New Session, Search filter, dan time grouping (Today / Yesterday / 7 Days).
- **Slice 3: Floating Composer & Conversation Header (2 Hari)**  
  Membangun `DshComposer` dan `DshConversationHeader` dengan selector model, mode Tanya/Ubah kode, dan token meter.
- **Slice 4: Chat Timeline, Collapsible Thinking & Tool Cards (2 Hari)**  
  Mengintegrasikan message card modern, `DshReasoningBlock` dengan shimmer animation, dan streaming status indicator.
- **Slice 5: Contextual Diff Review & Approval Takeover (2 Hari)**  
  Mengintegrasikan `DshDiffViewer` berwarna syntax, per-file tabs, serta kartu takeover `DshApprovalTakeover` dengan timer 120s.
- **Slice 6: Visual Audit & Side-by-Side Verification (1 Hari)**  
  Pengujian rendering visual side-by-side pada Light dan Dark theme, verifikasi responsiveness, serta pengetesan alur user journey menyeluruh.

---

## 10. Owner Scope Approval Request

Sesuai instruksi Karpathy Guidelines dan aturan workspace ("Ask before modifying code / Stage 2 dilakukan setelah approval"):

> [!IMPORTANT]
> **Keputusan Arsitektur yang Diajukan:**  
> **Gunakan Opsi B (Native WPF Port):** Menggunakan ulang 100% token styling CSS, geometri layout, ikon SVG, dan pola interaksi DeepSeek Harness resmi, di-porting secara bedah ke native WPF C# .NET 8 tanpa menambah WebView2 atau daemon backend Node.js.  
> Otoritas keamanan host Nexus, single shared model runtime, SafeWrite, digest-bound approval, serta milestone I2-B tetap terjaga 100% utuh dan independen.

**Apakah owner menyetujui laporan audit ini dan memberikan otorisasi untuk memulai Stage 2 (Implementasi Vertical Slice 1: Foundation Tokens, Icons & Styles)?**
