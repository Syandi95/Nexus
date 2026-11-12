# Final Implementation Plan v1.0 — AI Workspace Typography System & Markdown Renderer v1

## Executive Summary

**Tujuan:** Menyelesaikan Typography System AI Workspace dan Markdown Renderer v1 pada Nexus-CSharp.

**Scope:**
- Memusatkan seluruh pengaturan tipografi AI Workspace ke satu source of truth bersama Nexus.
- Memperluas parser dan renderer Markdown yang sudah ada, yaitu `NexusMessageParser` dan `MessageContentPresenter`.
- Menambahkan dukungan Heading, Horizontal Rule, dan Task List.
- Memperbarui warna teks Light Mode seluruh Nexus agar lebih hitam, kontras, dan nyaman dibaca.
- Tidak menyentuh modul di luar scope.

**Root cause:**
- Sistem tipografi terpecah antara `NexusFont*` dan `NexusTypography*` dengan nilai yang saling bertentangan.
- `ConversationView.xaml` dan `MessageContentPresenter.cs` masih memiliki ukuran font, font family, serta warna literal.
- Parser belum mengenali Heading, Horizontal Rule, dan Task List sehingga sintaks Markdown mentah masih tampil.
- Table dan Code Block sudah tersedia dan tidak boleh mengalami regresi.

**Source of truth final:**
- Sistem typography Nexus hanya memiliki satu canonical source of truth.
- Lokasi fisik dipilih Codex berdasarkan pendekatan paling aman dan paling sederhana.
- Warna menggunakan semantic color token Nexus.
- Theme switching tetap menggunakan `ThemeManager`.
- Tidak boleh membuat `ThemeManager`, `TypographyManager`, parser, atau renderer kedua.

---

## Final Architecture

```text
Nexus App
│
├─ ThemeManager
│  └─ Mengganti semantic dan compatibility theme dictionaries
│
├─ Typography Nexus
│  └─ Satu canonical source of truth
│     ├─ ukuran font
│     ├─ font family
│     └─ typography role
│
├─ ConversationView.xaml
│  └─ Mengonsumsi typography dan semantic color token bersama
│
├─ NexusMessageParser
│  └─ MessageDocument
│     ├─ Paragraph
│     ├─ Heading
│     ├─ HorizontalRule
│     ├─ List
│     ├─ Task List
│     ├─ Quote
│     ├─ Code
│     └─ Table
│
└─ MessageContentPresenter
   └─ Satu FlowDocument dalam satu RichTextBox read-only selectable
```

**Data flow:**

```text
Source teks
↓
NexusMessageParser
↓
MessageDocument
↓
MessageContentPresenter
↓
Satu RichTextBox selectable
```

---

## Phase 1 — Typography System

### Tujuan

Menyatukan typography Nexus tanpa memutus kompatibilitas resource dan tanpa membuat sistem baru.

### Kontrak

- Sistem typography Nexus harus mempunyai satu canonical source of truth.
- Codex bebas memilih lokasi fisik paling aman.
- Tidak wajib memindahkan seluruh key jika compatibility alias atau refactoring kecil lebih aman.
- Tidak boleh ada dua definisi typography yang saling bertentangan.
- `ResourceDictionary` dan urutan merge tidak boleh rusak.
- Key lama yang masih digunakan harus tetap dapat di-resolve.
- Jangan membuat `TypographyManager` atau `ThemeManager` baru.
- Jangan membuat token typography yang hanya dipakai AI Workspace.

### Rekonsiliasi typography

- Satukan `NexusFont*` dan `NexusTypography*` ke satu definisi nilai.
- Body, caption, title, heading, code, dan icon family harus berasal dari source of truth yang sama.
- `ConversationView.xaml` dan `MessageContentPresenter.cs` harus memakai token bersama.
- Jangan mempertahankan literal hanya untuk melewati governance.

### Light Mode

Keputusan final:

- Warna teks Light Mode seluruh Nexus boleh dibuat lebih hitam.
- Target visual adalah kontras dan kenyamanan baca setara ChatGPT Light Mode.
- Perubahan dilakukan melalui semantic color token dan compatibility brush Nexus.
- Jangan hardcode warna di renderer atau View.
- Dark Mode tidak boleh berubah atau mengalami regresi.

### ConversationView.xaml

- Pertahankan key `AIWorkspace*Style` yang sudah ada.
- Ganti `FontSize`, `FontFamily`, `FontWeight`, dan `Foreground` literal dengan resource bersama.
- Ganti `Foreground="White"` pada tombol send/stop dengan semantic token yang sesuai.
- Jangan membuat style system kedua.

### MessageContentPresenter.cs

- Hapus ukuran font literal.
- Hapus `FontFamily` yang dibuat langsung.
- Hapus `Brushes.Black` atau `Brushes.White` untuk kebutuhan semantic.
- `Brushes.Transparent` boleh dipertahankan untuk background transparan yang tidak mempunyai arti semantic.
- Gunakan resource reference yang mengikuti perubahan theme.
- Jangan memecah renderer menjadi beberapa selectable control.

### Definition of Done Phase 1

- Typography Nexus memiliki satu canonical source of truth.
- Tidak ada definisi ukuran atau family yang saling bertentangan.
- Tidak ada hardcode typography atau semantic color baru.
- Light Mode lebih hitam dan nyaman dibaca.
- Dark Mode tetap benar.
- Build berhasil.

---

## Phase 2 — Markdown Renderer v1

### Kontrak utama

- Tetap gunakan `NexusMessageParser`.
- Tetap gunakan `MessageContentPresenter`.
- Jangan membuat parser atau renderer kedua.
- Parser harus deterministik.
- Plain text tidak boleh salah dianggap Markdown.
- Table dan Code Block tidak boleh berubah atau regresi.
- Selection harus tetap satu dokumen.

### Block model

Tambahkan seperlunya:

- `HeadingBlock`
- `HorizontalRuleBlock`
- Status `IsChecked` pada item list untuk Task List

Jangan membuat struktur baru jika model list yang ada dapat diperluas secara aman.

### Heading

Dukung:

```text
# Heading 1
## Heading 2
### Heading 3
```

Kontrak:

- Karakter `#` tidak ikut dirender.
- H1, H2, dan H3 memiliki hierarchy yang jelas.
- H4 sampai H6 boleh memakai style H3.
- Typography heading berasal dari source of truth Nexus.
- Heuristic code tidak boleh menganggap heading sebagai blok kode PowerShell.

### Horizontal Rule

Dukung:

```text
---
***
___
```

Kontrak:

- Hanya dikenali pada baris standalone yang valid.
- Tidak boleh salah mengenali isi code fence.
- Tidak boleh merusak separator Markdown table.
- Dirender sebagai garis tipis mengikuti semantic stroke token.
- Tetap berada di dalam FlowDocument yang sama.

### Task List

Input:

```text
- [ ] Belum selesai
- [x] Sudah selesai
```

Output:

```text
☐ Belum selesai
☑ Sudah selesai
```

Dilarang menghasilkan:

```text
• ☐ Belum selesai
• ☑ Sudah selesai
```

Kontrak:

- Task List menggantikan bullet marker.
- Bullet List normal tetap memakai bullet.
- Ordered List tetap memakai nomor.
- Checkbox hanya bersifat visual read-only pada v1.

### Block yang wajib tetap bekerja

- Paragraph
- Bullet List
- Ordered List
- Block Quote
- Code Fence
- Inline Code
- Bold
- Italic
- Table
- Selection lintas seluruh isi satu pesan

### Definition of Done Phase 2

- Heading dirender tanpa karakter mentah.
- Horizontal Rule menjadi garis.
- Task List memakai `☐` dan `☑` tanpa bullet ganda.
- Table dan Code Block tidak regresi.
- Selection tetap satu dokumen.
- Build berhasil.

---

## Phase 3 — Verification

### Workflow wajib

```text
Implementasi
↓
Build
↓
Jalankan Nexus
↓
Verifikasi visual langsung
↓
Perbaiki jika masih salah
↓
Build ulang
↓
Jalankan ulang
↓
Verifikasi ulang
↓
Baru tambahkan regression test minimal
```

### Material verifikasi

Gunakan model apa pun yang tersedia pada AI Workspace. Renderer tidak boleh bergantung pada model tertentu.

Verifikasi satu respons yang memuat:

- `#`, `##`, `###`
- `---`, `***`, `___`
- Bullet List
- Ordered List
- Task List checked dan unchecked
- Block Quote
- Code Fence
- Inline Code
- Bold
- Italic
- Table
- Beberapa paragraf untuk menguji selection lintas blok

### Regression test

Regression test dibuat hanya setelah implementasi dan verifikasi visual berhasil.

Cukup tambahkan test minimum untuk:

- Heading
- Horizontal Rule
- Task List

Jangan membuat banyak test baru yang tidak dibutuhkan.

---

## Acceptance Criteria

Status **PASS** hanya jika seluruh poin berikut terpenuhi:

- [ ] Typography Nexus memiliki satu canonical source of truth.
- [ ] Tidak ada definisi ukuran atau font family yang duplikat dan bertentangan.
- [ ] Tidak ada `FontSize`, `FontFamily`, `Foreground`, `Background`, `BorderBrush`, atau semantic brush hardcoded baru.
- [ ] AI Workspace memakai typography dan semantic color token bersama.
- [ ] Heading dirender tanpa karakter `#`.
- [ ] Horizontal Rule dirender sebagai garis.
- [ ] Task List dirender dengan `☐` dan `☑` tanpa bullet ganda.
- [ ] Bullet List normal tetap memakai bullet.
- [ ] Ordered List tetap memakai nomor.
- [ ] Table tidak regresi.
- [ ] Code Block tidak regresi.
- [ ] Selection tetap menggunakan satu RichTextBox/FlowDocument.
- [ ] Plain text tidak salah diklasifikasikan sebagai Markdown.
- [ ] Light Mode seluruh Nexus memakai warna teks yang lebih hitam dan nyaman dibaca.
- [ ] Dark Mode tetap benar.
- [ ] Build berhasil.
- [ ] Nexus berhasil dijalankan.
- [ ] Verifikasi visual langsung berhasil.

Jangan menyatakan **COMPLETE** berdasarkan build atau test saja.

---

## Out of Scope

Jangan mengubah:

- AI Engine
- Llama Runtime
- Conversation Storage
- PDF Tools
- Image Tools
- Home
- Clean Downloads
- Rapikan File
- Settings

Tidak termasuk:

- HTML Markdown
- Image Markdown
- Mermaid
- Math
- LaTeX
- Footnote
- Emoji parser

---

## Implementation Philosophy

- Perubahan minimum.
- Pilih solusi paling sederhana yang memenuhi seluruh kontrak.
- Reuse komponen yang sudah ada.
- Jangan membuat sistem kedua.
- Jangan menambah abstraction tanpa kebutuhan nyata.
- Jangan memperluas scope.
- Jangan mempertahankan kompleksitas hanya karena disebut dalam rencana awal.
- Berhenti dan laporkan jika ditemukan konflik arsitektur kritis.

---

## Primary Files Expected To Change

Daftar ini bersifat indikatif, bukan absolut. Codex boleh menyentuh file lain jika benar-benar diperlukan untuk memenuhi kontrak, selama perubahan tetap minimum dan berada dalam scope.

### Implementasi utama

- `src/03_App/Nexus.App/Themes/Tokens/SharedMetrics.xaml`
- `src/03_App/Nexus.App/Themes/SharedStyles.xaml`
- `src/03_App/Nexus.App/Themes/Semantic/LightSemanticTheme.xaml`
- `src/03_App/Nexus.App/Themes/LightTheme.xaml`
- `src/03_App/Nexus.App/Views/ConversationView.xaml`
- `src/03_App/Nexus.App/AIWorkspace/Markdown/NexusMessageParser.cs`
- `src/03_App/Nexus.App/AIWorkspace/Rendering/MessageContentPresenter.cs`

### Governance bila diperlukan

- `src/03_App/Nexus.Integration.Tests/DesignSystemSourceAudit.cs`
- `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

### Regression test setelah verifikasi visual

- `src/03_App/Nexus.Integration.Tests/AIWorkspaceMessageParserTests.cs`
- `src/03_App/Nexus.Integration.Tests/AIWorkspaceRenderingTests.cs`

---

## Governance

- Governance hanya boleh diubah jika benar-benar diperlukan.
- Jangan menambah pengecualian untuk menyembunyikan hardcode.
- Setiap perubahan governance harus dijelaskan beserta alasan teknisnya.
- `AllowedModuleStyleKeys` tidak perlu berubah jika style key lama dipertahankan.
- `AllowedHexValues` tidak boleh ditambah hanya untuk mengakomodasi literal baru.
- Perubahan governance harus mengurangi debt, bukan memindahkannya.

---

## Notes for Codex

- Audit arsitektur sudah selesai. Jangan mengaudit ulang seluruh aplikasi.
- Gunakan dokumen ini sebagai kontrak implementasi.
- Jangan membuat plan baru kecuali ditemukan blocker kritis.
- Jangan membuat parser kedua.
- Jangan membuat renderer kedua.
- Jangan membuat `ThemeManager` atau `TypographyManager` baru.
- Jangan membuat token typography khusus AI Workspace.
- Jangan memutus ResourceDictionary dan compatibility key.
- Jangan membuat unit test sebelum implementasi dan perilaku visual benar.
- Urutan wajib: implementasi → build → jalankan Nexus → verifikasi visual → perbaiki → ulangi → baru regression test.
- Jika sesi tool tidak dapat melihat window WPF, jangan mengklaim visual PASS. Serahkan instance kepada pengguna untuk pemeriksaan langsung.
- Jika solusi yang lebih sederhana ditemukan dan tetap memenuhi kontrak, gunakan solusi yang lebih sederhana.
- Berhenti dan laporkan jika perubahan mulai menyentuh modul di luar scope.

---

**READY FOR CODEX IMPLEMENTATION**
