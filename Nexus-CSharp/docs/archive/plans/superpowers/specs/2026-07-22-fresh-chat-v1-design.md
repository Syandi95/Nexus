# Fresh Chat v1 Design

## Objective

AI Workspace provides one capability: the user chats with the configured LLM. A new conversation starts fresh, while later turns retain only the `user` and `assistant` messages stored in that same active conversation.

## Request contract

Every request contains exactly one short system message followed by active-conversation history and the current user prompt.

- Plan: `You are in Plan mode. Focus on analysis, reasoning, and planning. Do not invent actions.`
- Build: `You are in Build mode. Focus on implementation and technical solutions.`
- Persisted system messages and roles other than `user` or `assistant` are not forwarded.
- No message or content is read from another conversation.

## Attachment contract

Attachment controls and chips remain visible, but attachments do not enable Send, alter the prompt, or contribute paths or contents to the request. Attachment processing is reserved for v2.

## Preserved behavior

Streaming, the active-conversation sidebar, active-conversation persistence, model selector, Plan/Build selector, Settings UI, technical error status, and token sanitizer remain unchanged.

## Forbidden automatic request content

No AIContext, Nexus/project context, knowledge, attachment data, file paths, folder scans, persona, intent routing, tools, agents, workflow instructions, long system prompt, or history from another conversation.

## Verification

Tests capture request messages for fresh Plan, fresh Build, active history, and attached-file scenarios. An end-to-end test captures the JSON body emitted by `LlamaClientService` so the actual llama-server payload can be reported.
