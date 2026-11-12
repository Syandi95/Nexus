# Gate E.1 — bounded approved C# source plan

**Result: PASS for the reviewed Windows/NTFS fixture, 2026-10-05.** Final certification: **1,285 passed / 0 failed / 0 skipped / 0 aborted**. Production source authority remains default-deny. Gate F is HOLD.

## 1. Scope

One immutable independently approved plan for two or three existing allowlisted C# files, exact unique literal changes, sequential Gate E safe application, one fixed build, one fixed targeted-test invocation, fresh C# verification, and reverse conflict-safe recovery. The live proof changes Alpha/Beta; the three-file boundary has a real build/test regression proof. No general repository editing authority is conferred.

## 2. Starting Gate E checkpoint

Baseline: `C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-final-source-20261004-161134.zip`, SHA-256 `7d807ae847a5a8412cf2a27cd2fee8a03960e57a0f8309410c9121dba2ffb801`, 1,016 source files. Gate E certification was 1,261 passed, zero failed/skipped/aborted. Its Display-mode text formatting correction and regression remain in the frozen source.

## 3. Pre-edit checkpoint

`C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e1-before-source-20261004-174828.zip`

SHA-256: `8af96f502de282cd138fb78907083001716060ec4855d9f34dded416bae95185`; 1,016 files. Tracked generated output is separately captured in `nexus-gate-e1-before-tracked-generated-20261004-174828.zip`.

HEAD remains `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`. The staged binary patch is identical to this checkpoint. Dirty/untracked user work is preserved. All 53 archived Themes/Views files are unchanged; no existing XAML/project/solution/props/targets file differs from this checkpoint.

## 4. Open-source patterns adapted/rejected

Adapted Hermes task-scoped paths, shared multi-file preflight, one independent approval, freshness checks, and bounded output; adapted Aider prepare/dry-run and exact SEARCH/REPLACE with truthful partial-commit facts. Rejected fuzzy matching, cross-file fallback, shell/foreign runtimes, autonomous repair, and model-supplied approvals/commands/budgets.

## 5. Existing components reused

`FileSnapshotService`, `ProposalService`, `SyntaxValidatorService`, `DiffGeneratorService`, `SafePath`/`SafePathLease`, `FileWriterService`, `EditingWorkflowService.SafeWrite`, approval provider, existing Agent registry/policy/executor/native adapter/final verifier, shared runtime consumer lease, and run-local limits. The existing guarded local recovery was extracted for reuse; no second file writer, approval service, atomic backend, or recovery engine was introduced.

## 6. Build-graph/sealed-fixture prerequisite

Engineering provisions and restores the fixture before Agent admission. The catalog accepts only the explicit Product Alpha/Beta/Gamma compile mapping and one Tests project with three required facts, net8.0, SDK 8.0.424, fixed package versions, disabled Directory.Build imports/shared compilation, and no custom targets/Exec/linked compile items. The graph includes projects, test source, global.json, empty Directory.Build files, restored assets and generated NuGet props/targets; these are hash-bound and read-locked during verification. Source inputs are also read-locked at approved desired hashes.

`docs/gate-e1-evidence/fixture-build-graph.xml` proves the diagnostic evaluated graph excludes the repository's `AutoSignNexusOutputs`/PowerShell hook. The repository signing hook was not edited. SDK installation and restored package cache are trusted host prerequisites, not a sandbox boundary or a fully hash-pinned transitive installation. This catalog must not be used to accept arbitrary repositories.

## 7. Source inspection

The host supplies exact existing targets and before hashes. Existing guarded UTF-8/BOM/path checks are reused. Source preparation requires one exact original literal occurrence per file and a real change; no fuzzy matching or inferred already-desired acceptance. The model sees bounded literal arguments, not writable root, path configuration, build commands, or approval controls.

## 8. SourceEditPlan

`SourceEditPlan` binds PlanId, RunId, workspace identity, verification profile id/digest, ordered immutable proposals, operation/call identities, before/desired/diff hashes and diff text, and caps. Max files three, total diff 2,048 UTF-8 bytes, total changed lines six. The digest is recomputed during approval matching. Two or three constituent writes consume one separate source-plan budget; ordinary `.txt` MaxWriteOperations remains one.

## 9. Plan approval

`IWriteApprovalProvider.RequestPlanAsync` defaults to deny. One approval binds exact plan/workspace/run/profile/digest. After validation C# derives per-file Gate E grants with the same approval id; there are no per-file approval prompts. Model arguments cannot create grants. Production DI uses an empty `SourcePlanCatalog` and existing default-deny approval. Test approvals are explicit synthetic user decisions.

## 10. All-file preflight

Every approved before hash is refreshed after approval and before first commit. A stale file fails with zero commits. Profile integrity is checked before admission and each file. Each constituent safe write also refreshes its own guarded precondition. A later conflict triggers recovery of already committed earlier files; newer external contents are preserved.

## 11. Sequential safe apply

Existing Windows local fixed NTFS TxF/KTM commit path is reused with no unlocked replacement fallback. Source writes are strictly sequential. Operation records retain commit-reached, approval, backup, actual hash, verification, and recovery facts. Files are not claimed as cross-file atomic: a transient partial state exists between commits, followed by finite reverse recovery on failure.

## 12. Verification runner

`SourceVerification` is an internal fixed-profile runner, not a model-callable process API. Absolute SDK executable: `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe`. Shell execution is disabled, arguments use ArgumentList, working directory is the reviewed fixture, environment is restricted, output/error are drained concurrently. Retained combined diagnostics: 4,096 UTF-8 bytes; structured TRX cap: 32,768 bytes. Owned process-tree termination and bounded wait/drain run on cancellation and exceptions before recovery returns.

## 13. Build verification

Exactly one invocation:

```text
build Tests/Tests.csproj --no-restore --disable-build-servers -t:Rebuild -m:1 -nr:false -p:UseSharedCompilation=false -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false --verbosity quiet
```

The 15-second build cap shares the existing remaining run deadline. Successful exit is insufficient: graph/source state, newly emitted timestamps, hashes and MVIDs of Product/Test assemblies are checked. No dependency restore or repair is an Agent action.

## 14. Targeted-test verification

Exactly one invocation:

```text
test Tests/Tests.csproj --no-build --no-restore --filter FullyQualifiedName~SourceFixtureTests.RequiredFacts --logger trx;LogFileName=required.trx --results-directory <unique-owned-directory> --verbosity quiet
```

The 15-second test cap shares the run deadline. Before tests, receipt hashes/MVIDs must match. After tests, graph and assembly hashes must still match. A unique bounded TRX must have exactly AlphaDesired, BetaDesired and GammaBounded, all passed, total/executed/passed three and no failure/abort/nonexecution categories. Prose or exit code alone cannot establish success.

## 15. Recovery

Apply/build/test/final-state failure recovers committed files in reverse order through extracted Gate E guarded recovery. Original backups and approved desired-current hashes are required. Recovery conflicts preserve newer external bytes. Cancellation does not abandon a reached durable commit. Successful coordinator results retain an internal nonserialized approved-plan receipt so final-model/protocol/identity failure also invokes the same recovery path. Model final facts with a changed digest are rejected and both files are restored in the Agent regression test.

## 16. Cancellation/deadline

One monotonic run clock, default 60 seconds / maximum 120 seconds; no fresh per-stage deadline. Existing bounds remain four model turns, three tool calls, two tool failures and one retry. Source-plan requests disable post-mutation model transport retries. Runtime identity and cancellation are rechecked at source-plan boundaries, including between constituent writes. Existing executor fully awaits mutation, process cleanup and recovery. Finite local cleanup may complete beyond the admission deadline to preserve durable state facts.

## 17. Security/trust boundary

`nexus_apply_source_plan` permits only a_old/a_new/b_old/b_new and optional c_old/c_new, equal to trusted intent literals. Catalog mapping, hashes, commands, profile, approval and limits originate in C#. Source/diagnostics/tool text remains data. C# final verification checks actual desired files, untouched source files, graph and result records plus exact planId/planDigest/verified JSON. Compiled fixture/tests execute with current-user authority; this proof assumes the reviewed host SDK/package installation and fixture, rather than containment of hostile executable source.

## 18. Adversarial tests

Coverage includes stale all-file preflight (zero commits); A commit/B stale; reverse build/test failure recovery; newer-A recovery conflict; cancellation during approval/between files/build/test; owned process death; runner probe exception; runtime identity change between files; changed approval digest; duplicate/fourth/non-CS/unmapped target; changed project/linked compile mapping; changed generated NuGet import; changed final source; stale assembly receipt; malformed/injected-prose TRX; two/three-file success; Agent wrong final digest recovery. Existing Gate E path, encoding, size, atomic-replacement and `.txt` tests remain in the full suite.

## 19. Runtime/session isolation

`GateE1SourcePlanLiveTests` reuses the real shared-runtime harness and pinned Gemma native-tool capability. The live source plan uses one existing llama-server PID, one fingerprint computation cached across runs, one approval, one tool call, two model turns, one plan slot and zero single-write slots. The harness also verifies Chat streaming/coexistence, independent Chat/Agent cancellation, unchanged active conversation/detail/config/catalog/design fixture, and owned server shutdown. Existing navigation/model-switch Option A lifetime regressions pass in certification.

## 20. Performance observations

Prerequisite diagnostic: build 4.336 seconds, targeted tests 2.087 seconds; source 94–95 bytes/file, two changes four lines, measured logs 166/912 bytes. No run-limit increase was needed. Recorded live source runs: 45.877–48.545 seconds end-to-end, coordinator 6.231–6.865 seconds, build 4.005–4.479 seconds, tests 1.954–2.074 seconds. The entire live harness includes multiple additional Chat/Agent scenarios and can exceed one individual run's limit. Cold model fingerprint reading is outside the loop; it is not repeated per file/tool/model turn.

## 21. Targeted tests

Evidence under `docs/gate-e1-evidence/`:

| Evidence | Passed | Failed / skipped |
|---|---:|---:|
| source-plan-boundaries.trx | 47 | 0 / 0 |
| agent-source-final-recovery.trx | 125 | 0 / 0 |
| source-plan-live-retry.trx | 1 live harness | 0 / 0 |
| fixture-environment-check.trx | 8 | 0 / 0 |

Early compile-only failures are retained in recovery/source-live logs; the live compile issue was an incomplete helper-signature edit and was corrected before the successful live run. No failing result was relabeled PASS.

## 22. Impacted regression

Entire EditingLite project: **95 passed / zero failures/skips**, 78.245 seconds (`editing-impacted.trx`). Integration Agent/tools/shared-runtime/Conversation/Chat/AI CHECK/architecture/formatter selection: **388 passed, zero failures, three opt-in fixture skips**, 40.745 seconds (`integration-impacted.trx`). Those opt-in fixtures execute in final full certification, which has zero skips. Restore and solution build both returned exit code zero; existing compiler warnings are retained in logs.

## 23. Full certification

Final command uses absolute SDK, `test src/Nexus.sln --no-build --no-restore -m:1 --blame-hang --blame-hang-timeout 6m --logger trx`, results in `docs/gate-e1-evidence/full-final/`. Enable NEXUS_GATE_A_LIVE, NEXUS_GATE_B_PROBE, NEXUS_GATE_C_LIVE, NEXUS_GATE_D_LIVE, NEXUS_GATE_E_LIVE and NEXUS_GATE_E1_LIVE, all equal 1; evidence roots point to this gate's evidence folder. Model/engine roots: `D:\NEXUS\Models` and `D:\NEXUS\Engine`; design: `D:\NEXUS\test\1.jpg`; PDF: `D:\NEXUS\Audit\llama.cpp-master\tools\ui\tests\stories\fixtures\assets\example.pdf`; NEXUS_VISUAL_OUTPUT points to `docs/gate-e1-evidence/visual`.

**Final: 1,285 total/executed/passed, zero failed/skipped/aborted/error/timeout**, 964.601 seconds across TRX run timestamps; Integration 761/761. All eleven test assemblies pass. The first full attempt is preserved in `full/` and `full.log`: 1,283 passed, one Gate A failure due to omitted NEXUS_TEST_PDF_PATH, one screenshot skip due to omitted NEXUS_VISUAL_OUTPUT. Correcting only the environment yielded 8/8 fixture checks and the clean full rerun; no test tolerance, timeout or skip condition was weakened.

## 24. Files changed

Existing source files modified relative to pre-edit checkpoint:

- EditingLite `Services/EditingWorkflowService.SafeWrite.cs`, `Services/SafeWriteContracts.cs`.
- App `App.xaml.cs`.
- Agent `AgentContracts.cs`, `AgentOrchestrator.cs`, `AgentResultVerifier.cs`, `AgentRunLimits.cs`, `NativeToolModelAdapter.cs`, `SharedRuntimeAgent.cs`.
- Agent tools `ReadOnlyToolPolicy.cs`, `ToolContracts.cs`, `ToolExecutor.cs`, `ToolRegistry.cs`.
- Integration `GateCToolLiveTests.cs`.

Added source:

- EditingLite `Services/EditingWorkflowService.SourcePlan.cs`, `SourceEditPlan.cs`, `SourceVerification.cs`, `SourceVerificationProfile.cs`.
- EditingLite tests `Services/SourcePlanFixture.cs`, `SourcePlanTests.cs`.
- Agent tools `ApplySourcePlanTool.cs`.
- Integration `AgentSourcePlanTests.cs`, `GateE1SourcePlanLiveTests.cs`, `SourcePlanTestWorkspace.cs`.
- This report and `docs/gate-e1-evidence/README.md`; diagnostic artifacts remain evidence, not source payload. No new production project/package reference.

## 25. Known limitations

Certification is for the reviewed local Windows/NTFS TxF fixture and SDK 8.0.424, not general source editing, hostile build containment, or portable atomic recovery. Recovery is conflict-safe per file, not a multi-file transaction. SDK installation and restored package cache remain host-trusted executable inputs. No model-driven build configuration, dependency change, shell, Git write or autonomous repair is enabled. Engineering test provisioning loads the already-built EditingLite test fixture assembly from Debug/net8.0; build the full solution before running these Integration tests in isolation. The final freeze excludes generated binaries and non-Markdown diagnostic evidence, with tracked generated outputs archived separately.

## 26. Recommendation

**PASS — freeze this bounded Gate E.1 source and STOP before Gate F.** Production catalog remains disabled/default-deny. Source freeze is created after this report; its exact path, SHA-256, file counts and verification receipt are recorded in `docs/gate-e1-evidence/final-checkpoint.json` (excluded diagnostic receipt, avoiding recursive archive-hash changes). The checkpoint utility validates ZIP CRCs and every archived source hash and captures the existing index/patches without staging or committing.
