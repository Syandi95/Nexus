# Gate I CF2 — Chat-first diff review UX

14 Oktober 2026 · **CF2 PASS untuk scope terbukti di bawah; Gate J HOLD.**
Baseline: sealed CF1, AGENTS/policy dan pinned OSS UX review existing. Tidak ada
ulang audit, full Gate H certification, atau live model generation.

## Files changed dan preservation

Semua di `src/03_App/`; tidak ada production file baru:

| File | Perubahan | SHA-256 after (prefix) |
|---|---|---|
| `Nexus.App/ViewModels/AgentWorkspaceViewModel.cs` | Copy/status, plan-backed file/line presentation, selection/collapse | `be01506f1b2d5f55` |
| `Nexus.App/ViewModels/ConversationViewModel.cs` | Capability copy, hanya actual review/receipt dapat dibuka | `adf1f87a5761b7fe` |
| `Nexus.App/Views/AgentWorkspaceView.xaml` | File navigation, per-file toggles, colored preview, technical raw diff | `571cfab2b8e6016a` |
| `Nexus.App/Views/AgentWorkspaceView.xaml.cs` | Read-only FlowDocument presentation dan theme refresh | `66767fc5c18b2065` |
| `Nexus.App/Views/ConversationView.xaml` | Edit kode label, compact host-owned file summary/card | `2a09712775835eb5` |
| `Nexus.Integration.Tests/GateIAgentWorkspaceUiTests.cs` | Typed copy, diff fidelity/stats, exact review state tests | `bdbbd2cda92d8cfb` |

**663/669 baseline source/report files tetap identik.** Full before/after hashes,
commands/TRX, app binary identities, tooling dan screenshot provenance ada di
`C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-cf2\preservation.json`,
`identity.json`, dan `seal.json`. Staged SHA tetap
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
CF1 checkpoint/evidence dan Gate H certified checkpoint hashes diverifikasi tetap.
Backend, workflow/allowlist, provider, authority, SafeWrite/recovery, SDK/Job,
120 s/15 s/model caps, Chat renderer/streaming dan AI Check source tidak diubah.

## Slice 1 — bahasa UI

`Edit kode`, `Pilih proyek`, `Jalankan`, `Batalkan tugas`, `Tinjau perubahan`,
`Setujui perubahan`, `Tolak perubahan`, dan empty-state sederhana digunakan.
Capability hint tetap menjelaskan Alpha/Beta, teks yang dicari/pengganti dan
persetujuan wajib; unsupported project: **“Proyek belum siap untuk diedit. Anda
tetap bisa bertanya. Untuk mengedit kode, pilih proyek yang sudah didukung Nexus.”**

Success memerlukan actual source/build/3-test receipts. RecoveryConflict mempunyai
copy berbeda dari baseline-verified Restored; OutputLimit tetap gagal. Raw kategori,
IDs/hashes/digest, raw diff, hasil pemeriksaan dan exception stack tetap di Detail
teknis. Full source result ditambahkan ke presentation receipt, bukan grant baru.

## Slice 2 — review modern

- Card di area Chat menampilkan **“2 file akan diubah”**, setiap actual plan target
  `+1 −1`, dan total `+2 −2`. Tidak ada model-authored activity/history entry.
- Klik file dari card atau panel memilih preview; active state terlihat. Toggle
  per-file, Buka/Ciutkan semua, hide/reopen dan resize mempertahankan file/state.
- Adapter kecil membaca **existing immutable entry.Diff**, tanpa code/diff engine,
  filesystem read, regeneration, atau perubahan digest. Dua metadata lines I1
  dipisahkan dari code preview; raw diff tidak diubah dan tetap selectable.
- Read-only monospace FlowDocument: deletion merah lembut, addition hijau lembut,
  neutral context dan `−/+` markers. Brushes berasal dari semantic resources;
  High Contrast mengikuti system semantic colors serta markers, bukan warna saja.
- Raw whitespace/indentasi/Unicode/newline dipertahankan adapter; preview mengikuti
  native WPF line layout/wrapping dan selection. Raw text asli tersedia untuk
  menyalin exact newline representation. Current I1 tidak punya hunk line numbers;
  **nomor baris tidak direkayasa**.
- “Berhasil diubah dan diverifikasi” pada file summary memerlukan receipt cocok
  dengan reviewed PlanId/digest dan seluruh verification facts, bukan prose model.
- Approval tetap exact provider. Double action/stale UI tidak mengulang grant;
  reject/expiry tidak menulis. UI-only fixture tidak menjalankan approved edits.

USE/ENHANCE Nexus/WPF; ADAPT file summary/navigation/review concepts dari approved
OpenCode/Cline audit dan pola PR. Tidak ada framework/dependency atau global design
change.

## Tests dan visual proof

| Run | Hasil |
|---|---|
| Slice 1 targeted | **8/8 PASS** |
| Slice 2 targeted | **10/10 PASS** |
| Matching receipt summary tambahan | **2/2 PASS** |
| Affected UI regression, sekali | **220/220 PASS**, 0 skip/fail |
| Receipt-only empty-control refinement, targeted | **11/11 PASS** |
| Final actual WPF/copy proof | Exit0, **16 PNGs** |

Affected filter: GateIAgentWorkspaceUiTests, GateIChatFirstWorkspaceTests,
ConversationViewModelTests, AIWorkspaceRenderingTests, AiCheckRunIsolationTests.
App/test builds exit0. SDK8.0.424 + existing runsettings; verified Job-owned
launch/cleanup, empty remaining Job membership. Tidak ada retry model/top-up.

Actual App/MainWindow/resources: production initial Chat, UI-only prepared plans,
three themes, wide1440/900 dan compact700 (actual available DIP dicatat). Native
UI Automation invoke/toggle membuka file dan mengubah collapse. Read-only diff
focus/select/copy diuji; **172 selected characters** cocok dengan native clipboard
text. Exact instance/digest/deadline bertahan hide/reopen; approval footer tetap
terlihat. Historical success/recovery receipts diproyeksikan read-only dan tidak
mengaktifkan approval. Semua state dilabeli di `visual-proof.json`.

Before menggunakan immutable CF1 screenshots, tanpa mengulang CF1 matrix:

- Before: `gate-i-cf1/cf1-visual-final/pending-review-ui-fixture-{Light,Dark}-1440-1,00.png`.
- After: `gate-i-cf2/selected-copy-snapshot/beta-selected-ui-only-{Light,Dark}-1440.png`.
- Compact: `alpha-selected-compact-ui-only-HighContrast-700.png`.
- Hide/card: `summary-hidden-ui-only-Light-1440.png`.
- Replay: `historical-{success,test-failure-recovery}-receipt-replay-Light-1440.png`.

Semua path relatif terhadap external `...\Temp\opencode`; before/after juga dibawa
ke evidence ZIP. Screenshot terpilih diperiksa melalui image tooling. Directed
fixes: harness RaiseEvent(Click) tidak menjalankan ICommand, diganti native UIA;
receipt replay memerlukan existing Refresh notification; clipboard preservation
perlu snapshot managed formats sebelum Copy karena retained OLE object Flush gagal.
Failed invocation/log tetap disimpan; native Copy sendiri sudah cocok pada run
yang mengalami restoration failure, sedangkan final snapshot run exit0.

## Remaining acceptance gaps

Native OS-DPI, physical IME, screen-reader, full physical keyboard navigation dan
full CF1 focus acceptance tetap **HOLD**, tidak dihitung PASS oleh CF2. CF2 membuktikan
scoped WPF focus/selection/copy dan UIA actions, bukan seluruh native interaction.
Tidak ada fresh build/test/source-edit/model execution untuk visual fixtures atau
historical replay. Reviewed engineering workspace/pin tidak di-reset/diperluas.
**Gate J tetap HOLD.**
