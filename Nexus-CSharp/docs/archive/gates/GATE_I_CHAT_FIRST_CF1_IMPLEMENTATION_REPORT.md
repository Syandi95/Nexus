# Gate I — Chat-First CF1 implementation

Tanggal: 14 Oktober 2026. Gate J: **HOLD**.

## Keputusan

**S1/S2/S3 diimplementasikan; affected regression GREEN (219/219), targeted
post-regression mode-independence GREEN. Actual WPF scaled matrix tervalidasi.
Full CF1 UI acceptance masih HOLD untuk native OS-DPI, IME fisik, dan pembuktian
keyboard/screen-reader lengkap.** Ini bukan full solution certification dan bukan
fresh live source-edit/model execution.

## Scope dan baseline

- Scope mengikuti CF1: sepuluh existing files yang disetujui di App/Integration.Tests,
  ditambah `GateIChatFirstWorkspaceTests.cs`. Laporan dan external tooling/evidence
  terpisah dari application ownership.
- Baseline 667 source/report identities di
  `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-cf1\baseline.json`.
  Baseline memakai hash audit sebelum edit; satu known RED admission-test insertion
  sudah ada saat status-before diambil dan dicatat oleh baseline script.
- Staged identity wajib tetap
  `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
  Current before/after reconciliation, HEAD, command/TRX, binary hashes dan archive
  identities tersimpan dalam `cf1-seal.json` di evidence directory di atas.
- Tidak ada reset, restore, clean, stash, commit atau source-baseline refresh.
  Historical Gate H/I/recovery/Phase B/OSS-review evidence tetap dipertahankan.

## Implementasi

### S1 — composer, proyek, mode, draft

- Existing Chat timeline/renderer/composer digunakan kembali. Project selector
  ditempatkan langsung di atas input, dengan capability status yang jujur.
- `Tanya` menuju existing Chat route. `Ubah kode terbatas` menuju Agent hanya dengan
  current explicit request dan literal edits Alpha/Beta. Teks `setujui` di Chat
  tidak berubah menjadi source-edit approval.
- Literal old/new hanya terlihat pada mode edit dengan admitted workspace.
  Project selection tidak sama dengan authority membaca atau menulis file.
- Draft input, attachment context dan literal intent disimpan bounded in-memory
  untuk kombinasi project + conversation/session + mode, maksimum 20 draft scopes.
- Project/session switching cancel/drain Agent sebelum rebinding; activity/review
  dibersihkan saat ganti scope. Context-read completion memakai revision fence.
  Tanya dapat dipilih ketika Agent masih menunggu approval tanpa membatalkan plan.

### S2 — contextual review dan host-owned activity

- Opaque conversation overlay diganti contextual review column. Default closed
  menggunakan seluruh available conversation width. Available width <760 DIP
  memakai review surface dengan Chat/composer disembunyikan sementara; close
  mengembalikan existing surface dan draft, tanpa membuat composer kedua.
- Latest activity berada di host conversation area, tidak masuk `Messages` atau
  model-authored history. Status utama menggunakan bahasa Indonesia dan fakta
  typed verification/recovery; success memerlukan source/build/3 required tests.
- `Tutup tinjauan` hanya mengubah presentation. `Batalkan perubahan` terpisah.
  Hide/reopen mempertahankan pending instance, RunId/PlanId/digest/deadline dan
  tidak memulai run baru. Existing exact provider tetap one-shot/default-deny.
- Full selectable diff dan pinned approve/reject footer dipertahankan. Hash/IDs
  dipindahkan ke expandable Detail teknis plan; hasil teknis penuh tetap tersedia.
- Welcome glow yang menutupi compact review ditemukan melalui screenshot inspection
  dan disembunyikan ketika review aktif.

### S3 — konteks file yang benar-benar dikirim

- `Jelaskan kode` menggunakan actual authorized Alpha/Beta snapshot. Production
  workflow revalidates pinned manifest/root/graph/source/SDK lalu membandingkan
  current selection digest dan snapshot hash. Target lain/traversal/drift ditolak.
- Internal pinned helper dipakai hanya oleh fixture tests; public production route
  tetap menggunakan reviewed root dan manifest hash constants.
- Snapshot content masuk existing explicit attachment/context-budget pipeline dan
  Chat payload, bukan sekadar path label. Snapshot chip memiliki empty filesystem
  path sehingga tidak menjadi arbitrary reader/re-extraction authority.
- Interface default file-context implementation menolak. Tidak ada file indexer,
  shell/Git/MCP, implicit Agent attachment/history, atau tambahan runtime.

## Open-source dan keamanan

USE/ENHANCE existing Nexus renderer/composer, attachment budget, scoped editing,
exact approval, SafeWrite, verification/recovery. ADAPT konsep project/composer
proximity, scoped drafts, contextual panel dan request identity dari pinned OSS
audit `GATE_I_CHAT_FIRST_OSS_UX_ARCHITECTURE_REVIEW.md`. Tidak ada framework,
dependency/runtime transplant atau GPL source copying.

Agent hard deadline 120 s termasuk approval, native256/Chat64 certified limits,
SDK build/test15 s, independent CTS, one initial I1 plan/no automatic repair,
private LocalLow verification temp dan verified Job lifecycle tidak diubah.

## Tests dan contract governance

Evidence root: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-cf1`.
Seluruh failed TRX/log tetap tersedia.

| Evidence | Hasil |
|---|---|
| `s1-red` | Missing routing/admission contract RED |
| `s1-green` | 2/2 PASS; initial MC3088 XAML placement failure retained |
| `s2-red` | Overlay/contextual contract RED |
| `s2-green` | Contextual composition + hide/reopen + S1 PASS |
| `s3-red` | Missing pinned snapshot bridge RED |
| `s3-green` | Snapshot positive target/content/hash + deny/drift PASS |
| `cf1-affected-first` | 215 PASS / 2 FAIL, old UI assertions retained |
| `cf1-directed-green` | Payload/routing + new UI contracts PASS |
| `cf1-affected-final` | **219/219 PASS**, no skip/undiscovered counted PASS |
| `cf1-mode-independence-green` | 6/6 CF1 tests PASS after final mode-switch guard adjustment |

Affected filter includes GateIChatFirstWorkspaceTests, GateIAgentWorkspaceUiTests,
GateIAgentWorkspaceTests, ConversationViewModelTests, AIWorkspaceRenderingTests,
AiCheckRunIsolationTests. Build/test exact commands are in invocation JSON files;
SDK8.0.424/MSBuild17.11.48 with existing environment-dotnet.runsettings. App and
integration test builds exit0. No full Gate H certification or live generation run.

Approved baseline changes:

1. Real mutable engineering workspace admission test → disposable pinned fixture,
   retaining positive admission and source/Tests graph drift rejection. Real
   production pin/source files are not reset or relaxed.
2. Phase B standalone request/review panes/tabs → CF1 contextual review with existing
   request composer. Full diff scrolling/selection, action visibility, exact plan,
   expiry and stale approval assertions remain.
3. Keyboard source contract `SendCommand` → explicit `SendWorkspaceCommand` router;
   Enter/Shift+Enter handling assertions remain.
4. AI Check mode-switcher query scopes Chat/AI Check radios, excluding new project
   task-mode radios; the original two-button/casing/binding contract remains.

Payload test proves actual snapshot content is present inside `<lampiran>`, ordinary
`setujui` invokes no Agent, explicit Agent request excludes Chat history, activity
does not add Chat messages, and new session clears activity. Separate tests verify
project/mode/session attachment drafts and pending-plan hide/reopen identity.

## Actual WPF visual proof

External `gate-i-cf1-visual` harness launches actual `App`/`MainWindow` and production
ConversationView/resources. Initial state uses production DI VM. Further states use
labelled UI-only ReviewWorkflow/disposable fixture and production view models/views;
no source mutation or native-model call is used.

- Final `cf1-visual-final`: exit0, **144 PNGs** = four states × three themes × four
  requested widths (700/900/1280/1440) × layout scales (1/1.25/1.5).
- States: initial production Chat, explicit fixture intent, pending fixture review,
  rejected zero-write. Light/Dark/High Contrast; actual available DIP recorded.
- Verified default panel closed/project row visible; compact non-overlap; review
  visible; approval enabled only with pending state; footer fits viewport; full
  diff selection; welcome layer absent during review; hide/reopen preserves exact
  plan/deadline; rejected approval stays disabled; session clears activity.
- Initial visual failure was harness direct-Agent invocation without copying current
  composer Prompt. Diagnostic proved `CanRun=false`, empty Prompt, no execution.
  Harness corrected to match production router; failed runs retained.
- Earlier structural matrix passed while a welcome layer obscured compact review.
  Screenshot review caught it; final matrix adds the explicit no-glow assertion and
  corrected screenshots supersede that visual acceptance, not historical evidence.
- Representative final PNGs were manually inspected through image tooling.

**Limitations:** 125/150% are layout-transform/scaled RenderTargetBitmap probes,
not OS DPI changes or a per-monitor native WM_DPICHANGED proof. Native IME input,
screen-reader announcements, physical keyboard navigation/splitter movement and
automatic composer-focus return after hide have not all been proven end-to-end.
These remain acceptance items; do not interpret the scoped matrix as their PASS.
Success/recovery terminal receipts from earlier I1 are historical, not fresh CF1
model/source-edit runs. No fresh live-run baseline was authorized under CF1.

## Reviewed engineering workspace dan handoff

`D:\NEXUS\GateI-ReviewedWorkspace` masih memiliki legitimate successful I1 Alpha/Beta
`return 2;`, sedangkan original reviewed manifest mem-pin `return 1;`. Production
admission berikutnya dengan benar menolak drift. Manifest/root authority tidak
diubah. Positive context/admission proofs memakai fixture sendiri.

Application ownership reconciliation dan archive hashes harus dibaca dari external
`cf1-seal.json`. Working checkpoint bukan certification/source-freeze approval.
**Gate J tetap HOLD; native DPI/IME/accessibility acceptance harus ditutup sebelum
full CF1 UI PASS dinyatakan.**
