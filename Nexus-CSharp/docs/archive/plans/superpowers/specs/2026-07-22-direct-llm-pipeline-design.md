# Direct LLM Pipeline Design

## Objective

Nexus is an LLM client and must never synthesize an assistant answer. Every normal prompt is sent directly to llama-server and only streamed model output is rendered as an assistant message.

## Online pipeline

`User -> HTTP /v1/chat/completions -> llama-server/Gemma -> SSE stream -> assistant UI`

No intent classification or fallback response provider participates in this path.

## Failure behavior

- Connection failure, timeout, and HTTP failure produce an application status outside the assistant message collection.
- Cancellation ends the active request and produces no assistant reply.
- Unexpected programming exceptions are reported as an application error and are not converted to assistant content.
- An empty assistant placeholder created for streaming is removed when the request fails or is cancelled.

## Removed components

- `IntentDetector`
- `FallbackResponseProvider`
- `GenerateFallbackResponse`
- All Nexus-authored conversational fallback templates and their tests

## Acceptance proof

Deterministic HTTP-handler tests submit `Halo`, `Siapa kamu?`, `Selamat malam`, and `Bagi`, verify that each prompt is serialized into the request body, and verify that displayed chunks are exactly the server-provided SSE content. UI tests verify that an offline request leaves no assistant message and exposes `Model AI tidak tersedia.` as application status.
