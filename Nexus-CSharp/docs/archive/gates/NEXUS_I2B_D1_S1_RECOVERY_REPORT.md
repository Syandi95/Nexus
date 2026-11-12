# NEXUS — I2-B-D1 + I2-B-S1 Recovery Report

16 Oktober 2026 · OpenCode / `openai/gpt-6.1-sol` · Native C#/.NET 8/WPF.

**D1 offline replay dan S1 pending-consent fix selesai pada scope yang disetujui. Targeted 25/25 PASS. Impacted 314/318 PASS, empat failures terproduksi identik dengan VM pre-edit.** Ini bukan full-green impacted suite atau certification. **I2-B actual native NL acceptance tetap HOLD; Gate J HOLD.** Tidak ada live Agent/model call, perubahan runtime/template/parser/policy/cap/deadline/dependency, atau audit Harness/REA ulang.

## 1. Approval, baseline, dan evidence root

Owner menyetujui I2-B-D1 + I2-B-S1 dari §7 `NEXUS_AGENT_CAPABILITY_GAP_AND_GATE_M_ROADMAP.md`, termasuk directed RED, minimal VM fix hanya bila terbukti, targeted/impacted regression, dan satu keputusan D2 setelah laporan.

- Single writer: OpenCode untuk tiga source/test paths di §6. Pre-edit VM dan test hashes cocok dengan C1.1; tidak ada unresolved ownership drift sebelum edit.
- Repo HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`; dirty tree/staged/unstaged/untracked work dipreservasi, bukan dianggap baseline HEAD bersih.
- Evidence root **E** = `C:\Users\Syand\AppData\Local\Temp\opencode\i2b-d1-s1-20261016`.
- Immutable historical fixtures **H** = `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2b`; empat berkas asli diverifikasi SHA sebelum disalin byte-identik ke `E\fixtures`. Tidak ada rewrite capture, normalisasi newline/JSON, synthetic replacement, atau perubahan hasil failure historis.
- `E\baseline.json`: 2.583 baseline source/document inputs + 139 default App/IT output identities; status lengkap dan staged/unstaged patches disimpan sebelum edit. Scope inventory dan exclusions bin/obj terdokumentasi dalam external runner; bukan exhaustive hash setiap berkas mesin.

## 2. RED evidence dan continuous recovery

| Invocation | Actual result | Interpretasi |
|---|---|---|
| `red-build` | Build exit1: missing `System.IO` import dan required synthetic `FileSnapshot.AbsolutePath` pada test baru | Test-authoring compile defect, bukan production RED. Diperbaiki di test baru; log tetap disimpan. |
| `red-tests-build` | Build exit0 | Production VM masih byte-identik pre-edit. |
| `directed-red` | **21 executed: 13 PASS / 8 FAIL / 0 skip** | Semua D1 PASS. S1 gagal; tiga failure tertutup fixture teardown IO exception setelah assertion meninggalkan generation mock/leases aktif. Tidak dilabeli seluruhnya clean security RED. |
| `red-teardown-build` → `consent-red` | Build exit0; **8 executed / 8 FAIL**, seluruhnya assertion lifecycle | Teardown test baru memakai `finally` + navigation cancel/drain sebelum fixture deletion; assertions tidak diubah/dilemahkan. Production tetap pre-edit. Ini decisive RED untuk S1. |
| `fix-build` → `targeted-green` | Build exit0; **25 executed / 25 PASS / 0 fail/skip** | Satu directed production fix; 8 original S1 cases GREEN, ditambah 4 async-return boundary cases. D1 13/13 tetap PASS. |

Tidak ada retry-until-pass: invocation ulang terkait compile correction, teardown correction yang menutupi assertion, atau perubahan source/fence. Semua log/started/invocation/TRX memakai nama unik. Beberapa disposable fixture remnants dari teardown awal dipertahankan; tidak ada broad cleanup berdasarkan nama/PID. Test runs setelah teardown correction melepaskan leases dan memverifikasi hash fixture sebelum owned disposal.

### TRX aktual

Semua path berikut relatif E:

- `directed-red\Syand_SYANDI-PC_2026-10-16_19_02_30.trx`
- `consent-red\Syand_SYANDI-PC_2026-10-16_19_03_57.trx`
- `targeted-green\Syand_SYANDI-PC_2026-10-16_19_06_19.trx`
- `impacted\Syand_SYANDI-PC_2026-10-16_19_06_56.trx`
- `baseline-four\Syand_SYANDI-PC_2026-10-16_19_15_22.trx`

Exact result names, outputs, errors, counters dan per-TRX SHA disimpan dalam `E\test-reconciliation.json`. Initial failed evidence tidak ditimpa.

## 3. I2-B-D1 — hasil compatibility replay

Source baru: `src/03_App/Nexus.Integration.Tests/I2BNativeCompatibilityReplayTests.cs`. Prefix FQN tabel = `Nexus.Integration.Tests.I2BNativeCompatibilityReplayTests.`

| Exact test / parameter cases | Count | Bukti dan hasil |
|---|---:|---|
| `SealedLengthResponse_IsRejectedBeforeNativeAcceptanceDispatchApprovalOrMutation(number:1,promptTokens:1033)` dan `(number:2,promptTokens:1021)` | 2 | Request dan response bytes dihash; adapter-generated request fields dibandingkan secara structural JSON dengan request tersegel. Kedua respons mempunyai completion256/finish `length`/tanpa tool_calls. Existing adapter/parser melempar **OutputLimit**. Accepted native calls=0; executor branch tidak tercapai; fake tool calls=0; no approval calls; no source operations/accepted intent. Disposable source tree hashes unchanged. |
| `KnownGoodNativeEnvelope_DispatchesOnlyFakeReadOnlyTool` | 1 | Valid `tool_calls` envelope diterima oleh adapter, kemudian existing executor hanya menjalankan fake read-only `nexus_get_model_snapshot` sekali. Production writer tidak dikomposisi; tidak ada task-success claim. |
| `InvalidNativeEnvelope_IsRejectedBeforeFakeDispatch` — `invalid-finish`, `duplicate-call`, `multi-call` | 3 | Respectively **InvalidToolFinishReason**, **DuplicateToolCallId**, **ToolCallLimit**. Fake body tidak dipanggil. |
| `NativeEnvelopeWithInvalidArguments_IsDeniedByExecutor` — `{malformed`, `{"unexpected":true}` | 2 | Native envelope valid; existing executor menolak arguments sebagai **InvalidArguments**, tool body0/source writes0. Ini argument-stage denial, bukan native-envelope error. |
| `NativeSourceEnvelope_DoesNotBypassProposalValidation` — `unknown-slot`, `wrong-original`, `no-op`, `outside-declaration` | 4 | Synthetic native source envelope lolos wire parser, tetapi existing `NaturalLanguageSourceProposal.Validate` menolak sebelum host edits dieksekusi. Tidak ada writer/executor pada proposal-only composition. |
| `StructurallyValidProposal_IsNotTaskSemanticVerification` | 1 | Structurally valid adjacent-line proposal dengan constant bool menunjukkan counterexample terhadap tugas CPU=false/non-CPU=true. Structural validator tidak menjadi behavioral oracle. Tidak mengompilasi/menjalankan kode model atau mengubah capture historis. |
| **Total** | **13 PASS** | PASS berarti validator menolak input yang harus ditolak atau menerima control yang valid pada lapisan yang tepat; bukan keberhasilan live task I2-B. |

Semua HTTP memakai in-memory `HttpMessageHandler`; tidak ada socket/model inference. Descriptors/messages request asli dipakai untuk replay adapter failure. Strict native protocol, argument validator dan proposal validator **tidak diedit**. Tidak ada regex extraction/fallback dari call-like content.

### Pemisahan empat lapisan

1. **Generation:** historical API responses berhenti pada `length`/256. D1 membuktikan classification/rejection, tidak menghasilkan generation baru.
2. **Native envelope:** tidak ada accepted `tool_calls` pada captures; valid synthetic native envelope diterima. Tidak ditemukan alasan melonggarkan parser untuk dua failures tersebut.
3. **Proposal:** diuji terpisah dengan synthetic valid native envelopes; invalid scope/original/range/no-op tetap ditolak. Live captures tidak mencapai tahap ini.
4. **Task semantics:** structural acceptance tidak berarti kebenaran instruksi. Counterexample dan existing disposable behavior tests memisahkan kedua hal itu; tidak ada claim model sudah menghasilkan proposal benar.

No approval/no writes pada D1 adalah proof composition dan asserted control-flow/state; bukan claim bahwa sebuah real write approval provider dijalankan lalu menghasilkan success. Known-good hanya memakai fake no-side-effect tool.

## 4. I2-B-S1 — root cause terbukti dan minimal fix

### 4.1 Decisive RED

Pre-edit menyimpan pertanyaan hanya pada `_pendingProjectQuestion`. Root/session/revision tidak menjadi bagian pending intent. Revoke/switch membersihkan grant/context, tetapi tidak membuang pertanyaan/description pending. Confirm selalu memanggil `GrantProjectReadAsync()` pada **current selection**, bahkan tanpa pending request yang sah.

Clean `consent-red` membuktikan:

- Project/session switch sebelum confirm: **grantRequests=1, issued=1, prepare=1** walau expected0. Project case memakai current B; session case memakai sesi baru. Mock model dispatch belum tercapai ketika assertion gagal; tidak diklaim ada live model leak.
- Revoke/navigation sebelum confirm: grant terbit kembali dan preparation dimulai tanpa intent baru (expected0, actual1).
- Scope switch/revoke selama grant tertahan: cancellation existing menolak attempt pertama; stale reconfirm memulai **attempt kedua** dan menerbitkan grant. Existing cancellation fence saja tidak menghapus intent lama.
- Same-scope positive control berhasil pertama kali, tetapi konfirmasi ulang menerbitkan grant kedua (expected1, actual2).

Dengan demikian production patch memenuhi syarat approval: RED terukur, bukan speculative fence tambahan.

### 4.2 Delta production — satu file

`src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`:

- Ganti pending string dengan immutable `PendingProjectReadIntent(Root,Session,Revision,Question)`.
- Consume/clear pending record dan visible description **sebelum await** pada Confirm; stale/duplicate confirm tanpa intent menjadi no-op. Cancel hanya memproses intent current.
- Revoke membersihkan pending intent; existing project/session/navigation paths menggunakan revoke sehingga old intent tidak hidup kembali setelah transition.
- `IsCurrentReadScope` memeriksa canonical selected root, session dan read revision. Revision menjaga root/session berubah lalu kembali tidak dianggap consent lama yang sah.
- Capture cancellation source operation-lokal pada grant; revalidate scope/cancellation sebelum grant dan setelah async completion, termasuk current opaque grant identity/root/session/active state.
- Late completion hanya merevoke grant-nya sendiri bila masih current, bukan grant scope yang lebih baru; old operation tidak me-reset preparation state milik operation lain.
- Confirm revalidates captured intent setelah grant. Sesudah awaited `ValidateForDispatchAsync`, scope/current grant divalidasi lagi **sebelum publish context dan StartGeneration**.

Opaque grant/service, read limits, discovery/path validation, routing heuristic, WPF markup/styles/commands, reviewed-write consent, exact-plan approval, SafeWrite dan verification/recovery tidak diganti. Tidak ada authority baru.

### 4.3 Exact S1 tests

Prefix FQN = `Nexus.Integration.Tests.ConversationViewModelTests.`

| Exact test / cases | RED → GREEN / outcome |
|---|---|
| `I2BS1_PendingConsentInvalidatedBeforeConfirm_CannotGrantOrDispatch` — `project`, `session`, `revoke`, `navigation` | **4 RED → 4 GREEN**. Dua stale confirms per case; grant/preparation/dispatch0, no active grant/pending description, source hashes unchanged. |
| `I2BS1_ScopeChangesDuringAsyncGrant_NoLateGrantOrResurrection` — `project`, `session`, `revoke` | **3 RED → 3 GREEN**. One original grant attempt held using explicit TCS, cancellation before grant issuance; stale reconfirm ignored, issued0/prepare0/dispatch0. |
| `I2BS1_SameScopeConsent_WpfConfirmWorksOnce_AndPreservesSources` | **RED → GREEN** for duplicate grant. Actual WPF Window + production ConversationView bound to VM, visible confirm button/command, real I2-A reader on disposable sources. One grant, one source preparation, one **mock runtime** request; duplicate confirm ignored; source unchanged. |
| `I2BS1_ScopeChangesAtAsyncReturn_CannotPublishOrDispatchOldContext` — `project/session` × `grant-return/validation-return` | **4 additional GREEN boundary cases**, introduced with fix, not represented as prior RED. Late provider return deliberately ignores cancellation after real original-scope grant/validation. Switch invalidates it; no new-scope grant, context publication or model dispatch. |
| **Total** | **12/12 GREEN** in targeted and impacted runs. |

The async tests use barriers, not timing sleeps/retry-until-pass. Only a minimal real reader wrapper delays boundaries; it does not mint fake opaque grants. Before/after file hashes cover every file of disposable A/B fixtures. New tests have `finally` cancel/drain/dispose to avoid masking assertion failures.

WPF proof is actual control/command behavior with mock runtime, **not** a new standalone production App/live-model end-to-end acceptance. Existing rendering/theme/syntax tests also pass below.

## 5. Targeted/impacted execution and the four retained failures

### 5.1 Runner and commands

External tooling `C:\Users\Syand\AppData\Local\Temp\opencode\i2b_d1_s1_run.py` reuses existing approved `CertificationJob` unchanged: suspended direct root → verified anonymous Job membership before resume → cleanup only that Job. Every invocation has empty remaining membership and completed log reader. No name/raw-PID/ancestry-based termination.

Pinned SDK host: `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe`, SDK8.0.424, observed test runtime8.0.30. Runsettings: `C:\Users\Syand\AppData\Local\Temp\opencode\environment-dotnet.runsettings`. Builds use `--no-restore -m:1 -p:CopyRetryCount=0`; tests use `--no-build --no-restore --logger trx`. Output: **`bin/i2b-d1-s1-20261016/net8.0-windows/`** per project, preserving four-parent source lookup depth and user/default app output.

Exact commands/argument arrays/CWD/exit/time/Job records: `E\*-started.json`, `E\*-invocation.json`; logs `E\*.log`. Invocation commands are not shortened in those records.

Targeted filter:

```text
FullyQualifiedName~I2BNativeCompatibilityReplayTests|FullyQualifiedName~ConversationViewModelTests.I2BS1_
```

Impacted filter:

```text
FullyQualifiedName~ConversationViewModelTests|FullyQualifiedName~GateI2AProjectReadTests|FullyQualifiedName~GateI2BNaturalLanguageEditTests|FullyQualifiedName~ToolProtocolTests|FullyQualifiedName~NativeToolSelectionTests|FullyQualifiedName~ArchitectureGovernanceTests|FullyQualifiedName~GateIChatFirstWorkspaceTests|FullyQualifiedName~GateIAgentWorkspaceTests|FullyQualifiedName~AIWorkspaceRenderingTests|FullyQualifiedName~NexusSyntaxHighlightingTests|FullyQualifiedName~AiCheckRunIsolationTests
```

| Current-candidate suite | Total | PASS | FAIL | Skip | Wall/exit |
|---|---:|---:|---:|---:|---|
| Targeted | 25 | 25 | 0 | 0 | 4.967 s /0 |
| Impacted | 318 | 314 | 4 | 0 | 389.395 s /1 |
| Unique test names across current-candidate targeted + impacted | **331** | **327** | **4** | **0** | 12 S1 overlaps deduplicated; not historical I2-B 331/331 claim |

Pass details include I2-A reader **11/11**, rendering **24/24**, syntax highlighting **5/5**, C1.1 JIT positive path, all new S1 cases and I2-B disposable existing-pipeline cases `success`, `build-failure`, `test-failure`, `cancel`, `conflict`, `reject`, `expire`. Disposable success proves 3 facts; failure/recovery tests preserve original failures and baseline/conflict behavior. No live model generates those deterministic changes.

### 5.2 Baseline-controlled comparison, without working-source restore

Four failures were reproduced once against a comparison build using exact pre-edit VM SHA `c6a6bca9…`. External `i2b_d1_s1_baseline.targets` substitutes only the saved VM Compile item for App and WPF temporary compilation; it does not replace the working file. Unique output `bin/i2b-d1-s1-baseline-20261016/net8.0-windows/`; comparison build exit0, log records `D1S1_BASELINE_SOURCE` for both compile stages. Assertions/test cases unchanged.

`baseline-four`: **4 executed / 4 FAIL**, exit1, 16.193 s. `test-reconciliation.json` asserts the exact map **test name → error message** equals impacted failures:

| Exact FQN | Current and pre-edit comparison failure |
|---|---|
| `Nexus.Integration.Tests.GateIChatFirstWorkspaceTests.CF1_ExplicitCodeContextEntersChatPayload_QuestionTextNeverApproves_AndEditExcludesHistory` | At line56, expected `<lampiran` in payload; actual `setujui`. |
| `Nexus.Integration.Tests.ConversationViewModelTests.ConversationView_TopBarMenuButton_HidesInAiCheckMode` | At line4400, `Sequence contains no matching element` on current XAML lookup. XAML unchanged by D1/S1. |
| `Nexus.Integration.Tests.ConversationViewModelTests.I2A_ExplicitGrantInjectsActualSource_ExcludesHistoryAndAttachments_RevokeBlocksNextRequest` | At line636, expected `izin baca`; current status string empty for the test's `Read again` request. |
| `Nexus.Integration.Tests.NativeToolSelectionTests.BoundCompilerRepair_FiltersOtherDescriptors_AndFinalNoneStaysNone` | At line85, NullReferenceException accessing compiler-failure observation; same on pre-edit VM. |

Conclusion: four failures **not introduced by D1/S1 in the controlled environment**. Their exact older introduction/environmental cause is not inferred merely from the comparison. They remain unresolved test/contract/observation debt, not waived mandatory acceptance and not PASS. No unrelated XAML/CF1/compiler code or original assertion was changed to hide them. Fresh compiler-repair proof is not claimed from that failed case; historical Gate H proof remains separate.

## 6. Exact source delta and preservation

| Repo-relative path | Before SHA-256 | After SHA-256 |
|---|---|---|
| `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs` | `c6a6bca946f29c416aed02f258e5a1204c2fc780a8f39d9aeb33ab8ed0e4e3bc` | `4e9706edf7132219fd578b07c0ff0d260b2842021b16d655dba54d016abf42e7` |
| `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs` | `8ddf85c7a485c6e6fbf5a95937453b1ced0735924eba84f8257a2fff28460a52` | `ea026848288a687cd01d9564e79be9d0741d6b9cdd426c38afcf5fcd5cb1db01` |
| `src/03_App/Nexus.Integration.Tests/I2BNativeCompatibilityReplayTests.cs` | NEW | `1c09b869b69b22044f9d33e0090b4dc3ad85af07fa3f078b5366cd62ae8b3bd7` |

VM net +24 lines, changes confined to pending-consent capture/clear/confirm/cancel and grant/dispatch fences. Existing test file change is **additions only**; the evidence collector asserts no pre-existing line removed. Exact diffs: `E\ConversationViewModel.cs.diff`, `E\ConversationViewModelTests.cs.diff`.

`E\final-preservation.json` and `E\pilot-and-historical-preservation.json` establish:

- **2.581/2.583 baseline source/document identities unchanged**; exactly the two approved existing paths changed, plus one new D1 test. All preserved UI C1.1/XAML/tokens, syntax highlighting, protocol/parser, runtime/Agent/caps, I2-A services, approval/SafeWrite/verifier/Job code remain baseline-identical.
- **139/139 default-output identities unchanged**; builds only publish into unique isolated output. Generated obj inputs may change during builds; this is not a claim obj files are byte-identical.
- Staged patch remains **`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`**. Original user staged/unstaged/untracked work remains; no reset/restore/stash/clean/commit/merge.
- **17/17 I2-B reviewed pilot source hashes** match pinned manifest after tests. Real pilot not mutated; deterministic execution mutations occur only in owned disposable copies.
- Hash-verified historical archives: Gate H certified checkpoint `2039be89ee34e82cfa38c20471ea68bf742fb09dd08ccdbb27ef79665737f822`; I2-B owner-recovery source `00544ac48a6b122ff9a736cb8735126f0ee9f2c75e7748991bc4145041c0c88b`; I2-B owner-recovery evidence `4aae61a2b750f1c77acb13eb8a91a93b6e7bfc2cd9996dc085073905d80b4481` — all match historical receipts.

Immutable original **and** copied fixture identities match:

| File under H and E/fixtures | SHA-256 |
|---|---|
| `native-1-request.json` | `097ddec7c2d22451f124229ce2d4facde3dd3739522b78bf956a12efbc2887b9` |
| `native-1-response.json` | `7527db87acc4f455cb23e31980166ad06f64b2ba607dd3bf5a4ecbbf5648a201` |
| `native-2-request.json` | `b12184b205679fcdc2308e464167ca2e653d66d2b27ef07b7b19db076c70e737` |
| `native-2-response.json` | `6369fa77733497a2656beb5027bcd26fc71f7f7f295d64a335012bddf9315ca8` |

No unintended **source** mutation established by scoped inventories, fixture hashes and pilot checks. Expected disposable verified edits/recovery and new isolated build outputs are not represented as zero filesystem writes.

Enhanced OSS: **USE** existing Nexus adapter/validator/executor/test seams and approved Job launcher, xUnit/Moq/WPF/stdlib already available. **ADAPT** the already approved separation of provider/envelope/proposal/result facts; no new OSS audit, runtime/framework transplant or dependency. Current report plus external tooling/fixtures/receipts are separate from production ownership.

Closure archives under Temp/opencode: `nexus-i2b-d1-s1-source-20261016.zip`, `nexus-i2b-d1-s1-evidence-20261016.zip`. `E\final-seal.json` records report/archive/source-manifest/TRX-reconciliation SHA-256 outside archives to avoid circular seals. `current-binary-identities.json` identifies tested isolated binaries, not a new Gate H certification.

## 7. Native blocker remaining and one requested I2-B-D2 decision

D1 adds new deterministic evidence: the Nexus adapter constructs fields matching both sealed requests; strict parser rejects their actual malformed/incomplete native result; a good envelope is accepted through a fake tool; proposal/semantic validation remain distinct. **S1's proven root cause is pending-consent lifecycle, not the cause of native OutputLimit.**

Still established: two historical responses have `length`, 256 completion tokens, call-like content and no native tool_calls. Still unknown: whether the deployed Gemma chat-template/tool decoder pair can recognize a correctly terminated, schema-valid call in its intended dialect under the original required-tool configuration. No production parser defect or causal cap fix has been demonstrated.

**One next discriminator:** replay a fully terminated known-valid dialect control and the existing recorded outputs through the **exact deployed b11057 template/tool-decoder pair**, with model/tokenizer/template/build identities pinned. If the valid control is recognized but captures are not, generation/termination remains the branch to investigate; if even the valid control is rejected, template/decoder compatibility must be resolved before touching the Nexus parser. Raw captures remain unchanged; a separately labelled synthetic control is not substituted for historical failure. If exact matching decoder/template evidence is unavailable, report that missing identity/API rather than use a newer unrelated runtime and claim equivalence.

**One owner decision requested:** approve **I2-B-D2 decoder-only offline compatibility probe**, with zero inference/live Agent runs, no production Nexus edit, no runtime/template/cap/parser/policy changes, and no dependency installation. This authorizes only the missing discriminator above. Any causal production remediation or new live allocation must have its exact scope approved afterward; it is not bundled implicitly into this decision.

Historical live allocation remains **2/4 generations, 2/2 Agent runs consumed**; this session adds **0/0**. D1/S1 test PASS is not native functional PASS. Four current baseline-reproduced impacted failures remain visible and require closure before future certification. **STOP at this report; I2-B native HOLD, Gate J HOLD.**
