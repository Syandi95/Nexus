# Gate H acceptance — approved Option B / Workflow V2

Owner approval: 12 October 2026. Authoritative machine-readable selection contract:
[`gate-h-acceptance-manifest.json`](gate-h-acceptance-manifest.json).

## Old and new acceptance

Previously, the stochastic read-only P1 cold/warm/Chat generation-success probe was
mandatory and its safely rejected OutputLimit stopped certification. The owner now
approves two separately reported suites: **mandatory safety/functional certification**
and **P1 probabilistic reliability diagnostics**. Historical failures and reports are
preserved verbatim; this document changes future acceptance, not historical results.

Only this exact test is reclassified:

```text
Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat
```

Its original scenario, schema, 256 native-token cap, 64 Chat-token cap, 120-second
deadline, strict success assertions and validator remain. The test now has
`[Trait("Category","GateHP1Reliability")]`. Failure telemetry records the mode,
stage, exception/reason, available usage and zero-dispatch read-only boundary, then
**rethrows**. Raw requests/responses, xUnit failure and TRX remain visible. The
diagnostic remains independently executable; it is never deleted or silently skipped.

## Mandatory PASS remains unchanged

All discovered cases except the exact single P1 diagnostic are mandatory. This includes:
native protocol validation; malformed/truncated rejection; OutputLimit fail-closed;
zero unauthorized dispatch/mutation; approval A/B independence; SafeWrite/recovery;
multi-hunk correctness; genuine F assertion repair, G compiler repair and H 2+1 repair;
the Agent 120-second deadline; relevant A–H deterministic, WPF, OCR, ONNX and runtime
regression. A genuine repair that fails to generate a valid plan is a functional failure.
No other flaky-test exemption, blanket class exclusion or weakened security boundary.

Mandatory certification requires zero failures/skips/aborts/missing fixtures and safe
cleanup. Unauthorized mutation, invalid approval, acceptance of malformed output or
a deterministic host defect revealed by P1 is also a mandatory blocker.

## Explicit selection and inventory

```text
Mandatory:  Category!=GateHP1Reliability
Diagnostic: Category=GateHP1Reliability&FullyQualifiedName=Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat
```

These are reviewed filters, not undocumented exclusions. The manifest declares all
11 solution test projects and names all 13 required live A–H fixtures. Unclassified
tests default to mandatory. Discovery must find exactly one diagnostic-tagged case,
and it must be the named method. Additional tagged tests, missing assemblies/fixtures,
duplicate IDs and failed discovery are rejected by `tools/gate_h_acceptance.py`.

Implementation discovery (fresh VSTest discovery only; no model calls):

| Assembly | Discovered | Mandatory | Diagnostic |
|---|---:|---:|---:|
| Nexus.Contracts.Tests | 7 | 7 | 0 |
| Nexus.Foundation.Tests | 35 | 35 | 0 |
| Nexus.AI.EditingLite.Tests | 150 | 150 | 0 |
| Nexus.AI.Llama.Tests | 177 | 177 | 0 |
| Nexus.Conversation.Tests | 16 | 16 | 0 |
| Nexus.Core.HomeSettings.Tests | 24 | 24 | 0 |
| Nexus.Engine.ColorManagement.Tests | 10 | 10 | 0 |
| Nexus.Engine.ImageSubsystem.Tests | 123 | 123 | 0 |
| Nexus.FileUtilities.Tests | 19 | 19 | 0 |
| Nexus.PdfUtilities.Tests | 21 | 21 | 0 |
| Nexus.Integration.Tests | 868 | 867 | 1 |
| **Total** | **1450** | **1449** | **1** |

Exact IDs/FQNs/display names/sources/traits and binary hashes are in external
`nexus-workflow-v2/test-inventory.json`; summary is `discovery-proof.json`. Freeze a
fresh inventory matching the new candidate binaries. These counts do not claim that
the complete mandatory suite or P1 live diagnostic ran during implementation.

## Future execution commands — not authorized by this implementation approval

Pinned host on the present machine:

```powershell
$dotnet = "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe"
$settings = "C:\Users\Syand\AppData\Local\Temp\opencode\environment-dotnet.runsettings"
& $dotnet test "src/Nexus.sln" --no-build --no-restore -m:1 --settings $settings --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx --results-directory "<new-evidence>/mandatory/full" --verbosity normal --filter "Category!=GateHP1Reliability"
& $dotnet test "src/Nexus.sln" --no-build --no-restore -m:1 --settings $settings --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx --results-directory "<new-evidence>/diagnostic/full" --verbosity normal --filter "Category=GateHP1Reliability&FullyQualifiedName=Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat"
```

Actual certification must execute these commands through the existing verified Job
wrapper, rather than directly launching unowned process trees. Keep all manifest
`required_live_flags=1`, validated model/engine/fixture paths and evidence folders.
An unset fixture flag is not an acceptable skipped certification test. Test source
fixture requirements remain unchanged.

Updated external tooling paths, already used by Nexus certification:

- `C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_approved_preflight.py`
- `C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_instrumented_full.py`
- Unchanged `gate_h_certification_job.py` (verified anonymous Job ownership).

For a **new explicitly approved** freeze, set `NEXUS_GATE_H_FREEZE` to the new freeze
directory and `NEXUS_GATE_H_SUITE` to `mandatory` or `diagnostic`. Run preflight then
the wrapper once per suite. Each suite uses a separate evidence directory and exclusive
`run-started.json`; consumed historical approval/evidence cannot be reused.

New `candidate.json` must retain source/generated archives, source/input manifests,
binary/fixture/SDK/runsettings/environment identities and staged SHA from the existing
freeze mechanism, plus `acceptance_contract="NEXUS-GATE-H-OPTION-B-V2"`,
`acceptance_manifest_sha256`, and `test_inventory_sha256`. Put the exact frozen
`test-inventory.json` beside it. Source freeze must include the policy, acceptance,
test and repository tooling changes; external tooling changes must be hashed too.
Execution configuration stores the original unfiltered solution command; preflight
appends only the reviewed suite filter and routes evidence folders.

New freeze `approval.json` must be supplied from the explicit owner approval, with
matching candidate ID/SHA, acceptance-manifest SHA, inventory SHA,
`explicit_user_approval=true`, `authorized_suites=["mandatory","diagnostic"]` and
`authorized_runs_per_suite=1`. The updated preflight does not grant its own Option B
approval. Historical default mode retains its consumed-run guard.

## Reporting and safe diagnostic failure

Report discovered totals, mandatory executed/pass/fail/skip/missing and diagnostic
executed/pass/fail/skip separately. Exact TRX result IDs must match the frozen inventory;
duplicate, unknown, wrong-suite and missing results block certification. Report native
successes, OutputLimit, invalid tool responses, cold/warm/Chat modes, usage tokens and
failure traces from P1 proof and raw responses. Unattempted modes after failure stay
unattempted; no retry/top-up.

The wrapper lets a failed diagnostic finish writing TRX. It never relabels its failed
result PASS. `tools/gate_h_acceptance.py` automatically recognizes a **RELIABILITY FAILURE**
only when proof and actual TRX trace agree on a native-generation `ToolProtocolException`
rejection and record nativeSuccess=false with zero mutation/dispatch/approval. Missing
evidence, skip/abort, unexpected host/transport/lifecycle/assertion failure or any authority
violation remains a mandatory blocker requiring review. Other safely rejected stochastic
failures may be reviewed under this same owner contract; no speculative exemption is
automatically inferred from a generic assertion failure.

Combine both suites explicitly, preserving diagnostic fail counts:

```powershell
python "tools/gate_h_acceptance.py" --manifest "docs/gate-h-acceptance-manifest.json" --inventory "<new-freeze>/test-inventory.json" --mandatory-results "<new-evidence>/mandatory/full" --diagnostic-results "<new-evidence>/diagnostic/full" --diagnostic-evidence "<new-evidence>/diagnostic" --mandatory-invocation "<new-evidence>/mandatory/run-result.json" --diagnostic-invocation "<new-evidence>/diagnostic/run-result.json"
```

Mandatory PASS plus an evidenced safely rejected diagnostic reliability failure can
be reviewed without automatically failing mandatory Gate H. Final Gate H certification
and Gate I progression still require their respective owner reviews; **Gate I HOLD**.

## Implementation boundary

No complete solution certification or long live generation test runs in this task.
Targeted decoder/fail-closed, H and impacted F/G/H regressions plus wrapper/selection
safety checks verify the implementation. Reuse prior evidence only where source/test
identity permits and label it historical. Request a **new Gate H source freeze**, then
STOP; implementation approval is not full certification approval.
