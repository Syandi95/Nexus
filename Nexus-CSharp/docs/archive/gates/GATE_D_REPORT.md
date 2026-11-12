# Nexus Gate D Report

Date: **2026-10-03**. **Gate D / Phase 3 PASS** for the bounded read-only orchestration and certified model/runtime below. Final whole-solution certification: **1,194 passed, 0 failed, 0 skipped, no abort**. **Gate E HOLD — STOP after Gate D.**

## 1. Scope

Nexus-native finite Agent orchestration, sequential native tools, deterministic budgeting/retry classification and C# verification on the existing shared runtime. The fast path remains `EnableReadOnlyToolCall=false`: one streaming reasoning request, structural verification, no tools/capability hashing. The enabled path uses explicit known fact requirements, validated native requests and run-local execution facts.

No write, shell, arbitrary process, general filesystem read, persistence/database, memory, planner graph, Agent UI, second model, new runtime/project/assembly, MAF, Hermes runtime or ToolHost. Chat and AI CHECK retain their existing transports/features; server tuning is unchanged.

### Phase summary

| Item | Certified behavior |
|---|---|
| Max model turns | **4**, including failed/retried inference attempts; fast path uses1 |
| Max tool calls | **3** accepted native requests |
| Max retries | **1**, explicitly classified transient HTTP only |
| Max failures | **2**; shared tool/model-attempt failure budget |
| Run timeout | **60 seconds default / 120 seconds hard maximum**, one monotonic run deadline |
| Registered production tools | `nexus_get_model_snapshot`, `nexus_get_runtime_snapshot` |
| Write / shell capability | **NO / NO** |
| Tool concurrency | **1**, sequential |
| Agent concurrency | **1**, existing additional-consumer lease; Chat may coexist |
| Verification authority | **C# / deterministic** |
| Second model/verifier | **NO** |
| New runtime / database | **NO / NO** |
| MAF / ToolHost | **NO / NO** |
| Targeted final | **113 passed**,0 failed/skipped |
| Impacted regression | **561 passed**,0 failed; one opt-in live fact skipped in non-live selection |
| Full regression | **1,194 passed**,0 failed/skipped; all live facts enabled |

## 2. Checkpoint

HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`, branch `audit/nexus-agent-phase0-20261001`. Before editing, verified Gate C source ZIP SHA-256 `35d9ef39c2433ff53f86dea79cea4bab237a1693c556b0857d8d4e7f1403424d`, compared all archived files and verified staged patch equality. Agent/tool source matched Gate C freeze. The report receipt and subsequent user changes in Markdown rendering, Conversation views and related tests were identified and preserved; no blanket archive restoration.

Verified pre-Gate-D source checkpoint: **992 files**, per-file SHA-256, raw Git index, exact staged/unstaged patches and status, read-only ZIP. Tracked generated outputs separated (118 files):

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-d-before-source-20261003-120007.zip
SHA-256: af46b3b715a63ba71bd9d0f76d47ef34f5cfa08d8bd50f1babcb6b46e94bbc89
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-d-before-tracked-generated-20261003-120007.zip
```

Post-validation source comparison confirms user staged patch byte-identical, all40 baseline Themes/Views files unchanged from the Gate D checkpoint, and unchanged original `GetModelSnapshotTool`, registry, argument validator, native parser, runtime workflow/lease, Chat/Judge transport, catalog and persistence. Authored existing changes are listed in §20. No commit/stash/reset/clean. Final source-freeze receipt is appended after archive verification; restore only authored paths after inspecting later user edits.

Verified final read-only source freeze: **1,002 files**, ZIP integrity/per-file SHA-256 verified;118 tracked generated outputs separated:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-d-final-source-20261003-133519.zip
SHA-256: c4f46d487634d74914d4c37cffccc7452ae9067d03fbd3f8631cb237b151946b
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-d-final-tracked-generated-20261003-133519.zip
```

Only this receipt was appended after freeze. All production/test code is exactly the green full-certification code; no code edits followed certification. Diagnostic logs/TRX/live/visual outputs remain in evidence directories and are excluded from source archive.

## 3. Existing Gate C boundaries retained

- Explicit `ToolRegistry` remains the metadata source of truth; no service/assembly scanning or reflection exposure.
- Exact ordinal native names/IDs; closed schema validation, byte/depth limits and typed tool results retained.
- Nexus-owned immutable policy allowlist, default-deny, ReadOnly/Risk checks; model/prompt/result text cannot change permissions.
- Original model snapshot tool remains **2-second timeout / 2,048-byte result-envelope cap**, empty closed arguments; no tool behavior change.
- Native parser still rejects simultaneous multiple calls, duplicate JSON properties/IDs, malformed authority metadata and plain-text pseudo-tool execution.
- Matching assistant native ID / dispatch ID / role=tool ID retained on every follow-up; duplicate IDs across turns additionally rejected.
- Gate B run admission, shared instance ownership, one Agent lease, immutable model snapshot, request-scoped cancellation and Option A cancel/drain remain.
- Fingerprinted **Gemma E4B + b11057** capability required; Unknown deterministically fails without inference/model replacement/download.

## 4. Orchestrator

`AgentOrchestrator` replaces the Gate C one-tool `AgentToolRoundTrip`; the old helper is removed rather than retaining a separate unverified executor. Existing `SharedRuntimeAgent` owns admission/linked cancellation/results; orchestrator owns finite native turns and sequential dispatches.

Flow: capability check once per run → `for(turn < MaxModelTurns)` → model response → native request/validation/policy/tool/result → next turn, or deterministic final verification. There is no model-controlled `while(true)`, autonomous retry loop, scheduler or second model.

Explicit verification profiles are `ModelSnapshot` (default tool path) and `ModelAndRuntime` (two independent sources). Pending fact declarations are selected from Registry descriptors already allowed by Policy. Native `tool_choice=required` is used while required facts are missing; after facts are obtained, final turn has no tool declarations, `tool_choice=none`, and a native JSON schema for the final shape. Schema constrains types/keys, **not factual values**. Unexpected native requests are still parsed, counted and checked against registry/policy/budgets; declaration/request constraints are not relied on as the security boundary.

This is a small explicit fact workflow, not a complexity router or general planner. No tool schema is independently copied into an orchestrator catalog. The added runtime tool reads current workflow readiness/owned-runtime agreement, whereas model identity comes from the immutable run snapshot.

## 5. State machine

Implemented states only: `Idle`, `Analyzing`, `ExecutingTool`, `Verifying`, `Completed`, `Failed`, `Cancelled`. Inactive/busy/invalid requests retain existing Rejected outcome with terminal Failed state; deadlines retain TimedOut outcome with terminal Failed state. No approval/recovery/pause/resume state.

`AgentRunState` is separate from `ToolOutcome`. A denied/timed-out tool remains a typed dispatch fact; it does not become the Agent state. SharedRuntimeAgent assigns the terminal state after deterministic verification/failure/cancellation. No Partial state was needed.

## 6. Run limits

Immutable `AgentRunLimits` effective snapshot is attached to `AgentRunContext`. Trusted caller may lower limits; C# rejects ceilings above these bounds or invalid values before inference:

| Limit | Default / hard ceiling |
|---|---:|
| MaxModelTurns |4 |
| MaxToolCalls |3 |
| MaxToolFailures |2 |
| MaxRetries |1 |
| MaxRunDuration |60s default,120s maximum |
| MaxInputContext |32,768 UTF-8 bytes, aggregate serialized message context |
| MaxToolResultBytes |6,144 UTF-8 bytes, aggregate envelopes |
| MaxFinalOutput |16,384 characters |

Existing initial prompt+explicit RelevantContext remains <=8,192 characters. Output tokens remain1–256 per model request. Request Timeout, if supplied, becomes the effective immutable duration; it never resets between turns. Native adapter retains request/response cap65,536 UTF-8 bytes and parser depth12. No new token estimator.

`AgentRunProgress` owns C# counters/state/dispatches/verification and Stopwatch. Mutators are internal, limits immutable, cancellation token excluded from serialization. RunId, SessionId, UTC logs, immutable model snapshot and effective limits remain on context. Stopwatch is stopped at terminal completion, so returned execution summary is stable.

## 7. Tool-call budgeting

Each accepted native tool request consumes one slot **before** argument validation, policy or execution. Unknown/denied/invalid/timed-out/failed requests cannot get unlimited free attempts. At the ceiling, next request fails `ToolCallLimitReached` without executing another operation. Malformed native responses terminate rather than being repaired/retried.

Synthetic infinite-request proof: exactly **4 model requests,3 accepted tool requests,3 executions**, then ToolCallLimitReached; no fourth execution. Every inference/retry reserves a model-turn slot. Last-turn tool response without room for final verification fails ModelTurnLimitReached, with no extra inference. One native request per response, sequential awaits, no parallel executor.

## 8. Context/result budgeting

Accounts actual UTF-8 serialized messages: initial prompt/context/system envelope, retained assistant native responses/call metadata, tool-result envelopes and final response. Checks before sending another request and before verification. Historical call/result messages are appended once, not repeatedly duplicated. Initial user/context is preserved; Nexus may replace its own system final-format instruction after fact collection.

Aggregate `TotalToolResultBytes` includes every dispatch envelope, including typed failures. Per-tool cap remains independent. Crossing6,144 bytes fails ToolResultTooLarge; aggregate message context crossing32,768 bytes fails ContextBudgetExceeded; original oversize result payload is not forwarded. Tests lower aggregate limits to prove enforcement before further requests.

## 9. Failure classification

Typed `AgentFailureCategory` extends existing ErrorCategory compatibility rather than changing existing Gate B string expectations: Cancelled, DeadlineExceeded, ModelTransportFailure, NativeToolUnsupported, ToolNotFound, InvalidToolArguments, ToolDenied, ToolTimeout, ToolExecutionFailure, ToolResultTooLarge, ToolCallLimitReached, ModelTurnLimitReached, FailureBudgetExceeded, MalformedModelResponse, VerificationFailed, RuntimeDeactivated, ModelIdentityChanged, ContextBudgetExceeded, FinalOutputLimitReached, Unknown.

Counters include failed tool dispatches and failed model attempts (transport/protocol); verification failure is terminal and counted. At the failure ceiling no further turn is admitted. Typed dispatch facts survive cancellation, protocol/transport failure and failed final verification. No failure text/path/exception payload is turned into a successful result.

## 10. Retry policy

Default **no retry**. Only explicitly classified model HTTP408/429/502/503/504 may retry, max1, provided failure/model-turn/deadline budgets remain. Same logical request retried, sequentially, without deadline reset. Unknown/network errors without certified status,400/401/500, malformed response/metadata, policy/argument/identity/cancellation/budget/capability failures do not retry.

No automatic tool retry: current tools have no certified transient failure class. A timeout may be sent as bounded typed data for a model follow-up. If model asks for another operation, it consumes a new tool/model slot and failure budget; it is not a free executor retry. Any original non-success tool dispatch remains unresolved, so later success/prose cannot yield Completed.

RetryScheduled event records original ModelTransportFailure, attempt number and HTTP reason. Classifier table tests and transport tests prove one retry, no retry when disabled, no extra model slot and failure ceiling. No exponential framework/delay policy introduced.

## 11. Verification

`AgentResultVerifier` is deterministic C#, no LLM/Judge/critic. Fast path verifies nonempty bounded structure and finish_reason=stop; this is not a semantic truth guarantee for arbitrary no-tool prose.

Tool profiles require successful required executions, no non-success dispatch, and **raw JSON final** with exact unique keys/types and no extra fields. ModelSnapshot compares modelId/runtimeState to tool data and immutable model identity. ModelAndRuntime additionally requires the runtime snapshot and compares ready/current state against that independent output. Conflicting/missing facts, duplicate/extra keys, prose, fenced JSON or guessed correct-looking output without tool execution fail VerificationFailed.

Completed requires final model response, passed verification, no unresolved tool failures, and unchanged ready runtime identity. Failed tool outcomes remain in ToolDispatches; compatibility Tool references the last dispatch, never substitutes for the full fact list. No hidden reasoning/chain-of-thought transcript is retained.

Initial live failures were preserved: model returned fenced JSON (FinalStructureInvalid); stronger prompt alone then produced placeholders without execution (RequiredModelToolMissing). Neither was accepted. Native required-fact declarations plus final shape schema resolved formatting/required-execution reliability **without weakening verifier, increasing budgets or changing model/server**. Records: `orchestration-live.trx`, `live-diagnostic.trx`, `orchestration-live-final.trx` and associated live directories. Initial first failure lacked detailed category evidence; the diagnostic run captured fenced response and verifier facts.

## 12. Cancellation/deadline

One run-local Stopwatch budget and linked CTS cover admission, fingerprinting, model turns, dispatch/result handling, retry and verification. UTC Deadline is diagnostic/compatibility; active run calculations use monotonic remaining duration. Boundaries check cancellation and runtime identity, plus explicit monotonic expiration even if the timer callback has not fired yet. Tool CTS uses min(remaining run duration, tool timeout).

Tests cancel during initial request, tool execution and follow-up; verification checks cancellation; no later execution after cancel. Cancellation retains typed dispatched cancellation where present, releases lease and next run verifies. Two delayed model turns prove the deadline is not restarted. UTC Deadline moved into the past in a monotonic-context test does not change active elapsed authority.

Non-cooperative synchronous arbitrary extensions are not sandboxed: WaitAsync cannot preempt them. Both production tools are fast in-memory read-only snapshots. No global CancelStreaming call added.

## 13. Session/runtime isolation

Existing runtime/model/HttpClient reused; no starts/switches/stops initiated by Agent. SharedRuntimeAgent acquires the existing single additional-consumer lease and binds every turn to its immutable model path/PID/endpoint/instance snapshot. Identity changes fail ModelIdentityChanged; navigation/switch revocation cancels against the old snapshot, drains before stop/switch and never silently resumes on the new model.

Live proof confirms unchanged Chat Messages/SelectedConversation/ActiveConversation, byte-identical stored conversation detail/config/model index and unchanged design fixture hash. Themes/Views unchanged relative to pre-edit checkpoint. No implicit Chat history access; context is supplied by value. Gate B existing navigation/reuse/model-switch/live Chat and Gate A AI CHECK tests all execute in final full certification.

Real shared-transport cancellation passes both directions: Agent cancelled while Chat completes; Chat cancelled while tool-enabled Agent verifies. One Agent run at a time, same PID retained during coexistence, no orphan after cleanup. Server remains existing single-slot tuning; concurrency here is independent request lifetimes, not a claim of simultaneous inference.

## 14. Security/adversarial tests

Budget exhaustion exact counts; lowered model/failure/retry/context/result ceilings; invalid ceilings rejected; policy-denied declarations absent; accepted unknown/invalid requests consume budgets; no malformed free retry; user asks to increase permissions/budget; synthetic tool result contains fake system instructions and requests dangerous_tool; native request for dangerous tool rejected with zero execution. Reused IDs across turns rejected; existing simultaneous-call/parser/schema/name attacks remain covered.

Verifier tests reject final prose, wrong model/state, extra authority fields, missing required execution, missing second source and pseudo-tool text. Timeout→success→final still fails with retained original timeout. Cancellation/lifecycle/identity/monotonic tests, transient/non-transient retry table and hash-cache invalidation tests are included.

Structured events implemented: RunStarted, StateChanged, ModelTurnStarted/Completed, ToolRequested/Validated/PolicyDecision/Started/Completed, VerificationStarted/Completed, RetryScheduled, RunCompleted/Failed/Cancelled. Metadata includes RunId/SessionId/state/turn/call numbers/call ID/name/outcome/category/elapsed/modelId/remaining budgets. Tool events correlate RunId+ToolCallId. No production prompt/context/result payload/raw exception/chain-of-thought logging; synthetic native responses are diagnostic test artifacts only.

## 15. Runtime/PID evidence

Final full proof: `docs/gate-d-evidence/live-a8162dabec2d4d3d901c4cf6fbd7aa48/tool-proof.json`, native responses in the adjacent `synthetic-native-responses.json`.

- PID **17628**, runtime identity **65207930**, logical model **gemma-4-e4b**, existing localhost8080 endpoint.
- One-tool call ID `XcuSzC0OploE3DPPQX5z0JtZjXofwMYM`, Success,94-byte envelope,2 model turns.
- Two-source IDs `yNnwiZu6cfLHWpnIDkgjz2hkggV3Kcyn` and `fbXJ5f0WXxtx3PdTBbBXlZQfc8bRN9rh`, Success,177 aggregate result bytes,3 model turns/2 tool calls, deterministic facts verified.
- Intermediate assistant/tool IDs and exact serialized results asserted on captured follow-up requests, not just final prose. Exactly one alive PID at active stages, no owned/alive process after cleanup. Post-certification process check also0 llama-server.
- Model SHA-256 `519b9793ed6ce0ff530f1b7c96e848e08e49e7af4d57bb97f76215963a54146d`; executable SHA-256 `C72F76994F8D680D7B423FF68662DE33857D2A2B001465DE55952873D4EF3DF3`, b11057. Other identities remain Unknown.

RuntimeIdentity is process-local diagnostic identity, not durable/global ID. Diagnostic fixtures may contain paths/PIDs; neither production tool exposes them.

## 16. Performance observations

Final full-run exploratory observations, same owned model/runtime:

| Scenario | Elapsed | Model turns | Tool calls | First fingerprint cost |
|---|---:|---:|---:|---|
| One-call no-tool Agent |1,310ms |1 |0 |NO |
| First one-tool Agent |27,151ms |2 |1 |YES |
| Two-source bounded Agent |18,905ms |3 |2 |NO |
| Chat alone |600ms |1 |0 |NO |
| Chat with one-tool Agent, combined completion |9,665ms |Agent2 + Chat1 |1 |NO |

Capability check occurs once **outside** the turn loop. Actual computation count remains **1 across all tool-enabled runs in this fixture**, with4 cache hits at the end; no per-turn/tool GGUF hash. Cache invalidation tests cover model path/length/mtime and executable length. Original engine/model full path/length/mtime identity semantics and both SHA checks retained.

First fingerprint reading approximately5 GB still materially affects first Agent experience. Earlier trusted readiness initialization could be a separate optimization, but Gate D does not add eager/background hashing or weaken certification. Cost is documented. Different prompts, token caps, cache/warmup and scheduling affect samples; no SLA/P95/heavy-concurrency claim. Llama -np/context/threads/batching unchanged.

## 17. Targeted tests

- Initial Gate C boundary/protocol/Agent compatibility slice:56 passed.
- Initial orchestration selection:103 passed; lifecycle/cache selection:109 passed.
- Native schema live + targeted checkpoint:110 passed,0 failed/skipped.
- **Final targeted:113 passed,0 failed/skipped**, TRX interval **2.6419211 seconds**; `docs/gate-d-evidence/targeted-final.trx`.

Tests broadened at logical slice/certification boundaries. PASS stdout retained in logs, counters surfaced succinctly. Failed initial live attempts retained and investigated; no thresholds/assertions weakened.

## 18. Impacted regression

AI.Llama174, Conversation16, Integration Agent/tool/runtime/navigation/Chat/AI CHECK/architecture371: **561 passed,0 failed**, one opt-in Gate A live fact skipped in non-live selection. Remaining projects had no filter match. TRX/logs: `docs/gate-d-evidence/impacted-regression/` and `impacted-regression.log`. Skipped live fact executes in final full certification. Shared ordinary transport/runtime implementation was not changed.

## 19. Full certification

Restore PASS; whole solution build PASS,0 errors, two existing CS0067 emissions on unused `ApplicationVisualPreferences.PreferencesChanged`, **12.61 seconds**. Final entire `src/Nexus.sln`: **1,194 passed,0 failed,0 skipped,no abort**, all11 projects; Integration737. Wall **642.1450129 seconds**. Evidence: `docs/gate-d-evidence/full-certification-final/`, `full-certification-final.log`, `full-final.vstest*.log`.36 final visual captures under `docs/visual-evidence/gate-d-final/` (six views × three themes × two DPIs).

Initial full run is **incomplete**, retained in `full-certification/` and `full-certification.log`: nine project TRX files totaling447 passed, no integration TRX or final project result; harness reported ChildProcess.kill. No testhost/llama-server remained on inspection. Cause of the integration/harness stall was not established, and the incomplete run is not counted as certification. Diagnostic whole Integration run on identical binaries then passed **737/0/0** (TRX interval576.355 seconds). Final whole-solution rerun used added VSTest diagnostics with **unchanged binaries,6-minute hang watchdog, fixtures and assertions** and passed. No speculative production fix or timeout increase. This unexplained initial harness stall remains an observation, not claimed resolved permanently.

Reproduction environment:

```powershell
$env:NEXUS_GATE_A_LIVE='1'
$env:NEXUS_GATE_A_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-a-results'
$env:NEXUS_GATE_B_PROBE='1'
$env:NEXUS_GATE_B_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-b-evidence'
$env:NEXUS_GATE_C_LIVE='1'
$env:NEXUS_GATE_C_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-c-evidence'
$env:NEXUS_GATE_D_LIVE='1'
$env:NEXUS_GATE_D_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-d-evidence'
$env:NEXUS_TEST_MODEL_ROOT='D:\NEXUS\Models'
$env:NEXUS_TEST_ENGINE_ROOT='D:\NEXUS\Engine'
$env:NEXUS_TEST_DESIGN_PATH='D:\NEXUS\test\1.jpg'
$env:NEXUS_TEST_PDF_PATH='D:\NEXUS\Audit\llama.cpp-master\tools\ui\tests\stories\fixtures\assets\example.pdf'
$env:NEXUS_VISUAL_OUTPUT='D:\NEXUS\Nexus-CSharp\docs\visual-evidence\gate-d-final'
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore -m:1 --logger trx --diag "docs\gate-d-evidence\full-final.vstest.log" --results-directory "docs\gate-d-evidence\full-certification-final" --blame-hang-timeout 6m --blame-hang-dump-type none --verbosity minimal
```

Verify evidence parents/fixtures first; use the SDK executable because PATH dotnet is runtime-only. New total incorporates Gate D tests and user test additions present in pre-edit checkpoint; it is not a claim all70 added tests were authored by Gate D.

## 20. Files changed

New production files in `src/03_App/Nexus.App/Services/Agent/`:

```text
AgentOrchestrator.cs
AgentRunLimits.cs
AgentRunEvents.cs
AgentResultVerifier.cs
RetryClassifier.cs
Tools/GetRuntimeSnapshotTool.cs
```

Existing production changes: App.xaml.cs (explicit second read-only tool/orchestrator DI); AgentContracts.cs (immutable limits/progress/cancellation/fact verification/result summary); SharedRuntimeAgent.cs (bounded path, monotonic deadlines, verified terminal state); NativeToolModelAdapter.cs (native required/none declarations and final JSON schema); NativeToolCapability.cs (diagnostic hash/cache counters, unchanged SHA/cache identity semantics); ReadOnlyToolPolicy.cs (monotonic remaining time and declaration admission helper); ToolExecutor.cs (monotonic tool remaining time and correlated events). **AgentToolRoundTrip.cs removed/replaced** by finite orchestrator.

New tests: AgentOrchestrationTests.cs, AgentCapabilityCacheTests.cs, GateDAgentLiveTests.cs. Existing tests updated: ToolProtocolTests.cs (new verified orchestrator replaces old helper); SharedRuntimeAgentTests.cs (reusable controlled lifecycle fixture); GateCToolLiveTests.cs (unchanged original tool bounds checked, verified result and shared live fixture extended for two sources/cache evidence).

Documents: this report and `docs/gate-d-evidence/README.md`. Test/log/live/visual artifacts are diagnostics, not Agent persistence; tracked bin/obj changes are generated outputs. No csproj/package/solution/UI/ViewModel/runtime ownership/ordinary transport changes. Pre-existing user renderer/view/test changes were preserved byte-for-byte relative to Gate D checkpoint.

## 21. Known limitations

- Only two explicit fact verification profiles, not arbitrary semantic task verification or a planner. No-tool verification is structural only.
- One concurrent Agent, Option A workspace cancellation; no background pin, memory, persistent/resumable task or safe write.
- Non-stream native orchestration; only fingerprinted Gemma E4B/b11057 certified. Unknown fails; native required-tool/final JSON schema support demonstrated on that fixture.
- First-run SHA latency retained; path/length/mtime cache is not a tamper-resistant identity service. No hash per turn/tool.
- Trusted in-process tools must be fast/cooperative/bounded; no arbitrary-code sandbox. All non-success tool facts remain fatal to Completed.
- Only explicit transient HTTP retry; no certified transient tool retry class, no heavy-load performance guarantee.
- Initial incomplete full-run harness/integration stall cause unresolved; diagnostic and final full runs passed unchanged code/watchdog. Gate B's earlier startup variability remains documented in its report.

## 22. Recommendation for Gate E

Gate D bounded read-only orchestration is ready for review. **Gate E remains HOLD**. Write authority, safe mutation/expected hashes/conflicts/atomic replace/backups/recovery/approval/checkpoints require a separately reviewed authorization. This certification does not authorize any such capability.

**STOP after Gate D PASS.**
