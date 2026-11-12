# Nexus Engineering Workflow V2 — Implementation Report

Tanggal: 12 Oktober 2026. Authorization: project-owner instruction **Engineering Workflow V2.0 & Gate H Acceptance Option B**.

## 1. Verdict

**WORKFLOW V2 PASS — REQUEST NEW GATE H SOURCE FREEZE**

Policy telah dipasang untuk seluruh coding agents. Option B diimplementasikan sebagai pemisahan transparan antara mandatory certification dan **tepat satu** P1 reliability diagnostic. Strict validators, original P1 assertions, genuine F/G/H functional acceptance, approval, SafeWrite/recovery dan deadline tetap dipertahankan.

Ini verdict implementasi workflow, bukan Gate H certification PASS. **Full certification runs=0; new model generations=0. Gate I HOLD.** Source/test/tooling berubah, sehingga diperlukan candidate hash dan explicit source-freeze approval baru sebelum complete certification.

## 2. Exact policy dan acceptance files

| Path | Status / peran |
|---|---|
| `AGENTS.md` | Baru; instruksi ringkas universal untuk Gemini/GPT/Claude/Codex/OpenCode dan agent lain |
| `docs/NEXUS_ENGINEERING_POLICY.md` | Baru; detailed Workflow V2, UI/UX/test governance, checkpoint, single-writer dan STOP rules |
| `docs/GATE_H_ACCEPTANCE_OPTION_B.md` | Baru; focused future acceptance, old/new contract, exact P1 method, inventory, commands dan reporting |
| `docs/gate-h-acceptance-manifest.json` | Baru; machine-readable approved selection contract, 11 test projects, 13 mandatory live fixtures, flags dan reporting rules |

Tidak ada acceptance document khusus yang sudah terpasang; laporan historical Gate H tetap dipertahankan verbatim. File policy/acceptance baru masuk changed candidate dan future source freeze.

## 3. Modified production/test/tooling files

**Production:** tidak ada production source edit. **Tests:** hanya
`src/03_App/Nexus.Integration.Tests/GateHP1NativeSelectionLiveTests.cs`.

Perubahan test: method-level xUnit trait `Category=GateHP1Reliability`, tracking failure mode/stage, clearing stale usage sebelum mode berikutnya, failure telemetry dengan exception/reason/usage, dan `failure` field pada existing array proof. Exception tetap **rethrow**; validator, assertion, schema, scenario, request cap, Chat helper dan deadline asli tidak diubah. Semua original assertion/scenario lines dibandingkan dengan before snapshot dan diperiksa oleh contract tests.

**Repository development tooling:** `tools/gate_h_acceptance.py` baru, Python stdlib-only, untuk inventory partition, exact suite commands, TRX identity checking, failure classification dan combined reporting. Module ini tidak memulai atau menghentikan proses dan bukan dependency produk Nexus.

**Existing external certification tooling yang diadaptasi:**

- `C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_approved_preflight.py`
- `C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_instrumented_full.py`

Preflight menerima **new freeze** melalui `NEXUS_GATE_H_FREEZE`, memverifikasi contract/manifest/inventory SHA, exact test projects dan live flags, serta matching owner approval. Suite dipilih melalui `NEXUS_GATE_H_SUITE=mandatory|diagnostic`. Masing-masing memiliki evidence directory dan exclusive consumed-run marker sendiri. Default historical mode dan consumed guard tetap ada; Option B preflight tidak menerbitkan approval-nya sendiri.

Wrapper hanya menerima reviewed filter, mempertahankan Job Object launch/cleanup, dan membiarkan satu diagnostic FAIL menyelesaikan TRX sebelum klasifikasi. Skip/abort tetap blocker. Mandatory/host/ownership/identity failures tetap first-failure dan Option B wrapper keluar nonzero. Failed diagnostic tetap memiliki actual failed xUnit/TRX result dan underlying test-run exit; safe rejection dapat dilaporkan sebagai RELIABILITY FAILURE, bukan PASS. Combined reporter juga memeriksa invocation failure/cleanup/identity, bukan hanya jumlah TRX PASS.

Unchanged verified `gate_h_certification_job.py`, `test_gate_h_wrapper_safety.py` dan `gate_h_wrapper_dummy.py` tetap digunakan. External verification launcher, metadata discovery probe, synthetic filter fixture, contract tests, checkpoint/seal scripts tersimpan dalam evidence task ini. Tidak ada framework pengujian atau dependency produk baru.

## 4. Exact P1 diagnostic separation

```text
Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat
```

**Old contract:** stochastic read-only P1 generation-success probe mandatory; safely rejected OutputLimit menghentikan mandatory certification.

**New owner-approved contract:** hanya probe ini menjadi reliability diagnostic. Semua test lainnya, termasuk read-only probes dan genuine repair F/G/H, tetap mandatory. Native success masih harus memenuhi original strict assertions. **OutputLimit tidak pernah native tool-call success.** Malformed/missing output tidak diterima validator.

Manifest filters:

```text
mandatory:  Category!=GateHP1Reliability
diagnostic: Category=GateHP1Reliability&FullyQualifiedName=Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat
```

Default semua untagged cases adalah mandatory. Partition menolak additional diagnostic tags, unknown/missing assemblies, duplicate identities, missing A–H fixtures, atau discovery error. TRX matcher menolak wrong-suite/unknown/duplicate/missing results.

Safe diagnostic classification memerlukan native-generation `ToolProtocolException` rejection, reason whitelist, nativeSuccess=false, zero write/dispatch/approval, dan actual failed TRX trace yang sesuai. Runtime/fixture/transport/host/assertion defect atau evidence tidak cukup tidak otomatis dikecualikan. Unauthorized mutation/approval/parser acceptance selalu mandatory blocker. Diagnostic success juga memerlukan cold/warm/Chat capability proof lengkap.

## 5. Mandatory discovery proof

Fresh VSTest metadata discovery pada seluruh 11 current solution test binaries:

| Assembly | Total | Mandatory | Diagnostic |
|---|---:|---:|---:|
| Nexus.Contracts.Tests | 7 | 7 | 0 |
| Nexus.Foundation.Tests | 35 | 35 | 0 |
| Nexus.AI.EditingLite.Tests | 150 | 150 | 0 |
| Nexus.AI.Llama.Tests | 177 | 177 | 0 |
| Nexus.Conversation.Tests | 16 | 16 | 0 |
| Nexus.Core.HomeSettings.Tests | 24 | 24 | 0 |
| Nexus.Engine.ColorManagement.Tests | 10 | 10 | 0 |
| Nexus.Engine.ImageSubsystem.Tests | 123 | 123 | 0 |
| Nexus.FileUtilities.Tests | 19 | 19 | 0 |
| Nexus.PdfUtilities.Tests | 21 | 21 | 0 |
| Nexus.Integration.Tests | 868 | 867 | 1 |
| **Total** | **1450** | **1449** | **1** |

Seluruh **13 required mandatory live A–H fixtures** ditemukan dan tetap mandatory, termasuk genuine F assertion repair, G CS0029 compiler repair dan H 2+1 repair. Mandatory WPF/OCR/ONNX/runtime assemblies tidak dikeluarkan. Discovery bukan execution atau generation.

Inventory menyimpan ID, FQN, display name, source dan traits, disertai assembly hashes. SHA-256 `test-inventory.json`: `85f70fe83951717794370495ae8168fcb7f2d8f5665cb0013f19f9a1220af8b8`. Current rebuilt Integration DLL SHA-256: `bc1099395800ad42c8f8d0708ac70de2f7ffa4c4953c18fe779a99c7fb4649ee`.

Pemeriksaan API discovery dengan filter menunjukkan VSTest tetap mengembalikan full discovery inventory; output tersebut **tidak** diklaim sebagai filtered selection proof. Actual execution-filter behavior dibuktikan melalui external synthetic xUnit fixture dengan same exact P1 FQN/trait dan satu untagged control:

- Mandatory filter: **1 executed / 1 PASS / 0 FAIL / 0 SKIP**, hanya untagged control.
- Diagnostic filter: **1 executed / 0 PASS / 1 intentional FAIL / 0 SKIP**, hanya synthetic diagnostic. Actual exit=1, exception dan failed TRX dipertahankan.

Synthetic fixture tidak memuat Nexus/model dan bukan execution P1 production diagnostic; tidak masuk 1450-case solution inventory. Bukti menunjukkan kegagalan diagnostic tidak diubah menjadi PASS dan mandatory selection tidak kehilangan untagged test.

## 6. Strict fail-closed verification

Fresh targeted run **27/27 PASS**:

- `ToolProtocolTests`: strict native shape/name/ID/duplicate/missing/multiple handling, policy denial, bounded dispatch, repeated-call rejection, no parser fallback.
- `NativeToolSelectionTests`: host-owned exact tool selection, missing/wrong/malformed/extra/duplicate/multiple responses, closed arguments, no unauthorized execution.
- Dua retained historical OutputLimit cases pada `HostAuthoritativeRepairTests.RetainedOutputLimit_RealApprovedA_RecoversAndTerminatesWithoutB`: real approved A dipulihkan dan dihentikan tanpa B, approval tambahan atau unauthorized mutation.

Reason OutputLimit tetap exception fail-closed. Host-authoritative repair, approval A/B independence, recovery, default deny dan 120-second hard ceiling tidak diedit.

## 7. Targeted / impacted verification

| Verifikasi | Result / evidence |
|---|---|
| Integration rebuild, existing references, no restore | PASS; hanya test assembly yang dikompilasi ulang |
| Current 11-assembly VSTest discovery | 1450 cases, zero discovery errors / aborted |
| Strict protocol/fail-closed | **27 PASS**, complete TRX |
| Targeted H: MultiHunkSourceToolTests + HostAuthoritativeRepairTests | **20 PASS**, complete TRX |
| Impacted source repair/plan/orchestration | **106 unique cases terbukti PASS**: 86 console PASS + 20 resumed PASS pada complete TRX |
| Verified anonymous Job safety | **22 PASS**, unchanged helper/test identity |
| Final acceptance-contract tests | **18 PASS**, termasuk malformed selection, missing fixtures/results, OutputLimit reporting, authority violation, host/cleanup blocker dan nonzero blocker exit |
| Synthetic execution-filter fixture | Mandatory 1 PASS; diagnostic 1 intentional FAIL, separately reported |
| `git diff --check` | PASS; existing CRLF advisory messages dipertahankan |

**Disclosure impacted run:** initial aggregate external launcher wall bound 300 detik habis setelah 86 PASS, tanpa assertion failure dan tanpa completed TRX. Job termination menghentikan invocation milik launcher. Ini bukan satu complete impacted run yang PASS. Resume pertama mengalami quoting mismatch pada theory display-name filter dan hanya menjalankan 1 Fact PASS; actual count dipastikan dari TRX. Filter dikoreksi langsung menjadi FQN plus exclusions untuk delapan already-passed theory cases; **19 remaining theories PASS**. Exact resumed TRX IDs cocok dengan remaining inventory, tidak ada unresolved case, dan passing cases tidak diulang. Original timeout/log serta selection mismatch tetap tersedia. Agent deadline tidak diubah dan tidak ada model retry/top-up.

Aggregate Nexus coverage: **151 unique mandatory cases terbukti PASS**, 0 assertion FAIL, 0 SKIP; 27+20+106 menghasilkan 153 PASS entries dengan dua retained OutputLimit checks overlap pada ordered strict/H verification. Dari 151 unique, 65 memiliki complete-TRX evidence dan 86 memiliki console PASS evidence dari interrupted run. **1298 mandatory cases tidak dieksekusi dalam implementation scope**; bukan missing-test exemption untuk certification.

Actual P1 live diagnostic execution pada task ini: **0 executed / 0 PASS / 0 FAIL / 0 SKIP**. Independent execution tetap tersedia setelah appropriate future approval. Tidak ada full mandatory suite atau long live generation rerun.

Exact commands tersimpan di `*-invocation.json`; targeted filters ada di `nexus_workflow_v2_launch.py`. Seluruh build/discovery/test roots melalui verified `CertificationJob` (suspended root assigned sebelum resume). Semua recorded successful invocation cleanup memiliki empty Job membership sesudah cleanup. Interrupted impacted root juga dihentikan melalui Job-nya; tidak ada cleanup berdasarkan foreign process name/PID/ancestry.

## 8. Open-source reuse decisions

| Component / evidence | Decision | Audit rationale |
|---|---|---|
| Existing Nexus P1 / source-repair validators and tests | **ENHANCE** diagnostic telemetry only | Original contracts/assertions tetap; production security/authority tidak berubah |
| xUnit 2.5.3 dan existing VSTest adapter | **USE** | Existing dependencies; xUnit/adapter Apache-2.0, maintained upstream, native .NET 8 compatible; standard traits dan failure semantics, tanpa framework/package upgrade |
| Microsoft VSTest 17.11.1 TranslationLayer / SDK | **USE** external metadata API | MIT, mature Microsoft test platform; SDK-installed assemblies, discovery metadata/IDs; tidak masuk product runtime |
| Existing Nexus certification wrapper / verified anonymous Job helper | **ADAPT / USE** | Reviewed kernel ownership and 22 safety cases; selection/reporting diadaptasi, ownership helper byte-identical |
| New testing framework, model/runtime/parser, generic agent framework | **NOT NEEDED** | Existing solution menyelesaikan scope; tidak ada dependency/authority tambahan |

Referensi: [Microsoft selective unit tests](https://learn.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests), [xUnit VSTest adapter](https://github.com/xunit/visualstudio.xunit), [pinned VSTest v17.11.1 API](https://github.com/microsoft/vstest/blob/v17.11.1/src/Microsoft.TestPlatform.VsTestConsole.TranslationLayer/VsTestConsoleWrapper.cs). Security assessment dibatasi changed surface: exact selection cannot confer mutation authority; existing strict validation and Job boundary dipertahankan. Tidak ada klaim audit CVE menyeluruh atau runtime dependency baru.

## 9. User-work preservation dan diff review

Baseline dibandingkan dengan previous sealed recovery: **641 source/document entries**, zero unknown concurrent difference saat mulai. Before Git status, staged/unstaged patches, raw index, affected test/wrapper bytes dan hashes tersimpan. Single writer untuk affected files; tidak ada subagent/concurrent writer.

Sesudah perubahan: **640 baseline entries tetap identik**; satu baseline source yang berubah adalah P1 test yang diotorisasi. Lima policy/acceptance/repository-tooling files ditambah, lalu laporan ini. Historical reports, production, other tests, UI/UX, config dan previously approved user work tidak diedit. Before/after surgical diffs tersimpan untuk P1 dan dua existing external scripts.

Staged diff tetap byte-identical SHA-256 `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`. Tidak ada reset, restore, clean, stash, commit, merge, destructive rollback atau deletion.

Sebelum rebuild, 19 relevant Integration generated files disimpan di `before-integration-generated.zip`, SHA-256 `21ef5fb8d41618a5ed6cad1376361224cc4c95b78aaf1e3e8e983c9edcd3c5f8`. Generated test binaries/cache berubah sebagai hasil build dan tidak diperlakukan sebagai source rollback. Python bytecode cache adalah generated output, dikecualikan dari future source inventory; Option B imports tidak menulis cache baru.

## 10. Source / checkpoint hashes

Implementation source checkpoint sebelum memasukkan laporan: **646 entries** (641 baseline + 5 new files), `implementation-source-checkpoint.json` SHA-256:

```text
b5bca7069e9476555b51473d79911b4e27e31180cd61237c64ac1830f8308212
```

| Changed input | SHA-256 |
|---|---|
| AGENTS.md | `a09c492c9abcd6bd67f526f3e659de2522d8d71065820c047f086570a79280e2` |
| Engineering policy | `b53524d0c078dda0e29df39c55da4e15b57dfee08e82d662267b2d6e5869d1cb` |
| Acceptance document | `c47ca9be75ca3049f4a83ad271ab444565a4db4e620426a1d63a3c97061af2c6` |
| Acceptance manifest | `f1b12182112bdc0aa1a04fde5968958b1d29887e59c9b2f2e4e6d467555d5d8c` |
| Repository acceptance checker | `f1c4ec8d43df9e17b7da604bfa9c796e87843d1a8b1229c8f08309c40096d998` |
| P1 test before | `4b2f4e54607945d5520a4ac98776da2277ce612eec3e129525bd3a62d5698723` |
| P1 test after | `f8928d2a21e77c8ad40a0793629f76ebdaaa212a2d772bf04dfbe47a599ffbd9` |
| External preflight after | `bab6ab373aad20a44b7da9311914293a5054bd989404809e0443e27f9001baad` |
| External certification wrapper after | `38adf21091b82307f9cc3626ac278c010c957f2246779633765637d2e47f3387` |
| Unchanged verified Job helper | `bf7c149ee5fa3b419755b20cbf0a23ec14b6bc710158cee9c012125e91088357` |

Final seal menambahkan laporan ini menjadi **647 checkpoint source/document entries**, menyimpan current external tooling, verification evidence dan before snapshots. Source/evidence ZIP SHA serta report hash dicatat dalam `implementation-seal.json`, dibuat setelah laporan untuk menghindari circular report/archive hash. Checkpoint bukan approved freeze atau certified archive.

Evidence root: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-workflow-v2\`. Utama: `baseline.json`, `test-inventory.json`, `discovery-proof.json`, `verification-summary.json`, TRX/log/invocation records, surgical diffs, external tool source, source checkpoint dan final seal.

## 11. Remaining limitations dan recommendation

- Full mandatory certification dan actual P1 live diagnostic belum dieksekusi pada changed candidate. New freeze harus mengevaluasi current source/active-input graph, binaries/PDB/resources, all mandatory fixtures, SDK/runtime/model identities, acceptance manifest dan exact discovery inventory; hash checkpoint ini tidak menggantikan langkah tersebut.
- Impacted coverage mempunyai mixed console/TRX evidence karena interrupted aggregate run; tidak diklaim sebagai complete certification TRX. Future complete certification tetap memerlukan zero abort/missing/fail/skip pada mandatory suite.
- Historical ResponseEnded dan ONNX bad allocation tetap unresolved; **tidak** mendapat exemption Option B. WPF/OCR/ONNX/runtime regression tetap mandatory. Existing evidence historical dipertahankan; tidak diklaim fresh execution di task ini.
- Historical P1 OutputLimit causal trigger belum diperbaiki. Previous bounded recovery 8 valid native + 4 actual Chat/client-overlap evidence tetap historical, source/test identity dan scheduling limitations berlaku; bukan execution binary baru atau full certification PASS.
- Native Beta/three-target automatic repair, CorelDRAW manual paste dan existing renderer limitations tetap mengikuti historical reports; tidak ada production capability expansion.

**Recommendation: REQUEST NEW GATE H SOURCE FREEZE** untuk candidate Workflow V2/Option B beserta updated external tooling. Setelah explicit approval atas candidate/hash baru, jalankan mandatory dan diagnostic suites secara terpisah melalui verified wrapper dan laporkan keduanya tanpa menyembunyikan failures. **STOP sekarang; tidak menjalankan full certification atau memulai Gate I.**
