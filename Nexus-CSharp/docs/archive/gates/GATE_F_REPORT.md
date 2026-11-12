# Gate F — one bounded diagnostic-driven repair

**PASS for the reviewed pinned-model Windows/NTFS fixture, 2026-10-05.** Final full certification: **1,323 passed / 0 failed / 0 skipped / 0 aborted**. Production source/repair authority remains default-deny. **STOP after this gate.**

## 1. Scope

One recover-first repair after a recognized required-test equality assertion failure: A approval/apply/build/test, owned-process completion, reverse recovery, verified original baseline, shaped diagnostic observation, independent B proposal/approval/apply/build/test, deterministic final verification. Maximum two source-plan requests and one repair; no Plan C. Existing 2–3 C# files, exact ordered task targets, one unique literal replacement per file. Compiler-error repair is HOLD.

## 2. Starting E.1 checkpoint

Verified archive: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e1-final-source-20261005-102832.zip`, SHA-256 `8db96e04e457adaf6481802d5564c399a43cb1f097bf341ead4cff9941bc03bb`. All 1,028 source files, complete source set, staged/unstaged binary patches and 118 separately archived generated files matched before edits. E.1 certification remains 1,285 PASS, zero fail/skip/abort.

Pre-edit Gate F archive: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-f-before-source-20261005-111218.zip`, SHA-256 `506441ae25a23c264c88fa3eefe2a6522377a181aead15aab15868adfff2123d`, 1,028 files; tracked generated outputs separately captured. HEAD remains `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`. Final review confirms the staged user patch and archived Themes/Views unchanged; no XAML/project/solution/props/targets change relative to E.1. No reset/clean/stash/restore/commit.

## 3. Open-source patterns adapted/rejected

- [Hermes file tools](https://github.com/NousResearch/hermes-agent/blob/main/tools/file_tools.py), [security](https://github.com/NousResearch/hermes-agent/blob/main/website/docs/user-guide/security.md): adapt freshness invalidation/fresh per-attempt approval; reject shell/runtime/persistence and reusable broad grants.
- [Aider base coder](https://github.com/Aider-AI/aider/blob/main/aider/coders/base_coder.py): adapt failure observation → bounded correction and explicit reflection count; reject shell/test-command configuration and automatic Git writes. Issues [1090](https://github.com/Aider-AI/aider/issues/1090) and [5254](https://github.com/Aider-AI/aider/issues/5254) informed loop/command-boundary rejection; historical issue reports were not treated as blanket claims about every current release.
- [SWE-agent](https://github.com/SWE-agent/SWE-agent/blob/main/sweagent/agent/agents.py): adapt attempt history, cumulative statistics and stop condition; reject terminal, hard-reset, autosubmission and persisted thought/action trajectories.
- [OpenHands versioned controller](https://github.com/OpenHands/OpenHands/blob/0.54.0/openhands/controller/agent_controller.py): adapt controller-owned transitions/action-observation separation; reject generic event bus/runtime/Docker/delegation infrastructure. Current main has changed repository architecture, so the audit used versioned controller evidence.

No extra dependency or foreign agent runtime.

## 4. Existing components reused

`SourceEditPlan`, `SourcePlanApproval`, `IWriteApprovalProvider.RequestPlanAsync`, guarded source snapshots, existing SafePath/SafeWrite/NTFS TxF, reverse recovery, fixed `SourceVerification`, immutable profile, Agent registry/schema validator/policy/executor/native adapter, existing finite orchestrator, deterministic verifier, monotonic progress clock and shared runtime lease. No second writer, approval system, recovery engine, coding framework, DB or DAG.

## 5. Diagnostic shaping

`RequiredAssertionFailure` projects only known required-test identities, logical source path and reviewed integer equality values. The permitted raw message is the exact bounded xUnit integer `Assert.Equal() Failure: Values differ / Expected: 2 / Actual: <single digit>` format; appended prose, injection, unrelated exception or foreign test is rejected. Model message is regenerated from typed integers, not copied from stderr/TRX. Raw output and stack trace are not model feedback.

Feedback fields include AttemptNumber, PlanId, FailureStage/Category, LogicalSourcePath, nullable CompilerCode/Line/Column (null in this slice), RequiredTestName, bounded message, failed-state digest, recovered-base digest, remaining-budget snapshot and compact failed-edit facts. Compiler diagnostic extraction remains disabled. Success receipts normalize empty ImmutableArray facts for existing E.1 JSON evidence compatibility.

## 6. Repair eligibility

Only `KnownRequiredTestAssertionFailure` in A: build PASS, owned test process exits with the recognized failure exit, fresh bounded unique TRX, all three known facts executed exactly once, consistent passed/failed counters, no abort/timeout/nonexecution, only recognized equality failures, failed source belongs to original task, successful recovery, entire baseline/profile intact, valid runtime/model identity and remaining budget. B failure is never eligible.

Approval/policy/path/capability/budget failures, compile errors, stale binary, changed graph/profile, malformed/missing TRX, test-host crash, hash mismatch, invalid final model facts, cancellation/deadline/identity changes and recovery conflicts are terminal. Diagnostic text cannot select a command, tool, profile, target or approval.

## 7. Recover-first transition

Existing runner finishes owned-process cleanup before returning. Existing coordinator recovers reached commits in reverse order. `VerifySourcePlanBaseline` then uses guarded reads to check every original task before hash, all untouched fixture sources and profile graph integrity. `BaselineVerified` and `RecoveredBaseDigest` are retained in the result. The same baseline is rechecked before B preparation and existing all-file preflight after B approval. External edits are preserved, never adopted as a new base.

The initial WPF live prerequisite exposed synchronous recovery waiting on a temp writer that captured the dispatcher context. Existing safe temp writer now uses context-free asynchronous writes/flush/disposal; the same writer and atomic algorithm remain. A dedicated synchronization-context regression and live recover-first proof pass. Early interrupted/hung live logs are retained.

## 8. Attempt history

Run-local `SourceAttemptFact` records RunId, attempt number, PlanId, ParentPlanId, PlanDigest, verification/failure/recovery outcomes and baseline digest. Exact plan/results are retained internally and excluded from ordinary JSON metadata to avoid source/diagnostic logging. Latest pointers coexist with history. A failure survives B success; latest finalization recovery updates B only. No random additional AttemptId, persistent trajectory, DB or crash-resume.

Plan slots are reserved for requests even when arguments are rejected. Prepared-plan history counts actual prepared plans; an invalid B proposal can therefore consume request slot two while history contains only prepared A. These are distinct truthful facts.

## 9. Attempt B proposal authority

The original task supplies fixed ordered targets, workspace/profile and before hashes. B may supply only bounded literal arguments in the existing closed schema. Its target order equals A; optional c arguments are rejected for a two-file task. Preparation retains one unique match, real change, UTF-8/path/file/input/diff/line bounds. For sources without an assertion failure, B must reapply the exact A old/new literals after recovery; only failing sources may propose changed literals. No paths/root/profile/commands/budgets/approval/retry fields.

Explicit fixed-test goals and restore-all/reapply semantics were required: an earlier read-only probe proposed a no-op and failed adequacy before any dispatch. That failed probe is retained in `repair-live-final.trx`; tests were not relaxed. The subsequent explicit-goal cold/warm/contention probes and live proof pass.

## 10. Fresh approval

B is newly prepared with new PlanId/digest, fresh before/desired/diff hashes and independent `RequestPlanAsync`. A approval cannot match B, even when literal edits happen to repeat. Approval replay/denial/cancellation tests pass with zero unauthorized B writes. Only the same approved B plan derives constituent Gate E grants. Production approval remains deny by default.

## 11. Cumulative budgets

Run-wide hard maxima: source plans 2, repairs 1, build-start reservations 2, test-start reservations 2, model turns 4, tool calls 3, failures 2, transient retries 1. Default source plan budget remains 1 with repair budget 0. Counters are not reset between A/B; a start reservation that fails to spawn remains consumed conservatively.

Per-plan caps remain 2,048 diff bytes/six changed lines. Cumulative forward approved-plan preparation caps: 4,096 bytes/12 lines, including denied prepared plans. No recovery/HTTP retry becomes a third source plan. Final verification checks cumulative facts. Repeat/duplicate calls and larger budgets are rejected.

## 12. Deadline probe

Each live Gate F harness first obtains real engineering A failure/recovery facts, then runs a separate read-only model probe. During the probe there is no tool executor dispatch, approval request, source write or build/test; baseline hashes and approval-call count remain unchanged. Engineering failure setup occurs before the probe and is not misrepresented as read-only.

Same pinned Gemma GGUF/native runtime/tokenizer settings and source/diagnostic packet factory as live repair; native requests disable thinking, temperature zero, output cap 256. Cold repair prompt, repeated warm prompt and concurrent Chat are measured; adequacy requires the exact four bounded repair arguments and desired literals, not merely a successful HTTP response. Host-only receipt binds profile digest/model identity/path and the maximum measured latency. Production catalog has no receipt/profile grant.

Final full-run evidence: `docs/gate-f-evidence/live-73c2adb62b1b4b2b9e88c0834d9fff0f/tool-proof.json`:

| Probe | Prompt / completion tokens | Wall seconds | Cache tokens |
|---|---:|---:|---:|
| Cold repair prompt | 1,429 / 57 | 44.306 | 0 |
| Warm repair prompt | 1,429 / 57 | 8.106 | 1,428 |
| Chat contention | 1,429 / 57 | 9.133 | 1,427 |

Generation about 6.78–7.07 tokens/s. Cold here means zero repair-prompt KV reuse on an already-active pinned runtime, not model loading/fingerprint startup. Capability fingerprint remains cached across harness runs.

Measured full live repair: **106.757 seconds**, below existing hard maximum 120. Certification is restricted to explicit 120-second opt-in; default 60 is unchanged and cannot enable repair. Approval waits remain inside the same monotonic deadline. No pause/reset/increase. Human-delay/cold-start feasibility beyond this controlled fixture is not claimed.

## 13. Repair context

Serialized UTF-8 caps: diagnostic packet 2,048 bytes, hash-bound source slices 1,024 bytes, per-attempt result 3,072 bytes, aggregate results 6,144 bytes, repair-context admission 8,192 bytes; existing total context cap 32,768 remains. Overflow rejects deterministically. Process capture remains 4,096 bytes and structured TRX 32,768 bytes.

Final probe feedback (diagnostic plus sources) 1,453 serialized bytes; context 4,559 bytes. Final live task result aggregate 2,982 bytes and final accounted context 3,136 bytes. After B passes, C# retains full bounded attempt facts while final model context replaces the earlier source/diagnostic replay with a recovered-failure summary. A failure is not erased from authoritative history. No chain-of-thought persistence.

## 14. Build/test verification

Same absolute SDK `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe`, version 8.0.424, same fixture cwd/environment/graph/source locks and fixed arguments:

```text
build Tests/Tests.csproj --no-restore --disable-build-servers -t:Rebuild -m:1 -nr:false -p:UseSharedCompilation=false -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false --verbosity quiet
test Tests/Tests.csproj --no-build --no-restore --filter FullyQualifiedName~SourceFixtureTests.RequiredFacts --logger trx;LogFileName=required.trx --results-directory <unique-owned-directory> --verbosity quiet
```

15-second stage caps share remaining run time. Shell disabled, no restore/package/project mutation, bounded concurrent drains, owned process-tree termination and bounded cleanup. Fresh assembly timestamps/hash/MVID and graph checks remain; required success still demands exactly three executed passing facts. Failure TRX projection is added, not a replacement for success acceptance. Diagnostics never become arguments.

## 15. Cancellation/identity

Tests cover cancellation before B, during second approval/mutation/build/test; runtime deactivation token; model identity change between attempts; transport/malformed protocol while planning B; external source edits during reasoning and approval; untouched Gamma drift; B recovery conflict. No identity failure is repairable. Reached-commit facts are preserved and local recovery is awaited before return/lease drain. Existing global Chat cancellation behavior is unchanged.

## 16. Final verifier

Completed requires latest approved B's exact source/build/required-test state, matching baseline lineage/ordered targets, resolved eligible A failure, no unresolved authority/runtime/conflict failure and respected cumulative counters. Resolved failure exemption applies only to the exact source-attempt dispatch/history, not other tool failures. Result exposes Attempts, RepairUsed, Attempt1FailureCategory and Attempt2Outcome.

Whole-run finalization owns recovery: `SharedRuntimeAgent.Result` invokes the same latest-plan recovery for terminal failure before final success, including post-orchestrator checks. Root finalization refreshes source verification and identity before marking `FinalSuccessAccepted`. Finalization recovery clears verification success and rechecks baseline. After accepted success, subsequent cleanup does not undo it.

## 17. Adversarial tests

`AgentSourceRepairTests` covers A→B success, B compile/test failure, changed final digest, A approval replay, approval denial, new/third target, foreign profile/budget fields, duplicate call id, repeated third repair, transport/malformed protocol, stale source during reasoning/approval, untouched source changes, identity/deactivation/cancellation, B owned-process cancellation, B recovery conflict, start-budget exhaustion, nonfailed-file no-op, disabled catalog/default deadline and higher caps rejection. `SourceFailureProjectionTests` covers injection, foreign test, bad counters/abort, unrelated exception/exit, safe projection, dispatcher-context safety and success-receipt serialization.

Bounded second proof uses synthetic native responses with real safe writes/build/tests: A known assertion fails/recover; B compile or assertion failure/recover; terminal exception, two request slots, one repair, original source restored and no third model repair. This is separate from the genuine native-model successful live proof.

## 18. Runtime/session isolation

Gate F live harness reuses existing additional-consumer lease and Gate C/D shared-runtime isolation checks: same owned PID, cached fingerprint, Chat before/after/coexistence, independent Chat/Agent cancellation, unchanged conversation messages/selection/storage/config/catalog/design fixture and owned server shutdown. Probe contention also streams Chat without source/approval authority. Final process inspection finds no llama-server/testhost or fixture verification dotnet command left running.

## 19. Performance

Successful recorded repairs span 92.806–113.379 seconds across targeted/full attempts; final accepted certification task 106.757 seconds. Final A coordinator 6.837 seconds (build 4.415/tests 2.102); B 6.796 seconds (build 4.402/tests 2.079). Two builds/two tests, three model turns, two tool calls, one repair, no mutation retry. No successful fixture cycle exceeded 120 seconds. Model/context processing dominates; human approval waits reduce available headroom.

## 20. Targeted tests

Evidence in `docs/gate-f-evidence/`:

- `diagnostic-prerequisite.trx`: 2/2 PASS.
- `repair-authority.trx`: 93/93 PASS (early repair plus E.1/Agent selection).
- `receipt-regression.trx`: 9/9 PASS after default-array fix.
- `e1-receipt-live-regression.trx`: 3/3 PASS, including genuine E.1 live proof.
- `repair-live-explicit-goals.trx`: pinned read-only prerequisite and genuine repair/isolation harness PASS.

Early compile error and failed/no-op/hung live probes are preserved, not relabeled. No test assertion, timeout, skip condition or desired outcome was weakened.

## 21. Impacted regression

Entire EditingLite: 103/103 PASS before final serializer regression; final full runs 104/104 including that regression. `editing-watchdog.trx`: 103/103 with six-minute watchdog. Final Agent/tools/runtime/Conversation/Chat/AI CHECK/architecture/formatter impacted selection: 416 passed, zero failed, four opt-in fixture skips (`integration-final-impacted.trx`, 322.907 seconds). All opt-in fixtures execute in final full certification with zero skips. Restore/build/build-final exit zero; existing warnings retained.

## 22. Full certification

Final command: absolute SDK `test src/Nexus.sln --no-build --no-restore -m:1 --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx`, results `docs/gate-f-evidence/full-pass/`, log `full-pass.log`. All NEXUS_GATE_A_LIVE, B_PROBE, C_LIVE, D_LIVE, E_LIVE, E1_LIVE, F_LIVE flags = 1; evidence roots this gate's folder. Model/engine/design/PDF fixtures unchanged from E.1; visual output in this gate's `visual` directory.

**1,323 total/executed/passed; zero failed/skipped/aborted/error/timeout.** Eleven assemblies: Contracts 7, Foundation 35, Conversation 16, EditingLite 104, FileUtilities 19, Llama 174, ImageSubsystem 123, PdfUtilities 21, HomeSettings 24, Integration 790, ColorManagement 10. TRX wall span **1,633.911 seconds**.

Preserved earlier runs:

- `full/`: outer 25-minute command interruption while EditingLite pending, 58 completed PASS receipts; not a completed certification. EditingLite standalone watchdog subsequently passed; precise cause of this first incomplete run is not asserted.
- `full-final/`: outer 25-minute interruption with Integration pending, 522 completed PASS receipts; Gate F live task in this run passed at 92.806 seconds. Not a completed certification.
- `full-certified/`: completed, 1,321 passed/one failed out of 1,322, zero skips/aborts. Failure was E.1 evidence JSON serialization of default ImmutableArray. Fixed with empty-array normalization and targeted/live regression before clean full rerun.

Only the outer engineering command allowance was increased to permit the roughly 27-minute whole-suite run; Agent hard max remains 120 seconds and test hang watchdog remains six minutes. Hang dump collection was disabled for final engineering run, not hang detection or tests.

## 23. Files changed

Modified existing files relative to E.1:

- EditingLite `Services/EditingWorkflowService.SourcePlan.cs`, `FileWriterService.SafeWrite.cs`, `SourceEditPlan.cs`, `SourceVerification.cs`; tests `Services/SourcePlanTests.cs`.
- Agent `AgentContracts.cs`, `AgentOrchestrator.cs`, `AgentResultVerifier.cs`, `AgentRunEvents.cs`, `AgentRunLimits.cs`, `SharedRuntimeAgent.cs`.
- Agent tools `ApplySourcePlanTool.cs`, `ReadOnlyToolPolicy.cs`, `ToolExecutor.cs`.
- Integration `GateCToolLiveTests.cs`.

Added `SourceRepairFacts.cs`, `SourceFailureProjectionTests.cs`, `AgentSourceRepairTests.cs`, `GateFRepairLiveTests.cs`, this report and evidence README. No production project/package/config/XAML changes. Existing staged renderer/parser work and Gate E Display-mode fix are preserved.

## 24. Known limitations

Reviewed fixture only, exact named equality tests and integer diagnostic format; compiler repair and general source tasks remain HOLD. Explicit 120-second opt-in after measured read-only prerequisite; 60-second repair is disabled. Cold-prompt timing is not cold model-load/fingerprint or arbitrary human-approval latency certification. SDK/package cache/current-user compiled execution and Windows NTFS TxF remain trusted prerequisites, not a hostile-code sandbox. Recovery is per-file conflict-safe, not cross-file atomic or crash-resumable. Test engineering provisions/restores fixtures before Agent admission and loads the built EditingLite test assembly; this is not Agent provisioning authority. Metadata history is run-local; invalid proposals can consume slots without becoming prepared plans.

## 25. Recommendation

**PASS — freeze this bounded Gate F source and STOP.** Production source/repair catalog and approval stay default-deny. No subsequent file/edit/shell/Git/dependency/UI/memory/skills/runtime/self-improvement expansion.

The final source archive is created after this report, with CRC/source-hash/index-patch checks and separate 118 tracked generated outputs. Exact path/SHA/file counts and verified receipt are recorded in `docs/gate-f-evidence/final-checkpoint.json`, excluded from the source payload to avoid recursive archive-hash edits.
