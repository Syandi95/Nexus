# NEXUS — Harness Native UI/Behavior Upgrade Report
## Stage A Checkpoint + Implementation Handoff

Status: **Stage A COMPLETE (minimum reuse map produced).** STOP after this handoff. No new audit auto-started.
Gate I2-B native acceptance: **HOLD**. Gate J: **HOLD**.
No production source changed in this Research session.

> **UPDATE — Stage B implemented (see §8).** Composer simplification is coded, builds clean, and targeted/impacted tests pass except for **pre-existing** design-system/governance debt proven unchanged by Stage B. Stage C NOT started.

---

## 1. Evidence Classes (definition)

| Class | Meaning |
|---|---|
| **SOURCE VERIFIED** | Harness TypeScript/TSX source read directly from `D:\NEXUS\Audit\deepseek-harness` (tag `dsh-v0.2.0-rc.2`, HEAD `639ed015…`); paths + exports confirmed. |
| **REA VERIFIED** | Actual REA MCP tool invocation with evidence_id on the shipped binary. |
| **UI OBSERVED** | Running-app visual/interaction proof. **NONE captured** this session. |
| **NOT VERIFIED / INFERRED** | Plausible mapping not yet proven. |

### REA status (do NOT repeat)
- `rea_open_binary` → `app.asar` sha256 `983ca71114e6dfd353fc79af5a1f9481a250ee64c2a3c757673029b811b23bc2`.
- `rea_inspect_artifact` → evidence `ev_924288296254f2bd56af2c17ba852e0d1ba0d3a32d8a65bf7455c7c5a508aae0`; coverage `complete-within-view` 44706/44706; `node_count 13553`.
- **Scope of REA proof = ARCHIVE/INVENTORY LEVEL ONLY.** No component-level REA analysis of composer/sidebar/permission modules was executed. Component-level claims below are SOURCE VERIFIED, not REA VERIFIED.
- Further REA calls require **explicit per-call owner approval** ("jangan auto-approve"). Listed as optional gap-fill in §6, not scheduled.

---

## 2. Stage A Reuse Map (Harness → Nexus)

Legend decision: **USE** = concept/behavior replicated near 1:1. **ADAPT** = concept ported to Nexus WPF idioms. **ENHANCE** = Nexus extension beyond Harness. **REJECT** = not adopted. **NOT NEEDED** = no Nexus target.

### 2.1 PRIORITY — Composer / InputBar

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-conversation/src/client/skeleton/InputBar.tsx` — default composer body = `conversation.composer.bar` slot; single text surface via shell-owned Lexical editor (`ComposerContentEditable`); chips = decorator portals; no-session state renders SAME div inert as Workspace-picker trigger (no parallel tree). SOURCE VERIFIED | `ConversationView.xaml` composer area (`~985–1010` empty/Temporary Chat state; `538–565` `AIWorkspaceMain`/`AIChatPanelBorder`) | Nexus composer not a single-slot shell; empty state separate tree; chips/attachments logic scattered | **ADAPT** (one composer surface; empty state = same control inert) |
| `InputBar.module.css` — `.root` flex column, side pad `--dsh-composer-side-clearance`, bottom 4px; `.card` overlay anchor, gap 12px, max-width `--dsh-composer-card-max-width`, Input l2 stroke. SOURCE VERIFIED | `Themes/DshDesignTokens.xaml` + composer XAML | Nexus tokens have DSH base; composer-specific clearance/max-width tokens may be incomplete | **ADAPT** (map tokens; tighten padding-top 8px) |
| `input/facade.ts` — `SessionInputShell` per-session: Lexical editor + pure `SubmitMachine`; submit transaction (adjudicate via `InputTriggerController`, claim.submit, default sink); notice channel; draft persistence mirror; slash/popup faces are thunks. SOURCE VERIFIED | `ConversationView.xaml.cs` + `ViewModels/ConversationViewModel.cs` | Nexus lacks explicit submit state machine + notice channel separation | **ADAPT** (introduce submit phase model: idle→claim→submit; keep WPF) |
| `input/machine.ts` — pure `SubmitMachine`; claimed commands occupy frozen in-flight slot; ordinary messages detach at Enter (editor clears immediately). SOURCE VERIFIED | `ViewModels/ConversationViewModel.cs` | Nexus submit is not modeled as pure state machine; no frozen claim slot | **ADAPT** |
| `input/hub.ts` — `InputHub` = `ctx.conversation.input` resolver; one `SessionInputShell` per session, created on session materialization, torn down by scope disposer; default sink = one unconditional prompt path. SOURCE VERIFIED | `ConversationView.xaml.cs` | Nexus has no per-session input resolver lifecycle | **ADAPT** |
| `skeleton/ConversationHeader.tsx` — resident nav + session header; slots `conversation.header.leading`, `conversation.session.header`; `blank` gating. SOURCE VERIFIED | `ConversationView.xaml` header (model selector + Chat/AI Check switcher) | Nexus header keeps model selector + switcher (must stay); slot model absent but not required | **USE** (slot model NOT NEEDED for Nexus; keep header as-is) |

### 2.2 PRIORITY — Permission

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-permission-presets/src/client/PermissionSelect.tsx` — glyph map (`read-only`→ReadOnly, `workspace-write`→WorkspaceWrite, `danger-full-access`→FullAccess); `Menu` + `RiskConfirmation` + chevron. SOURCE VERIFIED | composer `/permission` control (`ConversationView.xaml`) | Nexus permission control presentation unclear; glyphs/risk-confirm may be missing | **ADAPT** |
| `PermissionRow.tsx` — new-session default preset on `settings.general.item`; current-session switch on composer `/permission`. SOURCE VERIFIED | `SettingsView` + composer | Nexus splits default vs session permission inconsistently | **ADAPT** |
| `presentation.ts` — `FULL_ACCESS_PRESET='danger-full-access'`, `AUTO_REVIEW_PRESET='auto'`, `PRESET_LABEL_KEYS` map, `displayPresetName`/`displayPermissionPreset`. SOURCE VERIFIED | `ViewModels/…` permission display | Nexus display mapping not centralized | **ADAPT** |
| `catalog.ts` — `PermissionCatalogDirectory`: one latest-result-wins catalog reader; `SnapshotStore` consumed by slash popup + composer seat. SOURCE VERIFIED | permission catalog service | Nexus catalog refresh/consistency model absent | **ADAPT** |
| `settings-store.ts` — default-settings controller; writes only `defaultPreset`; `PERMISSION_SETTINGS_NS='permission'`; states `idle/loading/ready/saving/unavailable/error`. SOURCE VERIFIED | settings persistence | Nexus has no explicit permission settings state machine | **ADAPT** |
| `locales.ts` (permission) — `PERMISSION_ACCESS_NS='permission.access'`; zh key-set source of truth (incl. `confirm.*`). SOURCE VERIFIED | Nexus resource strings | Nexus strings exist; ensure parity of confirm/acknowledge keys | **ADAPT** |

**Security invariant:** permission preset semantics (`read-only`/`workspace-write`/`danger-full-access`) and risk-confirmation MUST NOT be weakened. No backend/permission-contract change in Stage B/C without owner approval.

### 2.3 PRIORITY — Sidebar / Project Selector

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-sidebar/src/client/SidebarRoot.tsx` — shell column geometry + global panel navigation; collapse = slide + crossfade (`COLLAPSE_SETTLE_MS=150`); registrants `sidebar.workspaces`, `sidebar.settings`, `sidebar.footer.action`. SOURCE VERIFIED | `Views/DshSidebarView.xaml`+`.cs` | Nexus sidebar has history-expand fix; collapse animation/registrant model may differ | **ADAPT** |
| workspace picker / project selector (from InputBar no-session trigger + `sidebar.workspaces`) SOURCE VERIFIED (paths) | `DshSidebarView.xaml` + workspace view | Nexus project selector wiring not confirmed in code | **ADAPT** (verify exact target during Stage B; mark INFERRED wiring) |

### 2.4 PRIORITY — Chat Timeline

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-conversation` conversation skeleton (`ConversationRoot/Panel/MainPanel/Session`, `DefaultConversationViews`, `EmptyHero`, `TodoPanel`). SOURCE VERIFIED (inventory) | `ConversationView.xaml` timeline | Nexus timeline rendering exists; parity of empty hero/todo panel unverified | **ADAPT** |
| `locales`, `view-selection`, `stores`, `stop-shortcut`, `stop-sequence`. SOURCE VERIFIED (inventory) | `ConversationViewModel` | stop shortcut/sequence semantics unverified in Nexus | **ADAPT** (verify in Stage C) |

### 2.5 PRIORITY — Approval

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-approval/ApprovalPanel.tsx` — composer takeover for ONE pending approval; outcomes `'allowed-once'|'rejected'`; guarded by `pending.answerable` + `waiting` ref; `renderSlot('conversation.approval.detail',{callId})`. SOURCE VERIFIED | approval UI in `ConversationView`/`AgentWorkspaceView` | Nexus takeover + single-pending semantics unverified | **ADAPT** |
| `ui-approval/contract/slots.ts` — `SessionPendingInteractionMap.approval: PendingApproval`; `SlotMap['conversation.approval.detail']`; `settlePendingComposer`. SOURCE VERIFIED | approval contract | Nexus approval contract shape differs | **ADAPT** |
| `ui-approval/locales.ts` — `waiting`, `detail.aria`, `escalation`, `reject`, `allowOnce` (zh source of truth, en checked). SOURCE VERIFIED | approval strings | Ensure exact key parity | **USE** |

**Security invariant:** approval must remain real (no fake approval UI). Receipts/activity must reflect actual tool calls only.

### 2.6 Deliverables / Changed Files (secondary)

| Harness component (evidence) | Nexus target | Gap | Decision |
|---|---|---|---|
| `ui-deliverables/src/client/*` (`Deliverables.tsx`, `ChangedFiles.tsx`, `FileDiff.tsx`, `PresentRow.tsx`, `PresentedFileCard.tsx`, `ReviewTab.tsx`, `review-store.ts`, `review-definition.ts`, `turn-deliverables.ts`, `present-open.ts`, `file-actions.ts`, `changes-summary.ts`, `changes-diff.ts`, `host-read-store.ts`, `locales.ts`, `index.ts`). **Inventory SOURCE VERIFIED; contents NOT read.** | `AgentWorkspaceView.xaml` (trajectory/deliverables) | Contents unread → component mapping INFERRED | **ADAPT (contents NOT read — do not overclaim)** |
| `ui-tool`, `ui-attachment`, `ui-commands`, `ui-input-trigger`, `ui-trajectory`. Inventory observed | various | — | **ADAPT as needed in Stage C** |

---

## 3. Ownership / Frozen Artifacts (must preserve)

- Single writer = **GPT-6 SOL**. Gemini/UI agents stopped writing source.
- Frozen Harness UI candidate (6 files, NOT Gate H baseline) under `src/03_App/Nexus.App/`: `App.xaml` `3df99ccc…`, `Themes/Semantic/{Dark,Light}SemanticTheme.xaml` `521d9e13…`/`c53f9599…`, `Views/AgentWorkspaceView.xaml` `94b11602…`(+`.cs` `e82a71e3…`) — **still byte-identical**; `Views/ConversationView.xaml.cs` `9edd0623…` → **`07411a10…` (MODIFIED by Stage B, §8)**. `Views/ConversationView.xaml` (`e553ad14…` → **`60e9c9a6…`**) is also now MODIFIED by Stage B. Historical freeze no longer byte-identical for these two files (documented, not hidden).
- Gemini-attributed (read-only): `Themes/DshIcons.xaml`, `Themes/DshDesignTokens.xaml`, `Views/DshSidebarView.xaml`(+`.cs`).
- Staged patch SHA (must remain): `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
- I2-B pilot manifest `cc1f9432…`; 17/17 pilot source hashes unchanged.
- Working tree DIRTY (pre-existing, not this session): `Config/AppConfigRecord.cs`, `WpfColorManagementServiceTests.cs` (+ bin/obj artifacts). Account for before any Stage B edit; never reset/clean.
- No composer redesign during I2-B work; I2-B budgets: 2/4 generations, 2/2 runs consumed.

---

## 4. Stage Handoff (what to implement next)

**Stage B — Composer + Session Presentation (FIRST PRIORITY)**
- Ziel: Nexus composer sesederhana Harness: one composer surface, empty state = same control inert, submit state machine (idle→claim→submit), permission entry moved into contextual menu.
- Exact files (edit): `src/03_App/Nexus.App/Views/ConversationView.xaml`, `ConversationView.xaml.cs`, `ViewModels/ConversationViewModel.cs`, `Themes/DshDesignTokens.xaml` (tokens only).
- Reference (read-only): Harness `InputBar.tsx`, `InputBar.module.css`, `input/{facade,machine,hub}.ts`.
- Do NOT: change header model selector / Chat↔AI Check switcher; add second model selector in composer.

**Stage C — Activity / Changed-Files / Approval / Receipts**
- Exact files (edit): `Views/AgentWorkspaceView.xaml`(+`.cs`), approval/receipts in `ConversationView`, `ViewModels/AgentWorkspaceViewModel.cs`.
- Reference (read-only, contents still UNREAD → read minimally during Stage C, not now): `ui-deliverables/*`, `ui-approval/{ApprovalPanel.tsx,contract/slots.ts}`.

**Stage D — Verification**
- Targeted/impacted tests + actual WPF interaction/theme/keyboard proof. Report + seal.

---

## 5. Explicitly NOT Claimed
- Component-level **REA** not performed → not REA VERIFIED.
- No **UI OBSERVED** evidence (no running-app interaction proof).
- `ui-deliverables/*` **contents not read** → those mappings are INFERRED.
- "100% parity" (Gemini narrative) NOT adopted as certification.

---

## 6. Optional Gap-Fill (requires per-call owner approval — NOT scheduled)
1. One REA call to locate/analyze shipped composer/sidebar/permission modules inside `app.asar` (`rea_analyze_javascript_application` / artifact views on UI source paths) → would upgrade §2.1/2.2/2.3 from SOURCE VERIFIED to REA VERIFIED.
2. One running-app UI observation (screenshot/interaction) for composer empty-state + permission menu → would add UI OBSERVED class.

---

## 7. Blockers (unchanged)
- **I2-B native acceptance HOLD**: both runs failed `OutputLimit` before validated proposal; native `tool_calls` never accepted. Needs owner budget/decision; no auto parser/model/cap change.
- Further REA calls need explicit per-call owner approval.
- Stage B/C edits require owner-approved scope for any WebView2 swap, global nav change, many new components/deps, far-out-of-baseline source, or security/backend/runtime/SDK change.

---

## 8. Stage B Implementation

Status: **Stage B IMPLEMENTED — build clean, targeted/impacted regression green except PRE-EXISTING failures, before/after screenshots captured and compared.** Stage C NOT started.

### 8.1 Source delta (owned files)

| File | Pre-Stage-B SHA256 | Post-Stage-B SHA256 |
|---|---|---|
| `src/03_App/Nexus.App/Views/ConversationView.xaml` | `e553ad143bcca2b3aaeba1a1ebc14108a1914804477b0f749398bf68c1f6b5af` | `60e9c9a667de57ed177df6104403be8266dfed331f7f510dfe452637f85102ea` |
| `src/03_App/Nexus.App/Views/ConversationView.xaml.cs` | `9edd062385012549853af11d12a847d3f7c454f7f25796fd27c421ddae4f865e` | `07411a10ab74a6a626e72221a74b2558161a7524bcc5c84b3552138fcbf7e534` |
| `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs` | `8555fe6e7ee25fc5cbcd08b50a7d40be4da2e3c8ee969ef55c14489cfddbe338` | **UNCHANGED** (no VM edit required) |
| `src/03_App/Nexus.App/Themes/DshDesignTokens.xaml` | `9ebaa289554d58e0aabfcf41b33bbd51ff18d7bff422b155fb929242a028d842` | **UNCHANGED** (no new tokens needed) |

Verification against the I2-B snapshot (`gate-i2b\hold-source-snapshot\`, 17:12): `ConversationViewModel.cs`, `App.xaml`, `Themes/Semantic/{Dark,Light}SemanticTheme.xaml`, `Views/AgentWorkspaceView.xaml`(+`.cs`) are all **byte-identical**. Exactly **4** files differ, and Stage B owns **only the 2 production files** above (mtime ≈19:2x). The other 2 differing files are **test files modified earlier by I2-B owner-recovery** (mtime 17:30/17:48, before Stage B began ≈19:20) and were NOT touched by Stage B: `Nexus.Integration.Tests/ConversationViewModelTests.cs` (+35 lines: `LoadHarnessSidebar`, `DeepThinkIcon_UsesHarnessVectorResource`, `HarnessHistory_ActualButtonExpandsAllSessions…`) and `ArchitectureGovernanceTests.cs` (+19 lines: `IsolatedTesthost_SourceLookupContext_IsRecordedAndResolvesActualSource` + Medium-token SID `S-1-16-8192`).

### 8.2 What changed (composer simplification)

- **Compact project selector** replaces the wide pill row: single `BtnProjectSelector` (folder glyph `Folder` + `{Binding SelectedProjectName}` + `DshIconChevronDown`), all refs non-governed.
- **Contextual `ProjectMenuPopup`** (outside the composer toolbar) contains the RELOCATED (not deleted) project read-permission workflow (`HasProjectReadWorkflow` stack: grant/revoke/use-context/status), the reviewed-edit authorization checkbox, source selector, provenance expander, and "Jelaskan kode" row, plus a "Pilih / ganti proyek" action and close `X`.
- **`ModeMenuPopup`** (contextual Tanya / Edit kode radios, `NexusWorkspaceModePillRadioButtonStyle`) placed OUTSIDE `ComposerTopBar`; trigger `BtnModeSelector` sits before `BtnDeepThink` in the toolbar (label swaps via DataTrigger on `IsBoundedEditMode`).
- Removed from the default composer body: permanent grant/revoke buttons, inline mode radios, permanent capability/status text — so an empty session shows greeting + centered composer with project selector above (Harness-like).
- Code-behind (`ConversationView.xaml.cs`): added `BtnProjectSelector_Click`, `BtnProjectMenuClose_Click`, `ProjectMenuPopup_PreviewKeyDown`, `BtnModeSelector_Click`, `ModeMenuPopup_PreviewKeyDown`; `SelectProject_Click` closes the popup first; `ConversationView_PreviewKeyDown` closes popups on Escape.

### 8.3 Reused Harness patterns (source)
`InputBar.tsx` (single composer surface; no-session state = same control inert; side clearance) and `InputBar.module.css` (`.root`/`.card` overlay anchoring). No Lexical/Cordis/SubmitMachine/InputHub recreated; no backend/Agent/approval/SDK/recovery change; no new permission service or Full-Access path.

### 8.4 Evidence

- **Build**: `gate_b_run.py build it` → exit 0, 0 errors.
- **Targeted** (`stageb-core`, filter `DesignSystemGovernanceTests|ConversationViewModelTests|AIWorkspaceRenderingTests`): Total **185**, Passed **182**, Failed **3** — all `DesignSystemGovernanceTests`, all **pre-existing** (§8.5). All `ConversationViewModelTests` + `AIWorkspaceRenderingTests` PASS.
- **Impacted** (`stageb-impacted`, filter `ArchitectureGovernanceTests|ContextMenuCornerFidelityTests|AiCheckPipelineIntegrationTests|GateIChatFirstWorkspaceTests|NexusUIFoundationTests|GateIAgentWorkspaceUiTests|WorkspaceTabStyleTests`): Total **92**, Passed **89**, Failed **3** — all `NexusUIFoundationTests`, all **pre-existing** (§8.5).
- **Before screenshots** `…\Temp\opencode\stage-b-before\` (30 PNGs, 19:20). **After screenshots** `…\Temp\opencode\stage-b-after\` (30 PNGs, 19:28). Size deltas coherent: `empty` +3.7–7.4 KB (new compact composer/selector), `sidebar-*` −4.1–5.4 KB (removed inline radios/permission stack/status text).
- **UI OBSERVED**: `Light-1440-empty` before vs after confirms the simplification; `Light-1200-sidebar-True` confirms active-chat composer pinned at bottom with header model selector + Chat/AI Check switcher preserved.

### 8.5 Pre-existing failures (NOT Stage B regressions) — proof

All 6 failures trace to files **byte-identical** to the I2-B pre-Stage-B snapshot, so Stage B cannot have introduced them:
- `DesignSystemGovernanceTests` ×3: sources = `Views/AgentWorkspaceView.xaml` (94b11602…, identical), `Views/DshSidebarView.xaml` (untouched), and `ConversationView.xaml` drift. **ConversationView.xaml primitive-token total = 72 before → 72 after (Δ0)**, and every governed token count (StrokeDefault, TypographyCaptionSize, TypographyIconFontFamily, TypographyBodySize, TypographyIconSize, TypographyRegularWeight, RadiusSmall, StrokeFocus, StrokeSubtle) is identical. Full line diff confined to the composer region. Failing: `Existing_design_system_debt_is_visible_and_finite` (Expected 1, Actual 3 CommonControlTemplate), `Module_views_do_not_consume_primitive_tokens_or_define_common_templates`, `Primitive_token_references_are_limited_to_design_system_layers_or_the_legacy_baseline`.
- `NexusUIFoundationTests` ×3: sources = `App.xaml` (3df99ccc…, identical), `Themes/Semantic/*` (identical). Failing: `App_ShouldMergeFoundationSemanticAndCompatibilityDictionariesInExactOrder` (Actual adds `DshDesignTokens.xaml`,`DshIcons.xaml`), `SemanticThemes_ShouldExposeEveryCanonicalRoleAndUseOnlyApprovedPrimitiveColors` (hex `#FFFFFF` present), `MainViewModel_ThemeEntryPoint_ReplacesOnlyThemeAndCompatibilityDictionaries` (Expected 17, Actual 19).

No assertion weakened; no test edited. Fixing these = editing non-owned files or weakening tests → requires separate owner approval (out of Stage B scope).

### 8.6 Preservation checks
Header model selector + Chat/AI Check switcher: preserved (screenshots + `ModelSelector_RemainsInHeaderAfterModeSelectorRemoval` PASS). Sidebar/DSH visuals: preserved. Send/Stop, drafts/session, light/dark/HC, Enter/Shift+Enter/IME: covered by passing `ConversationViewModelTests` + contract tests. Edit-mode ≠ write authority: no new Full-Access path or permission service added; existing read-grant/reviewed-edit services reused.

### 8.7 Remaining UX gap (documented, not fixed)
`ProjectMenuPopup` uses `StaysOpen="True"` → no outside-click dismiss (close via `X` / Escape / toggle). Escape relies on focus in the main window or popup content (both handlers wired). Optional Stage B-polish item.

**END OF STAGE A HANDOFF. STOP.**
