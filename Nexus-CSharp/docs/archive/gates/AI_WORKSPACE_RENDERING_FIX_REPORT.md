# NEXUS AI WORKSPACE — RENDERING FIX

Tanggal: 2026-10-03. Scope: rendering Chat AI Workspace.

## FINAL STATUS: PARTIAL

Fix fungsional yang diimplementasikan lulus build dan **225/225 test relevan**.
Rendering diverifikasi pada WPF window aktual dengan fixture deterministik,
termasuk resize dan command sidebar. Status keseluruhan tetap PARTIAL karena
code block native FlowDocument Table masih bersudut persegi, dukungan markdown
existing belum setara seluruh CommonMark, dan streaming LLM live tidak diuji.

## RENDERING ISSUES FOUND

- P0: composer dibatasi 760 DIP sementara reading frame chat 860 DIP;
  scrollbar memakai viewport berbeda. Greeting tidak wrap pada viewport sempit.
- P0/P2: compact composer mengurangi lebar input berdasarkan rekonstruksi chrome
  dengan konstanta 28, sehingga input/placeholder dapat terdesak toolbar.
- P1: Document dan FallbackText mengganti seluruh RichTextBox/FlowDocument;
  selection dan blok yang sudah selesai tidak memiliki identitas stabil.
- P1: fenced code baru dikenali setelah closing fence; selama streaming tampil
  sebagai paragraph, lalu berubah bentuk saat fence selesai.
- P1: setiap perubahan token memaksa scroll ke bawah meskipun pengguna membaca
  bagian lama. Callback scroll juga tidak dikoaleskan.
- P1: sidebar selalu memulai animasi dari 0/260, bukan lebar animasi aktual;
  pembalikan animasi dapat melompat.
- P2: code copy mengganti label Salin menjadi Tersalin lalu Salin kode, mengubah
  panjang konten header. Indentasi kode pertama dibuang oleh Trim().
- P2: nested bold/italic menampilkan marker literal; strike dan escape belum
  direpresentasikan dengan benar; heading H4–H6 disamakan dengan H3.
- P2: indent nested list dibuang; ordered list selalu mulai dari 1.
- P2: alignment separator tabel dibuang. Pengukuran user table menggunakan
  cell terlebar alih-alih total kolom; indent list/quote juga tidak dihitung tepat.
- P2: ChoiceBlock hanya menampilkan pertanyaan dalam UI container non-selectable;
  opsi tidak terlihat di pesan setelah composer choice selesai. Copy pesan juga
  menghilangkan choice dan menyisakan marker nested emphasis.
- P2: border tabel/code/choice light theme terlalu samar pada snapshot.
  Hyperlink memakai warna bawaan WPF alih-alih accent NEXUS.

## ISSUES PREVIOUS AUDIT MISSED

Audit sebelumnya tidak dilampirkan sebagai daftar temuan yang dapat dibandingkan.
Tidak ada klaim bahwa suatu temuan pasti terlewat. Temuan independen tambahan:
copy feedback mengubah panjang header, hilangnya opsi choice pada pesan/copy,
lebar scrollbar NEXUS berbeda dari scrollbar sistem, ordered start dan table
alignment hilang, serta animasi sidebar menggunakan endpoint tetap.

## ISSUES VERIFIED FROM GEMINI

Tidak dapat diatribusikan: laporan audit Gemini spesifik tidak tersedia dalam
permintaan. Seluruh temuan di atas berasal dari source aktual dan/atau runtime WPF.

## CURRENT VS TARGET

Current di bawah berarti keadaan source sebelum perubahan sesi ini.

| Area | Current behavior | Problem | Target native behavior | Fix |
| --- | --- | --- | --- | --- |
| Paragraph | Native Paragraph dalam RichTextBox baru setiap update | Selection/tree churn | Paragraph selectable dengan identitas stabil | Reuse document dan update inline yang berubah |
| Heading | Native Paragraph; level di-clamp ke 3 | H4–H6 kehilangan hierarki | Level 1–6 memakai typography resource | Pertahankan level, mapping token H4–H6 |
| List | Native List flat; nomor mulai 1 | Nested indent dan ordered start hilang | List bertingkat dan indent native | Simpan indent/start, bangun child List pada ListItem |
| Quote | Native Section, left border 3, padding 10 | Tidak ditemukan clipping pada fixture; width probe mengabaikan indent | Native multiline wrapping dan selection | Tetap Section; width probe menyertakan block chrome |
| Code | Native Table selectable; header copy; fence harus lengkap | Late transition, trim indent, copy label bergeser, code size bukan token code | Header stabil, monospace, native wrapping, theme-aware | Open fence, preserve indent, token code size, separator header, feedback glyph/tooltip |
| Table | Native Auto columns, header weight, border 0.5 | Alignment hilang, size probe salah, light border samar | Native table wrapping dan alignment | Alignment pada Paragraph, total column width, NexusContentBorder |
| Inline code | Native Run, code font dan surface background | Style dapat bocor saat jenis inline berubah pada update | Baseline native, style hanya untuk inline code | Ganti inline bila jenis berubah |
| Bold | Bold membungkus Run mentah | Nested marker literal | Nested Bold/Italic spans | Render isi emphasis secara rekursif |
| Italic | Italic membungkus Run mentah | Nested delimiter salah | Native Italic dan nested Bold | Closing delimiter dan recursive spans |
| Strike | Plain text dengan ~~ | Tidak dirender | Native TextDecorations.Strikethrough | StrikeInline dan Span |
| Choice | UI card question-only | Opsi/selection/copy hilang | Pertanyaan dan opsi tetap terbaca/selectable | Native Section dan opsi; plain-text copy lengkap |
| User bubble | Right aligned, max 680, selectable | Width probe approximate untuk mixed fonts/table/list | Short bubble shrink, long bubble wrap | WPF TextBlock shaping dan per-block chrome; batas 680 tetap |
| Assistant content | Transparent bubble, one document | Entire document diganti | Content-oriented selectable document | Reuse RichTextBox/FlowDocument; chrome transparan tetap |
| Streaming | Rebuild tree, forced tail scroll | Churn, selection reset, scroll melawan pengguna | Update blok yang berubah, follow tail opt-in by position | Incremental updates, coalesced layout-time scroll |
| Composer | Single host; 760 vs chat 860; FormattedText dan chrome subtraction | Boundary mismatch, narrow input clipping | Shared reading token/gutter, native column width | Shared max width, actual scroll viewport, native TextBlock measure, narrow two-row layout |

## FIXES IMPLEMENTED

Path source relatif terhadap `src/03_App/Nexus.App/`, kecuali dinyatakan lain.

### 1. Reading boundary dan empty state
- **File:** `Views/ConversationView.xaml`, `Views/ConversationView.xaml.cs`.
- **Element:** AIComposerHost, AIInputBorder, AIChatReadingFrame, AIWelcomeTitle.
- **Observed problem:** max width 760/860 berbeda; greeting tidak wrap; scrollbar
  mengurangi ruang chat saja.
- **Root cause:** dua batas lebar independen dan area layout yang berbeda.
- **Change:** kedua host memakai NexusAIWorkspaceReadingFrameMaxWidth dan responsive
  gutter existing. Composer memakai selisih ActualWidth–ViewportWidth scroll yang
  dihitung WPF. Scrollbar mempunyai reserved lane stabil. Greeting wrap; composer
  tetap satu instance dan greeting tetap dalam host yang sama.
- **Why minimal:** binding/resource dan event layout existing; tanpa layout service
  atau composer duplikat. Teks welcome tetap persis sama.
- **Verification:** kedua tepi frame/composer cocok dalam toleransi 1 DIP pada
  1440, 1200, 900, 780, 600, Light/Dark, sidebar expand/collapse dan resize.

### 2. Composer native measurement
- **File:** `Views/ConversationView.xaml`, `Views/ConversationView.xaml.cs`.
- **Element:** AIComposerLayout, TxtAIInput dan placeholder.
- **Observed problem:** lebar compact direkonstruksi dari padding/action chrome
  dengan angka 28; placeholder terpotong ketika ruang input terlalu kecil.
- **Root cause:** approximation tidak mengikuti kolom Grid aktual.
- **Change:** ukur text dengan TextBlock WPF; bandingkan dengan actual star column
  dan margin child. Empty input memakai lebar placeholder sebagai kebutuhan minimum.
- **Why minimal:** pertahankan layout dua mode dan IsComposerExpanded existing.
- **Verification:** existing composer expansion/clear test; snapshot 600/sidebar
  menunjukkan input full-width di atas action row.

### 3. Stable streaming visual tree
- **File:** `AIWorkspace/Rendering/MessageContentPresenter.cs`.
- **Element:** RichTextBox, FlowDocument, Paragraph/Run dan code Table.
- **Observed problem:** setiap callback membuat ulang semua kontrol/block.
- **Root cause:** RebuildContent selalu mengganti Content, termasuk fallback token
  yang sudah mempunyai parsed Document.
- **Change:** simpan satu document/control; abaikan fallback saat Document tersedia;
  bandingkan block content; retain blok identik, update growing plain Run dan code
  Run, replace hanya blok/inline yang perlu. Code copy menyimpan source terbaru.
- **Why minimal:** logika berada dalam presenter existing, tanpa renderer/state
  service baru dan tanpa mengubah scheduler/runtime.
- **Verification:** document/control/completed block/growing Run identity; selection
  completed paragraph bertahan; code Table dan hyperlink bertahan selama fence selesai.

### 4. Code fidelity dan copy header
- **File:** `AIWorkspace/Markdown/NexusMessageParser.cs`,
  `AIWorkspace/Rendering/MessageContentPresenter.cs`.
- **Element:** fenced code, language/copy header, code paragraph.
- **Observed problem:** open fence belum code; Trim membuang indent; copy feedback
  mengubah lebar teks header; code font size mewarisi body.
- **Root cause:** regex mensyaratkan closing fence, trim seluruh whitespace, label
  feedback berbeda panjang, font size code tidak diset.
- **Change:** recognize open fence sampai EOF; preserve horizontal whitespace;
  monospace/code-size token; native header divider; feedback memakai glyph/tooltip
  dengan label tetap; native Auto columns.
- **Why minimal:** perubahan lokal pada regex/trim dan builder native existing.
- **Verification:** existing code copy/ASCII/selection tests dan updated-code copy;
  label bahasa satu visual line pada seluruh fixture viewport.
- **Remaining:** container native Table masih persegi. Mengganti dengan UI island
  rounded akan membutuhkan desain selection lintas blok yang berbeda.

### 5. Inline, heading dan list fidelity
- **File:** `AIWorkspace/Markdown/NexusMessageParser.cs`,
  `AIWorkspace/Rendering/MessageContentPresenter.cs`.
- **Element:** inline builders, headings, native List/ListItem.
- **Observed problem:** nested markers literal, strike/escape mentah, H4–H6 sama,
  nested list flat dan ordered numbering mulai 1.
- **Root cause:** emphasis dibungkus Run mentah; metadata level/indent/start dibuang.
- **Change:** recursive native spans; minimal delimiter/escape/strike handling;
  simpan level, indent dan ordered start; token heading 1–6 dan child native List.
- **Why minimal:** memakai parser/model dan builders existing; tanpa dependency
  markdown atau parser architecture baru.
- **Verification:** nested Bold/Italic/Strike/escape test, tiga tingkat list,
  ordered start test, existing parser suite, snapshots.
- **Remaining:** mixed ordered/unordered nesting dan multiline list continuation
  masih keterbatasan parser existing; tidak mengklaim dukungan semua CommonMark.

### 6. Tables, theme dan user width
- **File:** `AIWorkspace/Markdown/NexusMessageParser.cs`,
  `AIWorkspace/Rendering/MessageContentPresenter.cs`.
- **Element:** TableCell Paragraph alignment, renderer borders, hyperlink, MeasureOverride.
- **Observed problem:** table alignment hilang, cell-max width bukan total columns,
  mixed typography memakai manual per-run FormattedText; light border terlalu samar.
- **Root cause:** metadata alignment dibuang dan probe layout menduplikasi sebagian
  perhitungan text engine/chrome, memakai subtle legacy border.
- **Change:** preserve alignment; native Paragraph.TextAlignment; native TextBlock
  shaping mengganti FormattedText; total kolom dan block padding/border dipakai
  sekali, hapus recursive global chrome collector; NexusContentBorder dan accent
  hyperlink existing dipakai melalui DynamicResource.
- **Why minimal:** native Table tetap; tidak ada table engine/global token change.
- **Verification:** alignment/theme-resource tests, user short/long wrapping,
  ASCII width regressions, Light/Dark snapshots.
- **Necessary measurement:** SizeToContent tetap dibutuhkan karena RichTextBox
  mengisi constraint width. Allowance formatter 24 DIP tetap: eksperimen menghapus
  allowance gagal pada word Halo dan ketiga ASCII regression tests. Ini bukan
  padding dekoratif; removal ditolak berdasarkan runtime evidence.

### 7. Choice dan copy message
- **File:** `AIWorkspace/Rendering/MessageContentPresenter.cs`,
  `AIWorkspace/Markdown/MarkdownPlainText.cs`.
- **Element:** choice in message dan copy plain text.
- **Observed problem:** opsi tidak dirender; UI question tidak selectable;
  copy menghilangkan choice dan menyisakan nested formatting markers.
- **Root cause:** question-only BlockUIContainer dan flatten inline.Text mentah.
- **Change:** native selectable Section memuat question/options; copy memuat opsi,
  recursive emphasis text, indent list dan start numbering.
- **Why minimal:** composer choice interaction/commands tetap memakai implementasi
  existing; hanya visual content dan copy formatting diperbaiki.
- **Verification:** options ada di selection dan plain-text copy, snapshots.

### 8. Scroll dan sidebar visual behavior
- **File:** `Views/ConversationView.xaml`, `Views/ConversationView.xaml.cs`.
- **Element:** AIChatScroll dan AIConversationPanel animation.
- **Observed problem:** setiap Content token scroll ke bottom; callback berulang;
  sidebar reversal mulai dari endpoint tetap.
- **Root cause:** unconditional dispatch, tidak mengikuti scroll intent/current width.
- **Change:** follow tail hanya saat berada di bottom; update pada parsed document
  dan extent change; coalesce dispatch pada Loaded/layout priority; user send/reset
  mengembalikan tail following. Sidebar mulai dari current actual width.
- **Why minimal:** local view fields/events dan animation existing; runtime tidak berubah.
- **Verification:** long-response test untuk tail, scroll-up, appended tokens dan
  kembali bottom; sidebar command expand/collapse dan window resize pada fixture.

## ISSUES REJECTED

- Menghapus seluruh SizeToContent measurement: RichTextBox native tidak shrink
  untuk short bubble; regression test membuktikan allowance formatter masih perlu.
- Mengganti FlowDocument dengan ItemsControl/per-block UI controls demi rounded
  code card: akan menghilangkan native selection lintas paragraph/list/quote/code/table.
- Mengubah quote, action-row hover geometry atau assistant transparent chrome:
  existing source/fixture tidak membuktikan masalah; hover row memakai opacity dan
  mempertahankan layout slot.
- Mengubah global theme token: existing NexusContentBorder sudah mencukupi.
- Menambah markdown/editor dependency, virtualization atau layout service: tidak diperlukan.

## FILES MODIFIED

1. `src/03_App/Nexus.App/AIWorkspace/Markdown/NexusMessageParser.cs`
2. `src/03_App/Nexus.App/AIWorkspace/Markdown/MarkdownPlainText.cs`
3. `src/03_App/Nexus.App/AIWorkspace/Rendering/MessageContentPresenter.cs`
4. `src/03_App/Nexus.App/Views/ConversationView.xaml`
5. `src/03_App/Nexus.App/Views/ConversationView.xaml.cs`
6. `src/03_App/Nexus.Integration.Tests/AIWorkspaceRenderingTests.cs`
7. `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`
8. `docs/AI_WORKSPACE_RENDERING_FIX_REPORT.md`

Repo sudah mempunyai banyak perubahan staged/unstaged/untracked saat mulai,
termasuk renderer. Daftar di atas adalah file yang diedit sesi ini, bukan klaim
bahwa seluruh git diff dalam file tersebut berasal dari sesi ini.
Build menggunakan configuration RenderingVerify; output bin/obj adalah generated artifacts.

## RENDERING BEHAVIOR VERIFIED

| Requested scenario | Evidence |
| --- | --- |
| 1. Empty state | Window screenshots; title/composer satu host, center X cocok |
| 2. User message | Native selectable control, short/long bubble tests, snapshots |
| 3. Assistant paragraph | Native RichTextBox fixture dan snapshots |
| 4. Heading | Parser suite, native paragraphs, H1 snapshot; H4–H6 mapping source |
| 5. List | Native nested hierarchy sampai 3 tingkat, start number, snapshots |
| 6. Quote | Native Section, multiline snapshot, cross-block selection |
| 7. Inline code | Native Run/code font, nested fixture, type-transition regression |
| 8. Code block | Header/copy/indent, open fence update, ASCII regressions, snapshots |
| 9. Table | Header/borders/wrap snapshot, column alignment, cross-block selection |
| 10. Nested markdown | Bold+Italic dua arah, strike dan escape tests |
| 11. Long response | 50-paragraph runtime scroll fixture dan long bubble test |
| 12. Streaming | Incremental parsed/fallback updates, stable identity/selection/code copy; simulated input |
| 13. Resize | Actual Window.Width berubah +100 lalu kembali pada setiap width/theme/sidebar case |
| 14. Sidebar collapse/expand | Actual ToggleSidebarCommand dan WPF animation endpoint/layout |
| 15. Light theme | Actual themed windows dan screenshots |
| 16. Dark theme | Actual themed windows dan screenshots; existing dynamic color test |
| 17. Copy | Windows clipboard code copy, updated streaming code source, existing message copy tests |
| 18. Selection | Paragraph selection dan cross-block selection paragraph/list/quote/code/table; choice options |

Screenshot artifacts: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-rendering-*.png`.
30 distinct filenames: five widths × two themes × empty/sidebar-closed/sidebar-open.
Snapshot inspection mencakup Light/Dark wide layout, Dark empty state, dan Light
600/sidebar-open; geometry assertions mencakup seluruh matrix. Snapshot adalah
ConversationView nyata pada window WPF offscreen, bukan screenshot sesi LLM live.
Pengujian hover dilakukan dari source/layout-slot contract; tidak ada automated
physical-mouse drag atau manual desktop selection session.

## TESTS

SDK pada PATH tidak tersedia; portable Temp/dotnet8 SDK dapat build tetapi
vstest.console.dll-nya tidak lengkap. Verifikasi final memakai SDK existing
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (8.0.424).

Working directory: `D:\NEXUS\Nexus-CSharp`.

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build "src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj" -c RenderingVerify --nologo

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test "src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj" -c RenderingVerify --no-build --filter "FullyQualifiedName~AIWorkspaceRenderingTests|FullyQualifiedName~AIWorkspaceMessageParserTests|FullyQualifiedName~AsciiCodeBlockSizeToContentTests|FullyQualifiedName~AsciiDiagramNormalizationTests|FullyQualifiedName~ConversationViewModelTests|FullyQualifiedName~NexusClipboardTests" --logger "trx;LogFileName=nexus-rendering.trx" --results-directory "C:\Users\Syand\AppData\Local\Temp\opencode" --nologo
```

- Final build: **PASS**, 0 errors, 11 warnings pada source existing di luar fix rendering.
- Final test: **PASS**, 225 passed, 0 failed, 0 skipped; duration 13 seconds.
- TRX: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-rendering.trx`.
- Existing source-contract tests diperbarui untuk shared reading-width resource
  dan reserved scrollbar lane; behavioral assertions tetap diuji.
- `git diff --check` untuk tracked files yang diedit: PASS; Git hanya memberi
  informasi normalisasi LF/CRLF existing.

## REGRESSION

Tidak ditemukan regression pada 225 test relevan. Cross-block native selection
tetap ada; tidak ada migration message content ke ItemsControl. Eksperimen
penghapusan formatter allowance dan Grid star column ditolak/dikoreksi setelah
runtime tests/snapshot menemukan wrap/header artifacts. Final fixtures lulus.

Batas hasil: code/choice native containers persegi; long code memakai native
wrapping, bukan horizontal code scroller; parser existing bukan seluruh CommonMark
(contoh: mixed-kind nested lists dan escaped pipe dalam tabel); live LLM streaming
dan interaksi physical-mouse belum diverifikasi. Tidak mengklaim seluruh task PASS.

## NOT TOUCHED

Engine / llama.cpp / model selection logic / conversation runtime / prompt logic /
persistence / storage / database / history / agent architecture / AI Check /
image pipeline / engine services / network architecture / unrelated modules.
Subsystem terkait dapat ikut dikompilasi melalui ProjectReference, tetapi source-nya
tidak diedit oleh sesi rendering ini.
