# Gate I I1 — continuous recovery & UI review

13 Oktober 2026. **PHASE A / FUNCTIONAL GATE I1 PASS** pada approved reviewed-project
scope: actual WPF source edit, exact approval, build dan3 required tests telah
terbukti. **PHASE B layout proposal siap untuk satu owner approval. Gate J HOLD.**
Tidak ada arbitrary-project autonomous coding atau full Gate H recertification.

## 1. Proven MSB3883 mechanism

Baseline working checkpoint diverifikasi byte-identical:
`a9ffabffbbc3fd48ca92c50ae6a6a0bf8d41d836b107ea27fb2f2bd12c61c311`.
662 source/document files cocok sebelum edit; staged SHA tetap
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
Audit arsitektur/implementasi tidak diulang.

| Captured comparison | External PASS | Actual Nexus.App.exe FAIL |
|---|---|---|
| SDK | Same pinned8.0.424 `dotnet.exe`, SHA `76e6472063f53379b86fe8370203ab6c46c75d27f405b722845f0e495d8f5fd0` | Same |
| Arguments/CWD | Fixed reviewed build command; `D:\NEXUS\GateI-ReviewedWorkspace` | Same |
| TEMP/TMP | `%LOCALAPPDATA%\Temp` → shared MSBuildTemp | Same |
| User/owner/default DACL | Same user/owner/default token DACL; restricted SID count0 | Same |
| Integrity | Medium, `S-1-16-8192` | App and SDK child Low, `S-1-16-4096` |
| Executable label | SDK host medium launch | `Nexus.App.exe` has inherited Low mandatory label |
| Temp object protection | Shared MSBuildTemp has no Low label, therefore Medium | Low→Medium write denied by Windows MIC |

**Proven cause:** inherited executable Low integrity propagates to SDK. Windows
Mandatory Integrity Control applies no-write-up before DACL grants: the SDK cannot
create/write `.rsp` in shared Medium MSBuildTemp even though its user has Full Control.
The origin of the pre-existing inherited executable label was not changed; it is
not necessary to relax that boundary to fix verification.

Focused probes, all without generation/source mutation:
- Managed `Process.Start`, CreateNoWindow true/false, and existing verifier with
  graph/source locks PASS in Medium host; these do not explain the failure.
- Actual `Nexus.App.exe`, production DI, fixed verifier: FAIL before runtime and
  after runtime start, both Low children. Runtime/model inference is not causal.
- **Control using original managed launcher:** same Low token, SDK, arguments and
  CWD; change **only TEMP/TMP** to a private LocalLow directory → exit0/build PASS.
  This isolates the fix from the separately added Job lifecycle protection.

Captured token/ACL/`.rsp` events, exact arguments/environment and normalized delta:
`gate-i-i1-recovery/proven-cause-comparison.json`, raw probe JSON/logs and temporary
diagnostic source ZIP. Original failed I1 evidence is preserved.

## 2. Minimal host-owned fix / exact paths

Paths relative to `src/02_Modules/`:

| File | Change |
|---|---|
| `Nexus.AI.EditingLite/Services/SourceVerification.cs` | Per-invocation TEMP/TMP override; existing build/test commands execute through owned SDK Job; await root+Job drain before streams/temp retirement and source recovery. |
| **New** `Nexus.AI.EditingLite/Services/VerificationTempDirectory.cs` | Windows LocalLow Known Folder API; atomic unique directory with current-user/SYSTEM DACL, ownership marker/pinned directories, no reparse traversal, cleanup only that generated scope. |
| **New** `Nexus.AI.EditingLite/Services/OwnedSdkProcess.cs` | SDK lifecycle helper adapted from verified anonymous certification Job launcher: suspended root, assign+verify membership before resume, exactly3 stdio handles inherited, descendants inherit Job, TerminateJobObject cleanup. |
| **New** `Nexus.AI.EditingLite.Tests/Services/SdkVerificationEnvironmentTests.cs` |8 targeted environment/ownership/cleanup/argv regression cases. |

The SDK's process token remains Low. No elevation, global ACL/antivirus change,
global TEMP mutation, SDK replacement, new tool/executor, profile/target expansion,
command/fact change, deadline increase, repair enablement or approval bypass.
Existing120 s Agent and15 s build/test limits remain. Cancellation cleanup retains
the existing5 s bounded drain allowance; it does not refill execution time.

Temporary readonly diagnostic startup code was archived and removed. `App.xaml.cs`
retains only cleanup formatting/line-ending residue, no new functional startup/DI
path. The internal invocation probe remains test-only metadata observation.

Two directed implementation defects were fixed with retained failed evidence:
LocalLow's inherited NotContentIndexed attribute was normalized **only on the new
ownership marker**, keeping source validators unchanged; and Job accounting zero
was joined with the root process signalled fence, preventing early temp cleanup.
Three initialization-failure marker directories were retained as diagnostic data;
no SDK was started in those failures. Subsequent owned scopes retire successfully.

## 3. Targeted / impacted verification

| Verification | Actual result |
|---|---|
| Original Low WPF readonly SDK probe |2 build FAIL, retained RED mechanism evidence |
| Original managed launcher + private LocalLow control | Build PASS, still Low |
| Initial new regression |4 PASS/3 FAIL (marker attribute issue), retained |
| Marker-fixed targeted |7/7 PASS |
| Added foreign-Job regression | Exposed root-exit fence race; failed TRX retained |
| Focused root-exit/cancel regressions after fix |2/2 PASS |
| Final impacted SDK/environment/security/compiler/source-plan suite |**63/63 PASS**, 0 fail/skip |
| Impacted I1 approval/presentation/initial-plan/shared-runtime isolation |**25/25 PASS**, 0 fail/skip |

**88 unique completed impacted cases PASS** in this recovery. Earlier350 baseline
PASS evidence is historical and reused only for unaffected contracts; not described
as newly rerun. Development wrappers and all commands/TRX/logs are in evidence.
No full-solution certification was run.

## 4. Actual production WPF proof

Actual `Nexus.App.exe`, normal production DI and authorized native folder picker:
project outside reviewed root rejected; actual Alpha/Beta intent submitted; complete
exact diff/RunId/PlanId/digest displayed before mutation; explicit keyboard approval.

| Actual scenario | Receipt |
|---|---|
| Controlled Alpha→3 / Beta→2 |45.7482662 s; build PASS, recognized required assertion failure; both edits reverse-recovered, BaselineVerified=true, Gamma unchanged |
| Approved Alpha→2 / Beta→2 |**50.3231958 s**, Completed, verification `SourcePlanBuildTestsAndHashesVerified`; build PASS, **3/3 required TRX tests PASS**, exact final hashes, Recovery=NotNeeded |
| Concurrent temporary Chat + Agent | Both requests active; cancel Agent at1.0295911 s, Chat remains active, no source mutation; Chat subsequently finishes40 tips and `CHAT_COMPLETE` |

Success RunId `e2ee5298-cca0-40cb-8294-c601055ee299`;
PlanId `0235e0b4-f151-4048-8fe4-fdd3277de634`;
PlanDigest `f678fcce7481d99856e805bb2c2e1fdb43ba53b6dca73d7bf28e914f20e66d80`.
Both file operations have matching independent ApprovalId, CommitReached/Verified;
one initial plan, no automatic repair. Final source SHA-256:

| File | SHA-256 |
|---|---|
| Alpha.cs, now `return 2;` | `70053917c42c6f86be5732203e7b7e16e639635503c737da56d4b40b9f53c3af` |
| Beta.cs, now `return 2;` | `2164a22bbc2cb8f2c2976a14baba67c232ec602b689b75a8f492bb50b0fc61ed` |
| Gamma.cs, unchanged | `7d8b643b01f30746643967d41ecf8f7ce6a39573a372d7f0b92e2f771a91a8d4` |

Actual proof/PNG screenshots: `actual-approved-source-success/`; original/final
required TRX and source/backup hashes are sealed. `phase-a-reconciliation.json`
checks actual build/tests/final hashes against actual UI receipt.

Concurrent proof: `actual-production-ui-recovery/`, including both-active and
cancelled-agent/active-Chat receipts plus final Chat screenshot. The first external
collector omitted FlowDocument text from its tree Name dump and incorrectly failed
its marker check; screenshot visibly contains CHAT_COMPLETE and completed actions.
Collector fixed to use TextPattern; **no repeat generation to recreate that report**.
`concurrent-chat-reconciliation.json` retains the original failure and clarifies
the evidence. Requests overlap, not a claim of simultaneous GPU inference.

Runner initially emitted a model-count placeholder0 for UI mode; correction is
explicit in reconciliation. Authoritative actions: one Chat generation, cancelled
Agent model turn0, one controlled-failure native turn, two successful native turns.
Future UI invocation model count is null until reconciled, not a fabricated zero.

Reject/cancel/expiry/invalid grant/OutputLimit safety evidence from I1 remains
visible and valid; changed SDK cancellation/recovery is covered freshly. Complete
invocations finish with empty verified outer Job membership after owned cleanup;
SDK Job tests verify root/descendant drain and another independently owned Job survives.
No name/historical-PID cleanup or foreign-process termination.

## 5. Phase B — concise layout proposal, awaiting approval

Recommendation: **existing WPF Grid + GridSplitter + Nexus semantic controls**.
No package, docking framework, editor, diff engine or renderer replacement.

```text
Agent (.cs) — Reviewed I1                 [Close]
┌──────────────────────────┬─┬────────────────────────────────────┐
│ Workspace / select       │ │ Allowlisted files: Alpha | Beta    │
│ Reviewed scope badge     │ │                                    │
│ Coding instruction       │ │ Exact diff — selectable/full       │
│                          │ │ independent scrolling, mono text   │
│ Compact Alpha Old / New  │ │                                    │
│ Compact Beta  Old / New  │ │ Plan/root/digest/limits details     │
│                          │ │ [Approve actual plan] [Reject]     │
│ [Run] [Cancel]            │ │                                    │
│ Phase / countdown / error│ │ Build / 3 tests / recovery receipt │
└──────────────────────────┴─┴────────────────────────────────────┘
```

Desktop: roughly36% request /64% review; min left320/right400, splitter8, semantic
padding/borders. Independent pane scrolling; approval and countdown remain easy to
find. Below760 px **available panel width**, use Request/Review tabs with the same
bindings, rather than overflowing or a very long unified form. Initial workspace,
no-plan and no-result empty states explain the next action. Approval buttons still
depend only on valid actual pending plan; presentation never mints authority.

**Exact expected files after one layout approval** (existing only):
- `src/03_App/Nexus.App/Views/AgentWorkspaceView.xaml` — Grid/panes/empty states,
  full diff viewport, pinned review actions, semantic spacing.
- `.xaml.cs` — width adaptation and focus glue, no execution logic.
- `src/03_App/Nexus.App/ViewModels/AgentWorkspaceViewModel.cs` — presentation-only
  state/summary properties if needed; preserve run/approval/CTS semantics.
- `src/03_App/Nexus.Integration.Tests/GateIAgentWorkspaceUiTests.cs` — widths,
  focus/splitter, complete diff, pending-plan actions and three themes.

No new production files expected. Chat/AI Check views, renderer, shared design system,
Agent/SDK/profile/approval/recovery code are outside this layout change.

| Candidate | Targeted assessment / decision |
|---|---|
| Existing Nexus controls/diff | Existing .NET8 WPF, semantic themes, focus/automation checks and exact-plan text. **USE/ENHANCE**: hierarchy/viewport solves the observed cramped120 px diff without new parsing or mutation code. Lowest cost. |
| Microsoft WPF GridSplitter | Existing WindowsDesktop library, supported native resizing/focus states; style with Nexus resources and explicit Focusable/automation name/keyboard steps. No package/supply-chain addition. **USE/ADAPT**; low cost. |
| ICSharpCode/AvalonEdit | MIT; mature ILSpy/SharpDevelop usage and CI; current README targets net6.0-windows/net462, usable by a net8 WPF consumer. Syntax/highlighting/read-only viewing possible, but new dependency, theme/selection/automation testing and adaptation cost medium. Bounded exact diff already adequate once viewport improves: **NOT NEEDED now**. Not a diff engine or security authority. |
| Dirkster99/AvalonDock | **Ms-PL**, not MIT; mature community/CI, current v5 README targets net9/net10/net48, not net8. Docking/floating/layout serialization adds focus, lifetime, theme/a11y and persistence complexity; older compatible versions would need a separate selection review. **REJECT for I1 polish**: two resizable panes do not need docking. High cost, unnecessary new surface. |
| lepoco/WpfUI | MIT; active community/CI/gallery; current project explicitly targets net8.0-windows. Fluent controls/themes require extra packages/resources and duplicate existing Nexus theme/snackbar/dialog patterns. A11y/HC and dynamic resource behavior would require fresh validation; medium/high integration cost. **NOT NEEDED**; no theme replacement. |

Security evaluation is scoped to proposed integration: no candidate grants source
authority; avoid layout/plugin/serializer/editor features unrelated to I1. No claim
of a full vulnerability audit and no third-party dependency installed.

Reviewed sources: official [MIC](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control),
[Known Folders](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid),
[GridSplitter](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/gridsplitter),
[AvalonEdit README](https://github.com/icsharpcode/AvalonEdit/blob/master/README.md),
[AvalonDock README/license](https://github.com/Dirkster99/AvalonDock),
[WpfUI README/project](https://github.com/lepoco/wpfui).

After approval: targeted RED layout tests → minimum XAML integration → GREEN +
affected UI regression → actual screenshots at900/1280/1440 widths in Light/Dark/HC,
keyboard focus/splitter/selectable complete diff and correct pending/terminal/error
states. Reuse functional receipts where execution identity/contracts permit; do not
generate solely for screenshots/report. **Layout is proposed, not implemented yet.**

## 6. Preservation / seal

Gate H certified archive remains immutable, SHA
`2039be89ee34e82cfa38c20471ea68bf742fb09dd08ccdbb27ef79665737f822`;
prior I1 checkpoint/evidence/reports retained. Staged patch and baseline user status
entries preserved, no destructive Git operations. Source changes are restricted to
the SDK environment/lifecycle/regressions and temporary diagnostic cleanup above.
Authority/profile/runtime/approval/Chat/AI Check/OCR/UI layout source hashes match
the baseline except the stated formatting residue. No full certification or P1 replay.

Evidence base: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-i1-recovery\`.
Working source/identity and evidence ZIPs:
`nexus-gate-i-i1-recovery-working-20261013.zip`,
`nexus-gate-i-i1-recovery-evidence-20261013.zip` under the same Temp\opencode parent.
Per-file before/after source hashes, current binaries, original/actual workspace,
raw FAIL/PASS/TRX/PNG/tooling identities and final archive/report SHA are in
`recovery-seal.json`. New checkpoint is a recovery working checkpoint, not Gate H
recertification. No circular archive hashes.

## 7. Status / remaining decisions

**Gate I1 functional acceptance PASS for this actual approved workflow.**
Source authority remains reviewed Alpha/Beta with exact user literals, one approval,
one plan, existing verification/recovery. The completed workspace now contains
approved final hashes; its original admission baseline is not silently refreshed.
Further tasks on changed sources require a new reviewed baseline, as before.

P1 stochastic OutputLimit remains its original Option B diagnostic limitation.
Arbitrary-project coding and a full high-contrast/responsive acceptance of the
**proposed new layout** are not claimed.

Owner decision requested: approve the Phase B two-panel Grid/GridSplitter layout
and exact four-file presentation/test scope in §5. Then implement directly without
repeating this investigation. **Gate J does not start automatically.**
