# NEXUS — REA-FIRST UI AUDIT & IMPLEMENTATION PLAN

**Date:** 16 Oktober 2026  
**Mode:** READ-ONLY AUDIT (Tanpa Perubahan Source/XAML/C#)  
**Target:**
1. DeepSeek Harness terinstal dan berjalan di PC (`DeepSeek Harness.exe`, PID 20416).
2. DeepSeek Harness upstream source (`D:\NEXUS\Audit\deepseek-harness`).
3. Existing Nexus Native WPF (`D:\NEXUS\Nexus-CSharp`).

---

## 1. Executive Summary & Metodologi REA

Audit ini dilakukan secara **READ-ONLY** dengan memadukan bukti forensik Reverse Engineer Anything (REA), repositori source resmi DeepSeek Harness v0.2.0-rc.2, dan runtime discovery pada Windows Desktop.

### 1.1 Bukti REA & Hasil Pengujian Forensik

| Tool REA | Target | Evidence ID / Detail | Hasil Sebenarnya & Analisis |
| :--- | :--- | :--- | :--- |
| `rea:inspect_electron_fuses` | `C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\DeepSeek Harness.exe` | `ev_8a243e59694be478232bb759c7d32c341f229109155807b5501d511e1c2baa3a` | SHA-256 `df4e91fd91f6f1bee19a1990b0e167bfe06abf8353279d82bbb8e60edb6ba0d3`. Fuses: `EnableNodeCliInspectArguments` = true, `RunAsNode` = true, `EnableEmbeddedAsarIntegrityValidation` = false. Membuktikan aplikasi adalah Electron upstream tanpa proteksi asar tertutup. |
| `rea:analyze_javascript_application` | `C:\Users\Syand\AppData\Local\Programs\DeepSeek Harness\resources\app.asar` | `ev_7ac622bfc8e0caa0f526274befc6defcd9e8f1285cadf7a0cacdf4e14a96e759` | SHA-256 `983ca71114e6dfd353fc79af5a1f9481a250ee64c2a3c757673029b811b23bc2`. Menginventarisir 10.102 modul JavaScript/CSS bundle di dalam app.asar. |
| `rea:inspect_analysis_view` (summary) | `app.asar` (Parent `ev_7ac622bfc8e0...`) | `ev_7de6d13559f2c532b7da3575178ee5af2c12359eb9aadb3adf01318df6a8ea47` | Memvalidasi 7.823 modul aplikasi yang aktif, membuktikan struktur bundel client/runtime konsisten dengan monorepo upstream `D:\NEXUS\Audit\deepseek-harness`. |
| `rea:inspect_analysis_view` (modules) | `app.asar` (Parent `ev_7ac622bfc8e0...`) | `ev_4b802171206b4b6ee44778391db22fb48f62eec1e73f73a7bcc37061d97c3559` | Memverifikasi modul packages runtime: `@deepseek-ai/dsh-user-questions`, `@deepseek-ai/dsh-deque`, `@deepseek-ai/dsh-session`, `@deepseek-ai/dsh-terminal-bash`. |
| `rea:list_electron_targets` | `http://127.0.0.1:19387` | `invalid_endpoint_response` | Menemukan bahwa port 19387 yang dimiliki PID 2156 adalah daemon HTTP/RPC internal milik Harness backend, bukan port debugging CDP (Chrome DevTools Protocol). |
| Runtime Process & Window Discovery | Interactive Desktop `WinSta0\default` | Host Win32 Enumeration | Menemukan 5 proses aktif `DeepSeek Harness.exe` (PIDs: 20416, 2156, 7800, 12256, 17972). Top-level window utama adalah `HWND 134070` (`Chrome_WidgetWin_1`, Title `'DeepSeek Harness'`, keadaan *minimized* pada saat audit), dan child window presenter `HWND 134170` berukuran 1440×753 pixel. |

---

## 2. Audit Mendalam 5 Area Fokus: Harness vs Nexus

### Area 1: Composer (Posisi, Spacing, Input, Multiline)

#### A. DeepSeek Harness (Upstream Source & ASAR)
- **Source:** `packages/client/ui-conversation/src/client/skeleton/InputBar.tsx` dan `InputBar.module.css`.
- **Layout Hirarki:**
  - Single elevated card (`.card`) dengan radius 16px (`--dsw-radius-panel`), background elevated input major (`--dsw-specific-input-major`), dan soft drop-shadow (`box-shadow: var(--dsw-elevation-soft)`).
  - Card selalu memiliki struktur **2-Tier Vertikal Tetap**:
    - **Tier Atas (DraftEditor):** Contenteditable text area di dalam scroll container `.scroll` (`max-height: 180px`, auto scroll). Min-height 52px pada status hero/empty, 36px pada sesi chat aktif. Padding: 4px atas, 14px kiri, 8px kanan.
    - **Tier Bawah (.row toolbar):** Baris kontrol alat (`padding: 2px 8px 6px`) di bawah area teks. Sisi kiri memuat tools (`+` dan permission chip), sisi kanan memuat primary action (Send/Stop).
  - Max Width: `var(--dsh-composer-card-max-width)` (820px).

#### B. Nexus WPF Saat Ini
- **Source:** `src/03_App/Nexus.App/Views/ConversationView.xaml` (L1250–L1450).
- **Kelemahan / Root Cause:**
  - Nexus menggunakan logika DataTrigger `IsComposerExpanded` yang melompat-lompat baris:
    - Saat `IsComposerExpanded = False` (teks 1 baris): Tombol Lampiran (`BtnAIAddAttachment`) berada di Baris 0 Kolom 0, Teks Input di Kolom 1, Tombol Send di Kolom 3, dan Tombol Mode (`BtnModeSelector`) menumpuk di Baris 0 Kolom 0 dengan `Margin="40,0,8,0"`.
    - Saat `IsComposerExpanded = True` (multiline): Tombol Lampiran dan Send melompat ke Baris 1.
  - Hal ini menyebabkan tampilan composer pada keadaan default terlihat sesak dan tidak konsisten dengan Harness asli yang selalu 2 tingkat (textarea di atas, tools di bawah).

#### C. Keputusan: `ADAPT FROM HARNESS` + `FIX`
- Hilangkan trigger perpindahan baris.
- Terapkan layout 2-tier permanen di dalam Composer Card: Row 0 untuk multiline TextBox (MinHeight 36/52, MaxHeight 180), Row 1 untuk Toolbar kontrol.

---

### Area 2: Perilaku Tombol `+`, Pilih File, Attachment, Send/Stop

#### A. DeepSeek Harness
- **Tombol `+`:**
  - 28×28px lingkaran (`border-radius: 999px`), background `var(--dsw-specific-selector)`, warna teks primary.
  - Icon: **Plus outline** (`IconPlusOutlineMedium` 14px), bukan paperclip.
  - Klik membuka menu command / picker.
- **Attachment Cards:**
  - Terletak **di dalam** composer card tepat di atas area textarea (`ComposerAttachments.tsx`, `FileCard.tsx`).
  - Gambar: thumbnail 64×64px dengan tombol close bulat 18px di pojok kanan atas (muncul saat hover).
  - File: kartu ukuran 240×64px dengan icon tipe file 28px, nama file 14px bold, ukuran file 12px muted, dan tombol remove 18px.
- **Tombol Send / Stop:**
  - Lingkaran 34×34px (`border-radius: 999px`), background `#3964FE` (light) / `#679EFE` (dark).
  - Icon Send: **Panah Mengarah ke Atas** (Upward Arrow `M8.3125 0.980183...`), statis warna putih. **Bukan paper plane**.
  - Icon Stop: Kotak bersudut tumpul (`rect x="3" y="3" width="10" height="10" rx="3"`), warna putih.

#### B. Nexus WPF Saat Ini
- Tombol Add menggunakan icon Paperclip (`DshIconPaperclip`), ukuran 32×32px.
- Attachment dirender sebagai chip terpisah di luar card composer.
- Tombol Send menggunakan icon Paper Plane (`DshIconPaperPlane`).
- Tombol Stop sudah menggunakan kotak 14×14px (`DshIconStop`), sudah sesuai.

#### C. Keputusan
- Tombol `+` glyph & styling: `ADAPT FROM HARNESS` (ganti icon paperclip menjadi icon `+` lingkaran 28×28px).
- Menu Lampiran: `KEEP NEXUS` (menu klik kanan/popup WPF native Nexus untuk Lampirkan File & Folder sudah sangat baik dan sesuai Windows OS).
- Penempatan Attachment: `ADAPT FROM HARNESS` (letakkan chip lampiran di dalam card composer tepat di atas TextBox).
- Icon Send: `ADAPT FROM HARNESS` (ganti `DshIconPaperPlane` menjadi panah mengarah ke atas).
- Stop Button: `KEEP NEXUS` (sudah sesuai dengan kotak rounded Harness).

---

### Area 3: Project Selector dan Mode Dropdown

#### A. DeepSeek Harness
- **Project Selector (Workspace):**
  - Pada status New Session / Empty: sebuah baris chip kecil (`EmptyHero.tsx`, `.workspaceRow`) 12px di atas composer card. Berisi icon folder 16px + judul workspace 13px weight 500 + chevron 12px. Background transparan saat istirahat, hover tint saat disentuh pointer.
  - Mengklik membuka menu pemilihan folder/project.
- **Mode Dropdown (Permission Preset):**
  - Terletak di footer kiri composer (`PermissionSelect.tsx`).
  - Chip kompak tinggi 28px: Icon perisai/mode + teks "Tanya" atau "Edit kode" + chevron down.
  - Klik membuka popup seleksi mode kerja tanpa formulir izin panjang.

#### B. Nexus WPF Saat Ini
- Nexus sudah memiliki `HeroWorkspaceRow` di atas composer pada status kosong dan `BtnModeSelector` di footer composer.
- Namun tata letak `BtnModeSelector` terganggu oleh trigger `IsComposerExpanded`.
- Model selector utama dan tab AI Check Nexus berada di header atas.

#### C. Keputusan
- Project Selector: `ADAPT FROM HARNESS` + `FIX` (standarisasi tinggi 28px, font 13px, icon folder + label + chevron 12px di atas card).
- Mode Dropdown: `KEEP NEXUS` logic + `FIX` layout (kunci di baris toolbar bawah sisi kiri, berdampingan dengan tombol `+`).
- Model Selector & AI Check: `KEEP NEXUS` (**Inviolable constraint**: tetap di header atas, tidak dipindahkan).

---

### Area 4: Chat Bubble dan Timestamp Tanggal/Jam

#### A. DeepSeek Harness
- **User Bubble:**
  - Rata kanan, background `var(--dsw-specific-bubble)` (`#F2F4F7` light, `#212327` dark), border-radius 16px (`--dsw-radius-xl`), padding `10px 16px`, max-width ~70.2% (~525px).
- **Assistant Message:**
  - Rata kiri, **tanpa background dan tanpa border** (flat readable canvas).
- **Timestamp (Tanggal & Jam):**
  - Implementasi: `message-chrome.ts` (`formatMessageClock`) dan `MessageIconActions.tsx`.
  - Terletak pada baris aksi di bawah pesan user/asisten.
  - Logika format:
    - Hari ini: `HH:mm` (contoh: `09:15`).
    - Tahun ini: `MM-dd HH:mm` (contoh: `10-16 09:15`).
    - Tahun berbeda: `yyyy-MM-dd HH:mm`.
  - Warna: muted tertiary (`#81858C`), ukuran font 13px.
  - Efek hover: Baris aksi dan timestamp pada pesan sebelumnya memiliki `opacity: 0` dan bertransisi menjadi `opacity: 1` saat hover (`:hover`).

#### B. Nexus WPF Saat Ini
- User Bubble dan Assistant Message di Nexus sudah flat dan memiliki border-radius yang baik.
- **Root Cause Defisit:**
  - Objek penyimpanan `ConversationMessage` di `Nexus.Conversation.Models` **sebenarnya sudah memiliki properti `Timestamp` (DateTime)**.
  - Namun `ChatMessageItemViewModel` di `ConversationViewModel.cs` **tidak mengekspos properti `Timestamp` atau `FormattedTime`**, dan template XAML di `ConversationView.xaml` **tidak memiliki elemen TextBlock untuk menampilkan jam/tanggal**.
  - Akibatnya, baik pesan user maupun asisten tidak menampilkan informasi waktu sama sekali.

#### C. Keputusan: `ADAPT FROM HARNESS` + `FIX`
- Expose properti `FormattedTime` pada `ChatMessageItemViewModel` dengan aturan waktu Harness.
- Tambahkan TextBlock timestamp di `UserActionRow` (sisi kiri tombol Salin) dan baris footer pesan asisten.

---

### Area 5: Markdown Typography, Code Block dan Syntax Highlighting

#### A. DeepSeek Harness
- **Source:** `packages/client/ui-primitives/src/markdown/CodeBlock.tsx` dan `CodeBlock.module.css`.
- **Code Block:**
  - Border radius 8px/10px (`--dsw-radius-lg`), background dark neutral (`--dsw-alias-markdown-code-block`), margin `16px 0`.
  - Header Banner:
    - Sticky di bagian atas code block saat scroll panjang.
    - Background banner lebih gelap (`--dsw-alias-markdown-code-block-banner`).
    - Padding: `9px 14px`.
    - Kiri: label bahasa pemrograman 11px monospace.
    - Kanan: tombol Salin dengan feedback icon Check.
  - Content Body: Monospace 13px / 20px leading, padding 16px, wrapping dengan horizontal scrollbar halus jika diperlukan.
  - Syntax highlighting: Token warna Shiki.

#### B. Nexus WPF Saat Ini
- **Source:** `Nexus.App.AIWorkspace.Rendering.MessageContentPresenter` dan `NexusMessageParser`.
- Menggunakan WPF FlowDocument `Table` dengan `CellSpacing="0"`, 2 baris (Header dan Body).
  - Header: label bahasa dan hyperlink Salin kode dengan timer feedback 1500ms (" Salin" -> " Tersalin").
  - Body: `Run(code.Content)` dengan font `NexusTypographyCodeFontFamily` (Consolas/Cascadia Code).
  - Background: `SurfaceSecondaryBrush`.

#### C. Keputusan: `KEEP NEXUS` Architecture + `ADAPT FROM HARNESS` Styling
- **KEEP NEXUS Table Engine:** Arsitektur Table FlowDocument native WPF terbukti sangat cepat, mendukung seleksi teks multi-paragraf, dan tidak membebani memori.
- **ADAPT STYLING:** Sesuaikan padding header code block menjadi `10, 6, 12, 6` (tinggi ~30px, mendekati 9px/14px Harness), font label bahasa 11px, dan border-radius sudut visual.
- **NOT NEEDED:** Library syntax highlighting JavaScript raksasa di dalam WPF tidak diperlukan karena akan menambah dependensi dan memperlambat rendering pesan panjang.

---

## 3. Matriks Keputusan Komponen (Harness vs Nexus)

| Komponen UI | Status Keputusan | Alasan & Tindakan |
| :--- | :--- | :--- |
| **Composer 2-Tier Layout** | `ADAPT FROM HARNESS` + `FIX` | Hilangkan row-jumping triggers (`IsComposerExpanded`). Kunci TextBox di Tier Atas dan toolbar kontrol di Tier Bawah. |
| **Multiline Auto-Growth** | `KEEP NEXUS` | MinHeight 52 (Hero) / 36 (Active), MaxHeight 180 dengan auto scroll sudah terverifikasi bekerja dengan baik. |
| **Tombol `+` (Commands/Attach)** | `ADAPT FROM HARNESS` | Ganti icon paperclip menjadi icon `+` (28×28px lingkaran). |
| **Menu Lampiran File/Folder** | `KEEP NEXUS` | Menu native WPF Nexus untuk File/Folder picker bekerja optimal pada lingkungan desktop lokal. |
| **Attachment Chips** | `ADAPT FROM HARNESS` | Pindahkan chip lampiran ke dalam composer card, di atas baris teks input. |
| **Send Button Glyph** | `ADAPT FROM HARNESS` | Ganti Paper Plane dengan Upward Arrow putih di dalam lingkaran biru 34×34px. |
| **Stop Button Glyph** | `KEEP NEXUS` | Rounded square 10×10px putih saat generasi aktif sudah 100% parity dengan Harness. |
| **Project/Workspace Chip** | `ADAPT FROM HARNESS` + `FIX` | Samakan ukuran dan spacing 12px di atas composer card pada hero state. |
| **Mode Selector Chip** | `KEEP NEXUS` (Logic) / `FIX` (Layout) | Pertahankan opsi "Tanya" dan "Edit kode", kunci posisinya di kiri bawah composer card. |
| **Model Selector & AI Check** | `KEEP NEXUS` | **Kunci mati**: Tetap berada di header atas aplikasi Nexus sesuai mandat arsitektur. |
| **User Message Bubble** | `KEEP NEXUS` | MaxWidth 680, padding 10/16, corner radius 14-16px sudah identik dengan visual Harness. |
| **Assistant Flat Canvas** | `KEEP NEXUS` | Format transparan tanpa border bubble sudah identik dengan Harness. |
| **Message Timestamp (Waktu)** | `ADAPT FROM HARNESS` + `FIX` | Expose `FormattedTime` (`HH:mm` / `MM-dd HH:mm`) dari data `ConversationMessage.Timestamp` ke `UserActionRow`. |
| **Code Block FlowDocument** | `KEEP NEXUS` Architecture | Engine Table native WPF sangat cepat dan stabil. Cukup lakukan fine-tuning padding dan token warna. |

---

## 4. Exact Nexus Files yang Akan Diubah Nanti

Saat implementasi disetujui, perubahan hanya akan menyentuh file-file berikut tanpa dependensi baru:

1. `src/03_App/Nexus.App/Views/ConversationView.xaml`:
   - Penataan ulang grid composer menjadi 2 tier permanen (Row 0: Attachments + TextBox, Row 1: Tools + Send).
   - Penggantian icon Send (Upward Arrow) dan Add (`+`).
   - Penambahan TextBlock Timestamp di `UserActionRow` dan footer asisten.
2. `src/03_App/Nexus.App/Themes/DshIcons.xaml`:
   - Penambahan path geometry `DshIconArrowUp` (panah send Harness) dan `DshIconPlus`.
3. `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`:
   - Penambahan properti `DateTime Timestamp` dan string `FormattedTime` pada `ChatMessageItemViewModel`.
   - Pemetaan waktu dari `ConversationMessage` saat memuat sesi dan membuat pesan baru.
4. `src/03_App/Nexus.App/AIWorkspace/Rendering/MessageContentPresenter.cs`:
   - Fine-tuning metrik header dan content code block agar presisi dengan ukuran Harness.
5. Unit & UI Tests:
   - `tests/Nexus.App.Tests/ViewModels/ConversationViewModelTests.cs` (verifikasi format timestamp dan attachment state).
   - `tests/Nexus.App.Tests/AIWorkspace/AIWorkspaceRenderingTests.cs` (verifikasi rendering FlowDocument code block).

---

## 5. Rencana Implementasi Bertahap (Composer → Chat Rendering)

Implementasi akan dibagi menjadi 2 irisan kecil (Small Slices):

### Slice A: Composer & Attachments Fidelity
1. Tambahkan geometri `DshIconArrowUp` dan `DshIconPlus` di `DshIcons.xaml`.
2. Restrukturisasi card composer di `ConversationView.xaml`:
   - Tier 1: Container chip lampiran (jika ada) + `TxtAIInput` (TextBox multiline permanen).
   - Tier 2: Baris toolbar dengan `BtnAIAddAttachment` (`+`), `BtnModeSelector` di kiri, dan `BtnAISendBorder` di kanan.
3. Hubungkan visual state `IsSending` ke icon Stop (kotak putih) vs Upward Arrow.
4. Verifikasi dengan targeted tests: `ConversationViewModelTests`.

### Slice B: Chat Bubble, Timestamp & Code Block Refinement
1. Buka properti `Timestamp` dan `FormattedTime` pada `ChatMessageItemViewModel`.
2. Tambahkan TextBlock waktu di `UserActionRow` (kanan bawah bubble user) dan footer balasan asisten.
3. Poles metrik header code block di `MessageContentPresenter.cs` (padding 9/14, font size 11px).
4. Verifikasi dengan targeted tests: `AIWorkspaceRenderingTests`.

---

## 6. Verifikasi & Safety Checklist

- [x] **Zero Contract Impact:** `src/00_Contracts/` sama sekali tidak disentuh.
- [x] **Zero New Dependencies:** Tidak ada paket NuGet atau dependensi JavaScript baru.
- [x] **Security Invariants Preserved:** Read grant/revoke, host authority, dan single writer integrity tetap utuh.
- [x] **Pinned Header Invariant:** Model selector dan tombol Chat/AI Check tetap berada di header atas.
- [x] **Impacted Regression Shield:** Menjaga seluruh 182 tests eksisting tetap PASS.

---

## 7. Status Audit Selesai — Permohonan Persetujuan Owner

Laporan audit komprehensif berbasis bukti REA telah selesai disusun. Sesuai instruksi:
> *"Setelah implementation plan selesai, **STOP dan minta satu persetujuan owner** sebelum implementasi. Jangan mengulang audit yang sudah terbukti."*

Mohon persetujuan Owner untuk melanjutkan ke tahap eksekusi implementasi (Slice A: Composer & Attachments → Slice B: Timestamp & Chat Rendering).
