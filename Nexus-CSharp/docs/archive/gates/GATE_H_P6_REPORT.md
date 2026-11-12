# Gate H P6 - host-authoritative repair composition

## 1. Baseline reconciliation

User explicitly authorized P6 implementation after the supplied status said pending authorization. Verified blocked archive `nexus-gate-h-blocked-source-20261007-151345.zip`, SHA256 `227c154dd45a1ad459c76ac7d5c0d5c2eea3c19c09403e86e408a298843c2f9c`,1,055archive source files/ZIP/all hashes. Before edits, affected Agent/ToolExecutor/SourcePlan/EditingLite/test hashes match blocked and P5: no conflicting affected source. Exact hashes recorded in external P6baseline.json. P5's1,006path reconciliation,15src changes and broader unknown/generated inventory preserved; no destructive restore/reset/stash/clean/commit. Staged patch SHA256b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046 preserved. User/pre-existing UI/project work remains outside P6delta; current tree is not historical blocked freeze.

## 2. Open-source pattern reuse

Aider failed-edit correction responsibility; MAF model inputs versus host runtime facts/schema-not-authority; OpenHands controller-owned admission. Certified llama.cpp b11057 native protocol/P1 required+single-source-tool retained. No parser patch/runtime replacement/new package/tool runtime/writer/approval engine.

## 3. Exact production delta

New SourceRepairInputValidator/ValidatedSourceRepairInput/AdvisoryRepairExclusion. ToolExecutor chooses this repair-specific path from actual coordinator state, retains existing plan/repair reservation, and passes only a typed projection to the existing source tool. Ordinary initial SourcePlanArgumentValidator/intent checks unchanged. ApplySourcePlanTool raw JSON entry explicitly denies repair-mode direct execution; internal typed entry composes and reuses its existing preparation/execution helper. SourceRepairFacts model projection marks original nonfailed slots optional advisory, removes their const/copy obligation. EditingLite adds read-only actual approval/proposal/recovery integrity queries; mutation/recovery/backend/receipts unchanged.

## 4. Trusted A-lineage proof

Capture requires exactly one actual SourceAttemptFact, matching RunId/Attempt1/nullparent/PlanId/storeddigest/profile/ordered intent targets and hashes/canonical Acollections. Progress plan/result must be the same actual A objects. Result.HasMatchingApproval consults the coordinator's internally stored SourcePlanApproval and recomputes its matching digest. VerifyApprovedRecoveredSourcePlan validates original snapshot bytes, desired hash, reconstructed proposal, recovered operations/base digest and all-source baseline. Synthetic A without the actual stored approval is denied.

New tests execute A through real ToolExecutor/source tool/coordinator and independent test approval provider, genuine sealed SDKbuild/required tests/compiler receipt, reverse recovery and original baseline. Thus A isn't a hand-created synthetic plan/result; only deterministic unit capability/model transport boundaries use the existing synthetic probe receipt pattern. Genuine live fixtures separately enable capability from measured actual native probes.

## 5. Repair-specific validator

Trusted single failed ordinal from actual eligible file-level compiler receipt or known required assertion facts. Multiple distinct failed files fail closed. Failed slot mandatory1–2nonempty/unequal exact literals; other original slots optional0–2bounded old/new echoes. Reject unknown slots/root/nested keys, duplicates, wrong/null types, invalidUTF8/NUL/length and budgets. Args4096bytes/depth4/rawpairs3/rawliterals1024unchanged; completeBnormal limits recomputed. No generic validator expansion or model-selected path/profile/failure/workspace/counters.

## 6. Advisory exclusion boundary

Success type contains trusted Aidentity/base digest/failed target and only FailedEdits plus exclusion metadata(slot/count/bytes/digest/disposition). No raw JsonElement/JsonDocument or advisory edit collections survive. Model Beta3/noop/arbitrary/empty/omitted echoes cannot reach Bcontent. Source policy admission remains host-owned; repair execution receives typed data, not raw echoes. AgentSourceRepairProjection explicitly records excluded advisories and composition time. Initial-plan mode remains strict and unchanged.

## 7. Host-composed B

Before preparation, recapture/recheck actual approved recovered A/context. For failed entry use typed model collection; for every other entry use exact canonical ResolvedEdits from approved A. Preserve ordered targets/BeforeHashes; defensively compare preserved intent. Same content/hashes/diff for permitted advisory variations proved using actual A. Complete intent then enters existing PrepareSourcePlanAsync: original-snapshot resolution/budgets/desired images/diff/newPlanId/digest. No copied Approval A or direct commit from projection.

## 8. Fresh approval and recovery

Existing ExecuteSourcePlanAsync performs NEW RequestPlanAsync for complete B, verifies matching approval, all-file preflight and guarded whole-file execution. Actual A approval replay denied; external changes before repair validation or after Bapproval are not overwritten. Tampered approval/digest/lineage/canonical collections/original snapshot denied. Existing recovery/compiler/assertion receipts/finalDesiredHash unchanged. Plans2/Repair1/noC/zero retries/same monotonic120s. Tests cover failed Alpha/Beta/three targets/H2+1/unknown/malformed/ambiguous/tampered/stale/injection/replay. No unauthorized Bpreparation/approval for admission failures; legitimate B prepared before later external approval-time drift is denied at preflight with zero operations.

## 9. Gate F live evidence

PASS genuine assertion A->recovery->host-composedB/newapproval/build/requiredtests/finalhash:100.390s<120. Two plans/one repair/two builds/two test starts/three model turns; noC/retry. Actual approved A checked in production probe; wrong Beta echoes excluded and hostBeta2 verified. Cold/warm/Chat repair probes39.633/8.001/8.830s;1,418prompt/55completion/context4,527bytes.

## 10. Gate G live evidence

PASS genuine boundCS0029 with unchanged compiler receipt/noAtests/full recovery/newBapproval/finalhash:88.403s<120. Two plans/one repair/two builds/one test start/three model turns. Cold/warm/Chat36.687/5.346/5.925s;1,408prompt/37completion/context4,166bytes. Empty/advisory Beta never determines B. Existing shared Chat/session/cancellation checks in live harness pass.

## 11. Gate H live evidence

PASS genuine approved Alpha2+Beta1 Acompiler failure ->recovery ->Alpha2model correction+hostBeta1 ->freshBapproval/build/three tests/exactfinalhash:104.8265266s<120. Two plans/one repair/two builds/one test start/three model turns/noC/zero retries. Initial registered native cold/warm/Chat17.619/10.206/10.807s,402prompt/79completion/174argumentbytes. Repair38.914/8.177/8.747s,1,476prompt/61completion/131argumentbytes/context4,442bytes;cache0/1,475/1,474. All calls correctnative name/tool_calls/one tool; read-only probes make no source/approval/tool dispatch.

## 12. Failed-Beta/three-target limitation assessment

Actual coordinator positive tests prove host authority/composition for these target shapes, but NOT reliable native generation. P5sixOutputLimit samples remain failed historical evidence. Two dedicated tests replay those exact retained responses after genuine approved/compiler-failed/recovered A. Existing native parser yields typed OutputLimit, modelturns2/sourceplans1/repairplans0/approvalrequests1/noB/noC/no retries; all source hashes remain original. Native reliability for these contexts remains an explicit capability limitation needing review/adequate new live evidence. They are not quietly skipped or counted as live repair positives. Multiple eligible failures terminal/ambiguous, not model-selected.

## 13. Timing and impacted tests

BuildPASS on pinned SDK8.0.424. Targeted42PASS/zero fail-skip-abort; added retainedOutputLimit2PASS; EditingLite impacted46PASS; final directly impacted Integration162PASS/zero fail-skip-abort. Performance positive5cases repeat actual A/B only to measure composition, not extra unique pass count. Full native live F/G/Hthree required tests eachPASS, plus narrow Gate BdiagnosticPASS. Counter/hash/session facts in genuine proof JSON. Host composition21.560–127.137ms includes actual approval/baseline/profile integrity checks, small relative to model latency.

Historical impacted run161PASS/1GateBdirectHTTP ResponseEndedFAIL retained. It bypasses P6adapter/repaircode; no test or timeout changed. Isolated GateBfixture thenPASS; complete impacted selection rerun162PASS. Failed history is not relabeled. Initial build blocked by user Nexus PID20156; user closed app normally. Subsequent init-only test mutation compile error fixed in test using reflection, then buildPASS; no foreign process killed/ACL/elevation/watcher change. Output256/context8192/hard120/turn/tool/repair limits unchanged. Full certification NOT RUN and runner rejects full mode pending separate signoff.

## 14. Files changed

Production(6): SourceEditPlan.cs(read-only approvalquery),EditingWorkflowService.SourcePlan.cs(read-only recovered approval/proposalquery),Tools/SourceRepairInput.cs(new),Tools/ApplySourcePlanTool.cs,Tools/ToolExecutor.cs,SourceRepairFacts.cs.

Tests: HostAuthoritativeRepairTests.cs(new,19cases total),AgentSourceRepairTests.cs(advisorynoop now correcthost-preserved success),MultiHunkSourceRepairTests.cs(changedBeta now correcthost-preserved success),GateFRepairLiveTests.cs,GateHMultiHunkLiveTests.cs. Existing initial-plan validator/NativeToolProtocol/NativeToolModelAdapter/AgentOrchestrator/AgentResultVerifier/ToolRegistry/policy/backend/receipt/project/package/config/WPF unchanged. Report GATE_H_P6_REPORT.md. Source/evidence snapshot metadata under docs/gate-h-p6-evidence/checkpoint.json is P6verification evidence, NOT Gate Hfinal freeze or full combined-source certification.

## 15. Recommendation

RESOLVED — REQUEST GATE H CERTIFICATION

P6required actual-approved-A/deterministic/native live demonstrations succeed for reviewed Alpha-failure F/G/Hfixtures. Request separate review/signoff before full Gate Hcertification, including explicit Beta/three-target/multiple-failure limitation decisions and stable combined-source reconciliation. No automatic full suite, runtime replacement, production scope expansion or Gate I. STOP.
