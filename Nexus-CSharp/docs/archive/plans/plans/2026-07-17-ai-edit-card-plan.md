# AI Edit Proposal Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace raw `aiedit` block rendering with inline "Perubahan AI" card in chat messages.

**Architecture:** Six sequential tasks in `Modules/AIWorkspace.ps1` only: (1) strip aiedit from display, (2) update pending-edit state to hashtable per-message format, (3) create card UI function, (4) inject card into assistant messages, (5) update Finalize flow to show card instead of raw diff, (6) update apply/discard handlers to clean up card.

**Tech Stack:** PowerShell 5.1, WPF (System.Windows.Controls), Nexus ThemeManager

## Global Constraints

- Do NOT change parser (`ConvertFrom-AIFileEditProposal`)
- Do NOT change session engine (`New-AIFileEditSession`, `Get-AIFileEditSessionDiff`, `Restore-AIFileEditSession`)
- Do NOT change workflow (`Invoke-AIFileEditSession`)
- Do NOT change Safe-Edit.ps1, Atomic.ps1, or AIFileEditor.ps1
- All colors from `Get-ThemeColor`, no hardcoded hex values except `White` / `Transparent`
- 15/15 regression tests must pass unchanged
- Only modify `Modules/AIWorkspace.ps1`

---
### Task 1: Strip `aiedit` Blocks from Rendering

**Files:**
- Modify: `Modules/AIWorkspace.ps1` — `Split-AIWorkspaceCodeBlocks` (line ~1483-1499)
- Modify: `Modules/AIWorkspace.ps1` — `ConvertTo-AIPlainText` (line ~1459-1463)

**Interfaces:**
- Consumes: `Split-AIWorkspaceCodeBlocks(Text)` — existing function, no signature change
- Consumes: `ConvertTo-AIPlainText(Text)` — existing function, no signature change
- Produces: aiedit fenced blocks hidden from both the rich chat rendering and the plain-text fallback

**Acceptance:** When assistant response contains ````aiedit:path```` block with OLD/NEW, the block does not appear visually in chat; other code blocks and text render normally.

---

**Step 1 — Modify `Split-AIWorkspaceCodeBlocks`**

Inside the fenced match loop (lines ~1497-1499), after capturing `$language`:

```powershell
# Current code (lines ~1497-1499):
$language = $match.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($language)) { $language = "text" }
$blocks.Add([pscustomobject]@{ Type = "code"; Language = $language; Content = $match.Groups[2].Value.Trim() })

# Change to:
$language = $match.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($language)) { $language = "text" }
# Hide aiedit protocol blocks from user display
if ($language -match '^aiedit') { continue }
$blocks.Add([pscustomobject]@{ Type = "code"; Language = $language; Content = $match.Groups[2].Value.Trim() })
```

- [ ] **Step 1.1 — Add skip for aiedit blocks inside fenced loop**

**Step 2 — Modify `ConvertTo-AIPlainText`**

Before the existing regex replacement of fenced code blocks (line ~1460-1463), add a stripping regex for aiedit blocks:

```powershell
# Add BEFORE the existing fenced-code regex (line ~1460):
# Strip entire aiedit protocol blocks (internal, not for display)
$Text = [regex]::Replace($Text, '(?ms)```aiedit:[^\r\n]*\r?\n.*?````[ \t]*$?', '')
```

This regex matches from ````aiedit:` to the closing `````, consuming the entire block including OLD/NEW content.

- [ ] **Step 2.1 — Add aiedit-strip regex before fenced-code regex**

**Step 3 — Verify:**

- [ ] **Step 3.1 — Run regression tests**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 3.2 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "feat(ai-workspace): strip aiedit protocol blocks from chat rendering"
```

---
### Task 2: Update Pending Edit State to Per-Message Hashtable

**Files:**
- Modify: `Modules/AIWorkspace.ps1` — `Show-AIDiffProposal` (line ~1392-1401) — remove `$script:AIPendingEdit = $Session`
- Modify: `Modules/AIWorkspace.ps1` — `BtnAIApplyEdit` click handler (line ~1004-1032) — update `$script:AIPendingEdit` access to hashtable
- Modify: `Modules/AIWorkspace.ps1` — `Hide-AIDiffProposal` (line ~1403-1408) — reset `$script:AIPendingEdit`
- Modify: `Modules/AIWorkspace.ps1` — `Finalize-AIAssistantMessage` (line ~1331-1333) — set hashtable after session creation

**Interfaces:**
- Consumes: `Show-AIDiffProposal(Session)` — unchanged signature
- Consumes: `Hide-AIDiffProposal()` — unchanged signature
- Consumes: `$script:AIChatHistory.Count` — for MessageIndex
- Produces: `$script:AIPendingEdit` is now `@{ Session = <session>; MessageIndex = <int> }` or `$null`

---

**Step 1 — Remove `$script:AIPendingEdit = $Session` from `Show-AIDiffProposal`**

Current code (line ~1393-1401):
```powershell
function Show-AIDiffProposal {
    param([Parameter(Mandatory)][psobject]$Session)
    $diff = Get-AIFileEditSessionDiff -Session $Session
    $script:AIPendingEdit = $Session    # ← REMOVE THIS LINE
    if ($TxtAIDiffPath)
```

- [ ] **Step 1.1 — Remove line `$script:AIPendingEdit = $Session` from Show-AIDiffProposal**

**Step 2 — Set hashtable in `Finalize-AIAssistantMessage`**

Inside the success branch after `New-AIFileEditSession` (around line ~1331-1333):

```powershell
# Current:
$created = New-AIFileEditSession ...
if ($created.Success) {
    Show-AIDiffProposal -Session $created.Session
}

# Change to:
$created = New-AIFileEditSession ...
if ($created.Success) {
    $script:AIPendingEdit = @{
        Session = $created.Session
        MessageIndex = $script:AIChatHistory.Count - 1
    }
    # Card will appear on next Render-AIChatBox via New-AIMessageItem
    # Do NOT call Show-AIDiffProposal (diff panel closed by default)
}
```

- [ ] **Step 2.1 — Replace Show-AIDiffProposal call with hashtable assignment in Finalize-AIAssistantMessage**

**Step 3 — Update `BtnAIApplyEdit` click handler to use hashtable**

Current code (lines ~1006-1021):
```powershell
if (-not $script:AIPendingEdit) { return }
$confirm = [System.Windows.MessageBox]::Show(
    "Terapkan perubahan ke:`n$($script:AIPendingEdit.TargetPath)?",
    ...
$session = $script:AIPendingEdit
```

Change to:
```powershell
if (-not $script:AIPendingEdit) { return }
$confirm = [System.Windows.MessageBox]::Show(
    "Terapkan perubahan ke:`n$($script:AIPendingEdit.Session.TargetPath)?",
    ...
$session = $script:AIPendingEdit.Session
```

- [ ] **Step 3.1 — Update `$script:AIPendingEdit.TargetPath` to `$script:AIPendingEdit.Session.TargetPath` in BtnAIApplyEdit**
- [ ] **Step 3.2 — Update `$session = $script:AIPendingEdit` to `$session = $script:AIPendingEdit.Session` in BtnAIApplyEdit**

**Step 4 — Verify `Hide-AIDiffProposal` resets `$script:AIPendingEdit` to `$null`**

Current code (lines ~1403-1408):
```powershell
function Hide-AIDiffProposal {
    $script:AIPendingEdit = $null
    if ($AIDiffPanel) { $AIDiffPanel.Visibility = 'Collapsed' }
    ...
```

This already sets `$null`, which is correct. No change needed.

- [ ] **Step 4.1 — Confirm Hide-AIDiffProposal already resets AIPendingEdit to $null**

**Step 5 — Verify:**

- [ ] **Step 5.1 — Run regression tests**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 5.2 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "refactor(ai-workspace): change AIPendingEdit to hashtable with MessageIndex"
```

---
### Task 3: Create `New-AIEditProposalCard` Function

**Files:**
- Create (inline in): `Modules/AIWorkspace.ps1` — new function placed after `New-AIWorkspaceCodeBlock` (~line 1611)

**Interfaces:**
- Consumes: `$Session` (AIFileEditSession object), `$MessageIndex` (int)
- Consumes: `Get-ThemeColor('Key')` for all colors
- Produces: `[System.Windows.Controls.Border]` WPF element containing card layout

**Acceptance:** Card shows "✏ Perubahan AI" title, file path, status text, and three buttons (Lihat Detail, Terapkan, Batalkan) styled with theme colors.

---

**Step 1 — Write function**

```powershell
function New-AIEditProposalCard {
    param(
        [Parameter(Mandatory)][psobject]$Session,
        [int]$MessageIndex
    )
    $root = New-Object System.Windows.Controls.Border
    $root.Margin = "0,12,0,0"
    $root.CornerRadius = "14"
    $root.Background = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'PanelBackground')))
    $root.BorderBrush = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'Border')))
    $root.BorderThickness = "1"
    $root.Padding = "16,12"
    $root.Tag = $MessageIndex

    $stack = New-Object System.Windows.Controls.StackPanel
    $stack.Orientation = "Vertical"

    $title = New-Object System.Windows.Controls.TextBlock
    $title.Text = "✏ Perubahan AI"
    $title.FontSize = 13
    $title.FontWeight = "SemiBold"
    $title.Foreground = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'TextPrimary')))
    $title.Margin = "0,0,0,6"
    $stack.Children.Add($title) | Out-Null

    $pathLine = New-Object System.Windows.Controls.TextBlock
    $pathLine.Text = "📄 $($Session.TargetPath)"
    $pathLine.FontFamily = "Consolas"
    $pathLine.FontSize = 11
    $pathLine.Foreground = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'TextSecondary')))
    $pathLine.Margin = "0,0,0,2"
    $stack.Children.Add($pathLine) | Out-Null

    $status = New-Object System.Windows.Controls.TextBlock
    $status.Text = "Perubahan siap ditinjau."
    $status.FontSize = 12
    $status.Foreground = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'TextSecondary')))
    $status.Margin = "0,0,0,10"
    $stack.Children.Add($status) | Out-Null

    $btnPanel = New-Object System.Windows.Controls.StackPanel
    $btnPanel.Orientation = "Horizontal"
    $btnPanel.Margin = "0,0,0,0"

    $detailBtnBorder = New-Object System.Windows.Controls.Border
    $detailBtnBorder.CornerRadius = "10"
    $detailBtnBorder.Background = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'BackgroundInput')))
    $detailBtnBorder.BorderBrush = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'Border')))
    $detailBtnBorder.BorderThickness = "1"
    $detailBtnBorder.Margin = "0,0,8,0"
    $detailBtn = New-Object System.Windows.Controls.Button
    $detailBtn.Content = "🔍 Lihat Detail"
    $detailBtn.Background = "Transparent"
    $detailBtn.BorderThickness = "0"
    $detailBtn.Foreground = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'TextPrimary')))
    $detailBtn.Padding = "14,7"
    $detailBtn.FontSize = 12
    $detailBtn.FontWeight = "SemiBold"
    $detailBtn.Cursor = "Hand"
    $detailBtn.Add_Click({
        param($s, $e)
        try {
            Show-AIDiffProposal -Session $Session
        } catch { Write-CrashLog $_ }
    })
    $detailBtnBorder.Child = $detailBtn
    $btnPanel.Children.Add($detailBtnBorder) | Out-Null

    $applyBtnBorder = New-Object System.Windows.Controls.Border
    $applyBtnBorder.CornerRadius = "10"
    $applyBtnBorder.Background = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'Success')))
    $applyBtnBorder.Margin = "0,0,8,0"
    $applyBtn = New-Object System.Windows.Controls.Button
    $applyBtn.Content = "✅ Terapkan"
    $applyBtn.Background = "Transparent"
    $applyBtn.BorderThickness = "0"
    $applyBtn.Foreground = [System.Windows.Media.Brushes]::White
    $applyBtn.Padding = "14,7"
    $applyBtn.FontSize = 12
    $applyBtn.FontWeight = "SemiBold"
    $applyBtn.Cursor = "Hand"
    $applyBtn.Add_Click({
        param($s, $e)
        try {
            if (-not $script:AIPendingEdit) { return }
            $confirm = [System.Windows.MessageBox]::Show(
                "Terapkan perubahan ke:`n$($Session.TargetPath)?",
                'Konfirmasi Terapkan Perubahan', 'YesNo', 'Warning')
            if ($confirm -ne [System.Windows.MessageBoxResult]::Yes) { return }

            $result = Invoke-AIFileEditSession -Session $Session -Approved
            if ($result.Success) {
                $script:AILastEditedFile = $Session
                if ($BtnAIRollbackBorder) { $BtnAIRollbackBorder.Visibility = 'Visible' }
                Add-AIChatMessage -Role 'system' -Content "Perubahan diterapkan ke $($Session.TargetPath): $($result.Code) - $($result.Message)"
                Hide-AIDiffProposal
                Render-AIChatBox
            } else {
                Add-AIChatMessage -Role 'system' -Content "Gagal menerapkan perubahan: $($result.Code) - $($result.Message)"
                if ($result.Code -ne 'ApprovalRequired') {
                    Hide-AIDiffProposal
                    Render-AIChatBox
                }
            }
        } catch { Write-CrashLog $_ }
    })
    $applyBtnBorder.Child = $applyBtn
    $btnPanel.Children.Add($applyBtnBorder) | Out-Null

    $discardBtnBorder = New-Object System.Windows.Controls.Border
    $discardBtnBorder.CornerRadius = "10"
    $discardBtnBorder.Background = [System.Windows.Media.SolidColorBrush]::new((HexToColor (Get-ThemeColor 'Error')))
    $discardBtn = New-Object System.Windows.Controls.Button
    $discardBtn.Content = "✖ Batalkan"
    $discardBtn.Background = "Transparent"
    $discardBtn.BorderThickness = "0"
    $discardBtn.Foreground = [System.Windows.Media.Brushes]::White
    $discardBtn.Padding = "14,7"
    $discardBtn.FontSize = 12
    $discardBtn.FontWeight = "SemiBold"
    $discardBtn.Cursor = "Hand"
    $discardBtn.Add_Click({
        param($s, $e)
        try {
            Restore-AIFileEditSession -Session $Session | Out-Null
            Hide-AIDiffProposal
            Render-AIChatBox
        } catch { Write-CrashLog $_ }
    })
    $discardBtnBorder.Child = $discardBtn
    $btnPanel.Children.Add($discardBtnBorder) | Out-Null

    $stack.Children.Add($btnPanel) | Out-Null
    $root.Child = $stack
    return $root
}
```

- [ ] **Step 1.1 — Add function `New-AIEditProposalCard` after `New-AIWorkspaceCodeBlock` (~line 1611)**

- [ ] **Step 1.2 — Run regression tests to ensure no syntax errors**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 1.3 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "feat(ai-workspace): add New-AIEditProposalCard function"
```

---
### Task 4: Inject Card into Assistant Messages

**Files:**
- Modify: `Modules/AIWorkspace.ps1` — `New-AIMessageItem` (line ~1851-1854)

**Interfaces:**
- Consumes: `$script:AIPendingEdit` (hashtable or $null)
- Consumes: `$MessageIndex` (parameter of New-AIMessageItem)
- Consumes: `New-AIEditProposalCard(Session, MessageIndex)`

**Acceptance:** When `$script:AIPendingEdit` is set and `$MessageIndex` matches, the card appears at the bottom of the assistant message content stack.

---

**Step 1 — Inject card into content stack**

In `New-AIMessageItem`, after the existing content assembly (line ~1851-1854):

```powershell
# Existing code (lines ~1851-1854):
[void]$contentStack.Children.Add($title)
[void]$contentStack.Children.Add($body)
[void]$contentStack.Children.Add($actions)
$contentBorder.Child = $contentStack

# Add after $contentBorder.Child = $contentStack line:
# Inject AI Edit Proposal Card if this message has a pending proposal
if ($Role -eq 'assistant' -and $script:AIPendingEdit -and $script:AIPendingEdit.MessageIndex -eq $MessageIndex) {
    $card = New-AIEditProposalCard -Session $script:AIPendingEdit.Session -MessageIndex $MessageIndex
    if ($null -ne $card) {
        [void]$contentStack.Children.Add($card)
    }
}
```

Place this AFTER `$contentBorder.Child = $contentStack` so the card is inside the content border but at the bottom of the stack, below the actions row.

- [ ] **Step 1.1 — Add card injection code after line ~1854**

**Step 2 — Verify:**

- [ ] **Step 2.1 — Run regression tests**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 2.2 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "feat(ai-workspace): inject AI edit proposal card into assistant messages"
```

---
### Task 5: Update `Finalize-AIAssistantMessage` Flow

**Files:**
- Modify: `Modules/AIWorkspace.ps1` — `Finalize-AIAssistantMessage` (line ~1311-1344)

**Interfaces:**
- Consumes: `$script:AIPendingEdit` (set in Task 2, Step 2.1)
- Consumes: `Render-AIChatBox()` (existing)
- Produces: Card appears on re-render instead of raw aiedit block

**Acceptance:** After successful proposal creation, AIDiffPanel stays closed. The card appears inline in the chat on re-render.

---

**Step 1 — Update the success branch**

Current code (lines ~1311-1344):
```powershell
} else {
    Render-AIChatBox
    if ($script:AIRequestMode -eq 'Build' -and $script:AIEditTargetPath) {
        if ($last.Content -match '(?m)^[ \t]*```aiedit:[^\r\n]+') {
            $parsed = ConvertFrom-AIFileEditProposal -AssistantText $last.Content
            ...existing validation...
            } else {
                $created = New-AIFileEditSession ... -OldText ... -NewText ...
                if ($created.Success) {
                    Show-AIDiffProposal -Session $created.Session   # ← REMOVE THIS
                }
            }
        }
    }
}
```

Change to:
```powershell
$created = New-AIFileEditSession ... -OldText ... -NewText ...
if ($created.Success) {
    $script:AIPendingEdit = @{
        Session = $created.Session
        MessageIndex = $script:AIChatHistory.Count - 1
    }
    Render-AIChatBox   # re-render to show card (aiedit stripped) instead of raw block
}
```

- [ ] **Step 1.1 — Replace `Show-AIDiffProposal` call with `$script:AIPendingEdit` assignment + `Render-AIChatBox`**

**Step 2 — Verify:**

- [ ] **Step 2.1 — Run regression tests**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 2.2 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "feat(ai-workspace): replace auto diff panel with inline card in finalize flow"
```

---
### Task 6: Update Apply/Discard Handlers for Card Cleanup

**Files:**
- Modify: `Modules/AIWorkspace.ps1` — existing button handlers

**Interfaces:**
- Consumes: `Hide-AIDiffProposal()` (already resets `$script:AIPendingEdit`)
- Consumes: `Render-AIChatBox()` (re-render to remove card)

**Acceptance:** When user clicks Terapkan from card, edit is applied, card disappears, rollback button appears. When user clicks Batalkan from card, session is cleaned up, card disappears, no system message added.

---

**Step 1 — Update `BtnAIDiscardEdit` handler (existing panel button)**

Current code (lines ~995-1000):
```powershell
$BtnAIDiscardEdit.Add_Click({
    try {
        Hide-AIDiffProposal
        Add-AIChatMessage -Role 'system' -Content 'Perubahan dibatalkan, file tidak diubah.'
    } catch { Write-CrashLog $_ }
})
```

Add `Render-AIChatBox` after `Hide-AIDiffProposal` to remove the card from visual tree:
```powershell
$BtnAIDiscardEdit.Add_Click({
    try {
        Hide-AIDiffProposal
        Render-AIChatBox
        Add-AIChatMessage -Role 'system' -Content 'Perubahan dibatalkan, file tidak diubah.'
    } catch { Write-CrashLog $_ }
})
```

- [ ] **Step 1.1 — Add `Render-AIChatBox` after `Hide-AIDiffProposal` in BtnAIDiscardEdit handler**

**Step 2 — Update `BtnAIApplyEdit` handler (existing panel button)**

Current code (line ~1022) already calls `Hide-AIDiffProposal` which resets `$script:AIPendingEdit = $null`. But the card remains in the visual tree until re-render. The panel apply button is also called from the card's apply button (in Task 3). The card's apply handler already calls `Hide-AIDiffProposal` then `Render-AIChatBox`.

The panel apply button currently does NOT call `Render-AIChatBox`. However, since the card handles its own apply with re-render, and the panel is hidden by `Hide-AIDiffProposal`, this is fine. But for consistency, add `Render-AIChatBox` to the panel apply success path too.

- [ ] **Step 2.1 — Add `Render-AIChatBox` to BtnAIApplyEdit success path (after line ~1022)**

```powershell
# After line ~1022 (after Hide-AIDiffProposal in success branch):
if ($result.Success) {
    ...
    Hide-AIDiffProposal
    Render-AIChatBox   # add this
} else {
    ...
}
```

**Step 3 — Verify the card's cleanup on conversation transitions**

The existing calls to `Hide-AIDiffProposal` in:
- New conversation button (line 654)
- Open conversation (line 718)
- Send-AIChatMessage (line 1137)

Already reset `$script:AIPendingEdit = $null`, which means on re-render the card won't be injected. This is correct — when user starts new conversation or opens a different one, pending proposal should be cleared.

- [ ] **Step 3.1 — Confirm Hide-AIDiffProposal at transition points already handles card cleanup**

**Step 4 — Verify:**

- [ ] **Step 4.1 — Run regression tests**

```powershell
cd C:\Users\Syand\3D Objects\Nexus
.\RunTests.ps1 -Category AIWorkspace
# Expected: 15/15 PASS
```

- [ ] **Step 4.2 — Commit**

```bash
git add Modules/AIWorkspace.ps1
git commit -m "fix(ai-workspace): add re-render to apply handler for card cleanup"
```
