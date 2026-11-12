# Conversation UX Refinement v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move and refine the Conversation panel, add local-time grouping plus safe rename/delete behavior, and remove the large chat wrapper without changing chat execution.

**Architecture:** Keep the existing Conversation storage, index, manager, ViewModel, and WPF view. Add timestamp-preserving save/rename behavior to the current contracts, expose an `ICollectionView` over the existing flat list, and use existing view code-behind only for UI confirmation, focus, and key events.

**Tech Stack:** .NET 8, C#, WPF, `ICollectionView`, xUnit, Moq, Windows Recycle Bin through `Microsoft.VisualBasic.FileIO`.

## Global Constraints

- Modify only `Nexus.Conversation`, Conversation tests, `ConversationViewModel`, `ConversationView`, and their integration tests.
- Do not change AI Engine, DeepThink, streaming, chat pipeline, message rendering, or composer code.
- Keep stored timestamps UTC; local conversion is presentation-only.
- Rename must preserve `UpdatedAt`; normal message persistence must continue updating it.
- Never fall back to permanent deletion.
- The workspace is not a normal tracked repository root, so do not create commits from this plan.

---

### Task 1: Timestamp-preserving persistence and recycle-only deletion

**Files:**
- Modify: `src/02_Modules/Nexus.Conversation/Storage/ConversationStorageService.cs`
- Modify: `src/02_Modules/Nexus.Conversation.Tests/StorageServiceTests.cs`

**Interfaces:**
- Preserves: `Task SaveAsync(ConversationDetailRecord detail)` for normal message persistence.
- Produces: `Task SaveAsync(ConversationDetailRecord detail, bool updateTimestamp)` for explicit timestamp-preserving writes.
- Produces: protected virtual `SendToRecycleBin(string path)` test seam.

- [ ] Write a failing test proving `SaveAsync(record, updateTimestamp: false)` preserves the exact UTC `UpdatedAt` value.
- [ ] Run the storage test and confirm compilation/behavior fails because the option does not exist.
- [ ] Keep the existing overload as the timestamp-updating path and add an explicit boolean overload that updates `UpdatedAt` only when requested.
- [ ] Write a failing recycle-error test using a derived storage service whose `SendToRecycleBin` throws, then assert the exception propagates and the JSON file remains.
- [ ] Replace the catch-plus-`File.Delete` implementation with one virtual recycle call and no permanent-delete fallback.
- [ ] Run `dotnet test src/02_Modules/Nexus.Conversation.Tests/Nexus.Conversation.Tests.csproj --filter StorageServiceTests` and confirm GREEN.

### Task 2: Rename contract that preserves Last Updated

**Files:**
- Modify: `src/02_Modules/Nexus.Conversation/Manager/ConversationManagerService.cs`
- Modify: `src/02_Modules/Nexus.Conversation.Tests/ManagerServiceTests.cs`

**Interfaces:**
- Consumes: `SaveAsync(detail, updateTimestamp: false)`.
- Produces: `Task RenameAsync(string id, string title)` on `IConversationManagerService`.

- [ ] Write failing manager tests proving rename trims/updates the detail and index title while preserving `UpdatedAt`.
- [ ] Add `RenameAsync`; use the active record when IDs match, otherwise load from storage.
- [ ] Save with `updateTimestamp: false`, update only the index title, and persist the index without modifying its timestamp.
- [ ] Adjust `RemoveActiveAsync` so recycle succeeds before active/index state is changed.
- [ ] Run all Conversation tests and confirm GREEN.

### Task 3: Local-time grouping with ICollectionView

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Interfaces:**
- Produces on `ConversationListItemViewModel`: `DateTime UpdatedAtUtc`, `string GroupName`, `string UpdatedAtFormatted`, `bool IsEditingTitle`, and `string EditTitle`.
- Produces on `ConversationViewModel`: `ICollectionView GroupedConversations`, rename methods, and delete refresh method.

- [ ] Write parameterized failing tests for Today, Yesterday, 2 days, 7 days, and 8 days using UTC inputs converted against a fixed local date.
- [ ] Add deterministic grouping/format helpers that convert UTC to local without assigning the converted value back to the record.
- [ ] Write a failing test that loads unsorted index items and proves the `ICollectionView` has fixed group order with descending `UpdatedAtUtc` items.
- [ ] Configure `PropertyGroupDescription` and descending `SortDescription` over the existing observable list.
- [ ] Keep title search operating on the same grouped collection.
- [ ] Write and implement tests for begin/cancel/commit rename state; commit calls `RenameAsync` and leaves the displayed timestamp unchanged.
- [ ] Write and implement deletion refresh tests, including active deletion with no remaining conversation.
- [ ] Run focused integration tests and confirm GREEN.

### Task 4: Right panel, context menu, and inline editing

**Files:**
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml`
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Interfaces:**
- Consumes: `GroupedConversations` and item edit state from Task 3.
- Produces: `Rename`/`Delete` context-menu handlers and Enter/Escape inline edit handling.

- [ ] Add failing structural XAML tests proving the panel is column 1, main workspace is column 0, the list binds `GroupedConversations`, and group headers are enabled.
- [ ] Add failing structural tests for `Rename`/`Delete`, inline TextBox bindings, and key handlers.
- [ ] Swap the two outer columns and reverse the conversation-panel separator edge.
- [ ] Add `GroupStyle` headers and render Today/Yesterday as `HH:mm` plus title, older entries as `dd MMM` plus title.
- [ ] Add the item context menu and inline editor.
- [ ] In code-behind, use `MessageBox` confirmation because no Nexus dialog exists; on recycle failure show an error and retain state.
- [ ] Run integration tests and confirm GREEN.

### Task 5: Remove the large chat wrapper

**Files:**
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Interfaces:**
- Preserves: welcome panel, `AIChatScroll`, `AIChatItems`, message template, and all composer elements.

- [ ] Add a failing structural test asserting `AIChatPanelBorder` is absent and the replacement chat grid has no border/background/corner/max-width attributes.
- [ ] Replace only the outer chat Border with a transparent Grid; preserve its children and row placement.
- [ ] Run integration tests and confirm GREEN.

### Task 6: Regression and runtime verification

**Files:**
- Verify only; no planned production edits.

- [ ] Run `dotnet build src/Nexus.sln --no-restore --verbosity minimal`; require zero errors and zero warnings.
- [ ] Run Conversation tests and integration tests serially.
- [ ] Run `dotnet test src/Nexus.sln --no-restore --no-build --verbosity minimal -m:1`.
- [ ] If the known PDF host crash recurs, verify all six PDF tests individually and document it without modifying PDF.
- [ ] Launch Nexus for a manual smoke check: right panel, grouping, inline Enter/Escape, recycle confirmation, and borderless chat area.
- [ ] Verify source diff/search contains no changes to llama client/server, DeepThink, streaming, message template, or composer sections.
