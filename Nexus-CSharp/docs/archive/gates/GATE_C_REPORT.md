# Nexus — Gate C / Phase 2 Report

Date: **2026-10-03**. **Gate C PASS** for the bounded read-only tool path and certified fixture below. Full certification: **1,124 passed, 0 failed, 0 skipped, no abort**. **STOP before Gate D.**

## Scope

Explicit read-only registry, closed argument validation, Nexus-owned default-deny policy and a native protocol round-trip on the existing shared runtime. `AgentRequest.EnableReadOnlyToolCall` defaults to false; the existing no-tool streaming path remains available. Enabled runs allow at most **one tool execution and one model follow-up**, without retries or an autonomous loop.

Gate B admission, immutable run/model identity, one additional-consumer lease, per-request cancellation and Option A cancel/drain on navigation/model switch are retained. Existing Chat/Judge transport, runtime manager, model catalog and persistence are reused. No filesystem/shell/process/write tool, approval UI, MAF/Hermes runtime, ToolHost, Agent persistence, planner or visual polish was introduced.

## Checkpoint

HEAD `a530067973db2847d8c0028c6e17b0242e0ccabd`; branch `audit/nexus-agent-phase0-20261001`. Dirty working tree and user staged changes predate Gate C.

Verified pre-edit source checkpoint: **976 files**, raw Git index, exact staged/unstaged patches, status and per-file SHA-256:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-c-before-source-20261003-090610.zip
SHA-256: 728109cda2b3c48fa4738c6465519cacb322def2542fca8c61804113df072b4f
```

Post-certification comparison confirms no missing baseline file, user staged patch byte-identical and all **40 baseline Themes/Views files unchanged**. Only three existing source files differ from this checkpoint, listed under Files Changed. No commit, stash, reset or clean. Restore only authored paths after inspecting subsequent user edits; do not restore the whole index/tree blindly.

Verified post-certification source freeze: **991 files**, ZIP integrity and per-file SHA-256 verified, read-only archives; tracked generated outputs separated (118 files):

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-c-final-source-20261003-101421.zip
SHA-256: 35d9ef39c2433ff53f86dea79cea4bab237a1693c556b0857d8d4e7f1403424d
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-c-final-tracked-generated-20261003-101421.zip
```

Only this archive receipt was appended after freeze. Production/test code is exactly the green full-regression code; no source edit followed certification. Diagnostic live/log/TRX/visual outputs remain in their evidence directories and are excluded from source freeze.

## Tool Registry

`ToolRegistry` accepts explicit `INexusTool` instances from composition-root registration. No reflection, assembly scan, dynamic invocation or automatic exposure of existing services. Exact ordinal names match `\A[a-z][a-z0-9_]{0,63}\z`; case, trailing whitespace, dotted aliases and Unicode/lookalike spellings are not normalized. Unknown names yield `ToolNotFound` and zero execution.

Registry snapshots descriptors and clones JSON schemas, rejects duplicate names, invalid metadata/risk/limits and unsupported schema constructs. Descriptors exposed as a read-only collection; the policy allowlist is independently copied into a private set.

## Registered Tools

Exactly one production tool: **`nexus_get_model_snapshot`**. Empty arguments `{}`; risk `ReadOnly`, readOnly true, timeout **2 seconds**, result-envelope cap **2,048 UTF-8 bytes**. Returns logical model ID from the immutable run snapshot and state from the existing `ILlamaServerManager`. It does not start, stop or switch the runtime.

Live data: `{"modelId":"gemma-4-e4b","runtimeState":"Ready"}`. Complete serialized status/data/reasonCode envelope: **94 bytes**. No model path, PID, endpoint, configuration dump or credential is exposed by this tool.

## Tool Descriptor Schema

`ToolDescriptor`: Name, Description, Risk, ReadOnly, Timeout, MaxResultSize and ArgumentSchema. Registration bounds: description <=512 characters; timeout >0 and <=5 seconds; result cap 256–16,384 bytes; risk/readOnly consistency required.

`ToolExecutionContext` carries the existing `AgentRunContext`. Typed result outcomes: `Success`, `Denied`, `InvalidArguments`, `ToolNotFound`, `Timeout`, `Cancelled`, `ExecutionFailure`, `ResultTooLarge`. `ToolDispatchResult` includes exact call ID/name, validation, policy decision, typed result and serialized length.

## Argument Validation

Closed JSON schema dialect: object root, <=8 scalar properties, required keys and `additionalProperties:false`. Supports string (required maxLength 1–256, optional bounded enum), integer (Int64 range) and boolean. Unknown keywords, nested object/array types and invalid ranges fail registration; this is intentionally not a general JSON Schema implementation.

Raw arguments <=**4,096 UTF-8 bytes**, JSON depth <=4. Rejects malformed JSON, non-object roots, duplicate supplied properties, missing required keys, wrong type/enum/range, excessive strings/depth and extra properties before policy/execution. Attempts to supply `risk`, `allowWrite` or `requiresApproval` are extra properties, not authority.

## Policy Decisions

Nexus owns an explicit exact-name allowlist. ReadOnly must be true and Risk must be ReadOnly; writes/process/system-critical operations are denied even if their names are allowlisted. Valid RunId/session, model snapshot, unexpired run deadline and object arguments are required. Default-deny reasons include `UnknownTool`, `InvalidArguments`, `WriteOrProcessNotAllowed`, `PolicyDenied`, `InvalidRunContext`; successful admission is `AllowedReadOnlyTool`.

Prompt/context/tool-result text does not modify permission, descriptor risk, allowlist or execution bounds. Denied/invalid results may be represented as bounded tool data for the single follow-up; the overall Agent result cannot report Completed when its tool outcome is non-success.

## Tool Protocol

Optional dedicated **non-stream** native adapter uses the existing DI shared HttpClient and immutable shared endpoint. Ordinary Chat and Judge parsing is unchanged. Native `assistant.tool_calls` only: exact function name, argument JSON string and nonempty ID. Plain-text pseudo-calls remain text and are never executed.

Strict checks: one choice, assistant role, finish_reason, call type/shape, ID syntax, duplicate response properties/IDs, argument size and single-call count. Requests/responses capped at **65,536 UTF-8 bytes**, response JSON depth12; assistant text <=16,384 characters. The model receives the preserved assistant call plus `role:tool`, with **assistant ID = dispatch ID = tool_call_id**. Mismatches fail. A second native request on the follow-up fails `ToolCallLimitReached` without a second execution. No retries/text repair/automatic model replacement.

Capability is explicitly certified only for model `gemma-4-e4b`, `gemma-4-E4B-it-Q4_K_M.gguf` and llama-server directory/build **b11057**, with matching fingerprints:

```text
Model SHA-256: 519b9793ed6ce0ff530f1b7c96e848e08e49e7af4d57bb97f76215963a54146d
Engine SHA-256: C72F76994F8D680D7B423FF68662DE33857D2A2B001465DE55952873D4EF3DF3
```

Other identities/hashes return Unknown and fail `NativeToolCapabilityUnknown` before sending tools. File fingerprinting is trusted runtime capability verification within the run deadline, not a model-callable file-reading tool. Support cache uses full paths, lengths and last-write timestamps.

## Cancellation / Timeout

Caller, run deadline and Gate B lease revocation tokens remain linked across capability verification, native request, tool and follow-up. Run timeout defaults60 seconds/max120 seconds; output tokens1–256 per model request. Tool deadline is the smaller of descriptor timeout and remaining run deadline. Oversized results become `ResultTooLarge` with no original data forwarded. Exceptions become deterministic `ToolExecutionFailure` without exception payload/path forwarding.

Controlled tests verify cooperative tool cancellation and typed Timeout/Cancelled outcomes. `WaitAsync` bounds asynchronous completion but cannot preempt a blocking synchronous body or terminate a non-cooperative API; the sole production operation is a fast in-memory snapshot. Agent does not use global `LlamaClientService.CancelStreaming()`.

## Session Isolation

Actual live proof asserts Chat Messages/SelectedConversation/ActiveConversation unchanged, stored detail byte-identical, and fixture config/model-index byte-identical after tool-enabled runs. Context is explicitly supplied by value; registry/tool does not load Chat history. No production persistence was added.

Both cancellation directions use the real shared Chat transport: cancel Agent while Chat completes; cancel Chat while tool-enabled Agent completes. Owned PID remains the same and Ready during coexistence; cleanup stops the owned runtime with no alive leftover. Gate B navigation/reuse/switch/drain facts also execute in full certification.

## Security Tests

Adversarial coverage includes exact/Unicode/lookalike names, duplicate registration, invalid schema/metadata, malformed/non-object JSON, wrong integer type/fraction, enum/range/string/depth/size, required/extra/duplicate arguments and attempted authority overrides. Denied/unknown cases assert zero tool executions.

Tests also cover result caps, timeout/cancellation, sanitized execution failure/logs, native call IDs/mismatches/duplicates/missing fields/multiple calls, unexpected risk metadata, pseudo-tool text, unknown capability and repeated follow-up requests. A prompt asking to ignore policy cannot admit a denied tool. Logging records RunId/call ID/name/validation/policy/outcome/duration/result length/error category only; arguments, result data and raw exception text are not logged by the tool executor.

## Runtime / PID Evidence

Final live evidence: `docs/gate-c-evidence/live-3a869822e9dd415fbc4f47f69d03f4e6/tool-proof.json`:

- Owned PID **21912**, runtime identity **10810438**, logical model `gemma-4-e4b`, existing endpoint `http://127.0.0.1:8080/v1/chat/completions`.
- Native tool call **`QF5DvjCWNIzbojTTUbSKPfC6Vs2L4Pxq`**, Valid, AllowedReadOnlyTool, Success,94-byte envelope.
- Test observes exactly **two native requests**, asserts preserved assistant/tool IDs and exact serialized result envelope, and compares the final answer to the deterministic current model ID.
- Exactly one alive server at active stages; no owned/alive server after cleanup. Runtime identity is process-local diagnostic identity, not durable ID.

Earlier live evidence retained under `live-f5f0f45760a44eb5b3c3e0f5bcc08e29/` (PID19428). Raw Gate B native capability evidence remains in `docs/gate-b-evidence/`; Gate C assertions capture intermediate protocol facts rather than relying only on answer text. Evidence fixtures may record diagnostic paths/PIDs; these are not production tool results or Agent persistence.

## Performance Observation

Final live samples on the same owned model/runtime:

| Scenario | Elapsed |
|---|---:|
| Agent no-tool | 1,196 ms |
| First Agent one-tool round-trip | 23,944 ms |
| Chat normal | 571 ms |
| Chat with one tool-enabled Agent (combined completion) | 5,588 ms |

First tool run includes SHA-256 reading of the approximately5 GB GGUF; subsequent identity checks use the capability cache. Different prompts/token caps, warmup/cache and single-slot scheduling affect these exploratory samples. No normalized benchmark, P95/SLA, heavy-load concurrency or claim of no contention. Existing tuning/admission and Gate A performance policy are retained.

## Targeted Tests

- Initial boundary selection:29 passed; protocol/boundary/Gate B Agent selection:51 passed.
- Actual live + targeted checkpoint:52 passed,0 failed/skipped (45 seconds).
- Final edited boundary/protocol/Gate B Agent selection: **56 passed,0 failed/skipped**, TRX interval2.108 seconds. `docs/gate-c-evidence/tool-targeted-final.trx`.

PASS output is retained in logs; tests were broadened at slice/certification checkpoints rather than rerun after every small edit.

## Impacted Regression

Solution-filtered AI.Llama, Conversation, runtime/Chat/AI CHECK/navigation/architecture and new tool tests: **504 passed,0 failed**, one opt-in live fact skipped in this non-live run. AI.Llama174, Conversation16, Integration314 passed. Other projects had no matching tests. TRX: `docs/gate-c-evidence/impacted-regression/`. The skipped live fact executes in the full certification below.

## Full Regression

Restore PASS. Entire solution build PASS, **0 errors**, two existing CS0067 warning emissions (`ApplicationVisualPreferences.PreferencesChanged`),20.63 seconds. Full `src/Nexus.sln`,11 test projects: **COMPLETE/PASS 1,124 passed,0 failed,0 skipped,no abort**, wall **560.4669727 seconds**; Integration667 passed. Gate A/B/C live opt-ins enabled, existing Chat/AI CHECK/Image/PDF/history/lifecycle/UI checks executed.36 visual captures (six views × three themes × two DPIs) under `docs/visual-evidence/gate-c-final/`.

Evidence: `docs/gate-c-evidence/full-regression/`, `full-regression.log`, `restore.log`, `build.log`. No timeout/assertion weakening, replacement model or certification rerun required. Gate B's earlier startup timeout remains documented in its own report.

Reproduction (create/verify evidence parents first; use the SDK executable, not runtime-only PATH dotnet):

```powershell
$env:NEXUS_GATE_A_LIVE='1'
$env:NEXUS_GATE_A_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-a-results'
$env:NEXUS_GATE_B_PROBE='1'
$env:NEXUS_GATE_B_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-b-evidence'
$env:NEXUS_GATE_C_LIVE='1'
$env:NEXUS_GATE_C_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-c-evidence'
$env:NEXUS_TEST_MODEL_ROOT='D:\NEXUS\Models'
$env:NEXUS_TEST_ENGINE_ROOT='D:\NEXUS\Engine'
$env:NEXUS_TEST_DESIGN_PATH='D:\NEXUS\test\1.jpg'
$env:NEXUS_TEST_PDF_PATH='D:\NEXUS\Audit\llama.cpp-master\tools\ui\tests\stories\fixtures\assets\example.pdf'
$env:NEXUS_VISUAL_OUTPUT='D:\NEXUS\Nexus-CSharp\docs\visual-evidence\gate-c-final'
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore -m:1 --logger trx --results-directory "docs\gate-c-evidence\full-regression" --blame-hang-timeout 6m --blame-hang-dump-type none --verbosity minimal
```

## Files Changed

Existing source changed relative to Gate C checkpoint:

- `src/03_App/Nexus.App/App.xaml.cs`: explicit registry/tool/policy/adapter/capability/round-trip registrations.
- `src/03_App/Nexus.App/Services/Agent/AgentContracts.cs`: opt-in request flag and optional dispatch result.
- `src/03_App/Nexus.App/Services/Agent/SharedRuntimeAgent.cs`: bounded optional round-trip within the existing lease/deadline.

New production files under `src/03_App/Nexus.App/Services/Agent/`: `AgentToolRoundTrip.cs`, `NativeToolCapability.cs`, `NativeToolModelAdapter.cs`, `NativeToolProtocol.cs`; under `Tools/`: `ToolContracts.cs`, `ToolRegistry.cs`, `ToolArgumentValidator.cs`, `ReadOnlyToolPolicy.cs`, `ToolExecutor.cs`, `GetModelSnapshotTool.cs`.

New tests under `src/03_App/Nexus.Integration.Tests/`: `ToolBoundaryTests.cs`, `ToolProtocolTests.cs`, `GateCToolLiveTests.cs`. Documents: this report and `docs/gate-c-evidence/README.md`. Logs/TRX/live evidence/visual captures are diagnostic outputs; generated bin/obj changes are build artifacts. No package/project/solution dependency changes. Shared runtime workflow/lease, ordinary transport, catalog, persistence, ViewModels, themes and views are unchanged from checkpoint.

## Known Limitations

- Native capability certified only for the fingerprinted Gemma E4B/b11057 fixture. Other model/build combinations remain Unknown; no automatic substitution.
- Gate C native adapter is non-stream; Gate B's streamed diagnostic capability proof is retained, but streamed tool execution is deferred.
- Capability fingerprinting has a first-run IO/latency cost. Cache uses path/length/mtime, not a tamper-resistant identity service.
- Validator supports a deliberately small schema dialect. Trusted registered code must remain cooperative and return bounded data; executor is not a process sandbox for arbitrary extensions.
- Option A cancels/drains Agent when workspace runtime deactivates. No background pin, durable resume, scheduler, autonomous loop or heavy-concurrency guarantee.
- Tool dispatch metadata on the Agent result is available after a completed round-trip; protocol/transport/cancellation failure before return may omit it. Executor metadata logs still record dispatches that reached execution.

## Recommendation for Gate D

Gate C is ready for review on this explicit read-only scope. A separately authorized Gate D should preserve default-deny, exact native identities, bounded execution, session isolation and shared-runtime lifetime ownership. Expand authority or model support only with explicit contracts and evidence.

**STOP after Gate C. Gate D has not started.**
