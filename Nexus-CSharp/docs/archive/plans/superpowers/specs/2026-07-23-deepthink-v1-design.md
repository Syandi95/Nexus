# DeepThink v1 Design

## Scope

Add one request-scoped DeepThink preference to AI Workspace without changing the Fresh Chat architecture, transport, streaming, token sanitizer, or any non-chat module.

## Contract

- `llama-server` starts with `--reasoning off` and without a reasoning budget.
- DeepThink defaults to OFF for each `ConversationViewModel` instance.
- OFF requests omit `chat_template_kwargs` entirely.
- ON requests include only `chat_template_kwargs.enable_thinking = true`.
- The state is sampled when Send starts, so each request follows the button state selected for that request.
- A pill-shaped `DeepThink` button sits between the Plan/Build selector and model selector. ON uses the Nexus accent color; OFF uses normal toolbar styling.

## Data Flow

`DeepThink button` -> `ConversationViewModel.IsDeepThinkEnabled` -> `ILlamaClientService` request option -> conditional JSON payload -> existing HTTP streaming pipeline.

No new service, router, workflow, tool behavior, persistence contract, or assistant fallback is introduced.

## Verification

- Unit-test exact startup arguments and absence of `--reasoning-budget`.
- Unit-test OFF payload omission and ON payload value.
- Integration-test default/toggle state, request forwarding, and XAML button placement/style contract.
- Run the complete regression suite and a live llama-server smoke test.
