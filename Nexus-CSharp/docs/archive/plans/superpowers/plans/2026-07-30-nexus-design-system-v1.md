# Nexus Design System v1 Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to execute this plan task-by-task after explicit implementation approval.

**Goal:** Migrate Nexus to the approved canonical design-system architecture without changing business behavior, bindings, or module ownership.

**Architecture:** Resources flow from primitive dictionaries to theme-specific semantic dictionaries to component dictionaries, then into module views. `ThemeManager` owns Light/Dark/High Contrast dictionary selection; `ApplicationVisualPreferences` owns Reduced Motion and other non-theme visual preferences. Compatibility aliases bridge existing resource keys until every consumer has migrated and zero-reference evidence permits a separately approved removal.

**Tech Stack:** .NET 8, WPF XAML, C#, xUnit, WPF `ResourceDictionary`, UI Automation/manual verification, `dotnet build`, `dotnet test`.

**Authority:** [Nexus Design System v1 — Final Standard](../specs/2026-07-30-nexus-design-system-v1-standard.md)

## Execution Rules

- This document is a plan only. It does not authorize implementation.
- Execute one task at a time after explicit approval; stop at every checkpoint.
- Before each task, record `git status --short` and preserve unrelated changes.
- Never combine migrations for multiple modules in one task.
- Preserve commands, bindings, automation names, tab order, and runtime behavior unless the task's acceptance criteria explicitly change them.
- A failing baseline unrelated to the task is documented, not repaired opportunistically.
- Build and focused tests run serially to avoid output-file contention.
- Every commit named below is created only during an authorized execution.
- Compatibility aliases remain until a separately approved closure task has zero-reference evidence.
- Custom components are gated by a demonstrated product need plus keyboard, focus, automation, and lifecycle tests.
- Before adding a style/template/component, use an existing canonical style first, use `BasedOn` for additional behavior or state second, and request a Design System change if neither is sufficient.
- A new global style requires design review and must never be justified only by module ownership.
- Modules must not own Button, ComboBox, TextBox, ProgressBar, ScrollBar, or Menu templates unless the approved Design System records a domain-specific exception.

## Mandatory UI Code-Review Checklist

Every implementation task must record:

- [ ] Uses canonical styles.
- [ ] Does not consume primitive tokens directly.
- [ ] Does not add a new hex color.
- [ ] Does not create a new common-control template.
- [ ] Does not duplicate an existing style.
- [ ] Does not add responsibility to `ThemeManager`.
- [ ] Does not change the accessibility contract.
- [ ] Does not change keyboard behavior.
- [ ] Does not change the component lifecycle.
- [ ] Build passes.
- [ ] Design-system tests pass.
- [ ] Governance tests pass.

## Standard Verification Commands

Run from the repository root:

```powershell
dotnet build .\src\03_App\Nexus.App\Nexus.App.csproj --no-restore
dotnet test .\src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~NexusUIFoundationTests"
dotnet test .\src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~DesignSystem"
```

Expected: exit code `0`. If restore inputs are unavailable, stop and report the environmental blocker rather than changing package configuration.

---

## Stage 1 — Baseline, Governance, Primitive Tokens, Shared Metrics

### Task 0.1: Freeze the measurable legacy baseline

**Files:**

- Create: `docs/design-system/migration-baseline.md`
- Modify: none
- Test: existing test suite only

**Steps:**

1. Record current Git status, current resource dictionaries, every `x:Key`, local `ControlTemplate`, module-local common style, hardcoded hex, and legacy key reference count.
2. Record current results for app build and `NexusUIFoundationTests`.
3. Record known unrelated failures separately; do not broaden scope.
4. Store counts and exact `rg` commands in `migration-baseline.md`.
5. Checkpoint: documentation-only review.

**Acceptance:** The baseline is reproducible and identifies every legacy alias candidate and local template before migration starts.

**Rollback:** Remove only the new baseline document.

**Authorized-execution commit:** `docs: capture Nexus design-system migration baseline`

### Task 0.2: Add source-governance tests

**Files:**

- Create: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`
- Modify: none
- Test: `DesignSystemGovernanceTests`

**Test contract:**

```csharp
[Fact]
public void Module_views_do_not_consume_primitive_tokens_or_define_common_templates()
{
    var violations = DesignSystemSourceAudit.ScanModuleViews();
    Assert.Empty(violations);
}
```

Start with allowlists matching the recorded baseline so the harness passes before migration. Every later task reduces an allowlist; no task adds an entry without design review.

**Steps:**

1. Add helpers that enumerate source `.xaml` files while excluding `bin` and `obj`.
2. Detect primitive-token consumption, common-control `ControlTemplate` declarations, general hex colors, and module-named style keys.
3. Explicitly detect module-owned Button, ComboBox, TextBox, ProgressBar, ScrollBar, and Menu templates.
4. Represent the known baseline as explicit file/key allowlists with removal comments.
5. Run the focused tests and confirm they pass.
6. Checkpoint: review detections against manual `rg` results.

**Acceptance:** New violations fail deterministically, while existing debt is visible and finite.

**Rollback:** Revert only `DesignSystemGovernanceTests.cs`.

**Authorized-execution commit:** `test: govern Nexus design-system resource boundaries`

---

### Task 1.1: Create primitive and shared metric dictionaries

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Tokens/PrimitiveTokens.xaml`
- Create: `src/03_App/Nexus.App/Themes/Tokens/SharedMetrics.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Required resources:** primitive colors, radius `0/4/8/12/16`, strokes, heights `32/40/44`, spacing `2/4/8/12/16/24/32/40`, typography roles, icon sizes, static motion durations, and elevation metrics.

`NexusRadiusCircular` must not be added as a fixed `CornerRadius`.

**Steps:**

1. Add failing resource-load assertions for the approved shared metrics and absence of a static `NexusRadiusCircular`.
2. Run the focused test and confirm the new assertions fail.
3. Add the two dictionaries and merge them before semantic/component dictionaries.
4. Move only shared primitive/metric definitions; retain compatibility keys.
5. Run XAML resource-load tests and app build.
6. Checkpoint: verify views still resolve all existing keys.

**Acceptance:** Dictionaries load, metrics match the approved standard, and no view migration occurs.

**Rollback:** Restore `App.xaml`, tests, and remove only the two new dictionaries.

**Authorized-execution commit:** `feat: add Nexus primitive and metric token foundation`

### Task 1.2: Add token-layer enforcement

**Files:**

- Modify: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

**Steps:**

1. Assert primitive keys are referenced only by token/theme/component dictionaries.
2. Assert module views do not use primitive keys.
3. Keep an explicit legacy allowlist from Task 0.1.
4. Run focused governance tests.

**Acceptance:** The primitive → semantic → component → module boundary is machine-checked.

**Rollback:** Revert this test-only change.

**Authorized-execution commit:** `test: enforce Nexus token layering`

---

## Stage 2 — Theme, Semantic, Compatibility, Accessibility

### Task 2.1: Establish canonical semantic themes

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Semantic/LightSemanticTheme.xaml`
- Create: `src/03_App/Nexus.App/Themes/Semantic/DarkSemanticTheme.xaml`
- Create: `src/03_App/Nexus.App/Themes/Semantic/HighContrastSemanticTheme.xaml`
- Modify: `src/03_App/Nexus.App/Themes/Tokens/PrimitiveTokens.xaml`
- Modify: `src/03_App/Nexus.App/Themes/LightTheme.xaml`
- Modify: `src/03_App/Nexus.App/Themes/DarkTheme.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing tests for every canonical semantic key listed in the approved specification, including `NexusInfoForeground`, `NexusInfoBackground`, `NexusInfoStroke`, `NexusFocusOuter`, `NexusFocusInner`, and `NexusScrim`.
2. Add semantic dictionaries for Light, Dark, and High Contrast.
3. Map `NexusBrushStatusInfo` to `NexusInfoStroke` through the compatibility layer; do not leave it undefined.
4. Keep old theme dictionaries as compatibility entry points during this task.
5. Load each dictionary on an STA thread and verify all resources resolve.
6. Build and run focused tests.

**Acceptance:** One complete semantic family exists for all three modes; no common semantic resource is undefined.

**Rollback:** Restore merge order and remove only the new semantic dictionaries.

**Authorized-execution commit:** `feat: add canonical Nexus semantic themes`

### Task 2.2: Separate theme switching from visual preferences

**Files:**

- Create: `src/03_App/Nexus.App/Services/IApplicationVisualPreferences.cs`
- Create: `src/03_App/Nexus.App/Services/ApplicationVisualPreferences.cs`
- Modify: `src/03_App/Nexus.App/Services/ThemeManager.cs`
- Modify: `src/03_App/Nexus.App/App.xaml.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Interfaces:**

```csharp
public interface IApplicationVisualPreferences
{
    bool IsReducedMotionEnabled { get; }
    event EventHandler? PreferencesChanged;
}
```

`ThemeManager` continues to own only mode selection, semantic dictionary switching, and theme-change notification.

**Steps:**

1. Add failing tests for Light/Dark/High Contrast switching and preference ownership.
2. Extend `ThemeMode` with High Contrast mapping without putting Reduced Motion on it.
3. Add the preference service and register it in application composition.
4. Ensure theme switching replaces only semantic theme dictionaries.
5. Verify unrelated merged dictionaries retain reference identity.
6. Run focused tests and build.

**Acceptance:** Theme and Reduced Motion ownership are distinct; switching theme does not reconstruct unrelated resources.

**Rollback:** Revert the service registration and these four files.

**Authorized-execution commit:** `refactor: separate Nexus theme and visual preference ownership`

---

### Task 3.1: Centralize aliases without changing consumers

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml`
- Create: `src/03_App/Nexus.App/Themes/Compatibility/LegacyBrushAliases.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

> **Note:** Changes to `DesignSystemGovernanceTests.cs` in Task 3.1 are strictly limited to recognizing `Themes/Compatibility/` as a valid design-system directory layer. Adding rules to prevent new alias consumption, modifying legacy-reference allowlists, or performing other governance refactoring is reserved for Task 3.2.

**Steps:**

1. Add failing assertions for every alias in specification section 23.
2. Move alias ownership into the compatibility dictionaries without changing consumer XAML.
3. Mark aliases as migration-only in comments.
4. Do not automatically alias `NexusBackgroundComboBoxStyle`; preserve it for manual classification.
5. Load the full application resources and build.

**Acceptance:** Existing keys resolve from one compatibility layer; new canonical keys also resolve.

**Rollback:** Restore existing key locations and remove the new alias dictionaries.

**Authorized-execution commit:** `refactor: centralize Nexus compatibility aliases`

### Task 3.2: Prevent new alias consumption

**Files:**

- Modify: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

**Steps:**

1. Add an allowlist containing exact existing legacy references from Task 0.1.
2. Fail if a legacy key appears in a new file or its reference count increases.
3. Run governance tests.

**Acceptance:** Alias use can only decrease.

**Rollback:** Revert this test-only change.

**Authorized-execution commit:** `test: prevent new Nexus legacy-style references`

---

### Task 4.1: Add canonical focus and motion behaviors

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/Accessibility.xaml`
- Create: `src/03_App/Nexus.App/Behaviors/ReducedMotion.cs`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing tests that focus visuals use semantic focus resources, occupy no layout space, and expose 2 px outer thickness.
2. Add shared focus visual resources for keyboard-focused controls.
3. Add a behavior that reads `IApplicationVisualPreferences`; it must not switch themes.
4. Verify focus resources resolve in Light, Dark, and High Contrast.
5. Verify Reduced Motion disables optional scale/transition behavior.
6. Build and run focused tests.

**Acceptance:** A single focus contract and a separate Reduced Motion hook exist before component migration.

**Rollback:** Remove the new dictionary/behavior and restore merge order.

**Authorized-execution commit:** `feat: add Nexus accessibility foundation`

---

## Stage 3 — Canonical Controls — Button, TextBox, SearchBox, ComboBox, Selection Controls

> **Architecture Decision (FINAL):** Entire native input controls are consolidated into `Themes/Components/InputControls.xaml` (`src/03_App/Nexus.App/Themes/Components/InputControls.xaml`). This decision supersedes the original split into `TextBoxes.xaml`, `ComboBoxes.xaml`, and `SelectionControls.xaml`. Future implementation must continue using `InputControls.xaml` unless a separately approved architecture decision changes this contract.

### Task 5.1: Create canonical Button styles

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/Buttons.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Styles:** `NexusPrimaryButtonStyle`, `NexusSecondaryButtonStyle`, `NexusSubtleButtonStyle`, `NexusToolbarButtonStyle`, `NexusDangerButtonStyle`, and specialized `NexusIconButtonStyle`.

**Steps:**

1. Add failing dictionary assertions and lifecycle checks for Rest, Hover, Pressed, Focused, Disabled, and Loading visual contracts.
2. Implement one shared base template and variant component tokens.
3. Keep height/radius/padding/icon spacing consistent; use the circular rule only inside canonical circular icon-button templates.
4. Preserve normal WPF command and keyboard activation.
5. Verify loading prevents repeat invocation and retains a stable label when content permits.
6. Verify Reduced Motion removes optional scale.
7. Build and run focused tests.

**Acceptance:** Variants differ semantically, not structurally, and expose accessible names for icon-only usage.

**Rollback:** Restore merge order and remove only `Buttons.xaml`.

**Authorized-execution commit:** `feat: add canonical Nexus button family`

### Task 5.2: Point Button aliases at the canonical family

**Files:**

- Modify: `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Classify `AIMessageActionButtonStyle` usage as Icon or Subtle before mapping.
2. Map all approved Button aliases.
3. Verify old keys and new keys resolve to behaviorally equivalent styles.
4. Do not migrate any view in this task.

**Acceptance:** Legacy Button consumers receive canonical chrome without binding changes.

**Rollback:** Revert the alias dictionary only.

**Authorized-execution commit:** `refactor: bridge legacy buttons to canonical styles`

---

### Task 6.1: Add canonical TextBox style

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/InputControls.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing state tests for Rest, Hover, Focused, Readonly, Disabled, Invalid, placeholder, and optional clear action.
2. Add `NexusTextBoxStyle` using semantic/component tokens.
3. Preserve `Text`, validation, update trigger, caret, selection, and command bindings.
4. Point `NexusTextBoxStandard` and `RemoveBackgroundSettingsTextBoxStyle` aliases to it.
5. Build and test.

**Acceptance:** One native TextBox template covers common lifecycle states.

**Rollback:** Restore aliases/merge order and remove `InputControls.xaml`.

**Authorized-execution commit:** `feat: add canonical Nexus textbox style`

### Task 6.2: Add SearchBox specialization

**Files:**

- Create: `src/03_App/Nexus.App/Behaviors/SearchBoxBehavior.cs`
- Modify: `src/03_App/Nexus.App/Themes/Components/InputControls.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing tests for search accessible name, clear command, Escape-to-clear, and inherited focus chrome.
2. Add `NexusSearchBoxStyle` as a native TextBox specialization and attach shared search/clear behavior; do not create a new control class.
3. Preserve external text binding and validation semantics.
4. Run focused tests and build.

**Acceptance:** A native TextBox gains search behavior through canonical style/behavior while retaining the TextBox contract.

**Rollback:** Remove the specialization and its style additions.

**Authorized-execution commit:** `feat: add canonical Nexus searchbox specialization`

---

### Task 7.1: Create canonical ComboBox and item templates

**Files:**

- Modify: `src/03_App/Nexus.App/Themes/Components/InputControls.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing lifecycle tests for Rest, Hover, Focused, Open, Closed, Selected, Empty, Disabled, Invalid, and Loading Items.
2. Add keyboard tests for Up/Down, Home/End, Enter, Escape, and focus restore.
3. Add `NexusComboBoxStyle` and `NexusComboBoxItemStyle`; popup minimum width follows the control and placement respects window bounds.
4. Point `NexusComboBoxStandard` to the canonical style.
5. Verify disabled items remain legible and item height is consistent.
6. Build and test.

**Acceptance:** One canonical control template and one canonical item template cover common use.

**Rollback:** Restore alias/merge order and revert additions in `InputControls.xaml`.

**Authorized-execution commit:** `feat: add canonical Nexus combobox family`

### Task 7.2: Enforce local item specialization through BasedOn

**Files:**

- Modify: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

**Steps:**

1. Detect local `ComboBoxItem` styles.
2. Require `BasedOn="{StaticResource NexusComboBoxItemStyle}"`.
3. Fail any local replacement `ControlTemplate`.
4. Keep exact legacy exceptions until their owning module migration.

**Acceptance:** Domain state may extend the item style but cannot replace canonical chrome.

**Rollback:** Revert the test-only change.

**Authorized-execution commit:** `test: govern Nexus combobox item specialization`

---

### Task 8.1: Add CheckBox and RadioButton styles

> **Note:** `ToggleStyle` is deferred.
> **Reason:** The only production consumer is AI Workspace, which is outside the scope of Stage 3. Canonical Toggle will only be introduced after a separate product need and explicit design approval.

**Files:**

- Modify: `src/03_App/Nexus.App/Themes/Components/InputControls.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing state/focus/keyboard tests.
2. Add `NexusCheckBoxStyle` and `NexusRadioButtonStyle`.
3. Map existing standard aliases.
4. Preserve `IsChecked`, commands, grouping, and immediate-apply semantics.
5. Build and test.

**Acceptance:** Shared selected/hover/pressed/disabled/focus tokens govern both controls.

**Rollback:** Restore alias/merge order and revert additions in `InputControls.xaml`.

**Authorized-execution commit:** `feat: add canonical Nexus selection controls`

### Task 8.2: Add Segmented Control specialization

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/SegmentedControl.xaml`
- Create: `src/03_App/Nexus.App/Behaviors/SegmentedSelectionBehavior.cs`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing tests for 2–5 exclusive options, arrow-key traversal, selected state, automation selection semantics, and focus.
2. Specialize native `ListBox`/`ListBoxItem` selection semantics through shared styles and behavior; do not create a custom control class.
3. Add `NexusSegmentedControlStyle` and `NexusSegmentedItemStyle` with shared outer geometry.
4. Build and test.

**Acceptance:** Equal, frequent mode choices use one accessible native-control specialization.

**Rollback:** Remove the new control and dictionary; restore merge order.

**Authorized-execution commit:** `feat: add canonical Nexus segmented control`

---

## Stage 4 — Layout, Surfaces, Feedback, Overlay, Menu, Action Controls

### Task 9.1: Classify every candidate interaction

**Files:**

- Create: `docs/design-system/action-control-classification.md`
- Modify: none

**Steps:**

1. Inventory every compound action or selector currently approximating SplitButton/MenuButton.
2. For each candidate, record whether it has a directly executable default action and same-family alternatives.
3. Classify as Button, MenuButton, ComboBox, Segmented Control, or approved SplitButton need.
4. Review keyboard, focus, accessible names, and lifecycle requirements.

**Acceptance:** There is source evidence for every custom action-control decision.

**Rollback:** Remove only the classification document.

**Authorized-execution commit:** `docs: classify Nexus action controls`

### Task 9.2: Apply the on-demand gate

If no real need is proven, this task is a documented no-op: do not create SplitButton or MenuButton.

If a need is proven, first create a separate reviewed specification and test plan covering function, keyboard, `AutomationProperties`, focus, and lifecycle. Only then may an implementation task be appended.

**Required status:** `NexusSplitButtonStyle: APPROVED CONTRACT, IMPLEMENTATION ON DEMAND`.

**Acceptance:** Nexus does not add a custom component merely for catalog completeness.

---

### Task 10.1: Add Card, ProgressBar, and status presentation styles

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/Surfaces.xaml`
- Create: `src/03_App/Nexus.App/Themes/Components/Feedback.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing assertions for Card radius/stroke/no-default-shadow and Progress variants 4/8/indeterminate/determinate.
2. Add canonical Card and feedback styles.
3. Add Inline Message, Page Banner, Validation Message, and non-custom Status Panel presentation contracts.
4. Map Card and Progress aliases.
5. Verify status information uses icon/text and semantic status, not color alone.
6. Build and test.

**Acceptance:** Common surfaces and non-blocking feedback have one visual contract.

**Rollback:** Restore aliases/merge order and remove the two dictionaries.

**Authorized-execution commit:** `feat: add canonical Nexus surfaces and feedback`

### Task 10.2: Unify native Dialog, Popup, Menu, and ContextMenu chrome

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/Overlays.xaml`
- Create: `src/03_App/Nexus.App/Themes/Components/Menus.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.App/Themes/Compatibility/LegacyStyleAliases.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing tests for radius/elevation/padding, icon-label-shortcut columns, keyboard navigation, Escape, focus trap, and focus restore.
2. Add canonical native styles without introducing Toast/Flyout custom controls.
3. Classify MessageBox usage as blocking decision or migration candidate.
4. Map existing Dialog/Menu/ContextMenu aliases.
5. Build and test.

**Acceptance:** Native overlay/menu controls share canonical chrome and lifecycle behavior.

**Rollback:** Restore aliases/merge order and remove the new dictionaries.

**Authorized-execution commit:** `feat: unify Nexus overlay and menu styles`

### Task 10.3: Gate Toast, Flyout, and reusable Status Panel

Use the same gate as Tasks 9.1–9.2: no custom component until a real use case and reviewed keyboard, automation, focus, announcement, dismissal, stacking, and lifecycle tests exist.

**Acceptance:** Contract approval does not silently authorize custom-control creation.

---

### Task 11.1: Add shared page and toolbar layout metrics

**Files:**

- Create: `src/03_App/Nexus.App/Themes/Components/Layout.xaml`
- Modify: `src/03_App/Nexus.App/App.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`

**Steps:**

1. Add failing resource assertions for 24 px primary page inset, header/toolbar/content/footer spacing, 32 px compact-toolbar height, and 44 px navigation height.
2. Add layout resources and shared styles for page regions and toolbar hit targets.
3. Do not freeze numeric responsive breakpoints.
4. Add overflow and keyboard-traversal contract for toolbars.
5. Build and test.

**Acceptance:** Modules can consume one Page → Header → optional Toolbar → Content → optional Status/Footer contract.

**Rollback:** Restore merge order and remove `Layout.xaml`.

**Authorized-execution commit:** `feat: add Nexus page and toolbar layout contract`

---

## Stage 5 — Nexus Module Migrations

Each module task follows the same sequence:

1. Add/update focused source-governance assertions for that single view.
2. Replace legacy common styles with canonical styles.
3. Remove only that view's local common-control templates after zero local references; preserve domain-specific templates.
4. Preserve bindings, names, event handlers, commands, and view-model behavior.
5. Build the app and run the module's existing focused tests plus design-system tests.
6. Verify Light, Dark, High Contrast, keyboard, focus, 100–200% scaling, 1280×720 narrow behavior, and one standard/wide desktop size.
7. Update the migration baseline counts.
8. Stop for review.

### Task 12.1: File Utilities

**Files:** `src/03_App/Nexus.App/Views/FileUtilitiesView.xaml`, `src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs`, `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`.

**Acceptance:** Common controls use canonical styles; file-specific content remains local.

**Authorized-execution commit:** `refactor: migrate File Utilities to Nexus design system v1`

### Task 12.2: Settings

**Files:** `src/03_App/Nexus.App/Views/SettingsView.xaml`, `src/03_App/Nexus.Integration.Tests/SettingsViewModelTests.cs`, and the two design-system test files.

**Acceptance:** Settings inputs/buttons use canonical styles; bindings and persisted settings behavior are unchanged.

**Authorized-execution commit:** `refactor: migrate Settings to Nexus design system v1`

### Task 12.3: Home

**Files:** `src/03_App/Nexus.App/Views/HomeView.xaml` and the two design-system test files.

**Acceptance:** Home follows Wide/Standard/Narrow behavior and no longer forces three columns at insufficient width.

**Authorized-execution commit:** `refactor: migrate Home to Nexus design system v1`

### Task 12.4: Rapikan File

**Files:** `src/03_App/Nexus.App/Views/RapikanFileView.xaml` and the two design-system test files.

**Acceptance:** File workflow behavior is unchanged; all common inputs/actions/list chrome are canonical.

**Authorized-execution commit:** `refactor: migrate Rapikan File to Nexus design system v1`

### Task 12.5: Clean Downloads

**Files:** `src/03_App/Nexus.App/Views/CleanDownloadsView.xaml` and the two design-system test files.

**Acceptance:** Cleanup workflow and bindings are unchanged; common controls and feedback are canonical.

**Authorized-execution commit:** `refactor: migrate Clean Downloads to Nexus design system v1`

### Task 12.6: PDF Utilities

**Files:** `src/03_App/Nexus.App/Views/PdfUtilitiesView.xaml`, its code-behind only if visual-state wiring requires it, and the two design-system test files.

**Acceptance:** PDF page preview/thumbnail content remains domain-specific; buttons, sliders, progress, toolbar, menus, and layout use canonical contracts.

**Authorized-execution commit:** `refactor: migrate PDF Utilities to Nexus design system v1`

### Task 12.7: Image Subsystem

**Files:** `src/03_App/Nexus.App/Views/ImageSubsystemView.xaml`, `src/03_App/Nexus.App/Views/ImageSubsystemView.xaml.cs` only if visual-state wiring requires it, `src/03_App/Nexus.Integration.Tests/ImageSubsystemWorkflowTests.cs`, and the design-system tests.

**Acceptance:** Checkerboard, canvas, compare, pan/zoom, scaling, and color preview remain domain-specific; common controls/layout/feedback are canonical and image cache/disposal behavior is preserved.

**Authorized-execution commit:** `refactor: migrate Image Subsystem to Nexus design system v1`

### Task 12.8: Conversation

**Files:** `src/03_App/Nexus.App/Views/ConversationView.xaml`, `src/03_App/Nexus.App/AIWorkspace/Rendering/MessageContentPresenter.cs` only if presentation hookup requires it, `src/03_App/Nexus.Integration.Tests/AIWorkspaceRenderingTests.cs`, and the design-system tests.

**Acceptance:** Message bubbles and Markdown/code/table rendering remain domain-specific; toolbar/actions/input/scroll/focus/feedback become canonical without changing streaming or cancellation behavior.

**Authorized-execution commit:** `refactor: migrate Conversation to Nexus design system v1`

### Task 12.9: Shell navigation and dialogs

**Files:** `src/03_App/Nexus.App/MainWindow.xaml`, `src/03_App/Nexus.App/MainWindow.xaml.cs` only if focus restore or overlay lifecycle requires it, `src/03_App/Nexus.Integration.Tests/NavigationLifecycleTests.cs`, and the design-system tests.

**Acceptance:** Navigation uses canonical 44 px items and responsive layout; shell dialogs follow the one-dialog lifecycle; navigation and shutdown behavior remain unchanged.

**Authorized-execution commit:** `refactor: migrate Nexus shell to design system v1`

---

## Stage 6 — Full Verification, Compatibility Closure, Final Audit

### Task 13.1: Run the Frozen verification matrix

**Files:**

- Create: `docs/design-system/frozen-verification.md`
- Modify: none

**Automated verification:**

```powershell
dotnet build .\src\03_App\Nexus.App\Nexus.App.csproj --configuration Release --no-restore
dotnet test .\src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --configuration Release --no-restore
```

**Manual/runtime matrix:**

- Light, Dark, High Contrast.
- Keyboard traversal and activation.
- Focus visibility, trap, and restore.
- Screen reader name/role/state/announcement.
- Reduced Motion.
- Text and desktop scaling at 100%, 125%, 150%, 175%, 200%.
- 1280×720, 1366×768, 1600×900, 1920×1080, 2560-class, 3840-class.
- Visual regression for every module and lifecycle state.
- Release startup, steady-state memory, navigation, shutdown, theme switching, and large-image preview.
- Template-depth and repeated-item virtualization/shadow checks.

**Acceptance:** Every specification section 28 criterion has evidence. Any failure keeps `Frozen: BLOCKED`.

**Rollback:** Documentation-only task.

**Authorized-execution commit:** `docs: record Nexus design-system frozen verification`

### Task 13.2: Prove compatibility closure

**Files:**

- Modify: `docs/design-system/frozen-verification.md`
- Modify: none in production

**Steps:**

1. Search all non-generated source for every compatibility key.
2. Record exact zero-reference evidence per key.
3. Confirm governance allowlists are empty.
4. Do not delete aliases in this task.
5. Request separate approval for alias removal and status transition.

**Acceptance:** Compatibility removal is safe and reviewable, but no alias is removed without new authorization.

**Rollback:** Documentation-only task.

**Authorized-execution commit:** `docs: prove Nexus compatibility closure`

### Frozen Status Decision

`APPROVED STANDARD` is already valid for the design decision. `FROZEN` remains blocked until:

- all stages required by actual product use are implemented;
- all module views consume canonical common styles;
- no local common-control template remains;
- ComboBox/ComboBoxItem and specialization rules pass;
- optional custom components obey their decision gates;
- the complete accessibility, scaling, visual, build, behavioral, and performance matrices pass;
- compatibility closure has zero-reference evidence.

The final status change and any compatibility-alias deletion require separate explicit approval.
