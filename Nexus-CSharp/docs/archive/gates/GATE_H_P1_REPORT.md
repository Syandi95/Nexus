# Gate H prerequisite P1 — native selection compatibility

## Baseline Reconciliation

Historical Gate G source archive `nexus-gate-g-final-source-20261006-155616.zip` verifies SHA256 `d473c992c3a429d67ea4aa5f626e1b5b3e84cc0fdd5fc55be4f61b983847c8e5`, ZIP integrity and all1,044source hashes.118tracked generated outputs remain separately archived. The current12pre-existing source differences are preserved, with path/historical hash/current hash/staged-unstaged state/classification/relevance in `docs/gate-h-p1-evidence/baseline-reconciliation.json`.

Reviewed relevant differences: LlamaTransportHelper changed public→internal; Llama project added InternalsVisibleTo for Llama.Tests. No native-selection logic change and no conflict with direct-HTTP NativeToolModelAdapter. Other differences:9XAML visual/resource updates and a pre-existing absent UnitTest1 placeholder; outside selection/authority path. Its missing current hash is recorded as null, not the empty-file hash previously used by the audit helper. All are preserved; no UNKNOWN entries. Staged Markdown/parser/renderer patch is unchanged.

Set reconciliation additionally finds pre-existing `ContextMenuCornerFidelityTests.cs` (two visual tests), absent from the historical manifest but present before P1; reviewed and preserved. Full certification includes it. Complete added/absent/changed path inventory is `combined-delta-paths.json`. The12count refers to historical-manifest differences, not this additional source.

## Production Tool-Choice Gap

The compiler-repair override sent a named-function object, which certified llama.cpp b11057 treats as wrong type and defaults to auto. Root cause is reproduced in retained prerequisite audit control:1completiontoken/stop versus79–80tokens/tool_calls with only tool_choice changed. Earlier Gate G PASS remains historical; named forcing was not supported despite successful fixture output.

## Open-source Pattern

Pinned b11057 `tools/server/server-common.cpp:1161` reads a string/default auto; `common/chat.cpp:345–355` accepts auto/none/required. Follow native string semantics, single host-selected tool for exact required and independent C# checking. Microsoft Agent Framework's official explicit-schema example distinguishes model-facing schema from recursive runtime validation. No provider abstraction or MAF runtime/package dependency.

## Minimum Correction

Only production change: `src/03_App/Nexus.App/Services/Agent/NativeToolModelAdapter.cs`. Compiler repair filters supplied trusted descriptors to exactly one registered nexus_apply_source_plan; missing/duplicate selection fails before HTTP. Sends string required. Empty descriptors retain none, including final compiler facts. Auto retains supplied visibility. A required turn cannot return no native call; a single-visible required turn cannot return a different tool. Existing NativeToolProtocol and ToolExecutor argument/policy checks stay unchanged.

Affected production call site: AgentOrchestrator.GenerateAsync supplies policy/registry-selected needed descriptor for required turns and no descriptors for final facts. Compiler repair formerly overrode this with object forcing. Other required one-tool calls inherit the explicit missing/wrong identity check. No orchestration, approvals, SafeWrite, recovery, compiler receipt, repair counts, source/model limits, build/test authority or multi-hunk production model changed.

## Serialization Tests

Targeted25/25PASS, zero fail/skip/abort (`targeted/`). New NativeToolSelectionTests covers string required/single intended descriptor; auto/all supplied tools; none/emptytools; prompt cannot select tools/choice; registry rejects duplicate descriptors; real bound recovered CS0029 context filters other descriptors and rejects missing/duplicate exact source descriptor; final none retained.

## Protocol Validation Tests

Correct required tool passes decoder/executor. Stop/no call→MissingNativeToolCall; wrong valid tool→UnexpectedToolName; duplicate id→DuplicateToolCallId; distinct multiple calls→ToolCallLimit. Malformed arguments/unauthorized approval field→existing ToolOutcome.InvalidArguments/zero execution. Existing parser rules unchanged. Directly affected orchestration assertions now expect early protocol failure instead of later verification/tool lookup:zero dispatch/retries, consumed model turn and unchanged limits.

## Gate G Regression

Genuine scalar compiler A→B live harness PASS (`scalar/`, `live-2e3c6e7de06b489b89bef96a9253cee0/tool-proof.json`). Recognized bound CS0029; no A tests; original baseline recovered; repair uses required/single source tool; correct scalar call; independent fresh B approval; two plans/one repair/two builds/one test start/three model turns; accepted final hashes. Full integrated task85.841s<120. Probe/harness tests explicitly assert request choice/one-tool visibility. No Plan C, no new retry.

Full certification repeats the genuine compiler fixture as part of the requested complete suite:92.782s<120 (`live-2870140b9cef47b6a997f8ca8123d714/tool-proof.json`). Historical Gate G reports/checkpoints were not overwritten or relabeled.

## Gate H Array Protocol Proof

Test-only candidate descriptor constructed at production adapter boundary, not registered with ToolRegistry or executor. Existing production schema registration still rejects arrays (explicit assertion). Exact production NativeToolModelAdapter serialization/HTTP and NativeToolProtocol decoder accept recovered-baseline2+1nativecall. Strict test-only closed-array/exact-literal validation rejects path/profile/approval/limit fields. Actual shared ConversationRuntimeWorkflow performs Chat contention. Zero source writes/approval/tool/build/test dispatch during this array probe.

Fixture SHA256 `ada75e79dc2080d886fa14b97c837f47492eb302c614dd55f667c8b42f00bd79`, retained with prerequisite evidence. This synthetic bounded array feedback is not a production multi-hunk compiler feedback factory or mutation grant.

## Timing

Actual production adapter repair-array samples (`array-f3c9191d58c7484eab2eecf6275e6ef3/array-proof.json`):

| Mode | Prompt | Completion | Cached prompt | Request seconds | Context bytes | Adequate |
|---|---:|---:|---:|---:|---:|---|
| Cold prefix |1045|79|0|30.858|3288|yes|
| Warm |1045|80|1044|10.364|3288|yes|
| Concurrent Chat |1045|80|1044|10.393|3288|yes|

168argumentbytes. Output cap256/context8192/Agent hardmax120 unchanged. Genuine Gate G task85.841s. Cold is zero prompt KV reuse on a started owned runtime, not total model loading. Serialized context larger than Python audit because .NET JSON escaping; prompt tokens unchanged. Timing is hardware/fixture-specific, not arbitrary human approval delay coverage.

Full-certification array repeat (`array-91f2d1f5834f4467a762a51bcd3370b8/array-proof.json`):cold33.190s/warm11.064s/Chat11.319s;1,045prompt,79/80/80completion,0/1,044/1,044cachedprompt tokens;3,288contextbytes/168argumentbytes;alladequate. Same production serializer/decoder and existing shared-runtime Chat path, no candidate mutation dispatch.

## Impacted Regression

387/387PASS, zero fail/skip/abort (`impacted-confirmed/`): Agent source-plan/repair including20compiler cases, registry/policy/tools, orchestration, shared runtime/capability, Conversation Chat, AI CHECK run/pipeline/state, architecture. No unrelated EditingLite suite was repeated as an impacted selection.

Historical failed array run retained (`array/`):new test counted an exited process snapshot at shutdown; matched established Gate C HasExited/livePID check instead. No live orphan was found. Historical impacted log (`impacted.log`) retains three obsolete failure-code assertions and outer engineering-tool interruption/no completed TRX; these are not counted as PASS. Directly affected assertions corrected without retries or weakened authority; completed impacted run proves the change.

## Full Certification

PASS — 1,383total/executed/passed across all11assemblies; zero failed/skipped/aborted/error/timeout/notRunnable/disconnected. Integration826PASS. One completed full solution run, wall2,171.517s (`full/`, `full.log`, `full-invocation.json`, `certification-summary.json`). Existing Gate A–G live flags/fixtures, pinned SDK8.0.424 and external supported DotNetHostPath runsettings retained. No skip or per-test/Agent timeout relaxation. Test count delta from historical1,370:+11selection tests,+1array live test,+2pre-existing UI tests,-1pre-existing absent placeholder.

Before report finalization, all candidate source hashes and staged patch match the tested combined tree. Final process query finds no llama-server/testhost/dotnet/fixture verification orphan. New test helper shutdown uses established livePID checks, not permission/elevation changes or foreign-process cleanup.

## Stable Checkpoint

New prerequisite checkpoint metadata: `docs/gate-h-p1-evidence/final-checkpoint.json` (archive path/SHA256/source count/separate generated archive digest/count/staged preservation/tested-code equality).1,047sourcefiles and118tracked generated outputs separate. Candidate `nexus-gate-h-prereq-toolchoice-candidate-source-20261007-131212.zip`, SHA25621533ef1df4e1334efc525cb12c707cdd9918c1de902f36093156341503f1a11, verified before/after the full run. Final snapshot differs from tested candidate only by this report's post-test completion text; executable/test code and all preserved pre-existing work match. Archive embeds reviewed reconciliation, array protocol fixture and reproduction runner; evidence is separately archived with a digest. This is neither a Gate G final freeze nor Gate H multi-hunk PASS.

## Recommendation

RESOLVED — PROCEED TO GATE H IMPLEMENTATION

P1 compatibility correction and reviewed stable combined baseline are certified. STOP. No multi-hunk production editing implemented and no Gate I work started.
