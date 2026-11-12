# Fresh Chat v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send only the selected mode instruction, active conversation history, and current prompt to llama-server while keeping attachments UI-only.

**Architecture:** Keep request assembly inside the existing `ConversationViewModel`; add no service or public API. Keep `LlamaClientService` transport and sanitizer behavior unchanged.

**Tech Stack:** C# 12, .NET 8, WPF/MVVM, xUnit, Moq, HttpClient

## Global Constraints

- Add no architecture or service.
- Change only the active chat path and its tests.
- Preserve streaming, sidebar, persistence, model selector, Plan/Build selector, Settings, attachment UI, and token sanitizer.
- The single short system message may contain only the selected mode rule.
- Never send attachment paths or contents in Fresh Chat v1.

---

### Task 1: Lock Fresh Chat request assembly

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Interfaces:**
- Consumes: `ConversationViewModel.ExecuteSendAsync()` and `ILlamaClientService.StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload>, CancellationToken)`
- Produces: regression coverage for exact Plan/Build payloads, active-only history, and attachment exclusion

- [ ] Add a fresh Plan test expecting exactly `[system Plan, user prompt]`.
- [ ] Add an active Build test expecting `[system Build, active user, active assistant, current user]`.
- [ ] Add an attachment test proving paths and attachment labels never enter any message.
- [ ] Add an attachment-only test proving Send remains disabled and no request is made.
- [ ] Run the four focused tests and confirm failures against current behavior.

### Task 2: Implement minimal Fresh Chat assembly

**Files:**
- Modify: `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`

**Interfaces:**
- Produces: one private mode-instruction selector and the existing payload list

- [ ] Make `HasInputText` depend only on non-whitespace `InputText`.
- [ ] Remove attachment-to-prompt concatenation.
- [ ] Insert exactly one mode system message before filtered active history.
- [ ] Preserve only active `user` and `assistant` history, then append the current user prompt.
- [ ] Run the focused tests and confirm they pass.

### Task 3: Capture actual llama-server JSON

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`

**Interfaces:**
- Consumes: real `LlamaClientService` with a recording `HttpMessageHandler`
- Produces: captured JSON body from the HTTP request boundary

- [ ] Add an end-to-end payload test using the real transport client and active ViewModel request assembly.
- [ ] Output the captured JSON through xUnit test output.
- [ ] Assert exact message count, roles, contents, and absence of attachment path.
- [ ] Run the test with detailed console logging and retain the emitted payload for handoff.

### Task 4: Full regression and build verification

**Files:**
- Verify the modified chat files and tests.

- [ ] Scan active chat source for forbidden context-injection terms.
- [ ] Run all test projects serially and require zero failures.
- [ ] Build `src/Nexus.sln` serially and require zero warnings/errors.
- [ ] Confirm `LlamaClientService` sanitizer source is byte-for-byte unchanged from the pre-task hash.
