# Model Management Lite v1 Implementation Plan

> **Execution:** Use `superpowers:executing-plans` and `superpowers:test-driven-development`. Git is disabled for this task. Every production behavior must be preceded by a focused failing test.

**Goal:** Replace the hardcoded AI Workspace model resolver and dropdown state with the frozen Model Management Lite v1 catalog contract.

**Architecture:** A catalog service in `Nexus.AI.Llama` loads `model-index.json`, validates each referenced `model.json` independently, resolves real GGUF files, and publishes one immutable session snapshot shared by App startup and `ConversationViewModel`. Structural index errors fail catalog loading; inactive model errors degrade only that item; an invalid active model blocks server startup without fallback.

**Tech Stack:** .NET 8, C#, System.Text.Json, WPF/MVVM, xUnit, Moq.

## Global Constraints

- `model-index.json` is the only source of truth for model order and `activeModelId`.
- `model.json` is the source of truth for model ID, display name, and GGUF file.
- `ConfigService` receives no model fields.
- Unknown JSON fields are ignored and preserved when `activeModelId` is atomically updated.
- No `enabled` field.
- No automatic fallback.
- No folder scanning, watcher, polling, hot reload, download, add/remove UI, or runtime model switching.
- Runtime checks `File.Exists(resolvedGgufPath)` before starting `llama-server`.
- Context remains the existing global startup value `8192`.
- All existing local changes are preserved.
- No Git commands or metadata changes.

---

### Task 1: JSON fixtures and catalog contract RED

**Files:**
- Create: `src/02_Modules/Nexus.AI.Llama.Tests/Fixtures/ModelCatalog/Valid/model-index.json`
- Create: `src/02_Modules/Nexus.AI.Llama.Tests/Fixtures/ModelCatalog/Valid/Gemma-4-E2B/model.json`
- Create: `src/02_Modules/Nexus.AI.Llama.Tests/Fixtures/ModelCatalog/Valid/Qwen3-4B/model.json`
- Create: `src/02_Modules/Nexus.AI.Llama.Tests/ModelCatalogServiceTests.cs`
- Modify: `src/02_Modules/Nexus.AI.Llama.Tests/Nexus.AI.Llama.Tests.csproj`

**Required tests:**

- Valid index preserves array order and resolves active Gemma.
- Unknown fields in index and metadata do not invalidate loading.
- Missing/malformed/unsupported index produces the exact catalog error.
- Missing active ID or active ID absent from `models` invalidates the index.
- Duplicate model IDs and unsafe metadata paths invalidate the index.
- Corrupt inactive Qwen metadata leaves Gemma valid and active.
- Corrupt active metadata leaves snapshot available but active model unavailable.
- Missing inactive GGUF disables only that model.
- Missing active GGUF blocks active startup eligibility.
- Metadata ID mismatch disables only that entry.
- GGUF path traversal is rejected at model level.
- Atomic update requires server state `Stopped`.
- Atomic update rejects invalid targets.
- Atomic update changes only `activeModelId`, preserves order and unknown fields, and leaves no temporary file.

**RED command:**

```powershell
dotnet test src\02_Modules\Nexus.AI.Llama.Tests\Nexus.AI.Llama.Tests.csproj --no-restore --filter FullyQualifiedName~ModelCatalogServiceTests
```

Expected: compile failure because the catalog API does not exist.

---

### Task 2: Catalog models, loader, snapshot, and atomic update GREEN

**Files:**
- Create: `src/02_Modules/Nexus.AI.Llama/Models/ModelCatalogModels.cs`
- Create: `src/02_Modules/Nexus.AI.Llama/Services/IModelCatalogService.cs`
- Create: `src/02_Modules/Nexus.AI.Llama/Services/ModelCatalogService.cs`

**Interfaces produced:**

```csharp
public interface IModelCatalogService
{
    ModelCatalogSnapshot? CurrentSnapshot { get; }
    ModelCatalogSnapshot Load();
    Task<ModelCatalogSnapshot> UpdateActiveModelAsync(
        string modelId,
        CancellationToken cancellationToken = default);
}
```

The model contract exposes:

- `ModelCatalogSnapshot.ActiveModelId`
- ordered `ModelCatalogSnapshot.Models`
- `ModelCatalogSnapshot.ActiveModel`
- per-item `Id`, `DisplayName`, `MetadataFile`, `ResolvedGgufPath`
- per-item `Status`, `StatusMessage`, `IsAvailable`, and `IsActive`
- catalog-level exception code and user-facing technical status

**Implementation boundaries:**

- Parse with `JsonDocument`/`JsonNode`; do not add external dependencies.
- Validate required fields while ignoring additional fields.
- Normalize and contain relative paths below the catalog root.
- Load all entries independently after structural index validation.
- Use an in-process semaphore for catalog writes.
- Validate the target model and `ILlamaServerManager.State == Stopped`.
- Write a same-directory temporary JSON file, flush it, and use `File.Replace`.
- Preserve unknown fields by updating the parsed JSON tree rather than reserializing a narrow DTO.

**GREEN command:** same focused test command as Task 1; expected all catalog tests pass.

---

### Task 3: Startup integration RED then GREEN

**Files:**
- Modify: `src/03_App/Nexus.App/App.xaml.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs`

**Required tests:**

- Valid active model supplies its resolved GGUF to `StartServerAsync`.
- Structural catalog failure does not initialize or start the server.
- Invalid active model does not initialize or start the server.
- Missing GGUF never reaches the server manager.
- Existing context `8192`, template `auto`, port `8080`, and lifecycle remain unchanged.

**Implementation:**

- Register one catalog service using the fixed v1 root:
  `C:\Users\Syand\3D Objects\Nexus\Models\AI Workspace`.
- Load the catalog before resolving `MainWindow`, so `ConversationViewModel` sees the same snapshot.
- Always show the application window.
- For catalog-level errors, display the exception’s technical status and stop startup.
- For invalid active model, display `Model aktif tidak dapat digunakan.` and stop startup.
- Start only when the active item is available and its GGUF still passes `File.Exists`.

**Verification:**

```powershell
dotnet test src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --no-restore --filter FullyQualifiedName~AppStartup_
```

---

### Task 4: Dropdown and conversation metadata RED then GREEN

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Required tests:**

- Model order matches the catalog index.
- Active selection comes from catalog `activeModelId`.
- No hardcoded Gemma/Qwen collection or `_selectedModel` state remains.
- Invalid inactive model is present, disabled, and exposes its status.
- Conversation creation records active catalog ID and display name.
- Dropdown cannot select a non-active model in v1.

**Implementation:**

- Add the catalog service as the final optional constructor dependency to preserve existing test construction.
- Expose ordered catalog items and active item as read-only projections.
- Use empty values when no snapshot exists; do not introduce a hardcoded fallback.
- Replace conversation metadata arguments with active catalog ID/display name.
- Bind the ComboBox to catalog item objects, select the active item one-way, show `DisplayName`, disable non-active items, and show model-level error status as tooltip.

**Verification:**

```powershell
dotnet test src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~ModelCatalog|FullyQualifiedName~ModelSelector"
```

---

### Task 5: Remove legacy resolver

**Files:**
- Modify: `src/02_Modules/Nexus.AI.Llama/Services/LlamaClientService.cs`
- Modify: `src/02_Modules/Nexus.AI.Llama.Tests/LlamaClientServiceTests.cs`

**Implementation:**

- Remove `ResolveModelPath()`.
- Remove its resolver-specific test.
- Confirm `LlamaClientService` retains only HTTP streaming, cancellation, and token sanitization responsibilities.

**Verification:**

```powershell
rg -n "ResolveModelPath|Models\\\\AI Workspace\\\\Gemma" src
dotnet test src\02_Modules\Nexus.AI.Llama.Tests\Nexus.AI.Llama.Tests.csproj --no-restore
```

Expected search: no production resolver references.

---

### Task 6: Install frozen catalog data

**Files outside the C# workspace, explicitly authorized by the contract:**
- Create: `C:\Users\Syand\3D Objects\Nexus\Models\AI Workspace\model-index.json`
- Create: `C:\Users\Syand\3D Objects\Nexus\Models\AI Workspace\Gemma-4-E2B\model.json`
- Create: `C:\Users\Syand\3D Objects\Nexus\Models\AI Workspace\Qwen3-4B\model.json`

**Data:**

- Index schema version `1`, active `gemma-4-e2b`, ordered Gemma then Qwen.
- Metadata schema version `1`, ID, display name, and existing GGUF filename only.
- Do not remove or edit legacy `manifest.json`.

**Verification:**

- Parse all three files as JSON.
- Confirm both resolved GGUF paths exist.

---

### Task 7: Runtime and regression verification

**Runtime checks:**

- Start Nexus.
- Confirm lifecycle log contains active catalog GGUF path.
- Confirm `llama-server` is listening on port `8080`.
- Confirm `/health` returns HTTP `200`.
- Confirm `/props` reports Gemma from `Models\AI Workspace`.
- Prompt `Halo` and confirm a non-empty Gemma response.
- Confirm AI Workspace shows the catalog order and no unavailable status.

**Graceful-degradation checks using a temporary test catalog only:**

- Corrupt inactive Qwen metadata and prove Gemma remains start-eligible.
- Corrupt active Gemma metadata and prove startup is blocked without fallback.

**Build and regression:**

```powershell
dotnet build src\Nexus.sln --no-restore
dotnet test src\Nexus.sln --no-build --no-restore --logger "console;verbosity=minimal"
```

Document pre-existing unrelated failures without changing modules outside AI Workspace.

## Self-review

- Contract coverage: index, metadata, graceful degradation, source-of-truth sharing, atomic update, startup, dropdown, resolver removal, and runtime evidence are covered.
- Scope: no backlog feature is included.
- Type consistency: App and ViewModel both consume `IModelCatalogService.CurrentSnapshot`.
- No Git step is present.
- No placeholder step remains.
