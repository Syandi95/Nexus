# Nexus — Gate B / Phase 1 Report

Date: **2026-10-03**. **Gate B PASS** for the configured runtime/model and bounded proof scope below. **STOP before Gate C.** This is a shared-runtime consumer proof, not an autonomous Agent or production tool-calling platform.

## 1. Acceptance and frozen scope

| Requirement | Result / evidence |
|---|---|
| Existing runtime reused | PASS: existing manager, engine, catalog, selection, pipeline, endpoint and shared HttpClient |
| No second/orphan runtime | PASS: one alive PID at active proof stages; no alive server after cleanup |
| Production DI instance identity | PASS: concrete/interface aliases and consumer fields refer to the same ownership/pipeline/client |
| Minimal contracts / in-memory runs | PASS: one bounded call, explicit context, immutable run/model metadata |
| Session isolation | PASS: Chat ActiveConversation and stored detail byte-identical after Agent runs |
| Cancellation isolation | PASS both directions, real transport and deterministic controlled-stream tests |
| Runtime lifetime | Option A: reject inactive runtime; cancel/drain on navigation stop or model switch |
| Native tool capability | PASS on actual b11057 + active Gemma E4B; raw request/response evidence |
| Existing feature compatibility | PASS on final whole solution/live regression |
| Full regression | COMPLETE / **1,075 passed, 0 failed, 0 skipped, no abort** |

No visual polish in Gate B. SHA comparison against the pre-Gate-B archive confirms **40 existing Themes/Views files unchanged**. PDF/Chat/Rapikan/Image UI, resources and design system remain Gate A.1 source. No MAF/Hermes dependency, UI, tools, policy/approval/risk engine, ToolHost, shell authority, new catalog/database or durable Agent store was added.

## 2. Checkpoint and source ownership

HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`; dirty working tree/user staged changes predate this task.

Verified read-only pre-edit archive (968 source/document files + raw Git index + exact staged/unstaged patches + per-file hashes/status):

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-b-before-source-20261002-113356.zip
SHA-256: 4384860b7ff71b9c1138c51a5b491c80cfaf6daf8781dc0756e4bd4bdad6acf2
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-b-before-tracked-generated-20261002-113356.zip
```

Tracked generated outputs (118 files) separated from source. User staged patch remains byte-identical; no commit/stash/reset/clean. Existing source changes relative to this checkpoint are **only App.xaml.cs and ConversationRuntimeWorkflow.cs**. New files listed in §4. Restore only authored paths/new files if reverting; inspect later user edits rather than restoring the entire archive/index blindly.

Post-validation source freeze (976 files, read-only ZIP/source hashes verified; this receipt appended afterward):

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-b-final-source-20261003-084840.zip
SHA-256: 6cf4e37fdc5617e257e133919afb5937b7d8c4552c0103a39e3efd30285ff721
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-b-final-tracked-generated-20261003-084840.zip
```

Execution artifacts remain in docs/gate-b-evidence and are excluded from source freeze; all production/test source is exactly the validated full-regression code. No source edit followed the green full run.

## 3. Existing runtime reused / integration boundary

Production composition root `App.xaml.cs` already owns:

```text
Chat -> ConversationRuntimeWorkflow -> LlamaConversationPipeline
                                         -> shared HttpClient -> localhost:8080
                 -> GenericLlamaEngine -> shared ILlamaServerManager

AI CHECK -> AiCheckJudgeService -> shared LlamaClientService
                                -> shared HttpClient -> same localhost:8080

Agent -> SharedRuntimeAgent -> same ConversationRuntimeWorkflow/pipeline
                          -> same owned model/PID/endpoint
```

ModelCatalogService and ImageApplicationModelSelectionPolicy remain existing authoritative model selection. Agent neither starts nor switches nor stops a server, and does not create a model catalog or HttpClient. The production Chat and AI CHECK transport implementations are unchanged; the raw function-calling probe operates **in tests** on the already-owned DI runtime/HttpClient.

`ConversationRuntimeWorkflow` now has a concrete/interface singleton alias so the Agent and Chat can share that exact lifecycle owner. This is logical separation inside Nexus.App, no new assembly.

### Lifetime policy — Option A

Agent run is allowed only while the current workspace runtime is ready. A single bounded additional-consumer lease captures immutable catalog/model/PID/endpoint identity. Lease is an in-memory admission/cancellation/completion handle, **not an exclusive model owner or lifetime pin**; Chat remains free to issue requests.

Navigation stop, a different/unhealthy model switch, or non-reuse startup revokes additional-consumer admission, cancels its linked request and waits for its completion before existing engine stop/start. Same healthy model reuse leaves the lease/run alive and preserves PID. Application exit uses the same workflow stop path, so it also drains the consumer before owned-server shutdown. No global LlamaClientService.CancelStreaming call is used by Agent.

No deferred queue or scheduler: second concurrent Agent proof is rejected immediately, while a Chat request may coexist on the existing server. Navigation does not silently keep a model alive for Agent. A future background/pinned lifetime requirement would require a separate reviewed policy.

## 4. Agent contracts added / files changed

New production files:

```text
src/03_App/Nexus.App/Services/Agent/AgentContracts.cs
src/03_App/Nexus.App/Services/Agent/SharedRuntimeAgent.cs
src/03_App/Nexus.App/Services/RuntimeConsumerLease.cs
```

Contracts:

- `INexusAgent.ExecuteAsync(AgentRequest, CancellationToken)`.
- AgentRequest: SessionId, Prompt, explicit optional RelevantContext, MaxOutputTokens, Timeout.
- AgentRunContext: new RunId, supplied SessionId, UTC start/deadline, used output-token limit, immutable AgentModelSnapshot.
- AgentModelSnapshot: selected ID/path, owned PID, endpoint, in-process runtime identity.
- AgentResult: outcome, text, finish reason, deterministic error category, elapsed duration; rejected/inactive run never starts a model.

Actual execution bounds: **one model call, no retries or loop**, one additional Agent run at a time, 8,192 combined input characters, 16,384 result characters, 1–256 requested output tokens (default64), timeout default60 s/max120 s. Caller, deadline and runtime-revocation cancellation are linked per run. SSE must produce content and finish_reason `stop`; length/incomplete/malformed/identity-change failures do not become successful results.

No access/dependency on ConversationManager in Agent implementation. Relevant context is supplied by value; no implicit current history or memory loading. Logger records RunId/SessionId/model/PID/start/end/duration/outcome/error metadata; prompt/context/answer are not logged by the Agent proof.

Existing production files changed: `App.xaml.cs` (shared workflow alias, Agent registration, coordinated exit stop); `Services/ConversationRuntimeWorkflow.cs` (admission/revocation/drain around its existing lifecycle). LlamaServerManager, GenericLlamaEngine, ModelCatalogService, selection policy, transport helper/client/pipeline, persistence and ViewModels are otherwise reused unchanged.

New tests:

```text
src/03_App/Nexus.Integration.Tests/GateBToolCapabilityTests.cs
src/03_App/Nexus.Integration.Tests/SharedRuntimeAgentTests.cs
src/03_App/Nexus.Integration.Tests/GateBSharedRuntimeLiveTests.cs
```

Evidence/documents: GATE_B_REPORT.md and `docs/gate-b-evidence/`. JSON/raw protocol files are diagnostic artifacts, **not Agent persistence**. Test Chat config/history roots are isolated fixtures reusing existing storage, not new production stores. No csproj/package changes.

## 5. Runtime PID / instance evidence

Initial capability run: `41ce3c65e9a94c69a6657e815c773cbc/identity.json`:

- PID **20432**, state Ready.
- Model ID `gemma-4-e4b`, path `D:/NEXUS/Models/AI Workspace/Gemma-4-E4B/gemma-4-E4B-it-Q4_K_M.gguf`.
- Endpoint `http://127.0.0.1:8080/v1/chat/completions`.
- Runtime identity **39785641**, engine identity45523402, HttpClient identity35287174 (RuntimeHelpers.GetHashCode, process-local, not a durable/global ID).
- Resolved executable directory b11057; executable SHA-256 **C72F76994F8D680D7B423FF68662DE33857D2A2B001465DE55952873D4EF3DF3**.
- Raw response system fingerprint **b11057-59657a613**.

Final consumer run (`consumer-af7283063c8d4fff8f0b94a7e321694a/consumer-evidence.json`) used PID **19412**, runtime identity **16878532** for Agent-alone, coexistence and both cancellation directions. Navigation stopped it; back/start used PID7020; coordinated model switch cancelled the run on that model and loaded Qwen PID16116, then restored original Gemma. Every active stage asserts exactly one alive PID and matching ownership; exit/cleanup asserts OwnedPid null and no alive process. Sequential process IDs are lifecycle transitions, not simultaneous duplicate runtimes.

Production-DI assertions check Agent `_runtime` is the concrete/interface workflow instance; Agent `_server` is shared manager; engine `_serverManager` same; both client/pipeline HttpClient fields same; AI CHECK `_llamaClient` same production LlamaClient alias. Full existing live AI CHECK test also verifies its Judge HTTP call on the same current PID/model.

## 6. Session / cancellation / lifecycle results

| Test | Result |
|---|---|
| Inactive run | rejected, zero inference requests, no server start |
| Same-model reuse during Agent | Agent stays active, no engine stop/start, PID preserved |
| Second Agent run while busy | rejected; no hidden queue/concurrency expansion |
| Cancel Agent | CallerCancelled, queued/concurrent Chat completes, PID remains Ready |
| Cancel Chat | Chat token throws cancellation, Agent completes; no shared/global cancellation |
| Agent sessions | distinct RunIds and supplied SessionIds; no retained context transferred to next run |
| Chat session isolation | same serialized ActiveConversation and byte-identical stored detail after Agent runs |
| Navigation stop | WorkspaceRuntimeDeactivated; cancel/drain then owned model stop; no alive PID |
| Model switch during Agent | run cancelled against old immutable snapshot; switch proceeds after drain; new run uses new current model |
| Deadline/failure/oversize | deterministic category, lease released, next valid run succeeds |
| App startup/exit | standalone compiled Nexus window responds, native close exits, no leftover process |

Real cancellation proof uses at most one Agent and one Chat request, not high concurrency. Single-slot server scheduling may queue one request; isolation is independent request tokens and outcomes, not a claim both infer simultaneously.

## 7. Native tool-call capability and raw protocol

**Supported on the active Gemma E4B / b11057 combination tested.** No model/server change was made to obtain support. No native tool claim is made for Qwen or arbitrary models; Qwen appears only in lifecycle-switch proof.

Single declaration: `get_test_value`, object JSON args `{ "key": "gate-b" }`, enum whitelist, no extra properties. Automatic tool choice (not forced required choice) produces native `message.tool_calls`, type=function, function name, JSON argument string, nonempty id, finish_reason=tool_calls. Dummy evaluator is a fixed test string `{ "value":42 }` only; it performs no filesystem/process/theme/PDF/image action.

Captured sequence:

1. Single tool declaration + user request.
2. Native tool call with valid JSON and tool_call_id.
3. Assistant tool_calls message preserved + role=tool with matching tool_call_id and fixed result.
4. Second model response containing42, with no further tool call.
5. Streamed declaration yields **9 native delta fragments** (index0), reassembled id/name/arguments, `[DONE]`, then a matching tool-result follow-up/second model response.

Non-stream round trip repeated twice per probe. Initial, targeted and both full-run probes succeeded. Final probe directory **12731a0ae9eb4c9fb6228fe45dd494af**, PID19528, errorCategory null/cleanup true; includes:

```text
identity.json / props.response.json / result.json
nonstream-{1,2}.request.json / .response.txt / .http.json
followup-{1,2}.request.json / .response.txt / .http.json
stream.request.json / stream.response.txt / stream.http.json
stream-followup.request.json / .response.txt / .http.json
```

Malformed JSON, array arguments, wrong enum and additional args are rejected. Plain-text `get_test_value(...)` response is explicitly **NativeToolCallingUnsupported**, not parsed as a fallback/hack. Probe errors preserve raw response/status and propagate failure; stop rule remains applicable if future active-model probe fails. Argument/fragment parsing here is diagnostic test-only, not a production tool protocol executor or registry.

## 8. Performance observation

Simple final consumer measurements, same short user prompt, temperature0, max64 output tokens:

| Scenario | Observed elapsed |
|---|---:|
| Chat path without active Agent run | **1,207 ms** |
| Agent proof alone | **1,572 ms** |
| Chat with one Agent run existing | **1,505 ms** (elapsed covers starting Agent, Chat completion and awaiting Agent) |

Source: final consumer JSON. Agent/Chat system prompts differ; cache/native scheduling/warm-up and very short output affect samples. These are **single exploratory observations**, not normalized before/after benchmarking, P95/SLA or proof of no contention under heavy load. Server tuning still `-np 1`; no scheduling/tuning/concurrency change introduced. The runtime consumer object exists in DI even in the first row but has no active lease/request.

## 9. Tests / full regression / failure inventory

Targeted:

- Existing runtime + architecture boundary: 14 passed.
- New controlled-stream/session/limits/protocol cases + actual consumer/probe: **15 passed**, 0 skipped with opt-in.
- AI.Llama project: **174 passed**.
- Conversation project: **16 passed**.
- Integration runtime/Chat/AI CHECK/architecture/navigation regression selection: **198 passed**.

Final restore PASS; entire solution build PASS, 0 errors, 2 existing CS0067 emissions on incremental build, 13.85 s. Earlier compilation emitted existing warnings; no warning-debt cleanup claimed.

Final whole `src/Nexus.sln` run, all11 test projects and all live/visual opt-ins enabled: **COMPLETE/PASS 1,075 passed, 0 failed, 0 skipped, no abort**, wall **560.3891039 s**. Integration **618 passed**. Raw TRX: `docs/gate-b-evidence/full-regression-final/`. Existing Chat, AI CHECK, Image, PDF, history and theme/UI live/regression facts executed; UI resources/source were not changed.

Initial full run is retained: COMPLETE 1,074 pass/1 fail, 788.194 s. The existing Gate A live model-restore test failed after **HEALTH_CHECK_TIMEOUT at60 s**; cleanup removed owned process, then a fixture-cleanup startup succeeded. Lifecycle trace: `docs/gate-a-results/runtime-20261002-135034/runtime.json`. A separate existing-live recheck and final full rerun passed **with identical code/timeout/assertions**. No timeout increase, model replacement, blind retry or test weakening was applied. This startup variability remains a known observation, not silently erased or declared permanently fixed.

Reproduction environment (same explicit model/image/PDF/visual fixtures as Gate A.1, plus):

```powershell
$env:NEXUS_GATE_B_PROBE='1'
$env:NEXUS_GATE_B_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-b-evidence'
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore -m:1 --logger trx --results-directory "docs\gate-b-evidence\full-regression-final" --blame-hang-timeout 6m --blame-hang-dump-type none --verbosity minimal
```

Model/engine roots must be valid; evidence directory exists before fixture creates per-run subfolders. Native Gate B facts skip explicitly without probe opt-in; final acceptance set it and executed them. Capability raw data contains synthetic prompt/result only. Agent logs no prompt/context/answer payload. No new production persistence.

## 10. Known limitations / recommendation for Gate C / stop

- Gate B consumer supports one reasoning call only and has no production tool execution, Agent loop/planner/UI/memory/skills/recovery store.
- Option A cancels Agent on workspace deactivation; no background pin or durability/resume guarantee.
- Coordination applies to production workflow stop/switch/exit; calling the low-level manager directly bypasses it. Existing app consumers should continue using their owned workflow; a broader runtime-admission redesign is not claimed.
- Deprecated global client CancelStreaming remains in compatibility code. Agent uses pipeline/request tokens and never calls it; arbitrary external legacy consumer calls are outside cancellation-isolation proof.
- Native tool support is verified for this active model/build/protocol fixture, not a general model certification. Production transport still maps ordinary text/reasoning; future Gate C protocol extension must preserve Chat/Judge semantics and use validated typed calls.
- Limited contention samples and one cold startup failure remain documented. No performance exception/threshold adjustment newly granted here; Gate A benchmark policy unchanged.

Recommendation: after review, Gate C may begin with a small read-only allowlisted registry/policy/input validation/result bounds on the verified shared runtime, retaining side-effect-free initial capabilities. No MAF dependency or shell/write authority is justified by this proof.

**STOP after Gate B. Gate C is not started or automatically authorized.**
