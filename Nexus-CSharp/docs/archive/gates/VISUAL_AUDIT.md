# Nexus Gate A.1 — Visual Audit

Date: 2026-10-01. Gate B HOLD. Reference archives verified by SHA-256: pre-edit Gate A `nexus-gate-a-source-20261001-114200.zip`, validated post-Gate-A `nexus-gate-a-final-source-20261001-145814.zip`. Archives used only for visual source comparison, never whole-source restoration.

## Evidence before cleanup

Compiled production WPF captures: `docs/visual-evidence/before/{Conversation,PDF,Rapikan,CleanDownloads,Image,Settings}-{Light,Dark,HighContrast}-{96,144}.png`.
Resolved values: `docs/visual-evidence/before/resolved-resources.json`.

| Reported element | Current key/role | Pre-Gate-A source/value | Validated Gate-A result | Root cause / intended fix |
|---|---|---|---|---|
| Normal/secondary button outline | Buttons.xaml `NexusStrokeDefault`, interactive border, 1 DIP | Light SolidColorBrush EEF1F3; Dark 333333; HC SystemColors.ControlDark | compiled key **missing** in all three themes; WPF fallback outline visible | StaticResource alias re-keys canonical brush in compiled BAML. Use distinct explicitly keyed brushes sharing the color token, retaining both roles. |
| PDF outer card and drop/canvas boundary | NexusCardStyle -> NexusStrokeDefault; drop border -> same brush | visible boundary using existing semantic brush | card/drop boundary invisible | Same missing shared key, not removed layout/separator nodes. Restore theme key; container uses subtle role, control uses default. |
| Rapikan cards/group/action regions | NexusCardStyle -> shared stroke | same view/card/template source as validated Gate A | outlines absent, structure flattened; buttons retain fallback outlines | Rapikan/FileManagement source unchanged. Fix shared mapping, do not patch module with new decorative lines. |
| Conversation composer/history borders | NexusStrokeDefault or semantic NexusContentBorder | existing stroke + unchanged geometry | only newly mapped ContentBorder sites retain brush; original default sites lose it | Both keys must coexist; no commands, bindings or layout replacement. |

Diff confirms Surfaces.xaml/InputControls.xaml/Layout.xaml and Rapikan/FileManagement/CleanDownloads views are byte-identical pre/post Gate A. PDF view differs only in assembly-qualified InfoBar URI, which must remain. Pill button templates moved to shared Buttons with same radius, commands and geometry; normal state must not gain strong/focus outlines.

## Roles and safe scope

Existing NexusStrokeSubtle/Default/Strong/Focus roles are retained. Subtle = container/card; Default = interactive boundary; Strong = pressed/important neutral state; Focus = accessible accent. Small refinement: Light Default can reuse existing E2E8F0 neutral primitive to distinguish controls from white surfaces while Subtle retains EEF1F3; Dark retains the pre-Gate-A 333333 default rather than nearly-white outlines. High Contrast keeps SystemColors.

Structural boundaries are existing card/drop/list edges, not a reason to wrap every row with new borders. No Rapikan source patch justified. No Agent, framework/library, operation, navigation, binding, DataContext or runtime changes authorized.

## Verification requirements

Compiled resource presence/types (not just raw source-XAML loading), all themes/control states, 96/144 DPI module captures, before/after Light+Dark comparison, requested targeted tests, then full Gate A regression. Completion/results recorded in VISUAL_CLEANUP_REPORT.md.

## High Contrast review finding

Before compatibility/style: ThemeManager selected `DarkTheme.xaml` for both Dark and HighContrast. Legacy `TextPrimaryBrush`/`NexusBrushTextPrimary` remained white while HighContrast semantic surfaces used OS Window/Control colors (white/light in this environment). Screenshot review found unreadable white shell text and mixed Dark/SystemColors surfaces, despite tests being green.

After compatibility/style: a dedicated `HighContrastTheme.xaml` supplies existing legacy keys from SystemColors and replaces the same compatibility dictionary slot. Dictionary count, dispatch/rollback/event lifecycle and all functional commands remain unchanged. The existing theme-entry-point assertion now requires the actual HC dictionary rather than the old Dark fallback. Compiled tests assert OS WindowText/Window colors; HC card subtle stroke uses ControlDark, retaining the former accessible container boundary instead of reducing contrast for aesthetics.

The same explicit keyed-brush rule is applied to the preexisting status-info compatibility alias, retaining canonical `NexusInfoStroke` alongside its compatibility key. That is guarded in compiled resource tests; the primary reported regression remains the missing default stroke captured in before/resolved-resources.json.

Final status: Gate A.1 PASS; final full solution 1,060 passed / 0 failed / 0 skipped / no abort. Gate B remains HOLD. See [VISUAL_CLEANUP_REPORT.md](VISUAL_CLEANUP_REPORT.md).
