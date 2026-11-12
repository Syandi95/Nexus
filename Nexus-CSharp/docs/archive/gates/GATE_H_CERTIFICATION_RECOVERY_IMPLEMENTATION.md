# Gate H Certification Recovery — Implementation and Evidence

**Recommendation: RECOVERY PARTIAL — UNRESOLVED FAILURES**

Three original WPF failures are corrected and verified. Historical Gate B HTTP `ResponseEnded` and BiRefNet `bad allocation` remain unresolved: current targeted reproductions pass, but a verified causal repair of the historical failures has not been established. Gate H remains BLOCKED; Gate I remains HOLD. No full certification run or PASS checkpoint was created.

## 1. Starting source identity

- Verified blocked archive: `nexus-gate-h-certification-blocked-source-20261009-164159.zip`.
- SHA256: `ca96819264ef3e34802db87638e9527ca72e1aa46488884274b1237ca902aa46`.
- Candidate: 634 source files. Blocked archive: 635; the sole additional path is post-test `GATE_H_FINAL_REPORT.md`. Every candidate entry equals its archived bytes.
- Starting working tree: 635 files under the existing checkpoint source definition. Only difference from the blocked source archive: the authorized OCR test-only correction.
- HEAD: `a530067973db2847d8c0028c6e17b0242e0ccabd`.
- All 22 solution projects verified. Fresh read-only MSBuild evaluation verifies 428 active repository source/copy/import/configuration inputs, zero missing. Four copied `None` inputs are included; inert `None` items are excluded. An initial narrower 424-input enumeration omitted those four and was corrected, not interpreted as missing files.
- Tracked generated outputs remain separate: 118 paths. Generated binaries rebuilt during verification are not certified source.

Evidence: `docs/gate-h-recovery-evidence/starting-source-identity.json`, `current-active-input-manifest.json`, project evaluation files and `active-input-scope-reconciliation.json`.

## 2. Concurrent/user work preservation

Starting source hashes and staged bytes were saved before editing. No reset, restore, stash, clean, deletion or Git commit. No unexplained concurrent affected-source differences were found at start or final review. Existing source/UI work is preserved.

Staged patch SHA256 remains `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.

The OCR correction remains byte-identical: SHA256 `c0ddc8e77b4c2cd7f78b41823871bb042de86143718e81292a35b6f59f919391`. Clipboard implementation and OCR pipeline/ViewModel source remain untouched.

One build encountered a transient `.NET Host` output lock. The process had exited when inspected; no foreign process was terminated. A subsequent build succeeded. Final source comparison confines changes to the eleven reviewed WPF/diagnostic paths plus this report.

## 3. Root cause of ResponseEnded

**Localized, not causally resolved.** Historical `ResponseEnded` is the .NET `HttpIOException` error category, not a Nexus response-completion event. Historical stack fails inside `HttpConnection.SendAsync`, reached from `GateBToolCapabilityTests.SendRaw` and streamed-round-trip follow-up at old line 149.

That follow-up request explicitly has `stream=false`. The preceding SSE response passed its `[DONE]`, native tool-call identity and argument checks. Therefore this failure is not evidence of a missing Nexus terminal-event emission, double completion event, or content/event ordering bug. The failing transport operation ends before response headers are returned. The historical run completed two nonstream round trips and cleaned up its owned server.

Four fresh original-case runs with transport tracing PASS. PASS timelines show the SSE connection closed/replaced before the subsequent request headers are sent on a new connection. b11057's official `server-http.cpp` uses a chunked provider ending with `sink.done()` and returns `has_next`; this and HTTP pool reuse are relevant investigation leads. They do not prove the historical peer-close race without a matching traced FAIL.

No fresh traced FAIL was obtained. Historical evidence lacks connection IDs/peer-close timeline sufficient to settle the deeper cause. No delay, retry, exception suppression, fabricated event, pool policy change, or runtime rewrite was added.

Audited actual sources: `Nexus.AI.Llama/Pipelines/LlamaConversationPipeline.cs`, `Services/LlamaClientService.cs`, `Services/GenericLlamaEngine.cs`, `Services/LlamaServerManager.cs`, `Nexus.App/Services/ConversationRuntimeWorkflow.cs`, `RuntimeConsumerLease.cs`, `Nexus.Conversation/Manager/ConversationManagerService.cs`, transport parsing and relevant tests. Their production bytes are unchanged.

## 4. Streaming fix and exact files

**No production fix claimed.** `src/03_App/Nexus.Integration.Tests/GateBToolCapabilityTests.cs` adds diagnostic evidence only:

- System.Net.Http/System.Net.Sockets EventListener events and connection IDs.
- Existing server lifecycle events.
- Per-request start, headers, response lines/SSE frames, EOF, cancellation state, exception and disposal-scope timeline.
- Partial response preservation on failure.
- Timeline serialization deferred until fixture cleanup, reducing observation overhead in the immediate stream/follow-up transition.

Payloads, assertions, timeout, model identity, protocol and shared runtime ownership remain unchanged. The fixture does not borrow an Agent consumer lease; no Agent lease identity is invented in its trace. Fresh Chat/Agent causal event-completion coverage beyond the existing tests remains a prerequisite for any later lifecycle change.

## 5. Gate B regression results

- Original native tool-call/streamed-round-trip case: four separate runs, each 1/1 PASS.
- Existing `Nexus.AI.Llama.Tests`: 177/177 PASS.
- SharedRuntimeAgentTests, ConversationRuntimeWorkflowTests, ToolProtocolTests and AgentOrchestrationTests: 83/83 PASS.
- Zero skips, aborts or failures in these completed runs. Per-live-run server cleanup assertions pass; final process inspection finds no llama-server/testhost/Nexus.App process.

These passes validate current behavior and the diagnostic addition; they do not certify the historical failure as repaired. Historical FAIL and fresh PASS evidence remain separately retained.

## 6. ONNX allocation root cause

**Unresolved.** Exact historical exception: `Microsoft.ML.OnnxRuntime.OnnxRuntimeException : [ErrorCode:Fail] bad allocation`, thrown by `InferenceSession.Run` at `PerfAuditBenchmarkTests.cs:199` (timed inference, not session creation). Historical reported duration: 141.9799315 seconds. Commit/headroom measurements were not captured during that run.

The unchanged benchmark passes isolated, under 4 GiB additional measured commit pressure, and after an explicit relevant cold/warm predecessor followed by two threading-method repetitions in one process. Native peak is substantial, but observed memory returns near baseline after session disposal. A genuine historical leak, unintended concurrent session construction, fixture interference or environment exhaustion cannot be chosen as the cause from the existing evidence.

The benchmark creates one scoped session per thread configuration and disposes each Run result. Benchmark SessionOptions are not explicitly disposed; official ORT guidance identifies this as a lifetime concern, but its contribution to this historical large allocation failure is unmeasured. The manager's GetOrAdd/factory behavior also merits causal concurrent-construction tracing if implicated. Neither concern was relabeled as the proven root cause or repaired speculatively.

## 7. ONNX fix and memory measurements

**No ONNX source/configuration change applied.** Model, resolution, quality, acceptance, CPU arena and thread settings are preserved.

| Probe | Measurements |
|---|---|
| Isolated benchmark | 1/1 PASS; 151.238 s; minimum available commit 18,870,411,264 bytes. Initial process sampler missed dotnet-hosted testhost; its zero private-byte reading is invalid, not a zero-memory claim. |
| Corrected sampler + 4 GiB commit pressure | 1/1 PASS; 153.923 s; peak host commit 17,068,544,000 bytes; minimum available commit 14,377,091,072 bytes. Pressure is privately allocated/released by the external wrapper; benchmark unchanged. |
| Explicit cold/warm predecessor + threading twice | Exact unchanged methods PASS; peak commit 17,107,636,224 bytes; minimum available commit 16,703,225,856 bytes. After method disposal/return: private bytes 103,059,456; 104,075,264; 199,983,104. |

The explicit harness records managed heap/commit, GC counts and process memory before/after each method. Last managed heap approximately 28.8 MB, managed committed approximately 64.2 MB. Input stays `[1,3,1024,1024]`; threading benchmark stays `[1,2,4,6,8,12]`, default sequential ORT execution/default arena and no intentional concurrent benchmark inference.

An external reflection-harness setup initially resolved an incompatible native ORT library and crashed with AccessViolation before inference. Explicit NativeLibrary import resolution to the existing test output's win-x64 ORT library corrected that harness. These setup crashes are not the historical `bad allocation` and are not counted as successful benchmark runs. No model/native runtime replacement was made.

The evidence demonstrates release of large session allocations in these runs, not a universal absence of all native leaks. Arbitrary unmanaged allocations/session internals were not traced; historical native allocation ownership remains unknown.

## 8. BiRefNet regression results

- Isolated original case: PASS.
- Measured commit-pressure original case: PASS.
- Explicit predecessor plus two repeated original threading methods: PASS, external causal/lifetime harness rather than xUnit-runner totals.
- Existing relevant image pipeline/model-manager/service/diagnostic tests: 23/23 PASS, zero skips/aborts.
- Synthetic benchmark alpha compositing completes; no model or segmentation algorithm was modified. A newly certified end-to-end production-quality/cancellation/allocation result is not claimed while the historical root remains unresolved.

Evidence: `historical-onnx-failure.json`, `b-*-memory.json`, benchmark/TRX logs and `b-explicit-predecessor-exact-native-harness.log`.

## 9. Three WPF failure causes

Original three tests reproduced as 0 PASS / 3 FAIL before correction.

1. **BASELINE/INVENTORY DRIFT + INTENTIONAL DESIGN IMPROVEMENT:** Sidebar toggle migrated from literal `&#xE90C;` plus explicit `NexusTypographyIconFontFamily` to `NexusIcon.Glyph="SidebarToggle"`. Exact source comparison proves one retired font consumer, 8 -> 7, total primitive debt 126 -> 125. Preserve the icon improvement.
2. **GENUINE DEFECT, layering:** Photos-style Image Tool navigation introduces seven module-local RGBA literals and a common Button template. The visual improvement is intentional; defining common template/palette in the module view violates existing design-system ownership.
3. **BASELINE/INVENTORY DRIFT:** Frozen primitive allowlist still expects eight Conversation icon-font references. Actual seven; this is the same exact retired consumer, not a reason to restore a redundant font setting.

Impacted tests additionally enforce no hex literals in canonical Buttons.xaml and SystemColors in High Contrast semantic brushes. Those contracts are preserved, not weakened.

## 10. Exact WPF resource/test corrections

- `Views/ImageSubsystemView.xaml`: remove only local PhotosNavigationButtonStyle definition; existing key usages, host bindings, navigation commands and layout stay.
- `Themes/Components/Buttons.xaml`: centralize the same style, geometry, triggers and bindings; use eight semantic navigation roles for seven state colors plus foreground.
- `Themes/Tokens/PrimitiveTokens.xaml`: promote six existing navigation RGBA values; reuse existing white and #20FFFFFF primitives.
- `Themes/Semantic/{Light,Dark}SemanticTheme.xaml`: preserve exact original palette through semantic brushes.
- `Themes/Semantic/HighContrastSemanticTheme.xaml`: use dynamic SystemColors for foreground/background/stroke, retaining every original overlay alpha and geometry. This follows the existing HC contract and adapts to system palette changes.
- `DesignSystemGovernanceTests.cs`: retire only the exact font entry 8 -> 7 and reconcile total to 125 with source-based explanation. Other debt expectations, discovery and scanners unchanged.
- `NexusUIFoundationTests.cs`: require the eight new roles and approve only the six exact promoted existing colors. No arbitrary color/template allowance.
- `VisualFidelityTests.cs`: three theme cases verify compiled shared resource lookup, original geometry and rest/hover/pressed/disabled state contracts. Existing Dispatcher-drain pattern handles binding updates, no sleeps.
- `docs/design-system/migration-baseline.md`: retain historical inventories; document exact retirement/migration and visual comparison.

Remaining module debt: primitive 125, hardcoded hex 137, module-named style 7, common Button template 1. No new module exception is added. ConversationView.xaml itself remains unchanged.

## 11. Visual fidelity evidence

- Final WPF/theme/Image Tool/OCR/AI CHECK impacted run, including governance: **89/89 PASS**, zero failed/skipped/aborted.
- Governance + new navigation theme tests: 13/13 PASS in earlier targeted iterations; final 89-case run verifies the final High Contrast version as well.
- Before/after navigation render comparison: 3 themes × 4 states × 2 DPI values = **24 comparisons, zero differing pixel channels**, on the current system palette. 48 paired PNGs retained in `navigation-comparison/`; hover pair visually inspected.
- Existing compiled module capture: 36 PNGs, Light/Dark/High Contrast at 96/144 DPI, plus resolved resource inventory.
- No harsh outline, changed spacing, lost image-navigation behavior or broken dictionary reference observed. Future custom HC palettes intentionally follow SystemColors; equality to the former fixed black/white palette is not claimed for every possible system palette.

## 12. Enhanced Open Source decisions

| Reference/component | Decision | Application |
|---|---|---|
| Existing Nexus runtime, lease and native protocol | USE | Preserve ownership, session separation, cancellation and bounded Agent limits. |
| ggml-org/llama.cpp b11057 server-http.cpp | USE for audit | Inspect actual SSE provider/HTTP lifetime; no binary/parser patch adopted. |
| dotnet/runtime HttpClient/EventSource + official guidelines | ADAPT diagnostics | Existing EventListener traces pooled connections; reject an unproved retry/pool workaround. |
| Microsoft Agent Framework .NET samples | NOT NEEDED as dependency | Existing Nexus shared runtime already owns relevant lifecycle; replacing it would exceed causal evidence. |
| Official ONNX Runtime C# lifetime guidance | USE for audit | Compare IDisposable ownership/results; no unmeasured arena/thread/model adjustment. |
| NativeLibrary/AssemblyDependencyResolver | ADAPT in external harness | Resolve existing exact ORT native binary; no application abstraction change. |
| dotnet/wpf merged resource dictionaries | USE | Existing component/semantic/token layering and resource lookup. |
| lepoco/wpfui Resources/Palette.xaml | ADAPT established pattern | Central colors/brush references through existing Nexus tokens, without copying its palette or adding WpfUI dependency. |
| Existing Nexus WpfAppHost, theme/fidelity tests | ENHANCE | Compiled three-theme state checks and explicit before/after captures. |
| New framework, dependency, custom llama parser, disabling benchmark/scanner | REJECT | Unnecessary or unsupported by verified cause. |

References inspected:
- https://github.com/ggml-org/llama.cpp/blob/b11057/tools/server/server-http.cpp
- https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
- https://github.com/microsoft/agent-framework/blob/main/dotnet/README.md
- https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-merged-dictionaries
- https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Resources/Palette.xaml

## 13. Combined targeted/impacted tests

Completed evidence: Gate B four single-case passes, runtime 177/177, shared runtime/Agent 83/83, BiRefNet two single-case passes plus explicit three-method lifetime sequence, image regression 23/23, final WPF impacted 89/89.

The historical OCR 118-execution evidence is not the sole post-resource-change proof: its 116 unique affected tests were rerun together on final resources and PASS with zero failed/skipped/aborted. Live clipboard tests are retained and their prior format bytes restored/verified by the external wrapper. Owner identity/sequence cannot be restored; that limitation remains documented.

Final cross-group certification regression is not declared complete because A/B causal fixes are unresolved. Intermediate failed C attempts and helper setup failures are retained; they are not omitted from the evidence history.

## 14. Gate F/G/H regression results

No fresh live F/G/H run in this recovery pass: the specified all-groups-resolved prerequisite is not met. Their historical live PASS evidence remains historical, not reused as fresh certification. Source hashes confirm SourceEditPlan, multi-hunk resolver, repair composition/authority, approval, SafeWrite, recovery, ToolRegistry policy, repair count and 120-second deadline remain unchanged.

Known repair capability limitations remain: tested single trusted failed-file repair scope only; broader Beta/three-target automatic native repair is not newly demonstrated or enabled.

## 15. Full certification

**NOT RUN — prerequisites unmet.** No mandatory full-suite fixture was disabled to obtain a PASS. No full-suite loop, timeout relaxation or certification acceptance change. The one historical full run remains 1,447 executed / 1,441 PASS / 6 FAIL; OCR is separately resolved, the three WPF blockers now verified corrected, and two historical blockers remain causally open.

SDK remains 8.0.424, runtime .NET 8.0.30 and existing supported DotNetHostPath runsettings. Pinned server/model identities freshly verified in `pinned-runtime-model-identity.json`; neither changed.

## 16. Final source/test hashes

`implementation-review.json` contains the exact eleven changed source/test/document SHA256 values, unchanged staged SHA, unchanged OCR correction and allowed-diff comparison. `implementation.diff` records the actual blocked-to-recovery changes, including pre-existing user context.

`final-source-identity.json` records the complete current source manifest (including this post-verification report), generated-output separation, report SHA256 and active-input manifest. It is evidence of preserved working-tree identity, **not tested-code equality/full certification or a PASS checkpoint**.

`git diff --check` PASS; only existing LF/CRLF normalization warnings. Untracked source changes are additionally reviewed against archived bytes through the explicit implementation diff.

## 17. Remaining limitations

- A: capture a matching failed HTTP timeline/peer-close mechanism and prove a bounded causal repair before changing connection/event lifetime. Current passing repetitions do not close an intermittent historical defect.
- B: historical commit headroom and allocation ownership unknown. Current large native peak/disposal measurements do not reconstruct the original full-process predecessor/resource state or certify every production quality/cancellation path.
- Initial external harness native-loading/WPF-startup setup failures are not application regressions; final benchmark/visual probes have valid matching library/resource setup. All evidence remains retained.
- Fresh combined certification, F/G/H live deadlines/approvals and final solution certification are still required after A/B resolution.
- Manual real CorelDRAW paste validation remains independent; production clipboard fix preserved.
- No new recovery/certification source ZIP is created because certification conditions did not pass. Historical checkpoints are untouched.

## 18. Recommendation

Accept the verified WPF corrections and retain the enhanced Gate B diagnostics. Keep A/B open until their original causal failures are proved repaired. Preserve the current source and evidence for continuation; do not advance Gate I or broaden Agent mutation authority.

**RECOVERY PARTIAL — UNRESOLVED FAILURES**

STOP.
