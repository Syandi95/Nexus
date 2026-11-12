# Nexus — Gate A Baseline Repair Report

Tanggal: **2026-10-01**. Status: **Gate A PASS untuk solution aktif dan fixtures yang tercatat**, dengan exception cold-start PDF yang disetujui secara eksplisit. **Phase 1 / Gate B belum diotorisasi dan tidak dimulai.**

Evidence awal tetap di [ARCHITECTURE_AUDIT.md](ARCHITECTURE_AUDIT.md) dan bagian awal [BASELINE.md](BASELINE.md). Dokumen audit history tidak diedit. Semua path source di sini relatif terhadap `Nexus-CSharp/`.

## 1. Acceptance summary

| Gate | Result | Evidence |
|---|---|---|
| Restore | PASS | `docs/gate-a-restore.log` |
| Build seluruh `src/Nexus.sln` | PASS, 0 errors | `docs/gate-a-build.log`, MSBuild 14.90 s |
| Whole solution test | **COMPLETE / PASS: 1,054 passed, 0 failed, 0 skipped** | 11 TRX di `docs/gate-a-results/full-6-live/` |
| WPF host ownership | PASS | satu Application/STA Dispatcher, scoped resources; integration COMPLETE |
| Repeated integration completion | PASS | `full-5-live`: 597/597; `full-6-live`: 597/597; tanpa abort |
| Governance blockers | RESOLVED | architecture/design/resource/startup tests dalam full run |
| Representative live E2E | VERIFIED | dua live facts dieksekusi (bukan skipped), real HTTP/ONNX/PDF/WPF |
| Shared LLM/runtime | VERIFIED | one live PID per stage, same-model reuse, model identity, switch/cleanup |
| Compatibility | PASS pada inventory representatif di §6 | fixtures + hasil native + standalone startup/exit |
| Source/user-work recovery | VERIFIED checkpoint | arsip pre-edit + staged patch identik; scoped changes inventory |

Durasi test seluruh solution: **538.0027499 s** wall, sekitar 8 menit 58 detik. Integration project sendiri sekitar 7 menit 20 detik. Ini workload native/benchmark/live, bukan UI startup SLA.

## 2. Recoverable checkpoint before source edits

HEAD: `a530067973db2847d8c0028c6e17b0242e0ccabd`.
Branch: `audit/nexus-agent-phase0-20261001`.

Pre-edit read-only archives:

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-a-source-20261001-114200.zip
SHA-256: f4e426690d330d6cd81f04fef83c86347c076d222ed74d62b555e875e3a31988

C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-a-tracked-generated-20261001-114200.zip
```

- Source archive: **954 non-build-output files**, raw Git index, exact staged/unstaged binary patches, full status, manifest dengan setiap file SHA-256/size, HEAD/branch, staged/unstaged/untracked-source lists.
- Generated archive: **118 tracked generated files** dipisahkan dari source; untracked `bin*`, `obj`, `.vs`, TestResults dicatat sebagai excluded generated paths.
- ZIP CRC dan seluruh archived source hashes diverifikasi sebelum source diedit. Arsip dibuat exclusive-create dan read-only; checksum harus diperiksa sebelum pemulihan.
- Tiga staged source files user tetap sama: `MarkdownPlainText.cs`, `NexusMessageParser.cs`, `MessageContentPresenter.cs`. **Archived staged patch byte-identical dengan `git diff --cached --binary` setelah Gate A.** Raw index bytes dapat berubah karena Git stat-cache refresh; perubahan itu tidak berarti staging blobs berubah.
- Tidak melakukan commit, stash, reset, clean, atau discard untracked work. Seluruh preexisting source selain daftar Gate A di §4 hash-identical terhadap checkpoint sebelum document addenda.

Pemulihan harus scoped: ambil `working-tree/<path>` dari pre-edit archive **hanya untuk file yang disentuh Gate A**, setelah memeriksa apakah ada edit baru. Hapus file Gate A baru hanya jika tidak menerima perubahan user berikutnya. Raw index/staged patch disimpan untuk pemulihan staging bila diperlukan; jangan menimpa index modern secara blanket. Build/test artifacts terpisah dan tidak dipakai untuk mengganti source.

Post-validation source freeze (963 files, application/test source exact; this archive receipt was appended afterward):

```text
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-a-final-source-20261001-145814.zip
SHA-256: 3f04b51a56818dc87bdd89176e492fe19af6bee10693455ddd23a2fe55ee7d26
C:\Users\Syand\AppData\Local\Temp\opencode\nexus-gate-a-final-tracked-generated-20261001-145814.zip
```

Full execution evidence remains separately in `docs/gate-a-results/`; it is excluded from the final **source** ZIP to keep source and generated native/test artifacts distinct. Both final archives are read-only and ZIP/source hashes were verified. No source edits followed the successful full run; only report/checkpoint receipts were completed.

## 3. Problems fixed — evidence and decisions

### A. Application / Dispatcher ownership dan state isolation

`WpfAppHost` sebelumnya membuat App sendiri sementara tiga custom helpers juga membuat App pada short-lived STA threads. Helpers CleanDownloads/FileManagement/Rapikan sekarang memarshal ke host yang sama. Initialization error diteruskan melalui TaskCompletionSource dengan timeout; stack exception dipertahankan.

Integration collections serial karena Application resources/theme/clipboard merupakan process-global state. Resource dictionaries dibuat ulang dari canonical compiled sources per scoped UI action, dengan BeginInit/EndInit restoration; ini **bukan** membuat Application kedua. Test async yang mengubah grouped WPF history memakai host Dispatcher selama construct/await/assert. Tidak mengaktifkan parallel writes atau production synchronization framework.

Production crash-dialog handler dilepas **hanya pada test host**. Exception Dispatcher dicatat untuk menjadi assertion failure pada hosted action, sehingga dialog tidak menyembunyikan failure atau membuat run menunggu interaksi manusia. Production exception handling tidak diganti.

### B. Reflection governance

Governance test memilih `OnnxModelManager.GenerateMetadataAsync(string, string, AICapability?)` secara eksplisit. Overload dua argumen adalah forwarding compatibility method; parser/write/parse contract yang ingin diuji ada pada overload tiga argumen. Production API dipertahankan; assertion tentang metadata parser tidak dihapus.

### C. Physical model path boundary

Caller nyata adalah fallback discovery `RemoveBackgroundPipeline.TryResolveMvanetFallback`: membaca descriptor.ModelPath, mencari sibling metadata, lalu fallback ke path developer hardcoded. Itu discovery/model selection, bukan tensor pipeline responsibility.

Discovery dipindahkan ke **existing `ImageApplicationModelSelectionPolicy`**, menggunakan package sibling dan configured models root. Pipeline menerima optional descriptor resolver dan tetap menjalankan single PhotosSeg → MVANet fallback. Rule governance `get_ModelPath` tetap aktif; normalisasi/tensor/output semantics tidak diganti. Existing fallback test tetap menguji execution dengan descriptor fixture yang eksplisit.

### D. OCR availability vs shell startup

PpOcrEngine tetap DI-owned singleton. AiCheckViewModel menerima deferred factory dari production registration, sehingga native engine baru di-resolve dalam OCR background operation. Missing/corrupt OCR tidak perlu menggagalkan shell/Chat construction. Existing injected-engine constructor behavior tetap didukung.

Tests membuat **explicit temporary model/config/reference roots** untuk missing dan corrupt cases; shell/Chat resolve, inspection berakhir dengan `Pemeriksaan gagal`, bukan sukses palsu. Available case dibuktikan dengan real model root pada live E2E. Tidak mengganti prompts, G3/R3D thresholds, candidate identity/order, batching, atau judgment parser.

### E. Design/resource governance

Selisih 133 → 137 bukan empat token tambahan saja:

| Direct module references | Change sebelum repair |
|---|---:|
| Conversation focus `NexusStrokeStrong` | +1 |
| Conversation font family | +1 |
| Conversation default border references | +5 |
| Image standalone glyph font references | +4 |
| Previously removed radius/body/caption/code/small-icon references | -7 |
| **Net primitive-pattern debt change** | **+4** |

Sebelas direct references baru dimigrasikan melalui semantic content border/font/glyph roles atau component styles. Allowlist direduksi untuk tujuh references yang memang sudah hilang; strict expected debt menjadi **126**, bukan menaikkan angka ke 137. Dua pill styles dan dua round action templates dipusatkan pada existing Buttons dictionary. Geometry/commands tetap sama.

Sembilan literals module baru dipromosikan ke semantic/component resources. Lima primitive ARGB values merupakan **nilai existing yang dipindahkan**, bukan palette baru. Dark/Light mempertahankan nilai existing; HighContrast memakai SystemColors untuk roles baru. Existing baseline hardcoded debt kembali **137**, module-named styles **7**, common control templates **1**. Detail ada pada migration-baseline addendum.

Compiled-BAML regression menangkap alias FontFamily yang menghilangkan legacy key selama implementasi. Final resources memakai distinct keyed FontFamily instances dengan Source string yang sama dan tetap mempertahankan canonical keys. Regression membuktikan legacy glyph key dan semantic glyph key resolve dengan nilai identik. Font issue intermediate tidak dianggap PASS dan diperbaiki sebelum final run.

### F. Failures exposed after full completion

- WorkspaceTabStyle test loader memerlukan BaseUri ketika membuka source XAML yang mempunyai relative merged dictionaries; loader diperbaiki tanpa stripping dependency.
- ZzCausal probes mengharapkan NRE pada renderer tanpa App resources. Inputs dipertahankan sebagai regression pada real WPF host: assignment/render setup tidak throw. Renderer production tidak diberi dummy fallback untuk meloloskan probe.
- Clipboard assertion reads mempunyai bounded retry hanya untuk CLIPBRD_E_CANT_OPEN, sesuai failure Win32 transient yang teramati; assertions tetap memeriksa copied text yang benar.
- Scanner visual matrix memilih Dark palette secara eksplisit karena calibrated blue-channel/luminance assertion bukan palette-independent. Threshold tidak diturunkan.
- Window chrome hit-test diagnostic menunjukkan native window dengan uninitialized VM. DataContext dilepas untuk probe chrome-only agar Loaded/Closing tidak memakai SettingsVM yang tidak ada.
- IntegrationSmoke memakai isolated config file, sehingga default-theme assertion tidak bergantung pada config yang ditulis test/user sebelumnya.

### G. PDF InfoBar resource crash loop

Full run mengungkap XamlParseException berulang saat PDF template mencoba `/Controls/InfoBar/InfoBar.xaml`. Pada testhost sebagai entry assembly, resource itu tidak ada. Production view sekarang memakai `/Nexus.App;component/Controls/InfoBar/InfoBar.xaml`; ini benar untuk desktop executable dan assembly-hosted WPF.

TRX `full-1` serta Windows AppHang evidence menunjukkan resource exception loop, bukan bukti ONNX threading crash walaupun benchmark sedang aktif. URI qualified, rendered PDF navigation, dan test-host exception reporting menutup blocker tersebut.

### H. First-send history sidebar

Real live E2E menemukan first send otomatis membuat dan menyimpan conversation, tetapi current sidebar belum menerima row tersebut. Setelah successful persistence, VM sekarang memperbarui **metadata summary saja** pada Dispatcher (id/title/time/count/selection). Tidak menjalankan history reload yang mengganti message tree, tidak menulis database baru, dan tidak menambah inference call.

Regression memeriksa row muncul, dua messages tetap utuh, dan tidak memanggil OpenConversationAsync. Enam async UI/history tests yang sebelumnya membuat grouped CollectionView pada MTA dipindahkan ke existing STA host; assertions edit/regenerate/attachments tidak dilemahkan.

### I. RadioButton grouping stack overflow

Repeated suite membuka stack overflow pada `RadioButton.UpdateRadioButtonGroup` → TwoWay `IsChatModeActive`. Global GroupName `WorkspaceModeGroup` menggabungkan beberapa detached ConversationView instances.

Kedua buttons adalah siblings dalam UniformGrid, sehingga group native berbasis logical parent cukup. Dua GroupName attributes dihapus; exclusivity tetap scoped ke pair pada view yang sama. Regression dua workspace membuktikan mode masing-masing independen; label/binding test tetap memeriksa casing, TwoWay binding, dan sibling grouping.

## 4. Files changed relative to pre-edit checkpoint

**14 existing production files**, chiefly resource migration and small existing-feature fixes:

```text
src/02_Modules/Nexus.Engine.ImageSubsystem/Pipelines/RemoveBackgroundPipeline.cs
src/03_App/Nexus.App/App.xaml.cs
src/03_App/Nexus.App/Services/ImageApplicationModelSelectionPolicy.cs
src/03_App/Nexus.App/ViewModels/AiCheckViewModel.cs
src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs
src/03_App/Nexus.App/Themes/Components/Buttons.xaml
src/03_App/Nexus.App/Themes/Semantic/DarkSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Semantic/LightSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Semantic/HighContrastSemanticTheme.xaml
src/03_App/Nexus.App/Themes/Tokens/PrimitiveTokens.xaml
src/03_App/Nexus.App/Themes/Tokens/SharedMetrics.xaml
src/03_App/Nexus.App/Views/ConversationView.xaml
src/03_App/Nexus.App/Views/ImageSubsystemView.xaml
src/03_App/Nexus.App/Views/PdfUtilitiesView.xaml
```

**18 existing test files**:

```text
src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/PpOcrEngineTests.cs
src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/RemoveBackgroundPipelineTests.cs
src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/ThreeTierTriageFilterTests.cs
src/02_Modules/Nexus.PdfUtilities.Tests/PdfEngineServiceTests.cs
src/03_App/Nexus.Integration.Tests/AIWorkspaceRenderingTests.cs
src/03_App/Nexus.Integration.Tests/ArchitectureGovernanceTests.cs
src/03_App/Nexus.Integration.Tests/CleanDownloadsVisualCorrectionTests.cs
src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs
src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs
src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs
src/03_App/Nexus.Integration.Tests/ImageProcessingScannerAnimationVerificationTests.cs
src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs
src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs
src/03_App/Nexus.Integration.Tests/RapikanFileSemanticTypographyTests.cs
src/03_App/Nexus.Integration.Tests/WindowChromePolishVerificationTests.cs
src/03_App/Nexus.Integration.Tests/WorkspaceTabStyleTests.cs
src/03_App/Nexus.Integration.Tests/WpfAppHost.cs
src/03_App/Nexus.Integration.Tests/ZzCausalIsolationDiagnosticTests.cs
```

**Five new test files**: ImageSubsystem.Tests/TimingCollection.cs, PdfUtilities.Tests/TimingCollection.cs, Integration.Tests/AssemblyInfo.cs, Integration.Tests/OcrStartupAvailabilityTests.cs, Integration.Tests/GateALiveCompatibilityTests.cs (all under the project paths above).

Documents: BASELINE.md and IMPLEMENTATION_PLAN.md addenda, new GATE_A_REPORT.md, design-system/migration-baseline.md addendum. Execution artifacts remain under `docs/gate-a-results/` and gate-a build/restore logs. Local checkpoint/statistics utilities remain in approved Temp/opencode, not production.

**Dependencies/projects added or removed: none.** LlamaServerManager/GenericLlamaEngine, Chat/Judge transport, PDF engine stack, ONNX execution stack, conversation storage implementations, config schemas, App.xaml/main shell and message renderer remain the existing implementations.

## 5. Full test result and repeated completion

Final command (fixture environment in §8):

```powershell
& "C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe" test "src\Nexus.sln" --no-build --no-restore -m:1 --logger trx --results-directory "docs\gate-a-results\full-6-live" --blame-hang-timeout 5m --blame-hang-dump-type none --verbosity minimal
```

`-m:1` prevents test projects with native CPU workloads from contending with each other. WPF integration collections are serial; empirical CPU/PDF timing collections also exclude simultaneous collections within their process. This does not change production concurrency.

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Contracts | 7 | 0 | 0 |
| Foundation | 35 | 0 | 0 |
| Conversation | 16 | 0 | 0 |
| EditingLite | 28 | 0 | 0 |
| FileUtilities | 19 | 0 | 0 |
| AI.Llama | 174 | 0 | 0 |
| ImageSubsystem | 123 | 0 | 0 |
| PdfUtilities | 21 | 0 | 0 |
| HomeSettings | 24 | 0 | 0 |
| Integration | 597 | 0 | 0 |
| ColorManagement | 10 | 0 | 0 |
| **Total** | **1,054** | **0** | **0** |

Before/after attempts are preserved rather than replaced:

- Phase 0: 551 pass/4 fail completed records, integration aborted.
- Host repair run 2: integration COMPLETE 579 pass/12 fail (591 records), exposing hidden assertions.
- Full run 1: resource crash loop / aborted integration, real-world PDF cold spike.
- Full run 2: COMPLETE, two test fixture/resource failures.
- Full run 3 live: live facts PASS; six headless CollectionView-ownership regressions then corrected on STA.
- Full run 4: WPF radio grouping stack overflow, explicitly corrected with regression.
- Full run 5 live: **integration 597 PASS / COMPLETE**; one OCR timing failure 6,525 ms under concurrent native collections.
- Full run 6 live: **whole solution PASS / COMPLETE**, integration 597 PASS again after grouping fix. OCR collection isolation retained original 6,500 ms gate.

Final incremental build emitted **2 CS0067 warnings** (App and WPF temporary compilation), 0 errors. Earlier significant rebuild emitted 11 warnings; initial Phase 0 emitted 18. Existing CA1416/CS8425/nullability/xUnit warnings are not claimed removed merely because incremental compilation did not emit them again. No clean-cache/Release build claim.

## 6. Live compatibility E2E

Genuine compiled WPF shell/VMs, production service registrations, shared HTTP transport, real GGUF/ONNX, and existing PDF services were used. Only test config/reference/history output roots were isolated. Fake model/Judge responses were not used in these two live facts.

Latest artifacts:

```text
docs/gate-a-results/runtime-20261001-143109/runtime.json
docs/gate-a-results/artifacts-20261001-143336/artifacts.json
docs/gate-a-results/artifacts-20261001-143336/theme-{Light,Dark,HighContrast}-{96,144}.png
```

| Feature | Proven live checks |
|---|---|
| Chat | send, observed intermediate stream, nonempty answer, RenderedDocument, bound shell layout, detail save parity, reopen, regenerate, edited resend, cancel-midstream |
| History | first-send sidebar row; unchanged pair count after regenerate/edit; temporary chat has no detail file; delete removes detail/index; every remaining index entry loads a detail |
| AI CHECK | upload through LoadFileDirectlyAsync, actual OCR/evidence/Judge/mapping: **67 boxes, 6 findings, 1 Judge HTTP call**, same PID; cancellation clears run state/findings |
| Model/runtime | PID 15512 same-model reuse and Chat/Judge; switch to Qwen PID 22612; restore Gemma PID 19872; navigation away stops owned runtime, back loads PID 18060; all stages require exactly one **alive** server |
| Foreign port | occupied listener preserved; manager start fails (InvalidOperationException), failed process cleaned, OwnedPid null, no alive server; native executable bind failure is the observed safety path, not proof WMI preflight always detects foreign ownership |
| Image | native PhotosSeg remove background, RealESR upscale requested 2x, LaMa FastSinglePass inpaint, PNG save/decode/dimensions/hashes, cancelled operation produces no output; source image SHA-256 unchanged |
| PDF | actual 3-page sample: render preview, text extraction, 3 PNG conversions, merge to 6 pages, split to 6 readable one-page PDFs; source SHA-256 unchanged |
| Theme/UI | Light/Dark/HighContrast, semantic resource type checks, module navigation, real keyboard focus, enabled/disabled control state, render at 96 and 144 DPI; isolated mode-selector exclusivity regression |
| Startup/exit | final standalone Nexus.App DLL launch PID 17660: title Nexus/Responding=True; native PDF navigation responds; native close confirmation exits; zero llama process afterward |
| OCR availability | explicit missing/corrupt model fixtures permit shell/Chat resolve and show inspection error; real available model executes uploaded design |

Image outputs recorded: removed 128×128 / 15,957 bytes, upscaled 256×256 / 121,190 bytes, inpaint 128×128 / 11,022 bytes; full SHA-256 in artifacts.json. PDF source hash `83A68983203E51467B9FFAF42C1A6728BD7A9C5A2425EB8E26ACC2742020BDAB`. Source design preserved by before/after hash assertion.

Final live first Chat send/stream/render/save: **12,640 ms** at max_tokens 64; initial server readiness **22,220 ms**. This differs from Phase 0's direct 16-token `OK` probe; those latencies are not normalized comparison or a Chat P95/SLA.

Scope limits: Image/PDF core operations were driven through existing service APIs with native WPF navigation, not OS file-picker automation for every step. Chat/AI CHECK use actual bound VM/application execution paths. DPI evidence is 96/144 RenderTargetBitmap plus native focus/navigation, not OS multi-monitor scale switching. Hover/pressed/selected resource/template contracts are covered by existing governance/control tests; a complete interactive visual-state matrix/high-DPI hardware campaign was not claimed. Long-history virtualization/performance remains a separately recorded risk, not a new Gate A condition.

## 7. Performance findings and agreed baseline policy

Hardware observed: Intel Core i5-10400, **6 physical / 12 logical cores**, approximately 32 GB RAM, CPU inference. Native startup, tiered JIT, page initialization, disk/OS cache and unrelated native test CPU load affect cold timing. No production PDF optimization or engine/library replacement was performed.

### Repeated original test measurements (before policy changes)

| Workload | n | Median ms | Mean ms | Min / max ms | Sample std deviation ms |
|---|---:|---:|---:|---|---:|
| PDF dense10 | 8 processes | 89.5 | 93.25 | 82 / 117 | 11.17 |
| PDF dense50 | 8 processes | 320.5 | 323.38 | 300 / 357 | 16.58 |
| Triage | 8 processes | 28 | 28.75 | 27 / 33 | 1.91 |
| OCR Pilkades | 3 processes | 4,313 | 4,396.33 | 4,228 / 4,648 | 222.05 |
| OCR Panin | 3 processes | 3,323 | 3,327.67 | 3,311 / 3,349 | 19.43 |

Raw evidence: pdf-original-1..8.trx, triage-original-1..8.trx, ocr-timing-1..3.trx. P95 is not inferred from these small cold-run samples.

### Dense PDF stage profile, 3 processes × 20 warm samples

| Workload | Median | P95 | Min / max | Sample std |
|---|---:|---:|---|---:|
| dense10 warm | 25.323 ms | 27.471 ms | 24.473 / 28.569 | 0.991 ms |
| dense50 warm | 125.773 ms | 130.205 ms | 122.049 / 136.147 | 2.729 ms |

Warm page-reader initialization dominates: dense10 mean page-reader 23.969 ms, document open 0.177 ms, GetText 0.305 ms; dense50 118.944 / 0.199 / 1.414 ms. Six of 60 dense10 samples and 24 of 60 dense50 samples crossed managed GC collection counters. No forced collection in the measured loop. Text equality/end-page content assertions hold across reads; raw data: pdf-profile-1..3.trx.

Cold profile samples: dense10 median 70.717 ms/min70.249/max71.174; dense50 median314.212/min313.451/max673.414. Different harness/JIT/cache histories mean these are not interchangeable with original single-shot benchmark distributions.

### Explicit agreement

User selected **“Pisahkan cold dan warm”** after reviewing profile and the remaining original dense10 117 ms failure. Agreed policy: report cold-start separately as a reproduced/profiled/classified baseline exception; measure steady-state with explicit warm-up and 20 samples; **warm P95 limits remain <100 ms dense10 / <500 ms dense50**. This is not a claim cold-start always meets the old single-shot limit, and not an unapproved threshold increase.

The same cold/warm distinction was applied transparently to the real-world PDF timing after the full run exposed a 208 ms cold spike; warm limit remains 100 ms. Triage already had JIT warm-up; it now records 20 samples and keeps P95 <100 ms. OCR timing retains its original single native inference limit; isolation fixes contention instead of granting an exception.

Final full-run warm measurements:

| Workload | n | Median ms | Mean ms | P95 ms | Min / max | Sample std ms | Gate |
|---|---:|---:|---:|---:|---|---:|---|
| dense10 | 20 | 26.441 | 26.853 | 28.369 | 25.772 / 30.084 | 1.088 | PASS <100 |
| dense50 | 20 | 132.712 | 133.596 | 139.823 | 130.405 / 142.042 | 2.918 | PASS <500 |
| real-world 3 pages | 20 | 24.630 | 24.861 | 26.594 | 24.232 / 26.598 | 0.667 | PASS <100 |
| triage | 20 | 21.430 | 23.682 | 30.373 | 20.598 / 32.944 | 4.082 | PASS <100 |

Statistics: mean Σx/n; sample deviation `sqrt(Σ(x-mean)^2/(n-1))`; median mean of middle two sorted samples for even n; P95 nearest-rank `sorted[ceil(.95*n)-1]`. These are measured test workloads, not guaranteed production P95 across hardware/model/data.

## 8. Reproduction

Use actual installed SDK, not runtime-only `dotnet` on PATH. Test fixture paths are explicit; different machines must supply valid equivalent assets/models and may have different timing. The older native tests also contain machine-path fixtures; passing here does not certify absent-model branches as native coverage elsewhere.

```powershell
$env:NEXUS_GATE_A_LIVE='1'
$env:NEXUS_TEST_MODEL_ROOT='D:\NEXUS\Models'
$env:NEXUS_TEST_ENGINE_ROOT='D:\NEXUS\Engine'
$env:NEXUS_TEST_DESIGN_PATH='D:\NEXUS\test\1.jpg'
$env:NEXUS_TEST_PDF_PATH='D:\NEXUS\Audit\llama.cpp-master\tools\ui\tests\stories\fixtures\assets\example.pdf'
$env:NEXUS_GATE_A_EVIDENCE='D:\NEXUS\Nexus-CSharp\docs\gate-a-results'
```

Evidence directory must already exist. Live fixture creates isolated per-run config/reference/history/output folders inside it and restores catalog active model through existing model workflow. No user Chat history is used for create/delete tests. Live facts explicitly SKIP when opt-in is absent; final acceptance run set opt-in and executed both, yielding 0 skipped. Ensure no user-owned llama server is running before the live fixture; it asserts this rather than killing unrelated processes.

Restore/build/test commands follow §5 and saved logs. Keep benchmarks serial across projects (`-m:1`); 5-minute inactivity watchdog is appropriate for the existing multi-configuration native threading benchmark. No tests were filtered out of final solution run.

## 9. Regression result, known exceptions, stop point

**Compatibility PASS** for representative existing features and current configured assets. No new runtime, catalog, production persistence/database, framework/package, shell capability, ToolHost, or Agent implementation. Current engine/runtime implementation and AI CHECK semantics preserved.

Accepted performance exception: cold PDF startup variability under the explicit agreement above. Existing compiler warnings remain tracked. Separate limitations: cold/Release builds on a pristine machine, unavailable third-party/native assets, full interactive visual-state/per-monitor DPI matrix, and long-history performance are not newly claimed verified.

Source diff review must use pre-edit archive comparison, not ordinary HEAD diff (which includes user changes). New test files are listed explicitly. Git staging content remains identical; generated outputs and normal test/runtime logs are not part of authored source rollback.

Gate B prerequisite **baseline build/test/representative compatibility is now fulfilled within this policy and fixture scope**. This grants no Agent authorization. **STOP:** await review before Gate B; no Agent contracts/runtime/tools/policy/memory/skills/MAF/Hermes port written.
