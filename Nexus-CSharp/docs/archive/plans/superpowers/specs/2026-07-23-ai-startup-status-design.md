# AI Startup and Technical Status Design

## Scope

Implement only the llama-server application lifecycle and technical-status visibility. Fresh Chat, payload assembly, streaming, transport, token sanitizer, and unrelated modules remain frozen.

## Startup contract

- `App.OnStartup` resolves the existing `LlamaServerManager` and `LlamaClientService` instances.
- Existing engine/model resolvers provide paths.
- The manager is initialized for port 8080 and starts Gemma using chat template `auto` and context size 32768.
- Startup waits for `/health` with a finite 60-second deadline.
- On failure or timeout, the created process is stopped, the window still opens, and `ConversationViewModel.ApplicationStatus` becomes `Model AI tidak tersedia.`
- `App.OnExit` explicitly calls `StopServerAsync()`.

## Status layout contract

The existing application-status TextBlock moves from the chat panel into the composer as its first row. It remains outside `Messages`, occupies only its content height, and cannot be covered by the composer.

## Verification

Automated tests cover startup success, startup failure cleanup, and status ownership by the composer. Runtime proof requires a live application-owned llama-server, port 8080, successful `/health`, a streamed response for `Halo`, and a visible technical status after deliberately stopping the server.
