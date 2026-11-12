# Nexus Gate A.1 — Visual Cleanup Report

Date: 2026-10-01. **Gate A.1 PASS** at the tested native WPF/theme/DPI boundary. **Gate B / Phase 1 HOLD.** Root-cause mapping: [VISUAL_AUDIT.md](VISUAL_AUDIT.md). Gate A audit/baseline/report history remains unchanged.

## 1. Reported issues, before/after and root cause

### A. Hard button outlines

BEFORE resource/style: normal Buttons `NexusButtonStyle` -> DynamicResource `NexusStrokeDefault`, 1 DIP; semantic source declared a soft brush, but compiled application lookup returned null in all themes. WPF fallback control outlines appeared grey/black rather than the intended brush.

ROOT CAUSE: `StaticResource x:Key="NexusContentBorder" ResourceKey="NexusStrokeDefault"` introduced by Gate A re-keyed the canonical brush in compiled BAML. Raw-XAML dictionary tests alone did not detect the lost runtime key.

AFTER resource/style: both keys are distinct SolidColorBrush instances sharing the palette primitive/SystemColors source. Light default/control border uses existing E2E8F0 neutral, subtle/container stays EEF1F3; Dark normal strokes retain 333333. Strong neutral stroke is used for the pressed state, not normal. Existing focus visual/accent and disabled state remain distinct. No colors added to module views.

### B. PDF canvas/card boundary missing

BEFORE resource/style: NexusCardStyle and existing DropArea border consumed the missing default stroke. The XAML structural nodes were still present; no toolbar, canvas or panel structure had actually been deleted by Gate A.

AFTER resource/style: canonical key restored; NexusCardStyle consumes NexusStrokeSubtle. Existing drop-zone/default boundary, card/preview grouping and footer outline resolve again. Default card remains 1 DIP; existing drop-zone's intentional 2-DIP boundary remains its original design. No new nested boxes, manual per-view lines or PDF layout changes. Assembly-qualified InfoBar URI remains untouched.

### C. Rapikan/FileManagement flattened hierarchy

BEFORE resource/style: RapikanPanelFolder/Action/Stats/Log and existing category cards depend on shared NexusCardStyle. Checkpoint comparison proves Rapikan, FileManagement and CleanDownloads views were byte-identical pre/post Gate A. The missing shared brush erased their existing outlines.

AFTER resource/style: shared subtle card boundary restored, interactive button boundary uses Default. Group hierarchy is readable again with existing surfaces/spacing/radii; no Rapikan/FileManagement view patch or extra separator rows. Existing category cards were restored, not introduced as new per-row boxes.

### D. High Contrast mismatch found during visual smoke

BEFORE: HighContrast semantic theme used SystemColors, but ThemeManager still selected Dark compatibility resources; legacy white text on OS-white sidebar/header was unreadable. Initial post-repair screenshot review exposed this mismatch and prevented declaring completion on the first green test run.

AFTER: HighContrast compatibility dictionary supplies the same legacy resource keys from SystemColors; semantic and compatibility slots are still replaced atomically through existing ThemeManager flow. Strong/focus/accessibility colors are not softened. HC subtle card boundary uses SystemColors.ControlDark, equivalent to its former control boundary. Legacy decorative glow becomes OS Window surface only in HC.

## 2. Scope / files changed relative to validated Gate A

Existing production files:

```text
src/03_App/Nexus.App/Services/ThemeManager.cs
src/03_App/Nexus.App/Themes/Semantic/LightSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Semantic/DarkSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Semantic/HighContrastSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Components/Buttons.xaml
src/03_App/Nexus.App/Themes/Components/Surfaces.xaml
```

New production resource: `src/03_App/Nexus.App/Themes/HighContrastTheme.xaml` (existing legacy roles, SystemColors; no control library/framework).

Tests: new `src/03_App/Nexus.Integration.Tests/VisualFidelityTests.cs`; `NexusUIFoundationTests.cs` updates the HC compatibility-source assertion to preserve intended accessibility rather than requiring Dark fallback.

Documents/evidence: VISUAL_AUDIT.md, VISUAL_CLEANUP_REPORT.md, `docs/visual-evidence/README.md`, capture PNGs/resource JSON/build logs/TRX. Comparison helpers remain in approved Temp/opencode.

Why safe: no command/binding/DataContext/navigation changes, no inference/Chat/AI CHECK/history/PDF/image pipeline changes, no package/project changes. All module views remain byte-identical to validated Gate A. Snapshot/hash comparison specifically confirms App.xaml.cs, AiCheckViewModel, ConversationViewModel, ConversationView.xaml, PdfUtilitiesView.xaml, WpfAppHost and ImageApplicationModelSelectionPolicy are preserved. User staged patch remains byte-identical. No reset/clean/source rollback/commit.

## 3. Screenshot evidence

`before/` means **validated Gate A before A.1 cleanup**, not a rebuilt old executable. Pre-Gate-A checkpoint is used for resource/style/value comparison only, protecting Gate A functional fixes. Both archived source checksums were verified.

| Module/theme | Before | After |
|---|---|---|
| PDF Light | [before](docs/visual-evidence/before/PDF-Light-96.png) | [after](docs/visual-evidence/after/PDF-Light-96.png) |
| PDF Dark | [before](docs/visual-evidence/before/PDF-Dark-96.png) | [after](docs/visual-evidence/after/PDF-Dark-96.png) |
| Rapikan Light | [before](docs/visual-evidence/before/Rapikan-Light-96.png) | [after](docs/visual-evidence/after/Rapikan-Light-96.png) |
| Rapikan Dark | [before](docs/visual-evidence/before/Rapikan-Dark-96.png) | [after](docs/visual-evidence/after/Rapikan-Dark-96.png) |
| Conversation Light | [before](docs/visual-evidence/before/Conversation-Light-96.png) | [after](docs/visual-evidence/after/Conversation-Light-96.png) |
| Conversation Dark | [before](docs/visual-evidence/before/Conversation-Dark-96.png) | [after](docs/visual-evidence/after/Conversation-Dark-96.png) |
| PDF HighContrast | [before](docs/visual-evidence/before/PDF-HighContrast-96.png) | [after](docs/visual-evidence/after/PDF-HighContrast-96.png) |

Complete before/after matrix: Conversation, PDF, Rapikan, CleanDownloads, Image, Settings × Light/Dark/HighContrast × 96/144 DPI = **36 images per capture**, 72 paired images. 144-DPI variants follow the same filenames ending `-144.png`. Empty-state UI/fixture data are equivalent for these comparisons; layout spacing/positions unchanged. Final regression also recaptured the matrix under `regression-capture-final/`.

Resource evidence `before/resolved-resources.json` shows missing canonical default stroke; `after/resolved-resources.json` shows a SolidColorBrush for both default/content keys in every theme. Light default E2E8F0/subtle EEF1F3, Dark default/subtle333333, HC uses OS colors.

## 4. Tests and visual smoke

Final targeted run: **215 passed, 0 failed, 0 skipped**, `docs/visual-evidence/visual-targeted-final.trx`. It includes:

- DesignSystemGovernanceTests
- NexusUIFoundationTests
- WorkspaceTabStyleTests
- RapikanFileSemanticTypographyTests
- FileManagementShellTests
- PdfUtilitiesViewModelTests
- ConversationViewModelTests
- MainViewModelThemeSyncTests
- VisualFidelityTests

New verification adds 6 cases: compiled canonical brush presence/types and normal/hover/pressed/disabled/selected resource states in three themes, 96/144-DPI line rasterization (visible 1-DIP edge, no blur tail or clipping), and native module screenshot capture. Hover/pressed are driven deterministically through read-only WPF state keys **in test code only**, not an OS mouse automation claim. Focus visual is retained; real keyboard focus/navigation is additionally exercised in the Gate A live fixture. Selected TabItem and per-view radio grouping contracts remain covered.

At 150%, 1 DIP has 1.5-pixel coverage; one fractional coverage row is legitimate. The raster test rejects tail beyond ceil(scale), rather than pretending every 150% line is one physical pixel. Captures use native WPF RenderTargetBitmap at 96/144 DPI; real cross-monitor OS scaling is outside this boundary.

## 5. Final full regression

Restore PASS. Entire `src/Nexus.sln` build PASS: **0 errors, 2 existing CS0067 emissions on final incremental build, 12.21 s**. Warning debt from unchanged projects is not claimed removed. No assertions relaxed or benchmarks thresholds changed for this visual task; Gate A's agreed cold/warm policy remains.

Full final command, with Gate A live fixture environment plus `NEXUS_VISUAL_OUTPUT` set to the capture directory:

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore -m:1 --logger trx --results-directory "docs\visual-evidence\full-regression-final" --blame-hang-timeout 5m --blame-hang-dump-type none --verbosity minimal
```

**COMPLETE / PASS: 1,060 passed, 0 failed, 0 skipped, 0 aborted.** Integration project **603 passed**; whole-run wall **516.1024131 s**. All eleven test projects, actual model/Chat/AI CHECK/history/image/PDF live fixtures and visual capture execute, not only filtered/isolated tests. Raw TRX/build evidence is under `docs/visual-evidence/full-regression-final/` and `visual-build-final.log`.

Earlier green full run before HC compatibility fix is retained separately (`full-regression/`); visual review still required the HC fix. Final run follows final targeted and updated screenshot matrix. No duplicate/orphan llama/testhost process remained after verification.

## 6. Stop / review

Gate A PASS → **Gate A.1 Visual Fidelity PASS** within documented matrix → review. **STOP: Gate B / Phase 1 remains HOLD.** No Agent contracts/runtime/tools/MAF/Hermes/memory/skills/UI implemented. Existing native UI foundation and functional fixes retained.
