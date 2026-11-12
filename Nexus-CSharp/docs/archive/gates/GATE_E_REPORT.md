# Nexus Gate E Report

Implementation date: **2026-10-03**; blocker repair/certification: **2026-10-04**. **Gate E PASS** on the scoped Windows NTFS safe-write capability and certified runtime. Final whole solution: **1,261 passed, 0 failed, 0 skipped, 0 aborted**. The two earlier **1,259 pass / 1 fail / 0 skip** runs are preserved in §21 and the **Certification Blocker Resolution** section below. No assertion/timeout weakening. **STOP before Gate E.1 / Phase 5.**

## 1. Scope

One capability: `nexus_apply_text_patch`, one literal replacement in one existing small UTF-8 `.txt` file under an explicit Nexus-host workspace. Nexus prepares the exact proposal/diff, independent provider approves, C# rechecks expected state, retains backup, stages/flushes temp, commits atomically and verifies. Recovery is conditional on the committed state still being present.

No general WriteFile/ReadFile, create/delete/move capability, multi-file editing, source editing/build/test/Git tool, shell/arbitrary process, Agent dashboard, memory/skills, DB, second model/runtime, MAF/Hermes/ToolHost. Temp/backup artifacts are internal mutation responsibilities, not model-callable file operations.

### Phase summary

| Requirement | Evidence/status |
|---|---|
| Registered write tools | `nexus_apply_text_patch` only |
| Max write operations / retries | **1 / 0** |
| Approved roots | Explicit immutable `ApprovedTextWorkspace` supplied by trusted C# host; **production default disabled/null**; proof uses isolated `live-*/approved-workspace` and OS Temp roots |
| Allowed targets | Existing regular single-link `.txt`, UTF-8 BOM/no BOM, <=4,096 bytes, Windows local fixed NTFS |
| Expected hash / stale conflict | PASS |
| Sibling-prefix / traversal | PASS |
| Reparse/junction escape | PASS on actual junction; universal reparse rejection; symlink creation privilege unavailable on host, see §5 |
| Explicit approval / hash+diff binding | PASS, independent provider; production default deny |
| Atomic replacement | PASS on tested NTFS transaction backend; unsupported backend fails closed |
| Post-write verification | PASS, exact desired SHA-256 and fresh final-state check |
| Recovery / conflict protection | PASS on certified read-back fault and newer external content cases |
| Shell / arbitrary process / delete / multi-file | **NO / NO / NO / NO** |
| New DB / MAF / ToolHost | **NO / NO / NO** |
| Targeted | **46 mutation/path + 132 Agent/boundary passed** |
| Impacted | **655 passed, 0 failed**, one Gate A live fact skipped in this selection |
| Full | **Final PASS:1,261 passed / 0 failed / 0 skipped / 0 aborted**; two failed pre-repair runs retained |

## 2. Checkpoint

Verified Gate D source freeze SHA-256 `c4f46d487634d74914d4c37cffccc7452ae9067d03fbd3f8631cb237b151946b`: all code matched, only its archive receipt differed. Staged user patch byte-identical;40 Themes/Views unchanged. HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`.

Verified pre-edit checkpoint:1,002 source/document files, per-file hashes, raw Git index, exact staged/unstaged patches/status;118 tracked generated files separated:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-before-source-20261003-135250.zip
SHA-256: 7b5bf9d4aeeb19099685f22a32c11abf0aae071788843c9f916cdce7269a4ff5
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-before-tracked-generated-20261003-135250.zip
```

Current comparison preserves staged patch, user renderer/Conversation work, and all40 Themes/Views. No commit/stash/reset/clean. A blocked-state source checkpoint is recorded below after verification; **it is not a PASS-certified freeze**. Restore only authored paths after inspecting later user edits.

Verified blocked-state read-only source archive: **1,015 files**, ZIP integrity/per-file SHA-256 checked;118 tracked generated outputs separated:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-blocked-source-20261003-162726.zip
SHA-256: 87107a7dc3958d36172b151ca2a3072720bc19a4f6d00d5807c14dbe4af4a6f6
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-blocked-tracked-generated-20261003-162726.zip
```

Only this receipt appended after archive creation. Production/test code is the exact code used by both full runs and diagnostic rechecks. Evidence logs/TRX/live fixtures/visual captures remain in their diagnostic directories and are excluded from source archive.

Final **PASS-certified** read-only source freeze after blocker repair: **1,016 files**, ZIP integrity and every archived file SHA-256 verified, with118 tracked generated outputs separated. Blocked archive remains intact:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-final-source-20261004-161134.zip
SHA-256: 7d807ae847a5a8412cf2a27cd2fee8a03960e57a0f8309410c9121dba2ffb801
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-e-final-tracked-generated-20261004-161134.zip
```

Only this final receipt appended afterward. All code/tests match the successful1,261-test certification exactly; no code edit followed that run. Mutation/approval/Agent code is byte-identical to the blocked checkpoint; repair source delta is only MessageContentPresenter plus the new context-reuse regression.

## 3. Existing Gate D boundaries retained

Finite existing AgentOrchestrator retained: MaxModelTurns4, MaxToolCalls3, MaxRetries1, MaxToolFailures2, one monotonic default60s/max120s deadline, one Agent lease, sequential tools. Exact registry, closed validator/native parser, immutable model snapshot, request-scoped cancellation, Option A drain and C# verifier remain.

Original `nexus_get_model_snapshot` stays2-second timeout/2,048-byte cap with unchanged implementation. Native capability hashes/build/model checks and cache semantics unchanged; ordinary Chat/Judge transport/runtime workflow/server tuning unchanged. No new Agent engine or second verifier model.

## 4. Safe-write capability

Closed arguments: target (relative <=64 characters), expected_sha256 (exact64 hex characters checked by C#), old_text/new_text (<=256 characters each). Literal replacement uses EditingLite's existing unique-match ProposalService. Combined literal UTF-8 input <=1,024 bytes; target/desired file <=4,096 bytes. Initial caller intent is bounded before serialization/inference.

Risk SafeWrite, ReadOnly=false. Descriptor timeout120s is capped by remaining run deadline; result envelope cap2,048 bytes. Write-capable execution is fully awaited, never detached on cancellation. Non-success outcomes are typed. An already-present intended literal or unchanged request can return NoOp, with approval and exact-state verification but no commit/backup requirement.

## 5. Path containment

EditingLite `FileSnapshotService.ResolvePath` now delegates to canonical, separator-aware `SafePath.ResolveRelative`, replacing unsafe `StartsWith(root)` matching. Requires a fully qualified trusted root and relative target; rejects absolute/UNC/device targets, traversal/dot components, mixed-separator escapes, ADS/colon, trailing dot/space, repeated separators, DOS device/short aliases and excessive depth/length. Windows case handling is ordinal-ignore-case with final native-path equality.

Agent scope additionally requires existing local fixed NTFS root/target, `.txt`, normal/archived regular attributes, single hard link, no read-only/compressed/system/reparse target. Root and every existing ancestor/target reparse boundary is rejected. Native handles inspect attributes/link count/final resolved path; directory handles use real read access and no delete sharing, preventing ordinary rename/deletion while allowing required child-file transaction writes. Anchors rechecked at read/staging/commit boundaries; backup pinned through verification/recovery.

Actual junction escape, sibling prefix, traversal, alternate aliases, hard-link target and pinned-directory rename tests pass. Host cannot create a new file symlink (Win32 privilege1314); initial fixture attempt was retained as failed evidence. Final test explicitly proves unavailable target fail-closed without elevation/skip; an actual junction proves the same tag-independent reparse check. **Successful creation/testing of a file-symlink fixture is not claimed.**

## 6. Mutation proposal

Audited existing EditingLite primitives: snapshot, unique literal proposal, diff, syntax validator, temp writer and atomic helper. Gaps found: prefix containment, separately-read content/hash, deleted success backups and blanket legacy restore. Agent uses new safe methods on the **same partial EditingWorkflowService/FileWriterService**, not an AgentFileWriterV2.

Proposal binds OperationId/RunId/ToolCallId, workspace policy identity, logical target, exact original/desired hashes, exact escaped literal plus deterministic line diff, limits, timestamp and encoding. Before/desired text is available to approval provider in memory, excluded from serialization/logging. Escaped literals expose otherwise invisible CR/LF changes. UTF-8 BOM and unchanged line endings are preserved by encoding/replacement, not normalized. `.txt` proof does not claim source AST validation.

Legacy manual ApplyEditAsync is retained for existing compatibility and **not exposed as an Agent tool**; its blanket rollback is not the certified Agent path. Snapshot hash now comes from the same bytes read as content, not a second path read.

## 7. Approval

`IWriteApprovalProvider.RequestAsync(exactProposal, token)` is a real boundary after complete proposal. Decision binds approval/operation/policy/run/tool-call IDs, target, before/desired/diff hashes and effective file/input limits. Changed content, target, operation/run/call, workspace, diff or limits invalidates approval. Decision is not sourced from model arguments.

Production `DenyWriteApprovalProvider` and unconfigured workspace deliberately grant no writes. Proof provider explicitly approves only its synthetic exact target/hash/diff. Existing dialog service's fallback behavior is not reused; no silent automatic confirmation or dashboard. Provider receives exact physical snapshot path/owned workspace as well as logical target/content/diff, enabling a future specific review UI.

Rejected approval yields ApprovalDenied and no temp/commit. Cancellation while awaiting provider yields Cancelled and zero mutation; approval task cannot itself perform a hidden write. Late decision after cancelled wait is not applied.

## 8. Expected hash / conflict detection

Expected hash checked against one coherent original snapshot during preparation, then **re-read after approval**, and again inside protected transaction before commit. Changed hash returns Conflict, without adopting new state or applying stale patch. Tests preserve newer C bytes exactly and show no commit/approval retry. Record includes observed hash where recheck occurred. No write retry is permitted.

## 9. Backup / temporary write / atomic commit

Unique retained before-state artifact: `<target>.nexus-edit-<OperationId:N>.bak`, CreateNew, associated target/hash/timestamp in record. Copy from protected original reader, flush/close, verify original SHA, pin backup. No static overwrite; backup remains after success/failure for operator review.

Same-directory unique temp uses CreateNew, WriteThrough, cancellable bounded writes, flush-to-disk, closed encoding-aware content and SHA verification. Transaction rechecks both current target hash and temp desired hash, rejects unexpected attributes/links/security-descriptor differences, and never deletes/overwrites a pre-existing temp collision. Foreign altered temp is not committed and retained rather than blindly cleaned.

**Backend choice:** ordinary File.Replace with a no-write/no-delete target handle cannot commit on this Windows host; opening an unlock-and-replace gap would weaken the precondition. A POSIX rename probe also failed. Certified helper uses Windows **NTFS TxF/KTM**: exclusive transacted target/source handles, same-length private enlistment, transacted temp-name replacement and synchronous CommitTransaction. Tested external in-place write and replacement during the enlisted interval are blocked. On failure/cancellation before durable commit, private transaction rolls back. No unlocked fallback or automatic retry.

TxF is a deprecated/platform-specific Windows facility; availability was proven on this host, not all deployments. Unsupported/disabled/non-NTFS backend fails closed. No new app database or distributed workflow introduced.

## 10. Verification

Commit success alone is insufficient: guarded read-back SHA must equal DesiredHash. Operation records retain approval/backup/commit/actual hash/verification/recovery facts. Final C# verifier requires successful dispatch, exact operation facts/JSON claims, stable policy/intent binding and **fresh target plus backup hash check after model follow-up**. External change during final model turn fails verification without restoring over it.

NoOp requires verified unchanged before=desired state. Conflict, ApprovalDenied, verification failure, recovered failure or RecoveryConflict never becomes Completed through final prose. A committed write followed by model/transport/cancellation failure remains recorded as committed; failure does not imply nothing changed.

## 11. Recovery

Certified case: read-back verification fault while target still equals known committed B and backup still equals original A. Recovery prepares a separate bounded restore temp from immutable original snapshot, transactionally rechecks current B and restores A, then verifies A. Backup retained. RecoverySucceeded is a **non-success tool/run outcome**, not successful completion of the requested mutation.

If current target differs from committed B, or backup identity/hash cannot be trusted, no restore. RecoveryConflict preserves newer external C and original backup. Tests inject C after commit and at recovery boundary. Recovery also uses protected expected-hash transaction, so no unlocked recovery overwrite/retry. Read/restore inability fails safely; full crash/power-loss resume is not claimed.

## 12. Cancellation / commit point

Stages implemented: Prepared, Approved, PreconditionChecked, BackupCreated, TempWritten, CommitStarted, Committed, Verified, Recovering, Recovered. Agent AwaitingApproval/Recovering added only for real behaviors, separate from tool/recovery outcomes.

Commit point is successful synchronous CommitTransaction. Before it: cancellation rolls back/halts with no durable target change, owned temp cleanup where trustworthy. Explicit monotonic authorization check runs immediately before native commit, not merely timer-token inspection. Tests cancel before temp, halfway through temp, before commit and after private transaction staging.

After commit: finite verify/recovery ignores run cancellation so safe-state evaluation completes before lease drain; run still returns cancellation/deadline with truthful commit facts. Tests prove after-commit verification and cancellation during recovery. Kernel transaction timeout is5 seconds; post-commit safe-state completion can outlast the cancelled run deadline, without resetting the run or admitting new model/tool work.

## 13. Write budgets

Immutable MaxWriteOperations default/hard1; MaxWriteRetries fixed0. Accepted native request still consumes overall tool budget. Known write request reserves write slot before arguments/policy/approval, including rejected/invalid attempts. Second write request fails WriteOperationLimitReached, with exact one execution/approval/commit. No hidden write side channel. Recovery is internal remediation of the same operation, not another model-selected write.

Once any write workflow is requested, model transport follow-up retries are disabled as well; pre-write transient HTTP may follow Gate D classifier. Bounds cannot be raised by prompt, tool output or approval.

## 14. Policy

Existing default-deny policy augmented with separate explicit safe-write names and immutable host workspace. SafeWrite admission requires valid run/model/deadline, exact validated arguments matching trusted caller intent, approved existing target/type/size, one available/reserved write slot and no prior operation. Admission returns SafeWriteRequiresApproval, **not permission to commit**. Independent approval and all hash/path checks follow.

Readonly path unchanged; write descriptor not sent to model without configured workspace + explicit SafeTextChange intent/profile. Dangerous/SystemCritical/reversible/process/general write capabilities are not registered for production. Model `approved`, `allowedRoot`, `risk` or budget fields are additional/invalid arguments, not authority.

## 15. Security / adversarial tests

Covered: stale hash before proposal/after approval, approval reject/binding changes, path prefix/traversal/UNC/ADS/device names/mixed separators/reparse/junction/hard-link, missing/binary/invalid UTF-8/oversize target, temp collision/tamper, backup retention, post-temp fault, desired-state idempotence, BOM/CRLF and exact diff, external write/replace during transaction, cancellation on both sides of commit, read-back fault recovery, external C recovery conflict and fresh final-state conflict.

Agent tests cover self-approval/root/risk injection, trusted-intent mismatch, unregistered/second write attempt, disabled/raised budgets, untrusted file instructions, post-write HTTP no-retry, final prose rejection, approval cancellation/navigation/model switch and lease release with truthful committed facts. Native one-call/ID/parser/schema/default-deny adversarial tests retained.

## 16. Session/runtime isolation

Actual shared Gemma/b11057 proof uses exactly one owned PID; no startup/switch initiated by Agent and no orphan after cleanup. Original fingerprint cache remains once per fixture, not per turn/tool. Chat Messages/SelectedConversation/ActiveConversation and stored detail/config/model-index/design fixture hashes remain unchanged, except expressly approved `sample.txt` plus its backup in isolated workspace.

Real transport cancel-Agent/cancel-Chat isolation passes. Controlled approval waits cancelled/drained by navigation/model switch before runtime stop; model snapshot remains old run identity. Existing Chat/AI CHECK/Image/PDF/history tests execute in both full runs. Renderer/Views/Themes unchanged from checkpoint.

## 17. Observability

MutationProposed, ApprovalRequested/Granted/Denied, PreconditionChecked, BackupCreated, TempWritten, CommitStarted, Committed, WriteVerified, RecoveryStarted/Completed/Conflict. Correlation RunId/OperationId/ToolCallId, logical target, before/desired/actual hashes, policy/approval/backup identity, outcome and monotonic elapsed. No production full content/diff/prompt/context/secret logging or chain-of-thought capture.

Run-local immutable operation snapshots added to AgentRunProgress; no durable Agent journal/DB. Filesystem backups are retained operation artifacts. Synthetic fixture native responses/events are diagnostic evidence only.

## 18. Performance observations

Latest full-run live fixture: `docs/gate-e-evidence/live-7ad6f50247f0400ab7fa9907f1a020e8/tool-proof.json`:

| Scenario | Elapsed | Turns / tools | Cold fingerprint |
|---|---:|---:|---|
| No-tool Agent |1,217ms |1 / 0 |NO |
| First read-only tool |26,637ms |2 / 1 |YES |
| Two read-only sources |19,465ms |3 / 2 |NO |
| One approved text patch |53,013ms |2 / 1 write |NO |
| Chat alone |630ms |1 / 0 |NO |
| Chat with read-only Agent, combined |9,918ms |Agent2 + Chat1 / 1 |NO |

Write sample includes native generation/copying hash/operation facts, synthetic immediate approval, commit/read-back/final verification. Close to default60s; no timeout/server/token-budget increase to make it pass. No SLA/P95 or actual human-review latency claim.

PID **16236**, runtime identity **58144245**. Operation `dd86ca87-e32a-485f-8292-619bfa8f3bf2`; native call `gMw4ToiuTuVyFQVc1IdXuzomN60nd1G7`; approval `7eaf13f3-e51c-4e78-88b0-38d642183d3f`. Target/backup hashes:

```text
Before / backup: cd2eca3535741f27a8ae40c31b0c41d4057a7a7b912b33b9aed86485d1c84676
Desired / actual: 9c7e697ba452c03f88aacca684793440e90d1de1d52db49e7e7f27439ef3d50e
```

Exact content `hello nexus\r\n`, original backup `hello\r\n`, native ID preservation and exact desired state all asserted. Root is isolated fixture, not application source. Runtime model/engine certified hashes remain Gate D; no tuning change.

## 19. Targeted tests

Final mutation/path/kernel selection **46 passed / 0 failed/skipped**, TRX interval1.748s (`mutation-final-green.trx`). Final Agent/write/Gate D/boundary/protocol/cache selection **132 passed / 0 failed/skipped**,3.337s (`agent-certified.trx`). Live write selection passed; impacted selection also exercises live final code.

Development failures retained: unavailable symlink fixture privilege; POSIX locked rename unsupported; initial directory metadata-only handles did not pin rename; overly restrictive directory sharing blocked transaction writes. Real read/no-delete-sharing handles and revalidation resolved the tested directory/kernel issue; final path/mutation assertions unchanged. No unsafe fallback or silent skipped prerequisite.

## 20. Impacted regression

**655 passed,0 failed**, one Gate A live fact skipped in this filtered selection. EditingLite74, AI.Llama174, Conversation16, Integration391 (including live Gate E). TRX/logs: `docs/gate-e-evidence/impacted-regression/` and `impacted-regression.log`. Skipped Gate A fact executes in completed full runs.

## 21. Full certification

**Pre-repair history retained below. Current repaired certification is PASS:1,261/0/0/0, recorded in Certification Blocker Resolution.**

Restore PASS. Whole solution build PASS,0 errors, two existing CS0067 emissions on unused ApplicationVisualPreferences event,12.38s. Both full runs completed all11 projects and all Gate A/B/C/D/E live opt-ins, **1,259 passed / 1 failed / 0 skipped**, no abort. Integration756 pass/1 fail; EditingLite74 pass. First wall771.9065395s, second760.8786472s. Gate D harness stall did not recur; neither partial nor failed results are called PASS.

Blocker in both runs:

```text
AsciiCodeBlockSizeToContentTests.SizeToContent_UserCodeBlockWidth_IsNotSmallerThanRequiredCodeWidth
Code block wrapped: visual=10 != logical=8; viewport=502px too narrow for required 455,17px.
AsciiCodeBlockSizeToContentTests.cs:109
```

Raw evidence: `full-certification/`, `full-certification-final/`, associated logs and VSTest diagnostics. Same binary/assertions: ASCII/rendering recheck53/53 passed; broader layout/AI CHECK/visual isolation60/60 passed. Timeline comparison shows the same preceding AI CHECK isolation cases in Gate D PASS and both Gate E failures. Cause of whole-suite-dependent layout failure is **not established**. No renderer/theme/View/test assertion/timeout edit; no repeated full run after the second identical failure. Gate E PASS remains blocked pending diagnosis/fix and green full certification.

36 visual captures from full run remain in `docs/visual-evidence/gate-e-final/`; diagnostic captures separate. All native/approval/mutation/recovery facts passed in both full runs.

Reproduction uses SDK `C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe`, test `src/Nexus.sln --no-build --no-restore -m:1 --logger trx --diag <evidence-log> --results-directory <evidence-dir> --blame-hang-timeout 6m --blame-hang-dump-type none`. Enable NEXUS_GATE_A_LIVE/B_PROBE/C_LIVE/D_LIVE/E_LIVE=1 with their existing evidence parents; model/engine roots `D:\NEXUS\Models`, `D:\NEXUS\Engine`; design `D:\NEXUS\test\1.jpg`; existing Gate A PDF fixture and NEXUS_VISUAL_OUTPUT set. No limit weakening.

## 22. Files changed

New EditingLite files: `Services/SafePath.cs`, `SafeWriteContracts.cs`, `FileWriterService.SafeWrite.cs`, `EditingWorkflowService.SafeWrite.cs`, `AssemblyInfo.cs` (test seams only). Existing snapshot path/hash strengthened; EditingWorkflowService/FileWriterService made partial for safe methods. Proposal/diff/syntax implementations reused.

New App tool: `Services/Agent/Tools/ApplyTextPatchTool.cs`. Existing Agent contracts/limits/state/events/verifier/native final schema/failure classifier/shared admission path extended narrowly; existing finite orchestrator adds explicit write fact selection and disables post-write retry. Registry/policy/executor gain safe-write admission/counter/fully-awaited mutation semantics. App.xaml.cs registers same EditingLite singleton alias, disabled workspace, deny provider and exact tool.

New tests: `SafePathTests.cs`, `SafeAtomicReplaceTests.cs`, `SafeWriteWorkflowTests.cs`, `AgentSafeWriteTests.cs`, `GateESafeWriteLiveTests.cs`. Existing SharedRuntimeAgentTests fixture and GateCToolLiveTests shared live helper extended. Documents: report and `docs/gate-e-evidence/README.md`; logs/TRX/fixtures/visuals diagnostic. No project/package/solution/renderer/UI/ViewModel/ordinary transport/runtime ownership edits. User staged changes preserved.

Current certification after the ownership-correct formatting repair is **PASS**, as detailed under Certification Blocker Resolution. The preceding paragraph records the historical blocked state and its evidence; it is not the current acceptance status.

## 23. Known limitations

- Historical whole-suite ASCII blocker resolved by the explicit message formatting contract below. WPF's dispatcher PTS pool remains a framework behavior; this repair does not claim to fix arbitrary third-party mixed-mode FlowDocuments.
- Windows local fixed NTFS/TxF backend only. TxF deprecated; unsupported/disabled backend fails closed. No portable backend certification claimed.
- Host lacks symlink-creation privilege; actual junction/hard-link/pinned-directory tests pass. Successful file-symlink fixture creation is not claimed.
- Only small existing regular single-link `.txt`, one literal patch/operation, no arbitrary encoding/metadata/security-descriptor changes. Security-descriptor mismatch denies replacement.
- Production workspace disabled and approval provider denies; no review UI yet. Synthetic provider proves contract, not human usability/latency.
- In-memory operation record + retained backup; no restart/resume/crash journal, multi-file transaction, coding/build/Git/shell tool. Kernel atomicity is not an app crash-resume guarantee.
- Legacy manual EditingLite workflow is not the Agent-certified path. Future source workflow must select safe API explicitly.
- First fingerprint latency retained; one Agent/Option A/non-stream/fingerprinted Gemma+b11057 only. Write sample approaches60s deadline; truthful commit facts retained if later deadline/model stage fails.

## 24. Recommendation for next phase

Blocker repaired, causal regression added and full certification PASS. **Gate E.1 / Phase 5, Agent UI, memory/skills, MAF/workflow and self-improvement remain HOLD** pending separate review/authorization.

After separately reviewed Gate E PASS, next recommended slice is Gate E.1 Source Editing Workflow; it requires explicit multi-file/source/build/test/checkpoint/recovery authorization. No such work started here.

## Certification Blocker Resolution

### Observed failure and frozen baseline

The October3 whole-solution runs recorded visual10/logical8, viewport502px and nominal required455.17px. Blocked checkpoint `87107a7dc3958d36172b151ca2a3072720bc19a4f6d00d5807c14dbe4af4a6f6` preserved unchanged. Initial diagnosis used **the same blocked App/test DLLs**, `--no-build --no-restore`, before any repository code edit. A separately built temporary state runner loaded these DLLs; no safe-write/Agent/runtime/approval/recovery code was changed during repair.

### Minimal reproduction and reduction evidence

- Fresh-process original ASCII class: **3/3 PASS** (`blocker-fresh-control.trx`). Fresh state: Consolas12 Normal/Normal/Normal,96DPI, Ideal/Auto/Auto, LeftToRight, id-ID/en-GB, Dark, viewport502, visual8.
- Actual TRX order identified40 predecessor classes. Second half + ASCII:346/346 PASS. First half produced exact ASCII failure before a later diagnostic run timeout; that incomplete run is diagnostic evidence only. Further reductions including143-case fast first-half subset,57-case subset and76-case subset passed. Actual reduced-run order mattered: selecting Gate A alone placed ASCII **before** its predecessor, so its apparent PASS was not treated as a valid negative causal control.
- Explicit same-process **`LiveChatHistoryAiCheckAndModelLifecycle_UseOneOwnedRuntime → original ASCII assertion` FAIL** (`blocker-explicit-chat-state.log`, `blocker-explicit-chat-width-chain.log`). Explicit image/PDF/theme/96+144DPI predecessor → ASCII PASS. Chat-view with two static messages → ASCII PASS.
- Further reduction removed model/network/OCR entirely: show a Display-mode RichTextBox in a Window, close/detach it, allow its PTS context to be returned by finalization/background dispatcher, then run **the original unchanged ASCII assertion first**. Exact10/8/502/455.17 failure recorded in `blocker-minimal-original-red.log`. GC is used only to reproduce context release deterministically, not as a production fix or retry.

Temporary runner setup failures (dependency resolution/native assets, including wrong ONNX binding/access violation) were classified as **probe-environment failures**, not evidence of Nexus corruption. Exact test-bin dependencies/native paths were used afterward. The one server left by the failed external probe was identified by PID/command line and cleaned before further runtime experiments. No native/harness timeout was increased to pass the actual assertion.

### Root cause

Same font identity,12pt, style/weight/stretch, theme, dictionary source order, culture, flow direction,96DPI, viewport502, paddings and rounding flags. **Actual character rectangles differed:** fresh glyph advance6.5966667 DIP/line height14.05078125; contaminated advance7 DIP/line height14. Nominal Ideal measured width remained455.17, while native flow layout was pixel-aligned Display. Public TextOptions getters could still report Ideal.

Read-only inspection of the blocked runtime's dispatcher PTS context pool showed the Ideal document borrowing an in-use formatter marked **Display**. WPF `PtsCache.AcquireContextCore` chooses a free context by optimal-paragraph flag but omits TextFormattingMode; formatter mode is set only at context creation. `StructuralCache.EnsurePtsContext` always requests the formatter-enabled PTS context. A recycled displayed Chat context can therefore carry Display metrics into a detached Ideal document. Resource/theme restoration cannot correct that hidden formatter pool.

Source corroboration: dotnet/wpf `PresentationFramework/MS/Internal/PtsHost/PtsCache.cs` and `StructuralCache.cs`; same internal pool/formatter fields were inspected on the installed runtime. This is a dispatcher-level framework cache dependency, not safe-write contamination or a leaked theme/font-name change. It can occur in actual application lifecycle as displayed documents are discarded/reused.

### Why isolated runs passed

Fresh/detached renderer tests create or use an Ideal context, so measuring and rendering agree. The long live Chat lifecycle displays messages under ConversationView's Display formatting and allows contexts to be released; the next detached document can borrow the Display formatter. Whether a context remains in use or has been finalized explains the order/lifetime dependency. A capture that creates an extra document can itself change pool availability; original assertions were therefore invoked before additional capture in the minimal red proof.

### Fix and ownership

Two explicit formatting assignments in `src/03_App/Nexus.App/AIWorkspace/Rendering/MessageContentPresenter.cs`: the message RichTextBox and temporary no-wrap measurement TextBlock both use **TextFormattingMode.Display**, the existing production Chat formatting contract. Rendering and measurement are now coherent pixel-aligned operations regardless of a prior Display PTS context. This is a component formatting-contract fix, not a renderer rewrite, changed font, new padding/tolerance, hardcoded viewport or private framework cache mutation.

Added `src/03_App/Nexus.Integration.Tests/MessageTextFormattingIsolationTests.cs`: displayed document → close/detach → deterministic context release → **existing original ASCII assertions**. No existing assertion/expected count or timeout modified; no blanket resource/cache reset or parallelism change. User staged patch and all40 Themes/Views remain byte-identical. Original WpfAppHost behavior unchanged. All Gate E mutation/Agent code matches blocked checkpoint.

### Targeted and impacted evidence

- Minimal context-reuse regression + original ASCII class: **4 passed / 0 failed/skipped** (`blocker-root-fix-green.trx`).
- External minimal runner after fix: original assertion PASS (`blocker-minimal-fixed-state.log`). Explicit real Chat-live predecessor after fix: original assertion PASS (`blocker-live-chat-fixed-state.log`).
- Renderer/ASCII/layout/WPF resources/theme/Conversation/AI CHECK/write boundary relevant selection with visual capture enabled: **156 passed / 0 failed/skipped**,18.029s (`blocker-impacted-final.trx`). Earlier selection had155 pass plus one intentionally non-opted-in visual fact; final selection executes it.
- Frozen mutation/path/kernel tests: **46 passed / 0 failed/skipped** (`blocker-frozen-mutation.trx`). Original Gate E targeted132 and impacted655 evidence remains valid and retained; full final executes all those facts again.

### Final full certification

Exactly one full run after stable targeted/impacted fix: **1,261 passed,0 failed,0 skipped,0 aborted**, all11 projects, Integration**758/758**, wall **753.4018291 seconds**. All Gate A/B/C/D/E live flags and visual capture enabled,6-minute hang watchdog unchanged. Restore/build PASS. TRX: `docs/gate-e-evidence/full-certification-repaired/`; output `full-certification-repaired.log`; VSTest diagnostics `blocker-certified.vstest*.log`;36 final visual captures `docs/visual-evidence/gate-e-certified/`. No leftover llama-server after cleanup.

The two pre-repair failures in §21 are not deleted or relabelled as PASS. Gate E is now **PASS**; **STOP — Gate E.1 has not started**. Final verified source-freeze receipt appended to §2 after archive creation.
