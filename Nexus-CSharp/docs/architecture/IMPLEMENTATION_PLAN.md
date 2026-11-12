# Nexus Agent — Evidence-Based Implementation Plan

**2026-10-01 Gate A update:** baseline repairs and representative live verification are complete; whole solution PASS/COMPLETE (1,054/0/0), with explicitly agreed cold/warm PDF policy. [GATE_A_REPORT.md](GATE_A_REPORT.md) is the after-state. Sections below retain the original pre-repair plan/evidence; Gate B remains NOT AUTHORIZED.

Status: **proposal only after Phase 0/0.5; no Agent code written**. Date: 2026-10-01. Working-tree evidence: [ARCHITECTURE_AUDIT.md](ARCHITECTURE_AUDIT.md). Execution evidence: [BASELINE.md](BASELINE.md). Existing compatibility gate is FAIL/incomplete, so implementation phases below are conditional and not authorized merely by this document.

## 1. What exists

| Existing foundation | Evidence | Implication |
|---|---|---|
| Central DI + aliased singleton services | App.xaml.cs:55-217 | no replacement DI framework/container |
| Owned shared LLM process and model identity | LlamaServerManager; GenericLlamaEngine; live one-PID smoke | Agent must consume current runtime |
| Two existing generation paths | Chat pipeline; AI CHECK LlamaClient; shared transport helper | preserve tested semantic differences |
| Model catalog/selection/contracts | ModelCatalogService; IModelRegistry/IModelSelectionPolicy/ResolvedModelDescriptor | no new Agent-owned model registry |
| Conversation JSON/index/manager | Nexus.Conversation storage/index/manager | no default new database; mutable current state needs isolation |
| Context pruning/output reserve/attachment allocator | AttachmentContextBudgetService/WaterFillingBudgetAllocator | extend only where tool-result context requires it |
| Deterministic editing + backups | EditingLite snapshot/proposal/validator/writer/workflow | repair actual safe-write gaps instead of building new editing stack |
| Structured image/PDF/file/reference services | current module interfaces/workflows | adapter candidates, not unrestricted methods |
| Native tokens/theme/dialog/snackbar | App.xaml dictionaries, ThemeManager, existing controls | native WPF Agent UX can reuse controls |
| Logging/runtime events/tests/benchmarks | logger, ServerEvent, 11 test projects, Phase5Runner | observation/verification foundation already exists |

## 2. What is reused

- Same LlamaServerManager/GenericLlamaEngine instances, model root/catalog, current inference endpoint, payload helper, and shared HttpClient registration.
- Current Chat and AI CHECK execution paths, judgment parsing/noise/recall rules, model and UI settings semantics.
- Storage source of truth by responsibility. Run metadata may later extend existing persistence area after a durability requirement; it must not duplicate conversation history in an independent database.
- BCL async IO/JSON/SHA-256/Stopwatch/CTS/SemaphoreSlim for a small deterministic implementation.
- Existing deterministic file/domain services after validated input/permission boundary, not direct exposure of every public member.
- WPF Dispatcher, MVVM conventions, semantic resources, dialog/snackbar presentation.
- Existing regression tests and operation-specific verification before adding Agent-specific meaningful tests.

## 3. What is missing (proven gaps only)

1. **Reliable compatibility gate:** whole integration suite aborts; architecture/design/performance assertions fail. Build is green but not sufficient.
2. **General Agent contract/run orchestration:** existing fixed feature workflows do not provide Agent run state, identity, finite reasoning/tool loops, result verification.
3. **Tool-call protocol:** `LlamaTransportHelper.CreateChatPayload/TryParseChunk` has no tools schemas/call IDs/argument deltas/tool result messages; active model/build tool support not yet live-proven.
4. **Tool authority:** no general registry/risk/policy/approval boundary. Existing IImageToolModule is output configuration only.
5. **Bounded Agent run:** no max iterations/tools/context/run-time/retries/failure accounting or per-run concurrency authority.
6. **Shared runtime consumer admission/lifetime:** lifecycle locks serialize starts/stops, not inference ownership; navigation stops runtime. Agent could contend one server slot or be interrupted by model switching.
7. **Safe writes:** project-root prefix validation, no expected-current hash check before replace, incomplete desired-state verification and conflict-safe recovery; no tokens in EditingLite APIs.
8. **Run/session isolation:** singleton ActiveConversation cannot own concurrent Chat and Agent sessions safely; storage writers lack target serialization.
9. **Agent observability:** general logger/events exist but no correlated run/tool/budget/verification records.

Memory retrieval, skills, graph workflows, marketplace, self-improvement, remote providers and ToolHost are not automatically requirements just because another agent framework has them.

## 4. What will be added — smallest gated slices

### Gate A: Repair and complete baseline (before Agent contracts)

Required review: do fixes address existing user changes or test debt? Preserve authored working-tree work; checkpoint intended source independently before changes.

| Problem | Proven evidence | Smallest candidate fix | Verification |
|---|---|---|---|
| WPF host crash | full-run second Application exception; custom STA hosts | one test Application owner/STA lifecycle, isolated process for incompatible cases only if needed | entire integration suite completes repeatedly; no Application recreation/thread affinity failure |
| Reflection overload failure | ArchitectureGovernanceTests:274-277 | select expected GenerateMetadataAsync signature in test | reflection test covers intended method/contract |
| Physical-path gate violation | governance get_ModelPath assertion | identify exact production caller/fallback; choose minimal correction or update obsolete architectural rule with evidence | real fallback/image/Chat regressions; do not weaken gate just to pass |
| Default OCR fixture failure | isolated production graph resolves missing output Models folder | explicit test asset root/fixture; separately evaluate eager OCR startup coupling | DI graph test with actual model fixture; missing-model graceful startup scenario |
| Token governance failures | expected133/actual137 + ConversationView violations | review source baseline and migrate offending resources to existing semantic tokens | governance + native render/theme/focus tests |
| Timing failures | dense10 repeats fail; dense50/triage load sensitive | control benchmark workload/warmup/isolation first; profile dense10 before optimization | functional PDF parity + agreed reproducible timing gate, retain original failure evidence |

Gate A acceptance: restore/build PASS; **whole solution tests complete**, failures resolved or explicitly investigated and agreed baseline policy; representative live feature E2E inventory verified. Do not turn isolated passes into blanket hard-gate success. No broad architecture rewrite to repair test harness.

### Gate B / prospective Phase 1: Shared runtime consumer proof + minimal contract

Goal: run lifecycle/readiness ownership without second server. A small Nexus-owned request/result/run context can live in existing logical area; move a contract to Contracts only when actual cross-module consumers justify it. Name/signature follows existing code; do not create empty assemblies.

Plan:

1. Confirm instance identity and capture PID/model/config snapshots under production DI; verify no Agent `new LlamaServerManager`, `new GenericLlamaEngine`, server spawning, or independent model catalog.
2. Determine whether Agent initially runs only while workspace is active. If it must survive navigation, add minimum shared consumer admission/lifetime handling around current manager, with explicit user-visible model readiness. Agent cannot unilaterally switch/stop Chat runtime.
3. Add only request/result/limits/session ID/cancellation fields used by the first proof. Do not introduce persistence/schema until restart recovery is a requirement.
4. Verify exact b11057 engine and selected model native tool-call capability in a controlled fixture. If absent, stop and document protocol option; do not silently replace model/framework/Chat to make tools work.

Verification: same-model reuse/no duplicate PID; cancellation isolation; model switch/in-flight and navigation tests; Chat latency comparison on representative workload. Rollback: remove only new adapter/registration/contract; retain current feature registrations.

### Gate C / prospective Phase 2: Read-only registry + policy proof

Goal: one validated deterministic capability at a time before autonomous loop.

- Explicit registration allowlist, collision rejection, descriptor (name/description/risk/read-only/timeout/result limit), argument validation, typed result/error.
- Nexus policy decides allowed/deny; model cannot change metadata, roots, budget, approval, permission, or policy.
- First tool candidate: read approved config/model/theme **snapshot**, excluding sensitive values; GetThemeToken accesses approved keys on Dispatcher. Filesystem ReadFile comes only after canonical root and reparse-point containment is proven; existing SafePathCombine/ResolvePath are insufficient boundaries.
- Unknown tool/invalid arguments/path/oversize result -> deterministic deny/error. No shell/process/write tools.
- Cancellation/deadline and run/tool counters are in place before public execution; descriptor timeout alone cannot stop noncancelable native APIs.

Verification: denial cannot reach service; no writes; bounded output/timeout; cancel-one does not cancel Chat; malicious data/tool payload cannot grant authority; single registered instance; deterministic repeat result. Rollback: remove read-only adapter registration and run entry point only.

### Gate D / prospective Phase 3–5: Minimal bounded orchestration

Only after B/C. Add Agent-only tool-call request/response support on existing transport via optional fields/adapter; preserve Chat and Judge payload/parse behavior. A single-step reasoning/tool/verification request is the first proof.

Per-run context owns IDs, finite limits, CTS, immutable policy snapshot, history/tool outcomes, monotonic timing. Initial states used only where behavior exists: Idle, Analyzing, Executing, Verifying, Completed, Failed, Cancelled; Planning/AwaitingApproval/Recovering enabled when actually required. Tool state remains separate. No RollingBack state before implementation of rollback.

**Boundedness is not postponed until after an autonomous loop ships.** Define max iterations/tool calls/failures/retries/duration/context/output and sequential execution initially. Error classification distinguishes cancellation/timeout/invalid args/denied/transport/context/validation/unknown. Read-only transient retries finite; no blind write retries. C# validates result against desired state; LLM assertion is not success authority.

Fast path uses deterministic approved operation with minimum intent extraction. Agent path requires multiple reasoning steps. Heuristic Simple/Moderate/Complex/HighRisk only once multiple capabilities exist; telemetry precedes calibration. Workflow path deferred without long-running/resume dependencies. No arbitrary production weighted-score formula.

Verification: budget exhaustion/cancel-midstream/malformed tool deltas/unknown IDs/errors/truncated SSE, no cross-session contamination, no hidden process/write/permission increase; deterministic tool outcomes, no Dispatcher-blocking work, regression suite and latency sample. Rollback: unregister new entry point/adapter; Chat/Judge unchanged.

### Gate E / prospective Phase 6–7: Writes and operation-class recovery

Not part of first Agent proof. First fix EditingLite path/hash/cancellation/verification limitations, then adapt the service if requirement asks for file writes.

Read current state + hash -> compare expected at apply -> backup/temp -> deterministic syntax validation -> atomic replace -> exact desired hash/state verification. Concurrent change must conflict, including recovery (never overwrite external changes silently). Idempotent desired-state operation preferred; no-op can succeed rather than requiring every call to change hash.

Read: no checkpoint. Simple config/theme change: validated before-state + verify/revert. Multi-file source modification: branch/worktree/source checkpoint + build/test/diff, **not running executable mutation**. Dangerous filesystem: manifest/backup + exact approval + verify. Existing FileUtilities manifests/undo and EditingLite backups are candidates, not one universal rollback engine.

Verification: path sibling/junction/symlink, stale hash, cancellation before commit point, partial failure, conflict-safe rollback, desired-state equality, build/test diagnostics for source edits. Rollback: scoped checkpoint and conflict-aware recovery; never blanket reset dirty repo/user data.

### Later only with demonstrated requirement

- Native Agent WPF panel: progress/state/tool activity/cancel/failure; approvals only when write/risky operations exist. Existing dialogs/snackbar/resources; semantic theme tools, no arbitrary runtime XAML.
- Persist durable task/journal metadata in existing responsibility area only for restart/resume feature; no duplicate history DB. Agent must not mutate ConversationManager.ActiveConversation behind Chat.
- Context: reuse budgeting then measured tool-result bounds/pruning; accurate runtime budget alignment before compaction/retrieval.
- Memory/skills: incremental relevance-based loading; no eager all-history/all-skills prompt. Semantic/episodic/procedural distinction only when useful.
- Evaluation/telemetry: run/session/model/tool/error/state/duration/counters/verification; log bounded metadata without secrets. Measure success/error/latency/P95 from actual workloads; no invented SLA results.
- Routing calibration after observed data. Experimental self-improvement remains off pending evaluation/versioning/staging/approval/rollback; never policy/permission/core auto-rewrite.

## 5. What will NOT be added (and why)

| Excluded by default | Evidence/reason |
|---|---|
| Second LLM/server runtime | current shared manager + live single-PID model works |
| Agent-owned conversation/memory/task DB upfront | existing JSON responsibilities, no durable task requirement yet |
| Rebuilt Chat/AI CHECK | tests/source show functioning specialized flows; transport extension can be optional |
| New PDF/image stack | existing native services/tests and operation pipelines |
| Electron/WebView/browser shell | current WPF design system and native controls already available |
| Unrestricted shell/PowerShell | structured services suffice for read-only proof; actual build hook is separate risk boundary |
| Mandatory ToolHost executable | no risky external Agent tool yet; extra process justified only after isolation assessment |
| Many new assemblies | logical responsibilities can fit existing graph |
| Tool marketplace/plugin ecosystem/graph scheduler | no product requirement/evidence |
| Autonomous source/core/policy modification | correctness/safety/recovery outrank autonomy |
| MAF/Hermes runtime integration by default | Hermes is patterns reference; MAF benefit not measured |

## 6. Hermes and Microsoft Agent Framework decision

Hermes examined: reported main commit `5a2a300d99a156c6b286ba2376e7025015894eee`, run_agent.py + pinned tools/registry.py; patterns and review limitations in audit §25. Native C# adaptation only: lifecycle callbacks, one registry metadata authority, bounded results/errors, explicit session ownership/verification. Do not copy unlimited iteration default, Python runtime/mixin hierarchy/plugins/terminal tools. No source copied, license fetch 429; verify license/version/files before any future copying.

MAF: official overview documents sessions, tools, middleware, workflows/harness. Candidate advantage is long-running graph orchestration/resume/observability when a real workflow requirement appears. Before adding dependency answer: exact missing feature, existing equivalent/BCL option, maintained/version support/net8 compatibility, license/transitive/native assets/security/performance, and comparative implementation complexity/latency. A bounded spike must use existing local model runtime and Nexus-owned contract/policy/storage, with removal straightforward. No package added until benefit demonstrated; MAF must not dictate Chat/Judge architecture.

## 7. Before-significant-change decision checklist applied to planned slices

| Question | Required answer/evidence |
|---|---|
| Problem? | specific gap from §3 or failing Gate A; no diagram-driven work |
| Proof? | source location + failing test/measured scenario |
| Existing solution? | shared manager/transport/domain service/store/token/logger noted explicitly |
| Smaller solution? | repair/adapt/optional field before rewrite/new project |
| Dependency change? | default none; any addition has license/tree/cost review first |
| Failure mode? | no process ownership loss, unintended writes, cross-session state, silent partial success |
| Cancellation? | per-run/request token + finite deadline; classify noncancelable native boundary honestly |
| Verification? | deterministic expected state + appropriate tests/live regression/performance evidence |
| Recovery? | scoped registration revert, hash/before-state or worktree checkpoint as operation requires |
| Existing impact? | exact Chat/Judge/image/PDF/history/theme/runtime/config/startup gate recorded each slice |

## 8. Regression plan (hard gate per slice)

1. Restore/build active solution with actual SDK; preserve duration/errors/warnings. Build failure stops implementation.
2. Run **all** solution tests with reliable WPF harness; classify asset absence instead of false Passed; record full completion, not only filtered success.
3. Run targeted semantic tests for changed boundary (especially Chat sanitization versus Judge sanitization, cancellation isolation, model identity, policy denies, context budget).
4. Native smoke: startup/navigation/exit + one model process/readiness, representative Chat send/cancel/history and uploaded AI CHECK, image/PDF outputs, theme/config behavior. Use test fixture files/output area.
5. Compare Chat TTFT/end-to-end latency and UI Dispatcher/frame profile with representative prior workload; same hardware/model/context/load. Samples/P95 only from enough real measurements; set thresholds with evidence before gate approval.
6. Source diff review confirms no extra server/persistence/dependency, no unrestricted process/shell, no change to AI CHECK thresholds/prompts or PDF/image stack without requirement.
7. Record complete phase report: evidence/files/reuse/add/refactor/dependencies/build/tests/per-feature gate/performance/security/issues/checkpoint/next step.

Initial gate is currently FAIL, not waived by this plan. A future Agent working while old failures worsen is FAIL.

## 9. Rollback / checkpoint plan

Audit branch is an organizational checkpoint only. Before later source changes, inspect status/diff and preserve staged/unstaged/untracked **source** intentionally; do not infer all untracked files are generated. Obtain immutable checkpoint/backup of current user work (commit only when explicitly requested) and operation-specific expected hash.

Use small, independently buildable slices and opt-in new registration/entry point. Revert only authored changes; no git reset --hard/clean or overwrite of user's pending edits. For local source tasks, use approved branch/worktree/checkpoint and inspect full diff before merge/commit. Build signing hook/output and tests have side effects; separate them from application source rollback and record any artifact updates.

No schema migration in initial proof, so no DB recovery burden. Later schema/journal addition requires backward compatibility and recovery test before release. Hash/backup/checkpoint strategy matches operation class, not a universal rollback abstraction.

## 10. Stop point / next recommended work

Current deliverable completes audit and baseline documentation with truthful failed/unverified gates. **Stop here.** Next work, after review, is surgical Gate A baseline repair and missing live compatibility verification. Only then select the smallest read-only Agent integration slice. Phase 1 is not automatically underway.

## 11. Gate A completion addendum — stop before Gate B

Gate A executed surgically, including blockers exposed only after full-suite completion. Source/user staging preserved by pre-edit hashed archive and scoped comparison; no commit or Agent code. Existing runtime/persistence/UI/PDF/image implementations reused. Test ownership/resource/fixture debt repaired; actual first-send history and per-view radio-group defects repaired; PDF resource URI fixed. No dependency/assembly added.

Final gates: restore PASS; build PASS; all eleven test projects COMPLETE, 1,054 passed/0 failed/0 skipped; integration COMPLETE/PASS twice (597 each); genuine representative live Chat/AI CHECK/Image/PDF/history/theme/runtime verified. Exceptions/measurement scope and reproduction settings are explicit in GATE_A_REPORT and BASELINE §7. Original audit history remains intact.

Gate B baseline prerequisite is now fulfilled within agreed scope, **but authorization is still absent**. If later authorized, first re-inspect current working tree/checkpoint and select only shared-runtime consumer proof + minimal Nexus-owned contract. Initial constraints remain: no new server/catalog/database/MAF/write tools/shell/ToolHost. This addendum does not implement or authorize those additions.

**STOP. Await review of Gate A before any Phase 1 change.**
