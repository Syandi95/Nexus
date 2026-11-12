# Gate I2-B — I2B-NL-P1 implementation

## CURRENT RESULT — owner recovery, 15 Oktober 2026

**Gate I2-B HOLD: deterministic implementation/recovery GREEN, actual native NL
functional acceptance FAILED safely. Gate J HOLD.** Ownership conflict telah
direkonsiliasi; ini bukan lagi HOLD karena single-writer ambiguity.

**331/331 impacted tests PASS, 0 skipped.** Dua actual Agent runs telah dipakai;
keduanya berakhir `OutputLimit`, tanpa accepted native tool call, diff approval,
mutation, build atau tests pada live run. **2/4 native generations, 2/2 actual Agent
runs, 0 read-only prerequisite generations, 0 live source mutations.** Allocation dua
native generations yang belum dipakai bukan izin menambah Agent run atau retry.

Bagian bernomor 1–6 di bawah tetap menjadi **catatan historis checkpoint HOLD sebelum
owner recovery**, bukan status saat ini. Checkpoint/evidence historis tidak diubah.

### R1. Frozen Harness UI candidate dan attribution

Keenam file berikut cocok byte-for-byte dengan observed HOLD snapshot. Working tree,
full Git status (termasuk seluruh untracked paths), staged/unstaged patches, source
snapshots dan diffs telah disimpan sebelum perubahan recovery. Candidate ini **bukan
certified Gate H baseline** dan keenam file tidak diedit oleh recovery:

| Path relatif `src/03_App/Nexus.App/` | Frozen SHA-256 |
|---|---|
| `App.xaml` | `3df99ccc8c9476d59b8890649d92bdb567815e14566be57f5382e659d5d72d18` |
| `Themes/Semantic/DarkSemanticTheme.xaml` | `521d9e13995713be073919ae68e61374223476971421016c5e78ffac28e38b4b` |
| `Themes/Semantic/LightSemanticTheme.xaml` | `c53f9599b05fdca9ba0d47b894b5b80150150dc4b17ef614fb3e11a715c9320f` |
| `Views/AgentWorkspaceView.xaml` | `94b116028c13d5682f7afffcf5ac532a7296958cf9f6bd077b86b1e39e21669c` |
| `Views/AgentWorkspaceView.xaml.cs` | `e82a71e32bc13278abb4020ef80ddc8e4de79eebbbd563f44a5ffeb0922f9f59` |
| `Views/ConversationView.xaml.cs` | `9edd062385012549853af11d12a847d3f7c454f7f25796fd27c421ddae4f865e` |

Attribution dibandingkan dengan Gemini Stage 2 report
`D:\NEXUS\NEXUS_DEEPSEEK_HARNESS_UI_PARITY_VERIFICATION.md`, SHA-256
`ec37a6c7b09f0b471e6ba7a9d156aa8753cdbe111d5647f6ac36aa3f0ad864f9`.
Report menyebut keenam path/perilaku tersebut dan empat support files DshIcons,
DshDesignTokens, DshSidebarView.xaml/.cs. **Report tidak mempunyai per-file SHA
manifest/diff receipt**: byte identity berasal dari current snapshot + HOLD comparison;
klaim report “100% parity/zero conflicts” tidak diadopsi sebagai certification.
Reference closure archive untuk LightSemanticTheme.xaml dan ConversationView.xaml.cs
memiliki hash berbeda dari starting baseline I2-B; reference diffs itu tidak diklaim
sebagai exact Stage 1 deltas. Tidak ada unknown baseline drift lain.

Manifest: `ownership-recovery/frozen-harness-candidate.json`. Source I2-B lainnya
cocok dengan HOLD identity sebelum recovery. SOL menjadi single writer sesuai owner.

### R2. Root cause 51 path failures — setiap kasus dipetakan

Seluruh 51 original failures diperiksa melalui actual TRX: semuanya
`DirectoryNotFoundException`, dengan missing path yang dipetakan ke file source fisik
yang ada. Shared root cause: helper memakai empat parent dari `AppContext.BaseDirectory`,
sementara output isolated lama satu tingkat lebih dangkal:

- Lama: `.../Nexus.Integration.Tests/bin/gate-i2b-owned-20261015` → `src/Nexus.App/...`.
- Benar: `.../bin/gate-i2b-owner-recovery/net8.0-windows` → `src/03_App/Nexus.App/...`.
- Runner working directory tetap `D:\NEXUS\Nexus-CSharp`; SDK/testhost tetap pinned
  `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe`, SDK8.0.424,
  .NET runtime8.0.30. Fresh actual testhost token **Medium**, SID `S-1-16-8192`.
- Historical integrity level tidak direkonstruksi dari tebakan path; fresh diagnostic
  merekam cwd, assembly/output, actual host executable, runtime dan resolved source.

Perbaikan hanya layout output external runner; tidak mengubah resolver/assertion source
atau production agar mengikuti runner. **51/51 lookup berhasil**, tetapi setelah lookup
benar hasilnya **44 PASS / 7 legitimate old UI-contract failures**. Tujuh ini dianalisis
terpisah, bukan disebut path failures atau disembunyikan. Full mapping/stack setiap test:
`ownership-recovery/path-recovery.json`.

### R3. Dua CF2 contracts, contrast, dan approved HC fallback

Original RED dua theory cases dipertahankan pada kandidat yang sama. Old assertions
membandingkan row background dengan `NexusSuccessStroke` / `NexusErrorStroke`. Frozen
Harness memakai dedicated `DshDiffAddedBgBrush` / `DshDiffDeletedBgBrush`:
Light `#E6F4E7`/`#FCE6E2`, Dark `#1F3124`/`#3C1F1B`. Expected mengikuti role resource,
bukan mengganti angka hingga PASS. Actual foreground fallback tetap `NexusForegroundPrimary`
karena frozen dictionaries tidak mendefinisikan `DshDiff*FgBrush`; deviasi dari prose
Gemini dicatat, tidak diisi dengan fabricated expectation.

Test tetap memeriksa exact raw diff/text/selection, marker `+`/`−`, Light/Dark/HC,
foreground role dan contrast **≥4.5**. Ini menemukan real HC defect: system text hitam
di cached Dark canvas `#151517`. Owner memberi approval tambahan spesifik untuk
`Themes/Semantic/HighContrastSemanticTheme.xaml`: **hanya tiga alias**
`DshBgBaseBrush`→System Window, `DshTextPrimaryBrush`→System WindowText,
`DshBorderL2Brush`→System WindowText (plus explanatory comment). Tidak ada palette statis,
layout, composer, approval atau authority change. RED contrast dipertahankan; kedua CF2
cases kemudian **GREEN pada ketiga semantic themes**, termasuk keyboard focus/select-all.
Native OS High Contrast mode tidak ditoggle; proof menggunakan existing semantic-theme loop.

### R4. Tujuh expected reviews dan real history regression

Owner memberikan explicit review approval untuk tujuh contracts di existing-owned
`ConversationViewModelTests.cs`, lalu approval tambahan satu file untuk history repair.

| Original contract | Reviewed current contract / preserved invariants |
|---|---|
| Sidebar panel kanan + grouped list | Harness sidebar kiri column0, main column1, row-span2, inherited DataContext; grouped ItemsSource dan GroupStyle tetap diuji |
| E90C sidebar font glyph | `DshIconPanelLeft` SVG resource; same ToggleSidebarCommand dan accessible name tetap wajib |
| DeepThink NexusIcon glyph in UI | `DshIconThink` vector; no raw Unicode-code text tetap wajib |
| DeepThink UI uses registry font | Vector font-independent + real nonempty Geometry; legacy registry font/application tests tetap dipertahankan |
| Run settings + minimal More link | Same RunSettings bounds; More command dipulihkan pada actual Harness sidebar, border0/transparent tetap |
| Conversation list scroll ancestor | Outer Auto/Disabled-horizontal viewport pada actual Harness sidebar; chat viewport bounds tetap |
| Thinking icon/text/tooltip/a11y states | SVG + approved R1 tooltip, actual automation peer name/help text; command/OneWay state, semantic triggers tetap |

Review history menemukan real regression: VM default delapan sessions masih berlaku,
tetapi Harness tidak menyediakan ToggleHistoryExpandedCommand. Tidak mengubah test
agar menerima kehilangan sesi. Owner menyetujui **`Views/DshSidebarView.xaml` saja**:
existing-command button `HistoryExpandLabel`, `HasMoreConversations`, accessible name,
minimal transparent style, dalam existing outer scroll. Grouping, storage, session
authority dan composer tidak berubah. Actual WPF targeted RED→GREEN membuktikan
button **8→12→8**, outer viewport bounded/scrollable, scroll offset bergerak dan backing
history tetap12. Collector test menunggu queued automation invoke sebelum assertion;
tidak menambah sleep/retry-until-pass. Ketujuh contracts dan history recovery GREEN.

### R5. Exact recovery delta dan regression

Tambahan/modifikasi setelah HOLD hanya:
- Existing-owned `ArchitectureGovernanceTests.cs`: actual testhost/path/integrity receipt.
- Existing-owned `ConversationViewModelTests.cs`: tujuh approved contract reviews dan
  actual history/button/scroll test.
- Newly approved `GateIAgentWorkspaceUiTests.cs`: dua visual contracts + contrast/selection.
- Separately approved `HighContrastSemanticTheme.xaml`: tiga system-color aliases.
- Separately approved existing Gemini `DshSidebarView.xaml`: restore More command button.
- External owned runner/output layout, diagnostic scripts dan WPF collector. **Tidak
  mengubah source-write authority, permissions, caps, deadline, SDK graph atau renderer.**

Final relevant impacted regression: **331 executed / 331 PASS / 0 FAIL / 0 skipped**.
Original RED/path/HC/history failures tetap tersimpan. Completed Slice A/B proof tidak
diulang tanpa perubahan relevan; full Gate H certification tidak dijalankan.

### R6. Actual WPF native end-to-end outcome — mandatory acceptance NOT MET

Actual WPF memakai native picker, explicit read dialog, separate reviewed consent,
Temporary Chat dan native production shared runtime. Isolated proof config disiapkan
dengan copy original config ke owned output saja, menuju existing Models/Engine roots;
original app config hash tetap sama. Passive forwarding HTTP capture merekam actual
requests/responses tanpa mengubah model payload/response semantics.

| Planned task | Actual result |
|---|---|
| Controlled failure/recovery | Run `3cc9bb20-462f-41e9-9414-dbc27f499ed0`: Failed / OutputLimit; 74.187 s Agent duration; 256 completion tokens, 1033 prompt tokens; zero accepted tool call, no source plan/build/test/write |
| Correct NL success task | Run `0020ccb4-1879-426e-a655-c1159fea8b62`: Failed / OutputLimit; 75.299 s Agent duration; 256 completion tokens; zero accepted tool call, no source plan/build/test/write |

Both responses finish `length`, return function-call-like **text**, and have no native
`tool_calls`. Strict existing parser rejects them; no text-to-tool fallback, cap increase,
repair or retry. Ini **functional acceptance failure**, bukan native tool-call success dan
bukan exemption Gate H P1.

Original collector salah memakai prior `HasResult` untuk menunggu task kedua; UI attempt
kedua terinterupsi saat preflight sebelum actual Agent/native dispatch. Semua marker
dan failed evidence dipertahankan. Wait diperbaiki ke actual `IsRunning`; hanya reserved
success slot kedua dilanjutkan, dengan guard bahwa original log tepat satu RunStarted
dan native2 belum ada. **Total actual RunStarted=2, native POST=2**, bukan retry task pertama.
Sequential App restart berarti same-PID cross-run proof tidak diklaim; setiap App tetap
memakai satu production shared runtime, tanpa paralel/foreign process cleanup. Verified
Job cleanup kosong pada kedua invocation.

Actual proven: project selection, separate consents, NL-only input, shared runtime use
per run, truthful OutputLimit presentation, safe zero-write rejection/revoke, 17 source
hashes unchanged. **Not reached:** live validated proposal, actual exact diff review,
plan approval, source edit, build/3 facts, controlled live recovery dan artifact/MVID
acceptance. Deterministic/disposable success/recovery tidak dipromosikan menjadi proof live.

### R7. Preservation, sealed evidence, next decision

Six frozen UI hashes tetap identik; tiga support files Gemini lainnya tetap identik;
DshSidebar delta hanya approved history recovery. Semua 17 pilot sources unchanged;
manifest `cc1f9432ccfd60c150554212a7ff3d32319184594da801e531c2efebeb036582`
tetap sama. Staged SHA tetap
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
Tujuh historical Gate H/CF1/CF2/I2-A archives terverifikasi unchanged; tidak ada unknown
source-baseline drift, reset/restore/stash/clean/commit atau overwrite user work.

Current recovery evidence/identities: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2b\ownership-recovery\recovery-summary.json`
dan `recovery-final-seal.json`; report, source candidate dan evidence archives disegel
dengan filename baru (historical HOLD archive tidak diubah).

**STOP native proof pada two-run limit.** Melanjutkan compatibility/prompt/model/protocol
investigation atau additional live runs memerlukan keputusan owner/budget baru; report
ini tidak mengusulkan parser weakening, general execution authority atau model cap change.
**Gate I2-B belum PASS. Gate J HOLD. Composer redesign tetap backlog.**

---

## Historical pre-recovery HOLD checkpoint

15 Oktober 2026 · **PARTIAL IMPLEMENTATION / HOLD — SOURCE OWNERSHIP CONFLICT.**

Owner telah menyetujui implementation scope I2B-NL-P1. Ini bukan pengulangan Stage 1.
**Gate I2-B belum PASS; Gate J tetap HOLD.** Native generations **0/4**, read-only
proposal prerequisite **0/1**, actual native Agent runs **0/2**. Pilot belum dimutasi.

## 1. Stopping condition yang ditemukan

Reconciliation terhadap baseline 679 source/report menemukan **enam perubahan di
luar ownership I2-B**, yang tidak diedit oleh implementasi ini:

| Path relatif `src/03_App/Nexus.App/` | SHA-256 baseline → observed, prefix |
|---|---|
| `App.xaml` | `789e91e0fe1c` → `3df99ccc8c94` |
| `Themes/Semantic/DarkSemanticTheme.xaml` | `4a5a6fc87f47` → `521d9e139957` |
| `Themes/Semantic/LightSemanticTheme.xaml` | `4bc2e5e6df92` → `c53f9599b05f` |
| `Views/AgentWorkspaceView.xaml` | `571cfab2b8e6` → `94b116028c13` |
| `Views/AgentWorkspaceView.xaml.cs` | `66767fc5c18b` → `e82a71e32bc1` |
| `Views/ConversationView.xaml.cs` | `55f5ba0d7776` → `9edd06238501` |

Observed renderer memakai `DshDiffAddedBgBrush`/`DshDiffDeletedBgBrush`, sedangkan
existing CF2 assertions mengharuskan `NexusSuccessStroke`/`NexusErrorStroke`.
Tidak dilakukan restore, overwrite, rebaseline diam-diam, perubahan golden/assertion,
atau perluasan ownership. Implementasi/proof dihentikan ketika konflik terkonfirmasi.
Owner perlu menetapkan baseline UI yang sah dan koordinasi single writer sebelum
melanjutkan. Full before/observed hashes ada di `ownership-reconciliation.json`.

## 2. Actual source delta dalam ownership

Abbreviation: App=`src/03_App/Nexus.App`, IT=`src/03_App/Nexus.Integration.Tests`,
Edit=`src/02_Modules/Nexus.AI.EditingLite`.

**14 existing production files:**
- App `App.xaml.cs`, `Views/ConversationView.xaml`,
  `ViewModels/ConversationViewModel.cs`, `ViewModels/AgentWorkspaceViewModel.cs`.
- App `Services/AgentWorkspaceWorkflow.cs`, `ProjectReadContracts.cs`,
  `ProjectReadContextWorkflow.cs`.
- App `Services/Agent/AgentContracts.cs`, `AgentRunLimits.cs`, `SharedRuntimeAgent.cs`,
  `AgentOrchestrator.cs`, `Tools/ReadOnlyToolPolicy.cs`.
- Edit `Services/SourceVerificationProfile.cs`, `SourceVerification.cs`.

**5 existing tests:** IT `AgentOrchestrationTests.cs`, `GateIAgentWorkspaceTests.cs`,
`ConversationViewModelTests.cs`, `ArchitectureGovernanceTests.cs`; EditingLite.Tests
`Services/SourcePlanTests.cs`.

**4 new files:**
- App `Services/Agent/NaturalLanguageSourceProposal.cs`.
- App `Services/ReviewedProjectCatalog.cs`.
- Edit `Services/ReviewedProjectVerificationManifest.cs`.
- IT `GateI2BNaturalLanguageEditTests.cs`.

Tidak ada perubahan pada `ToolExecutor`, `ApplySourcePlanTool`, strict native/schema
validator, existing approval provider, SafeWrite, path/file security, SDK process/temp
implementation, Chat runtime owner, atau renderer CF2 oleh pekerjaan ini. Enam
observed foreign files dicatat terpisah dari 23-file ownership candidate.

## 3. Implementasi yang sudah tersedia

- Exact UTF-8/BOM source bridge melalui current I2-A grant; tidak memakai normalized
  Chat fragments sebagai original edit text. Byte/hash/file identity direvalidasi.
- Opaque host `SourceProposalScope`, bind sekali ke RunId/session/profile/read grant;
  full snapshots, original declaration ranges, cancellation, read-byte consumption,
  one initial proposal dan fixed budgets. Hash/identity fences mempertahankan external
  edits; own approved recovery dibedakan dari pergantian identity eksternal.
- Existing dialect `nexus_apply_source_plan` slots a/b: strict existing argument parser,
  unique ordinal resolution/overlap checks, UTF-8/Unicode/newline/hash fidelity,
  retain declaration + satu adjacent code line, no-op/unknown slot/range denied.
- Orchestrator membentuk immutable host intent setelah proposal valid, menyimpan
  run-local fact, lalu memakai executor/tool/approval/SafeWrite/recovery yang existing.
  I1 literal mode tetap ada; fixture constructor/mapping tidak diperluas.
- Reviewed root/catalog fixed; read consent dan one-shot reviewed edit/verification
  consent terpisah. Global source catalog tetap disabled/default-deny. Composer Edit
  kode menerima NL tanpa Old/New pada profile baru. Exact-plan approval tetap terpisah.
- Deadline tetap 120 s termasuk preflight/approval, SDK build/test 15 s, native cap256,
  max2 model turns, one plan, repair0/retry0. Tidak ada shell/Git/general writer baru.

Structural validation bukan pembuktian makna instruksi. Exact diff consent dan behavior
facts tetap wajib. Pinned harness **bukan sandbox**; approved code berjalan dengan hak
proses verifikasi.

## 4. Pilot dan offline safety proof

Prepared root: `D:\NEXUS\I2B-ReviewedContracts`; complete 17 source asli Nexus.Contracts
disalin byte-identik beserta provenance. Hanya dua writable target sesuai scope.
Working-copy source **17/17 masih baseline** pada stopping point.

Trusted harness `.nexus-verification` memakai explicit compile inputs, offline cached
assets, SDK8.0.424, no restore/network di Agent, ancestor/wildcard/user hooks disabled
atau diarahkan ke pinned empty files. `.csproj.user` dipin. Original copied `.csproj`
hanya data. Import VSTest ke **pinned SDK target** eksplisit.

- Final pre-live manifest SHA-256:
  `cc1f9432ccfd60c150554212a7ff3d32319184594da801e531c2efebeb036582`.
- 18 graph files, 6,433 dependency files, exhaustive 92-file baseline artifact inventory.
  Execution leases mem-pin read handles; unexpected/drifted artifacts ditolak.
- Actual compiler query: expected17 product inputs + trusted generated inputs;
  **Analyzer/source generator items = 0** pada product dan tests.
- Actual preprocessed graph tidak memuat ancestor auto-sign target. Disposable ancestor
  `Exec` marker tetap absent pada execution success dan required-fact failure proof.
- Existing SourceVerification tetap satu live execution launcher, fresh hash/MVID,
  exact three-fact TRX inventory, verified Job/drain dan recovery.
- Required facts: `I2BContracts.RequiredFacts.CpuFlag`, `ActiveSessionFlag`, `Compatibility`.

Continuous Recovery sebelum live: Test SDK semula memaksa Exe dengan GenerateProgramFile
disabled (CS5001); harness dijadikan library eksplisit. Kemudian disabled wildcard imports
meniadakan VSTest target (MSB4057); diperbaiki dengan explicit SDK import. Kandidat manifest
sebelumnya dipertahankan dengan alasan supersession, **tanpa writable-source rebaseline**.
Failed text/TRX evidence dipertahankan. Binary log baseline build pertama terlanjur tertimpa
oleh build diagnostik berikutnya; kekurangan evidence ini dicatat, dan tooling kemudian
memakai nama binlog unik. Tidak ada native retry/top-up.

## 5. Tests dan actual WPF evidence

Evidence root: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2b`.
Semua invocation yang selesai dibersihkan hanya melalui owned anonymous Job; membership
sesudah cleanup kosong. Tidak ada process-name/historical-PID cleanup.

| Verification | Actual result |
|---|---|
| Slice A RED → GREEN | Proposal admission dan malformed/duplicate/unknown-slot/range/overlap/no-op/encoding/Unicode/CRLF/BOM targeted cases PASS |
| Native protocol/orchestrator replay | 9 bounded subcases PASS: exact reject through existing executor, malformed, OutputLimit, invalid tool finish, byte drift, identity drift, revoke, wrong session, expiry; no real generation/write |
| Reviewed graph negatives | Exec/import/linked-source/analyzer/missing-assets/readonly-source/user-hook denial cases PASS |
| Disposable existing pipeline | Success + build-failure + test-failure + reject + expiry + cancellation + external-conflict cases exercised; success initially FAIL due missing VSTest target |
| Directed harness recovery | Fresh success **3/3 facts PASS**; controlled CpuFlag failure **2/3 PASS**, exact failure inventory and baseline recovery PASS |
| Composer/governance | Separate read/execute consent, NL-only routing without literals, global catalog disabled, one shared runtime/executor/writer PASS |
| I1 targeted compatibility | Original fixture constructor/target mapping/default filter remains strict PASS |
| Actual WPF preflight | Native picker, separate read dialog, separate reviewed consent checkbox, NL composer without Old/New, revoke disables edit; **PASS, 0 generation/Agent run/source write** |
| Impacted IT regression | **329 executed: 276 PASS, 53 FAIL; 0 skipped** — belum GREEN |

Impacted failures: **51** source-file lookup failures karena alternate generated output
berbeda satu level directory dari baseline test layout; **2** CF2 brush assertions berbeda
dengan observed unowned renderer. Assertions tidak dilemahkan. Environment-only path
recovery dan source-owner reconciliation belum dilanjutkan setelah stopping condition.

Engineering build awal terkena locked App output milik active .NET host. Recovery memakai
owned generated output, dependency provenance tercatat. Satu percobaan global append-TFM
override menyebabkan missing reference paths; override dihapus dan build berhasil. Collector
awal gagal transitive System.Text.Json10 resolution; existing ProjectReference pattern
memperbaiki collector, lalu actual WPF preflight PASS. Collector copy config masih menunjuk
test model/engine folders; isolated proof config perlu dipersiapkan ke existing approved
model/engine roots sebelum live. Tidak ada model/runtime baru dipasang.

**Belum dibuktikan:** actual native NL proposal, actual prewrite exact diff + independent
approval, two-file live mutation, live build/3-fact success, actual controlled-failure
recovery, dan final live artifact/MVID receipt. Dua reserved Agent runs belum dipakai.

## 6. Preservasi, limitations, status

Staged SHA-256 tetap:
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
Reconciliation menemukan 19 intended existing changes, 6 unowned changes, dan 654 baseline
source/report tetap cocok; 4 new owned files dicatat terpisah. Tidak diklaim seluruh source
unchanged. Policy/scope/historical reports tidak ditulis ulang. Archive preservation dan
candidate/observed hashes disegel di `hold-seal.json`.

OSS decisions mengikuti approved scope: USE/ENHANCE Nexus; ADAPT approved conversation/review
patterns; no new runtime dependency/framework/patch engine. REA tidak digunakan, tidak ada
penelitian OSS/instalasi tooling baru atau full Gate H certification.

Limitations tetap: reviewed root tunggal; dua target UTF-8 kecil; bounded adjacent additions;
no general edit/single-file/UTF-16 write support; semantic correctness tetap membutuhkan
review dan behavior tests. Pilot/project-general usability dan native reliability belum
dapat diberi PASS tanpa acceptance yang tersisa.

**Keputusan berikutnya milik owner:** reconcile enam foreign files dengan baseline yang
disetujui, konfirmasi single writer dan scope untuk kontrak CF2 bila dipertahankan. Setelah
itu lanjutkan pending recovery/acceptance dalam budget yang belum dikonsumsi. **Gate J HOLD.**
