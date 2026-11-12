# Gate H — Clipboard Recovery Source Freeze

**SOURCE FREEZE READY FOR APPROVAL** — 12 Oktober 2026.
Gate H BLOCKED pending certification; Gate I HOLD.

## Candidate dan validasi

| Item | Identity |
|---|---|
| Candidate ID | `gate-h-clipboard-20261012-212144` |
| Candidate SHA-256 | `4b56ccf8d009f2f3542c48b49feabf5e7faef5e33fa375eb7f16383707b04184` |
| Source/document entries | **651**, semua cocok dengan recovery checkpoint |
| Active inputs | **433**: 428 build inputs + 5 policy/contract inputs |
| Projects / test assemblies | **22 / 11** |
| Current cases | **1451: 1450 mandatory + 1 P1 diagnostic** |
| Required live A–H fixtures | **13**, semuanya mandatory |
| Binary identities | **526**: 78 primary + 448 Windows dependencies |
| PDB / copied assembly checks | **386 / 69**, zero mismatch |
| External / runtime-model-fixture identities | **21 / 40**, validated |
| SDK / runtime | **8.0.424 / .NET & WindowsDesktop 8.0.30** |
| Staged SHA-256 | `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046` |

Source ZIP (read-only):
`C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-h-clipboard-frozen-candidate-20261012-212144.zip`

Generated/binary ZIP (read-only, **not certified**):
`C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-h-clipboard-generated-identity-20261012-212144.zip`
SHA-256 `971c52aa91d0e319f4f89feb9a70dc2a99a4b2cd42f450cd5f640ca21a2926d1`.

Recovery checkpoint SHA `0a59655d97bfcc0b079b9acf844fd5fad6710925d16e1a61b9053fa702fbca2c`
direkonsiliasi dengan current working tree, exact staged bytes dan external tooling:
zero unknown conflict/mismatch. Existing user work dipertahankan; tidak ada source,
test, production, UI/XAML atau policy edit pada freeze task ini. Historical consumed
candidate/approval tetap dipertahankan.

Perubahan executable dibanding freeze lama hanya `NexusClipboardTests.cs` serta updated
Integration DLL/PDB. Project/import/SDK inputs unchanged, sehingga evaluated graph
dipakai ulang setelah hashing semua inputs. Current discovery/targeted recovery evidence
dipakai ulang hanya setelah seluruh11 assembly hashes cocok. **Tidak ada targeted tests,
clipboard audit, red-green, model generation atau full-suite run yang diulang.**

## Manifest dan readiness

Freeze directory: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-freeze\`.

- `source-manifest.json`: 651 paths, hash/bytes; SHA `55b6f16d2eb9b441066306fd9af94f15bb1ff5c4754ae053a3129457144732ca`.
- `frozen-input-manifest.json`: active/external/project graph; SHA `84f8c5373233642dcfdc9de21edee8ae6e245db4c20219fdc76a6423a24bfe80`.
- `complete-binary-manifest.json`, `managed-mvid-pdb-identity.json`, `fixture-manifest.json`:
  current binary/PDB/runtime/model identities; source/binary archives store these.
- `test-inventory.json`: exact cases/traits/assembly hashes; SHA `000f127f91202959da0137053c660989178352b82287d9d90c9833d7092b7164`.
- `execution-configuration.json`, `execution-plan.json`: exact commands, environment
  flags/fixtures, separate markers and immutable limits.
- `consistency.json`, `diagnostic-read-only-preflight.json`, `mandatory-read-only-preflight.json`:
  actual read-only validator PASS for both suites, **1236 protected handles**, isolated
  deny-write probe error32. Job membership empty after each preparation invocation.

Unchanged verified `gate_h_certification_job.py` SHA
`bf7c149ee5fa3b419755b20cbf0a23ec14b6bc710158cee9c012125e91088357`.
Only external wrapper delta: exact approval-review output filename, now this document,
to include all651 checkpoint inputs and avoid circular report/candidate hash. No change
to Job ownership, source/binary locks, first-failure cancellation, cleanup or classifier.
Updated external scripts are hashed as frozen inputs. This review is the sole new
repository output outside the651 frozen inputs.

Option B unchanged: original strict P1 assertions and failed TRX preserved; OutputLimit
never successful native call. All other tests, genuine F/G/H, protocol fail-closed,
approval/SafeWrite/recovery, WPF/OCR/ONNX/runtime and new reader regression remain mandatory.
Agent deadline120s, native cap256, Chat cap64, VSTest hang6m and wrapper wall3600s unchanged.

## Exact plan — only after explicit approval

**One P1 diagnostic first, then one mandatory invocation**, same candidate. Separate
identities `gate-h-clipboard-20261012-212144:diagnostic:1` and `:mandatory:1`.
If diagnostic has safety/host/cleanup blocker, stop before mandatory. Safely rejected
P1 model failure remains documented failed diagnostic under Option B.

Working directory: `D:\NEXUS\Nexus-CSharp`. Underlying commands:

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src/Nexus.sln" --no-build --no-restore -m:1 --settings "C:\Users\Syand\AppData\Local\Temp\opencode\environment-dotnet.runsettings" --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx --results-directory "C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-20261012-212144-evidence\diagnostic\full" --verbosity normal --filter "Category=GateHP1Reliability&FullyQualifiedName=Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat"
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src/Nexus.sln" --no-build --no-restore -m:1 --settings "C:\Users\Syand\AppData\Local\Temp\opencode\environment-dotnet.runsettings" --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type none --logger trx --results-directory "C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-20261012-212144-evidence\mandatory\full" --verbosity normal --filter "Category!=GateHP1Reliability"
```

Execution must use the verified wrapper, not direct unowned command launch:

```powershell
$env:NEXUS_GATE_H_FREEZE = "C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-freeze"
$env:NEXUS_GATE_H_SUITE = "diagnostic" # mandatory only after diagnostic permits continuation
python "C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_approved_preflight.py"
# only after approved preflight PASS:
python "C:\Users\Syand\AppData\Local\Temp\opencode\gate_h_instrumented_full.py"
```

All nine live flags=1 and original pinned fixture/model/engine paths are frozen in config;
preflight routes suite-specific evidence/visual folders. Exclusive run-started markers
prevent replay. `approval.template.json` stays false; no actual approval/run artifacts
created. Mandatory requires all1450 cases, all13 live fixtures, zero fail/skip/abort/missing/
deadline violation, safe cleanup and unchanged identities. If it fails, cancel invocation,
preserve actual first failure/TRX and continue authorized Continuous Recovery on working
copy; changed code needs new freeze/approval before another full certification.

## Evidence reuse, risks dan approval request

Accepted `GATE_H_CLIPBOARD_CONTINUOUS_RECOVERY_REPORT.md` establishes reader-isolation
recovery: RED→GREEN controlled regression, 77 unique targeted/impacted xUnit PASS,
12 ordered steps PASS and3 same STA cases PASS. It is **not full certification**.
Historical external actor remains unidentified; owned reader HWND does not make the
write-close→read-open gap atomic. Strict assertions/failure telemetry still catch changed
data. Production/CorelDRAW objective unchanged; actual CorelDRAW paste not certified.
Historical ONNX/transport limitations have no extra exemption.

**Approve candidate `gate-h-clipboard-20261012-212144`, SHA
`4b56ccf8d009f2f3542c48b49feabf5e7faef5e33fa375eb7f16383707b04184`, for
exactly one original P1 diagnostic and one mandatory full-suite invocation**, provided
diagnostic has no mandatory blocker. Concurrent agents must leave frozen inputs unchanged.

**STOP waiting for explicit approval. Gate H BLOCKED; Gate I HOLD.**
