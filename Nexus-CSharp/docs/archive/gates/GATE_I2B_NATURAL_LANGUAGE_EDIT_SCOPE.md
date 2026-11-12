# Gate I2-B — Natural-Language Source Edit / Stage 1

14 Oktober 2026 · **REVIEW COMPLETE; implementation dan Gate J HOLD.**
Rekomendasi tunggal: **`I2B-NL-P1` — proposal model dibatasi host, satu existing
Agent/source-plan pipeline, satu real-project profile yang direview eksplisit.**

## 1. Actual findings / reuse

- I2-A `ProjectReadContextWorkflow` menyediakan explicit session/root grant, catalog,
  identity/hash/revalidation. **USE**, tetapi read grant bukan write/build authority.
  `PrepareAsync` menghasilkan potongan dan menormalisasi newline; **jangan memakai
  fragment Chat sebagai original exact-edit text**. Perlu full byte-faithful snapshot
  dua target melalui reader yang sama, dalam read grant/byte limits existing.
- I1 `SharedRuntimeAgent` menolak SourcePlan tanpa host `SourceIntent`; system prompt
  meminta model menyalin exact supplied literals. `ApplySourcePlanTool` melakukan
  `SameIntent` terhadap literal host. Missing: validasi untrusted NL proposal menjadi
  host-bound intent; bukan menonaktifkan checks tersebut.
- **USE unchanged execution:** `NativeToolModelAdapter`/strict protocol,
  `SourcePlanArgumentValidator`, `ToolExecutor`, `ApplySourcePlanTool`,
  `ExactSourceEdits`, `EditingWorkflowService` prepare/apply/recovery,
  `AgentWorkspaceApprovalProvider`, CF2 plan-backed diff/card/hide/reopen.
- `SourceVerificationProfile` mem-pin fixture paths/graph; `SourceVerification`
  mem-pin Product/Tests artifacts dan tiga fixture test names. Missing: additive
  immutable real-project manifest/profile, tanpa melonggarkan fixture constructor.
- **Actual execution risk:** repository ancestor `Directory.Build.targets` mempunyai
  `AutoSignNexusOutputs`/`Exec powershell`. Tidak aman menjalankan build proyek yang
  dipilih begitu saja. Verification profile adalah execution authority, bukan sandbox.

## 2. Flow dan authority — model hanya proposer

```text
UI read consent + separately enabled reviewed edit/verification profile
 → fresh exact snapshots + host SourceProposalScope
 → existing SharedRuntimeAgent / native model (one initial proposal)
 → host validates structured proposal → immutable SourcePlanIntent
 → existing ToolExecutor → ApplySourcePlanTool → immutable SourceEditPlan
 → existing diff + independent exact-plan approval
 → existing SafeWrite → pinned build/tests → hash verification / recovery
```

`SourceProposalScope` host-only binds RunId/session/root identity, active read grant,
profile digest, target slots, full source hashes, allowed original-coordinate ranges,
expiry/cancellation and budgets. Tidak bisa dibuat/diubah dari Chat, source atau model.
User memilih **Edit kode**, mengetik NL, lalu Jalankan; tidak ada Old/New input untuk
mode ini. I1 literal mode tetap tersedia pada profil lamanya.

Model mengisi existing sole dialect `nexus_apply_source_plan`:
`a:[{old,new}], b:[{old,new}]`. Paths, hashes, commands, approvals, profile IDs dan
limits **tidak menjadi argumen model**. Slots dipetakan host ke dua target. Natural
proposal tidak dikirim ulang untuk disalin pada model turn kedua: existing
orchestrator memvalidasi respons sebelum dispatch, mencatat accepted host intent
sebagai run-local fact, lalu memakai executor/tool yang sama. Tidak ada executor,
filesystem writer atau approval provider kedua. Global catalog tetap disabled.

Host wajib memeriksa strict schema/duplicate keys/tool IDs/counts, active scopes,
root/file identity, full raw-byte hash, encoding, unique ordinal match, presented
edit ranges, overlap/surrogate/newline fidelity, no-op dan diff/size limits.
`ExactSourceEdits.Resolve` tetap final deterministic resolution; tanpa fuzzy matching,
Markdown patch execution atau “best effort” fallback. Recheck setelah generation,
sebelum approval/first commit dan verification; stale/unknown/out-of-range ditolak.

**Perluasan nyata:** SourceIntent boleh dibentuk host dari *validated model proposal*,
bukan hanya literal yang diketik user. Structural validation tidak membuktikan model
memahami maksud NL. Semantic consent tetap dari user yang melihat exact immutable
diff. Read permission, profile admission dan source-plan approval adalah tiga hal
berbeda; tidak saling menggantikan.

Proposal/approval/build/test memakai **satu120 s deadline**, native cap256 dan existing
Agent input-context cap; I2-A24 KiB tidak boleh otomatis masuk Agent melebihi capnya.
Target source≤4 KiB UTF-8/file,≤256 chars/literal,≤1024 literal bytes,≤2 edits/file,
≤3 edits total,≤2 KiB diff/≤6 changed lines tetap. Pilot tepat dua changed files;
single-file/large-file/UTF-16 writes belum termasuk. MaxSourcePlans1, repair0/retry0.
Malformed/OutputLimit berhenti tanpa partial execution atau automatic top-up.
Revoke/read-expiry/session/project/model switch membatalkan run, deny pending approval
dan drain recovery sebelum scope rebinding. Recovery hanya untuk approved owned
operations; external edits tetap dipertahankan. Hide bukan Cancel dan tidak refill waktu.

## 3. Pilot minimum dan verification profile

**Real project:** complete reviewed working copy dari
`src/00_Contracts/Nexus.Contracts` (17 existing source files, standalone net8.0,
tanpa ProjectReference/PackageReference). Proposed authorized root:
`D:\NEXUS\I2B-ReviewedContracts`. Source asli/certified engineering tree tidak diedit.
Copy ini bukan fixture Alpha/Beta yang diganti nama; seluruh library source genuine
dipertahankan dan source provenance/hash dicatat sebelum task.

Writable hanya:
- `Nexus.Contracts/EngineSessionOptions.cs`
- `Nexus.Contracts/EngineMemoryMetrics.cs`

Contoh NL: **“Tambahkan properti bool IsCpu yang menunjukkan provider CPU dan
HasActiveSessions yang true jika jumlah session aktif lebih dari nol. Pertahankan
API/default yang sudah ada.”** Dua additive expression properties adalah real code
change yang muat batas existing; user tidak memasukkan replacement literals.
Pilot ranges hanya insertion adjacent to `ExecutionProvider`/`ActiveSessionsCount`
declarations; original declarations harus tetap utuh. Other15 source files read-only,
tests/metadata tidak writable. Model tidak menambah target atau membuat file baru.

**Profile `contracts-i2b-p1-v1`:** host-reviewed trusted harness di
`.nexus-verification/Contracts.csproj` + `Tests.csproj` + `RequiredFacts.cs` dalam
root tersebut. Original copied `.csproj` dibaca sebagai data, tidak dieksekusi.
Harness compile explicit17 source paths (no wildcard/linked-outside-root), default
compile/ancestor props/targets disabled, no custom targets/Exec/generators/analyzers
dari proyek, no unresolved imports. Pin SDK8.0.424, graph/source hashes, trusted
dependency assets/package identities/environment dan artifact mapping sebelum run.

Existing `SourceVerification` tetap satu launcher: pinned Rebuild/no-restore lalu
test/no-build/no-restore;15 s masing-masing, current private LocalLow temp dan
verified Job/drain. No project scripts, arbitrary args, restore/network/auto-download
dalam Agent. Missing assets/changed graph ⇒ edit disabled. One-time harness/assets
preparation adalah reviewed offline host setup, bukan loading arbitrary repository
targets; manifest/commands/dependency hashes dibawa ke readiness evidence.

Tiga immutable required facts: `I2BContracts.RequiredFacts.CpuFlag`, `ActiveSessionFlag`,
`Compatibility` (default/alternate provider, zero/positive session counts, original
API/init-only/defaults dan readonly source identities). Artifact hashes/MVID/freshness
dan exact TRX inventory tetap wajib; no success berdasarkan prose/exit0 saja.
Graph/source baru adalah executable code setelah exact approval, **bukan sandbox**;
user harus mengetahui verification menjalankan approved compiled code dengan haknya.
Setelah pilot, stale read snapshots direvoke; tidak ada silent manifest rebaseline
atau promotion ke proyek/root lain.

## 4. Expected exact source ownership setelah approval

Abbreviations: **App**=`src/03_App/Nexus.App`, **IT**=`src/03_App/Nexus.Integration.Tests`,
**Edit**=`src/02_Modules/Nexus.AI.EditingLite`.

Existing production files (14):
- App: `App.xaml.cs`, `Views/ConversationView.xaml`,
  `ViewModels/ConversationViewModel.cs`, `ViewModels/AgentWorkspaceViewModel.cs`;
- App services: `Services/AgentWorkspaceWorkflow.cs`, `Services/ProjectReadContracts.cs`,
  `Services/ProjectReadContextWorkflow.cs` (exact snapshot bridge; read policy unchanged);
- Agent: `Services/Agent/AgentContracts.cs`, `AgentRunLimits.cs` (run-local facts,
  **bukan cap increase**), `SharedRuntimeAgent.cs`, `AgentOrchestrator.cs`,
  `Services/Agent/Tools/ReadOnlyToolPolicy.cs` (proposal visibility, unchanged commit gate);
- Edit: `Services/SourceVerificationProfile.cs`, `Services/SourceVerification.cs`
  (additive catalog path/artifact/required-test mapping; I1 fixed branch preserved).

Existing tests (5): IT `AgentOrchestrationTests.cs`, `GateIAgentWorkspaceTests.cs`,
`ConversationViewModelTests.cs`, `ArchitectureGovernanceTests.cs`;
`src/02_Modules/Nexus.AI.EditingLite.Tests/Services/SourcePlanTests.cs`.

New files (4): App `Services/Agent/NaturalLanguageSourceProposal.cs` (scope/validator),
`Services/ReviewedProjectCatalog.cs`; Edit `Services/ReviewedProjectVerificationManifest.cs`;
IT `GateI2BNaturalLanguageEditTests.cs`.

Pilot working-copy/harness/manifests and external proof/evidence tooling terpisah
dari application ownership. `ToolExecutor`, `ApplySourcePlanTool`, strict native/schema
validator, exact approval provider, SafeWrite/file/path security, SDK process/temp
implementation, Chat runtime owner, CF2 renderer dan certified archives unchanged.
Jika path tersebut tidak cukup, jelaskan kebutuhan sebelum memperluas ownership.

## 5. OSS decision, risks, bounded implementation / acceptance

**USE/ENHANCE** Nexus exact original-coordinate resolver/prepare-first plan, host
scope catalog dan reviewed verification. **ADAPT** pinned OpenCode MIT/Cline Apache2
conversation/proposal/review identity patterns dari approved OSS review; .NET/WPF
presentation tetap native. **REJECT** broad executors/allow-always/shadow-Git,
fuzzy search-replace fallback dan postwrite “Keep” sebagai prewrite approval.
**NOT NEEDED** UI framework, new patch engine, Roslyn/MSBuildWorkspace/dependency.
No OSS install/execution; attempted pinned OpenCode edit-file URL returned404 and
tidak dijadikan klaim bahwa editing engine-nya telah diverifikasi.

Risks: incorrect NL intent (exact review + behavior tests), hostile source/proposal
(untrusted data/strict slots/ranges/no commands), build supply-chain/hooks (pinned
offline harness/profile), stale/scope leak (same-run identity fences/linked cancel),
malicious approved code (not sandbox), model capacity (caps tetap, failure visible).

1. **RED→GREEN proposal admission:** deterministic valid NL-shaped proposal from
   exact snapshots; CRLF/BOM/Unicode, partial context, unknown slot/path, duplicate/
   overlapping edits, changed read grant/profile/root/hash and OutputLimit ⇒ deny
   before write. I1 literal-intent negatives tetap strict; no model self-approval.
2. **RED→GREEN reviewed profile:** malicious ancestor/import/Exec/linked path/asset
   drift denied; only reviewed commands/three TRX facts/fresh artifacts; build/test
   failure/cancel/conflict recovers owned bytes without overwriting external edits.
3. **RED→GREEN Chat integration:** type NL without Old/New, show actual CF2 diff;
   exact approval only, double/stale/Enter/hide/expiry/revoke/session isolation;
   Chat/AI Check/runtime/I1 regressions hanya terdampak setelah integration stabil.
4. Actual WPF: explicit read/profile consent on working copy; zero mutation/process
   starts sebelum exact approval; controlled failing proposal→recovery and successful
   two-file task→build/3 facts/hash PASS. All other sources/graph/original tree unchanged.
   Plan at most one read-only proposal prerequisite plus two Agent runs (failure/
   success), at most4 native generations including final facts; no retry-until-pass,
   no caps enlargement. Reconcile real vs deterministic/replayed evidence separately.

## 6. One approval request / Stage 1 result

**Mohon setujui `I2B-NL-P1` hanya untuk dua perluasan authority:**
**(a)** host may accept a structurally validated model-derived proposal inside the
existing source-plan pipeline, still requiring independent exact-plan approval;
**(b)** one pinned real working-copy write/verification profile and its reviewed
offline execution graph. Scope includes exact files/pilot/preparation/proof budget
di atas; engineering approval tidak menggantikan consent user di actual Nexus UI.
Arbitrary roots/builds, single-file/general edits, repairs dan privilege expansion
tidak diminta. Tidak membuat executor atau mutation authority paralel.

Stage1 hanya dokumen:678 source/report files matched closure baseline, staged SHA
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046` tetap;
**0 production edits/tests/model generations**, no pilot copy/build/restore dibuat.
Evidence/hash receipt: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2b-scope\scope-seal.json`.
**I2-B implementation dan Gate J HOLD sampai keputusan owner.**
