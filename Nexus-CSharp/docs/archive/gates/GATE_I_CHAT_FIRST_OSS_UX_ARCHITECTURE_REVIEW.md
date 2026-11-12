# Gate I — chat-first OSS UX architecture review

13 Oktober 2026. **DESIGN READY FOR ONE OWNER SCOPE REVIEW.**
Rekomendasi: pertahankan percakapan/composer Nexus sebagai pengalaman utama;
letakkan **Pilih proyek tepat di atas composer**, ungkapkan literal intent I1 hanya
ketika aksi ubah kode dipilih, dan gunakan panel review kontekstual yang tertutup
secara default. Reuse pipeline/renderer existing; bukan Agent/editor/runtime baru.
Gate H certified dan Gate I1 functional PASS tetap baseline; **Phase B belum PASS**.

## 1. Recovery interrupted work dan screenshot-based defects

AGENTS/policy dan laporan I1 dibaca; current tree direkonsiliasi dengan recovery
checkpoint SHA `30481818f87008f6b6e3d1f76e9df59b1d2a2224b820d5ab8caa47525f014405`.
Hanya empat file Phase B yang berbeda dari666-file baseline; patch dipertahankan.
Staged SHA tetap `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`;
certified Gate H ZIP tetap `2039be89ee34e82cfa38c20471ea68bf742fb09dd08ccdbb27ef79665737f822`.
Snapshot/hash/status lengkap: external `gate-i-chat-first-review/audit-baseline.json`.

| Interrupted Phase B | Actual evidence / disposition |
|---|---|
| View XAML/code-behind | Dua pane, splitter, compact tabs <760 DIP, single control instances, full selectable diff, empty states sudah ada. Footer approval kemudian dipindah keluar review scroll; tab underscore diperbaiki. Preserve, tidak restart. |
| Agent VM | Presentation state/DiffText/PlanDetails, allowlisted files, summaries, late-UI guard dan existing pending approval binding ada. Preserve execution contract. |
| UI tests | RED6 layout cases; GREEN13 awal, GREEN15 setelah pending/expiry tests dan immediate action-disable fix. Final affected suite **169 PASS/1 FAIL**. |
| Final failure | `GateIAgentWorkspaceTests.Admission_RequiresHostPinnedManifest_NotJustFolderSelection`: Alpha hash berubah setelah successful I1 task. Host **correctly denies** original baseline. Test depends on mutable real engineering workspace and needs isolation; bukan alasan reset files atau weaken admission. |
| Visual evidence | External visual host menjalankan actual Nexus App/MainWindow/production DI,9 width/theme combinations ×4 display states, fixture pending provider dan historical receipt replay, zero generation/mutation. Matrix mendahului final pinned-footer patch; final visuals belum lengkap. **Tidak klaim Phase B PASS.** |

Screenshot yang benar-benar diperiksa: `gate-i-phase-b/actual-visual-matrix-1/`
`empty-Light-1280.png`, `pending-ui-only-Light-1280.png`,
`pending-ui-only-Dark-1440.png`, `pending-ui-only-HighContrast-900.png`,
`terminal-receipt-replay-Light-1280.png`; actual functional screenshots juga dibaca
dari `gate-i-i1-recovery/actual-approved-source-success/`.

**UX defects:** empty review menghabiskan ±64% layar, mandatory four literal boxes
muncul sebelum user menyatakan ingin edit, pilihan proyek jauh dari Chat composer,
overlay menutup percakapan, jargon mixed-language/profile/receipts dominan.
**Missing integration:** project/draft binding lintas conversation, explicit routing
composer ke bounded edit, file context yang benar-benar disertakan, activity/result
di conversation, close/reopen panel tanpa implicit cancellation.
**Certified constraints:** reviewed graph/two files/exact literals/one approval,
120 s including review, source hash drift default-deny. Ini bukan UX defects yang
boleh dihilangkan lewat polesan UI.

## 2. Actual ownership / user-journey map

Abbreviation: `App/`=`src/03_App/Nexus.App/`,
`IT/`=`src/03_App/Nexus.Integration.Tests/`.

| Existing owner / code path | Finding / reuse |
|---|---|
| `App/ViewModels/MainViewModel.cs:ExecuteNavigationAsync/TransitionLifecycleAsync`; `MainWindow.xaml` | AIWorkspace routes to singleton Conversation VM and module lifecycle. Keep navigation/runtime owner unchanged. |
| `App/Views/ConversationView.xaml`: `AIChatItems`, `AIWorkspaceMain`, composer `TxtAIInput`, header Chat/AI Check/model | Familiar chat, streamed renderer and one existing composer already exist. Current AgentWorkspaceView is opaque `Panel.ZIndex=850`, Grid.Row1/ColumnSpan2 overlay; this explains the separate-form experience. |
| `ConversationView.xaml.cs:TxtAIInput_PreviewKeyDown`, drag/drop/focus/follow-tail | Enter→existing SendCommand, Shift+Enter newline; preserve these contracts and route explicit task kind, not natural-language heuristics. |
| `ConversationViewModel.cs:InputText/Attachments/SendCommand/StartGeneration/RunGenerationAsync` | Chat owns input/history/payload/rendering. Agent has a different Prompt and RunCommand. Selecting a project currently does not enrich Chat with file content. |
| `ConversationViewModel.ExecuteNewConversationAsync/OpenConversationAsync` | Chat history opens/rebuilds; no project-keyed drafts or Agent invalidation here. Navigation-away does cancel/drain Agent. Integration must cover session change as well as navigation without importing history into Agent. |
| `AgentWorkspaceViewModel.SelectWorkspaceAsync/RunAsync/Decide/CloseCommand` | Selection/snapshots, fresh Agent session, literal intents and exact prepared plan already available. Close currently cancel+drains; contextual **hide** needs a distinct presentation action. |
| `App/Services/AgentWorkspaceWorkflow.Select/RunAsync` | Pinned manifest+SDK+graph/source hashes, authorized snapshots and run-local existing Agent graph. Context sent to edit Agent is identities + exact supplied literals, not a general repository reader. |
| `AgentWorkspaceApprovalProvider.BindRun/RequestPlanAsync/Decide`; existing editing services | Actual plan/profile/run/digest one-shot approval and guarded SafeWrite/verification/recovery. Reuse intact. Never infer grants from card text, selected tab or a model's response. |
| `ConversationRuntimeWorkflow` / `SharedRuntimeAgent` | Same runtime; Agent/Chat CTS isolated; model/navigation drain lease. Keep existing120 s Agent/15 s SDK limits and owned Job cleanup. |

Renderer/design-system files, SDK verification fix, policy, SafeWrite and source-plan
contracts are preserved user/baseline work. Current audit changes only this document;
no tests/model/full certification rerun, no application edits.

## 3. Deep OSS UX comparison — actual source and interaction evidence

All source snapshots are pinned, hashed and stored externally. Published images/demos
were viewed and source/state/interaction tests read; these applications were **not
installed/executed** and their tests were not rerun. Images show their documented
interaction, not new Nexus execution proof. Links below point to audited commits.

### OpenCode — project/session/composer proximity, scoped drafts, contextual review

Snapshot [`4ffede4`](https://github.com/anomalyco/opencode/tree/4ffede49416c18302bbccf8517c68db427d71844), MIT, active dev branch/CI.
Actual desktop/web UI is **Solid**, with separate desktop host; not a native WPF control.

- `packages/app/src/new-session/view.tsx:NewSessionView` composes Composer and
  project/workspace selectors in the same centered region. At this snapshot project
  controls are **below** Composer; adapt proximity, while Nexus requirement places
  selector **above**. `new-session/project/selector.tsx` handles selection/dialog/focus.
- `composer/composer.tsx:Composer/ComposerModelControl` keeps editor/model choices
  together, uses mobile drawer vs desktop popover and restores composer focus.
- `composer/persistence.tsx:scopeKey/selectPromptTab` keys draft by draftID or
  directory+session, bounded cache20; context/cursor/model state stays with that scope.
- `session/screen-layout.ts:createSessionScreenLayout` yields100% conversation
  width when no side panel is open; `session/screen.tsx` preserves mobile/desktop
  region state rather than replacing conversation with a setup form.
- `session/requests/session-permission-dock.tsx` renders a pending request with
  resources/responding state. `e2e/regression/session-request-docks.spec.ts` verifies
  minimize/restore without rejection and caret restoration after request closes.
  `packages/session-ui/src/v2/components/session-review-v2.tsx` exposes selected file,
  diff style, empty states, sidebar/filter/accessibility independently of backend.
- Viewed `packages/app/e2e/screenshots/session-project-menu.png`: session tabs,
  project identity and path menu. Story/source tests supply composer/review interaction
  evidence; landing screenshot is TUI marketing, **not** desktop evidence.

**ADAPT** nearby project control, session-keyed draft/focus, contextual width and
request identity. **REJECT** allow-always, shell/worktree/extensions capabilities and
composer replacement during permission waits for Nexus's independent Chat path.
Direct Solid/Tauri/runtime transplant **NOT NEEDED**.

### Cline — conversation-first tasks, explicit mode, anchored approval

Snapshot [`1a5c3f8`](https://github.com/cline/cline/tree/1a5c3f84e0933268a9dba14a55a6383a6578414d), Apache-2.0, active.
Audited VS Code React webview; current desktop uses a Tauri/Bun/Next stack, not WPF.

- `apps/vscode/webview-ui/src/components/chat/ChatView.tsx` composes task/messages,
  input and actions. `chat-view/components/layout/InputSection.tsx` owns draft,
  selected attachments/quote, authoritative turn state and submit routing.
- `ChatTextArea.tsx:onModeToggle/handleKeyDown` renders Plan/Act near the input,
  handles draft action/focus and IME-safe Enter. Mode change is explicit, not a
  guessed permission from wording. Nexus will use **Tanya/Ubah kode terbatas**, not
  imply its I1 can explore/build arbitrary projects like Cline Plan/Act.
- `chat-view/components/layout/ActionButtons.tsx` anchors buttons to backend
  TurnState/ask identity and latches a processed ask immediately; prevents double
  submission while trailing bookkeeping renders. Good UX pattern; Nexus provider
  remains authoritative rather than importing Cline's grant protocol.
- `DiffEditRow.tsx:FileBlock` expands per-file diff/stats, uses actual backend line
  positions, and follows streaming only while user stays at bottom. `ChatRow.tsx`
  renders tool events within conversation instead of a separate mandatory form.
- Viewed official `assets/docs/demo.gif` (LFS media resolved), frames64/225/387/548:
  chat tool narrative, inline file-read approval, command output, repair and completion.
  `docs/core-workflows/checkpoints.mdx` documents conversation-linked compare/restore
  with a **shadow Git repository**. That is not Nexus recovery semantics.

**ADAPT/ENHANCE** conversation task/status cards, expandable details, explicit action
mode and disabled-after-decision UX. **REJECT** auto-approve, terminal execution,
shadow-Git restore, post-edit acceptance as a substitute for Nexus prewrite approval.
Direct React/IDE/SDK reuse **NOT NEEDED**.

### OpenHands Agent Canvas — chat dominant, right panel session state, compact route

Snapshot [`2bdc0e9`](https://github.com/OpenHands/OpenHands/tree/2bdc0e9d6c5940bc3d4667fb2127fbb753b2f7c8), MIT for audited Canvas repo,
active React/Zustand UI. No React/sandbox/runtime transplant into WPF.

- `src/components/features/conversation/conversation-main/conversation-main.tsx`
  renders chat at100% when right panel is hidden, resizable side panel only when
  shown; compact uses `conversation-mobile-panel-page.tsx` and separate panel route.
- `src/stores/conversation-store.ts` explicitly defaults right panel closed after
  restart but preserves in-app hide/show; comments identify persisted sticky panels
  as unwanted UX. Panel visibility is distinct from task execution state.
- `chat/custom-chat-input.tsx` delegates submission, grip/focus/file handling and
  draft hooks; stable onSubmitRef avoids duplicate send. `hooks/chat/use-chat-input-logic.ts`
  supplies draft restore/save. `workspace-mode-selector.tsx` expresses capabilities.
- `conversation-tabs/conversation-tabs.tsx`, `files-tab/no-file-selected-message.tsx`
  and `tool-visualizers/file-editor/file-editor.tsx` separate files/review/work area
  from conversation; `conversation-tab-empty-state.tsx` explains empty context.
- `shared/buttons/conversation-confirmation-buttons.tsx` binds confirmation to
  awaiting agent state/conversation, tracks submitted event IDs, disables repeated
  keyboard decisions; API accept bool is **not** Nexus's digest-bound plan grant.
- Viewed `.github/pr-assets/issue-16505-02-after-desktop.png` and `04-after-mobile.png`:
  conversational welcome/composer, Open Workspace nearby and readable LLM setup state;
  `.pr/screenshots/02-ui-in-indonesian.png` confirms localized real settings/sidebar.
  Onboarding image `pr-16975-agent-canvas-loopback.png` was inspected, not mistaken
  for a full conversation/review screenshot.

**ADAPT** default-closed contextual panel, compact drawer/tab, draft restore and
readable capability/error states. **REJECT** arbitrary workspace file discovery,
editor/terminal/Git/plugin/remote-agent surfaces and relaxed event-only confirmation.

### Additional references — targeted, with limitations

| Project | Actual components / viewed interaction | License/maintenance/compatibility and decision |
|---|---|---|
| Goose [`69de89c`](https://github.com/aaif-goose/goose/tree/69de89c50cc01d4e0187d6645bf3618729873f74) | `ui/desktop/src/components/ChatInput.tsx`: model+working directory bottom bar, hides secondary controls on narrow width, central draft update; `ToolCallConfirmation.tsx`: friendly tool name, arguments, session/generation/request identity; `acp/chatSessionStore.ts` session state. Viewed2024 `documentation/blog/2024-12-06-previewing-goose-v10-beta/goose-gui.png`, clearly **historical** composer/working-dir image. | Apache-2.0, active Rust/Electron project. **ADAPT** concise project-name/context bar and human-readable action confirmation; **REJECT** tool/extension permission breadth. No Electron code/runtime reuse. |
| Zed [`26363aa`](https://github.com/zed-industries/zed/tree/26363aabdb31a0c016728634202e1ad5067033aa) | `crates/agent_ui/src/message_editor.rs` explicit context/mentions; `agent_panel.rs` thread actions; `conversation_view/thread_view.rs` renders Edits/file stats + Review Changes + Keep/Reject actions. Official `zed.dev/agentic` review poster viewed: conversation and multi-file diff alongside composer. `docs/src/ai/agent-panel.md` explains capability-dependent profiles/context and review. | Agent UI crate GPL-3.0-or-later (`LICENSE-GPL`); marked components elsewhere Apache-2.0. Active Rust/GPUI, not WPF. **ADAPT concepts only**: contextual changed-files summary and honest unsupported-tool status. **REJECT direct GPL code**, post-edit/hunk approval semantics, broad context search/worktrees/terminal. |
| Continue [`5522c6f`](https://github.com/continuedev/continue/tree/5522c6f44ca0ac3528b37244818fbfa39b5af470) | `gui/src/pages/gui/Chat.tsx`, `mainInput/ContinueInputBox.tsx` uses inputId, separate chat/edit historyKey and filtered context providers; `InputToolbar.tsx` varies visible controls/capability. Viewed historical `docs/images/continue-screenshot.png`: chat input/context entry alongside editor. | Apache-2.0; used **historically/read-only per owner instruction**, no development integration. GitHub snapshot reports archived=false, so read-only here is audit scope, not an invented archival fact. **ADAPT** explicit context chips/mode-specific affordances; **REJECT** broad indexing and TipTap/React transplant. |
| Roo Code [`b867ec9`](https://github.com/RooCodeInc/Roo-Code/tree/b867ec9145750d0ae1ff7f02d35406e9bf2a0b16) | `webview-ui/src/components/chat/ChatTextArea.tsx`: ModeSelector beside model/input, send/stop/queue; `ChatView.tsx`; `CheckpointRestoreDialog.tsx`. Viewed `apps/docs/static/img/using-modes/using-modes.png`, read chat-interface docs. | Apache-2.0; repo archived=true; README states extension shutdown May15. **Historical UX only**. **ADAPT** understandable explicit task choice, not its full Code/Architect/Debug/Orchestrator powers. **REJECT** continued dependency or checkpoint restore backend. |

**USE** existing Nexus + Microsoft WPF controls. **ENHANCE** native orchestration/
presentation from these UX concepts. No new UI library is needed, no OSS execution
engine copied, no GPL code transplantation or new runtime/dependency installed.

## 4. Recommended native architecture — one composer, distinct authority channels

Keep `ConversationView` the AI Workspace shell. Its existing Chat/AI Check module
selection and model picker remain; add separate **composer task kind** (Tanya /
Ubah kode terbatas), not a third value encoded as `!IsChatModeActive`.

```text
ConversationView / ConversationViewModel
  existing Chat messages, renderer, history and streaming
  selected-project display + capability status + session/project draft key
  existing composer: explicit task routing
    Tanya → existing Chat SendCommand/payload/runtime
    Jelaskan kode → explicit authorized snapshot/attachment → Chat, no mutation
    Ubah kode → progressive literal intent → existing AgentWorkspace VM/workflow
  latest host-owned Agent activity/result card (not model text, not Chat history)
  contextual AgentWorkspace review view: files/diff/approval/technical receipt
```

No parallel composer or new timeline engine. First bounded slice shows one latest
run/activity card in the conversation area; keep existing Chat message collection/
renderer and persistence unchanged. Agent lifecycle events project onto that card.
General persistent multi-run task timelines are later scope, not assumed available.

Project selection has **two separate states**: chosen display location and host
admission result. Unreviewed folder gets a readable limited status without directory
scan/read/write. “Jelaskan kode” requires actual explicit attachment or revalidated
authorized Alpha/Beta snapshot; selected project/path alone is never file content.

## 5. User journey / responsive specification and examples

**Initial:** existing conversation welcome, no Alpha/Beta form and no empty half-width
panel. Project row directly above composer: `[Pilih proyek ▾]`, capability hint;
placeholder **“Tanyakan sesuatu...”**. Model picker stays familiar. Normal Chat and
AI Check continue their existing routes.

**Select:** show leaf project name, full path on accessible tooltip/technical details,
and honest status: “Chat tersedia” / “Perubahan kode terbatas tersedia” /
“Proyek berubah; perlu ditinjau ulang sebelum mengubah kode”. Ordinary questions
remain usable even when admission fails; do not show fake Agent activity.

**Explain example:** select authorized Alpha → “Jelaskan kode” → explicit context
chip shows file/hash/source provenance and validated current snapshot actually enters
the Chat payload. Missing/unreviewed context: “Pilih atau lampirkan file yang boleh
dibaca”. Never answer as if repository content was read when only path is known.

**Edit example:** user selects **Ubah kode** and types “Ubah nilai hasil menjadi2”.
Admission must succeed. Only then reveal compact “Teks yang dicari” / “Teks pengganti”
for actual Alpha/Beta targets. Explain “Versi I1 memerlukan perubahan teks eksplisit
pada dua file”. Do not infer executable edits from Chat output/free-form request.
User confirms literals and starts the existing bounded workflow; no plan/write yet
from merely choosing a mode or project.

**Pending:** card “2 file siap ditinjau — belum ada file diubah” + **Tinjau perubahan**.
Open contextual panel with all affected files, exact full selectable diffs, concise
summary, **Setujui perubahan / Tolak perubahan**, readable expiration. IDs/digests/
hashes remain complete inside **Detail teknis**. Closing/minimizing panel only hides
presentation, not cancellation/decision; reopen shows the same run/plan/deadline.
Expiry disables decisions while hidden too; a pending badge/countdown stays reachable.

**After:** card uses actual typed facts: “Perubahan berhasil diverifikasi” only after
existing source/build/3-test verification; “Perubahan dibatalkan dan file dipulihkan”
only when recovery/baseline proof says restored. RecoveryConflict instead says file
changed externally and was preserved; OutputLimit says response was incomplete and
task failed. “Lihat hasil” opens real build/test/recovery summary + technical receipt.

```text
Wide: conversation (dominant)                 Review (only when relevant/open)
  chat / latest Agent activity                 [File] [Perubahan] [Hasil]
  …                                            exact diff + expiration/actions
  [Pilih proyek ▾] [capability hint]            [Detail teknis ▾]
  [Tanya] [Ubah kode]  ← explicit task choice
  existing composer / send / task-specific cancel
```

Wide default conversation100%, optional right review around35–40%, native splitter.
Use available content width, not raw window width. Below760 DIP: Chat primary,
**Tinjau perubahan (2)** tab/drawer opens the same review instance; composer project
row remains associated with input. No blank review pane, no duplicate controls or
loss of plan/draft/focus on resize. Verify900/1280/1440 windows, compact640/759 content,
and125/150% DPI. Escape hides panel and returns focus; it never implicitly grants.

## 6. Supported I1 vs future general-project functionality

| Within approved I1 / proposed UI links | Not available / not part of this scope |
|---|---|
| Ordinary Chat; explicit supported attachments; display selected location | Recursive project reads/indexing/repository understanding from a selected folder |
| Revalidated content from the same existing two authorized snapshots | Reading arbitrary paths or model-generated file lists |
| Explicit Alpha/Beta literals, one source plan/approval, fixed SDK/tests/recovery | Natural-language autonomous multi-project coding, arbitrary `.sln` verification |
| Host-fact latest activity/review/result in Chat-first shell | Shell/terminal/Git/MCP/auto-approval, general checkpoint restore or new editor |

The engineering workspace now has approved Alpha/Beta final hashes from the earlier
success. Original manifest still pins pre-task baseline; next admission correctly
denies drift. **Do not refresh manifest, reset source or broaden profile silently**.
New live edit baseline needs owner review separately. Chat-first UI can still be
tested without generation using prepared fixture plans and read-only receipt replay.

## 7. Exact anticipated ownership / bounded migration

Recommend one new **CF1 scope approval** for these existing files:

| Repo-relative file | Exact purpose |
|---|---|
| `App/Views/ConversationView.xaml` | Project row above the existing composer; explicit task controls; latest host activity card; contextual review host, replace Agent overlay composition. |
| `App/Views/ConversationView.xaml.cs` | Focus/Enter route and responsive host glue; preserve follow-tail, attachments and Chat edit/regenerate handlers. |
| `App/ViewModels/ConversationViewModel.cs` | Explicit task routing, bounded in-memory session/project drafts, selected-project display/admission distinction, lifecycle invalidation and activity presentation. No implicit history to Agent. |
| `App/Views/AgentWorkspaceView.xaml` | Reuse diff/plan/result/pinned actions; progressive intent presentation and Indonesian labels; remove permanent standalone-form ownership. |
| `App/Views/AgentWorkspaceView.xaml.cs` | Panel resize/close/reopen/focus glue, preserve control instances. |
| `App/ViewModels/AgentWorkspaceViewModel.cs` | Expose immutable selected-context/task presentation as needed; separate Hide from explicit Cancel; friendly typed status mapping, keep exact approval identity and deadlines. |
| `App/Services/AgentWorkspaceWorkflow.cs` | Thin scoped context revalidation bridge if needed, using existing Select/ReadScopedSource for the same targets; no policy/catalog/profile expansion or new executor. |
| `IT/GateIAgentWorkspaceUiTests.cs` | Revised chat-first/progressive panel/focus/width/theme/pending-state scenarios; preserve useful Phase B tests. |
| `IT/ConversationViewModelTests.cs` | Composer routing, Chat/AI Check/history/attachments/draft preservation and no implicit Agent context. |
| `IT/GateIAgentWorkspaceTests.cs` | Remove mutable shared-workspace test dependency through isolated prepared fixtures/explicit source-state checks; retain positive admission and drift-denial evidence, never weaken production manifest. |

**One new test file:** `IT/GateIChatFirstWorkspaceTests.cs` for session/project switches,
hide/reopen/expiry, explicit content and host activity isolation. No new production
framework/class/file expected; keep small presentation records within existing VMs.
`App/`/`IT/` abbreviations follow §2, all under `src/03_App/`.

Reuse the current four-file patches, not a reset: keep full diff template, immediate
CanDecide action-disable, compact single-instance switching and accessibility.
No changes to App DI/Main navigation/runtime/renderer/design-system/OCR/SDK fix/
approval provider/SafeWrite/source contracts. If that proves insufficient, explain
additional path/need before expanding ownership.

After approval, bounded slices: **S1** composer/project/progressive intent + draft
tests; **S2** contextual review/latest activity + hide/reopen/exact approval tests;
**S3** explicit scoped file-context bridge + final UI/affected regression. Each
RED→minimal integration→GREEN, then actual WPF proof; no new architecture audit.

## 8. Security impact / state transitions

- Keep immutable binding tuple: Chat session/draft identity, canonical project,
  selection version, Agent SessionId/RunId, profile/workspace identity and actual
  plan/digest. Display labels and cached receipts never become grants.
- Ordinary Chat payload follows existing contract. Agent receives only the current
  explicit request/validated literals/scoped data; never Chat history/attachments.
- Switching project/session saves drafts, invalidates pending actions, and cancels/
  awaits existing Agent/recovery before rebinding. Ordinary Chat CTS is distinct;
  a late response/card remains tagged to its original session, not the newly selected
  project. Closing only a review panel does not revoke/recreate a valid run.
- No pause/refill of120 s when panel hides; SDK15 s and Job ownership remain unchanged.
  Approve routes only to existing exact pending provider; Enter-to-send cannot grant.
- Context snapshot content is explicit, bounded and revalidated; reject missing,
  stale/reparse/outside-root requests before read/send. File labels without content
  produce no claim of file-specific knowledge. Gamma remains protected/verified,
  not an extra browsable authorized target.
- Host outcome/build/TRX/recovery facts determine success text; model prose never
  changes task state. Full raw error/evidence stays in technical details; recovered
  baseline and RecoveryConflict are different user messages.

## 9. Focused acceptance / test plan

1. Fresh AI Workspace: Chat/composer visible, no mandatory literals/empty half panel,
   project selector immediately above input; Chat/AI Check/model behavior unchanged.
2. ProjectA/B switching preserves keyed text/literals/context and caret; no draft/
   file data/history leak across normal/temporary conversations. Unsupported project
   permits ordinary Chat but clearly denies edit/read-specific capabilities.
3. File explanation asserts actual allowed snapshot/explicit attachment enters
   payload; stale/missing/outside/reparse/root-drift cases fail closed. No fabricated
   explanation from project name; no directory scan/general read tool.
4. Explicit Ubah kode progressively reveals literals; request reaches existing Agent;
   all diffs shown before write, same RunId/plan/digest, one actual approval only.
5. Hide/reopen, narrow/wide switch, user tabs, theme changes and focus do not change
   plan/expiry. Expired, rejected, cancelled and stale UI cannot approve; user typing
   “setujui” in Chat and pressing Enter never authorizes a write.
6. Friendly conversation status exactly matches real build/3 tests/recovery facts;
   OutputLimit stays failure, recovered vs conflict differentiated, technical data
   complete/selectable. Header badge accessible while panel hidden.
7. Keyboard tab order, IME/Shift+Enter, focus restoration, announcements via WPF UIA
   (`AutomationProperties.LiveSetting`/existing accessibility resources), complete
   diff selection, splitter,900/1280/1440 and compact widths/three themes/DPI.
8. Preserve Agent/Chat cancellation isolation, navigation/model drain and default
   deny. Use existing successful I1 backend proof for unchanged execution; rerun only
   affected UI/router/context tests, not all350 or full Gate H certification.

Actual visual proof may use production App/MainWindow with clearly labelled
prepared UI-only plans/receipt replay and zero generation/mutation, as before.
Do not label replay as fresh functional execution. A fresh live source-edit proof,
if needed after context/routing changes, requires a valid reviewed baseline and
separate identity reconciliation, not a hidden reset/retry-until-pass.

## 10. Owner approval boundary / evidence and decisions

Requested **one CF1 approval**: chat-first composer/project restructuring, progressive
I1 intent, contextual panel/latest activity, Indonesian primary labels, session/project
draft/focus preservation and bounded explicit source-context bridge, on the ten
existing files + one test file in §7. This exceeds previous four layout files.

No application implementation in this audit. No test/generation/full certification
rerun; original interrupted patch and FAIL/PASS/visual evidence preserved. Remaining
Phase B admission test isolation and final footer visual verification are tracked,
not dismissed or converted to PASS. Gate H/I1 reports remain historical evidence.

External evidence folder:
`C:\Users\Syand\AppData\Local\Temp\opencode\gate-i-chat-first-review\` contains
baseline/status hashes, pinned repo metadata/trees, actual source snapshots/licenses,
published images, Cline GIF/keyframes and provenance manifest. Continue/Roo were read
only. Pins/source hashes and final preservation checks are sealed with the report;
fetched main-branch metadata does not imply executing those applications.

**DESIGN READY FOR OWNER REVIEW — await CF1 scope approval, then implement surgically.
Do not advance Gate J automatically.**
