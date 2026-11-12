# MessageBox Usage Classification & Migration Inventory

Documented inventory and classification of all `MessageBox.Show` usages in the Nexus application repository for Task 10.2 of the Nexus Design System v1.

---

## 1. Classification Policy

All usages of `MessageBox.Show` across the codebase are classified into one of two categories:

1. **Blocking Decision**: Essential unhandled runtime/diagnostic error alerts that must remain native OS blocking modal dialogs for application stability and crash diagnostics.
2. **Migration Candidate**: User-facing application workflows (confirmations, non-fatal validation alerts, progress notifications) that will be migrated to canonical Nexus overlay/dialog contracts (`NexusDialogStyle`, `NexusPageBannerStyle`, `NexusInlineMessageStyle`) during module migrations.

---

## 2. Inventory & Classification Evidence

| Location / File | Line(s) | Description / Trigger | Classification | Proposed Canonical Replacement Contract |
| :--- | :--- | :--- | :--- | :--- |
| `src/03_App/Nexus.App/App.xaml.cs` | L288 | Unhandled exception crash diagnostic alert | **Blocking Decision** | Native OS Blocking Dialog (Preserved) |
| `src/03_App/Nexus.App/ViewModels/CleanDownloadsViewModel.cs` | L295, L522, L549, L568, L595 | Folder scan & file operation error alerts | **Migration Candidate** | `NexusInlineMessageStyle` / `NexusDialogStyle` |
| `src/03_App/Nexus.App/ViewModels/CleanDownloadsViewModel.cs` | L470, L516, L544, L590 | Operation completion & status info alerts | **Migration Candidate** | `NexusPageBannerStyle` / `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/CleanDownloadsViewModel.cs` | L477, L533 | Process execution & Undo confirmation dialogs | **Migration Candidate** | `NexusDialogStyle` (Modal Container) |
| `src/03_App/Nexus.App/ViewModels/PdfUtilitiesViewModel.cs` | L523, L576 | DPI & Page range validation warning alerts | **Migration Candidate** | `NexusValidationMessageStyle` / `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/PdfUtilitiesViewModel.cs` | L570 | Page count loading status alert | **Migration Candidate** | `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/PdfUtilitiesViewModel.cs` | L780 | Folder opening error alert | **Migration Candidate** | `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/RapikanFileViewModel.cs` | L297, L372, L379 | Target folder selection & Undo availability warnings | **Migration Candidate** | `NexusValidationMessageStyle` / `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/RapikanFileViewModel.cs` | L308, L383 | File organization & Undo execution confirmation dialogs | **Migration Candidate** | `NexusDialogStyle` (Modal Container) |
| `src/03_App/Nexus.App/ViewModels/RapikanFileViewModel.cs` | L397, L445, L450 | Log availability info & error alerts | **Migration Candidate** | `NexusInlineMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs` | L218 | Empty downloads folder warning alert | **Migration Candidate** | `NexusValidationMessageStyle` |
| `src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs` | L224, L253 | Save changes & Reset to default confirmation dialogs | **Migration Candidate** | `NexusDialogStyle` (Modal Container) |
| `src/03_App/Nexus.App/ViewModels/SettingsViewModel.cs` | L243, L247, L270, L274 | Save & Reset success/error notification alerts | **Migration Candidate** | `NexusPageBannerStyle` / `NexusInlineMessageStyle` |

---

## 3. Summary

- **Total Usages Inventoried**: 36 `MessageBox.Show` invocations.
- **Blocking Decisions**: 1 usage (`App.xaml.cs` L288 — unhandled exception handler).
- **Migration Candidates**: 35 usages across application view-models (`CleanDownloads`, `PdfUtilities`, `RapikanFile`, `Settings`).
- **Governance**: Usages identified as Migration Candidates will be systematically replaced with canonical Nexus dialog and feedback contracts during their respective module migration tasks.
