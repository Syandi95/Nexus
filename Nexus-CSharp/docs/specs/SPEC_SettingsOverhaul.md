# Specification: Settings Overhaul — Nexus Configuration Center

## 1. Objective

Transform Settings from a loose collection of preferences into the authoritative configuration center for Nexus. Every persisted setting must flow through a verified pipeline (UI → ViewModel → SettingsManager → ConfigService → AppConfigRecord → JSON file) and upon reload produce identical application behavior.

---

## 2. Architecture & Data Flow

### 2.1 Pipeline (Load → Edit → Save → Reload)

```
┌──────────────┐     ┌──────────────────┐     ┌──────────────────────┐     ┌──────────────────┐
│ SettingsView │────▶│ SettingsViewModel │────▶│ SettingsManagerService│────▶│   ConfigService   │
│    (XAML)    │     │    (WPF VM)       │     │   (Module Service)   │     │  (Foundation)     │
└──────────────┘     └──────────────────┘     └──────────────────────┘     └──────────────────┘
       ▲                       │                                                  │
       │                       │                                                  ▼
       │              Bindings propagate                                   ┌──────────────────┐
       │              (TwoWay/OneWay)                                      │  AppConfigRecord  │
       │                                                                   │   (POCO Model)    │
       │                                                                   └──────────────────┘
       │                                                                          │
       │                                                                          ▼
       │                                                                   ┌──────────────────┐
       └───────────────────────────────────────────────────────────────────│ nexus_data/      │
                                                                           │ config.json      │
                                                                           └──────────────────┘
```

### 2.2 Load Sequence (App Startup)

1. `App.xaml.cs` → `new ConfigService()` → constructor calls `LoadConfig()` → reads `nexus_data/config.json` → deserializes to `AppConfigRecord` → cached in `_currentConfig`
2. `SettingsViewModel` constructor → `LoadSettings()` → calls `_settingsManagerService.GetTheme()`, `GetDownloadsPath()`, `GetConfirmExit()`, `GetConfirmModule()` → each delegates to `_configService.GetConfig()` → getters on `AppConfigRecord`
3. VM properties propagate to View via WPF bindings
4. `MainWindow.xaml.cs` header theme toggle reads `_isDarkMode` field — **this is the duplication problem**

### 2.3 Save Sequence

1. User clicks "Simpan Pengaturan" → `SaveSettingsCommand` → `SaveSettingsAsync()`
2. Validates `DownloadsFolder` (non-empty, warns if directory doesn't exist)
3. Calls `_settingsManagerService.SaveSettingsAsync(theme, folder, confirmExit, confirmModule)`
4. Service mutates fields on the in-memory `AppConfigRecord` (which ConfigService already holds), then calls `_configService.SaveConfig(config)` → serializes to JSON → writes to file

### 2.4 Reset Sequence

1. User clicks "Reset ke Default" → confirms dialog
2. `_settingsManagerService.ResetToDefaultAsync()` → creates `new AppConfigRecord()` → `_configService.SaveConfig(newConfig)` → overwrites file
3. `LoadSettings()` re-reads from service → UI refreshes

### 2.5 Problem: Theme Toggle Duplication

| Location | Persisted? | Source of Truth |
|---|---|---|
| `SettingsViewModel` `IsLightTheme`/`IsDarkTheme` | ✅ Via `ConfigService` | ✅ Correct |
| `MainWindow.xaml.cs` `_isDarkMode` field | ❌ Never persisted | ❌ Drifts |
| `MainWindow.xaml.cs` `BtnTheme_Click` | ❌ Ad-hoc toggle | ❌ Drifts |

**Fix**: Replace the old `BtnTheme`/`BtnTheme_Click` button with a **Day & Night Toggle Switch** in the header. This toggle:

- Writes through `SettingsManagerService.SaveSettingsAsync()` → `ConfigService` → `AppConfigRecord` (single source of truth)
- Is a custom animated slide control (not a standard ToggleSwitch, not a RadioButton — see Section 7)
- On toggle, immediately persists AND applies the theme (same as current "Simpan Pengaturan" behavior)
- The Settings page theme selector updates to reflect the change (since both read from same `ConfigService`)

### 2.6 Unsaved Changes Detection

A new `SettingsViewModel.IsDirty` flag tracks whether any setting has been modified since the last save:

1. Every setter in `SettingsViewModel` sets `IsDirty = true` (except during `LoadSettings()`)
2. `SaveSettingsAsync()` sets `IsDirty = false`
3. `ResetSettingsAsync()` → `LoadSettings()` sets `IsDirty = false`
4. Before navigating away from Settings page OR closing the app, if `IsDirty == true`, show the **Unsaved Changes Dialog** (see Section 7.3)

---

## 3. UI/UX — Design System Alignment

### 3.1 Target Design System

Reference: `ImageSubsystemView.xaml` (lines 167–205 for buttons, 242–270 for text boxes, 514–685 for panel layout).

All controls in SettingsView must match:

| Control | Target Style | Source (Image Tools) |
|---|---|---|
| Card / Panel | `SurfacePrimaryBrush` background, `BorderSubtleBrush` border, `CornerRadius="18"` | Lines 515–517 |
| Section header icon | `Border` with `Width="34"`, `Height="34"`, `CornerRadius="11"`, `SurfaceSecondaryBrush` bg | Line 521 |
| Section header text | `FontSize="17"`, `FontWeight="SemiBold"` | Lines 525–526 |
| Primary button | `RemoveBackgroundPrimaryActionStyle` (`AccentPrimaryBrush`, `CornerRadius="13"`, 86% opacity on press) | Lines 207–240 |
| Modern button | `RemoveBackgroundModernButtonStyle` (`SurfaceSecondaryBrush`, `CornerRadius="12"`, `BorderSubtleBrush` border, hover → `AccentPrimaryBrush` border) | Lines 167–205 |
| TextBox | `RemoveBackgroundSettingsTextBoxStyle` (`SurfaceSecondaryBrush`, `CornerRadius="11"`, hover → `AccentPrimaryBrush` border) | Lines 242–270 |
| Icon button (38×38) | `RemoveBackgroundToolbarButtonStyle` | Lines 16–64 |
| ComboBox | `NexusComboBoxStyle` (already used at line 540) | Keep as-is |
| RadioButton | `NexusRadioButtonStandard` (already used at line 576) | Keep as-is |
| CheckBox | `NexusCheckBoxStandard` (already used at line 79) | Keep as-is |
| Slider | `RemoveBackgroundZoomSliderStyle` (or shared variant) | Lines 121–153 |
| Expander | Used in Image Tools for advanced settings (line 621) | Adopt for nested config |

### 3.2 Color Management Section — Layout Change

**Current**: Import ICC Profile is a full-width button (`NexusButtonPrimary`), same width as the card.

**Target** (Image Tools layout pattern):
- ComboBox for profile selection → full width
- Import button → `RemoveBackgroundPrimaryActionStyle` with `Padding="16,10"`, same visual weight as "Browse" button in Downloads Folder section, not full card width
- Detail box → `SurfaceSecondaryBrush` background, `CornerRadius="11"`, consistent with Image Tools info boxes (lines 531–536, 556–561)
- Warning box → same but with `BorderBrush="{DynamicResource NexusBrushStatusDanger}"` and `BorderThickness="1"`

### 3.3 Language

All UI text in English, consistent with Image Tools naming conventions:
- "Settings" (not "Pengaturan")
- "Application Theme" (not "TEMA APLIKASI")
- "Downloads Folder" (not "FOLDER DOWNLOADS")
- "Save Settings" (not "Simpan Pengaturan")
- "Reset to Default" (not "Reset ke Default")
- "Confirm on Exit" (not "Tampilkan konfirmasi saat keluar aplikasi")
- "Confirm on Module Switch" (not "Tampilkan konfirmasi saat pindah modul")
- "Color Management" (not "MANAJEMEN WARNA")
- "Import ICC Profile..." (already English)
- Detail/warning text → English

---

## 4. Dialog System (Unified Overlay)

### 4.1 Design Principle

All confirmation dialogs share a single reusable overlay hosted in `MainWindow.xaml`. Each dialog type is a `Border` within the overlay, shown/hidden via a `Visibility` binding on a `MainViewModel` property.

### 4.2 Common Overlay Container

```xml
<!-- In MainWindow.xaml, above ContentBorder but within the same Grid -->
<Border x:Name="DialogOverlay" Visibility="{Binding IsDialogVisible, Converter={StaticResource BooleanToVisibilityConverter}}"
        Background="#80000000" Panel.ZIndex="100" IsVisibleChanged="DialogOverlay_IsVisibleChanged">
    <!-- Each dialog type is placed here, only one visible at a time -->
    <Border x:Name="ConfirmExitDialog" ... />
    <Border x:Name="ConfirmModuleDialog" ... />
    <Border x:Name="UnsavedChangesDialog" ... />
</Border>
```

- Semi-transparent backdrop (`#80000000` black)
- `Panel.ZIndex="100"` ensures overlay sits above all content
- Click on backdrop = Cancel (close dialog, no action)
- Dialog card centered: `HorizontalAlignment="Center"` `VerticalAlignment="Center"`
- Dialog card design: `Width="380"` (fixed), `CornerRadius="16"`, `Background="{DynamicResource SurfacePrimaryBrush}"`, `BorderBrush="{DynamicResource BorderSubtleBrush}"`, drop shadow

### 4.3 Exit Confirmation Dialog

**Trigger**: `BtnClose_Click` → if `ConfirmExit == true` → show overlay.

**Content**:
- Icon: Warning/Exit glyph in accent icon box
- Title: "Exit Nexus?"
- Description: "Are you sure you want to exit Nexus?"
- Buttons:
  - **Exit** (primary, `NexusBrushStatusDanger` background) → calls `Close()`
  - **Cancel** (secondary, `SurfaceSecondaryBrush` background) → hide overlay

**If `ConfirmExit == false`**: close immediately, no dialog.

### 4.4 Module Switch Confirmation Dialog

**Trigger**: `MainViewModel.NavigateCommand` → before `CurrentViewModel` changes, if `ConfirmModule == true` AND `IEngineStateService.IsBusy == true` → show overlay.

**Content**:
- Icon: Engine/Process glyph
- Title: "Active Process Detected"
- Description: "You have an active process. Switching modules will cancel it. Continue?"
- Buttons:
  - **Continue** (primary, `AccentPrimaryBrush`) → proceed with navigation
  - **Cancel** (secondary) → hide overlay, no navigation

**If `ConfirmModule == false` or engine not busy**: navigate immediately, no dialog.

### 4.5 Unsaved Changes Dialog

**Trigger**: User tries to close app OR navigate away from Settings page while `SettingsViewModel.IsDirty == true`.

**Cases**:
1. **Exit with unsaved changes** — `BtnClose_Click` checks `IsDirty` first (before `ConfirmExit`)
2. **Navigate away from Settings** — `MainViewModel.NavigateCommand` checks `IsDirty` when `CurrentViewModel is SettingsViewModel`

**Content**:
- Icon: Save/Edit glyph
- Title: "Unsaved Changes"
- Description: "You have settings changes that haven't been saved yet."
- Buttons:
  - **Save & Exit / Save & Switch** (primary) → calls `SaveSettingsAsync()` synchronously, then proceeds
  - **Discard & Exit / Discard & Switch** (warning, `NexusBrushStatusDanger` background) → proceed without saving
  - **Cancel** (secondary) → hide overlay, stay on page

**Dialog prioritization** (for exit with `ConfirmExit==true` AND unsaved changes):
1. Show Unsaved Changes Dialog first
2. User picks "Save & Exit" or "Discard & Exit"
3. Then check `ConfirmExit` — if true, show Exit Confirmation
4. After confirming exit, close

### 4.6 Implementation Approach

Option A: **Inline overlay** in `MainWindow.xaml` — recommended.

- All state managed in `MainViewModel` (`IsDialogVisible`, `DialogType`, `DialogCallbacks`)
- Each dialog variant is a `Border` with `Visibility` bound to `DialogType`
- Callbacks use delegates or commands set before showing the dialog
- Backdrop click = cancel via `PreviewMouseDown` handler
- Animation: overlay fades in (opacity 0→1, 200ms); dialog card scales in (0.95→1.0, 200ms, ease-out)

Option B: Custom `Window`.

**Decision**: Option A. Avoids multi-window positioning issues with the custom title bar. The overlay pattern is already used conceptually by the app's borderless design.

---

## 5. Module Switch Confirmation

### 5.1 Current State

- `ConfirmModule` is read from config and bound to checkbox
- Navigation in `MainViewModel` (via `NavigateCommand`) switches `CurrentViewModel` unconditionally — **no check anywhere**

### 5.2 Target

- Before `CurrentViewModel` changes, if engine is active (`EngineState != "READY"` or similar) AND `ConfirmModule` is `true`, show confirmation dialog
- "You have an active process. Switch modules will cancel it. Continue?" with "Continue" and "Cancel"
- Same overlay dialog pattern as exit confirmation (reuse if possible)
- If engine is not active, skip dialog regardless of `ConfirmModule` setting

### 5.3 Integration Point

`MainViewModel.NavigateCommand` → before setting `CurrentViewModel`, evaluate:
```csharp
if (_configService.GetConfig().ConfirmModule && _engineStateService.IsBusy)
{
    // show dialog — if Cancel, return without navigating
}
```

**Critical**: Use `IEngineStateService.IsBusy` (runtime service, `Nexus.Foundation.State`) for the engine-active check, **not** `AppConfigRecord.EngineState` (persisted config field). The runtime service reflects current operation status; the config field is a stale snapshot for the Home Dashboard display.

---

## 6. AppConfigRecord Audit

### 6.1 Audit Results (Completed 2026-07-26)

All 7 fields were grepped across `/src/**/*.cs`. Data flow for each:

### 6.2 LastActivityModule — **Active**

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:11` — `public string LastActivityModule { get; set; } = string.Empty;` |
| Write | `HomeDashboardService.cs:19` — `config.LastActivityModule = module;` (via `UpdateActivityAsync`) |
| Read | `HomeDashboardService.cs:30` — returned in tuple from `GetLastActivity()` |
| Consumer | `HomeViewModel.cs:28-30` — `activity.Module` assigned to `ActiveModule` property in `Refresh()` |
| UI Binding | `HomeView.xaml:103` — `TxtLastActivityModule` bound to `{Binding ActiveModule}` |
| **Verdict** | Used end-to-end as part of Home dashboard "Last Activity" card. Keep. |

### 6.3 LastActivityFileCount — **Active**

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:12` — `public int LastActivityFileCount { get; set; } = 0;` |
| Write | `HomeDashboardService.cs:20` — `config.LastActivityFileCount = fileCount;` |
| Read | `HomeDashboardService.cs:30` — returned in tuple |
| Consumer | `HomeViewModel.cs:31` — `activity.FileCount.ToString()` → `TotalFiles` |
| UI Binding | `HomeView.xaml:111` — `ValDashTotalFile` bound to `{Binding TotalFiles}` |
| **Verdict** | Used. Keep. |

### 6.4 LastActivityTotalSize — **Active**

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:13` — `public long LastActivityTotalSize { get; set; } = 0;` |
| Write | `HomeDashboardService.cs:21` — `config.LastActivityTotalSize = totalSize;` |
| Read | `HomeDashboardService.cs:30` — returned in tuple |
| Consumer | `HomeViewModel.cs:32` — `FormatFileSize(activity.TotalSize)` → `TotalSizeFormatted` |
| UI Binding | `HomeView.xaml:117` — `ValDashTotalSize` bound to `{Binding TotalSizeFormatted}` |
| **Verdict** | Used. Keep. |

### 6.5 LastActivityTimestamp — **Active**

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:14` — `public string LastActivityTimestamp { get; set; } = string.Empty;` |
| Write | `HomeDashboardService.cs:22` — `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")` |
| Read | `HomeDashboardService.cs:30` — returned in tuple |
| Consumer | `HomeViewModel.cs:33-36` — concatenated into `ActivityDetail` string |
| UI Binding | `HomeView.xaml:105` — `TxtLastActivityDetail` bound to `{Binding ActivityDetail}` |
| **Verdict** | Used. Keep. |

### 6.6 EngineStatus — **Active** (runtime + persisted)

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:16` — `public string EngineStatus { get; set; } = "Standby";` |
| Write | `HomeDashboardService.cs:36` — `config.EngineStatus = status;` (via `SetEngineStatus(status, state)`) |
| Read | `HomeDashboardService.cs:51` — fallback `"Standby"` in `GetDashboardStatus()` |
| Consumer | `HomeViewModel.cs:47` — `EngineState = status.Status;` *(note: VM property name is `EngineState` but it receives `EngineStatus` value)* |
| UI Binding | `HomeView.xaml:69` — `CardEngineValue` bound to `{Binding EngineState}` (shows "Standby" / "Running") |
| **Verdict** | Used. Keep. Note: naming mismatch between `AppConfigRecord.EngineStatus` and `HomeViewModel.EngineState` is confusing but functional. |

### 6.7 EngineState — **Active** (runtime + persisted)

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:17` — `public string EngineState { get; set; } = "READY";` |
| Write | `HomeDashboardService.cs:37` — `config.EngineState = state;` (via `SetEngineStatus(status, state)`) |
| Read | `HomeDashboardService.cs:52` — fallback `"READY"` in `GetDashboardStatus()` |
| Consumer | `HomeViewModel.cs:48` — `SystemStatus = status.State;` |
| UI Binding | `HomeView.xaml:53` — `CardStatusValue` bound to `{Binding SystemStatus}` (shows "READY" / "Active") |
| **Verdict** | Used. Keep. |

### 6.8 ActiveFolderPath — **Active**

| Aspect | Location |
|---|---|
| Declaration | `AppConfigRecord.cs:18` — `public string ActiveFolderPath { get; set; } = "Downloads";` |
| Write | `HomeDashboardService.cs:44` — `config.ActiveFolderPath = path;` (via `SetFolderStatus(path)`) |
| Read | `HomeDashboardService.cs:53` — fallback `"Downloads"` in `GetDashboardStatus()` |
| Consumer | `HomeViewModel.cs:49` — `ActiveFolder = status.FolderPath;` |
| UI Binding | `HomeView.xaml:85` — `CardFolderValue` bound to `{Binding ActiveFolder}` |
| **Verdict** | Used. Keep. |

### 6.9 Critical Discovery: Two "EngineState" Concepts

There are **two separate services** with confusingly similar names:

| Service | Namespace | Purpose | Persisted? |
|---|---|---|---|
| `IEngineStateService` / `EngineStateService` | `Nexus.Foundation.State` | **Runtime-only** tracking (`IsBusy`, `ActiveModule`, `LastError`) | ❌ No |
| `AppConfigRecord.EngineState` / `HomeDashboardService` | `Nexus.Core.HomeSettings` | **Persisted** config value saved to `config.json` | ✅ Yes |

The runtime `EngineStateService` is the one that should drive the `ConfirmModule` engine-active check (via `IsBusy`), **not** the persisted `AppConfigRecord.EngineState`. The `EngineStatus`/`EngineState`/`ActiveFolderPath` fields in `AppConfigRecord` are purely for the Home Dashboard display — they reflect whatever was last saved, not current runtime state.

### 6.10 Conclusion

**No field is dead or legacy.** All 7 are actively used in the `AppConfigRecord` → `HomeDashboardService` → `HomeViewModel` → `HomeView` data flow for the Home Dashboard's "Last Activity" and "System Status" cards.

**Action**: Keep all fields. No deletions. No write-cessation.

### 6.11 Storage Location

Current: `nexus_data/config.json` (relative to working directory)

Considerations:
- Works for debug/development
- For production, `Environment.SpecialFolder.LocalApplicationData` or `Environment.SpecialFolder.ApplicationData` is more appropriate
- **Defer decision**: Keep relative path for now. If user wants production deployment, add an `IConfigService` overload that resolves to `AppData`.

---

## 7. Quick Theme Switch — Day & Night Toggle (Header)

### 7.1 Decision: Keep Header as Quick Theme Switch

Replaced the old `BtnTheme`/`BtnTheme_Click` with a **custom animated Day & Night Toggle** in the title bar.

The toggle is a **shortcut only**. Header has no state of its own — every toggle writes through `SettingsManager` → `ConfigService` → `AppConfig` → `ThemeManager`. Settings and Header are always in sync.

### 7.2 Shape

- Rounded pill (capsule) shape
- Fully rounded corners (half-height radius)
- Width approximately **2.5× height**
- Thumb: perfect circle
- Compact size, proportional with Nexus Header

### 7.3 Track

**Light Mode:**
- Clean white track
- Very thin border or no border
- Flat modern (no 3D, no texture)

**Dark Mode:**
- Dark gray/black track
- Thin border
- Flat modern (no 3D, no texture)

### 7.4 Thumb (Slider)

- Perfect circle
- Size: approximately **75–80% of track height**
- Slides left (Light) to right (Dark)
- Smooth animation: **200–300 ms**
- Color: **Nexus accent brush** (`AccentPrimaryBrush` or `NexusBrushAccent`)
- Subtle drop shadow on thumb

### 7.5 Icons

Use **existing NexusIcon glyphs only** — no new assets, no emoji, no external icon libraries:

| Side | Glyph | Source |
|---|---|---|
| Left (Light) | `services:NexusIcon.Glyph="LightTheme"` | Already exists in codebase |
| Right (Dark) | `services:NexusIcon.Glyph="DarkTheme"` | Already exists in codebase |

- Icons are placed outside the thumb, on the track surface
- Light icon on left side, Dark icon on right side
- When active: full opacity, white foreground
- When inactive: reduced opacity, gray foreground
- Thumb itself has no icon (to keep it clean)

### 7.6 Colors

All colors from existing Nexus Theme Resource Dictionary — **no hardcoded colors**:

| Element | Light Mode | Dark Mode |
|---|---|---|
| Track | White resource | Dark gray/black resource |
| Thumb | `AccentPrimaryBrush` / `NexusBrushAccent` | Same |
| Active icon | White (`#FFFFFF`) | White (`#FFFFFF`) |
| Inactive icon | Gray (`TextSecondaryBrush` opacity) | Gray (`TextSecondaryBrush` opacity) |

### 7.7 Animation

Simple, lightweight, GPU-friendly. No bounce, zoom, rotation, or decorative effects.

Animated properties:
1. **Thumb position** — slides horizontally (`Margin.Left` or `TranslateTransform.X`)
2. **Track color** — optional subtle transition or instant swap
3. **Icon opacity** — crossfade active/inactive opacity

Duration: **200–300 ms**
Easing: linear or `CircleEaseOut` (subtle deceleration)
Implementation: `System.Windows.Media.Animation.DoubleAnimation`

### 7.8 Visual Style

- Modern, minimalis, flat, elegant, lightweight
- Subtle shadow on thumb only
- No cloud/sun/moon illustrations, no stars, no decorative effects, no complex backgrounds
- Reference design's interaction pattern only — adapted to Nexus design identity

### 7.9 Position

- Right side of the Header title bar
- Same row as minimize/maximize/close buttons
- Compact and proportional — does not dominate the header
- Consistent with existing header element spacing and alignment
- Easily reachable for quick one-click toggle

### 7.10 Data Flow

```
Header Theme Switch (click)
        ↓
MainViewModel.ToggleThemeCommand
        ↓
_settingsManagerService.SaveSettingsAsync(theme, ...)
        ↓
ConfigService.SaveConfig()
        ↓
nexus_data/config.json  (persisted)
        ↓
ApplyThemeImmediate(theme)  (UI updated)
```

- Header has **no private state** (`_isDarkMode` field removed)
- On startup: theme loaded from `AppConfigRecord.Theme` via `ConfigService`
- Settings page RadioButtons read same `AppConfigRecord.Theme` — always in sync
- Toggle writes full settings (theme + current values for other fields) — preserves all other settings

### 7.11 Behavior Matrix

| Action | Theme Changes? | Persisted? | Settings Sync? |
|---|---|---|---|
| Click header Day/Night toggle | ✅ Immediate (200ms) | ✅ Via ConfigService | ✅ Next nav to Settings |
| Change RadioButton in Settings | ✅ After save | ✅ Via "Simpan Pengaturan" | ✅ Header reflects immediately via shared config |
| App restart | Loads from config.json | N/A | ✅ Both read from same source |

### 7.12 Settings Page Theme Section

The Settings page **keeps** the Light/Dark RadioButtons for users who prefer explicit selection in the configuration center. Both write to the same pipeline. No drift possible.

### 7.13 Implementation Notes

- Inline XAML control in `MainWindow.xaml` (no separate UserControl needed unless complexity warrants it)
- Animation via `DoubleAnimation` on thumb's `Margin` or `RenderTransform`
- The toggle command lives in `MainViewModel` (header scope), calls `ISettingsManagerService`
- Theme string ("Light"/"Dark") derived from toggle position before calling save
- Remove old `BtnTheme`, `BtnTheme_Click`, `LabelThemeToggle`, `IconThemeToggle`, `_isDarkMode` field from `MainWindow.xaml.cs`

---

## 8. Implementation Order

### Phase 1: Foundation (No UI Changes)
1. ✅ Completed — `AppConfigRecord` field audit (all 7 fields Active)
2. Audit `MainViewModel.NavigateCommand` to confirm no module-switch dialog exists
3. Write unit tests confirming current save/load produces identical state

### Phase 2: Unified Dialog Overlay
4. Create `DialogOverlay` container in `MainWindow.xaml` + `MainViewModel` dialog state properties
5. Implement **Exit Confirmation Dialog** — wire `ConfirmExit` to `BtnClose_Click`
6. Implement **Module Switch Confirmation Dialog** — wire `ConfirmModule` + `IEngineStateService.IsBusy` to `NavigateCommand`
7. Implement **Unsaved Changes Dialog** — add `SettingsViewModel.IsDirty` flag with detection in navigation + close
8. Confirm all three dialogs work via manual test

### Phase 3: Day & Night Header Toggle
9. Build custom animated Day & Night Toggle control (inline in `MainWindow.xaml`)
10. Wire toggle command through `MainViewModel` → `ISettingsManagerService.SaveSettingsAsync()`
11. Remove old `BtnTheme`/`BtnTheme_Click` code
12. Ensure `OnStartup` applies theme from `ConfigService` (not `_isDarkMode` default)

### Phase 4: Settings UI Redesign
13. Rewrite `SettingsView.xaml` to match Image Tools design system:
    - Replace `NexusCardStandard` cards with `SurfacePrimaryBrush` panels (`CornerRadius="18"`)
    - Use `RemoveBackgroundSettingsTextBoxStyle` for text boxes
    - Use `RemoveBackgroundPrimaryActionStyle` / `RemoveBackgroundModernButtonStyle` for buttons
    - Standardize section headers (34×34 icon, "Pengaturan" header style)
    - Keep RadioButton (`NexusRadioButtonStandard`), CheckBox (`NexusCheckBoxStandard`), ComboBox (`NexusComboBoxStyle`) as-is
14. Compact Color Management section:
    - Import ICC Profile button → `RemoveBackgroundPrimaryActionStyle`, same size as Browse button
    - Detail box → `SurfaceSecondaryBrush`, `CornerRadius="11"`
    - Fix resource keys (`TextSecondaryBrush` → `NexusBrushTextSecondary`, etc.)
15. Localize all text to English

### Phase 5: SettingsViewModel Enhancements
16. Add `IsDirty` property and tracking logic
17. Add `HasUnsavedChanges` property (for external check by `MainViewModel`)
18. Ensure `SaveSettingsAsync()` writes through `SettingsManagerService` → `ConfigService` → JSON file
19. Ensure `LoadSettings()` resets `IsDirty = false`

### Phase 6: Persistence Verification
20. End-to-end test: change each setting → save → close → reopen → verify all values persist
21. End-to-end test: reset to default → verify file content
22. End-to-end test: header toggle → navigate to Settings → RadioButtons match
23. End-to-end test: unsaved changes → close → dialog appears → all three choices work
24. End-to-end test: confirm exit ON → close → dialog appears → cancel stays
25. End-to-end test: confirm module ON + engine busy → navigate → dialog appears

---

## 9. Regression Risks

| Change | Risk | Mitigation |
|---|---|---|
| Day/Night toggle replaces old BtnTheme | Old click handler removed; toggle must write through ConfigService | Wire toggle to same `SaveSettingsAsync` pipeline. Verify `AppConfigRecord.Theme` changes on toggle. |
| Dialog overlay introduced | Existing mouse/click handlers on MainWindow may conflict | Overlay has `Panel.ZIndex="100"` and captures clicks via `PreviewMouseDown` on the backdrop only. Does not interfere with normal content. |
| `IsDirty` tracking in SettingsViewModel | False positives if a binding fires during LoadSettings | Set a `_isLoading` flag before `LoadSettings()` calls and clear it after; ignore dirty tracking when `_isLoading == true`. |
| Unsaved Changes Dialog on navigation | User is interrupted every time they leave Settings, even if no changes | Only trigger when `IsDirty == true` (user explicitly changed something since last save). Loading Settings then immediately navigating away = no dialog. |
| Wire module confirm | Navigation can be blocked by dialog. | Only blocks when `IEngineStateService.IsBusy == true` AND `ConfirmModule == true`. Default `true` ensures safe behavior for active processes. |
| UI redesign of SettingsView | Broken bindings, missing converters, wrong resource keys | Keep VM untouched. Only change XAML. Run app after each change. |
| Resource key changes (`TextSecondaryBrush` → `NexusBrushTextSecondary`) | Missing resource at runtime → exception | Use `DynamicResource` for fallback. Test both themes. |

---

## 10. Open Questions / Blockers

1. **ConfirmModule logic**: ✅ Resolved — use `IEngineStateService.IsBusy`. Not `AppConfigRecord.EngineState`. User confirmed separation of concerns.
2. **Legacy fields**: ✅ Resolved — all 7 fields Active. No deletions. User accepted audit results.
3. **Header toggle**: ✅ Resolved — Day & Night custom animated toggle, kept as quick switch, writes through ConfigService.
4. **Save granularity**: ✅ Resolved — manual save with unsaved changes dialog on close/navigate away.
5. **Theme default on first launch**: When `config.json` doesn't exist, `AppConfigRecord.Theme = "Dark"`. Does the header toggle need to match on first load? ✅ Already handled — `OnStartup` should call `ApplyThemeImmediate(config.Theme)` based on loaded config, not a hardcoded `_isDarkMode = true` field.

---

## 11. Success Criteria

- [ ] Settings → Save → Close → Reopen → all values identical (Theme, DownloadsFolder, ConfirmExit, ConfirmModule, DefaultCmykProfilePath)
- [ ] Confirm Exit dialog appears when `ConfirmExit == true`, bypasses when `false`
- [ ] Confirm Module dialog appears when `ConfirmModule == true` AND `IEngineStateService.IsBusy == true`, bypasses otherwise
- [ ] Unsaved Changes dialog appears when `IsDirty == true` on close or navigation away from Settings — all 3 choices functional
- [ ] Header Day/Night toggle sets theme immediately, persists to config, and Settings RadioButtons reflect the change on next visit
- [ ] Every control in SettingsView matches Image Tools design system (see Section 3)
- [ ] Color Management section layout compact (Import ICC button same visual weight as Browse)
- [ ] All dialog overlays use Nexus card styling (rounded corners, proper brushes, drop shadow)
- [ ] All UI text in English
- [ ] All existing unit tests still pass
- [ ] Solution builds with 0 errors
- [ ] End-to-end: create unsaved changes → close → dialog → Save & Exit → reopen → changes persisted
- [ ] End-to-end: create unsaved changes → close → dialog → Discard → reopen → changes NOT persisted
