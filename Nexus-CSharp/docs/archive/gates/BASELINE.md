# Nexus Baseline

**Current status (2026-10-01, after Gate A): PASS — 1,054 passed, 0 failed, 0 skipped, whole solution COMPLETE.** Cold/warm PDF policy was explicitly agreed. The original Phase 0/0.5 evidence below is retained; see §7 and [GATE_A_REPORT.md](GATE_A_REPORT.md) for the after-state.

Tanggal eksekusi: **2026-10-01**, Windows x64. Scope: working tree aktual `D:/NEXUS/Nexus-CSharp`, `src/Nexus.sln`, Debug. Source aplikasi tidak diedit untuk baseline. Lihat [ARCHITECTURE_AUDIT.md](ARCHITECTURE_AUDIT.md) dan [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## BASELINE

```text
Build: PASS (0 errors; 18 warning emissions)
Tests: FAIL / integration run ABORTED
Initial completed records: 551 passed, 4 failed, 0 skipped, total 555
Entire integration suite completed: NO
Startup: PASS (live WPF window + native exit)
Chat: PASS isolated VM/workflow tests + live workspace load;
      send/render/save live UI E2E NOT VERIFIED
AI CHECK: PASS isolated pipeline/run tests; live uploaded-document E2E NOT VERIFIED
Image: functional/native tests mostly PASS; initial triage performance gate FAIL
PDF: functional tests PASS; performance gate FAIL
Conversation History: module tests PASS; live UI persistence E2E NOT VERIFIED
Theme/UI: navigation/theme-sync smoke/tests PASS; design governance FAIL
Model Runtime: PASS live single-process health/model/completion/cleanup smoke
Configuration: config/settings tests PASS; default test OCR root failure recorded
Overall Existing Feature Compatibility Gate: FAIL / incomplete
```

PASS terisolasi tidak mengubah initial failed/aborted solution run menjadi PASS. NOT VERIFIED/UNKNOWN dipakai ketika tidak ada evidence; tidak dipaksa menjadi PASS atau diklaim sebagai defect fitur.

## 1. Environment and checkpoint

- SDK terbukti: `C:/Users/Syand/AppData/Local/Microsoft/dotnet/dotnet.exe`, **8.0.424**, MSBuild **17.11.48**, runtime .NET/WPF **8.0.30**.
- `dotnet` pada PATH menemukan `C:/Program Files/dotnet` runtime-only: output `No SDKs were found`. Perintah baseline memakai absolute SDK path. Tidak menginstal SDK/dependency baru.
- OS dari SDK: Windows 10.0.26300, win-x64. Tidak ditemukan global.json oleh SDK.
- HEAD awal `a530067`, branch `master`; branch audit `audit/nexus-agent-phase0-20261001` dibuat sebelum baseline. Working tree awal sudah staged/unstaged/untracked, termasuk app/config/image/color/test changes dan build outputs.
- Branch **bukan backup perubahan lokal**. Tidak commit/stash/reset/clean dan tidak menganggap HEAD baseline seluruh source. Tidak menghapus output/user work untuk memaksa clean build.
- Build ini incremental pada cache/output yang sudah ada. Signing hook existing `Directory.Build.targets` berjalan sebagai bagian Build, ContinueOnError=true. Restore/build/test menghasilkan `obj/bin` dan beberapa file itu sudah tracked oleh repo.

## 2. Restore and build commands

Dijalankan dari `D:/NEXUS/Nexus-CSharp`:

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" restore "src\Nexus.sln" --verbosity minimal "-flp:logfile=docs\phase0-restore.log;verbosity=normal"
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" build "src\Nexus.sln" --no-restore --configuration Debug --verbosity minimal "-flp:logfile=docs\phase0-build.log;verbosity=normal"
```

| Step | Exit | Result | Duration wall |
|---|---:|---|---:|
| Restore entire active solution | 0 | PASS, 22 projects restored | 6.3483546 s |
| Debug build entire active solution | 0 | PASS, 18 warnings, 0 errors | 25.0428051 s |

MSBuild build duration: 24.76 s. Raw evidence: `docs/phase0-restore.log`, `docs/phase0-build.log`. No Release/cold build performed. UI Pack/external net48 AIEngine/Phase5Runner/reference solutions are not in active solution and not included in these PASS claims.

### Existing warnings (18 emissions)

| Code | Count | Location / meaning |
|---|---:|---|
| CA1416 | 3 | LlamaServerManager.cs:604,606,608, WMI Windows-only from net8.0 module |
| CS8425 | 4 | AI.Llama.Tests/AiCheckJudgeServiceTests.cs:75,84,94,102, iterator token missing EnumeratorCancellation |
| CS0067 | 2 | ApplicationVisualPreferences.cs:9, event unused; emitted for WPF temporary project and App |
| CS8425 | 3 | Integration/AiCheckPipelineIntegrationTests.cs:928,937,947, iterator token attribute |
| CS8604 | 1 | Integration/ZzCausalIsolationDiagnosticTests.cs:139, possible null source |
| CS8625 | 2 | Integration/AiCheckStateWorkflowTests.cs:368,433, null to nonnullable |
| CS8602 | 1 | Integration/WindowChromePolishVerificationTests.cs:37, possible null dereference |
| CS8600 | 1 | Same test file:42, possible null assignment |
| xUnit2017 | 1 | Integration/ConversationViewModelTests.cs:722, Assert.False collection existence |

Warning count adalah observed incremental build, bukan klaim zero additional warnings pada clean rebuild semua outputs.

## 3. Initial entire-solution test attempt

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore --configuration Debug --logger trx --results-directory "docs\phase0-test-results" --blame-hang-timeout 3m --verbosity normal
```

Exit **1**, wall **71.1952936 s** (MSBuild test invocation 70.69 s). Test hosts berjalan concurrent sesuai existing runner defaults. Seluruh sebelas test project diattempt; integration test host crash sebelum seluruh test selesai.

Raw initial TRX berada di `docs/phase0-test-results/Syand_SYANDI-PC_2026-10-01_11_12_*.trx` (suffix `[1]`, `[2]` membedakan nama bersamaan).

| Project | Passed | Failed | Completed records | Outcome |
|---|---:|---:|---:|---|
| Nexus.Contracts.Tests | 7 | 0 | 7 | PASS |
| Nexus.Foundation.Tests | 35 | 0 | 35 | PASS |
| Nexus.Conversation.Tests | 16 | 0 | 16 | PASS |
| Nexus.AI.EditingLite.Tests | 28 | 0 | 28 | PASS |
| Nexus.FileUtilities.Tests | 19 | 0 | 19 | PASS |
| Nexus.AI.Llama.Tests | 174 | 0 | 174 | PASS |
| Nexus.Engine.ImageSubsystem.Tests | 122 | 1 | 123 | FAIL performance |
| Nexus.PdfUtilities.Tests | 17 | 2 | 19 | FAIL performance |
| Nexus.Core.HomeSettings.Tests | 24 | 0 | 24 | PASS |
| Nexus.Engine.ColorManagement.Tests | 10 | 0 | 10 | PASS |
| Nexus.Integration.Tests | 99 | 1 | 100 | ABORTED host crash |
| **Sum of completed records** | **551** | **4** | **555** | **FAIL / incomplete** |

### Initial failures

1. `ThreeTierTriageFilterTests.Gate2_Empirical_ThreeTierTriage_518ScaleAndExactMatchTrumpsNearMiss`: **202 ms**, required <100 ms (`ThreeTierTriageFilterTests.cs:139`).
2. `PdfEngineServiceTests.Benchmark_Docnet_DenseText_10Pages`: **136 ms**, required <100 ms (`:202`).
3. `PdfEngineServiceTests.Benchmark_Docnet_DenseText_50Pages`: **654 ms**, required <500 ms (`:245`).
4. `ArchitectureGovernanceTests.Phase5_sources_of_truth_and_contracts_remain_unique`: `AmbiguousMatchException` on reflection `GetMethod(GenerateMetadataAsync)` (`ArchitectureGovernanceTests.cs:274-277`).

### Host abort (separate from assertion counts)

```text
System.InvalidOperationException:
Cannot create more than one System.Windows.Application instance in the same AppDomain.
at Nexus.App.App..ctor() App.xaml.cs:45
at Nexus.Integration.Tests.WpfAppHost... WpfAppHost.cs:71
```

Blame sequence: `docs/phase0-test-results/559d8689-96c0-47d5-867a-ad77a14eb114/Sequence_c79ab72365be462bbcb82e9e5e0f8668.xml`. Concurrent active-test list is not proof of which test caused the crash. Source confirms multiple test classes own independent STA App creation in addition to WpfAppHost. Integration coverage remaining after abort is UNKNOWN.

## 4. Targeted diagnostic rechecks

No test source or production code changed. These rechecks were justified by performance failures and aborted coverage, and retain the initial results.

### Performance isolation

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\02_Modules\Nexus.PdfUtilities.Tests\Nexus.PdfUtilities.Tests.csproj" --no-build --no-restore --filter "FullyQualifiedName~Benchmark_Docnet_DenseText" --logger "trx;LogFileName=pdf-isolated.trx" --results-directory "docs\phase0-test-results" --verbosity minimal
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\02_Modules\Nexus.Engine.ImageSubsystem.Tests\Nexus.Engine.ImageSubsystem.Tests.csproj" --no-build --no-restore --filter "FullyQualifiedName~Gate2_Empirical_ThreeTierTriage" --logger "trx;LogFileName=triage-isolated.trx" --results-directory "docs\phase0-test-results" --verbosity minimal
```

| Case | Initial | Isolated | Conclusion |
|---|---|---|---|
| PDF dense10 | FAIL 136 ms | FAIL 109 ms | gate still fails, no Agent changes |
| PDF dense50 | FAIL 654 ms | PASS 319 ms | load-sensitive timing; initial FAIL remains |
| Triage | FAIL 202 ms | PASS 28 ms | load-sensitive timing; not enough samples for causal proof/P95 |

Isolated performance totals: 2 passed, 1 failed (3 results); not unique additional tests.

### Feature/regression isolation

Each class below executed in a fresh test process. Exact command template:

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\03_App\Nexus.Integration.Tests\Nexus.Integration.Tests.csproj" --no-build --no-restore --filter "FullyQualifiedName~Nexus.Integration.Tests.<Class>." --logger "trx;LogFileName=isolated-<Class>.trx" --results-directory "docs\phase0-test-results" --blame-hang-timeout 90s --verbosity minimal
```

| Class | Pass | Fail | Scope |
|---|---:|---:|---|
| ConversationRuntimeWorkflowTests | 5 | 0 | same-model reuse/model switch/runtime workflow |
| ConversationViewModelTests | 145 | 0 | Chat generation/actions/history/context/cancellation with test doubles |
| AiCheckPipelineIntegrationTests | 21 | 0 | deterministic evidence/judge/mapping scenarios |
| AiCheckRunIsolationTests | 21 | 0 | run reference state isolation |
| PdfUtilitiesViewModelTests | 4 | 0 | PDF workflow UI state |
| ImageSubsystemWorkflowTests | 1 | 0 | native-output scale transform |
| NavigationLifecycleTests | 4 | 0 | navigation ownership/lifecycle |
| MainViewModelThemeSyncTests | 2 | 0 | theme/config sync |
| DesignSystemGovernanceTests | 7 | 3 | token/style governance |
| SettingsViewModelTests | 25 | 0 | settings UI/config behavior |
| ArchitectureGovernanceTests | 6 | 3 | DI/contracts/pipeline boundaries |
| **Isolated results** | **241** | **6** | **247; not merged with initial totals** |

Additional existing failures exposed by isolated runs:

- DesignSystem `Existing_design_system_debt_is_visible_and_finite`: expected **133**, actual **137**, source line 272.
- DesignSystem `Module_views_do_not_consume_primitive_tokens_or_define_common_templates` and `Primitive_token_references_are_limited_to_design_system_layers_or_the_legacy_baseline`: ConversationView primitive-token violations, lines 186/194.
- Architecture `Pipelines_do_not_access_physical_paths_or_UI`: `ResolvedModelDescriptor.get_ModelPath` usage violates assertion, line 119.
- Architecture `Production_graph_excludes_legacy_conversation_workflow`: DI resolution fails first on missing PP-OCRv6 under test output default `Models/AI Workspace/PP-OCRv6`. This does **not** prove legacy workflow is registered; production factory explicitly supplies current workflow.
- Architecture uniqueness reflection failure reproduces unchanged.

All assertion messages and stacks preserved in `isolated-DesignSystemGovernanceTests.trx`, `isolated-ArchitectureGovernanceTests.trx`. Some native test fixtures return normally if assets missing, counted Passed instead of Skipped; review test bodies before using totals as E2E confidence.

## 5. Live runtime / WPF smoke inventory

Launch tested directly using existing DLL:

```powershell
Start-Process -FilePath "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" -ArgumentList '"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows\Nexus.App.dll"' -WorkingDirectory "D:\NEXUS" -PassThru
```

Native Windows UI Automation (`UIAutomationClient`/`UIAutomationTypes`) used to invoke existing WPF buttons. No new application/automation dependency installed. Initial process/port check found no llama-server/listener; app host PID **12652**.

| Action | Actual observation | Status |
|---|---|---|
| Startup (8-second sample) | alive, window title Nexus, nonzero window handle, Responding=True | PASS smoke; precise startup latency unmeasured |
| Native shell controls | Home/AI Workspace/Image/PDF/FileManagement/Settings nav buttons present/enabled | PASS discovery |
| AI Workspace navigate | Memuat model AI, one llama PID 20644; initial /health 503 during load | expected loading |
| Runtime ready | /health `{status:ok}`, /props.model_path matches active GGUF | PASS |
| Completion probe | direct same server `/v1/chat/completions`, `Reply with OK only.`, max_tokens=16, temperature=0, thinking=false -> `OK`, stop, **1787 ms** | PASS inference smoke |
| PDF navigate | Responding=True, title Nexus | PASS navigation; live conversion not executed |
| Image navigate | Responding=True, title Nexus | PASS navigation; live image save not executed |
| Settings -> Home | both respond, no crash observed | PASS navigation |
| Leave AI Workspace | llama count becomes 0 | PASS existing navigation cleanup |
| Exit BtnClose -> DialogPrimaryButton | native confirmation name Keluar, process exits | PASS |
| Post-exit cleanup | app test PID absent, llama count 0 | PASS |

Actual configuration source: `src/03_App/Nexus.App/bin/Debug/net8.0-windows/nexus_data/config.json`; model/engine roots `D:/NEXUS/Models`, `D:/NEXUS/Engine`, selected Light theme. Model catalog `D:/NEXUS/Models/AI Workspace/model-index.json`: active `gemma-4-e4b` (Qwen also catalogued). Engine resolved `D:/NEXUS/Engine/LlamaServer/b11057/llama-server.exe`, backend CPU, server listen **127.0.0.1:8080**.

Observed server WorkingSet during loading: **5,381,537,792 bytes**; no steady-state memory/P95 claim. Actual startup server event evidence (copied here because runtime log is overwritten on next startup):

```text
Started at: 2026-10-01 11.17.21.145 (local)
[04.17.42.728] INIT EngineDir=D:\NEXUS\Engine\LlamaServer\b11057 Port=8080 Thread=1
[04.17.42.733] [019BDD19] START_REQUESTED Model=...Gemma-4-E4B\gemma-4-E4B-it-Q4_K_M.gguf
[04.17.42.812] [019BDD19] BACKEND_DETECTED cpu
[04.17.42.833] [019BDD19] PROCESS_STARTED +99ms PID=20644
[04.18.04.988] [019BDD19] HEALTH_CHECK_PASS +22254ms Attempt=44 Latency=13ms
[04.18.04.989] [019BDD19] SERVER_READY +22255ms
[04.19.09.514] [019BDD19] STOP_REQUESTED +86780ms
[04.19.09.532] [019BDD19] PROCESS_EXITED ExitCode=-1 Reason=UserShutdown Expected=True
[04.19.09.533] [019BDD19] STOP_COMPLETE State=Stopped
[04.19.18.722] STOP_SKIP_ALREADY_STOPPED
[04.19.18.755] STOP_SKIP_ALREADY_STOPPED
```

Runtime source log: `src/03_App/Nexus.App/bin/Debug/net8.0-windows/server_lifecycle.txt`. UTC event timestamps differ from local header (+07:00); not a second startup. Process count observed at samples, not continuous multi-instance/race proof.

### Regression inventory and outstanding E2E checks

| Feature | Proven evidence | Remaining to close hard gate |
|---|---|---|
| Chat | isolated 145+5 tests, production source path, live lazy model load | live send/stream/render/save/reopen, cancel-midstream, edit/regenerate, latency under concurrent Agent |
| AI CHECK | isolated 42 tests; Judge/transport unit tests; OCR native tests in solution | real uploaded design through live UI to same PID; cancel/switch; judgment baseline |
| Image tools | 122 initial image tests pass incl native inpaint/OCR durations; UI navigation | representative removeBG/upscale/inpaint output hash/dimensions/save, cancellation/CMYK, memory under load |
| PDF | 17 functional/perf tests pass, UI navigation, 4 VM tests | resolve dense10 timing gate; representative live render/convert/merge/split/source preservation |
| History | 16 conversation tests + Chat VM tests | actual save/reopen/temporary/delete + detail/index integrity on concurrent writes |
| Theme/UI | native shell/nav/exit, 2 theme-sync tests | resolve design governance; light/dark/high contrast, DPI/focus, long-history virtualization/perf |
| Model runtime | one owned PID, verified model, live response, no process after exit | same-model reuse, switch-under-generation, foreign port, cancellation, Agent contention |
| Configuration | Foundation/HomeSettings/Settings VM tests, actual roots usable | isolated test fixture root correction; missing/corrupt roots startup without unrelated failure |
| Startup | live shell smoke | model missing/OCR missing/config missing scenarios and reproducible whole integration harness |

No live Chat history was intentionally added by the completion probe (HTTP inference only). Application/test initialization can create normal config/reference/conversation-index/log files according to existing code. UI responsiveness checks use Process.Responding; they do not certify 60 FPS, visual correctness, or precise Dispatcher latency.

## 6. Baseline gate decision / phase report

PHASE: 0.5. Evidence: build logs, initial 11 TRX, isolated rechecks, native UI/runtime smoke.

Components reused: all existing service/test paths. Production components added/refactored: none. Dependencies changed: none. New files: three requested documents plus execution logs/TRX/blame artifacts.

Build: **PASS**. Tests: **FAIL / ABORTED**, with subsets PASS. Regression Gate: **FAIL / incomplete**. Existing errors listed above are pre-Agent working-tree baseline errors; not automatically classified as regressions against earlier commits.

Performance impact: only verification/build/model loading workload; measured baseline figures above. Security impact: findings documented; no permissions/tool exposure added. Known issues: WPF host collision, governance failures, model fixture coupling, performance gates, unverified live E2E.

Checkpoint: audit branch created, working tree preserved, no commit. Rollback of documentation is scoped removal of authored documents/artifacts; no broad git reset/clean to remove user work. Native smoke closed its process through existing UI and confirmed cleanup.

Next recommended phase: review audit, fix baseline blockers surgically, rerun whole solution until complete, execute missing live E2E inventory. **No Phase 1 automatically started.**

## 7. Gate A after-state — 2026-10-01

This dated addendum supersedes status for the current working tree; it does not erase initial failures/aborts.

```text
Restore: PASS
Build: PASS, 0 errors, 2 warning emissions on final incremental build (14.90 s)
Whole Nexus.sln tests: COMPLETE / PASS
Passed: 1,054
Failed: 0
Skipped: 0
Wall duration: 538.0027499 s
Integration project: 597 passed, 0 failed, 0 skipped
Repeated integration: full-5-live and full-6-live both COMPLETE/PASS 597
Host crashes / duplicate Application / STA/Dispatcher abort: none in final runs
Existing Feature Compatibility: PASS for representative live inventory
Phase 1 / Gate B: NOT AUTHORIZED; no Agent implementation started
```

Final evidence: `docs/gate-a-results/full-6-live/*.trx`, `docs/gate-a-build.log`, `docs/gate-a-restore.log`. All eleven test projects run, including two live opt-in facts with real fixtures. Existing warning debt is not considered fixed because incremental compilation skipped unchanged sources.

Changes and discoveries are detailed in GATE_A_REPORT: one WPF test owner/scoped resources, explicit reflection signature, fallback discovery on existing selection policy, deferred OCR availability, resource/template migration, assembly-qualified PDF InfoBar URI, isolated smoke config, first-send sidebar metadata publication, per-view mode grouping, meaningful regression/fixture tests.

Live compatibility after-state:

- Chat: send/observed stream/render/save/reopen/edit/regenerate/cancel PASS.
- History: sidebar first-send row, temporary no-detail, delete, index/detail integrity PASS in isolated existing store.
- AI CHECK: real uploaded design -> 67 OCR boxes -> evidence -> 1 actual Judge HTTP call -> 6 mapped findings; same existing PID, cancellation PASS.
- Image: PhotosSeg remove background, RealESR requested 2x, LaMa inpaint/save/cancel PASS; PNG dimensions/hashes recorded; source preserved.
- PDF: 3-page source render/text/3 PNG convert/6-page merge/6 readable split PDFs PASS; source hash preserved.
- Theme/UI: native navigation, Light/Dark/HighContrast resource/render checks, keyboard focus/disabled state, 96/144 DPI captures PASS at tested boundary. Full interactive visual matrix and real multi-monitor DPI switching not claimed.
- Model: same-model PID reuse, Qwen/Gemma switch/restore, cancel, navigation away/back, foreign occupied port preserved, no alive duplicate/orphan server PASS.
- Startup: missing/corrupt OCR fixture shell resolve + explicit inspection error PASS; real OCR available PASS. Final standalone executable shell/PDF navigation/native exit PASS.

Performance policy explicitly accepted by user: separate cold and warm; retain cold failures as profiled baseline exception, use 3 warm-up reads + 20 measured samples, original warm thresholds unchanged. Final warm P95: dense10 28.369 ms (<100), dense50 139.823 ms (<500), real-world PDF 26.594 ms (<100), triage 30.373 ms (<100). OCR keeps its original 6,500 ms inference gate; timing collections isolated from concurrent native workloads.

Current checkpoint: pre-edit source archive (954 files + raw index + exact patches + hashes) and separate tracked-generated archive; SHA/path/recovery instructions in GATE_A_REPORT §2. Staged patch remains byte-identical to pre-edit user staging. Only source files listed in that report differ against the source checkpoint; ordinary HEAD diff still includes preexisting user work. No commit/stash/reset/clean.

Next: review Gate A report and then decide whether to authorize Gate B. **STOP at Gate A.**

Post-validation application/test source freeze: `C:/Users/Syand/AppData/Local/Temp/opencode/nexus-gate-a-final-source-20261001-145814.zip`, SHA-256 `3f04b51a56818dc87bdd89176e492fe19af6bee10693455ddd23a2fe55ee7d26`. This receipt was added after the archive; application/test source remains exactly the validated snapshot. Generated execution evidence is retained separately under `docs/gate-a-results/`.
