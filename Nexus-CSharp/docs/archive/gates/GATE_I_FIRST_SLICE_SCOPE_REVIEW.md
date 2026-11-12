# Gate I — first usable Agent workspace / Stage 1 scope review

**13 Oktober 2026 — GATE I SCOPE READY FOR OWNER REVIEW.**
Rekomendasi: satu workflow source-edit nyata melalui WPF pada **satu project
reviewed yang kompatibel dengan profile Gate H**, dua existing `.cs` files,
literal edits dari pengguna, exact review/approval, build/tests dan recovery.
Integrasi ini feasible tanpa executor/runtime/diff engine baru. General-purpose
coding pada arbitrary project belum feasible dalam authority tersertifikasi.

Baseline: **Gate H CERTIFIED WITH APPROVED OPTION B SCOPE**. Stage 1 hanya membaca
source dan menulis dokumen ini; belum ada integrasi Agent UI atau test/model run.

## 1. Current architecture map dan baseline ownership

Policy dibaca: `AGENTS.md`, `docs/NEXUS_ENGINEERING_POLICY.md`, Gate H final report,
Option B dan acceptance manifest. Certified checkpoint SHA-256 terverifikasi:
`2039be89ee34e82cfa38c20471ea68bf742fb09dd08ccdbb27ef79665737f822`.
Nested candidate SHA juga cocok; **651/651 source files identik**, staged dan
unstaged patch identik dengan freeze. HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`,
branch `audit/nexus-agent-phase0-20261001`. Staged SHA tetap
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.

Semua existing staged/unstaged/untracked source diperlakukan sebagai baseline user
work, termasuk rendering/UI changes. Snapshot full Git status dan before-hashes
prospective files: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-stage1-baseline-20261013.json`.
Sole Stage 1 repository writer: dokumen baru ini. Stage 2 hanya mengambil ownership
surgical integration pada daftar §5 setelah approval; baseline tidak boleh ditimpa.

Path berikut relatif terhadap repo; `App/` = `src/03_App/Nexus.App/`,
`IT/` = `src/03_App/Nexus.Integration.Tests/`,
`Edit/` = `src/02_Modules/Nexus.AI.EditingLite/`,
`ET/` = `src/02_Modules/Nexus.AI.EditingLite.Tests/`.

| Area | Actual code path / class.member | Existing test evidence / gap |
|---|---|---|
| Folder/file selection | `App/ViewModels/ConversationViewModel.cs`: `ExecuteAttachFolderAsync`, `ExecuteAttachFileAsync`, `AddAttachmentsAsync`, `ExecuteViewAttachmentContentAsync` memakai Microsoft.Win32 dialogs dan attachment extraction. Ini context Chat, bukan approved coding workspace. | `IT/ConversationViewModelTests.cs`: `Issue5_AttachmentWorkflow_FileFolderImageChipsAndPromptContext`, `SendWithAttachment_InjectsFormattedAttachmentIntoUserMessagePayload`. Belum ada workspace/session admission UI test. |
| Manajemen File | `App/ViewModels/FileManagementViewModel.cs`: `SelectedTab`, `ActiveViewModel`; `App/Views/FileManagementView.xaml`: `WorkspaceTabSelector` hanya CleanDownloads/RapikanFile. `RapikanFileViewModel.BrowseFolder` menyetel `TargetPath`. | `IT/FileManagementShellTests.cs`, `WorkspaceTabStyleTests.cs`. Tidak ditemukan coding file tree atau shared active-project state di jalur ini; folder Rapikan tidak otomatis menjadi Agent root. |
| WPF routing/streaming | `App/Views/ConversationView.xaml`: Chat/AI Check, `TxtAIInput`, `BtnAISend`, read-only attachment preview; `.xaml.cs`: `TxtAIInput_PreviewKeyDown` → `SendCommand`. `ConversationViewModel.StartGeneration/RunGenerationAsync` → `IConversationRuntimeWorkflow.StreamAsync`, current active Chat history, scheduled/final rendering. Tidak memanggil `INexusAgent`. | `IT/ConversationViewModelTests.cs`: `Issue4_Streaming_CreatesBubblesInstantly`, `FreshPlanConversation_SendsOnlyModeSystemAndCurrentPrompt`, `ActiveBuildConversation_SendsOnlyModeSystemActiveHistoryAndCurrentPrompt`, `TechnicalStatus_IsOwnedByComposer_NotChatMessages`. Nama Plan/Build test historis tidak membuktikan source-edit UI. |
| Agent entry / session | `App/Services/Agent/AgentContracts.cs`: `INexusAgent.ExecuteAsync`, `AgentRequest(SessionId, Prompt, RelevantContext, SourceIntent)`; `SharedRuntimeAgent.ExecuteAsync` membuat messages baru tiap run, validasi limits, acquire lease, verified terminal result. Context diberikan caller; history Chat tidak diambil sendiri. | `IT/SharedRuntimeAgentTests.cs`: `TimeoutOrFailure_ReleasesLeaseAndDoesNotRetainSessionOrLogContent`, `InputAndResultLimits_AreEnforcedWithoutToolsOrPersistence`. UI belum membentuk workspace-bound session/context. |
| Single runtime owner | `App/App.xaml.cs:RegisterServices`: singleton server/engine, concrete+interface `ConversationRuntimeWorkflow` alias. Workflow `AcquireAdditionalConsumer`, `CancelAdditionalConsumerAsync`, `SwitchModelAsync`, `StopAsync`; `App/Services/RuntimeConsumerLease.cs` memisahkan Agent CTS. `ConversationViewModel.OnNavigatedFromAsync` → Stop; `MainViewModel.TransitionLifecycleAsync` await lifecycle. | `IT/SharedRuntimeAgentTests.cs`: `CancelAgent_DoesNotCancelConcurrentChatRequest`, `CancelChat_DoesNotCancelAgentRequest`, `NavigationOrSwitch_CancelsAndDrainsAgentBeforeStoppingModel`, `SameModelReuse_DoesNotRevokeAgentOrRestartRuntime`; `IT/ArchitectureGovernanceTests.cs`: `Production_DI_has_one_active_implementation_and_shared_alias_instances`. |
| Only execution/mutation path | `App/Services/Agent/AgentOrchestrator.cs:ExecuteCoreAsync` → `Tools/ToolExecutor.cs:ExecuteAsync` → `ApplySourcePlanTool.ExecuteAsync/ExecuteAuthoritativePlanAsync` → `Edit/Services/EditingWorkflowService.SourcePlan.cs:PrepareSourcePlanAsync/ExecuteSourcePlanAsync` → approved SafeWrite → verification/recovery. | `IT/AgentSourcePlanTests.cs:NativePlanRequest_UsesOnePlanBudget_AndRecoversInvalidFinalFacts`, `MultiHunkSourceToolTests.cs:ExplicitArrayRegistration_ClosedArguments_NeverPrepareOnInvalidInput`; `ET/Services/SourcePlanTests.cs:TwoSources_OneApproval_FreshBuild_RequiredTests_ExactFinalHashes`. |
| Workspace/path authority | `Edit/Services/SafeWriteContracts.cs:ApprovedTextWorkspace.ForSourceFiles/ResolveTarget`, `SafePath.cs:SafePath.ResolveRelative/SafePathLease`; `SourceVerificationProfile.cs` constructor hashes graph/SDK/source, `IsIntact`; `App/Services/Agent/Tools/ReadOnlyToolPolicy.cs:Evaluate` checks host catalog+intent, not model paths. | `ET/Services/SafePathTests.cs`: `JunctionEscape_IsDenied`, `HardLinkedTarget_IsDenied`; `SourcePlanTests.cs`: `UnknownGraphAndLinkedCompileMapping_AreRejected`, `GraphChangedAfterApproval_DeniedWithoutBuild_AndSourceCannotAddAuthority`. |
| Exact review/approval | `Edit/Services/SourceEditPlan.cs`: `Entries[].Diff`, hashes, `PlanDigest`, `WorkspaceIdentity`, `SourcePlanApproval.For/Matches`; `SafeWriteContracts.cs:IWriteApprovalProvider.RequestPlanAsync`. `App/App.xaml.cs` currently registers empty `SourcePlanCatalog`, rootless workspace, `DenyWriteApprovalProvider`. No exact-plan approval/diff UI found in inspected Conversation flow; MainViewModel dialogs are generic confirmations. | `ET/Services/SourcePlanTests.cs`: `ChangedApprovalDigest_DeniedBeforeApply`, `AllFileStaleAfterApproval_ZeroCommits`, `CancelApproval_ZeroWrites_AndCancelRecovery_DoesNotAbandonStateEvaluation`. Production source writes currently disabled. |
| Verification/recovery/result | `Edit/Services/SourceVerification.cs:BuildAsync/TestAsync/ParseRequiredTests` fixed SDK commands, fresh hash/MVID and exact three TRX facts. `EditingWorkflowService.SourcePlan.cs:Failure/RecoverSourcePlanAfterFinalFailure`; `ApplySourcePlanTool.RecoverFinalFailure`; `SharedRuntimeAgent.Result` preserves typed outcome/error. `AgentRunEvents.State/Emit` currently metadata logging, not UI notifications. | `ET/Services/SourcePlanTests.cs`: `BuildOrTestFailure_RecoverInReverse_NoRepair`, `RecoveryConflict_NewerAIsNeverOverwritten`, `StaleBinaryReceipt_IsNeverAcceptedAsPassingTests`; `IT/HostAuthoritativeRepairTests.cs:RetainedOutputLimit_RealApprovedA_RecoversAndTerminatesWithoutB`. |

## 2. Reusable components / Open Source First

- **USE Nexus:** entire Agent/registry/policy/SafeWrite/verification/recovery path;
  `ExactSourceEdits`, existing `DiffGeneratorService` and prepared `Entries[].Diff`;
  `ViewModelBase`, Relay/AsyncRelay commands, semantic WPF resources, runtime lease.
- **ADAPT existing Microsoft/.NET/WPF:** `OpenFolderDialog`, existing DI,
  Dispatcher, CancellationToken/TaskCompletionSource, read-only TextBox+ScrollViewer
  for exact diff and explicit buttons. Approval adapter implements existing interface.
- **ENHANCE integration only:** workspace/session binding, exact-plan approval
  presentation and run-local metadata-to-UI adapter forwarding existing logger.
- **NOT NEEDED:** AvalonEdit editor, AvalonDock docking, WpfUI theme, new diff engine.
  Existing bounded diff is enough. Roslyn/compiler instrumentation already exists
  through pinned SDK; no new package. GitHub pattern search **NOT NEEDED** because
  these concrete gaps are covered by existing Nexus/.NET primitives.
- **REJECT for this slice:** Aider executor/fuzzy replacement or unrestricted shell;
  they would introduce a second execution/authority path. No dependency added.

## 3. Missing integration links — material constraints

1. UI does not own a reviewed coding workspace+session snapshot or call Agent.
   Attachment contents/path labels must not become mutation authority.
2. `SourceVerificationProfile` is **fixture-shaped**, not a general project adapter:
   SDK8.0.424; fixed `Product/Product.csproj`, `Tests/Tests.csproj`,
   `Product/Alpha.cs`, `Beta.cs`, `Gamma.cs`; explicit compile/dependency/hook checks;
   required facts `AlphaDesired`, `BetaDesired`, `GammaBounded`. Arbitrary `.sln` or
   `.csproj` selection cannot safely enable it without changing the contract.
3. Initial `SourcePlanIntent` has **2–3 files**, 1–2 exact edits/file, ≤3 edits total.
   `SharedRuntimeAgent` supplies trusted initial literals; `ApplySourcePlanTool`
   rejects any model divergence. A free-form request alone does not generate a new
   executable source intent. Single-file `.cs` mutation is not this contract.
4. Reviewed profile and UI approval must enter the existing tool graph consistently;
   changing a singleton catalog while a run is active is unsafe. Default DI remains
   deny-write outside the specifically admitted workspace/run.
5. Live status is logged, not exposed as an observable Agent UI contract. Need a
   small run-local logger adapter, filtered by captured session/run and marshalled
   to Dispatcher; no raw prompt/source/history logs.

## 4. Smallest usable workflow — recommended milestone I1

**Select reviewed project → request + explicit literal edits → Agent proposes exact
intent → review all diffs → approve once → bounded edit → fixed build/tests or
recovery → truthful UI result.**

- Proposed first project: owner-reviewed `D:\NEXUS\GateI-ReviewedWorkspace`, using
  the existing compatible Product/Tests graph, externally prepared/restored before
  admission. Root/graph/targets/task are explicitly owner-reviewed; matching a file
  layout alone is not permission. No provisioning/restore command in Agent tools.
  Engineering UI-proof setup reuses `ET/Services/SourcePlanFixture.cs` patterns;
  production does not load test assemblies or call `SourcePlanTestWorkspace.Create`.
- Native picker selects only this admitted project; other roots fail closed with
  an honest unsupported/unreviewed-workspace message. Display canonical root,
  approved profile and a flat list of the **two allowed targets Alpha/Beta**;
  Gamma remains unchanged and verified. A general recursive tree is unnecessary.
- User supplies coding request plus literal Old/New entries for Alpha/Beta (e.g.
  `return 1;` → `return 2;` to make their required tests pass). Host reads scoped
  snapshots/hashes and creates immutable `SourcePlanIntent`; no hash input from
  model. Prompt is descriptive, not a substitute for explicit edit intent.
- Add a compact **Agent (.cs)** entry in AI Workspace opening a child WPF panel:
  workspace, request/edits, Run/Cancel, state, exact diff review, Approve/Reject,
  terminal verification/recovery facts. Existing Chat/AI Check rendering stays intact;
  Agent request command is explicit and never uses Chat send/regenerate as mutation.
- Capture workspace identity/profile digest, fresh Agent session ID and request
  revision per run. `RelevantContext` includes only scoped selected snapshots needed
  for this task within existing limits; no automatic Chat history/attachments.
- Thin workspace workflow composes a run-local graph using the **existing**
  `SharedRuntimeAgent`/orchestrator/tool classes, immutable selected catalog, UI
  approval provider and same shared runtime/server/HttpClient. It does not execute
  writes/builds itself. Runtime owner remains singleton; serialize admitted runs.
- `RequestPlanAsync` publishes the actual prepared plan. Read-only diff per file,
  canonical root/targets, before/desired hashes, profile, PlanId/digest, limits and
  build/tests shown together. Approve mints `SourcePlanApproval.For(exactPlan,true)`
  only for the still-current run/snapshot; Reject/close/cancel never grants approval.
- I1 uses **one source plan, no automatic repair**, MaxRetries=0, native cap256,
  hard120 s including model/approval/build/test. Existing rollback recovery remains
  enabled. Build/test limits15 s each remain. Review UI shows remaining time and
  expiration; no paused/refilled deadline. H repair remains available in its existing
  certified path, but I1 does not enable repair without its measured prerequisites.

This gives a real, bounded two-file coding workflow on disk through production WPF,
not autonomous arbitrary-project coding. Broader project support is a later reviewed
milestone, not a silent fallback within I1.

## 5. Exact files expected to change in Stage 2

All paths below are under `src/03_App/`; one integration writer after approval.

| File | Surgical purpose |
|---|---|
| `Nexus.App/App.xaml.cs` | Register thin workspace workflow/approval bridge; reuse singleton runtime aliases; keep default deny path. |
| `Nexus.App/ViewModels/ConversationViewModel.cs` | Expose child Agent VM/open command; lifecycle cancel+drain/invalidate pending approval; preserve Chat commands/history. |
| `Nexus.App/Views/ConversationView.xaml` | Agent entry and child host using existing resources. |
| **New** `Nexus.App/ViewModels/AgentWorkspaceViewModel.cs` | Workspace/request/edit fields, commands, exact-plan review and typed outcome presentation. |
| **New** `Nexus.App/Views/AgentWorkspaceView.xaml` and `.xaml.cs` | Minimal native panel; picker/focus/keyboard UI glue only. |
| **New** `Nexus.App/Services/AgentWorkspaceWorkflow.cs` | Interface+thin admission/session/run composition; no new tool, writer or verifier. |
| **New** `Nexus.App/Services/AgentWorkspaceApprovalProvider.cs` | Run-bound asynchronous `IWriteApprovalProvider`; non-source requests deny. |
| **New** `Nexus.App/Services/AgentWorkspaceRunLogger.cs` | Forward existing logger and safely project run-local state metadata to UI. |
| **New** `Nexus.Integration.Tests/GateIAgentWorkspaceTests.cs` | Admission/session/approval/outcome/cancellation integration tests. |
| **New** `Nexus.Integration.Tests/GateIAgentWorkspaceUiTests.cs` | Actual WPF controls/commands, real reviewed disk edits, verification/recovery evidence. |
| `Nexus.Integration.Tests/ArchitectureGovernanceTests.cs` | Assert same runtime owner/aliases and existing mutation path under workspace composition. |

`ConversationView.xaml.cs` was audited and its hash captured; no planned change to
Chat Enter/streaming handlers. Existing core Agent/editing/profile/policy contracts
need no changes for this milestone. External reviewed project preparation and UI
proof receipts are separate artifacts; exact approved location required before setup.

## 6. Approval/security boundaries

- Root selection ≠ write approval. Host-reviewed admission ≠ plan approval.
  UI cannot register arbitrary targets/commands/profile from model or attachment.
- Exact immutable run/profile/intent; path validation/NTFS pinned-directory handles,
  source hashes and graph integrity checked by existing services before writes.
  No unrestricted reads, terminal/shell/Git tools or legacy `ApplyEditAsync` bypass.
- One independent plan approval bound to RunId, PlanId, workspace policy ID,
  verification profile and plan digest. All file diffs visible before approval;
  stale UI events/double clicks cannot grant a different plan. Approval expires on
  cancel/deadline/session/root/model/navigation changes.
- Agent Cancel cancels only Agent CTS, not Chat/server. Navigation/model switch
  drains the existing lease; workspace/session changes cancel+await recovery before
  rebinding. Late Dispatcher updates cannot populate another session's review/result.
- Only `Completed` plus verified source/build/three required tests/hash facts displays
  success. OutputLimit, rejected/failed/timed-out/cancelled and recovery conflict are
  explicit failures/statuses. Never infer success from streamed text, exit0 alone,
  retained old receipts or source being temporarily changed.
- Recovery is existing hash-guarded reverse restoration, preserves external edits,
  and is awaited before releasing UI ownership. No automatic repeat of a failed run.

## 7. Targeted and impacted tests (Stage 2 only)

**RED→GREEN additions:** reject unreviewed/outside-root/reparse/graph drift; snapshot
and session binding; exact UI→Agent intent; mismatch/unsupported single-file denial;
zero writes before approval or on deny/close/cancel/timeout; stale/double approval;
cross-session late-result rejection; success requires actual verification; OutputLimit
failure; failed build/tests restore baseline, recovery conflict preserves newer file;
Agent/Chat CTS isolation and navigation/model switch while approval is pending.

**Impacted existing:** `SharedRuntimeAgentTests`, `ConversationRuntimeWorkflowTests`,
`AgentSourcePlanTests`, `MultiHunkSourceToolTests`, `AgentSourceRepairTests`,
`HostAuthoritativeRepairTests`, `ArchitectureGovernanceTests`, `NavigationLifecycleTests`;
EditingLite `SourcePlanTests`, `MultiHunkSourcePlanTests`, `ExactSourceEditsTests`,
`SafeWriteWorkflowTests`, `SafePathTests`, `SafeAtomicReplaceTests`; affected
`ConversationViewModelTests` routing/lifecycle/history and theme/accessibility/layout
checks for the new panel. Keep existing assertions; add tests for the approved feature.

Run targeted first, then impacted regression on pinned .NET8 host. Reuse Gate H
certified evidence for untouched behavior, labelled historical. Fresh UI live workflow
proof is separate from full certification; Stage 1 has executed **zero tests/model calls**.
P1 diagnostic remains independently failed/read-only under Option B; do not replay
consumed Gate H approval or launch a full certification for this audit.

## 8. UI end-to-end acceptance criteria

1. Launch actual production WPF app/composition; select admitted project with native
   picker. UI displays correct root/profile/two paths; another project is rejected.
2. Enter coding request and two bounded literal edits. Run invokes existing Agent;
   one runtime owner, model snapshot/SessionId/context bound to this workspace.
3. Both exact diffs and identity/verification facts appear. Disk hashes unchanged
   until explicit click on Approve; Reject closes with zero mutations.
4. Approve actual plan once: Alpha/Beta desired hashes, Gamma unchanged, real fresh
   build and **3/3 required TRX tests** verified; UI shows Completed + matching receipt.
5. Controlled failure path through the same WPF action flow shows failure and real
   recovery/baseline result; controlled external drift shows RecoveryConflict without
   overwriting newer content. OutputLimit presentation verified deterministically,
   and any actual live OutputLimit remains a visible failure, never a rerun-to-pass.
6. Cancel Agent while Chat is active leaves Chat request alive; converse remains
   isolated. Model switch/navigation cancels/drains pending approval/run safely.
7. Capture actual UI screenshots/action trace plus RunId/PlanId/profile digest,
   pre/post hashes, approval and build/TRX/recovery receipts. STA/UI tests alone
   without the real production-app live workflow do not satisfy UI proof.
8. New controls keyboard-accessible with automation names, explicit Approve/Reject,
   semantic Light/Dark/High Contrast styling; existing Chat/AI Check behavior and
   unrelated rendering preserved. Targeted+impacted regressions PASS.

## 9. Risks and decisions requiring owner approval

- **Scope decision:** approve I1 as an honest reviewed-project/literal-intent workflow,
  not natural-language autonomous editing of arbitrary repositories. Fixed graph and
  tests are a material product limitation. If arbitrary projects are required now,
  a separate verification-profile/authority design is necessary before implementation.
- **Workspace decision:** approve `D:\NEXUS\GateI-ReviewedWorkspace` and its reviewed
  Alpha/Beta task/graph for external engineering preparation, or name an existing
  compatible reviewed project. App must not auto-create/restore projects from UI.
- **Review timing:** approve hard120 s including human approval; UI warns via countdown
  and safely expires. If a longer human review phase is required, it needs separately
  reviewed protocol semantics rather than weakening the certified deadline.
- Native generation may fail even for exact literals. Surface failure with original
  typed outcome/receipts; no retries/top-up/fuzzy edits or token/deadline changes.
- Cancellation while approval is pending needs complete Task/Dispatcher cleanup to
  avoid hanging runtime drain. New integration tests must prove this before UI proof.
- New working-tree integration is not the old certified candidate. Gate H archives
  stay immutable; any future full certification needs a new freeze/explicit approval.

## 10. Proposed implementation sequence

After one owner approval of I1 scope/root/timing: **UNDERSTAND → RED TEST → MINIMAL
INTEGRATION → GREEN → IMPACTED REGRESSION → UI PROOF**. Reuse this audit and baseline.

1. Capture affected-file hashes/ownership delta; write RED admission, run binding,
   asynchronous approval expiry/cancellation and typed outcome tests.
2. Add thin workspace/approval/logger adapters; compose existing Agent/tool pipeline
   with admitted immutable profile and shared runtime; GREEN service integration.
3. Add child WPF panel and lifecycle links; GREEN control/command tests, then impacted
   regressions. No rendering/design-system cleanup.
4. Prepare approved reviewed project externally; perform actual WPF success,
   reject/failure/recovery/cancellation proof and seal truthful receipts/report.
5. Ordinary failures: observe → hypothesize → directed test/fix → verify; maximum two
   directed fix cycles/root cause, no speculative patches/retry-until-pass. No audit
   restart or approval per minor in-scope edit; request review for material boundary
   changes. Report completion, stop before any Gate J progression.

**FINAL STAGE 1: GATE I SCOPE READY FOR OWNER REVIEW.**
Approval requested: I1 reviewed-project scope, proposed workspace preparation/root,
one initial plan with existing recovery, and hard120 s including exact-plan review.
