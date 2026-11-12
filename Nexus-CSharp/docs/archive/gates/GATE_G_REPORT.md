# Gate G — bounded compiler-diagnostic repair

## 1. Scope

**PASS for the reviewed pinned-model Windows/NTFS fixture and confirmed stable combined source. Final certification: 1,370 passed / zero failed, skipped or aborted.** Earlier Gate G source certification had1,366 PASS; post-certification concurrent user work was preserved and subsequently rebuilt/reverified with explicit user confirmation. One recognized bound CS0029 string→int compiler error on approved A source may feed the existing recover-first Gate F repair. Same ordered 2–3 existing C# targets, unique literal replacement, one B/new approval, no Plan C, cumulative budgets, fixed verification authority, production default-deny and 120-second hard maximum.

Historical PARTIAL/BLOCKED result is preserved verbatim (apart from its historical title) in `docs/gate-g-evidence/blocked-report-history.md` and the immutable blocked archive. It is not relabeled as an earlier PASS. This report records the completed resume only where new evidence exists. STOP after this freeze; Gate H HOLD.

## 2. Starting Gate F checkpoint

Gate F baseline archive `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-f-final-source-20261005-144131.zip`, SHA-256 `73136ea1b3938f3b4114282b5e97f4cd7df0252c08e7bb9b0f392f6b048c31e8`, 1,034 source files and separate 118 tracked generated files; Gate F certification 1,323 PASS, zero fail/skip/abort.

Gate G pre-edit `nexus-gate-g-before-source-20261005-155457.zip`, SHA-256 `6e0fb619852752d952916a2b968e1f1c08f278401e7d7d708ab2d44c1410e997`. Resume baseline `nexus-gate-g-blocked-source-20261005-163540.zip`, SHA-256 `96eaa66c9fb921430f29281d3989ae4e3ea787c70c0b9904c96e6204447ea12a`, 1,041 source files. Source hashes/staged patch matched before environment review/resume. Blocked is not a PASS freeze.

HEAD unchanged `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`. Final source review preserves staged user patch and Gate F frozen Themes/Views/XAML/project/solution/props/targets byte-for-byte. No Git writes/reset/clean/stash/blanket restore, dependency or project changes.

## 3. Open-source patterns used

[Roslyn SARIF logger](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/CommandLine/SarifV2ErrorLogger.cs): compiler-emitted diagnostic semantics, no reconstructed compilation/workspace/code-fix engine. [MSBuild public typed replay](https://github.com/dotnet/msbuild/blob/main/src/Build/Logging/BinaryLogger/BinaryLogReplayEventSource.cs): current Csc context/project/completion and infrastructure exclusion, no handwritten binlog decoder or console fallback.

Reuse Gate F's Aider observation→bounded correction, SWE-agent run-local history/cumulative stop, OpenHands observation/action separation and Hermes freshness/new exact approval. No foreign agent runtime or extra NuGet dependency.

Environment mechanism traced before remedy in VSTest17.11.1 DefaultEngineInvoker/ProcessHelper/DotnetTestHostManager/RunConfiguration, SDK8.0.424 VSTest forwarding and runtime8.0.30 Process/OpenProcess. Supported exact-cased `DotNetHostPath` selects pinned muxer without disabling watcher. No issue-derived workaround or exception suppression.

## 4. Structured compiler instrumentation

Only opt-in compiler profiles append logging-only Roslyn ErrorLog SARIF2.1 and built-in binary-log arguments to the same fixed SDK build. Per-invocation GUID artifact names; Product and Tests each emit SARIF in their own working directory. Built-in binary ledger uses `ProjectImports=None`. SDK replay is isolated/collectible and uses pinned assemblies, no dynamic project/task evaluation in Nexus.

Pinned proof: SDK8.0.424, MSBuild17.11.48, compiler4.11.0-3.25569.22. G profile binds relevant SDK binary/target hashes and instrumentation caps; Gate F default profile/digest/arguments remain unchanged. Producer finishes before guarded artifact reads and deterministic owned cleanup. SARIF1MiB internal cap was measured against 337,865-byte rule descriptor output. Ledger1MiB compressed/8MiB expanded/20,000 events, bounded fields and container-completeness check; replay deadline≤2s and remaining task time. These internal artifacts are not model packets; existing model/TRX/process caps are unchanged.

## 5. Compiler failure receipt

Immutable internally minted `CompilerFailureReceipt`: invocation/attempt/plan/profile, CS0029/error, approved logical source, UTF-16 line/column, failed/desired hashes, Product identity, Csc task context, artifact identity/digest and validated infrastructure/source/completion facts. Caller/model JSON cannot construct authority. Receipt remains retained with A history after recovery and B success.

Library proof minted receipt while A failed desired source remained read-locked, before reverse recovery; no A test invocation. Compiler Agent delta proves this receipt drives only one independent B. No raw log storage in receipt.

## 6. Infrastructure exclusion

Current completed build, pinned compiler task file/assembly and driver, unique per-project producer, matching current nonce in typed command event, Csc task/error/completion context, exactly one typed error and one SARIF result. Only known Tests project-reference MSBuild failed-task propagation from failed Product Csc is accepted; unrelated failed tasks/errors, mixed MSBuild/package/SDK/IO/analyzer/generator/logger failures and unknown producers are terminal.

No global BuildFailed exemption. Missing/old/partial/oversized/ambiguous structured evidence fails closed as CompilerArtifactFailure. Diagnostic parsing never consumes console prose. Early null build-context callback, quoted invocation metadata and undersized tentative SARIF cap failures remain in evidence; corrections were based on actual pinned SDK shape, not weaker attribution.

## 7. Source/hash binding

Exact physical path agreement between SARIF and typed Csc error; exact inverse mapping to an A entry, no basename/suffix/fuzzy match or network URI dereference. Current guarded failed hash must equal A DesiredHash before receipt minting. Graph/SDK/profile remain intact. Locations explicitly refer to failed A state; feedback also binds recovered original hash.

Unapproved Gamma, Tests, generated/linked source, duplicate basename, uriBase ambiguity, PathMap and mapping directives are ineligible. G rejects `#` in failed desired content conservatively. Remaining source tests exercise actual `#line` baseline, linked compile mapping denial and mismatched approved desired-hash fault injection; Agent tests separately preserve external edited bytes and deny untouched-source drift.

## 8. CS0029 eligibility

Only single CS0029/error string→int case, compiler-category/tagged/default-error descriptor, pinned producer, exact approved source/location/hash/task binding and completed infrastructure-clean invocation. Before B: A recovered, whole original baseline/profile verified, runtime identity valid, cumulative repair budget available, deadline valid and measured compiler-capable host prerequisite present.

Default production catalog has neither profile nor repair grant. Existing Gate F probe receipt defaults compiler capability false. Opt-in compiler repair still requires explicit120s, source plans≤2/repairs≤1, four model turns, three tool calls, existing failure/retry bounds and diff budgets. B failure always terminal; tests A do not start for compile failure.

## 9. Compiler observation

Explicit compiler branch beside preserved known-test-assertion branch. Only attempt/plan/invocation/id/severity/logical path/line/column/regenerated message/failed hash/recovered hash/remaining budgets; no raw compiler prose/stack/environment/SARIF/binlog. Message regenerated as `Cannot implicitly convert type 'string' to 'int'.` Source slices are bounded, original-state hash-bound data.

Native projection keeps the registered source-plan tool and four exact fields for the two-file task. Trusted old literals and nonfailed new literal are schema constants; failed new literal remains bounded. Named native tool selection plus no-explanatory-prose instruction reduced observed output from audit241–246 to57 tokens. Actual executor retains the closed registered schema and C# authority checks; model schema projection is not approval or permission. No target/profile/command/budget fields.

## 10. Recover-first repair

One authoritative SDK build observes A, owned process/drains finish, receipt is sealed under source locks, artifacts cleaned, A operations reverse-recover, entire baseline verified; only then compiler observation→B. No failed A source remains during reasoning. Existing Gate F coordinator, history, cumulative counters and local recovery engine reused.

Positive fixture A: Alpha `return "two";`, Beta `return 2;`; receipt CS0029 on Alpha; no A tests; original Alpha/Beta exact after recovery. B: both `return 2;`, same targets; new approval; build/three required tests/final hashes PASS. Negative genuine build/test coordinator cases with synthetic native responses prove B compile or test failure recovers and terminates without C.

## 11. Fresh B authority

New PlanId/digest/before-desired-diff facts and RequestPlanAsync. Approval A replay fails B matching; denied/cancelled B approval produces zero unauthorized B commits. Nonfailed Beta must reapply exact A intent. Agent tests prove external edits before B or during approval are not adopted/overwritten; B recovery conflict preserves newer Alpha bytes.

Latest B final model/fact/protocol failure invokes the same recovery; baseline restored and verification cleared. Whole-run finalization recovery until accepted-success boundary remains Gate F-owned. Compiler history preserves A; final result identifies Attempts2/RepairUsedtrue/Attempt1FailureCategoryCompilerError/DiagnosticIdCS0029/Attempt2Completed on success.

## 12. Deadline evidence

Exact production compiler feedback factory, recovered source slices, compact descriptor/native selection, pinned model/tokenizer/context8192, temperature0/thinkingdisabled/output cap256. Engineering setup creates real A failure/recovery before read-only probe. During cold/warm/Chat probe: zero executor dispatches/approvals/source writes/build-test dispatch, hashes and approval count unchanged. Then host prerequisite enabled only if adequacy and time prove valid.

Final full integrated harness evidence: `docs/gate-g-evidence/live-862dbf7aaa4049a886effdaaeeb3e542/tool-proof.json`:

| Probe | Prompt tokens | Completion tokens | Wall seconds | Cache tokens |
|---|---:|---:|---:|---:|
| Cold repair prompt | 1,312 | 57 | 35.915 | 0 |
| Warm | 1,312 | 57 | 7.985 | 1,311 |
| Concurrent Chat | 1,312 | 57 | 8.607 | 1,310 |

Generation7.26–7.34tokens/s. Feedback plus sources1,023 serialized bytes; context3,998 bytes. Cold is no repair-prompt KV reuse on an already-active server, not cold model loading. Native adequacy requires one registered tool, correct exact four Alpha/Beta literals, no extra fields and tool_calls finish; real adapter/protocol parser used.

Targeted full Agent run **91.266 s**; earlier certification full Agent run **93.139 s**. Final combined-tree certification's genuine compiler task **93.089 s** (evidence `live-596309f08cb240078dcad5352db39759`), all <120 using one monotonic clock, no pause/reset. Final cold/warm/contention probe35.290/7.472/8.129s,1,321 prompt/57 completion tokens; context3,998bytes. Three model turns, two plans, one repair, two build starts, one test start. Default60s unchanged/compiler repair disabled at that duration. No claim of arbitrary human approval delay or hardware/cold-start portability.

## 13. Adversarial tests

Initial proven18/18 correlation retained; not rerun during environment review/resume targeted slices because instrumentation code unchanged. Full solution naturally includes them once at certification.

Resume `compiler-agent-delta.trx`:20/20 PASS—success, A approval replay, B denied/cancelled, B compile/test failure, recovery conflict, runtime/model transition, before-approval/mutation/build/test cancellation, final fact mismatch/post-orchestrator finalization recovery, external reasoning/approval/untouched-source drift, repeat repair/no C, nonfailed no-op/new-target rejection. `remaining-source-identity.trx`:3/3 PASS for mapping/linked/hashidentity cases. History/counters/assertions remain strict; no timeout/skip workaround.

Initial artifact/correlation negative cases retain spoof mismatch/analyzer category/mixed MSBuild error/multiple diagnostics/Tests/Gamma/generated/duplicate-basename/mapped URI/PathMap/old/partial/collision/no-completion/overflow/injection. Failure message cannot alter registry/profile/commands/policy.

## 14. Runtime/session isolation

G extends the existing live shared-runtime harness. Same owned llama-server PID and one cached fingerprint, Chat coexistence and independent Chat/Agent cancellation, unchanged Chat messages/selection/conversation storage/config/catalog/design fixture, owned shutdown. Compiler A/B uses immutable run identity and existing Option A lease/drain. Existing navigation/model-switch tests execute in full certification. No source authority is given to Chat and no second runtime is introduced.

Environment prerequisite historical cause: `D:\NEXUS` inherited Low Mandatory Label makes repo testhost.exe low0x1000; SDK VSTest runner medium0x2000. Runtime parent watcher OpenProcess0x1F0FFF returnedNULL/error5; synchronize/query0x101000 succeeded. Same process/user/session/version/architecture comparison with upstream exact-cased external `DotNetHostPath` selects pinned SDK dotnet.exe exec same testhost.dll at normal medium integrity. Parent watcher stays active; natural-parent-exit proof host exits1 in~31ms. No ACL/label/elevation/watcher/ownership changes. External engineering runsettings does not modify production Gate G runner or profiles.

## 15. Performance

Earlier final A coordinator3.572s (build2.605s, no tests); B coordinator7.799s (build4.729s/tests2.002s). Final combined-tree A3.486s/build2.526s/no tests; B8.152s/build4.940s/tests2.115s; Agent93.089s. G live task final context3,181 bytes/result aggregate2,543 bytes, within inherited ceilings. Compact probe57 completion tokens; original audit241–246 observed responses preserved as historical evidence. Model turns dominate elapsed time; approval waits still consume same120s budget.

## 16. Targeted tests

PASS evidence under this gate folder:

- Initial `compiler-correlation-adversarial.trx`:18/18 (before blocked checkpoint).
- `remaining-source-identity.trx`:3/3.
- `compiler-agent-delta.trx`:20/20.
- `compact-compiler-probe.trx`:read-only cold/warm/Chat harness PASS.
- `compiler-integrated-timing.trx`:genuine pinned-model compiler A→B and isolation harness PASS.

Historical pre-discovery Access denied runs have0 executed and remain failed/aborted run evidence, not PASS. Their environment cause/resolution is preserved in blocked history and external `ENVIRONMENT_PREREQUISITE_REVIEW.md`. An initial resume impacted invocation produced no completion receipt; retained log is not counted, followed by completed impacted evidence. No requirements weakened.

## 17. Impacted regression

EditingLite selection excluding previously proven correlation/coupling classes:107/107 PASS (`editing-impacted-resume.trx`). Completed Integration selection Agent/tools/policy/verifier/runtime/Conversation/Chat/AI CHECK/architecture/formatter:436 PASS, zero failures, six opt-in live skips (`integration-impacted-resume-complete.trx`). Every opt-in fixture executes in full with zero skips. Gate F assertion-repair behavior, default-deny and default budgets remain covered by existing tests.

Following post-certification concurrent AI CHECK/Chat changes, user confirmed current source stable and authorized combined-tree verification. `combined-judge-targeted.trx`:32/32 PASS; `combined-impacted.trx`:200/200 PASS, zero skips/fails. Combined restore/build exit0. Candidate `nexus-gate-g-candidate-source-20261006-144553.zip`, SHA256 `42c6eb6dcbb1b713660d0a259feff3392bcb0b269b797cb9cc8477d54a774936`,1,044 files, matched current source hashes/staged patch before and after final combined full run. No compiler/repair logic edit or unrelated user-code rewrite during this revalidation.

## 18. Full certification

Absolute SDK test `src/Nexus.sln --no-build --no-restore -m:1 --settings <external environment-dotnet.runsettings> --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx`, results `docs/gate-g-evidence/full-resume/`, log `full-resume.log`.

All Gate A_LIVE/B_PROBE/C_LIVE/D_LIVE/E_LIVE/E1_LIVE/F_LIVE/G_LIVE=1; evidence roots this gate; existing model/engine/design/PDF fixtures and explicit visual output supplied. SDK8.0.424 unchanged. Outer engineering allowance60min only accommodates suite duration; Agent120s and per-test6min watchdog unchanged.

**1,366 total/executed/passed, zero failed/skipped/aborted/error/timeout.** TRX wall span2,060.647s. Contracts7/Foundation35/Conversation16/EditingLite125/FileUtilities19/Llama174/ImageSubsystem123/PDF21/HomeSettings24/Integration812/ColorManagement10. Gate F and all Gate A–G live facts PASS, including both G read-only and integrated fixtures.

The above is the preserved earlier source certification, not coverage of later user edits. Accepted final combined source uses the identical test command/flags/runsettings with results `full-combined-isolated/`, log `full-combined-isolated.log`: **1,370 total/executed/passed; zero failed/skipped/aborted/error/timeout**, wall span2,074.008s. Assembly counts unchanged except Llama177 and Integration813; all Gate A–G live fixtures PASS. Source stability verified against candidate before/after.

Preserved `full-combined/` attempted combined run:1,357 passed/13failed/zero skips or aborts. Ten live fixtures failed isolation prechecks because an existing foreign llama-server PID11588 occupied port8080; SDK coupling cleanup/file-lock, three-file deadline and ONNX bad-allocation failures also occurred. No tolerance or ownership check changed. User explicitly authorized termination of only that orphan PID; path/parent/command identity reverified, parent4968 absent. Isolated SDK/source recheck2/2 and ONNX recheck1/1 passed, then complete isolated full rerun passed without source changes. Resource-failure attribution beyond recorded symptoms is not asserted.

Final inspection finds no orphan dotnet/testhost/fixture compiler/build/llama-server processes. Parent watcher active through external supported host selection; no elevation/ACL/label or process cleanup relaxation. No unrelated process was terminated.

## 19. Files changed

Initial G slice (relative F): added EditingLite `CompilerBuildLedger.cs`, `CompilerArtifacts.cs`, `CompilerFailureReceipt.cs`; tests `CompilerSdkCouplingTests.cs`, `CompilerReceiptTests.cs`. Modified EditingLite `EditingWorkflowService.SourcePlan.cs`, `SourceEditPlan.cs`, `SourceVerification.cs`, `SourceVerificationProfile.cs`; Agent `AgentContracts.cs`, `AgentOrchestrator.cs`, `AgentResultVerifier.cs`, `NativeToolModelAdapter.cs`, `SourceRepairFacts.cs`, `Tools/ApplySourcePlanTool.cs`.

After blocked checkpoint, production compiler/repair code unchanged. Resume modifies only existing Integration `AgentSourceRepairTests.cs`, `GateCToolLiveTests.cs`, `GateFRepairLiveTests.cs`, `SourcePlanTestWorkspace.cs`; adds `CompilerSourceIdentityTests.cs` and `GateGCompilerLiveTests.cs`. Report updated, original blocked report preserved in evidence markdown. No repository runsettings/config/project/package/XAML or UI edit. Source scope/staged user patch checked against archives.

Additional preserved concurrent user work (not authored/rewritten by Gate G): `AiCheckJudgeService.cs`, `LlamaTransportHelper.cs`, `AiCheckJudgeServiceTests.cs`, App.xaml.cs, AI CHECK/Conversation viewmodels and related pipeline/Conversation tests. Its post-certification timing was detected rather than silently covered by old PASS. User authorized verification/freezing the stable combined tree. Root `diag.log` inspected as VSTest diagnostic output, preserved on disk but excluded from the accepted source payload as generated diagnostic evidence.

## 20. Known limitations

Reviewed existing tiny fixture and pinned SDK/Gemma only; single string→int CS0029, not all compiler errors. Compiler tags/category are validated together with trusted pinned compiler/analyzers/graph and task/source correlation; no claim of containment of malicious compiler/analyzer binaries. SDK/package installation remains host-trusted current-user execution, Windows/NTFS TxF recovery per file, not multi-file transaction/crash-resume. Mapping directives conservatively rejected. Internal artifact caps differ from model/TRX caps because SDK emits full rule descriptors.

Hash mismatch delta test is controlled immutable-plan identity fault injection under read-locked sources, not a claim that external writers can mutate locked compile inputs. Engineering provisions temporary fixture/restore before admission; no Agent restore authority. Read-only setup and probe duration are separate from genuine Agent clock. Cold prompt excludes cold model-load/fingerprint and arbitrary delayed approvals. Explicit120s opt-in only; source/compiler repair production catalog remains default-deny.

## 21. Recommendation

**PASS — create accepted new final freeze of the verified stable combined source, then STOP.** The earlier full run finished2026-10-06 09:54+07:00; later user changes around13:43–14:02 invalidated final freeze coverage. This was detected, preserved and reverified with user confirmation. Final combined source now meets all gates, including runtime isolation, exact compiler receipt, fresh authority, single repair,93.089s integrated run and1,370/1,370 full certification.

The earlier `nexus-gate-g-final-source-20261006-143534.zip` remains a historical unaccepted snapshot; its receipt is preserved as `stale-snapshot-checkpoint.json` and is not reused as PASS. Accepted new path/hash/counts and source equality/staged preservation/generated118 output separation are recorded in fresh `final-checkpoint.json` after this report. Blocked/stale/failed history preserved. No user changes removed/restored. Gate H and authority expansion remain HOLD.
