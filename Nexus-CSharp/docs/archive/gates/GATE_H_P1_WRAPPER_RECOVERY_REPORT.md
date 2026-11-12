# Gate H P1 Reliability and Wrapper Safety Recovery

**RECOVERY PARTIAL — P1 OR WRAPPER STILL UNRESOLVED**

Wrapper ownership defect is corrected and targeted safety/integration tests pass. P1 request/decoder contract is correct, and three predeclared original cold/warm/Chat repetitions pass, but the historical Chat-mode OutputLimit trigger is not causally resolved. No production/test/acceptance change or full certification run in this task. Gate H BLOCKED; Gate I HOLD.

## 1. Historical candidate identity

Historical source freeze `gate-h-instrumented-20261010-100928`, ZIP SHA256 `2bb26a498de63e3cdae6f8f9d48f35cf3d5ea79e42ccca2ee03953423c36301f`, verified as immutable reference only. Its consumed one-run approval and failed evidence remain unchanged. No attempt to reactivate that approval.

Read the instrumented certification report, source freeze review, original P1 test, NativeToolModelAdapter/NativeToolProtocol, runtime/isolation tests, existing wrapper and raw failure/cleanup evidence before editing.

## 2. Current source reconciliation

All **636 historical source/document hashes match** before and after targeted work. Existing source-freeze review and instrumented certification report are known additional output documents. No affected concurrent source differences found. Git status and staged/unstaged/untracked work recorded; no reset, restore, clean, stash, deletion or commit.

Staged digest remains `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.

External wrapper before SHA256 `0347050e154ab4292224e130bf4a556a0f1f739fcf4f93cbd522d91c090d6818`; exact before copy retained. External preflight source remains unchanged, before SHA256 `172cb0f6f6fc2dd419a20c9d34108d13e5ab12eef4b1140c037d26638ae041ee`.

## 3. P1 failure reconstruction

Original failed test: `GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat`, adapter GenerateAsync call at line 58.

All three historical effective native requests are canonically identical; SHA256 `e2e1d037480c7615daeb83eb01526d247a394cdc915649c10a084e25b27e5c28`. Each uses:

- Pinned Gemma-4-E4B / llama.cpp b11057 (`b11057-59657a613` fingerprint).
- `stream=false`, `temperature=0`, `max_tokens=256`.
- `tool_choice="required"`; only `nexus_apply_source_plan` exposed.
- Same source-plan nested-array schema, messages and `enable_thinking=false`.
- Context 3288 bytes; successful arguments 168 bytes.

| Historical mode | Finish | Completion / prompt tokens | Cached tokens | Native call |
|---|---|---|---:|---|
| Cold | tool_calls | 79 / 1076 | 0 | Valid a:2 + b:1 arrays |
| Warm | tool_calls | 79 / 1076 | 1075 | Valid a:2 + b:1 arrays |
| Chat | length | 256 / 1076 | 1074 | None |

Chat response contains 980 content characters, no reasoning_content field. Token allocation specifically to reasoning is not reported, so no invented reasoning-token distribution. Cache/prompt evaluation differs (warm prompt_n=1; failed Chat prompt_n=2); this is an observed difference, not proof of cause.

Fixture source starts StreamChat before awaiting the native GenerateAsync task in Chat mode, on the same runtime/lease, with an independently linked 120-second cancellation token. Cold/warm take 35.1996326/11.0585072 seconds; failed prediction timing 37.43413 seconds. The exception is OutputLimit, not cancellation/deadline/transport failure. Fixture finally stops its owned server; read-only probe has no tool dispatch/approval/write.

Sensitive prompt content is not reproduced in this report; captured requests are synthetic fixture data in retained evidence.

## 4. Verified cause and unresolved hypotheses

**Verified immediate failure:** the model exhausted the existing 256-token generation contract and returned no native call. NativeToolProtocol correctly throws OutputLimit. No valid native tool call is present for the decoder to discard.

**Trigger remains unresolved:** no Nexus request mutation, wrong tool visibility, changed generation options, parser loss or session transition defect is proved. Chat scheduling, cache state and model/template behavior remain hypotheses. Current cache difference alone is insufficient causal evidence.

NativeToolModelAdapter builds tools/payload locally for every request, DeepClone snapshots messages, uses the host-selected single descriptor, required string choice, fixed options and independent validation. Pinned b11057 common/chat.h declares AUTO/REQUIRED/NONE tool-choice values and Gemma4 parsing support; real cold/warm/native samples validate the intended request path. A required request is not host approval and cannot turn an absent call into authority.

Three later PASS repetitions do not prove a historical reliability repair. No further P1 generation repetition or speculative production fix is made. Workstream A stops for scope/reliability review under the existing contract.

## 5. Enhanced Open Source decisions

| Pattern | Decision | Use |
|---|---|---|
| Existing Nexus NativeToolModelAdapter/decoder/runtime | USE | Preserve request construction, strict OutputLimit rejection and isolated cancellation. |
| ggml-org/llama.cpp b11057 common/chat.h/native protocol | USE for audit | Confirm required enum/native format; real captured requests/responses retained. No runtime/parser replacement. |
| dotnet/runtime Process/HTTP lifetime patterns | USE/ADAPT | Stable directly created handles, original HttpClient contract; no retries or keep-alive change. |
| Windows Process API creation identity | ADAPT | PID + creation time + live handle/job membership for observation; no PID/name ancestry permission. |
| Anonymous Windows Job Objects | ADAPT | Invocation-only lifecycle boundary; direct suspended root assigned before resume, descendants inherit membership. |
| Existing wrapper/targeted test infrastructure | ENHANCE | Kernel-owned cleanup and deterministic plus harmless-process tests. |
| New general-purpose supervisor/dependency | NOT NEEDED | Small wrapper-specific helper uses built-in Python _winapi/ctypes and existing Windows APIs. |
| Token cap increase/model retry/timeout increase/parser weakening/model upgrade | REJECT | No causal justification; would disguise the genuine terminal failure. |

References: pinned https://github.com/ggml-org/llama.cpp/blob/b11057/common/chat.h; Microsoft https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects; https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject; https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes.

## 6. Changes and justifications

**Nexus production/test/XAML/configuration changes: NONE.** Existing P1 Chat mode and strict assertions remain. P6, source-plan initial/repair validators, approval/SafeWrite/recovery/ToolRegistry limits untouched.

External files under `C:\Users\Syand\AppData\Local\Temp\opencode\`:

- Modified `gate_h_instrumented_full.py`: remove historical PID ancestry admission, per-PID termination and executable-name cleanup selection. Launch/cleanup through one owned job; sample ownership via current handle + kernel membership + creation identity. Consumed-run guard retained.
- Added `gate_h_certification_job.py`: narrowly scoped certification job/process interface, native suspended launch, retained direct handle, anonymous job, no breakaway, KILL_ON_JOB_CLOSE and no CPU/memory/time/UI limits.
- Added `test_gate_h_wrapper_safety.py`: unit/native lifecycle safety tests.
- Added `gate_h_wrapper_dummy.py`: harmless test processes only.

Audit runners/evidence helpers are external and do not modify the candidate. This report is the sole new repository output in this task.

## 7. P1 deterministic results

**60/60 PASS, zero failure/skip/abort**: NativeToolSelectionTests, ToolProtocolTests, SharedRuntimeAgentTests, HostAuthoritativeRepairTests, MultiHunkSourceToolTests and MultiHunkSourceRepairTests.

Covers required/single-tool host selection; valid native parsing; invalid/truncated/OutputLimit rejection; retained OutputLimit with real approved A recovering and terminating without B; no extra approval or dispatch; source baseline hashes preserved; runtime cancellation/session separation; initial and repair-specific validators. These are existing tests and unchanged source, not acceptance edits.

## 8. Bounded original P1 live results

**Exactly three original repetitions were declared before execution**, no retry-until-pass loop. All cold/warm/Chat scenarios retained, pinned runtime/model and 256-token/120-second caps unchanged.

| Repetition artifact | Cold s | Warm s | Chat s | Result |
|---|---:|---:|---:|---|
| array-f71a364d351b4fc2b6245fbb36300c81 | 34.1511976 | 10.4962543 | 10.3304854 | PASS |
| array-c0b20dfd07d54403b0c1ec9e300c85a6 | 33.4690670 | 11.3406580 | 11.5714973 | PASS |
| array-9593a3d59b8042a8ad7a991e8ce06cd1 | 34.4432324 | 11.2507196 | 12.7050913 | PASS |

All nine native calls finish tool_calls, 79 completion tokens, strict a:2+b:1 arrays, same canonical request SHA as historical. Cold cached=0, current warm/Chat cached=1075. Zero probe writes/approvals/dispatches; runtime cleanup asserted. Historical failed Chat cached=1074 remains distinct evidence. No statistical or causal reliability guarantee inferred from 3/3 test passes.

## 9. Proven wrapper ownership defect

Old admission condition was `child_pid not in owned and parent_pid in owned`. Past parent PIDs never expired or validated as current parent process identities. Taskmgr.exe (PID 19536) was born before the run; its original parent PID later matched a run-associated reused PID. The wrapper stored Task Manager's own creation time after incorrect admission.

Cleanup's subsequent `creation(handle)==owned[pid]` only confirmed the incorrectly admitted child identity; it did not establish run ownership. Hence it still authorized foreign termination. Root cause is **untrusted historical ancestry admission**, not merely missing a final PID/creation check.

Before source and raw cleanup/first-failure evidence retained. No actual Task Manager/Corel/browser/coding-agent process was targeted during recovery testing.

## 10. Wrapper ownership fix

Strict contract:

1. Create a private unnamed, non-inherited job; wrapper holds the only management handle.
2. Create the root via native CreateProcess **suspended**, retain process/thread handles and creation identity.
3. Assign that directly created root to the job and verify membership **before ResumeThread**. Assignment/identity failure aborts launch; only the exact newly created suspended-root handle is cleaned, never an existing enumerated PID.
4. Descendants created normally by CreateProcess inherit kernel job membership, including after parent exit. No breakaway permission or resource/priority/deadline restriction added.
5. Per-process observation requires current handle membership and available creation identity. Foreign/missing/ambiguous identity is unproven, not cleanup eligible.
6. Forced cleanup uses TerminateJobObject on this directly owned invocation job, not process names or historical PID sets. Unknown external processes are untouched. Aggregate job ownership is kernel-established even if enumeration fails; no PID list becomes termination authority.
7. Closing the owned job cancels only associated processes; cleanup is idempotent and serialized. No arbitrary existing-process assignment API exposed.

Windows 8+ nested jobs are supported; real nested-job test passes on this Windows environment. The root job has no UI constraints and is empty before assignment. If assignment is incompatible, fail launch rather than fall back to unsafe ancestry. Child creation through WMI Win32_Process.Create does not inherit membership; such processes remain unproven/untouched and require review. Nexus's normal Process.Start/CreateProcess paths were exercised under the job in impacted tests.

## 11. Foreign-process safety verification

Final **22/22 safety tests PASS**, zero skips. Coverage:

- Direct root handle/identity and single launch.
- Verified child inheritance and cleanup.
- Pre-existing foreign dummy preserved.
- Foreign dummy born after root also preserved.
- Synthetic PID/creation reuse and historical Task Manager identity rejected.
- Fake enumeration PID substitution does not alter kernel membership/cleanup target.
- Parent exits before child; child remains kernel-owned and cleaned.
- Missing/inaccessible creation or membership metadata fails closed.
- Enumeration failure never creates PID-based cleanup authority.
- Process exits before cleanup.
- Cancellation during cleanup and repeated terminate/close.
- Nested job, pinned dotnet host and assignment-failure-before-resume.
- Wrapper regression check forbids old terminate_owned/ancestry/TerminateProcess selection.

Harmless dummies are test-created; foreign dummies are torn down only through the test's own retained Popen handle. No real desktop apps. Initial helper setup failed because a Python subprocess constant was not exported; no root process ran in those failed cases. That run's log is preserved. Corrected helper uses documented native flag 0x400; subsequent 19/19 and final 22/22 logs retained.

## 12. Impacted regression

**71/71 PASS, zero failure/skip/abort**, under the new actual native job launcher: GateBToolCapabilityTests, NativeToolSelectionTests, ToolProtocolTests, SharedRuntimeAgentTests, ConversationRuntimeWorkflowTests, HostAuthoritativeRepairTests, MultiHunkSourceToolTests and MultiHunkSourceRepairTests.

This includes existing real b11057 Gate B server startup/native round-trip/cleanup plus deterministic Chat/Agent/repair validation. It does not add a fourth P1 repetition. One residual process born in the invocation remained in the kernel job after the test root exited; job-only cleanup removed it, final job member list empty. No foreign name/PID lookup used for termination.

No full solution run; original consumed approval marker retained. Final process inspection shows no llama-server/testhost/Nexus.App. `git diff --check` passes with only pre-existing LF/CRLF normalization warnings.

## 13. Exact files and hashes

| External source | Final SHA256 |
|---|---|
| gate_h_instrumented_full.py | `55bf952f03458dd847af8b91d763191dfd448d0d11f818c8811f201d8babb584` |
| gate_h_certification_job.py | `bf7c149ee5fa3b419755b20cbf0a23ec14b6bc710158cee9c012125e91088357` |
| test_gate_h_wrapper_safety.py | `6eb1000e608915d40e390e7e786e38b409f475d0e0657a7a4bd977b21b17ed27` |
| gate_h_wrapper_dummy.py | `18478b31474c57be6c044e6dd0bc3bb58a74af5f58ec44af306f0cb550fa86a2` |

Evidence: `docs/gate-h-p1-wrapper-recovery-evidence/baseline.json`, before wrapper copy, `wrapper-minimal-fix.diff`, `recovery-review.json`, live raw requests/responses/proofs/TRX, final wrapper test log/results and actual job impacted invocation/TRX.

Separate **recovery source/tooling checkpoint** and manifest/digest are recorded in `recovery-checkpoint.json`, including this report and external tooling. It is not an approved source freeze, certification PASS, or permission to execute the repaired full wrapper. The historical archive/failed evidence remain unchanged. Future candidate identity must bind changed wrapper/tooling as well as source before a separately approved certification.

## 14. Staged and user work preservation

All 636 historical candidate entries match at final review; no Nexus production/test/XAML/config edits. Staged digest unchanged. P6 host facts/authority, OCR native compatibility/isolation, WPF role/visual corrections, AI Workspace native rendering, approved UI spacing/typography/icons and unrelated user work are preserved. No unknown overlapping affected-source modification found. No Git mutation or desktop-process cleanup performed.

## 15. Remaining risks

- P1 immediate OutputLimit cause is known, but its intermittent model/runtime trigger is not proved or fixed. Three isolated successes do not establish full-suite Chat reliability.
- No cap/turn/retry/time/model/template/parser relaxation authorized or performed. Scope review needed if reliable generation cannot be established within the existing contract.
- Anonymous job boundary is verified for this Windows environment and normal inherited CreateProcess paths, not WMI/external launched processes. Unproven processes remain untouched.
- New wrapper has passed lifecycle and actual targeted testhost/runtime integration, not a complete certification invocation. Nested job incompatibility must fail safely at assignment.
- Historical ResponseEnded, ONNX allocation, broader Beta/three-target repair and manual CorelDRAW/rendering limitations remain.
- Current full-wrapper historical run marker prevents reuse. No full-suite approval is active for a future candidate.

## 16. Recommendation

Accept the proven wrapper safety correction and retained targeted evidence. Keep P1 reliability OPEN for scope/root-cause review; no speculative Nexus implementation change. Do not declare combined recovery resolved or start full certification/Gate I. A future source freeze must receive fresh review and separately authorized full run.

**RECOVERY PARTIAL — P1 OR WRAPPER STILL UNRESOLVED**

STOP.
