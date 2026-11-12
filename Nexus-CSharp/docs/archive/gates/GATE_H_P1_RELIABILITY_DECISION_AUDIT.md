# Gate H P1 Reliability Decision Audit

**INCONCLUSIVE — RETAIN GATE H HOLD**

Read-only Nexus audit. No production/test/XAML/configuration, token cap, deadline, runtime/parser/model/dependency or authority change. No full certification. Historical failure is genuine OutputLimit; its trigger remains unresolved. Eight native diagnostics returned valid calls, but all intended concurrent-Chat conditions were invalidated by an external diagnostic assembly-binding error before Chat HTTP. Those conditions are UNVERIFIED, not PASS.

## 1. Baseline and source identities

Both ZIPs verified independently, including entry hashes:

| Reference | SHA256 | Scope |
|---|---|---|
| Historical gate-h-instrumented-20261010-100928 | `2bb26a498de63e3cdae6f8f9d48f35cf3d5ea79e42ccca2ee03953423c36301f` | 636 source/document entries; no external wrapper source manifest |
| Recovery gate-h-p1-wrapper-recovery-20261010-145652 | `41cfdb85b619f9766c91b1b92183b15fa4808562212fc36a7858cf3ebd605e34` | 639 source/document entries plus five external certification tooling files |

Recovery's three additional documents are source-freeze review, instrumented certification report and P1/wrapper recovery report. Manifests are not assumed identical. All 636 historical and 639 recovery working-tree identities match; all five external recovery tooling hashes match.

Staged patch remains `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`. P6 host-owned repair authority, OCR native clipboard/isolation, WPF corrections and AI Workspace improvements unchanged. This audit report is an output outside those snapshots. No restore/reset/stash/clean/delete/commit or overlapping affected-source edit.

Read the instrumented/recovery/P1 reports, original P1 fixture, adapter, decoder, shared-runtime/Chat lifecycle and verified Job Object helper. Current supporting binaries used directly; external package-free diagnostic harness built separately under the verified job.

## 2. Open-source implementations actually reviewed

Pinned b11057 source, fetched as read-only evidence and hashed:

- `tools/server/server-common.cpp`: string tool_choice parsing, request kwargs, thinking override, grammar/parser admission.
- `common/chat.cpp`: REQUIRED enum conversion, template inputs and Gemma4 selection.
- `common/parsers/gemma4.cpp`: actual native parser/grammar and tool-name traversal.
- `tools/server/server-context.cpp`: available-slot/LCP/LRU selection, cache update and per-task sampler construction.

Verified facts: server maps string `required` to REQUIRED; forwards it to Gemma4; Gemma4 tool-call repeat minimum is 1 for REQUIRED and grammar_lazy=false. It nevertheless allows thought/content and scan-to-toolcall prefix before eventual tool call. A finite 256-token generation can stop before that call; required grammar is not a bounded-time/token completion guarantee. Tool names are taken from exposed functions; its generic Gemma4 dictionary grammar does not apply each function's parameter JSONSchema. Strict host array validation remains necessary.

Server-context source creates/reset sampler from per-task sampling options (around line 1783). This contradicts assuming stale sampler reuse from cache-count similarity alone. Slot selection skips busy slots and selects available LCP/LRU matches; source behavior does not itself prove corruption or cross-session state mutation.

USE existing Nexus adapter/strict decoder/lease and Microsoft HTTP cancellation primitives; ADAPT read-only HTTP/slot observation in an external diagnostic; USE the verified Windows Job Object wrapper unchanged. REJECT runtime/grammar/parser replacement, retries and cap/deadline relaxation. No new framework/package dependency.

Source URLs are under `https://github.com/ggml-org/llama.cpp/blob/b11057/` with the paths above. Raw sources and SHA256 are in offline-analysis.json/experiment-plan.json. A requested common/chat-parser.cpp URL was absent; it is not falsely claimed reviewed. Actual specialized Gemma4 parser source was inspected instead.

## 3. Historical failure reconstruction

Test: `GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat`. Failed GenerateAsync at line 58; NativeToolProtocol.Parse raises OutputLimit at line 38.

Pinned model/runtime fingerprint: Gemma-4-E4B, `b11057-59657a613`; historical model/executable hashes retained by approved freeze. Native options: temperature=0, max_tokens=256, stream=false, enable_thinking=false, required, one source-plan tool.

| Historical mode | Prompt | Cached | Completion | Finish | Native call |
|---|---:|---:|---:|---|---|
| Cold | 1076 | 0 | 79 | tool_calls | Valid a:2+b:1 |
| Warm | 1076 | 1075 | 79 | tool_calls | Valid a:2+b:1 |
| Chat | 1076 | 1074 | 256 | length | None |

Failed response: 980 content characters, no reasoning_content field. Usage does not expose a reasoning-token breakdown; none invented. Prompt evaluation changes 1→2 tokens between warm and failed Chat, prediction duration 37.43413 s. This is observable cache/timing divergence, not a proven cause. Cancellation/120-second deadline is not the exception; runtime cleanup ran in finally.

Historical fixture starts a Chat task before native generation, then awaits Chat after native completion. Actual server overlap/slot state at native start was not recorded; the name Chat is not proof of simultaneous inference or active SSE. Single-slot runtime can queue requests.

## 4. Canonical request equivalence

Historical cold/warm/failed Chat requests are structurally identical, canonical SHA256:

`e2e1d037480c7615daeb83eb01526d247a394cdc915649c10a084e25b27e5c28`.

Every current native wire request has the same canonical SHA. Same model, messages/context, tool description/schema, required string choice, exposed `nexus_apply_source_plan`, temperature, thinking flag and cap. No cache_prompt/id_slot/response_format or scheduling parameter injected into native request. Context 3288 bytes, successful args 168 bytes.

Adapter builds request-local payload/tools, DeepClone snapshots messages, and independently requires matching native tool identity. Decoder accepts valid native shape and rejects length/no-call. No valid historical native call was discarded. Request mutation/admission defect is not established.

## 5. Offline evidence and first observed divergence

Offline analysis completed before model execution. Immediate failure is proven: generation exhausted 256 tokens without native call, safely terminal.

The first difference available in retained response metadata is cache/timing (warm cached=1075/prompt_n=1 versus failed Chat 1074/2), followed by generated content/finish. Historical request ordering at server/slot is unavailable, so this is not necessarily the first internal divergence. No claim that a single-token difference causes failure.

Three recovery repetitions (nine valid native calls) remain historical support for successful generation, not proof of zero failure probability. Current native grammar permits a prose prefix; it explains how required can coexist with length truncation, not why that prefix was selected in the failed run.

## 6. Fixed experiment matrix and budget

Plan written before live start: **8 native calls + 4 auxiliary Chat calls = at most 12 total model generations**, conservatively counting Chat as well. No extra warmup: each cold native measurement also sets up its following warm repeat.

Fixed order: NoChat pair → Chat pair → Chat pair → NoChat pair (ABBA). Four fresh runtime groups, same source runtime configuration/context8192 and single slot. Native cap256 and per-request120-second CTS unchanged. Auxiliary Chat uses original `Reply READY only.`, stream=true, MaxTokens64 and temperature0.

Cold criterion: cached<=16 and cached/prompt<=5%. Warm criterion: cached/prompt>=95%. Otherwise UNVERIFIED. Intended Chat gate waits for observed streaming response headers, then records live client state and read-only /slots before native send; no response-reader delay. This is an explicitly different controlled scheduling diagnostic, not an identical replay of original fixture timing. No artificial easier scenario or native payload substitution claimed.

External dry binding run had zero model generations. Actual live run attempted the fixed matrix once; no retry/replay/top-up after harness errors.

## 7. Every native generation result

All prompt counts 1076, completion79, finish=tool_calls, strict decoder/exact P1 arrays accepted, no cancellation, same native hash. Label for all eight: **VALID_NATIVE_TOOL**.

| # | Scenario | Intended Chat | Actual Chat condition | Actual cache | Cached | Native seconds |
|---:|---|---|---|---|---:|---:|
| 1 | N1-cold | No | NoChat control | Cold | 0 | 36.484166 |
| 2 | N1-warm | No | NoChat control | Warm | 1075 | 10.864133 |
| 3 | C1-cold | Yes | **UNVERIFIED** | Cold | 0 | 33.406321 |
| 4 | C1-warm | Yes | **UNVERIFIED** | Warm | 1075 | 10.523051 |
| 5 | C2-cold | Yes | **UNVERIFIED** | Cold | 0 | 33.734663 |
| 6 | C2-warm | Yes | **UNVERIFIED** | Warm | 1075 | 10.461802 |
| 7 | N2-cold | No | NoChat control | Cold | 0 | 32.474497 |
| 8 | N2-warm | No | NoChat control | Warm | 1075 | 10.717368 |

Four auxiliary attempts: **OTHER — HARNESS_BINDING_ERROR before HTTP**, no Chat generation. External isolated assembly load context produced MissingMethodException for `System.Net.Http.Json.JsonContent.Create(..., System.Text.Json.JsonSerializerOptions)`. Native reflection path worked, but framework/custom JSON type binding prevented Chat serialization. This is not an observed defect in normal Nexus/VSTest runtime. Earlier actual original Chat tests passed under their normal host.

Actual wire/model count: **8 native + 0 Chat = 8 model generations**. Four unused model-budget slots are not consumed by new calls. Raw generation-results.json's chatCount=4/total=12 counts attempted auxiliary invocations, not sent model generations; decision-analysis.json explicitly corrects this using zero Chat HTTP-start events. No silent budget extension.

Each native result includes scenario/start/end, PID/leasePID/state, strict result, cancellation/lease state, slot snapshot, raw request/response and timings. Source writes/tool dispatches/approval requests=0 throughout.

## 8. Chat contention comparison

**No valid concurrent-Chat cell was obtained.** Four attempts failed before HTTP. Trace: ChatHeaders=false, ChatDone=false, Chat task already completed/faulted at native-send start. Read-only /slots reports slot0 idle before native request. Therefore these four native passes are observations under effective no-Chat conditions; they are not concurrent success evidence.

Controlled client overlap/server inference overlap and the cache×Chat interaction remain UNVERIFIED. No inference about Chat contention helping/hurting success is justified from this matrix. Harness problem is reported rather than silently repaired and rerun beyond the fixed schedule.

## 9. Cache comparison

Valid cold/warm no-Chat comparisons show cold cached0/prompt1076 and warm cached1075/prompt1076, all native calls79 tokens and accepted. This demonstrates cache reuse/latency reduction in measured controls. It does not reproduce historical failed cache1074 or establish cause, corruption, or universal independence from cache state.

Intended-Chat rows have observed cold/warm cache but cannot test combined active-Chat factors. The intended full factorial matrix is incomplete despite eight successful native outputs. Small sample cannot establish zero failure probability.

## 10. Model/runtime findings

Runtime PIDs by group: N1 22248, C1 3084, C2 24428, N2 24100. Native lease PID matched server PID and stateReady for all calls; lease/current request cancellation=false. Responses have fingerprint b11057-59657a613. /props captures actual runtime metadata; /slots returns id0 and idle state before send, plus prior taskID/prompt tokens where available.

Each group's lifecycle records start/ready/stop/process-exited/STOP_COMPLETE and OwnedPid=null. Exited process snapshots are recorded alive=false, not orphans. Verified Job Object encloses build/dry/live roots and descendants; final job-only cleanup left membership empty. No historical PID ancestry cleanup or desktop application targeting.

Upstream required grammar is enabled, not ignored or automatically converted to auto. Required min1 applies to eventual tool-call branch while unbounded thought/content prefix is allowed. This structural observation and historical OutputLimit support a finite-budget mechanism; they do not exclusively distinguish model versus cache/scheduling trigger.

## 11. Confirmed/unproven decision table

| Question / hypothesis | Verified fact / supporting evidence | Contradicting or missing evidence | Confidence / required action |
|---|---|---|---|
| H1 Nexus isolation defect | Same native requests; local payload/DeepClone; unchanged lease/runtime identity; prior isolation regressions pass | No valid current Chat overlap; hidden runtime interaction not exhaustively excluded | No causal host defect verified; no production fix authorization requested |
| H2 Cache/KV sensitivity | Warm cache1075 controls pass; failed historical cache1074 | Different historical scheduling unknown; no matched 1074 reproduction | Inconclusive; do not label corruption |
| H3 Concurrent scheduling effect | Fixture attempts coexisting Chat; np1 slot source queues busy requests | Historical overlap unknown; current Chat fails pre-HTTP | UNVERIFIED; repair diagnostic compatibility before any separately reviewed matrix |
| H4 Finite model generation | Historical length256/no call; Gemma4 permits content prefix before required eventual call | Eight measured no-Chat calls valid; causal trigger unobserved | Mechanism established, exclusive reliability attribution not established; OutputLimit stays terminal |
| H5 Fixture instability | Source does not gate/trace actual Chat overlap; synthetic fixed feedback scope documented | No demonstrated fixture mutation/admission bug; mandatory generation assertions remain valid | Clarify request-overlap versus simultaneous inference contract, no unilateral test change |
| Harness artifact | MissingMethodException before Chat HTTP; false header/task state, idle slots | Normal Nexus tests had valid Chat runs; native path still functions | High confidence external compatibility failure, not P1 root cause |
| Safety contract | Existing decoder rejects invalid/no-call; 0 dispatch/approval/write; prior60/71 regressions | No native OutputLimit reproduced in current controls | Preserve fail-closed; never promote invalid generation to success |
| Success contract | Historical failure; cold/warm/current controls can succeed | Intended concurrent cells unverified; small sample | Mandatory success remains open, not certified |

## 12. Safety versus success contract

**Safety:** no non-tool/truncated response authorizes tool dispatch, policy approval or source mutation. Historical OutputLimit rejects before an executable plan. Prior retained OutputLimit safety tests and current read-only observations support this, without adding authority.

**Success:** required P1 native proposals must have valid exact arrays within256/120s in required modes. It is a separate requirement; safety terminal failure is not successful generation.

Authoritative current classification: P1 is read-only supporting protocol evidence in architectural scope, but its enabled GateGLiveFact and strict cold/warm/Chat native-generation assertions were included in explicitly approved full certification with zero-failure/missing-mandatory acceptance. Thus it remains **mandatory functional-success acceptance for that certification** and supports safety evidence. It cannot be waived/reclassified based on the read-only label. Historical P1 report predates registered multi-hunk production implementation; current source/approval takes precedence over its earlier schema-registration description.

No test classification or expectation changed in this audit.

## 13. Minimum recommended change

No Nexus request/session/parser production defect verified, so **no production change recommended or implemented now**.

Minimum diagnostic proposal: bind the temporary harness HTTP-JSON/JSON dependencies consistently to the same verified testhost dependency closure (or use an existing compatible testhost observation path), verify Chat wire request and streaming observation without model generation, then obtain separate review for another fixed matrix. Do not create another provider/parser/framework. This external setup repair is not a P1 causal fix and was not used to add calls to this audit.

For the historical generation itself, no cap/retry/deadline/runtime/model change is justified by the current comparative evidence.

## 14. Acceptance-contract implications

Keep current mandatory native-success requirement and Gate H HOLD until the missing comparison/cause is reviewed. Do not accept OutputLimit as native success.

Separate explicit proposal, only if future evidence establishes unavoidable model success variability: consider classifying concurrent P1 as supporting reliability diagnostics while maintaining mandatory invalid-output safety and genuine approved repair success. Consequence: reduced claimed concurrent proposal-generation capability; intermittent generation failure explicitly supported as terminal, not functional success. This would require architectural acceptance review, separately authorized test-contract edits and a new source freeze/full-run approval. It is not adopted or recommended as a shortcut from this inconclusive matrix.

## 15. Evidence preservation and identities

External evidence:

`C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-p1-decision-audit\`

Contains separate archive verification/source manifests, staged/status snapshots, raw hashed pinned upstream sources, fixed experiment-plan.json (written before live), external diagnostic source/binaries/build/dry/live invocation records, every native request/response/result, redacted slot observations, /props, runtime/HTTP/SSE-state timeline, raw attempt counters and corrected decision-analysis.json.

All639 recovery source hashes, all636 historical identities and five verified tooling hashes match after diagnostics; staged digest unchanged. Only this repository report is added. Runtime/model binary identities are verified against the frozen reference in final evidence seal. No source/test/XAML/config edit, dependency/runtime replacement, new grammar or mutation authority. No full-suite invocation or Gate H PASS checkpoint; old approval unused.

## 16. Next action

Review the harness compatibility proposal and clarify the exact required Chat-overlap observation contract before another separately budgeted diagnostic. Retain native256/120s, required/single-tool and strict decoder; retain P6/default-deny authority and known limits.

Because concurrent conditions were not validly tested, this audit cannot distinguish H1/H2/H3/H4 sufficiently for an exclusive causal or model-reliability verdict. No implementation or full certification follows.

**INCONCLUSIVE — RETAIN GATE H HOLD**

STOP — Await architectural and acceptance review.
