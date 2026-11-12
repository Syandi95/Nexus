# Nexus Color Foundation PrimaryAction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all `#2563EB` literals from `ManajemenFile.ps1` while preserving the approved Light Mode visual and routing primary UI color through `ThemeManager.ps1`.

**Architecture:** Add a semantic `PrimaryAction` color token to the existing `$script:Themes` hashtable without changing `AccentPrimary`, `AccentHover`, or status tokens. Replace the 14 active `#2563EB` usages in `ManajemenFile.ps1` with token-driven runtime application through `Apply-Theme`, adding only minimal `x:Name` references needed for elements that are not currently addressable. No UI rebuild, layout, behavior, module logic, dependency, or file architecture changes are allowed.

**Tech Stack:** PowerShell 5+/7-compatible script syntax, WPF XAML loaded by `XamlReader`, existing Nexus `ThemeManager.ps1`, existing `HexToColor`, existing `Apply-Theme`.

## Global Constraints

- `Foundation\ThemeManager.ps1` remains the single source for Nexus UI color tokens.
- Final `ManajemenFile.ps1` literal count for `#2563EB` must be `0`.
- Do not use blind Replace All.
- Do not change layout, size, margin, padding, radius, position, hierarchy, or UX behavior except adding `x:Name` to existing active controls where needed for runtime theme switching.
- Do not change business logic, streaming, AI history, conversation engine, attachment, or file editing.
- Do not add dependencies, DLLs, packages, frameworks, or a new DesignSystem file.
- Do not change `AccentPrimary`, `AccentHover`, `Success`, `Warning`, `Error`, or `Info`.
- Add `PrimaryAction = "#2563EB"` to Light theme.
- Add `PrimaryAction = "#4CC2FF"` to Dark theme.
- Use `PrimaryAction` for all 14 current `#2563EB` occurrences in `ManajemenFile.ps1`.
- Leave the 3 `#2563EB` literals in `Modules\RapikanFile.ps1` unchanged for this first job; record them as follow-up Color Foundation work.
- Verification must include a `#2563EB` search in `ManajemenFile.ps1`, syntax test, Light to Dark to Light test, and Light Mode visual parity check against the baseline.

---

## File Structure

- Modify `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1`
  - Owns the new semantic token.
  - Creates a `$primaryActionBrush` from `$theme.PrimaryAction`.
  - Applies that brush to named primary action backgrounds and accent foregrounds during `Apply-Theme`.
- Modify `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1`
  - Removes all 14 `#2563EB` literals from active XAML.
  - Adds only the minimal `x:Name` attributes needed for runtime theme switching.
  - Registers newly named controls in the existing `$controls` list.
- Do not modify `C:\Users\Syand\3D Objects\Nexus\Modules\RapikanFile.ps1` in this job.

## Dependency Map

- `ManajemenFile.ps1` loads XAML, then resolves named controls with `$window.FindName($c)` using the `$controls` list.
- `ManajemenFile.ps1` dot-sources `Foundation\ThemeManager.ps1` after controls are resolved.
- `Apply-Theme` reads the current theme via `Get-ThemeColors`, converts hex values via `HexToColor`, and mutates existing WPF controls in place.
- Runtime theme switching is triggered by `BtnTheme.Add_Click`, which calls `Set-Theme`, `Sync-ThemeRadios`, then `Apply-Theme`.
- Therefore, every active XAML element that needs Light/Dark primary action color must either already be represented by a variable or receive a minimal `x:Name` and be added to `$controls`.

---

### Task 1: Add PrimaryAction Token and Brush

**Files:**
- Modify: `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1:26-28`
- Modify: `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1:98-112`

**Interfaces:**
- Consumes: `Get-ThemeColors`, `$script:Themes`, `HexToColor`.
- Produces: `$theme.PrimaryAction` and `$primaryActionBrush` inside `Apply-Theme`.

- [ ] **Step 1: Confirm current token lines before editing**

Run:

```powershell
Select-String -LiteralPath 'C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1' -Pattern 'AccentPrimary|AccentHover|Success|Warning|Error|Info' | ForEach-Object { "$($_.LineNumber):$($_.Line.Trim())" }
```

Expected: lines show existing `AccentPrimary`, `AccentHover`, `Success`, `Warning`, `Error`, and `Info`; do not change their values.

- [ ] **Step 2: Add `PrimaryAction` to both theme hashtables**

In `Foundation\ThemeManager.ps1`, edit only the two `$script:Themes` entries so they include these exact key/value pairs:

```powershell
PrimaryAction = "#2563EB"
```

for `Light`, and:

```powershell
PrimaryAction = "#4CC2FF"
```

for `Dark`.

The edited entries should preserve all existing token values. The relevant lines should become structurally equivalent to:

```powershell
$script:Themes = @{
    Light = @{ WindowBackground = "#FAFAFA"; Sidebar = "#F3F3F3"; PanelBackground = "#B3FFFFFF"; BackgroundInput = "#FFFFFF"; Border = "#E2E8F0"; TextPrimary = "#E4000000"; TextSecondary = "#9E000000"; AccentPrimary = "#0078D4"; AccentHover = "#106EBE"; PrimaryAction = "#2563EB"; CornerPanel = 8; CornerButton = 4; CornerTextbox = 4; NavHoverLight = "#E5E7EB"; NavHoverDark = "#243247"; BtnStart = "#0078D4"; BtnUndo = "#F1F5F9"; SidebarText = "#E4000000"; SidebarDimText = "#9E000000"; SidebarNavHover = "#E5E7EB"; CardShadow = "#00000000"; Success = "#10B981"; Warning = "#F59E0B"; Error = "#EF4444"; Info = "#0078D4"; BubbleUser = "#F1F5F9"; BubbleAssistant = "#F8FAFC" }
    Dark = @{ WindowBackground = "#202020"; Sidebar = "#1C1C1C"; PanelBackground = "#252525"; BackgroundInput = "#252525"; Border = "#333333"; TextPrimary = "#FFFFFF"; TextSecondary = "#C5FFFFFF"; AccentPrimary = "#4CC2FF"; AccentHover = "#60A5FA"; PrimaryAction = "#4CC2FF"; CornerPanel = 8; CornerButton = 4; CornerTextbox = 4; WindowControlBg = "#202020"; NavHoverLight = "#E5E7EB"; NavHoverDark = "#2A2A2A"; BtnStart = "#4CC2FF"; BtnUndo = "#333333"; SidebarText = "#FFFFFF"; SidebarDimText = "#C5FFFFFF"; SidebarNavHover = "#2A2A2A"; CardShadow = "#00000000"; Success = "#10B981"; Warning = "#F59E0B"; Error = "#EF4444"; Info = "#4CC2FF"; BubbleUser = "#2A2A2A"; BubbleAssistant = "#1C1C1C" }
}
```

- [ ] **Step 3: Create a `PrimaryAction` brush in `Apply-Theme`**

Immediately after the existing `$accentBrush` line:

```powershell
$accentBrush = [System.Windows.Media.SolidColorBrush]::new((HexToColor $theme.AccentPrimary))
```

add:

```powershell
$primaryActionBrush = [System.Windows.Media.SolidColorBrush]::new((HexToColor $theme.PrimaryAction))
```

- [ ] **Step 4: Run syntax parse for the token change**

Run:

```powershell
$errors=$null; [System.Management.Automation.Language.Parser]::ParseFile('C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1',[ref]$null,[ref]$errors) | Out-Null; if($errors){$errors | ForEach-Object { "$($_.Extent.StartLineNumber):$($_.Extent.StartColumnNumber) $($_.Message)" }; exit 1} else {'OK'}
```

Expected:

```text
OK
```

---

### Task 2: Name Unaddressable PrimaryAction XAML Elements

**Files:**
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:284-285`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:342`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:354`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:408`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:521`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:1006-1007`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:1074`

**Interfaces:**
- Consumes: XAML `x:Name` lookup via `$window.FindName`.
- Produces: script variables named `IconEngineAccent`, `IconCleanTotalAccent`, `BtnCleanRefreshBorder`, `BtnOpenFileBorder`, `IconRapikanStatsAccent`, and `IconSettingsFolderAccent`.

- [ ] **Step 1: Add names only to currently unaddressable primary action elements**

Apply these exact naming changes in `ManajemenFile.ps1`.

Change the engine card icon from:

```xml
<TextBlock Text='&#xE94B;' FontFamily='Segoe MDL2 Assets' FontSize='24' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

to:

```xml
<TextBlock x:Name='IconEngineAccent' Text='&#xE94B;' FontFamily='Segoe MDL2 Assets' FontSize='24' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

In the `cardCleanTotal` one-line XAML, change only the inner total icon `TextBlock` from:

```xml
<TextBlock Text='&#xE8EC;' FontFamily='Segoe MDL2 Assets' FontSize='16' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

to:

```xml
<TextBlock x:Name='IconCleanTotalAccent' Text='&#xE8EC;' FontFamily='Segoe MDL2 Assets' FontSize='16' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

Change the refresh wrapper from:

```xml
<Border CornerRadius='20' Background='#2563EB' Margin='0,0,6,0'>
```

to:

```xml
<Border x:Name='BtnCleanRefreshBorder' CornerRadius='20' Background='#2563EB' Margin='0,0,6,0'>
```

Change the open file wrapper from:

```xml
<Border Grid.Column='0' CornerRadius='14' Background='#2563EB'>
```

to:

```xml
<Border x:Name='BtnOpenFileBorder' Grid.Column='0' CornerRadius='14' Background='#2563EB'>
```

Change the Rapikan stats icon from:

```xml
<TextBlock Text='&#xE8EC;' FontFamily='Segoe MDL2 Assets' FontSize='14' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

to:

```xml
<TextBlock x:Name='IconRapikanStatsAccent' Text='&#xE8EC;' FontFamily='Segoe MDL2 Assets' FontSize='14' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

Change the settings folder icon from:

```xml
<TextBlock Text='&#xE8B7;' FontFamily='Segoe MDL2 Assets' FontSize='14' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

to:

```xml
<TextBlock x:Name='IconSettingsFolderAccent' Text='&#xE8B7;' FontFamily='Segoe MDL2 Assets' FontSize='14' Foreground='#2563EB' HorizontalAlignment='Center' VerticalAlignment='Center'/>
```

- [ ] **Step 2: Add new names to `$controls`**

In the existing `$controls = @(...)` list in `ManajemenFile.ps1`, add the six new names once:

```powershell
"IconEngineAccent"
"IconCleanTotalAccent"
"BtnCleanRefreshBorder"
"BtnOpenFileBorder"
"IconRapikanStatsAccent"
"IconSettingsFolderAccent"
```

Recommended placement to keep locality:

```powershell
"CardEngine","CardEngineValue","IconEngineAccent"
```

```powershell
"BtnCleanRefresh","BtnCleanRefreshBorder"
```

```powershell
"BtnOpenFile","BtnOpenFileBorder"
```

```powershell
"cardCleanTotal","valCleanTotal","lblCleanTotal","IconCleanTotalAccent"
```

```powershell
"RapikanPanelStats","RapikanStatsWrapPanel","IconRapikanStatsAccent"
```

```powershell
"SettingsPanelFolder","IconSettingsFolderAccent"
```

- [ ] **Step 3: Run XAML load smoke for new names**

Run:

```powershell
Add-Type -AssemblyName PresentationFramework; $content = Get-Content -LiteralPath 'C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1' -Raw; $match = [regex]::Match($content, '(?s)\[xml\]\$xaml\s*=\s*@"\r?\n(.*?)\r?\n"@'); if(-not $match.Success){ throw 'Blok XAML tidak ditemukan' }; [xml]$doc = $match.Groups[1].Value; $reader = [System.Xml.XmlNodeReader]::new($doc); $windowTest = [System.Windows.Markup.XamlReader]::Load($reader); 'XAML_LOAD_OK'; $reader.Close(); $windowTest.Close()
```

Expected:

```text
XAML_LOAD_OK
```

---

### Task 3: Route All 14 ManajemenFile Primary Uses Through ThemeManager

**Files:**
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:285`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:342`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:354`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:388`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:408`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:445`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:462`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:486`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:490`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:521`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:593`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:961`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:1007`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:1016`
- Modify: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1:1044`
- Modify: `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1:178-180`
- Modify: `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1:248-255`

**Interfaces:**
- Consumes: `$primaryActionBrush`, named controls from Task 2, existing named controls.
- Produces: zero `#2563EB` literals in `ManajemenFile.ps1`; runtime Light/Dark switching for all 14 primary action uses.

- [ ] **Step 1: Replace the 14 XAML literals with neutral initial values, not final colors**

Change only the 14 `#2563EB` literals in `ManajemenFile.ps1`. Use `#2563EB` nowhere in `ManajemenFile.ps1` after this step.

For primary action backgrounds, use:

```xml
Background='Transparent'
```

for these existing or newly named borders:

```text
BtnCleanRefreshBorder
BtnCleanExecuteBorder
BtnOpenFileBorder
BtnRapikanBrowseBorder
BtnRapikanStartBorder
RapikanProgressFill
BtnRapikanOpenLogBorder
BtnAISendBorder
BtnBrowseBorder
BtnSaveBorder
```

For primary action foregrounds, use:

```xml
Foreground='#0078D4'
```

only as a temporary XAML fallback for these TextBlocks:

```text
IconEngineAccent
valCleanTotal
IconCleanTotalAccent
LblRapikanPhase
IconRapikanStatsAccent
IconSettingsFolderAccent
```

This fallback is allowed only because the target literal is `#2563EB`; the runtime value must come from `PrimaryAction` after `Apply-Theme`. Do not introduce any new `#2563EB` literal.

- [ ] **Step 2: Use PrimaryAction for AI send and model refresh primary buttons**

In `Foundation\ThemeManager.ps1`, change:

```powershell
if ($BtnAISendBorder) {
    $BtnAISendBorder.Background = $accentBrush
}

if ($BtnAIRefreshModelsBorder) {
    $BtnAIRefreshModelsBorder.Background = $accentBrush
}
```

to:

```powershell
if ($BtnAISendBorder) {
    $BtnAISendBorder.Background = $primaryActionBrush
}

if ($BtnAIRefreshModelsBorder) {
    $BtnAIRefreshModelsBorder.Background = $primaryActionBrush
}
```

`BtnAIRefreshModelsBorder` is included because it is an existing primary action border already handled beside send. This keeps runtime primary action semantics consistent without changing its XAML.

- [ ] **Step 3: Use PrimaryAction for all primary action backgrounds**

Replace the current primary background loop:

```powershell
foreach ($el in @($BtnRapikanBrowseBorder, $BtnRapikanStartBorder, $BtnRapikanOpenLogBorder, $RapikanProgressFill)) {
    if ($null -eq $el) { continue }
    if ($el.PSObject.Properties.Name -contains "Background") { $el.Background = $accentBrush }
}
```

with:

```powershell
foreach ($el in @($BtnCleanRefreshBorder, $BtnCleanExecuteBorder, $BtnOpenFileBorder, $BtnRapikanBrowseBorder, $BtnRapikanStartBorder, $RapikanProgressFill, $BtnRapikanOpenLogBorder, $BtnBrowseBorder, $BtnSaveBorder)) {
    if ($null -eq $el) { continue }
    if ($el.PSObject.Properties.Name -contains "Background") { $el.Background = $primaryActionBrush }
}
```

- [ ] **Step 4: Use PrimaryAction for all primary accent foregrounds**

After the primary background loop from Step 3, add:

```powershell
foreach ($el in @($IconEngineAccent, $valCleanTotal, $IconCleanTotalAccent, $LblRapikanPhase, $IconRapikanStatsAccent, $IconSettingsFolderAccent)) {
    if ($null -eq $el) { continue }
    if ($el.PSObject.Properties.Name -contains "Foreground") { $el.Foreground = $primaryActionBrush }
}
```

- [ ] **Step 5: Keep button text foreground white**

Extend the existing white foreground loop:

```powershell
foreach ($el in @($BtnCleanRefresh, $BtnCleanExecute, $BtnRapikanBrowse, $BtnRapikanStart, $BtnRapikanOpenLog)) {
    if ($null -eq $el) { continue }
    if ($el.PSObject.Properties.Name -contains "Foreground") { $el.Foreground = [System.Windows.Media.Brushes]::White }
}
```

to:

```powershell
foreach ($el in @($BtnCleanRefresh, $BtnCleanExecute, $BtnOpenFile, $BtnRapikanBrowse, $BtnRapikanStart, $BtnRapikanOpenLog, $BtnBrowseDownloads, $BtnSaveSettings)) {
    if ($null -eq $el) { continue }
    if ($el.PSObject.Properties.Name -contains "Foreground") { $el.Foreground = [System.Windows.Media.Brushes]::White }
}
```

Do not add `BtnAISend` here because it is already handled by the AI button foreground loop:

```powershell
foreach ($btn in @($BtnAISend, $BtnAIRefreshModels, $BtnAIApplyEdit, $BtnAIRollback)) {
    if ($null -eq $btn) { continue }
    if ($btn.PSObject.Properties.Name -contains "Foreground") { $btn.Foreground = [System.Windows.Media.Brushes]::White }
}
```

- [ ] **Step 6: Verify no target literal remains in ManajemenFile**

Run:

```powershell
$hits = Select-String -LiteralPath 'C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1' -Pattern '#2563EB'; if($hits){ $hits | ForEach-Object { "$($_.LineNumber):$($_.Line.Trim())" }; exit 1 } else { 'NO_2563EB_IN_MANAJEMENFILE' }
```

Expected:

```text
NO_2563EB_IN_MANAJEMENFILE
```

---

### Task 4: Parse, Theme Toggle, and Visual Parity Verification

**Files:**
- Test only: `C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1`
- Test only: `C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1`
- Do not modify: `C:\Users\Syand\3D Objects\Nexus\Modules\RapikanFile.ps1`

**Interfaces:**
- Consumes: final patched scripts.
- Produces: verification evidence that target literal count is zero, syntax is valid, app starts, theme switching can run Light to Dark to Light, and Light Mode primary action color remains `#2563EB`.

- [ ] **Step 1: Run syntax parse for both changed files**

Run:

```powershell
$files = @('C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1','C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1'); foreach($file in $files){ $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile($file,[ref]$null,[ref]$errors) | Out-Null; if($errors){ "PARSE_FAIL $file"; $errors | ForEach-Object { "$($_.Extent.StartLineNumber):$($_.Extent.StartColumnNumber) $($_.Message)" }; exit 1 } else { "PARSE_OK $file" } }
```

Expected:

```text
PARSE_OK C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1
PARSE_OK C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1
```

- [ ] **Step 2: Run XAML load test**

Run:

```powershell
Add-Type -AssemblyName PresentationFramework; $content = Get-Content -LiteralPath 'C:\Users\Syand\3D Objects\Nexus\ManajemenFile.ps1' -Raw; $match = [regex]::Match($content, '(?s)\[xml\]\$xaml\s*=\s*@"\r?\n(.*?)\r?\n"@'); if(-not $match.Success){ throw 'Blok XAML tidak ditemukan' }; [xml]$doc = $match.Groups[1].Value; $reader = [System.Xml.XmlNodeReader]::new($doc); $windowTest = [System.Windows.Markup.XamlReader]::Load($reader); 'XAML_LOAD_OK'; $reader.Close(); $windowTest.Close()
```

Expected:

```text
XAML_LOAD_OK
```

- [ ] **Step 3: Run startup smoke**

Run:

```powershell
$root='C:\Users\Syand\3D Objects\Nexus'; $crash=Join-Path $root 'Foundation\crash.log'; $before = if(Test-Path $crash){ (Get-Item $crash).LastWriteTimeUtc } else { $null }; $p=Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-WindowStyle','Hidden','-File',(Join-Path $root 'ManajemenFile.ps1')) -WorkingDirectory $root -PassThru -WindowStyle Hidden; Start-Sleep -Seconds 5; $alive = -not $p.HasExited; if($alive){ Stop-Process -Id $p.Id -Force }; $after = if(Test-Path $crash){ (Get-Item $crash).LastWriteTimeUtc } else { $null }; "STARTUP_PROCESS_ALIVE=$alive"; "CRASH_LOG_CHANGED=$($before -ne $after)"; "EXIT_CODE=$($p.ExitCode)"
```

Expected:

```text
STARTUP_PROCESS_ALIVE=True
CRASH_LOG_CHANGED=False
EXIT_CODE=
```

- [ ] **Step 4: Run Light to Dark to Light runtime theme check**

Run this process-level WPF check. It loads the app script hidden, invokes the existing runtime theme path, verifies `PrimaryAction`, then closes the window:

```powershell
$root='C:\Users\Syand\3D Objects\Nexus'
$scriptPath=Join-Path $root 'ManajemenFile.ps1'
$ps = [powershell]::Create()
$ps.AddScript({
    param($scriptPath, $root)
    Set-Location $root
    . $scriptPath
    Set-Theme 'Light'; Apply-Theme
    $light1 = (Get-ThemeColors).PrimaryAction
    $sendLight = if($script:BtnAISendBorder.Background -is [System.Windows.Media.SolidColorBrush]) { $script:BtnAISendBorder.Background.Color.ToString() } else { '' }
    Set-Theme 'Dark'; Apply-Theme
    $dark = (Get-ThemeColors).PrimaryAction
    $sendDark = if($script:BtnAISendBorder.Background -is [System.Windows.Media.SolidColorBrush]) { $script:BtnAISendBorder.Background.Color.ToString() } else { '' }
    Set-Theme 'Light'; Apply-Theme
    $light2 = (Get-ThemeColors).PrimaryAction
    $sendLight2 = if($script:BtnAISendBorder.Background -is [System.Windows.Media.SolidColorBrush]) { $script:BtnAISendBorder.Background.Color.ToString() } else { '' }
    if($script:window){ $script:window.Close() }
    "LIGHT1_TOKEN=$light1"
    "LIGHT1_SEND=$sendLight"
    "DARK_TOKEN=$dark"
    "DARK_SEND=$sendDark"
    "LIGHT2_TOKEN=$light2"
    "LIGHT2_SEND=$sendLight2"
}).AddArgument($scriptPath).AddArgument($root) | Out-Null
$ps.Runspace.ApartmentState = 'STA'
$result = $ps.Invoke()
$result
if($ps.Streams.Error.Count){ $ps.Streams.Error | ForEach-Object { $_.ToString() }; exit 1 }
$ps.Dispose()
```

Expected output includes:

```text
LIGHT1_TOKEN=#2563EB
LIGHT1_SEND=#FF2563EB
DARK_TOKEN=#4CC2FF
DARK_SEND=#FF4CC2FF
LIGHT2_TOKEN=#2563EB
LIGHT2_SEND=#FF2563EB
```

If dot-sourcing the full app blocks because `ShowDialog()` is reached, use manual UI validation instead: launch `ManajemenFile.ps1`, toggle Light to Dark to Light with `BtnTheme`, and verify no crash log update.

- [ ] **Step 5: Verify Light Mode visual parity for primary action color**

Run this token-level check:

```powershell
. 'C:\Users\Syand\3D Objects\Nexus\Foundation\ConfigManager.ps1'; . 'C:\Users\Syand\3D Objects\Nexus\Foundation\ThemeManager.ps1'; $light = Get-ThemeColors 'Light'; "LIGHT_PRIMARY_ACTION=$($light.PrimaryAction)"; "LIGHT_ACCENT_PRIMARY=$($light.AccentPrimary)"
```

Expected:

```text
LIGHT_PRIMARY_ACTION=#2563EB
LIGHT_ACCENT_PRIMARY=#0078D4
```

Then manually confirm Light Mode primary action UI still shows the same blue as baseline for:

```text
Clean Refresh
Clean Execute
Open File
Rapikan Browse
Rapikan Start
Rapikan progress fill
Rapikan Open Log
AI Send
Settings Browse
Settings Save
Primary accent icons and labels
```

- [ ] **Step 6: Record follow-up scope without editing it**

Run:

```powershell
Select-String -LiteralPath 'C:\Users\Syand\3D Objects\Nexus\Modules\RapikanFile.ps1' -Pattern '#2563EB' | ForEach-Object { "$($_.LineNumber):$($_.Line.Trim())" }
```

Expected current follow-up evidence:

```text
272:Add-RapikanLog "================================" "#2563EB"
273:Add-RapikanLog "HASIL PROSES - RAPIKAN FILE" "#2563EB"
288:Add-RapikanLog "================================" "#2563EB"
```

Do not change these lines in this implementation.

---

## Self-Review

- Spec coverage: The plan adds `PrimaryAction`, removes all 14 `#2563EB` literals from `ManajemenFile.ps1`, preserves existing `AccentPrimary`, preserves Light Mode primary color through `PrimaryAction = "#2563EB"`, keeps module logic untouched, and leaves `Modules\RapikanFile.ps1` as follow-up evidence.
- Placeholder scan: No `TBD`, `TODO`, or undefined future work remains in task steps. The only follow-up is explicitly out of scope and has exact evidence lines.
- Type consistency: All produced names are XAML `x:Name` values added to `$controls`, then consumed as PowerShell variables by `Apply-Theme`. `$primaryActionBrush` is a `System.Windows.Media.SolidColorBrush`, matching existing brush assignment patterns.
