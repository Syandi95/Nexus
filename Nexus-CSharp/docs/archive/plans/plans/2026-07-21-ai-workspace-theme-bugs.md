# AI Workspace Theme Bugs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two theme bugs in AI Workspace: code blocks not updating on theme switch, and attachment MenuItems/ContextMenu not themed in dark mode.

**Architecture:** Both fixes localized to `Apply-AIWorkspaceTheme` and `Render-AIChatBox` in `Modules\AIWorkspace.ps1`.

**Tech Stack:** WPF, PowerShell, DynamicResource/Theming system

## Global Constraints

- Only modify `Modules\AIWorkspace.ps1` — no XAML changes
- Keep all changes minimal, focused, and safe — no restructuring
- Maintain scroll position when re-rendering

---

### Task 1: Fix code blocks not updating on theme switch

**Files:**
- Modify: `Modules\AIWorkspace.ps1` — `Apply-AIWorkspaceTheme` function (end)
- Modify: `Modules\AIWorkspace.ps1` — `New-AIWorkspaceCodeBlock` function (tag code blocks)

**Root cause:** `New-AIWorkspaceCodeBlock` creates `Border` elements with hardcoded inline hex colors (`"#1E1E1E"`, `"#F5F5F5"`, etc.) at message creation time. These colors never update when the user switches themes because `Apply-AIWorkspaceTheme` doesn't touch existing code blocks in the `AIChatItems` ItemsControl.

**Fix approach:** Re-render all messages by calling `Render-AIChatBox` at the end of `Apply-AIWorkspaceTheme`, so every code block is recreated with the current theme's colors.

**Why this is safe:** `Render-AIChatBox`:
- Clears and re-populates `$AIChatItems.Items` from `$script:AIChatHistory` (read-only model)
- Reuses `New-AIMessageItem` which calls `New-AIWorkspaceCodeBlock` with current `Test-AIWorkspaceIsDark`
- Clears and re-populates `$script:AIChatVisualMap` via `New-AIMessageItem` registration
- Already handles scroll-to-bottom if near bottom
- Does NOT trigger any AI network calls — purely UI re-render

- [ ] **Step 1: Add `Render-AIChatBox` call at end of `Apply-AIWorkspaceTheme`**

After the combo box color sync block (line ~2940), add a try/catch that calls `Render-AIChatBox`:

```powershell
# Re-render messages so code blocks pick up the new theme
try {
    if ($script:AIChatItems) { Render-AIChatBox }
} catch { Write-Diag "AIWorkspace theme re-render FAILED: $_" }
```

The `if ($script:AIChatItems)` guard ensures we only re-render if the ItemsControl is initialized.

- [ ] **Step 2: Verify**

- Launch app, switch to AI Workspace
- Type a message in dark mode → see code block with dark bg (`#1E1E1E`)
- Switch to light mode → code block should now have white bg (`#FFFFFF`)
- Switch back to dark mode → code block should revert to dark bg
- Verify scroll position is maintained (near-bottom auto-scroll)

---

### Task 2: Fix attachment MenuItems and ContextMenu not themed in dark mode

**Files:**
- Modify: `Modules\AIWorkspace.ps1` — `Apply-AIWorkspaceTheme` function

**Root cause:** Two issues:
1. The text sweep (line 2893) includes `BtnAIAttachFile` and `BtnAIAttachFolder` (hidden buttons), but does NOT include `MenuAIAttachFile` and `MenuAIAttachFolder` (the visible ContextMenu items). So in dark mode, the "File"/"Folder" menu items have black text (system default).
2. The ContextMenu popup for `BtnAIAddAttachment` uses system default styles for its background, border, and popup appearance. In dark mode app on any system, the ContextMenu popup border remains gray (system `SystemColors.ControlDarkBrushKey`).

**Fix approach:** Add `MenuAIAttachFile` and `MenuAIAttachFolder` to the text sweep. Also theme the ContextMenu's background, border, and foreground directly.

- [ ] **Step 1: Add MenuItems to theme text sweep**

Add `MenuAIAttachFile` and `MenuAIAttachFolder` to the existing text sweep list at line 2893:

```powershell
foreach ($t in @("AIWelcomeTitle", "TxtAIDiffPath", "TxtAIDiffContent", "TxtAIAttachLabel", "BtnAIApplyEdit", "BtnAIDiscardEdit", "BtnAIAttachFile", "BtnAIAttachFolder", "MenuAIAttachFile", "MenuAIAttachFolder")) {
```

- [ ] **Step 2: Theme the ContextMenu popup for BtnAIAddAttachment**

Add this block after the text sweep (after line 2900) to apply theme colors to the ContextMenu:

```powershell
# Attachment ContextMenu theme
if ($script:BtnAIAddAttachment -and $script:BtnAIAddAttachment.ContextMenu) {
    $ctx = $script:BtnAIAddAttachment.ContextMenu
    $ctx.Foreground = $textBrush
    $ctx.Background = $inputBg
    $ctx.BorderBrush = $borderBrush
}
```

- [ ] **Step 3: Verify**

- Launch app, switch to AI Workspace in dark mode
- Click the paperclip attachment button
- ContextMenu should appear with white "File"/"Folder" text
- ContextMenu popup border should be themed (not gray)
- Switch to light mode, repeat — should look correct in light mode too
