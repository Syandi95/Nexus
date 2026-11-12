# Action Control Classification & Decision Evidence

Documented evidence and classification of compound action controls and selectors in the Nexus application codebase for Stage 4 of the Nexus Design System v1.

---

## 1. Candidate Action Control Inventory & Evidence

| Candidate / Interaction Site | Location / XAML Source | Directly Executable Default Action? | Same-Family Alternatives? | Decision Classification | Custom SplitButton Needed? |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Attachment Source Selector (`BtnAIAddAttachment`)** | `src/03_App/Nexus.App/Views/ConversationView.xaml` (L444–L453) | **No** (Click opens attachment source menu) | Yes (`AttachFileCommand`, `AttachFolderCommand`, `AttachImageCommand`) | **MenuButton** (Native `Button` triggering `ContextMenu` placement) | **No** |
| **Conversation Item Context Options** | `src/03_App/Nexus.App/Views/ConversationView.xaml` (L150–L159) | **No** (Secondary click opens contextual operations) | Yes (`RenameCommand`, `DeleteCommand`) | **ContextMenu** (Native WPF `ContextMenu`) | **No** |
| **Sidebar Navigation Toggle (`BtnAIToggleSidebar`)** | `src/03_App/Nexus.App/Views/ConversationView.xaml` (L241) & `MainWindow.xaml` (L58) | **Yes** (`ToggleSidebarCommand`) | No | **Button** (Native `Button` with `NexusIconButtonStyle`) | **No** |
| **AI Model Selection** | `src/03_App/Nexus.App/Views/ConversationView.xaml` (L580–L601) | **No** (Selection choice from dynamic list) | Yes (Available AI models) | **ComboBox** (Native `ComboBox` with `NexusComboBoxStyle`) | **No** |
| **Image Subsystem Background / Quality Selectors** | `src/03_App/Nexus.App/Views/ImageSubsystemView.xaml` (L316, L360) | **No** (Mode/Quality selection choice) | Yes (`Checkerboard`, `White`, `Black` / `Lite`, `Balanced`, `High Quality`) | **ComboBox** / **Segmented Control** | **No** |
| **Clean Downloads Filter / Sort** | `src/03_App/Nexus.App/Views/CleanDownloadsView.xaml` (L177–L182) | **No** (Sort criteria selection) | Yes (`Terbaru`, `Terlama`, `Terbesar`, `Terkecil`, `Nama A-Z`, `Nama Z-A`) | **ComboBox** | **No** |
| **PDF Utilities Naming / Export Mode** | `src/03_App/Nexus.App/Views/PdfUtilitiesView.xaml` (L269, L352) | **No** (Format & naming scheme choice) | Yes (`JPEG`, `PNG` / Naming format options) | **ComboBox** | **No** |

---

## 2. Decision Summary & Action Control Classification

1. **Button**: Standard single-action triggers (e.g. `BtnAIToggleSidebar`, `BtnAIAddAttachment`, submit/action buttons) use native `Button` styled with canonical button variants (`NexusPrimaryButtonStyle`, `NexusSecondaryButtonStyle`, `NexusSubtleButtonStyle`, `NexusIconButtonStyle`).
2. **MenuButton**: Actions requiring a primary button to launch a dropdown list of options without an independent default execution split (e.g. `BtnAIAddAttachment`) are implemented using native `Button` + `ContextMenu` placement (`ContextMenu.IsOpen = true`).
3. **ComboBox**: Single-selection choices from a drop-down list (e.g. AI Model selection, Image background mode, PDF export options) use canonical native `ComboBox` (`NexusComboBoxStyle`).
4. **Segmented Control**: Frequency-critical mode switches with 2–5 exclusive options use native `ListBox` specialized via `NexusSegmentedControlStyle` & `SegmentedSelectionBehavior.cs`.
5. **SplitButton (Custom Control)**: **NO APPROVED NEED FOUND**. There is no compound control in production that requires a dual-hit-target split button (where the main button triggers an immediate default action while a separate arrow button opens a menu).

---

## 3. Keyboard, Focus, Accessibility & Lifecycle Review

- **MenuButton**: Uses standard `Button` keyboard activation (`Space` / `Enter` to open menu). Focus is retained on the triggering button when closed via `Escape`. `AutomationProperties.Name` is assigned on the parent `Button`.
- **ContextMenu**: Keyboard navigation (`Up`/`Down` arrow keys, `Enter` to execute, `Escape` to close and restore focus to placement target).
- **ComboBox**: Standard WPF ComboBox keyboard contract (`Alt+Down` or `Space` to open, `Up`/`Down` to navigate, `Enter` to select, `Escape` to dismiss).
- **Segmented Control**: Arrow key traversal (`Left`/`Right`/`Up`/`Down`) wraps around items; `SelectionMode = Single`.

---

## 4. Contract Status & Gatekeeper Determination

> **Status:** `NexusSplitButtonStyle: APPROVED CONTRACT, IMPLEMENTATION ON DEMAND`
>
> **Gate Result:** **NO-OP**. Based on empirical source evidence, no production view currently requires a custom `SplitButton` or `MenuButton` control class. Canonical native WPF controls (`Button`, `ComboBox`, `ContextMenu`, and `ListBox` segmented specialization) satisfy 100% of interaction requirements. No custom component will be created for catalog completeness.
