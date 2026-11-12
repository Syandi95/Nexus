# AI Startup and Technical Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start and stop the existing llama-server with the WPF application and keep technical status visible above the composer.

**Architecture:** `App` owns lifecycle orchestration using existing services and resolvers. `LlamaServerManager` keeps process ownership and enforces a finite health deadline. No service or request-pipeline abstraction is added.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit, Moq, llama.cpp HTTP server

## Global Constraints

- Modify only startup lifecycle, its manager timeout, and technical-status layout.
- Do not modify Fresh Chat, payload, streaming, transport, or token sanitizer.
- Do not add a service or alter unrelated modules.

---

### Task 1: Startup lifecycle tests

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs`
- Modify: `src/03_App/Nexus.App/AssemblyInfo.cs`

- [ ] Add a success test verifying initialize on port 8080 and start with `auto`, 32768.
- [ ] Add a failure test verifying cleanup and a false result.
- [ ] Run focused tests and confirm RED because App lifecycle helper does not exist.

### Task 2: Startup lifecycle implementation

**Files:**
- Modify: `src/03_App/Nexus.App/App.xaml.cs`
- Modify: `src/02_Modules/Nexus.AI.Llama/Services/LlamaServerManager.cs`

- [ ] Register concrete manager/client instances behind their existing interfaces.
- [ ] Add an internal lifecycle helper in `App` using only `ILlamaServerManager`.
- [ ] Await startup before showing the main window; set exact failure status and still show the window.
- [ ] Stop the server explicitly during `OnExit`.
- [ ] Replace retry count with a finite 60-second health deadline and detect early process exit.
- [ ] Run startup tests and confirm GREEN.

### Task 3: Technical status layout test and implementation

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`
- Modify: `src/03_App/Nexus.App/Views/ConversationView.xaml`

- [ ] Add a structural XAML test requiring the ApplicationStatus TextBlock inside `AIInputBorder`.
- [ ] Run it and confirm RED against the current chat-panel placement.
- [ ] Move the existing TextBlock into the first composer row and shift existing composer rows down.
- [ ] Run the focused test and confirm GREEN.

### Task 4: Verification

- [ ] Verify frozen hashes for `LlamaClientService` and Fresh Chat request assembly.
- [ ] Run all test projects serially and build the solution.
- [ ] Launch the application and prove process, port, `/health`, and `Halo` response.
- [ ] Stop the owned server, submit a prompt, and visually verify `Model AI tidak tersedia.` above the composer.
