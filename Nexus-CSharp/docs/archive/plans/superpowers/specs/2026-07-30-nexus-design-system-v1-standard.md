# Nexus Design System v1 — Final Standard

**Status:** APPROVED STANDARD  
**Architecture Decision:** COMPLETE  
**Frozen:** BLOCKED  
**Implementation:** NOT AUTHORIZED  
**Implementation plan:** Separate staged document; execution requires explicit approval.

## 1. Purpose

Nexus Design System v1 defines one framework-neutral desktop design language for every Nexus module. No existing module is a visual reference. Existing views are evidence of legacy behavior only.

The standard prioritizes, in order:

1. Consistent styles and behavior across modules.
2. Button, ComboBox, SplitButton/MenuButton decision rules, and input controls.
3. Theme and token ownership.
4. Focus and accessibility.
5. Layout and responsive behavior.
6. Feedback and dialogs.
7. Performance.

## 2. Design Principles

1. A function has one canonical visual and behavioral contract.
2. Visual differences require a real functional difference.
3. Common components are shared; modules consume them.
4. Global styles are named by function, never by module.
5. Native WPF semantics are preserved where practical.
6. Focus, keyboard, screen reader, High Contrast, scaling, and Reduced Motion are component requirements.
7. Motion is functional, short, consistent, and optional.
8. Shadow communicates elevation and is not decorative.
9. Card is used only for meaningful grouping, an action boundary, status grouping, or real elevation.
10. Status is never communicated by color alone.
11. ViewModels expose state and commands; they do not create `Brush`, `Color`, shadow, radius, or other presentation values.
12. Domain-specific rendering may remain local, but common chrome still uses semantic tokens and canonical controls.

## 3. Token Architecture

The required dependency direction is:

```text
Primitive Token
      ↓
Semantic Token
      ↓
Component Token and Component Style
      ↓
Module View
```

### 3.1 Primitive tokens

Primitive tokens store context-free raw values:

- palette steps;
- spacing values;
- radius values;
- stroke widths;
- control sizes;
- typography measurements;
- icon sizes;
- motion durations and easing;
- elevation parameters.

Modules must not reference primitive tokens directly.

### 3.2 Semantic tokens

Semantic tokens assign meaning to primitives:

- surfaces;
- foregrounds;
- strokes;
- accent states;
- status states;
- focus;
- scrim;
- typography roles;
- elevation roles;
- motion roles.

Light, Dark, and High Contrast mappings provide semantic values.

### 3.3 Component tokens and styles

Component tokens exist only when semantic tokens are insufficient, for example button padding, ComboBox item height, or navigation indicator width.

Component tokens must not contain business state.

### 3.4 Module views

Modules may consume:

- semantic tokens;
- canonical component styles;
- shared interaction behaviors;
- documented domain-specific exceptions.

Modules must not:

- contain common hex colors;
- reference primitive tokens directly;
- define common-control templates;
- redefine focus, hover, pressed, or disabled chrome;
- create module-named global styles.

Local styles may add domain state only through `BasedOn` and must not replace the canonical template.

## 4. Single Source of Truth and Ownership

| Domain | Final owner |
|---|---|
| Primitive values | Primitive token dictionaries |
| Light semantic values | Light semantic theme dictionary |
| Dark semantic values | Dark semantic theme dictionary |
| High Contrast values | High Contrast semantic mapping |
| Typography | Typography token dictionary |
| Geometry, spacing, and sizing | Geometry token dictionary |
| Motion | Motion token dictionary |
| Elevation and shadow | Elevation token dictionary |
| Shared styles and templates | Component dictionaries |
| Theme selection and dictionary switching | `ThemeManager` |
| Reduced Motion and other visual accessibility preferences | `ApplicationVisualPreferences` |
| Icon name to glyph/vector mapping | `NexusIcon` |
| Shared interaction behavior | Shared behaviors |
| Module View | Consumer |
| ViewModel | State and commands only |

`ThemeManager` is the sole owner of:

- Light;
- Dark;
- High Contrast mapping;
- semantic dictionary switching;
- theme-change notification.

Reduced Motion is not a theme. `ApplicationVisualPreferences` owns Reduced Motion and other non-theme visual preferences. `ThemeManager` may read those preferences but must not own them.

Theme switching must not rebuild unrelated resource dictionaries.

## 5. Canonical Semantic Color Roles

### Surface

- `NexusSurfaceCanvas`
- `NexusSurfaceBase`
- `NexusSurfaceRaised`
- `NexusSurfaceOverlay`
- `NexusSurfaceSunken`

### Foreground

- `NexusForegroundPrimary`
- `NexusForegroundSecondary`
- `NexusForegroundDisabled`
- `NexusForegroundOnAccent`

### Stroke

- `NexusStrokeSubtle`
- `NexusStrokeDefault`
- `NexusStrokeStrong`
- `NexusStrokeFocus`

### Accent

- `NexusAccentRest`
- `NexusAccentHover`
- `NexusAccentPressed`
- `NexusAccentSelected`

### Status

- `NexusInfoForeground`
- `NexusInfoBackground`
- `NexusInfoStroke`
- `NexusSuccessForeground`
- `NexusSuccessBackground`
- `NexusSuccessStroke`
- `NexusWarningForeground`
- `NexusWarningBackground`
- `NexusWarningStroke`
- `NexusErrorForeground`
- `NexusErrorBackground`
- `NexusErrorStroke`

### Accessibility

- `NexusFocusOuter`
- `NexusFocusInner`
- `NexusScrim`

The existing undefined `NexusBrushStatusInfo` becomes a temporary compatibility alias to `NexusInfoStroke`, matching its proven legacy use as a border brush. It is not a canonical token.

The legacy `AccentPrimaryBrush` and `NexusBrushAccent` families must resolve to one canonical accent family during migration.

## 6. Geometry, Stroke, and Size

### Radius scale

- None: `0`
- Small: `4`
- Medium: `8`
- Large: `12`
- XLarge: `16`

Component mapping:

| Component | Radius |
|---|---:|
| Button | 8 |
| TextBox and SearchBox | 8 |
| ComboBox | 8 |
| SplitButton and MenuButton | 8 |
| MenuItem | 8 |
| Popup, Flyout, Menu, and Card | 12 |
| Dialog | 16 |
| Progress track | 4 |

Values `5`, `7`, `10`, `11`, `13`, `14`, `15`, `18`, `19`, `20`, and `28` are not core radius values.

### Circular rule

`NexusRadiusCircular` is a component contract, not a fixed primitive `CornerRadius`.

- Circular is used only for components whose function calls for a circle.
- `Width` must equal `Height`.
- The canonical template computes half the actual size or uses a sufficiently large internal radius.
- Modules must not calculate or assign a circular radius.
- The name `NexusRadiusCircular` may identify the contract but must not be exposed as a universal static primitive value.

### Stroke widths

- None: `0`
- Standard: `1`
- Strong: `2`
- Focus ring: `2`

### Control heights

- Standard input/action: `40`
- Compact toolbar: `32`
- Large action: `44`
- Navigation item: `44`

Adjacent Button, TextBox, ComboBox, and SplitButton controls must have equal visual height.

## 7. Spacing

Canonical scale:

- `2`
- `4`
- `8`
- `12`
- `16`
- `24`
- `32`
- `40`

Minimum mapping:

| Role | Value |
|---|---:|
| Icon to text | 8 |
| Related controls | 8 or 12 |
| Form row | 16 |
| Section | 24 |
| Standard page inset | 24 |
| Spacious page modifier | 32 |

The primary page inset is `24`. A `32` inset is allowed only as a documented spacious layout modifier. Local `20`, `28`, and `40` page insets are not retained without a documented layout reason.

## 8. Typography and Icons

Font stack:

1. Segoe UI Variable
2. Segoe UI

| Role | Size | Weight |
|---|---:|---|
| Caption | 12 | Normal |
| Body | 14 | Normal |
| Body Strong | 14 | SemiBold |
| Subtitle | 16 | SemiBold |
| Title | 20 | SemiBold |
| Display | 28 | SemiBold |

Line height belongs to the typography role. Text sizes `9–11` must not carry important information.

Icon measurements are separate:

- Small: `12`
- Standard: `16`
- Navigation: `20`
- Status: `24`
- Illustration: `40`

Larger domain illustrations are content exceptions, not typography roles.

## 9. Motion and Elevation

### Motion

- Fast: `100 ms`
- Standard: `160 ms`
- Slow: `240 ms`
- Enter: EaseOut
- Exit: EaseIn
- Move/resize: EaseInOut
- Linear: continuous progress only

Reduced Motion disables scale, slide, stagger, and decorative motion. State changes must remain understandable without animation.

### Elevation

- Level 0: base/inline
- Level 1: genuinely raised card
- Level 2: popup/menu/flyout
- Level 3: dialog
- Level 4: exceptional shell overlay

Shadow is not the default Card treatment and is forbidden on repeated items.

## 10. Component Classification

### A. Canonical Style

Native WPF controls with canonical styles:

- Button
- ComboBox and ComboBoxItem
- TextBox
- CheckBox
- RadioButton
- Slider
- ProgressBar
- Menu and MenuItem
- ContextMenu
- ScrollViewer and ScrollBar

### B. Canonical Specialized Style

Native controls with a specialization that preserves the canonical template:

- SearchBox
- IconButton
- ToolbarButton
- Segmented Item
- Navigation Item

### C. Canonical Custom Component

Requires a custom control or shared behavior:

- SplitButton
- MenuButton
- Flyout
- Toast
- reusable Status Panel, if implemented as a component

No custom component may be implemented before its functional contract, keyboard behavior, `AutomationProperties`, focus lifecycle, and lifecycle tests exist.

## 11. Canonical Component Style Names

- `NexusPrimaryButtonStyle`
- `NexusSecondaryButtonStyle`
- `NexusSubtleButtonStyle`
- `NexusToolbarButtonStyle`
- `NexusDangerButtonStyle`
- `NexusIconButtonStyle`
- `NexusComboBoxStyle`
- `NexusComboBoxItemStyle`
- `NexusSplitButtonStyle`
- `NexusMenuButtonStyle`
- `NexusTextBoxStyle`
- `NexusSearchBoxStyle`
- `NexusToggleStyle`
- `NexusCheckBoxStyle`
- `NexusRadioButtonStyle`
- `NexusSegmentedControlStyle`
- `NexusSegmentedItemStyle`
- `NexusCardStyle`
- `NexusStatusPanelStyle`
- `NexusDialogStyle`
- `NexusPopupStyle`
- `NexusFlyoutStyle`
- `NexusMenuStyle`
- `NexusMenuItemStyle`
- `NexusContextMenuStyle`
- `NexusProgressBarSubtleStyle`
- `NexusProgressBarStandardStyle`
- `NexusSliderStyle`
- `NexusScrollViewerStyle`
- `NexusScrollBarStyle`
- `NexusNavigationItemStyle`

`NexusSplitButtonStyle` status is:

**APPROVED CONTRACT, IMPLEMENTATION ON DEMAND.**

It is not created or introduced merely to complete the catalog.

## 12. Button System

Variants:

- Primary
- Secondary
- Subtle
- Toolbar
- Danger
- Icon

| Variant | Height | Surface | Stroke | Primary use |
|---|---:|---|---|---|
| Primary | 40 | Accent Rest | Accent Rest | Primary page/dialog action |
| Secondary | 40 | Base/Raised | Default | Normal action |
| Subtle | 40 | Transparent | None | Low-emphasis action |
| Toolbar | 32 | Transparent | None | Dense toolbar |
| Danger | 40 | Error Background | Error Stroke | Destructive action |
| Icon | 40, or 32 in toolbar | Subtle behavior | Contextual | Icon-only action |

All variants share:

- radius `8`;
- Body Strong typography;
- standard icon size `16`;
- icon-text gap `8`;
- focus ring;
- hover;
- one pressed pattern;
- semantic disabled treatment;
- loading behavior;
- keyboard activation;
- accessible name.

Pressed uses semantic pressed background/stroke. Optional scale is capped at `0.98`, does not affect layout, and is disabled by Reduced Motion.

Loading:

- prevents re-entry;
- shows a loading indicator;
- preserves label and width when possible;
- is announced as busy;
- is visually distinct from ordinary disabled.

## 13. ComboBox System

One canonical ComboBox and ComboBoxItem template covers:

- Rest
- Hover
- Focused
- Open
- Closed
- Selected
- Empty
- Disabled
- Invalid
- Loading items

Visual contract:

- height `40`;
- radius `8`;
- Body typography;
- canonical chevron;
- semantic border and focus ring;
- popup radius `12`;
- elevation level `2`;
- consistent item height;
- readable disabled item;
- popup minimum width follows control width;
- edge-aware popup placement.

Keyboard:

- Up/Down;
- Home/End;
- Enter;
- Escape;
- normal Tab traversal;
- focus restore after close.

Local `ComboBoxItem` specialization must use:

```xaml
BasedOn="{StaticResource NexusComboBoxItemStyle}"
```

It may add a domain state such as unavailable. It must not replace the canonical template.

## 14. SplitButton and MenuButton Decision Rule

SplitButton is allowed only when:

- a default action exists;
- alternatives remain in the same action family;
- the primary area remains directly executable.

It is not used to select a mode or value.

Use instead:

- Button for one action;
- MenuButton for actions without a default;
- ComboBox for values/modes;
- Segmented Control for 2–5 equal frequent modes.

If a real SplitButton need is approved, its contract is:

- one outer border;
- radius `8`;
- height `40`;
- primary and menu regions;
- internal separator;
- per-region hover/pressed;
- whole-control disabled state;
- keyboard and focus behavior;
- accessible names for both actions.

The two regions must not look like separate adjacent Buttons.

## 15. TextBox and SearchBox

TextBox states:

- Rest
- Hover
- Focused
- Readonly
- Disabled
- Invalid
- Valid when useful
- Placeholder
- Clear action when relevant

Single-line contract:

- height `40`;
- radius `8`;
- Body typography;
- canonical padding;
- semantic validation;
- placeholder does not replace an external label.

SearchBox specializes TextBox without replacing its common chrome. It adds:

- search icon;
- clear action;
- Escape-to-clear;
- accessible name;
- the same focus behavior as TextBox.

## 16. Toggle, CheckBox, RadioButton, and Segmented Control

- Toggle/Switch: immediate binary setting.
- CheckBox: independent option.
- RadioButton: 2–5 mutually exclusive choices that should remain visible.
- Segmented Control: 2–5 equal, frequent modes.
- ComboBox: more choices or constrained space.

All share semantic focus, hover, pressed, selected/checked, disabled, High Contrast, text scaling, and accessible state conventions.

Segmented Control is canonical and is not recreated per module.

## 17. Card, Panel, and Status Panel

Card:

- radius `12`;
- stroke `1`;
- padding `16` or `24`;
- no default shadow;
- elevation only for a genuinely raised surface.

Panel is a structural surface and does not automatically receive radius, border, or shadow.

Status Panel contains:

- icon;
- title;
- message;
- optional action;
- optional progress;
- semantic status;
- screen-reader announcement.

It never relies on color alone.

## 18. Dialog, Popup, Flyout, and Menu

Dialog lifecycle:

1. Save current focus.
2. Open dialog and move focus appropriately.
3. Trap Tab traversal.
4. Support Enter when safe.
5. Support Escape/cancel.
6. Label destructive action explicitly.
7. Close.
8. Restore focus to the trigger.

Dialog structure:

- title;
- optional description;
- content;
- validation/status area;
- primary action;
- secondary/cancel action.

Dialog uses radius `16`, elevation `3`, semantic scrim, and an accessible name.

Popup, Flyout, Menu, and ContextMenu share:

- radius `12`;
- elevation `2`;
- overlay surface;
- consistent padding;
- edge-aware placement;
- Escape-to-close;
- focus restore;
- High Contrast boundary.

Menu layout supports icon, label, shortcut, and submenu columns.

Native MessageBox remains only for classified blocking system-level fallback. It must not remain a parallel general notification system.

## 19. Layout Contract

```text
Page
├─ Header
├─ Command/Toolbar area (optional)
├─ Content
└─ Status/Footer (optional)
```

Standard page inset is `24`.

Toolbar supports:

- leading actions;
- trailing actions;
- overflow;
- icon size `16`;
- hit target `32`;
- keyboard traversal;
- accessible names.

Layout modes:

- Wide: workspace plus full side panel.
- Standard: workspace plus bounded narrower panel.
- Narrow: panel becomes a secondary view, Flyout, or vertical stack.

Breakpoints are not frozen before runtime testing.

Home/dashboard must not always force three columns.

## 20. Feedback Contract

Canonical feedback:

- Inline Message
- Page Banner
- Toast
- Status Panel
- Dialog
- Validation Message

Decision rules:

| Condition | Pattern |
|---|---|
| Field invalid | Validation Message |
| Recoverable section/task issue | Inline Message |
| Persistent page-wide issue | Page Banner |
| Short non-blocking completion | Toast |
| Persistent/background task | Status Panel |
| Blocking decision/error | Dialog |
| Destructive confirmation | Dialog |

Progress variants:

- Subtle: `4`
- Standard: `8`
- Determinate
- Indeterminate

Legacy heights `5` and `7` do not remain common variants.

Toast is non-blocking, concise, dismissible, announced to screen readers, and never the sole location of important information.

## 21. Accessibility Contract

Focus:

- `2 px`;
- no layout change;
- visible in Light, Dark, and High Contrast;
- preserved with selected/checked state;
- restored after Dialog, Popup, Flyout, or Menu.

All interactive components retain:

- logical Tab traversal;
- native activation semantics;
- appropriate arrow/Escape behavior;
- accessible role;
- accessible name;
- accessible state;
- help text where needed.

Every icon-only Button has:

- ToolTip;
- `AutomationProperties.Name`.

Contrast acceptance:

- normal text `≥4.5:1`;
- large text `≥3:1`;
- non-text interactive boundary `≥3:1`.

Text scaling must not clip important content. High Contrast uses system-aware semantic mappings. Reduced Motion comes from `ApplicationVisualPreferences`.

## 22. Performance Contract

1. Shadow is not default Card chrome.
2. Repeated items do not use `DropShadowEffect`.
3. Repeated lists use virtualization or a bounded item count.
4. `ThemeManager` alone switches semantic theme dictionaries.
5. Theme switching does not rebuild unrelated dictionaries.
6. Common component templates should remain at or below six visual levels.
7. Repeated item templates should remain at or below eight visual levels.
8. Pages above twelve visual levels require profiling.
9. Animations do not change layout across many repeated items.
10. `DynamicResource` is limited to theme-dependent values.
11. Static geometry and size use shared tokens.
12. Large image previews have scaling, cache, and disposal lifecycle.
13. Runtime performance is verified using the real Release executable.

Depth thresholds are review gates, not automatic failures.

## 23. Compatibility Aliases

Compatibility aliases are temporary. New code must not consume them.

| Legacy key | Canonical target |
|---|---|
| `NexusButtonPrimary` | `NexusPrimaryButtonStyle` |
| `RemoveBackgroundPrimaryActionStyle` | `NexusPrimaryButtonStyle` |
| `RemoveBackgroundModernButtonStyle` | `NexusSecondaryButtonStyle` |
| `RemoveBackgroundToolbarButtonStyle` | `NexusToolbarButtonStyle` |
| `ImageToolsIconButtonStyle` | `NexusIconButtonStyle` |
| `AIMessageActionButtonStyle` | `NexusIconButtonStyle` or `NexusSubtleButtonStyle`, after usage classification |
| `NavButtonStyle` | `NexusNavigationItemStyle` |
| `NexusTextBoxStandard` | `NexusTextBoxStyle` |
| `RemoveBackgroundSettingsTextBoxStyle` | `NexusTextBoxStyle` |
| `NexusComboBoxStandard` | `NexusComboBoxStyle` |
| `NexusCheckBoxStandard` | `NexusCheckBoxStyle` |
| `NexusRadioButtonStandard` | `NexusRadioButtonStyle` |
| `NexusToggleButtonStandard` | `NexusToggleStyle` |
| `NexusSliderStandard` | `NexusSliderStyle` |
| `NexusProgressBarStandard` | `NexusProgressBarStandardStyle` |
| `RemoveBackgroundProgressStyle` | `NexusProgressBarStandardStyle` |
| `NexusCardStandard` | `NexusCardStyle` |
| `NexusDialogStandard` | `NexusDialogStyle` |
| `NexusScrollViewerStandard` | `NexusScrollViewerStyle` |
| `NexusMenuStandard` | `NexusMenuStyle` |
| `NexusContextMenuStandard` | `NexusContextMenuStyle` |
| `NexusBrushStatusInfo` | `NexusInfoStroke` |

`NexusBackgroundComboBoxStyle` requires manual classification as ComboBox, MenuButton, or domain-specific selector and does not receive an automatic alias.

## 24. Local Styles to Retire

The following stop being sources for new work:

- local `RemoveBackground*ButtonStyle` copies;
- local `RemoveBackgroundSettingsTextBoxStyle`;
- local `RemoveBackgroundProgressStyle`;
- `ImageToolsIconButtonStyle`;
- `PdfPageSegmentStyle`;
- `AIMessageActionButtonStyle`;
- `AIWorkspaceToolbarChipStyle`;
- local implicit common Button, ComboBox, TextBox, ToolTip, and ProgressBar chrome;
- local common shadows;
- local `BooleanToVisibilityConverter` declarations;
- module-local typography styles that duplicate canonical roles.

Retirement does not authorize deletion. A resource may be removed only after migration, zero-reference evidence, and a separately approved closure task.

## 25. Allowed Domain-Specific Exceptions

Allowed local content/behavior includes:

- transparency checkerboard;
- image canvas;
- before/after compare thumb;
- pan/zoom transforms;
- image scaling and decoding;
- CMYK/color preview;
- PDF page preview and thumbnail content;
- Conversation message bubble geometry;
- Markdown/code/table rendering;
- file preview content;
- domain-specific `DataTemplate`;
- unavailable domain state added through `BasedOn`;
- documented decorative artwork that passes contrast and performance review.

Exceptions may customize content and domain state. They must not replace common-control templates or common focus/state chrome.

## 26. Safe Migration Order

1. Token and semantic foundation.
2. Theme ownership and `ApplicationVisualPreferences`.
3. Compatibility aliases and governance checks.
4. Accessibility foundation.
5. Button.
6. TextBox and SearchBox.
7. ComboBox and ComboBoxItem.
8. Toggle, CheckBox, and RadioButton.
9. Segmented Control.
10. SplitButton/MenuButton only when a proven need exists.
11. Surfaces, feedback, dialog, popup, flyout, and menu.
12. Layout contract.
13. Low-risk modules: File Utilities, Settings, Home.
14. Medium-risk modules: Rapikan File, Clean Downloads.
15. Specialized modules: PDF, Image.
16. Conversation.
17. Shell navigation/dialog.
18. Verification and compatibility closure.

Every migration stage must be small, buildable, testable, reversible, scoped to its named files, and preserve existing behavior and bindings unless the stage explicitly changes them.

## 27. Design-System Governance

### 27.1 New style, template, and component decision rule

Before creating any style, template, or component, the author must perform this review in order:

1. Use an existing canonical style.
2. If only additional behavior or state is required, extend the canonical style through `BasedOn`.
3. If the requirement cannot be represented by a canonical style, submit a Design System change proposal.
4. Create a new global style only after design review approval.

A style must never be created merely because its consumer belongs to a different module.

### 27.2 Module template prohibition

A module must not own its own:

- Button template;
- ComboBox template;
- TextBox template;
- ProgressBar template;
- ScrollBar template;
- Menu template.

An exception is permitted only when it is recorded as a domain-specific exception in this Design System. The exception must document why canonical content/state extension is insufficient and must not silently redefine common-control behavior.

### 27.3 Mandatory UI code-review checklist

Every UI change must satisfy and record all applicable checks:

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

Any intentional change to accessibility, keyboard behavior, or component lifecycle requires an approved Design System change before the UI change can be accepted.

## 28. Frozen Acceptance Criteria

`APPROVED STANDARD` means this design decision is accepted. It does not mean implementation is complete.

`FROZEN` requires all of the following:

### Architecture

- One primitive layer, semantic layer, and component layer.
- No module directly consumes primitives.
- No module owns a common-control template.
- No module-named global styles.
- No ViewModel-generated visual Brush.

### Theme

- Light, Dark, and High Contrast pass.
- One semantic brush family.
- No undefined resources.
- Theme switching is owned only by `ThemeManager`.
- Reduced Motion is owned by `ApplicationVisualPreferences`.

### Components

- Button appearance and behavior match across modules.
- ComboBox and ComboBoxItem use one canonical template.
- ComboBoxItem specialization uses `BasedOn`.
- TextBox/SearchBox and selection controls pass their lifecycle matrices.
- SplitButton/MenuButton follow the decision rule.
- No local common-control templates remain.

### Accessibility and scaling

- Keyboard traversal passes.
- Focus visibility and restore pass.
- Screen-reader name, role, state, and announcements pass.
- Contrast passes.
- High Contrast passes.
- Reduced Motion passes.
- Text scaling passes.
- 100%, 125%, 150%, 175%, and 200% scaling pass.
- 1280×720 through 3840-class desktop layouts pass.

### Feedback and performance

- Feedback patterns are consistently classified.
- MessageBox usage is classified.
- Validation is accessible.
- Repeated lists are virtualized or bounded.
- No repeated-item shadows.
- Pages over the depth threshold are profiled.
- Real Release performance, memory, shutdown, and visual regression checks pass.

### Verification

- XAML parse passes.
- Build passes.
- Focused component tests pass.
- Governance tests pass.
- Visual regression review passes.
- Keyboard/manual flows pass.
- UI Automation checks pass.
- Scaling matrix passes.
- Performance profile passes.
- Compatibility closure has zero-reference evidence before deletion.

Until every criterion passes, the status remains:

**APPROVED STANDARD — FROZEN BLOCKED.**
