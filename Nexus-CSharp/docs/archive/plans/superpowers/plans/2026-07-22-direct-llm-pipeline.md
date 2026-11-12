# Direct LLM Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure Nexus only displays llama-server output as assistant content and reports transport failures as application status.

**Architecture:** `LlamaClientService` becomes a transparent HTTP/SSE client and propagates failures. `ConversationViewModel` owns application status and removes the pending assistant placeholder whenever no model response exists.

**Tech Stack:** C# 12, .NET 8, WPF/MVVM, HttpClient, xUnit, Moq

## Global Constraints

- Remove all intent detection and conversational fallback templates.
- Do not create assistant content for connection, timeout, HTTP, cancellation, or programming failures.
- Preserve direct streaming of successful llama-server chunks.

---

### Task 1: Lock direct HTTP behavior with regression tests

**Files:**
- Modify: `src/02_Modules/Nexus.AI.Llama.Tests/LlamaClientServiceTests.cs`
- Modify: `src/02_Modules/Nexus.AI.Llama.Tests/RegressionTestMatrix.cs`
- Delete: `src/02_Modules/Nexus.AI.Llama.Tests/IntentDetectorAndFallbackTests.cs`

**Interfaces:**
- Consumes: `LlamaClientService.StreamChatCompletionsAsync(string, CancellationToken)`
- Produces: acceptance coverage for direct request serialization, server-originated SSE output, propagated offline failure, and propagated cancellation

- [ ] Add a theory for the four acceptance prompts using a recording HTTP handler and unique SSE response.
- [ ] Change offline coverage to require `HttpRequestException`, not fallback chunks.
- [ ] Add cancellation coverage requiring `OperationCanceledException` and zero assistant chunks.
- [ ] Run `dotnet test src/02_Modules/Nexus.AI.Llama.Tests/Nexus.AI.Llama.Tests.csproj` and confirm the new offline test fails because a fallback is returned.

### Task 2: Remove assistant fallback architecture

**Files:**
- Modify: `src/02_Modules/Nexus.AI.Llama/Services/LlamaClientService.cs`
- Delete: `src/02_Modules/Nexus.AI.Llama/Services/IntentDetector.cs`
- Delete: `src/02_Modules/Nexus.AI.Llama/Services/FallbackResponseProvider.cs`

**Interfaces:**
- Consumes: injected `HttpClient`
- Produces: unchanged streaming API whose failures propagate to the UI layer

- [ ] Remove the catch-all request fallback branch and `GenerateFallbackResponse`.
- [ ] Preserve `EnsureSuccessStatusCode` so HTTP errors propagate.
- [ ] Stop swallowing stream-read exceptions.
- [ ] Run the Llama test project and confirm all direct-pipeline tests pass.

### Task 3: Render failures as application status

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`
- Modify: `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml`

**Interfaces:**
- Produces: `string ApplicationStatus`, `bool HasApplicationStatus`

- [ ] Add integration tests proving offline failure removes the assistant placeholder and sets `Model AI tidak tersedia.`.
- [ ] Add integration coverage proving cancellation removes the assistant placeholder without saving assistant content.
- [ ] Run the focused integration tests and confirm they fail before implementation.
- [ ] Add bindable application-status properties, clear status on a new request, classify expected transport errors, and report unexpected exceptions without assistant content.
- [ ] Add a non-message status TextBlock bound to the new properties.
- [ ] Run focused integration tests and confirm they pass.

### Task 4: Full verification

**Files:**
- Verify all modified and deleted files above.

- [ ] Search source for `IntentDetector`, `FallbackResponseProvider`, `GenerateFallbackResponse`, and known template text; expect no active-code hits.
- [ ] Run `dotnet test src/Nexus.sln` and require zero failures.
- [ ] Run `dotnet build src/Nexus.sln` and require exit code 0.
- [ ] Review the final diff by explicit workspace paths because Git provenance is unsafe in this checkout.
