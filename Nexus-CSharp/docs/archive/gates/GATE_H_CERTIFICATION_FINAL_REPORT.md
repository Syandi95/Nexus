# Gate H — final certification report

Tanggal penutupan: 13 Oktober 2026. **CERTIFIED WITH APPROVED OPTION B SCOPE**
untuk candidate `gate-h-clipboard-20261012-212144`. **Gate I HOLD**.

## 1. Scope dan verdict

Owner menyetujui candidate/source-freeze secara eksplisit, tepat satu invocation
diagnostic kemudian satu invocation mandatory. Acceptance:
`NEXUS-GATE-H-OPTION-B-V2`. Semua kasus selain tepat satu P1 probabilistic probe
tetap mandatory; genuine F/G/H repair tetap wajib PASS.

| Suite | Discovered | Executed | PASS | FAIL | SKIP | Missing/abort | Exit | Wall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Mandatory | 1450 | 1450 | 1450 | 0 | 0 | 0 | 0 | 2716.9953134 s |
| P1 diagnostic | 1 | 1 | 0 | 1 | 0 | 0 | 1 | 103.9296232 s |

Inventory frozen berisi **1451 kasus**. Mandatory menghasilkan **11 TRX assembly
lengkap**, exact result IDs cocok dengan inventory, **13/13 required live fixtures
PASS**, tanpa duplicate/unknown/wrong-suite/missing result. Diagnostic dinilai
**RELIABILITY FAILURE — OutputLimit**, bukan PASS dan bukan mandatory blocker
berdasarkan read-only rejection yang dibuktikan. Tidak ada rerun/top-up.

## 2. Mandatory dan genuine repair

| Assembly | Mandatory PASS |
|---|---:|
| Nexus.Contracts.Tests | 7 |
| Nexus.Foundation.Tests | 35 |
| Nexus.Conversation.Tests | 16 |
| Nexus.AI.EditingLite.Tests | 150 |
| Nexus.FileUtilities.Tests | 19 |
| Nexus.AI.Llama.Tests | 177 |
| Nexus.Engine.ImageSubsystem.Tests | 123 |
| Nexus.PdfUtilities.Tests | 21 |
| Nexus.Core.HomeSettings.Tests | 24 |
| Nexus.Integration.Tests | 868 |
| Nexus.Engine.ColorManagement.Tests | 10 |
| **Total** | **1450** |

Seluruh 13 fixture A, B tool/shared-runtime, C, D, E, E1, F, G probe/repair,
dan H initial/repair berstatus Passed pada TRX aktual. Nama dan IDs lengkap tersedia
di `certification-reconciliation.json`. Count Integration meningkat satu dari
inventory implementasi lama karena regression clipboard baru; freeze terbaru
adalah authority count yang dieksekusi.

- H initial 2+1: **63.2278310 s**, verification
  `SourcePlanBuildTestsAndHashesVerified`, sourcePlanCount=1, repairPlanCount=0,
  retryCount=0.
- H bound compiler repair 2+1: **111.9068294 s**, verification yang sama,
  sourcePlanCount=2, repairPlanCount=1, retryCount=0; fresh approval B tetap wajib.
- F assertion repair dan G genuine compiler repair PASS; receipt mencatat satu
  bounded repair dan verification build/tests/hashes. Approval/recovery facts
  lengkap dipertahankan di raw `tool-proof.json` dan `gate-h-proof.json`.
- 47 `[AgentProof]` records direkonsiliasi; semua completed Agent runs di bawah
  deadline **120 s**. Wall fixture dapat mencakup warmup/probe terpisah.
- Clipboard native payloads dan regression competing NULL-window opener, OCR,
  ONNX, WPF, Chat/shared-runtime dan deterministic authority regressions PASS.

## 3. P1 diagnostic — kegagalan tetap terlihat

Exact test:
`Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat`.

| Mode | Native generation | Prompt | Completion | Cached prompt |
|---|---|---:|---:|---:|
| cold | valid | 1076 | 79 | 0 |
| warm | valid | 1076 | 79 | 1075 |
| chat condition | OutputLimit, rejected | 1076 | 256 | 1074 |

Native successes=2, OutputLimit=1, invalid_tool_response=0. `chat` adalah kondisi
probe native saat Chat aktif; usage 256 di atas adalah native probe, bukan Chat
overlap cap. Native cap tetap 256; Chat cap tetap 64. Telemetry failure:
stage=`NativeGeneration`, `nativeSuccess=false`, toolDispatches=0,
approvalRequests=0, sourceWrites=0. Tidak ada output terpotong yang diterima sebagai
valid tool-call. Actual failed TRX dan raw requests/responses/usage tetap diarsipkan.

Actual exception/trace (excerpt; full trace dalam failed TRX):

```text
Nexus.App.Services.Agent.ToolProtocolException : OutputLimit
NativeToolProtocol.Parse(String raw) — NativeToolProtocol.cs:38
NativeToolModelAdapter.GenerateAsync(...) — NativeToolModelAdapter.cs:78,81
GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat()
  — GateHP1NativeSelectionLiveTests.cs:60,93
```

Failed result ID: `72e2d4a3-a2bd-d94f-7d6a-5dbdca604de5`.
Raw TRX: `diagnostic/full/Syand_SYANDI-PC_2026-10-13_08_48_22.trx`.
Safe native protocol rejection ini memenuhi exception sempit Option B; tidak
membuktikan probabilistic reliability selalu berhasil.

## 4. Certified identity dan preservation

| Identity | SHA-256 |
|---|---|
| Approved source candidate ZIP | `4b56ccf8d009f2f3542c48b49feabf5e7faef5e33fa375eb7f16383707b04184` |
| Source manifest | `55b6f16d2eb9b441066306fd9af94f15bb1ff5c4754ae053a3129457144732ca` |
| Frozen input manifest | `84f8c5373233642dcfdc9de21edee8ae6e245db4c20219fdc76a6423a24bfe80` |
| Test inventory | `000f127f91202959da0137053c660989178352b82287d9d90c9833d7092b7164` |
| Acceptance manifest | `f1b12182112bdc0aa1a04fde5968958b1d29887e59c9b2f2e4e6d467555d5d8c` |
| Generated identity ZIP | `971c52aa91d0e319f4f89feb9a70dc2a99a4b2cd42f450cd5f640ca21a2926d1` |
| Staged patch | `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046` |

Reconciliation memverifikasi **651 source/document files**, **526 binary identities**,
21 external inputs, 40 runtime/model/fixture identities dan exact staged patch
tetap cocok dengan approved freeze. Provenance freeze: 433 active repository inputs,
22 projects, 386 source/PDB checks, 69 copied Nexus MVID matches, zero mismatches.
Pinned SDK 8.0.424, Windows x64; runtime/model dan execution configuration unchanged.

Verified anonymous Windows Job ownership dipakai pada kedua invocation. Keduanya:
first_failure=null pada wrapper safety record, reader_completed=true,
cleanup_actions=[], remaining_owned_processes=[]. Diagnostic test FAIL tetap
terpisah dari wrapper safety status. Tidak ada foreign-process cleanup.
Agent120/native256/Chat64, wrapper3600 s dan VSTest hang6m dipertahankan.

Perbaikan clipboard adalah test-reader message-only HWND isolation yang sudah
di-review dan diverifikasi RED→GREEN; production `NexusClipboard.cs` tetap sama.
Original assertions/retry limits dan approved UI/UX, OCR isolation, native Windows/
WPF serta host-authoritative repair/default-deny boundaries dipertahankan.
Tidak ada commit atau perubahan staged user work pada penutupan ini.

## 5. Execution dan sealed deliverables

Invocation aktual memakai `NEXUS_GATE_H_FREEZE` menunjuk
`C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-freeze`, suite
`diagnostic` lalu `mandatory`, `gate_h_approved_preflight.py` lalu
`gate_h_instrumented_full.py`. Kedua `run-started.json` sudah consumed.
Command/flags, approval, preflight, full logs, TRX dan receipts ada di evidence.

Base path artefak: `C:\Users\Syand\AppData\Local\Temp\opencode\`.

- Certified checkpoint: `nexus-gate-h-certified-checkpoint-20261013.zip`;
  menyimpan approved source ZIP dan generated identity ZIP secara byte-identical,
  candidate/approval/reconciliation, serta laporan post-execution.
- Evidence archive: `nexus-gate-h-certified-evidence-20261013.zip`;
  menyimpan raw kedua invocation, failed diagnostic, freeze metadata, tooling dan
  laporan, dengan per-entry hash manifest.
- Final seal/hash receipt:
  `gate-h-clipboard-20261012-212144-evidence/certification-seal.json`.

Hash arsip akhir dan hash laporan dicatat dalam seal di luar arsip untuk menghindari
circular hash. Laporan ini **post-execution artifact**, di luar approved 651-file
source manifest. Certified source adalah approved candidate asli; checkpoint
tidak mengklaim laporan baru ikut dibangun/diuji. Metadata candidate yang menyatakan
awaiting approval/zero runs adalah snapshot freeze historis; actual approval dan
consumed invocation records menentukan execution status.

Enhanced Open Source: **USE** verified Nexus acceptance/reconciliation/Job tooling
dan Python stdlib ZIP/SHA-256 untuk seal; dependency/framework baru **NOT NEEDED**.

## 6. Batasan dan keputusan lanjutan

- P1 stochastic OutputLimit tetap known reliability limitation; tidak ada causal
  production fix atau unconditional all-tests-PASS claim.
- Aktor historis clipboard interference belum teridentifikasi; defect NULL-window
  test-reader telah dibuktikan dan diperbaiki. Historical ResponseEnded/ONNX
  allocation mechanisms tidak dinyatakan solved secara kausal; current mandatory
  regressions PASS.
- Manual paste CorelDRAW tetap belum diverifikasi.
- Certification berlaku pada candidate, mesin/runtime dan approved Option B scope
  ini. Approval execution sudah consumed.
- **Gate H certified sesuai approval bersyarat PASS owner; Gate I HOLD** hingga
  scope/progression disetujui secara eksplisit. Penutupan ini selesai dengan laporan
  dan sealed evidence.
