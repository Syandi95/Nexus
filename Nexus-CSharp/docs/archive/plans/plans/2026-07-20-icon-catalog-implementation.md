# Icon Catalog Implementation Plan (REVISED)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate semua ikon Nexus dari hardcoded `Segoe MDL2 Assets` glyphs ke centralized `Segoe Fluent Icons` via `NexusIcons.ps1`.

**Architecture:** Buat `Foundation/NexusIcons.ps1` sebagai satu source of truth untuk seluruh ikon dengan 1 private function (`Test-NexusIconFont`) + 3 public API functions (`Get-NexusIconDefinition`, `Get-NexusIcon`, `Set-NexusIcon`). Bersihkan `ThemeManager.ps1` dari semua kode ikon. Migrasi semua XAML hardcode glyph + FontFamily ke pola `x:Name` + inisialisasi Set-NexusIcon. Runtime deteksi font saat startup; jika Segoe Fluent Icons tidak tersedia, tampilkan notifikasi dan SKIP semua Set-NexusIcon — jangan fallback diam-diam.

**Tech Stack:** PowerShell 5.1, WPF, Segoe Fluent Icons font

## Design Spec Corrections

Design spec header says "64 Icons" — akurat: **84 unique semantic names** (~76 unique codepoints). Header akan diperbaiki setelah plan disetujui.

## Global Constraints

- Tiga public API functions: `Get-NexusIconDefinition`, `Get-NexusIcon`, `Set-NexusIcon`
- Satu private function: `Test-NexusIconFont` (internal, bukan API)
- Satu font global: `$script:NexusIconFont = 'Segoe Fluent Icons'`
- Satu katalog: `$script:NexusIconCatalog`
- CodePoint katalog sudah beku — jangan diubah
- Tidak ada factory, provider, converter, attached property, atau IconService
- Tidak ada fallback diam-diam — jika nama tidak ditemukan di katalog, `Get-NexusIcon` return $null
- Tidak ada perubahan perilaku fitur, layout, warna, theme contract
- Segala glyph literal (`&#x...`, `[char]0x...`, `FontFamily="Segoe MDL2 Assets"`) HARUS dihapus dari XAML dan PS1
- Set-NexusIcon hanya menerima `[System.Windows.Controls.TextBlock]` — Button icons harus punya child TextBlock

---

## File Structure

### Created
| File | Responsibility |
|------|---------------|
| `Foundation/NexusIcons.ps1` | Satu source of truth: font global, katalog 84 ikon, 3 public API + 1 private function, runtime font detection |

### Modified
| File | Changes |
|------|---------|
| `ManajemenFile.ps1` | Tambah dot-source NexusIcons.ps1; tambah font detection di startup (skip icon init jika font tidak tersedia, jangan skip app); ganti Get-NexusIcon calls jadi Set-NexusIcon via child TextBlock |
| `Foundation/ThemeManager.ps1` | Hapus `$script:NexusIcons`, `Get-NexusIcon`, `New-ThemeToggleContent`; ganti theme toggle logic pakai Set-NexusIcon |
| `Views/MainWindow.xaml` | Hapus ~17 glyph literal + FontFamily; ganti dengan `x:Name` |
| `Views/Modules/AIWorkspaceView.xaml` | Hapus 6 glyph literal + FontFamily |
| `Views/Modules/RapikanFileView.xaml` | Hapus 7 glyph literal + FontFamily |
| `Views/Modules/CleanDownloadsView.xaml` | Hapus 9 glyph literal + FontFamily |
| `Views/Modules/RemoveBackgroundView.xaml` | Hapus ~17 glyph literal + FontFamily |
| `Views/Modules/SettingsView.xaml` | Hapus 8 glyph literal + FontFamily |
| `Views/Modules/HomeView.xaml` | Hapus 3 glyph literal + FontFamily |
| `Views/Modules/PdfToolsView.xaml` | Hapus 3 glyph literal + FontFamily |
| `Themes/SharedStyles.xaml` | Hapus 2 glyph literal + FontFamily |
| `Modules/AIWorkspace.ps1` | Ganti ~12 `[char]0x...` dengan Set-NexusIcon |
| `Modules/RapikanFile.ps1` | Ganti 12 `[char]0x...` dengan Set-NexusIcon (mapping kategori) |
| `Modules/RemoveBackground.ps1` | Ganti 12 `[char]0x...` dengan Set-NexusIcon (status icons) |

---

## Task 1: Create Foundation/NexusIcons.ps1

- [ ] **Step 1: Create file header and font variable**

```powershell
# Foundation/NexusIcons.ps1
# Source of truth untuk seluruh ikon Nexus.
# Di-dot-source oleh ManajemenFile.ps1 SEBELUM ThemeManager.ps1
# Public API: Get-NexusIconDefinition, Get-NexusIcon, Set-NexusIcon (3 functions)
# Private: Test-NexusIconFont

$script:NexusIconFont = 'Segoe Fluent Icons'
$script:NexusIconFontAvailable = $false
```

- [ ] **Step 2: Create icon catalog — 84 entries**

```powershell
$script:NexusIconCatalog = @{
    # Navigation & Modules (11)
    Menu            = @{ CodePoint = 0xE700 }
    Home            = @{ CodePoint = 0xE80F }
    AIWorkspace     = @{ CodePoint = 0xE8BD }
    CleanDownloads  = @{ CodePoint = 0xE7AC }
    RapikanFile     = @{ CodePoint = 0xE8DE }
    PDFTools        = @{ CodePoint = 0xEA90 }
    ImageTools      = @{ CodePoint = 0xE8B9 }
    RemoveBG        = @{ CodePoint = 0xE7C5 }
    Settings        = @{ CodePoint = 0xE713 }
    RecentActivity  = @{ CodePoint = 0xE823 }
    Engine          = @{ CodePoint = 0xE950 }

    # Actions (19)
    Search          = @{ CodePoint = 0xE721 }
    Send            = @{ CodePoint = 0xE725 }
    Stop            = @{ CodePoint = 0xE71A }
    Attach          = @{ CodePoint = 0xE723 }
    AttachFile      = @{ CodePoint = 0xE723 }
    AttachFolder    = @{ CodePoint = 0xE8B7 }
    Add             = @{ CodePoint = 0xE710 }
    Remove          = @{ CodePoint = 0xE738 }
    Delete          = @{ CodePoint = 0xE74D }
    Save            = @{ CodePoint = 0xE74E }
    Edit            = @{ CodePoint = 0xE70F }
    Copy            = @{ CodePoint = 0xE8C8 }
    Paste           = @{ CodePoint = 0xE77F }
    Cut             = @{ CodePoint = 0xE8C6 }
    Undo            = @{ CodePoint = 0xE7A7 }
    Redo            = @{ CodePoint = 0xE7A6 }
    Refresh         = @{ CodePoint = 0xE72C }
    Sync            = @{ CodePoint = 0xE895 }
    Check           = @{ CodePoint = 0xE73E }

    # Window Controls (4)
    Minimize        = @{ CodePoint = 0xE921 }
    Maximize        = @{ CodePoint = 0xE922 }
    Restore         = @{ CodePoint = 0xE923 }
    Close           = @{ CodePoint = 0xE8BB }

    # Theme (5)
    LightTheme      = @{ CodePoint = 0xE706 }
    DarkTheme       = @{ CodePoint = 0xE708 }
    ThemePalette    = @{ CodePoint = 0xE771 }
    Palette         = @{ CodePoint = 0xE790 }
    Light           = @{ CodePoint = 0xE793 }

    # Files & Folders (7)
    Folder          = @{ CodePoint = 0xE8B7 }
    FolderOpen      = @{ CodePoint = 0xE838 }
    NewFolder       = @{ CodePoint = 0xE8F4 }
    File            = @{ CodePoint = 0xE7C3 }
    Document        = @{ CodePoint = 0xE8A5 }
    PDF             = @{ CodePoint = 0xEA90 }
    PDFPage         = @{ CodePoint = 0xE8A5 }

    # Media (5)
    Image           = @{ CodePoint = 0xE8B9 }
    Videos          = @{ CodePoint = 0xE8B2 }
    Audio           = @{ CodePoint = 0xE8D6 }
    Play            = @{ CodePoint = 0xE768 }
    Pause           = @{ CodePoint = 0xE769 }

    # Utilities (8)
    Download        = @{ CodePoint = 0xE896 }
    Upload          = @{ CodePoint = 0xE898 }
    Print           = @{ CodePoint = 0xE749 }
    Zoom            = @{ CodePoint = 0xE71E }
    ZoomOut         = @{ CodePoint = 0xE71F }
    Camera          = @{ CodePoint = 0xE722 }
    Lock            = @{ CodePoint = 0xE72E }
    Share           = @{ CodePoint = 0xE72D }

    # Status & Info (5)
    Info            = @{ CodePoint = 0xE946 }
    Help            = @{ CodePoint = 0xE897 }
    Warning         = @{ CodePoint = 0xE7BA }
    Error           = @{ CodePoint = 0xE783 }
    Confirmation    = @{ CodePoint = 0xE930 }

    # Misc (17)
    Stats           = @{ CodePoint = 0xE9D2 }
    Size            = @{ CodePoint = 0xE958 }
    Pin             = @{ CodePoint = 0xE718 }
    Flag            = @{ CodePoint = 0xE7C1 }
    Tag             = @{ CodePoint = 0xE8EC }
    Label           = @{ CodePoint = 0xE932 }
    Compare         = @{ CodePoint = 0xE8AB }
    More            = @{ CodePoint = 0xE712 }
    Chat            = @{ CodePoint = 0xE8BD }
    Repair          = @{ CodePoint = 0xE90F }
    ChevronLeft     = @{ CodePoint = 0xE76B }
    ChevronRight    = @{ CodePoint = 0xE76C }
    ChevronUp       = @{ CodePoint = 0xE70E }
    ChevronDown     = @{ CodePoint = 0xE70D }
    SelectAll       = @{ CodePoint = 0xE73A }
    Color           = @{ CodePoint = 0xE790 }
    OpenPane        = @{ CodePoint = 0xE8B7 }  # share Folder codepoint

    # RapikanFile Categories (3)
    Font            = @{ CodePoint = 0xE8D2 }
    Archive         = @{ CodePoint = 0xE8B1 }
    Applications    = @{ CodePoint = 0xE74C }
}
```

- [ ] **Step 3: Implement private function Test-NexusIconFont**

```powershell
function Test-NexusIconFont {
    <#
    .SYNOPSIS
        Private. Detects whether Segoe Fluent Icons is installed.
        Sets $script:NexusIconFontAvailable.
    #>
    $script:NexusIconFontAvailable = $false
    try {
        $families = [System.Windows.Media.Fonts]::SystemFontFamilies
        foreach ($f in $families) {
            if ($f.Source -eq $script:NexusIconFont) {
                $script:NexusIconFontAvailable = $true
                return $true
            }
        }
    } catch {
        $script:NexusIconFontAvailable = $false
    }
    return $false
}
```

**Catatan:** Ini private function, bukan bagian dari 3 public API.

- [ ] **Step 4: Implement Get-NexusIconDefinition**

```powershell
function Get-NexusIconDefinition {
    param([string]$Name)
    if ($script:NexusIconCatalog.ContainsKey($Name)) {
        return $script:NexusIconCatalog[$Name]
    }
    return $null
}
```

- [ ] **Step 5: Implement Get-NexusIcon**

```powershell
function Get-NexusIcon {
    param([string]$Name)
    $def = Get-NexusIconDefinition -Name $Name
    if ($null -ne $def) {
        return [char]$def.CodePoint
    }
    # No silent fallback — return $null, caller must handle
    return $null
}
```

**Tidak ada fallback 0xE161** — jika nama tidak ditemukan, return $null.

- [ ] **Step 6: Implement Set-NexusIcon (TextBlock only)**

```powershell
function Set-NexusIcon {
    <#
    .SYNOPSIS
        Sets icon glyph and FontFamily on a TextBlock.
        Does nothing if font unavailable or target null.
    #>
    param(
        [System.Windows.Controls.TextBlock]$Target,
        [string]$Name
    )
    if ($null -eq $Target) { return }
    if (-not $script:NexusIconFontAvailable) { return }
    $glyph = Get-NexusIcon -Name $Name
    if ($null -eq $glyph) { return }
    $Target.Text = $glyph.ToString()
    $Target.FontFamily = [System.Windows.Media.FontFamily]::new($script:NexusIconFont)
}
```

**Key behavior:**
- Hanya menerima `[System.Windows.Controls.TextBlock]` — compile-time safety
- Jika font tidak tersedia (`$script:NexusIconFontAvailable = $false`), silent skip
- Tidak set FontFamily di XAML, hanya di code

- [ ] **Step 7: Verify file syntax**

Run:
```powershell
powershell -Command "& { . '.\Foundation\NexusIcons.ps1'; Get-NexusIcon -Name 'Send' }"
```
Expected: no errors, returns a glyph character.

Run:
```powershell
powershell -Command "& { . '.\Foundation\NexusIcons.ps1'; Get-NexusIcon -Name 'NonExistentIcon' }"
```
Expected: returns $null (no output), no errors.

---

## Task 2: Clean ThemeManager.ps1

**Files:**
- Modify: `Foundation/ThemeManager.ps1`

- [ ] **Step 1: Remove `$script:NexusIcons` hash table (lines 21-32)**

Delete lines 21-32 — `$script:NexusIcons` with icon definitions.

- [ ] **Step 2: Remove `Get-NexusIcon` function (line 36)**

Delete line 36 — old `Get-NexusIcon` fallback function.

- [ ] **Step 3: Remove `New-ThemeToggleContent` function (lines 44-74)**

Delete the entire function — `New-ThemeToggleContent` with hardcoded MDL2 font.

- [ ] **Step 4: Replace theme toggle logic in Apply-Theme (lines 305-309)**

Replace:
```powershell
if ($BtnTheme) {
    $BtnTheme.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI Semibold')
    if ($cfg.theme -eq "Dark") {
        $BtnTheme.Content = New-ThemeToggleContent -IconCode 0xE706 -Label "LIGHT MODE" ...
    } else {
        $BtnTheme.Content = New-ThemeToggleContent -IconCode 0xE708 -Label "DARK MODE" ...
    }
}
```

With:
```powershell
if ($BtnTheme) {
    $isDark = ($cfg.theme -eq "Dark")
    # Show icon for mode TO ACTIVATE: Dark mode → show LightTheme (sun), Light mode → show DarkTheme (moon)
    $themeIconName = if ($isDark) { 'LightTheme' } else { 'DarkTheme' }
    $themeLabel = if ($isDark) { 'LIGHT MODE' } else { 'DARK MODE' }
    $iconToggle = $BtnTheme.FindName('IconThemeToggle')
    $labelText = $BtnTheme.FindName('LabelThemeToggle')
    if ($iconToggle) { Set-NexusIcon -Target $iconToggle -Name $themeIconName }
    if ($labelText) { $labelText.Text = $themeLabel }

    # Set theme button visual
    if ($isDark) {
        $BtnTheme.Background = [System.Windows.Media.SolidColorBrush]::new((HexToColor $script:Themes.Dark.WindowControlBg))
        $BtnTheme.Foreground = [System.Windows.Media.Brushes]::White
    } else {
        $BtnTheme.Background = [System.Windows.Media.Brushes]::Transparent
        $BtnTheme.Foreground = $textBrush
    }
}
```

**Theme toggle behavior (CORRECT):**
| Mode aktif | Ikon ditampilkan | Nama semantik | Teks label |
|------------|-----------------|---------------|------------|
| Dark | LightTheme (matahari) | 'LightTheme' | 'LIGHT MODE' |
| Light | DarkTheme (bulan) | 'DarkTheme' | 'DARK MODE' |

Ikon menunjukkan mode yang AKAN DIAKTIFKAN.

- [ ] **Step 5: Verify remaining ThemeManager only has theme code**

Expected remaining functions: `Get-ThemeColors`, `Get-ThemeColor`, `Set-Theme`, `Animate-BrushColor`, `HexToColor`, `Set-SafeBrush`, `Apply-Theme`. No icon code, no `$script:NexusIcons`, no `New-ThemeToggleContent`.

---

## Task 3: Bootstrap — Add NexusIcons.ps1 Loading to ManajemenFile.ps1

**Files:**
- Modify: `ManajemenFile.ps1`

- [ ] **Step 1: Add dot-source for NexusIcons.ps1 before ThemeManager**

Replace:
```powershell
# 6. Load Modules and ThemeManager
. "$PSScriptRoot\Foundation\ThemeManager.ps1"
```

With:
```powershell
# 6. Load NexusIcons (before ThemeManager — ThemeManager will reference its functions)
. "$PSScriptRoot\Foundation\NexusIcons.ps1"

# 7. Font detection at startup
$fontOk = Test-NexusIconFont
if (-not $fontOk) {
    $result = [System.Windows.MessageBox]::Show(
        "Font 'Segoe Fluent Icons' tidak terdeteksi. Ikon Nexus tidak akan ditampilkan.`n`nUnduh dan instal font dari Microsoft Design Resources, lalu restart Nexus.",
        'Font Ikon Tidak Ditemukan',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Warning
    )
    # App tetap jalan, tapi $NexusIconFontAvailable = $false
    # Semua Set-NexusIcon akan silent skip
}

# 8. Load Modules and ThemeManager
. "$PSScriptRoot\Foundation\ThemeManager.ps1"
```

**Perilaku font tidak tersedia:** App TETAP jalan (tidak dihentikan), hanya ikon yang tidak dirender. Notification ditampilkan. Test-NexusIconFont dipanggil SEBELUM ThemeManager agar ThemeManager bisa langsung pakai Set-NexusIcon tanpa load ulang.

- [ ] **Step 2: Update line numbers in comments**

Update subsequent section comment numbers (9, 10, etc.) in ManajemenFile.ps1.

- [ ] **Step 3: Replace Maximize/Restore icon logic (lines 350-360)**

Replace:
```powershell
function Update-MaximizeButtonIcon {
    if (-not $BtnMaximize) { return }
    if ($window.WindowState -eq [System.Windows.WindowState]::Maximized) {
        $BtnMaximize.Content = (Get-NexusIcon 'Restore')
    }
    else {
        $BtnMaximize.Content = (Get-NexusIcon 'Maximize')
    }
}
```

With:
```powershell
function Update-MaximizeButtonIcon {
    if (-not $BtnMaximize) { return }
    $icon = $BtnMaximize.FindName('IconMaximizeRestore')
    if (-not $icon) { return }
    if ($window.WindowState -eq [System.Windows.WindowState]::Maximized) {
        Set-NexusIcon -Target $icon -Name 'Restore'
    }
    else {
        Set-NexusIcon -Target $icon -Name 'Maximize'
    }
}
```

**Catatan:** BtnMaximize akan punya child TextBlock `x:Name="IconMaximizeRestore"` setelah migrasi XAML. Kita gunakan child TextBlock, bukan Button.Content.

- [ ] **Step 4: Add icon initialization after window load**

Add block after ThemeManager loading and before module registrations:
```powershell
# 9. Initialize MainWindow icons (font must be available)
if ($script:NexusIconFontAvailable) {
    Set-NexusIcon -Target $window.FindName('IconSidebarToggle') -Name 'Menu'
    Set-NexusIcon -Target $window.FindName('IconMaximizeRestore') -Name 'Maximize'
    Set-NexusIcon -Target $window.FindName('IconMinimize') -Name 'Minimize'
    Set-NexusIcon -Target $window.FindName('IconClose') -Name 'Close'
    Set-NexusIcon -Target $window.FindName('NavHomeIcon') -Name 'Home'
    Set-NexusIcon -Target $window.FindName('NavAIIcon') -Name 'AIWorkspace'
    Set-NexusIcon -Target $window.FindName('NavPdfToolsIcon') -Name 'PDFTools'
    Set-NexusIcon -Target $window.FindName('NavCleanIcon') -Name 'CleanDownloads'
    Set-NexusIcon -Target $window.FindName('NavRemoveBackgroundIcon') -Name 'RemoveBG'
    Set-NexusIcon -Target $window.FindName('NavRapikanIcon') -Name 'RapikanFile'
    Set-NexusIcon -Target $window.FindName('NavSettingsIcon') -Name 'Settings'
}
```

- [ ] **Step 5: Verify app starts without errors**

Launch Nexus normally (or run `powershell -File ".\ManajemenFile.ps1"` if standalone).
Expected: No "command not found" errors for icon functions.

---

## Task 4: Migrate MainWindow.xaml

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: Replace BtnSidebarToggle (line 20)**

Before:
```xml
<Button x:Name='BtnSidebarToggle' Content='&#xE700;' Width='36' Height='36' Background='Transparent' BorderThickness='0' FontSize='18' Margin='0,0,10,0' FontFamily='Segoe MDL2 Assets'/>
```
After:
```xml
<Button x:Name='BtnSidebarToggle' Width='36' Height='36' Background='Transparent' BorderThickness='0' FontSize='18' Margin='0,0,10,0'><TextBlock x:Name='IconSidebarToggle'/></Button>
```

- [ ] **Step 2: Replace BtnTheme icon and label (lines 28-29)**

Before:
```xml
<TextBlock Text='&#xE708;' FontFamily='Segoe MDL2 Assets' FontSize='11' Margin='0,0,6,0' VerticalAlignment='Center'/>
<TextBlock Text='DARK MODE' FontFamily='Segoe UI Semibold' VerticalAlignment='Center'/>
```
After:
```xml
<TextBlock x:Name='IconThemeToggle' FontSize='11' Margin='0,0,6,0' VerticalAlignment='Center'/>
<TextBlock x:Name='LabelThemeToggle' Text='DARK MODE' FontFamily='Segoe UI Semibold' VerticalAlignment='Center'/>
```

- [ ] **Step 3: Replace window control buttons (lines 32-34)**

Before:
```xml
<Button x:Name='BtnMinimize' Content='&#xE921;' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0' FontFamily='Segoe MDL2 Assets'/>
<Button x:Name='BtnMaximize' Content='&#xE922;' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0' FontFamily='Segoe MDL2 Assets'/>
<Button x:Name='BtnClose' Content='&#xE8BB;' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0' FontFamily='Segoe MDL2 Assets'/>
```
After:
```xml
<Button x:Name='BtnMinimize' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0'><TextBlock x:Name='IconMinimize'/></Button>
<Button x:Name='BtnMaximize' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0'><TextBlock x:Name='IconMaximizeRestore'/></Button>
<Button x:Name='BtnClose' Width='46' Height='32' FontSize='10' Background='Transparent' BorderThickness='0'><TextBlock x:Name='IconClose'/></Button>
```

- [ ] **Step 4: Replace navigation icons (7 sidebar TextBlocks)**

For each nav icon, remove `Text='&#x...'` and `FontFamily='Segoe MDL2 Assets'`. Keep `x:Name`.

Before pattern:
```xml
<TextBlock x:Name='NavHomeIcon' Text='&#xE80F;' FontSize='20' FontFamily='Segoe MDL2 Assets' VerticalAlignment='Center' HorizontalAlignment='Center'/>
```
After:
```xml
<TextBlock x:Name='NavHomeIcon' FontSize='20' VerticalAlignment='Center' HorizontalAlignment='Center'/>
```

Apply to: NavHomeIcon, NavAIIcon, NavPdfToolsIcon, NavCleanIcon, NavRemoveBackgroundIcon, NavRapikanIcon, NavSettingsIcon.

- [ ] **Step 5: Verify visually**

Launch Nexus. Check:
- All sidebar icons render correctly (Menu, Home, AIWorkspace, PDFTools, CleanDownloads, RemoveBG, RapikanFile, Settings)
- Window control buttons show proper glyphs (Minimize, Maximize/Restore, Close)
- Theme toggle shows icon + label correctly
- No empty squares (tofu)
- Toggle Light/Dark mode — icons should NOT change (theme toggle icon changes, not the nav icons)

---

## Task 5: Migrate AIWorkspaceView.xaml + AIWorkspace.ps1

**Files:**
- Modify: `Views/Modules/AIWorkspaceView.xaml`
- Modify: `Modules/AIWorkspace.ps1`

- [ ] **Step 1: Replace hardcoded glyphs in AIWorkspaceView.xaml**

Each Button with `Content='&#x...'` and `FontFamily='Segoe MDL2 Assets'` → Button with child TextBlock.

| Current Element | Current Glyph | New Pattern |
|----------------|---------------|-------------|
| `BtnAISettings` | Content='&#xE713;' | `<TextBlock x:Name='IconAISettings'/>` |
| `BtnAIMoreActions` | Content='&#xE76C;' | `<TextBlock x:Name='IconAIMoreActions'/>` |
| `BtnAIToggleSidebar` | Content='&#xE700;' | `<TextBlock x:Name='IconAIToggleSidebar'/>` |
| `AIWelcomeIcon` | Text='&#xE9CE;' | Keep x:Name, remove Text + FontFamily |
| `BtnAIAddAttachment` | Content='&#xE710;' | `<TextBlock x:Name='IconAIAddAttachment'/>` |
| `BtnAISend` | Content='&#xE122;' | `<TextBlock x:Name='IconAISend'/>` |

- [ ] **Step 2: Add Set-NexusIcon calls in AIWorkspace.ps1 initialization**

```powershell
if ($script:NexusIconFontAvailable) {
    Set-NexusIcon -Target $window.FindName('IconAISettings') -Name 'Settings'
    Set-NexusIcon -Target $window.FindName('IconAIMoreActions') -Name 'More'
    Set-NexusIcon -Target $window.FindName('IconAIToggleSidebar') -Name 'Menu'
    Set-NexusIcon -Target $window.FindName('AIWelcomeIcon') -Name 'Chat'
    Set-NexusIcon -Target $window.FindName('IconAIAddAttachment') -Name 'Add'
    Set-NexusIcon -Target $window.FindName('IconAISend') -Name 'Send'
}
```

- [ ] **Step 3: Replace `[char]0x...` patterns in AIWorkspace.ps1**

Each `$target.Content = [char]0x...` or `$icon.Text = [char]0x...` → `Set-NexusIcon`.

| Location | Current Code | Replacement |
|----------|-------------|-------------|
| Line 159 | `$icon.Text = if ($item.Type -eq 'Folder') { [char]0xE8D5 } else { [char]0xE7C3 }` | `$iconName = if ($item.Type -eq 'Folder') { 'FolderOpen' } else { 'File' }; Set-NexusIcon -Target $icon -Name $iconName` |
| Line 174 | `$btnRemove.Content = [char]0xE711` | Button → child TextBlock x:Name, then `Set-NexusIcon` with 'Close' |
| Lines 265-267 | `[char]0xE7C3` / `[char]0xEB9F` / `[char]0xE8B7` | `'File'` / `'Image'` / `'Folder'` via Set-NexusIcon |
| Line 287 | `$remove.Content = [string][char]0xE711` | Same as line 174 |
| Line 1889 | `"$([char]0xE8FB) Copied"` | `"$(Get-NexusIcon 'Check') Copied"` |

- [ ] **Step 4: Replace existing Get-NexusIcon calls in AIWorkspace.ps1**

Existing calls at lines 1446, 1455, 2356, 2372, 2377, 2387 currently set Button.Content. After XAML migration, these buttons have child TextBlocks. Replace with:

```powershell
# Lines 1446/1455: BtnAISend icon swap (Stop ↔ Send)
$iconSend = $BtnAISend.FindName('IconAISend')
if ($iconSend) {
    $iconName = if ($isStreaming) { 'Stop' } else { 'Send' }
    Set-NexusIcon -Target $iconSend -Name $iconName
}

# Lines 2356-2377: Copy/Check toggle
$iconCopy = $copy.FindName('IconCopyContent')
if ($iconCopy) { Set-NexusIcon -Target $iconCopy -Name 'Copy' }
# ... $iconCopy dynamically Set-NexusIcon 'Check' when copied

# Line 2387: Regenerate button
$iconRegen = $regenerate.FindName('IconRegenerate')
if ($iconRegen) { Set-NexusIcon -Target $iconRegen -Name 'Refresh' }
```

**Catatan:** Setiap button yang saat ini pakai `Content = (Get-NexusIcon 'X').ToString()` harus punya child TextBlock. Jika belum, tambahkan di XAML.

- [ ] **Step 5: Verify visually**

Launch Nexus. Navigate to AI Workspace. Check:
- Settings icon (gear) renders correctly
- Send/Stop button swaps correctly
- File attachment icons show correct type
- Copy/Check/Stop/Refresh buttons work

---

## Task 6: Migrate RapikanFileView.xaml + RapikanFile.ps1

**Files:**
- Modify: `Views/Modules/RapikanFileView.xaml`
- Modify: `Modules/RapikanFile.ps1`

- [ ] **Step 1: Replace hardcoded glyphs in RapikanFileView.xaml**

| Location | Current Glyph | New Pattern |
|----------|--------------|-------------|
| Line 34 (Browse) | Folder E8B7 | `<TextBlock x:Name='IconRapikanBrowse' .../>` |
| Line 51 (Start) | Play E768 | `<TextBlock x:Name='IconRapikanStart' .../>` |
| Line 59 (Undo) | Undo E7A7 | `<TextBlock x:Name='IconRapikanUndo' .../>` |
| Line 108 (Stats accent) | Tag E8EC | Keep x:Name, remove Text + FontFamily |
| Line 115 (Refresh) | Refresh E72C | `<TextBlock x:Name='IconRapikanRefresh' .../>` |
| Line 150 (Clear log) | Delete E74D | `<TextBlock x:Name='IconRapikanClearLog' .../>` |
| Line 174 (Open log) | Document E8A5 | `<TextBlock x:Name='IconRapikanOpenLog' .../>` |

- [ ] **Step 2: Add Set-NexusIcon calls in RapikanFile.ps1 initialization**

```powershell
if ($script:NexusIconFontAvailable) {
    Set-NexusIcon -Target $window.FindName('IconRapikanBrowse') -Name 'Folder'
    Set-NexusIcon -Target $window.FindName('IconRapikanStart') -Name 'Play'
    Set-NexusIcon -Target $window.FindName('IconRapikanUndo') -Name 'Undo'
    Set-NexusIcon -Target $window.FindName('IconRapikanStatsAccent') -Name 'Stats'
    Set-NexusIcon -Target $window.FindName('IconRapikanRefresh') -Name 'Refresh'
    Set-NexusIcon -Target $window.FindName('IconRapikanClearLog') -Name 'Delete'
    Set-NexusIcon -Target $window.FindName('IconRapikanOpenLog') -Name 'Document'
}
```

- [ ] **Step 3: Replace category icon mapping (lines 83-96)**

Replace the `$iconMap` hash table with icon name mapping, then use Set-NexusIcon:

```powershell
$iconNameMap = @{
    'Images'       = 'Image'
    'Videos'       = 'Videos'
    'Desain'       = 'Edit'
    'Documents'    = 'Document'
    'Audio'        = 'Audio'
    'Font'         = 'Font'
    'Archives'     = 'Archive'
    'Applications' = 'Applications'
    'Others'       = 'NewFolder'
    'Sampah'       = 'Delete'
    'TotalFile'    = 'Tag'
    'TotalSize'    = 'Size'
}
```

Replace usage:
```powershell
$iconChar = if ($iconMap.ContainsKey($category)) { $iconMap[$category].ToString() } else { [char]0xE8F4 }
```
With:
```powershell
$iconName = if ($iconNameMap.ContainsKey($category)) { $iconNameMap[$category] } else { 'NewFolder' }
Set-NexusIcon -Target $targetIcon -Name $iconName
```

- [ ] **Step 4: Verify visually**

Launch Nexus. Navigate to Rapikan File. Check:
- Toolbar icons render (Folder, Play, Undo)
- Stats accent icon shows AreaChart glyph
- Refresh/Delete/Document icons correct
- Category icons show correct mappings

---

## Task 7: Migrate RemoveBackgroundView.xaml + RemoveBackground.ps1

**Files:**
- Modify: `Views/Modules/RemoveBackgroundView.xaml`
- Modify: `Modules/RemoveBackground.ps1`

- [ ] **Step 1: Replace hardcoded glyphs in RemoveBackgroundView.xaml**

| Location | Current Glyph | New Pattern |
|----------|--------------|-------------|
| Line 295 | ChevronDown E70D | `<TextBlock x:Name='IconRbChevronDown' .../>` |
| Line 375 | Photo2 EB9F | `<TextBlock x:Name='IconRbEmpty' .../>` |
| Line 439 | ZoomOut E71F | `<TextBlock x:Name='IconRbZoomOut' .../>` |
| Line 449 | Zoom E71E | `<TextBlock x:Name='IconRbZoomIn' .../>` |
| Line 463 | Folder E8B7 | `<TextBlock x:Name='IconRbFolder' .../>` |
| Line 468 | Switch E8AB | `<TextBlock x:Name='IconRbCompare' .../>` |
| Line 496 | Settings E713 | `<TextBlock x:Name='IconRbSettings' .../>` |
| Line 561 | Folder E8B7 | `<TextBlock x:Name='IconRbResultFolder' .../>` |
| Line 581 | Folder E8B7 | `<TextBlock x:Name='IconRbBrowse1' .../>` |
| Line 596 | Folder E8B7 | `<TextBlock x:Name='IconRbBrowse2' .../>` |
| Line 611 | Folder E8B7 | `<TextBlock x:Name='IconRbBrowse3' .../>` |
| Line 645 | Info E946 | `<TextBlock x:Name='IconRbInfo' .../>` |
| Line 685 | Warning E7BA | `<TextBlock x:Name='IconRbWarning' .../>` |

Buttons with `Tag='&#x...'` patterns (lines 343, 347, 384, 662, 665, 668) — these use Tag to pass glyph + FontFamily via template binding (line 197: `<TextBlock Text="{TemplateBinding Tag}" FontFamily="Segoe MDL2 Assets" .../>`). These need a different approach — likely a DataTemplate style change. See step 3.

- [ ] **Step 2: Fix template-bound icon buttons (lines 343, 347, 384, 662, 665, 668)**

These buttons have `Tag="&#xE..."` which is read by a template TextBlock (line 197). After migration, Tag should hold the semantic icon name instead of glyph, and the template TextBlock should call Set-NexusIcon at runtime. Or: change template to bind to a different property.

**Approach:** Keep Tag for icon name (not glyph), change template TextBlock to be named or identified, then set icon during item generation.

- [ ] **Step 3: Replace status icon mapping (lines 406-409, 510-513, 529-532)**

Replace old MDL2-based switch:
```powershell
'Processing' { [char]0xE769 }   # Pause
'Result'     { [char]0xE74E }   # Save
'Error'      { [char]0xE777 }   # (unknown)
default      { [char]0xE8C4 }   # (unknown)
```

With semantic Fluent names:
```powershell
'Processing' { 'Sync' }
'Result'     { 'Check' }
'Error'      { 'Error' }
default      { 'Document' }
```

Then call `Set-NexusIcon -Target $targetIcon -Name $resultName` at each call site.

**Semantic rationale:**
- Processing → Sync (aktivitas berlangsung, bukan Pause yang artinya berhenti)
- Result → Check (berhasil selesai)
- Error → Error (kesalahan)
- default → Document (dokumen awal)

- [ ] **Step 4: Verify visually**

Launch Nexus. Navigate to Remove Background. Check:
- Zoom in/out icons render
- Folder selection icon shows Folder glyph
- Settings/Info/Warning icons correct
- Status indicators show correct glyphs (Sync, Check, Error)

---

## Task 8: Migrate Remaining Views

**Files:**
- Modify: `Views/Modules/CleanDownloadsView.xaml`
- Modify: `Views/Modules/PdfToolsView.xaml`
- Modify: `Views/Modules/SettingsView.xaml`
- Modify: `Views/Modules/HomeView.xaml`
- Modify: `Themes/SharedStyles.xaml`

- [ ] **Step 1: CleanDownloadsView.xaml (9 glyphs)**

| Line | Current Glyph | x:Name | Nexus Name |
|------|--------------|--------|------------|
| 21 | E8EC (Tag) | IconCleanTotalAccent | Stats (E9D2) |
| 34 | E958 (Size) | IconCleanSize | Size (E958) |
| 47 | E74D (Delete) | IconCleanDelete | Delete (E74D) |
| 60 | E8B7 (Folder) | IconCleanFolder | Folder (E8B7) |
| 84 | E72C (Refresh) | IconCleanRefresh | Refresh (E72C) |
| 92 | E73A (SelectAll) | IconCleanSelectAll | SelectAll (E73A) |
| 100 | E711 (Cancel) | IconCleanClearSelect | Close (E8BB) |
| 108 | E7A7 (Undo) | IconCleanUndo | Undo (E7A7) |
| 162 | E8A0 (OpenPane) | IconCleanEmptyState | Folder (E8B7) — share with Folder |

Add initialization in CleanDownloads.ps1 (or module init function).

- [ ] **Step 2: PdfToolsView.xaml (3 glyphs)**

| Line | Current Glyph | x:Name | Nexus Name |
|------|--------------|--------|------------|
| 48 | E8E5 (OpenFile) | IconPdfEmptyState | PDF (EA90) |
| 76 | E8A5 (Document) | IconPdfFileIcon | Document (E8A5) |
| 90 | E711 (Cancel) | BtnPdfRemoveFile | Close (E8BB) — Button with child TextBlock |

Add initialization in PdfTools.ps1.

- [ ] **Step 3: SettingsView.xaml (8 glyphs)**

| Line | Current Glyph | x:Name | Nexus Name |
|------|--------------|--------|------------|
| 14 | E771 (Personalize) | IconThemePalette | ThemePalette (E771) |
| 21 | E706 (Brightness) | IconLightMode | LightTheme (E706) |
| 27 | E708 (QuietHours) | IconDarkMode | DarkTheme (E708) |
| 40 | E8B7 (Folder) | IconSettingsFolderAccent | Folder (E8B7) |
| 52 | E8B7 (Folder) | IconSettingsFolder | Folder (E8B7) |
| 66 | E7FB (DeviceMonitor) | IconSettingsMonitor | Confirmation (E930) |
| 80 | E74A (Up) | IconSettingsSave | Save (E74E) |
| 88 | E72C (Refresh) | IconSettingsRefresh | Refresh (E72C) |

Add initialization in Settings.ps1.

- [ ] **Step 4: HomeView.xaml (3 glyphs)**

| Line | Current Glyph | x:Name | Nexus Name |
|------|--------------|--------|------------|
| 43 | E930 (Completed) | IconStatusCompleted | Confirmation (E930) |
| 55 | E94B (CalcSqrt) | IconEngineAccent | Engine (E950) |
| 67 | E8B7 (Folder) | IconFolderAccent | Folder (E8B7) |

Add initialization in Home.ps1.

- [ ] **Step 5: SharedStyles.xaml (2 glyphs)**

| Line | Current Glyph | x:Name | Nexus Name |
|------|--------------|--------|------------|
| 99 | E70D (ChevronDown) | IconComboChevron | ChevronDown (E70D) |
| 151 | E790 (Color) | IconColorPicker | Color (E790) |

SharedStyles.xaml uses ResourceDictionary — TextBlocks may be in ControlTemplates. For template-based icons, initialization happens when template is applied (not at startup). Options:
1. Add x:Name and set via code-behind in Apply-Theme
2. Keep minimal FontFamily reference if template doesn't allow code injection

**Recommendation:** For ControlTemplate icons, set icon in the code that loads the template or in Apply-Theme. If that's not feasible, keep FontFamily reference with the new font name instead.

- [ ] **Step 6: Initialize all module icons in their respective init functions**

Each module's Initialize-* function should include a guarded block:
```powershell
if ($script:NexusIconFontAvailable) {
    Set-NexusIcon -Target $ViewRoot.FindName('IconXxx') -Name 'Xxx'
    # ... all icons for this module
}
```

- [ ] **Step 7: Verify all views**

Launch Nexus. Navigate to every module. Check every icon renders correctly.

---

## Task 9: Verification & Cleanup

- [ ] **Step 1: Verify no remaining MDL2 references in XAML**

```powershell
$results = Get-ChildItem -Recurse -Filter *.xaml | Select-String "Segoe MDL2 Assets"
if ($results) { $results | Format-Table Path, LineNumber }
```
Expected: 0 results (all XAML files cleaned).

- [ ] **Step 2: Verify no remaining `[char]0x` in non-catalog PS1 files**

```powershell
$results = Get-ChildItem -Recurse -Filter *.ps1 `
    | Where-Object { $_.FullName -notmatch '\\Archive\\' -and $_.FullName -notmatch 'scratch\\' -and $_.FullName -notmatch '\\NexusIcons\\.ps1$' } `
    | Select-String "\[char\]0x"
if ($results) { $results | Format-Table Path, LineNumber }
```
Expected: 0 results. **Excludes NexusIcons.ps1** (it has legitimate `[char]` in Get-NexusIcon) and Archive/scratch.

- [ ] **Step 3: Verify no remaining hardcoded glyph `&#x` in XAML**

```powershell
$results = Get-ChildItem -Recurse -Filter *.xaml | Select-String "&#x"
if ($results) { $results | Format-Table Path, LineNumber }
```
Expected: 0 results.

- [ ] **Step 4: Verify ThemeManager.ps1 has no icon code**

```powershell
Get-Content .\Foundation\ThemeManager.ps1 | Select-String -Pattern "NexusIcon|MDL2|0x"
```
Expected: only theme-related references (no icon catalog, no Get-NexusIcon, no New-ThemeToggleContent).

- [ ] **Step 5: Font detection test (safe)**

Test font detection by temporarily checking with a known-invalid name:
```powershell
# Don't uninstall the real font! Instead, verify function logic:
. .\Foundation\NexusIcons.ps1
$fontOk = Test-NexusIconFont
Write-Host "Font available: $fontOk"  # Should be $true
```
To test the "font missing" path, create a temporary test script that:
```powershell
$oldFont = $script:NexusIconFont
$script:NexusIconFont = 'NonExistentFontName'
$result = Test-NexusIconFont  # Should be $false
$script:NexusIconFont = $oldFont
```

- [ ] **Step 6: Screenshot comparison**

Take screenshots of each module before/after to verify visual parity.

---

## Revised Corrections (12-point summary)

| # | Issue | Correction Applied |
|---|-------|-------------------|
| 1 | Catalog count mismatch (64 vs 84) | Updated to 84 entries; design spec header to be fixed |
| 2 | Test-NexusIconFont as 4th "API function" | Classified as private; only 3 public API functions |
| 3 | Silent fallback glyph 0xE161 | Get-NexusIcon returns $null when not found |
| 4 | Font-unavailable still proceeds | Set-NexusIcon silently skips when font unavailable; app continues |
| 5 | Theme toggle logic reversed | Corrected: Dark mode → show LightTheme (sun), Light mode → show DarkTheme (moon) |
| 6 | Set-NexusIcon accepts Button | Parameter typed to [TextBlock]; caller must find child TextBlock |
| 7 | Maximize/Restore uses Button.Content | Changed to child TextBlock `IconMaximizeRestore` via Set-NexusIcon |
| 8 | AI More Actions mapping | Consistent: ChevronRight → 'More' (0xE712) |
| 9 | RemoveBG status mapping wrong | Processing→Sync, Result→Check, Error→Error, default→Document |
| 10 | `[char]0x` search scope too broad | Excludes NexusIcons.ps1, Archive/, scratch/ |
| 11 | Font uninstall test unsafe | Replaced with safe alternative (mock font name, don't uninstall) |
| 12 | Rollback command dangerous | Replaced with safer patterns (see below) |

---

## Rollback Plan (SAFE)

### Per Task:
- Setiap task adalah commit terpisah:
  ```bash
  git add <files>; git commit -m "feat(icons): migrate <component>"
  ```
- Rollback per task: `git restore --source <previous-commit-hash> -- <files>`
  Or: `git revert --no-commit <commit-hash>` to revert a specific commit without creating a new commit.

### Full Rollback:
```bash
# Find the first icon-migration commit
git log --oneline --grep="feat(icons):" --reverse
# Then restore files from before that commit:
git restore --source $(git log --oneline --grep="feat(icons):" --reverse --format="%H" | select -First 1)^ -- .
```

### Partial Rollback (file level):
```bash
git restore --source HEAD~1 -- Views/MainWindow.xaml
git restore --source HEAD~1 -- Foundation/ThemeManager.ps1
```
Or specify exact commit hash: `git restore --source abc1234 -- <file>`

**Jangan gunakan** `git checkout main -- .` — ini overwrite semua file dari branch main tanpa konfirmasi.
**Jangan gunakan** `git revert HEAD~8..HEAD` — ini asumsi 8 commit linear pada branch.
