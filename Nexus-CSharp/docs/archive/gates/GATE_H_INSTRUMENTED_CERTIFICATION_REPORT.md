# Gate H Instrumented Certification Report

**CERTIFICATION FAILED — GATE H BLOCKED**

The exact approved candidate was tested once. The run was stopped on its first observed failure: `GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat` returned `ToolProtocolException: OutputLimit` in chat mode. No rerun or source/test/acceptance modification. Gate I remains HOLD.

## 1. Source freeze approval evidence

User explicitly approved candidate `gate-h-instrumented-20261010-100928`, SHA256 `2bb26a498de63e3cdae6f8f9d48f35cf3d5ea79e42ccca2ee03953423c36301f`, and exactly one instrumented full-solution run. Approval and constraints are recorded in `approval.json`; `preflight.json` records PASS before execution.

Run start: **2026-10-10 03:32:55.239814 UTC / 10:32:55 local**. First observed failure: **03:54:57.851282 UTC**, elapsed **1322.610939 s**. Supervisor completion/cleanup: approximately **1335.001116 s** (22 min 15 s). `run-started.json` is an exclusive one-run marker; authorization is consumed. No additional test invocation follows failure.

## 2. Candidate SHA256

Approved immutable source archive:

`C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-h-instrumented-frozen-candidate-20261010-100928.zip`

SHA256: `2bb26a498de63e3cdae6f8f9d48f35cf3d5ea79e42ccca2ee03953423c36301f`.

Source manifest: `871bd03cda5ab44b312e6bed91a4cd5b966f9b305782bcbe8ee881cbfc55c4fa`.

Active-input/project manifest: `5589b1eb55358bc6d27b09f5211b884a0838f19a8f6244eb0a5ea1e317e0756c`.

The candidate is not relabeled certified. No `nexus-gate-h-certified-candidate-*` archive is created.

## 3. Project/source/test inventory

Preflight reverified **636 source/document files, 428 active repository inputs, 11 external inputs, 22 solution projects, 11 test assemblies, 78 primary binary identity files**, exact runtime/model/fixture hashes and staged patch. Freeze also records 386 matching PDB source-document checks and managed MVIDs. Separate generated identity snapshot remains separate from active source.

Command: pinned SDK `dotnet test src/Nexus.sln --no-build --no-restore -m:1`, existing supported DotNetHostPath runsettings, unchanged 6-minute VSTest hang detection, TRX logger and no filter. All configured Gate A–H flags were enabled. Normal solution order was retained. Integration interruption prevents claiming complete discovery/execution of every mandatory fixture.

## 4. Concurrent-agent reconciliation and user work

All source differences were reconciled before approval. During execution, **737 read-sharing file handles** denied write/delete to frozen existing source/input/binary/fixture/index files without changing ACLs or contents. Periodic source hashes and inventory were compared, and full post-run validation passed.

Staged SHA256 remains `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`. No source/test/XAML/configuration mutation, reset, restore, stash, clean, commit, rollback or recovery-checkpoint overwrite. UI/UX and user source bytes remain preserved.

An external process-cleanup attribution error occurred after failure; see §12. Source preservation does not erase that separate process-handling mistake.

## 5. Enhanced Open Source architecture preservation

Native WPF/C#/.NET 8, SDK 8.0.424/runtime 8.0.30, pinned llama.cpp b11057/Gemma-4-E4B, native tools, existing ONNX model/configuration and Nexus-owned authority remain unchanged. No dependency/framework/parser/runtime, keep-alive adjustment, new retry, source logger, arena/threading/quality change or speculative fix.

Existing .NET EventListener diagnostics and Windows process/system memory APIs were reused externally. Existing WPF/ORT/Roslyn/Aider/MAF/OpenHands responsibility boundaries are preserved. Agent deadline remains **120 seconds**; protocol output cap remains **256 tokens** where required.

## 6. OCR and WPF regression

The three original WPF governance failures each **PASS in this run**:

- Existing_design_system_debt_is_visible_and_finite.
- Module_views_do_not_consume_primitive_tokens_or_define_common_templates.
- Primitive_token_references_are_limited_to_design_system_layers_or_the_legacy_baseline.

OCR selected/all copy regression was **not observed executed before stop**. Its prior isolation/regression evidence remains historical and is not substituted for fresh full-suite execution. Production native clipboard and isolated OCR test hashes match the approved source.

AI Workspace rendering tests observed before stop passed; this does not certify the interrupted whole assembly or every remaining visual/copy case. Existing enhancements remain byte-identical to the approved candidate.

## 7. Gate B diagnostics

Original Gate B native tool/SSE/follow-up case **PASS**, 20.365 s, run ID `658f14b4-03a7-4806-a458-b456983bdb77`, runtime PID 12636. Two nonstream round trips and streamed tool round trip completed; fixture cleanup=true.

Previous SSE `[DONE]` was received. Nonstream follow-up started at **03:53:10.8995576 UTC**, cancellation=false, no forced connection-close header. Trace records connection **112 closed**, new connection **113 established**, request headers sent on 113, response headers **200** at **03:53:12.6024144 UTC**, normal EOF/return and disposal scope. No `ResponseEnded` exception occurred in this executed case.

Evidence: `658f14b403a74806a458b456983bdb77/` contains identity, result, per-request timelines and HTTP/Sockets/server lifecycle trace. This is scenario PASS within the partial solution run; historical ResponseEnded root cause remains unconfirmed. The complete full-suite scenario did not PASS.

## 8. BiRefNet diagnostics

The BiRefNet threading benchmark was **not observed executed before stop**. No fresh benchmark PASS or leak/exhaustion/contention diagnosis can be claimed.

Low-impact sampler collected **1205 timestamped process/system samples**, nominal 1-second interval, no artificial memory pressure or heavy profiler. It correctly includes dotnet-hosted testhost.

- Minimum observed available system commit: **19,587,776,512 bytes** (~18.24 GiB).
- Integration host PID 6208 peak private bytes: **2,843,025,408** (~2.65 GiB).
- Integration host peak commit: **2,846,212,096**.
- Integration host peak sampled working set: **1,986,715,648**.

These are whole-run/host measurements, not BiRefNet inference measurements. Native session count/per-Run internals were not exposed by the frozen benchmark. No `bad allocation` was observed, but the relevant benchmark had not run. Historical ONNX root cause stays open. Raw process ownership labels require the caveat in §12; system metrics and the directly identified integration host measurements remain distinct from that attribution.

## 9. Test counts by assembly and exact first failure

### Completed authoritative TRX files

| Assembly | Executed | PASS | FAIL / skip / abort |
|---|---:|---:|---:|
| Nexus.Contracts.Tests | 7 | 7 | 0 / 0 / 0 |
| Nexus.Foundation.Tests | 35 | 35 | 0 / 0 / 0 |
| Nexus.Conversation.Tests | 16 | 16 | 0 / 0 / 0 |
| Nexus.AI.EditingLite.Tests | 150 | 150 | 0 / 0 / 0 |
| Nexus.FileUtilities.Tests | 19 | 19 | 0 / 0 / 0 |
| Nexus.AI.Llama.Tests | 177 | 177 | 0 / 0 / 0 |
| Nexus.Engine.ImageSubsystem.Tests | 123 | 123 | 0 / 0 / 0 |
| Nexus.PdfUtilities.Tests | 21 | 21 | 0 / 0 / 0 |
| Nexus.Core.HomeSettings.Tests | 24 | 24 | 0 / 0 / 0 |
| **Completed TRX subtotal** | **572** | **572** | **0 / 0 / 0** |

`Nexus.Integration.Tests` was interrupted on first failure and has **no completed TRX**. Console observations record **448 PASS / 1 FAIL / 0 skipped** results; these are provisional observations, not authoritative completed-assembly totals. `Nexus.Engine.ColorManagement.Tests` had not started before stop. Do not claim 11 completed assemblies or zero whole-run aborts.

Exact first failing test:

`Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat`

Exception: **`Nexus.App.Services.Agent.ToolProtocolException: OutputLimit`**, production adapter/protocol parse, caller `GateHP1NativeSelectionLiveTests.cs:58`.

The fixture's cold mode (35.1996326 s) and warm mode (11.0585072 s) each produced valid native arrays, 79 completion tokens. In **chat mode**, the third response has **finish_reason=length**, **completion_tokens=256**, prompt_tokens=1076, **no native tool_calls**. Existing parser correctly rejects OutputLimit. Inference timing: prompt 280.807 ms, prediction 37,434.13 ms. This is neither the historical HTTP ResponseEnded nor ONNX bad allocation, and not an observed 120-second deadline violation.

Captured `response-3.json` SHA256: `94da16b67f947cea14aa540793f29504cc80741c0e6b6145a8a4cd1c20bc5efc`.

Evidence: `first-failure.json`, full.log lines 2009–2033 and `array-37ed3ae8ebea42b2a187c1bf72215f8d/`. Cold/warm proof records remain; no successful chat record is invented. The read-only probe had no mutation dispatch/approval/source write. No acceptance/cap change or rerun.

## 10. Gate F/G/H genuine live evidence

| Required run | Fresh observed result | Integrated Agent duration |
|---|---|---:|
| F genuine assertion repair | Not observed executed before stop | Not available |
| G genuine compiler A→B repair | PASS | **93.246 s** |
| H genuine multi-hunk A→B repair | PASS | **105.8605069 s** |

H initial 2+1 plan also passed, integrated duration **57.2260226 s**. Unit-test wall durations (G ~3m40s, H ~4m) include separate prerequisite cold/warm/chat probes; they are not the integrated Agent deadline measurements. Required repairs observed stay below 120 s. H current headroom approximately **14.1395 s**.

G proof: `live-e63481fff6104191a46664314641f744/tool-proof.json`, run `fc23269d-86d4-4b9b-8118-fde724e9c2b7`.

H proof: `h-live-30a31995fca14a5ca9b503d5c72adec8/gate-h-proof.json`, run `60f52f80-0055-4c2c-9ed4-73af9b64cc61`.

Cold/warm/cache/timing data is retained in those proofs. Historical Gate F success is not substituted for missing fresh execution.

## 11. Repair authority integrity

G/H proof review verifies:

- First attempt recognized compiler failure, recovery outcome BaselineVerified.
- Second plan has fresh PlanId/digest, correct parent lineage and fresh approval.
- Two source plans / one repair / zero retries in observed genuine repair runs.
- Alpha/Beta ActualHash equals DesiredHash; final verification is SourcePlanBuildTestsAndHashesVerified.
- Required fixture tests passed=3.

H fresh repair approval ID `75474f98-3776-4e5a-9755-75f682f8f07a`; G `ef65e0ba-a5f1-46ab-8b18-8ac34df239ad`. Full receipts/hashes retained in compact-summary.json and original proof files.

No SourceEditPlan, resolver, host composition, approval, SafeWrite, recovery, ToolRegistry policy, repair count or deadline source change. P1 failed read-only native response did not become an executable plan or success claim.

## 12. Runtime/process cleanup and wrapper attribution error

First failure triggered CTRL_BREAK for the approved run process group at **03:54:58.561856 UTC**, approximately 0.71 s after observation. Existing failure fixture had already stopped its runtime in finally. Stop/remaining-output processing is part of the same invocation, not a rerun.

Post-run process inspection found **no llama-server, testhost or Nexus.App process**. Freeze handles were released after identity checks.

**Cleanup mistake:** the external wrapper retained past parent PIDs in its ancestry set. A reused parent PID caused **pre-existing Taskmgr.exe PID 19536** to be misclassified as run-owned. Task Manager was created at **02:52:29.740068 UTC**, before run start **03:32:55.239814 UTC**, and was terminated by cleanup at **03:55:10.241265 UTC**. This violated the intended foreign-process preservation constraint. It occurred after the native OutputLimit failure and did not cause that first failure. The assistant has disclosed the mistake; no further process mutation or automatic restoration was attempted.

Raw ownership labels in run-result.json must not be treated as authoritative for every PID. `process_cleanup_attribution_error` in certification-analysis.json preserves the correction. Source/config/staged hashes are independent and remain intact. The wrapper's process-ownership/cleanup logic requires separate review before reuse; no fix or new run is performed in this certification-only task.

## 13. Source hash verification and evidence preservation

Before, during and after run: source fingerprints and inventory match approved candidate. Final revalidation includes **all 636 source files, 428 active inputs, 11 external inputs, 78 primary binaries, fixture/model/native ORT identities and staged patch**. Approved candidate ZIP SHA256 remains exact. No coding-agent source/test/XAML/config input mutation observed; existing files were write/delete protected during execution.

The source freeze review and this final report are outputs outside the frozen input manifest, not executable candidate changes. Existing source/UI/clipboard/rendering work is preserved.

Evidence directory:

`C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-instrumented-20261010-100928-evidence\`

Contains approval, preflight, single-run marker/result, nine TRX files, full log, first failure, HTTP/Sockets trace, raw native fixture responses, memory.ndjson, original repair proofs/receipts, console observations, corrected attribution audit and analysis summaries.

A separately named **failed evidence archive** and digest manifest are recorded in `evidence-seal.json`. Historical archives and approved source snapshot are untouched. No certified candidate archive is produced because mandatory acceptance failed.

## 14. Remaining reliability limitations

- Newly observed native array probe fails under its chat-mode scenario with token-limited non-tool response. This is an observation, not proof of a particular concurrency/template/cache root cause.
- Gate B executed scenario passes, but historical ResponseEnded root cause remains unconfirmed.
- BiRefNet benchmark, OCR selected/all copy, Gate F genuine repair and ColorManagement assembly were not reached/verified in this stopped run. Historical passes cannot fill missing mandatory evidence.
- Interrupted Integration assembly lacks completed TRX and full discovery/execution totals.
- Process-cleanup wrapper ownership inference is unsafe under PID reuse; foreign Task Manager termination is separately documented.
- Beta/three-target automatic native repair, multiple-failed admission, full CommonMark/rendering and manual CorelDRAW limitations remain documented; no broader authority enabled.
- The full-suite scenario did not PASS; no claim that historical errors cannot recur.

## 15. Final recommendation

The one approved full-solution invocation has been consumed and stopped at first failure. Preserve this candidate and exact evidence for user review. Any future implementation or certification requires a separately reviewed action; no automatic rerun, speculative production/test fix or acceptance relaxation.

Gate H remains BLOCKED and is not certified. Gate I remains HOLD.

**CERTIFICATION FAILED — GATE H BLOCKED**

STOP.
