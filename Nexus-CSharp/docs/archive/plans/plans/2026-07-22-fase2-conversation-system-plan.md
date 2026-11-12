# [Fase 2] Conversation System C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan logika riwayat percakapan (`ConversationStorage.ps1`, `ConversationIndex.ps1`, `ConversationManager.ps1`) ke C# Library `Nexus.Conversation.csproj` dan xUnit test `Nexus.Conversation.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan paritas fitur 100%, 0 warning/error, dan 0 regresi data.

**Architecture:** Menerapkan 3 C# Services terdecouple (`IConversationStorageService`, `IConversationIndexService`, `IConversationManagerService`) yang beroperasi pada C# Record DTOs (`ConversationDetailRecord`, `ConversationIndexRecord`) menggunakan `System.Text.Json` secara *atomic file I/O* (`.tmp` rename pattern).

**Tech Stack:** C# 12 / .NET 8, `System.Text.Json`, xUnit, `Microsoft.VisualBasic` (RecycleBin FileIO).

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: File `Modules/ConversationStorage.ps1`, `ConversationIndex.ps1`, dan `ConversationManager.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA** di folder `Modules/`.
* 📌 **STATUS MODUL POWERSHELL**: Seluruh skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **BEBEKUAN HANYA SETELAH QUALITY GATE**: Pembekuan Fase 2 dilakukan HANYA setelah `dotnet build`, `dotnet test`, dan seluruh Quality Gate lulus 100%.

---

## MIGRATION MATRIX (22 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Get-ConversationTimestamp` | `IConversationStorageService.GetTimestamp()` | `GetTimestamp_ReturnsIsoFormat` | Candidate for Deprecation |
| `Get-ConversationRootPath` | `IConversationStorageService.GetRootPath()` | `GetRootPath_ReturnsValidPath` | Candidate for Deprecation |
| `Initialize-ConversationStorage` | `IConversationStorageService.InitializeStorage()` | `InitializeStorage_CreatesDirectory` | Candidate for Deprecation |
| `Get-ConversationJsonPath` | `IConversationStorageService.GetJsonPath(id)` | `GetJsonPath_ReturnsExactPath` | Candidate for Deprecation |
| `Ensure-ConversationStorageReady`| `IConversationStorageService.EnsureReady(id)` | `EnsureReady_PreparesStorage` | Candidate for Deprecation |
| `New-ConversationObject` | `IConversationStorageService.CreateNew(...)` | `CreateNew_ReturnsDefaultObject` | Candidate for Deprecation |
| `Save-Conversation` | `IConversationStorageService.SaveAsync(detail)`| `SaveAsync_AtomicWriteSuccess` | Candidate for Deprecation |
| `Load-Conversation` | `IConversationStorageService.LoadAsync(id)` | `LoadAsync_ReadsValidDetailJson` | Candidate for Deprecation |
| `Remove-ConversationToRecycleBin` | `IConversationStorageService.DeleteToRecycleBin(id)`| `DeleteToRecycleBin_RemovesFile` | Candidate for Deprecation |
| `Get-ConversationIndexPath` | `IConversationIndexService.GetIndexPath()` | `GetIndexPath_ReturnsValidPath` | Candidate for Deprecation |
| `New-ConversationIndexObject` | `IConversationIndexService.CreateNewIndex()` | `CreateNewIndex_ReturnsSkeleton` | Candidate for Deprecation |
| `ConvertTo-ConversationIndexItem` | `IConversationIndexService.ToIndexItem(detail)` | `ToIndexItem_MapsDetailProperties`| Candidate for Deprecation |
| `Sort-ConversationIndexItems` | `IConversationIndexService.SortItems(items)` | `SortItems_SortsByUpdatedAtDesc` | Candidate for Deprecation |
| `Save-ConversationIndex` | `IConversationIndexService.SaveIndexAsync(index)`| `SaveIndexAsync_AtomicWrite` | Candidate for Deprecation |
| `Rebuild-ConversationIndex` | `IConversationIndexService.RebuildIndexAsync()` | `RebuildIndexAsync_Scans36Files` | Candidate for Deprecation |
| `Get-ConversationIndex` | `IConversationIndexService.GetIndexAsync(rebuild)`| `GetIndexAsync_ReturnsValidIndex` | Candidate for Deprecation |
| `Initialize-ConversationManager` | `IConversationManagerService.InitializeAsync()` | `InitializeAsync_PreparesManager` | Candidate for Deprecation |
| `Get-ActiveConversation` | `IConversationManagerService.ActiveConversation` | `ActiveConversation_StateHandling` | Candidate for Deprecation |
| `New-Conversation` | `IConversationManagerService.NewConversationAsync(...)`| `NewConversation_SetsActive` | Candidate for Deprecation |
| `Open-Conversation` | `IConversationManagerService.OpenConversationAsync(id)`| `OpenConversation_LoadsActive` | Candidate for Deprecation |
| `Add-ConversationMessage` | `IConversationManagerService.AddMessageAsync(...)` | `AddMessage_AppendsToActive` | Candidate for Deprecation |
| `Remove-ActiveConversation` | `IConversationManagerService.RemoveActiveAsync(id)` | `RemoveActive_DeletesAndClears` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.Conversation` & `Nexus.Conversation.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.Conversation/Nexus.Conversation.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.Conversation.Tests/Nexus.Conversation.Tests.csproj`
- Add references: `Nexus.Conversation` -> `Nexus.Foundation` & `Nexus.Contracts`.
- Register both projects to `Nexus.sln`.

### Task 2: Implement `IConversationStorageService` & `ConversationStorageService`
- Implement storage I/O, `.tmp` atomic write pattern, and RecycleBin file deletion in `Nexus.Conversation/Storage/`.

### Task 3: Implement `IConversationIndexService` & `ConversationIndexService`
- Implement index sorting, cache management, and full index rebuild scanner in `Nexus.Conversation/Index/`.

### Task 4: Implement `IConversationManagerService` & `ConversationManagerService`
- Implement in-memory active conversation manager, message append, and auto-title generation in `Nexus.Conversation/Manager/`.

### Task 5: Implement xUnit Unit Tests & Verify Quality Gate
- Write 15+ xUnit unit tests covering storage, index, and manager services in `Nexus.Conversation.Tests`.
- Execute `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 errors and 0 warnings.
