# DeepThink v1 Implementation Plan

> Execute with test-driven development and verify every claim from fresh command output.

**Goal:** Make DeepThink a real per-request AI Workspace control while keeping reasoning disabled by default at server startup.

**Architecture:** Extend the existing llama client contract with one boolean request option. Keep the current overload as the OFF-compatible path, conditionally serialize the llama.cpp chat-template option, and bind one ViewModel property/command to a pill button.

### Task 1: Lock startup and payload contracts with failing tests

- Modify `src/02_Modules/Nexus.AI.Llama.Tests/LlamaServerManagerTests.cs`.
- Modify `src/02_Modules/Nexus.AI.Llama.Tests/LlamaClientServiceTests.cs`.
- Assert startup includes `--reasoning off`, excludes `--reasoning-budget`, OFF omits thinking fields, and ON sends `enable_thinking: true`.
- Run focused tests and confirm RED.

### Task 2: Implement server and client behavior

- Modify `src/02_Modules/Nexus.AI.Llama/Services/LlamaServerManager.cs`.
- Modify `src/02_Modules/Nexus.AI.Llama/Services/ILlamaClientService.cs`.
- Modify `src/02_Modules/Nexus.AI.Llama/Services/LlamaClientService.cs`.
- Preserve existing overload behavior as DeepThink OFF and conditionally add the ON payload object.
- Run llama module tests and confirm GREEN.

### Task 3: Lock and implement AI Workspace control

- Modify `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs`.
- Modify `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs`.
- Modify `src/03_App/Nexus.App/Views/ConversationView.xaml`.
- Test default OFF, toggle behavior, request-state forwarding, and pill placement.
- Implement the state, command, request snapshot, and XAML button.
- Run integration tests and confirm GREEN.

### Task 4: Regression and live verification

- Build the solution.
- Run the entire regression suite serially.
- Start the normal app/server lifecycle and inspect process arguments, port 8080, and `/health`.
- Capture OFF and ON request payload evidence and verify a `Halo` response.
- Confirm no files outside the approved chat/server-argument paths changed.
