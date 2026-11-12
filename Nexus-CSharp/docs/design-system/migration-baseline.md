# Nexus Design System v1 Migration Baseline

> Historical Stage 0 inventory is retained below. Gate A reconciliation (2026-10-01): direct module primitive-pattern debt 137 -> 126; frozen preexisting references reduced by seven, eleven new direct references migrated rather than allowlisted. Module hardcoded debt 146 -> 137, module styles 9 -> 7, common templates 5 -> 1. See GATE_A_REPORT.md §3E for before/after evidence and resource migration; original Stage 0 counts are not overwritten.

> Gate H recovery reconciliation (2026-10-10): `Views/ConversationView.xaml` sidebar-toggle icon changed from literal `&#xE90C;` with `NexusTypographyIconFontFamily` to `NexusIcon.Glyph="SidebarToggle"`. Preserve the native icon improvement; retire exactly that explicit font consumer (8 -> 7), reducing direct primitive debt 126 -> 125. The existing Photos-style image navigation Button template moves to `Themes/Components/Buttons.xaml`; its seven RGBA literals and foreground become eight semantic brush roles. Light/Dark preserve exact colors using six newly promoted primitive colors plus existing white and `#20FFFFFF` primitives. High Contrast follows the existing `SystemColors` contract while retaining every overlay alpha. Geometry, bindings, opacity and triggers are preserved; no new module exceptions are added. Remaining module debt stays hardcoded hex 137, module styles 7, common templates 1. Before/after renders are pixel-identical for all 24 captured theme/state/DPI comparisons on the current system palette. Exact source comparison and regression evidence: `docs/gate-h-recovery-evidence/`.

**Stage:** 0  
**Task:** 0.1 — Freeze the measurable legacy baseline  
**Captured:** 2026-07-30 (Asia/Jakarta)  
**Scope:** Read-only source inventory plus the new baseline document  
**Authority:** `docs/superpowers/plans/2026-07-30-nexus-design-system-v1.md`

## Checkpoint

This document records the active working tree before any Design System v1 migration. It does not classify debt as a defect, authorize cleanup, or modify production resources.

The checkout is a standalone repository rooted at Nexus-CSharp:

- working directory: `C:\Users\Syand\3D Objects\Nexus\Nexus-CSharp`;
- Git top-level: `C:\Users\Syand\3D Objects\Nexus\Nexus-CSharp`;
- Git directory/common directory: `.git`;
- branch: `master`;
- filtered local `HEAD`: `9fe2b3e576b09601efac4e4976a20def3d8f4f0d`;
- tracked files: 192;
- scoped status after Git-root correction: 162 entries — 41 modified, 121 untracked, 0 other.

The previous local `.git` directory was empty, causing Git discovery to climb to `C:\.git`. Repository history was reconstructed from the Nexus-CSharp subtree into the local `.git` without checkout. The source metadata at `C:\.git` remains intact as recovery evidence.

Safety verification:

- 337 active non-generated files were hashed before and after metadata reconstruction;
- 0 files were added, removed, or changed by the Git-root correction;
- `git fsck --full --no-dangling` passed;
- branch `master` and tag `ui-foundation-v1` are present;
- no remote was introduced;
- recovery checkpoint: `%TEMP%\nexus-git-root-recovery-20260730`.

The dirty state predates this task and is preserved. The increase from 120 to 121 untracked entries is the `docs/design-system/` directory created by Task 0.1.

## Verification Result

| Verification | Result | Evidence |
|---|---|---|
| App build | PASS | 0 errors, 5 `NU1900` warnings |
| `NexusUIFoundationTests` | PASS | 9 passed, 0 failed, 0 skipped |

Commands:

```powershell
dotnet build .\src\03_App\Nexus.App\Nexus.App.csproj --no-restore
dotnet test .\src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~NexusUIFoundationTests"
```

The warnings report unavailable NuGet vulnerability metadata from `https://api.nuget.org/v3/index.json`; compilation still completed successfully.

The full test suite was not part of Task 0.1 and was not run. The previously accepted baseline recorded three unrelated failures:

- `AIWorkspaceRenderingTests.MessageActions_UseNativeIconsAndExposeCopyForBothRoles`;
- `ConversationViewModelTests.DeepThinkChip_MatchesSelectorHeightPaddingAndAlignmentContract`;
- `IntegrationSmokeTests.CompositionRoot_ResolvesAllServicesAndRunsIntegrationFlow`.

Those historical failures are not reclassified or investigated here.

## Inventory Summary

The inventory excludes `bin/**` and `obj/**`.

| Measure | Count |
|---|---:|
| Source XAML files | 13 |
| Theme `ResourceDictionary` files | 3 |
| `x:Key` declarations | 155 |
| Unique `x:Key` names | 106 |
| Actual `ControlTemplate` declarations | 30 |
| View-local `ControlTemplate` declarations | 10 |
| SharedStyles `ControlTemplate` declarations | 20 |
| View-local `Style` declarations | 43 |
| Hardcoded hex occurrences in app XAML/C# | 122 |
| Unique hardcoded hex values | 46 |

## Current Resource Dictionaries

| Dictionary | Declared keys |
|---|---:|
| `src/03_App/Nexus.App/Themes/SharedStyles.xaml` | 56 |
| `src/03_App/Nexus.App/Themes/LightTheme.xaml` | 37 |
| `src/03_App/Nexus.App/Themes/DarkTheme.xaml` | 37 |

`App.xaml` currently merges `SharedStyles.xaml` and `DarkTheme.xaml`.

## Complete `x:Key` Inventory

The same 37 theme-role names occur in both Light and Dark dictionaries, so the list below represents 74 declarations.

### LightTheme.xaml and DarkTheme.xaml — 37 each

`WindowBackgroundBrush`, `PageBackgroundBrush`, `SurfacePrimaryBrush`, `SurfaceSecondaryBrush`, `PanelBackgroundBrush`, `InputBackgroundBrush`, `BorderBrush`, `BorderSubtleBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `AccentPrimaryBrush`, `AccentHoverBrush`, `PrimaryActionBrush`, `DangerBrush`, `AIWorkspaceWelcomeGlowBrush`, `AIWorkspaceDeepThinkOffBackgroundBrush`, `AIWorkspaceDeepThinkOnBackgroundBrush`, `AIWorkspaceDeepThinkOffBorderBrush`, `AIWorkspaceDeepThinkOnBorderBrush`, `AIWorkspaceDeepThinkOffForegroundBrush`, `AIWorkspaceDeepThinkOnForegroundBrush`, `NexusBrushBackground`, `NexusBrushPanelBackground`, `NexusBrushInputBackground`, `NexusBrushTextPrimary`, `NexusBrushTextSecondary`, `NexusBrushBorder`, `NexusBrushAccent`, `NexusBrushStatusSuccess`, `NexusBrushStatusDanger`, `NexusBrushHeroStop0`, `NexusBrushHeroStop1`, `NexusBrushHeroStop2`, `NexusBrushCardIconBg`, `NexusColorHeroStop0`, `NexusColorHeroStop1`, `NexusColorHeroStop2`.

### SharedStyles.xaml — 56

`CardShadow`, `SidebarShadow`, `NexusRadiusSmall`, `NexusRadiusMedium`, `NexusRadiusLarge`, `NexusRadiusCard`, `NexusHeightCompact`, `NexusHeightStandard`, `NexusHeightLarge`, `NexusFontDisplay`, `NexusFontHeader`, `NexusFontTitle`, `NexusFontBody`, `NexusFontLabel`, `NexusFontCaption`, `NexusSpacingSmall`, `NexusSpacingMedium`, `NexusSpacingLarge`, `CheckerboardBrush`, `MaximizeBorderStyle`, `NavButtonStyle`, `NexusComboShadow`, `NexusComboItemStyle`, `NexusComboBoxStyle`, `NexusBackgroundComboBoxStyle`, `NexusScrollRepeatButtonStyle`, `NexusScrollThumbStyle`, `NexusButtonPrimary`, `NexusTextBoxStandard`, `NexusPasswordBoxStandard`, `NexusComboBoxStandard`, `NexusCheckBoxStandard`, `NexusRadioButtonStandard`, `NexusToggleButtonStandard`, `NexusSliderThumbTemplate`, `NexusSliderDecreaseTemplate`, `NexusSliderIncreaseTemplate`, `NexusSliderStandard`, `NexusProgressBarStandard`, `NexusListBoxStandard`, `NexusListViewStandard`, `NexusDataGridStandard`, `NexusTreeViewStandard`, `NexusTabControlStandard`, `NexusScrollViewerStandard`, `NexusMenuStandard`, `NexusContextMenuStandard`, `NexusToolTipStandard`, `NexusDialogStandard`, `NexusCardStandard`, `RemoveBackgroundToolbarShadow`, `RemoveBackgroundToolbarButtonStyle`, `RemoveBackgroundModernButtonStyle`, `RemoveBackgroundPrimaryActionStyle`, `RemoveBackgroundSettingsTextBoxStyle`, `RemoveBackgroundProgressStyle`.

### Module and shell resources — 25

| File | Line | Key |
|---|---:|---|
| `SettingsView.xaml` | 10 | `BooleanToVisibilityConverter` |
| `RapikanFileView.xaml` | 11 | `BooleanToVisibilityConverter` |
| `CleanDownloadsView.xaml` | 12 | `BooleanToVisibilityConverter` |
| `PdfUtilitiesView.xaml` | 12 | `BooleanToVisibilityConverter` |
| `PdfUtilitiesView.xaml` | 13 | `PdfPageSegmentStyle` |
| `ImageSubsystemView.xaml` | 11 | `RemoveBackgroundToolbarShadow` |
| `ImageSubsystemView.xaml` | 14 | `BooleanToVisibilityConverter` |
| `ImageSubsystemView.xaml` | 16 | `RemoveBackgroundToolbarButtonStyle` |
| `ImageSubsystemView.xaml` | 66 | `ImageToolsIconButtonStyle` |
| `ImageSubsystemView.xaml` | 108 | `RemoveBackgroundCheckerboardBrush` |
| `ImageSubsystemView.xaml` | 120 | `RemoveBackgroundModernButtonStyle` |
| `ImageSubsystemView.xaml` | 160 | `RemoveBackgroundPrimaryActionStyle` |
| `ImageSubsystemView.xaml` | 195 | `RemoveBackgroundSettingsTextBoxStyle` |
| `ImageSubsystemView.xaml` | 225 | `RemoveBackgroundProgressStyle` |
| `ConversationView.xaml` | 13 | `AIWorkspaceHeadingStyle` |
| `ConversationView.xaml` | 23 | `AIWorkspaceBodyStyle` |
| `ConversationView.xaml` | 31 | `AIWorkspaceWelcomeTitleStyle` |
| `ConversationView.xaml` | 41 | `AIWorkspaceSecondaryStyle` |
| `ConversationView.xaml` | 49 | `AIWorkspaceInputStyle` |
| `ConversationView.xaml` | 57 | `AIWorkspaceCodeStyle` |
| `ConversationView.xaml` | 65 | `BooleanToVisibilityConverter` |
| `ConversationView.xaml` | 66 | `AIMessageActionButtonStyle` |
| `ConversationView.xaml` | 566 | `AIWorkspaceToolbarChipStyle` |
| `MainWindow.xaml` | 15 | `WindowDropShadow` |
| `MainWindow.xaml` | 16 | `BooleanToVisibilityConverter` |

Repeated declarations visible in this list are baseline evidence, not authorization to consolidate them in Task 0.1.

## `ControlTemplate` Inventory

### SharedStyles.xaml — 20

| Line | Target/key |
|---:|---|
| 60 | Button |
| 84 | ComboBoxItem |
| 111 | ComboBox |
| 116 | ToggleButton inside ComboBox |
| 173 | ComboBox |
| 178 | ToggleButton inside ComboBox |
| 226 | RepeatButton |
| 238 | Thumb |
| 273 | ScrollBar |
| 297 | ScrollBar |
| 332 | Button |
| 400 | `NexusSliderThumbTemplate` / Thumb |
| 407 | `NexusSliderDecreaseTemplate` / RepeatButton |
| 410 | `NexusSliderIncreaseTemplate` / RepeatButton |
| 419 | Slider |
| 558 | Button |
| 605 | Button |
| 641 | Button |
| 679 | TextBox |
| 705 | ProgressBar |

### Module-local templates — 10

| File | Line | Target |
|---|---:|---|
| `PdfUtilitiesView.xaml` | 23 | RadioButton |
| `ConversationView.xaml` | 76 | Button |
| `ConversationView.xaml` | 576 | ToggleButton |
| `ImageSubsystemView.xaml` | 30 | Button |
| `ImageSubsystemView.xaml` | 77 | Button |
| `ImageSubsystemView.xaml` | 131 | Button |
| `ImageSubsystemView.xaml` | 167 | Button |
| `ImageSubsystemView.xaml` | 205 | TextBox |
| `ImageSubsystemView.xaml` | 231 | ProgressBar |
| `ImageSubsystemView.xaml` | 370 | Thumb |

The Button, TextBox, and ProgressBar entries are common-control template candidates governed by the Final Standard. The PDF RadioButton segment and image Thumb still require their planned domain/specialization classification; this baseline makes no migration decision.

## Module-Local Style Inventory

There are 43 actual `<Style>` declarations in module views:

- `ConversationView.xaml`: 25;
- `ImageSubsystemView.xaml`: 12;
- `PdfUtilitiesView.xaml`: 5;
- `RapikanFileView.xaml`: 1.

| File:line | Key | Target | BasedOn |
|---|---|---|---|
| `RapikanFileView.xaml:202` | implicit | ListBoxItem | — |
| `PdfUtilitiesView.xaml:13` | `PdfPageSegmentStyle` | RadioButton | — |
| `PdfUtilitiesView.xaml:392` | implicit | TextBlock | — |
| `PdfUtilitiesView.xaml:436` | implicit | TextBlock | — |
| `PdfUtilitiesView.xaml:452` | implicit | TextBlock | — |
| `PdfUtilitiesView.xaml:479` | implicit | ProgressBar | `RemoveBackgroundProgressStyle` |
| `ImageSubsystemView.xaml:16` | `RemoveBackgroundToolbarButtonStyle` | Button | — |
| `ImageSubsystemView.xaml:66` | `ImageToolsIconButtonStyle` | Button | — |
| `ImageSubsystemView.xaml:120` | `RemoveBackgroundModernButtonStyle` | Button | — |
| `ImageSubsystemView.xaml:160` | `RemoveBackgroundPrimaryActionStyle` | Button | — |
| `ImageSubsystemView.xaml:195` | `RemoveBackgroundSettingsTextBoxStyle` | TextBox | — |
| `ImageSubsystemView.xaml:225` | `RemoveBackgroundProgressStyle` | ProgressBar | — |
| `ImageSubsystemView.xaml:257` | implicit | Button | `RemoveBackgroundModernButtonStyle` |
| `ImageSubsystemView.xaml:273` | implicit | Button | `RemoveBackgroundModernButtonStyle` |
| `ImageSubsystemView.xaml:312` | implicit | Border | — |
| `ImageSubsystemView.xaml:345` | implicit | Grid | — |
| `ImageSubsystemView.xaml:438` | implicit | Button | `RemoveBackgroundToolbarButtonStyle` |
| `ImageSubsystemView.xaml:688` | implicit | ProgressBar | `RemoveBackgroundProgressStyle` |
| `ConversationView.xaml:13` | `AIWorkspaceHeadingStyle` | inferred | — |
| `ConversationView.xaml:23` | `AIWorkspaceBodyStyle` | inferred | — |
| `ConversationView.xaml:31` | `AIWorkspaceWelcomeTitleStyle` | inferred | — |
| `ConversationView.xaml:41` | `AIWorkspaceSecondaryStyle` | inferred | — |
| `ConversationView.xaml:49` | `AIWorkspaceInputStyle` | inferred | — |
| `ConversationView.xaml:57` | `AIWorkspaceCodeStyle` | inferred | — |
| `ConversationView.xaml:66` | `AIMessageActionButtonStyle` | Button | — |
| `ConversationView.xaml:99` | implicit | ToolTip | — |
| `ConversationView.xaml:116` | implicit | Border | — |
| `ConversationView.xaml:145` | implicit | TextBlock | `AIWorkspaceSecondaryStyle` |
| `ConversationView.xaml:188` | implicit | TextBlock | `AIWorkspaceSecondaryStyle` |
| `ConversationView.xaml:205` | implicit | TextBlock | `AIWorkspaceSecondaryStyle` |
| `ConversationView.xaml:218` | implicit | TextBlock | `AIWorkspaceHeadingStyle` |
| `ConversationView.xaml:234` | implicit | TextBox | `AIWorkspaceBodyStyle` |
| `ConversationView.xaml:296` | implicit | Border | — |
| `ConversationView.xaml:318` | implicit | TextBlock | — |
| `ConversationView.xaml:418` | implicit | TextBlock | `AIWorkspaceBodyStyle` |
| `ConversationView.xaml:432` | implicit | ItemsControl | — |
| `ConversationView.xaml:489` | implicit | TextBlock | `AIWorkspaceBodyStyle` |
| `ConversationView.xaml:503` | implicit | Border | — |
| `ConversationView.xaml:527` | implicit | Button | — |
| `ConversationView.xaml:543` | implicit | Button | — |
| `ConversationView.xaml:561` | implicit | ComboBox | `NexusComboBoxStyle` |
| `ConversationView.xaml:566` | `AIWorkspaceToolbarChipStyle` | ToggleButton | — |
| `ConversationView.xaml:616` | implicit | ComboBoxItem | — |

This table intentionally includes presentation/domain styles alongside common-control candidates. Classification and retirement belong to later authorized tasks.

## Hardcoded Hex Baseline

The scan found 122 occurrences and 46 unique normalized values:

| Value | Count | Value | Count |
|---|---:|---|---:|
| `#EF4444` | 13 | `#FFFFFF` | 9 |
| `#10B981` | 7 | `#0078D4` | 6 |
| `#252525` | 6 | `#E2E8F0` | 6 |
| `#000000` | 5 | `#1E293B` | 5 |
| `#2563EB` | 5 | `#202020` | 3 |
| `#333333` | 3 | `#4CC2FF` | 3 |
| `#64748B` | 3 | `#C5FFFFFF` | 3 |
| `#FAFAFA` | 3 | `#00202020` | 2 |
| `#0F172A` | 2 | `#244CC2FF` | 2 |
| `#334155` | 2 | `#CBD5E1` | 2 |
| `#DC2626` | 2 | `#EFF6FF` | 2 |
| `#F59E0B` | 2 | `#F8FAFC` | 2 |
| `#FEF2F2` | 2 | `#FFF7ED` | 2 |
| `#002563EB` | 1 | `#00FFFFFF` | 1 |
| `#102563EB` | 1 | `#14000000` | 1 |
| `#16A34A` | 1 | `#182563EB` | 1 |
| `#18FFFFFF` | 1 | `#1D4ED8` | 1 |
| `#1F000000` | 1 | `#402563EB` | 1 |
| `#4F000000` | 1 | `#60A5FA` | 1 |
| `#664CC2FF` | 1 | `#806B7280` | 1 |
| `#8094A3B8` | 1 | `#88000000` | 1 |
| `#8C111111` | 1 | `#93C5FD` | 1 |
| `#ECFDF5` | 1 | `#F1F5F9` | 1 |

Occurrences by file:

| File | Occurrences | Matching lines |
|---|---:|---:|
| `Themes/DarkTheme.xaml` | 39 | 39 |
| `Themes/LightTheme.xaml` | 39 | 39 |
| `Views/CleanDownloadsView.xaml` | 17 | 17 |
| `Views/ImageSubsystemView.xaml` | 6 | 4 |
| `ViewModels/CleanDownloadsViewModel.cs` | 5 | 4 |
| `ViewModels/RapikanFileViewModel.cs` | 4 | 4 |
| `Views/FileUtilitiesView.xaml` | 4 | 4 |
| `Themes/SharedStyles.xaml` | 2 | 2 |
| `Views/HomeView.xaml` | 2 | 2 |
| `MainWindow.xaml` | 2 | 2 |
| `Views/PdfUtilitiesView.xaml` | 1 | 1 |
| `ViewModels/PdfUtilitiesViewModel.cs` | 1 | 1 |

The C# occurrences are recorded because they may indicate visual values produced by ViewModels, but Task 0.1 does not change or classify their behavior.

## Legacy and Compatibility Candidate Reference Counts

Counts include declaration and consumption within non-generated Nexus.App XAML/C#.

| Key | References |
|---|---:|
| `NexusButtonPrimary` | 7 |
| `RemoveBackgroundPrimaryActionStyle` | 7 |
| `RemoveBackgroundModernButtonStyle` | 10 |
| `RemoveBackgroundToolbarButtonStyle` | 12 |
| `ImageToolsIconButtonStyle` | 3 |
| `AIMessageActionButtonStyle` | 3 |
| `NavButtonStyle` | 8 |
| `NexusTextBoxStandard` | 2 |
| `RemoveBackgroundSettingsTextBoxStyle` | 10 |
| `NexusComboBoxStandard` | 1 |
| `NexusCheckBoxStandard` | 3 |
| `NexusRadioButtonStandard` | 5 |
| `NexusToggleButtonStandard` | 1 |
| `NexusSliderStandard` | 4 |
| `NexusProgressBarStandard` | 3 |
| `RemoveBackgroundProgressStyle` | 4 |
| `NexusCardStandard` | 15 |
| `NexusDialogStandard` | 2 |
| `NexusScrollViewerStandard` | 4 |
| `NexusMenuStandard` | 1 |
| `NexusContextMenuStandard` | 1 |
| `NexusBrushStatusInfo` | 1 |
| `NexusBackgroundComboBoxStyle` | 2 |

`NexusBackgroundComboBoxStyle` is included for manual classification, not automatic aliasing.

## Reproduction Commands

Run from `C:\Users\Syand\3D Objects\Nexus\Nexus-CSharp`.

### Git state

```powershell
git rev-parse --git-dir
git rev-parse --git-common-dir
git rev-parse --show-superproject-working-tree
git branch --show-current
git rev-parse --show-toplevel
git status --short -- .
```

### Source file and dictionary inventory

```powershell
rg --files src/03_App/Nexus.App -g '*.xaml' -g '!bin/**' -g '!obj/**'
rg --files src/03_App/Nexus.App/Themes -g '*.xaml' -g '!bin/**' -g '!obj/**'
```

### Keys

```powershell
rg -n --no-heading 'x:Key=' src/03_App/Nexus.App -g '*.xaml' -g '!bin/**' -g '!obj/**'
```

### Templates

```powershell
rg -n --no-heading '<ControlTemplate[ >]' src/03_App/Nexus.App -g '*.xaml' -g '!bin/**' -g '!obj/**'
rg -n --no-heading '<DataTemplate\b|<ItemsPanelTemplate\b' src/03_App/Nexus.App -g '*.xaml' -g '!bin/**' -g '!obj/**'
```

### Module-local styles

```powershell
rg -n --no-heading '<Style[ >]' src/03_App/Nexus.App/Views src/03_App/Nexus.App/MainWindow.xaml -g '*.xaml' -g '!bin/**' -g '!obj/**'
rg -n --no-heading '<Style[^>]*x:Key=' src/03_App/Nexus.App/Views src/03_App/Nexus.App/MainWindow.xaml -g '*.xaml' -g '!bin/**' -g '!obj/**'
```

### Hex colors

```powershell
rg -n --no-heading '#[0-9A-Fa-f]{3,8}\b' src/03_App/Nexus.App -g '*.xaml' -g '*.cs' -g '!bin/**' -g '!obj/**'
```

### Legacy key counts

Use fixed-string search for each key in the compatibility table:

```powershell
rg -n -F --no-heading '<LEGACY_KEY>' src/03_App/Nexus.App -g '*.xaml' -g '*.cs' -g '!bin/**' -g '!obj/**'
```

## Stage 0 Task 0.1 Acceptance

- Current Git state recorded: yes.
- Current dictionaries recorded: yes.
- Every current `x:Key` declaration represented: yes, 155 declarations.
- Every current `ControlTemplate` represented: yes, 30 declarations.
- Every module-local style represented: yes, 43 declarations.
- Hardcoded hex baseline recorded: yes, 122 occurrences / 46 values.
- Legacy candidate counts recorded: yes, 23 keys.
- App build recorded: PASS.
- `NexusUIFoundationTests` recorded: PASS, 9/9.
- Production source/resource/test changes: none made by this task.

Checkpoint result: ready for documentation review. Stage 0 Task 0.2 remains unauthorized.
