# Nexus Architecture Audit

Tanggal: 2026-10-01. Scope: **Phase 0 + Phase 0.5 saja**. Basis: working tree aktual, bukan hanya HEAD. Semua path di bawah relatif terhadap `Nexus-CSharp/`, kecuali diberi prefix `D:/NEXUS/`. Hasil eksekusi ada di [BASELINE.md](BASELINE.md); usulan bersyarat ada di [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## 1. Solution Structure

Repository Git aktif: `D:/NEXUS/Nexus-CSharp/.git`; `D:/NEXUS` sendiri bukan repository Git. HEAD awal `a530067` (`test: prove file management state preservation`), branch awal `master`. Working tree sudah berisi banyak perubahan staged, unstaged, dan untracked, termasuk source dan `bin/obj`. Audit dibuat pada branch `audit/nexus-agent-phase0-20261001`; branch bukan snapshot immutable perubahan lokal.

Solution produksi: `src/Nexus.sln:8-56`, **22 project: 11 produksi + 11 test**.

| Area | Project produksi | Target | Test project |
|---|---|---|---|
| 00_Contracts | Nexus.Contracts | net8.0 | Nexus.Contracts.Tests |
| 01_Foundation | Nexus.Foundation | net8.0 | Nexus.Foundation.Tests |
| 02_Modules | Nexus.Conversation | net8.0 | Nexus.Conversation.Tests |
| 02_Modules | Nexus.AI.EditingLite | net8.0 | Nexus.AI.EditingLite.Tests |
| 02_Modules | Nexus.FileUtilities | net8.0 | Nexus.FileUtilities.Tests |
| 02_Modules | Nexus.AI.Llama | net8.0 | Nexus.AI.Llama.Tests |
| 02_Modules | Nexus.Engine.ImageSubsystem | net8.0-windows | Nexus.Engine.ImageSubsystem.Tests |
| 02_Modules | Nexus.PdfUtilities | net8.0 | Nexus.PdfUtilities.Tests |
| 02_Modules | Nexus.Core.HomeSettings | net8.0 | Nexus.Core.HomeSettings.Tests |
| 02_Modules | Nexus.Engine.ColorManagement | net8.0-windows, WPF | Nexus.Engine.ColorManagement.Tests |
| 03_App | Nexus.App | net8.0-windows, WPF, WinExe | Nexus.Integration.Tests (WPF) |

Evidence target/reference: masing-masing `.csproj` pada direktori project; `Nexus.App.csproj:3-18,29-35`. Dependency map project nyata:

```text
Nexus.App (composition root, WPF shell)
  +-- Contracts
  +-- Foundation --> Contracts
  +-- Conversation --> Foundation, Contracts
  +-- AI.EditingLite --> Foundation, Contracts
  +-- FileUtilities --> Foundation, Contracts
  +-- AI.Llama --> Foundation, Contracts, PdfUtilities
  +-- PdfUtilities --> Foundation, Contracts
  +-- Core.HomeSettings --> Foundation, Contracts
  +-- Engine.ImageSubsystem --> Foundation, Contracts, Engine.ColorManagement
  +-- Engine.ColorManagement --> Foundation, Contracts
```

Project lain ditemukan dan dibedakan dari dependency produksi:

- `src/Phase5Runner/Phase5Runner.csproj`: console benchmark net8.0-windows, tidak masuk `Nexus.sln`; menautkan source Evidence/OCR/Judge langsung (`:16-24`), memakai `RealLlamaClient` port 8089. Belum dibuild/dijalankan pada baseline solution ini.
- `Nexus.UI Pack/Nexus.UI.Pack.sln`: enam project WPF UI vendored (Ui, Abstractions, DependencyInjection, Tray, ToastNotifications, SyntaxHighlight). Tidak direferensikan `Nexus.App.csproj`; bukan runtime shell aktif. Build solution ini belum diverifikasi.
- `D:/NEXUS/Engine/AIEngine/AIEngine.csproj`: engine net48, ONNX Runtime 1.27.0, build menyalin output ke parent Engine (`:19-26`). Tidak direferensikan solution C# aktif. Consumers legacy di luar solution: UNKNOWN.
- `Audit/Reference`, `Audit/Referensi`, `Archive`, `scratch`, dan sample/vendor solutions bukan anggota `Nexus.sln`. Tidak diperlakukan sebagai application boundary.

Dependency eksternal produksi yang terbukti di `.csproj`:

| Project | Package langsung penting | Implikasi |
|---|---|---|
| App | Microsoft.Extensions.DependencyInjection 8.0.1; System.Drawing.Common 8.0.15 | DI sudah ada; desktop Windows |
| AI.EditingLite | System.Management.Automation 7.4.3 | Parser PowerShell; bukan bukti shell executor |
| AI.Llama | System.Management 10.0.10; UTF.Unknown 2.7.0 | WMI Windows dan deteksi encoding |
| ImageSubsystem | Microsoft.ML.OnnxRuntime 1.27.0; System.Drawing.Common 8.0.15; System.Text.Json 10.0.10 | ONNX native + bitmap |
| PdfUtilities | Docnet.Core 2.6.0; PdfSharp 6.2.4; SkiaSharp 4.150.1; SkiaSharp.NativeAssets.Linux.NoDependencies 4.150.1 | PDFium, PDF manipulation, raster/native assets |

Test dependencies: xUnit, Microsoft.NET.Test.Sdk, coverlet, serta Moq/FluentAssertions/NSubstitute di project terkait. Restore berhasil tanpa warning tercatat. Audit ini tidak menyatakan seluruh transitive dependency bebas vulnerability; dependency/license tree menyeluruh belum dinilai. **Tidak ada dependency baru ditambahkan.**

## 2. Startup and Application Lifecycle

`src/03_App/Nexus.App/App.xaml.cs:45-53` membangun `ServiceCollection` dan satu `ServiceProvider`. Mayoritas services, ViewModels, dan MainWindow singleton; `IColorManagementService` transient dengan factory. Alias konkret/interface engine mengembalikan instance yang sama (`:116-142`).

`App.xaml` tidak mempunyai StartupUri; `App.OnStartup:219-270` memuat katalog lalu resolve/show MainWindow. `MainWindow.xaml.cs:41-53` memasang DataContext, notification, navigation, dan closing handlers. `MainViewModel:258-310` memakai navigation semaphore dan `IModuleLifecycle`.

LLM lazy start saat `ConversationViewModel.OnNavigatedToAsync:983-1001`, stop saat `OnNavigatedFromAsync:1003-1016`. `App.OnExit:320-346` stop server dan dispose provider secara sinkron. Startup tidak otomatis meluncurkan model server.

**Coupling terbukti:** MainViewModel membutuhkan ConversationViewModel; factory ConversationViewModel membutuhkan AiCheckViewModel; AiCheck membutuhkan PpOcrEngine. Factory PpOcrEngine (`App.xaml.cs:88-93`) langsung memeriksa model directory. Missing PP-OCR dapat menggagalkan resolusi Chat/shell, walaupun LLM tersedia; isolated governance test membuktikan failure pada root default test. Ini bukan alasan untuk mengganti execution path AI CHECK; evaluasi lazy availability cukup.

Runtime smoke membuka window `Nexus`, navigasi utama, dan exit melalui dialog native dengan sukses. FPS/layout fidelity dan seluruh flow file picker belum diukur.

## 3. Existing LLM / Model Runtime

Nama nyata: `ILlamaServerManager`, `LlamaServerManager`, `IGenericLlamaEngine`, `GenericLlamaEngine`, `IModelCatalogService`, `ModelCatalogService`, `LlamaConversationPipeline`, `ILlamaClientService`, `LlamaClientService`.

```text
Chat: ConversationViewModel
  --> IConversationRuntimeWorkflow / ConversationRuntimeWorkflow
      +--> GenericLlamaEngine --> shared ILlamaServerManager
      +--> LlamaConversationPipeline --> shared HttpClient --> localhost:8080

AI CHECK: AiCheckViewModel --> IAiCheckJudgeService / AiCheckJudgeService
  --> shared ILlamaClientService / LlamaClientService
      --> shared HttpClient --> localhost:8080

ModelCatalogService --> shared ILlamaServerManager (runtime model verification)
```

**Kesimpulan:** share server ownership dan HttpClient pada production DI, tetapi bukan satu high-level generation gateway. AI CHECK bukan consumer langsung `LlamaConversationPipeline`. Satu process saat smoke mendukung bukti DI; bukan bukti bebas race pada setiap interleaving.

Evidence: `App.xaml.cs:73-87,116-133,159-160,193-201`; `ConversationRuntimeWorkflow.cs:33-56,132-147,168-182`; `AiCheckJudgeService.cs:294-320`.

`LlamaServerManager.cs`:

- Owner satu process/PID/model path (`:54-75`), lifecycle semaphore (`:275-280`), state Stopped/Starting/WaitingForHealth/Ready/Failed/Stopping.
- Resolve `Engine/LlamaServer/b11057`; backend WMI NVIDIA -> cuda, selain itu cpu (`:94-171`).
- Context cap 32768; tuning `-t 4 -tb 8 -b 128 -ub 64 -fa on -np 1`, reasoning deepseek (`:174-191`). Default runtime workflow memakai context 8192.
- Health 2 s dan `/props.model_path` verification (`:193-262`), startup timeout 60 s, stop wait timeout 5 s (`:397-446,489-502`). Tidak ada autorestart loop.
- Same-model start skip setelah verifikasi; foreign-port check berusaha memakai WMI; exceptions pada port detection ditelan (`:600-624`). Efektivitas foreign-port check belum dibuktikan live.
- Failed start cleanup dimiliki manager; event dari process callback tidak otomatis berarti UI-thread safe.

Actual smoke: satu PID 20644, localhost:8080, Gemma E4B GGUF sesuai katalog, Ready setelah **22,255 s**, probe response `OK` **1,787 s**, stop saat meninggalkan workspace, tidak tersisa server setelah exit.

Contracts `EngineSessionOptions`/`IJobScope` sudah ada; nama tersebut **bukan bukti class EngineSession atau scheduler Agent**. Jangan menambah runtime/model server baru.

## 4. Existing Chat Architecture

WPF `Views/ConversationView.xaml` + `ViewModels/ConversationViewModel.cs`; message parsing/rendering native ada di `AIWorkspace/Markdown` dan `AIWorkspace/Rendering/MessageContentPresenter.cs`.

`StartGeneration:2735-2773` mengambil snapshot sampling, membuat CTS per request, menjalankan generation di background. `RunGenerationAsync:2776-2949` stream melalui workflow, marshal perubahan kecil ke Dispatcher, render hasil, lalu simpan user/assistant atau replacement prefix melalui conversation manager. Error HTTP/cancellation/unknown diubah menjadi status user-facing.

Model switching mempunyai lock; feature meliputi history, temporary chat, edit/resend, regenerate, attachments, DeepThink, dan prompt overrides. Existing context/attachment trimming dibahas di §7/§17.

Legacy constructor `ConversationViewModel:960-980` membuat `LegacyConversationRuntimeWorkflow`; production DI memakai constructor workflow secara eksplisit. Legacy overload ini mengabaikan generation options (`LegacyConversationRuntimeWorkflow.cs:101-106`) dan `CancelStreaming` membatalkan semua active client requests. Pertahankan sampai consumers compatibility dipahami.

145 ConversationViewModel tests + 5 runtime workflow tests PASS dalam process terisolasi. Ini kebanyakan fake/mock runtime; bukan 150 live model tests. Live generation probe dilakukan langsung ke server existing, bukan send/render/save Chat end-to-end.

## 5. Existing AI CHECK Architecture

`AiCheckViewModel.ExecuteRunCheckAsync:803-1085`:

```text
uploaded design/image
 --> PpOcrEngine (background; token)
 --> run-local EvidenceEngine + reference snapshots (background)
 --> SUSPECT candidates only / early exit if none
 --> IAiCheckJudgeService
 --> candidate-ID parser/sanitization
 --> deterministic overlay/findings mapping (UI)
```

`EvidenceEngine`, `ThreeTierTriageFilter`, PP-OCR, OneOCR berada dalam ImageSubsystem; AI CHECK tidak semata-mata chat prompt. User dictionary, domain lexicon, structured phrases, entity references berasal dari `IReferenceDataManager`. EvidenceEngine diinstansiasi per run (`:972-1002`), menghindari indeks mutable global untuk run.

Judge `AiCheckJudgeService.cs:76-157,159-272`: preflight ID unik; batch default 10; G3 noise guard; pass 1 clean; bounded pass 2 targeted hint; output order/candidate ownership dipertahankan; unanswered -> Unresolved. Tidak ada blind destructive retry. `BatchSize` public mutable dan pembagian batch tidak guard zero (`:101-105`); kandidat berasal dari aplikasi, belum general untrusted tool API.

Timeout batch `max(90, candidates * 10)` detik, max tokens `max(32, candidates * 32)`. Cancellation di `ExecutePromptAsync:322-337` dikonversi menjadi unresolved timeout; pass 2 catch-all (`:243-246`) juga menangkap cancellation. ViewModel mengecek token lagi setelah judge. Sebelum reuse untuk Agent, pisahkan caller cancellation dari timeout tanpa mengubah aturan judgment existing.

Judge membaca sampling melalui `Func<LlamaGenerationOptions>` yang mengambil ConversationViewModel (`App.xaml.cs:77-86`), sehingga current settings/model ikut menentukan hasil. Komentar menyebut Gemma, tetapi request memakai current server; tidak ada server Gemma kedua yang otomatis dibuat.

21 pipeline integration + 21 run-isolation tests PASS terisolasi. Live OCR-to-judge document lewat UI belum dijalankan. Pure text AI CHECK masih menampilkan warning development (`AiCheckViewModel:807-810`).

## 6. Existing Agent / Task Execution

Pencarian source `.cs` pada `src` untuk Agent, tool registry/policy, approval/checkpoint/skills tidak menemukan orchestration Agent umum. Ada `IJobScope`, pipeline job cancellation, commands async, fixed feature workflows, dan EditingLite. **Fixed workflow bukan autonomous agent loop.** `EngineStateService` hanya mutable IsBusy/ActiveModule/LastError (`:3-7`), bukan concurrency authority.

`IEditingWorkflowService` terdaftar DI (`App.xaml.cs:101-107`), tetapi pemanggilan produksi `ApplyEditAsync` tidak ditemukan di App source. Implementation sudah mempunyai snapshot/proposal/syntax/temp/backup/replace/read-back/restore; tidak mempunyai planning/tools/model reasoning, approval, execution budgets, build/test gate, atau persisted resumable tasks.

Keputusan: reuse operation-level capabilities, bukan menjadikan EditingLite agent runtime. Tidak ada Agent implementation pada fase audit ini.

## 7. Existing Session / Conversation Handling

`ConversationManagerService` singleton menyimpan **satu ActiveConversation** dan flag IsTemporary (`:26-38`). New/open/add/replace/rename/delete serta sampling/settings tersedia. Ini state current UI conversation, bukan isolated parallel Agent session. Temporary chat tidak ditulis ke disk (`:40-49,86-101`).

Models `ConversationDetailRecord`, `ConversationMessage`, `ConversationAttachmentReference` menyimpan history/settings/file references. `ConversationIndexService` mengurutkan UpdatedAt dan cache index. Index rebuild melewati JSON rusak (`:66-95`).

Reuse DTO/storage responsibility; jangan membagi mutable ActiveConversation antara background Agent dan Chat. Persistence tidak menerima cancellation token. Session ownership baru diperlukan hanya jika feature Agent berjalan bersama Chat.

## 8. Existing Persistence

Tidak ditemukan DB package/connection pada production projects yang diperiksa. Penyimpanan nyata dominan JSON/files:

| Responsibility | Source / owner | Location / behavior |
|---|---|---|
| Configuration | Foundation `ConfigService:13-17,24-75` | `<app-base>/nexus_data/config.json`; synchronous write; corrupt JSON fallback tanpa overwrite saat load |
| Prompts/settings | `PromptRegistry`, `SettingsManagerService` -> IConfigService | Responsibility config yang sama; bukan DB kedua |
| Conversations | `ConversationStorageService:28-31,71-90` | `<app-base>/nexus_data/conversations/<id>.json`; tmp + move overwrite |
| Conversation index | `ConversationIndexService:34,53-64` | folder sama, `index.json`; derived/rebuildable cache |
| Reference data | `ReferenceDataManager:20-48,254-386` | `%APPDATA%/Nexus/ReferenceData/*.json`; own responsibility, cache + semaphore |
| Model catalog | `ModelCatalogService`, `Models/AI Workspace/model-index.json` | catalog + model metadata; validasi path/id dan serialized operations diuji |
| File organize undo | FileUtilities `FileManifestService`; CleanDownloads/Rapikan services | manifests berkaitan filesystem operations |
| Editing rollback | EditingLite | backup file sementara per edit; dibuang setelah sukses |
| CMYK profiles | ColorManagement / config profile selection | ICC resources/profile files; tidak diganti |

Separate responsibility reference-data folder bukan bukti duplicate conversation persistence. JSON tmp+move tidak memberikan transaksi multi-file detail/index, conflict detection, writer isolation, atau crash durability universal. Detail/index memakai nama `.tmp` tetap; concurrent writers dapat bertabrakan. `GetJsonPath(id)` menerima arbitrary string tanpa guard GUID (`ConversationStorageService:45`). Ini harus divalidasi jika menjadi Agent-facing input.

`SettingsManagerService.SaveSettingsAsync:101-105` sebenarnya synchronous SaveSettings lalu Task.CompletedTask. Jangan menganggap suffix Async berarti off-Dispatcher. Baseline memakai model/engine roots `D:/NEXUS/Models` dan `D:/NEXUS/Engine`; app-base default lain dapat gagal.

## 9. Existing Tool / Plugin Infrastructure

**Ada tool abstraction domain image:** `SavePipeline/IImageToolModule.cs:5-14` menentukan suffix, output format/color space; implementations RemoveBackgroundModule/UpscaleModule/InpaintModule dipilih oleh `ImageSubsystemViewModel:351-353`. Itu output-policy abstraction, bukan execution registry/risk/permission.

Interfaces structured services sudah tersedia untuk filesystem metadata, PDF conversion/merge/split, image workflows, reference data, model catalog, editing. Tidak ditemukan generic agent tool descriptor/registry/policy/plugin loader pada production `src`. Tidak ada MAF dependency pada `.csproj` yang diaudit.

Reuse services melalui allowlisted adapters setelah input policy; jangan expose setiap public method otomatis atau mengubah IImageToolModule menjadi enum/tool system umum.

## 10. Existing Process / Shell Execution

- `LlamaServerManager:334-345` menjalankan executable fixed engine, `UseShellExecute=false`, no console, args model/backend/context, owned PID dan cleanup.
- UI membuka explorer/output paths melalui ProcessStartInfo di Settings, CleanDownloads, RapikanFile, PDF, Image. Ini user-triggered shell association/folder opening, bukan general command execution.
- Markdown link hanya `http/https` (`MessageContentPresenter:455-478`) kemudian OS browser association.
- EditingLite memakai `Parser.ParseInput` untuk PowerShell syntax (`SyntaxValidatorService:48-55`), **tidak menjalankan script**.
- `Directory.Build.targets:2-3` menjalankan PowerShell setelah setiap Build untuk signing output dengan first code-signing certificate CurrentUser. ContinueOnError=true; signing bisa berbeda antarmesin. Ini build-time shell boundary nyata.
- `D:/NEXUS/Dev_Run.cmd` memakai SDK path Temp/dotnet8; launcher release/debug `Jalankan_NEXUS.cmd` menggunakan runtime Program Files. Baseline memakai SDK user yang terbukti tersedia.

Tidak ditemukan production general unrestricted PowerShell/cmd executor atau privilege escalation (`Verb=runas`) pada pencarian. Absence claim dibatasi source aktif, bukan seluruh vendor/archive. Belum ada alasan membuat ToolHost untuk read-only phase; jika build/shell diekspos ke Agent, signing hook dan process/resource boundary wajib masuk isolation assessment.

## 11. Existing UI Architecture

Main UI native WPF: XAML, DataTemplates, custom control templates, resource dictionaries, MVVM (`ViewModelBase`, RelayCommand/AsyncRelayCommand), plus code-behind untuk input/layout/window chrome. `MainWindow.xaml` shell + module templates; `MainViewModel` navigation/lifecycle; singleton VMs mempertahankan state.

AI Workspace menggabungkan Chat/AI CHECK modes dalam ConversationViewModel. Image VM lazy di MainViewModel (`:14,89,98-115`), sementara AI CHECK/OCR dependency saat shell resolve belum lazy. Native controls untuk dialog/snackbar sudah ada; reusable sebagai UI approval presentation, tetapi bukan permission authority.

**Chat virtualization gap nyata:** `Views/ConversationView.xaml:688-692,1016-1019` memakai ScrollViewer -> ItemsControl Messages, tanpa virtualizing/recycling panel yang eksplisit. Conversation history sidebar juga ListBox dalam outer ScrollViewer (`:612-645`). Context trimming tidak mengurangi seluruh message visual tree. Existing CleanDownloads list memakai recycling; jangan mengganti seluruh UI library.

Smoke navigation PASS; full visual states normal/hover/pressed/focus/disabled/light/dark/high-contrast, 1:1 dimensions, high-DPI dan long-history scrolling masih UNKNOWN.

## 12. Existing Theme / Resource Architecture

`App.xaml:8-24`: PrimitiveTokens -> SharedMetrics -> DarkSemanticTheme -> Components (accessibility/buttons/input/segmented/surfaces/feedback/overlays/menus/layout) -> compatibility aliases -> SharedStyles -> legacy DarkTheme -> SettingsCard.

`ThemeManager.cs:19-99` memarshal Dispatcher, mengganti tepat satu semantic dan satu compatibility dictionary, restore dictionary lama bila replace gagal; mode Dark/Light/HighContrast. Event ThemeChanged setelah sukses. Bila dictionary cardinality tidak tepat, silent return; beberapa exceptions ditelan sehingga status failure terbatas.

`RegisterTypographyCompatibilityResources` (`App.xaml.cs:272-295`) menjaga legacy keys bertipe Double. Existing tests menunjukkan token governance debt: expected 133, actual 137, plus primitive-token references di ConversationView. Native theme runtime dipertahankan; semantic get/set API belum tersedia sebagai tool tervalidasi.

Vendored `Nexus.UI Pack/LICENSE.md` MIT (WPF UI contributors 2021-2025); project metadata menyebut WPF UI. Upstream tag/commit, daftar file yang diadaptasi ke Nexus.App, dan local modification provenance belum terbukti: **UNKNOWN**. Tidak ada source UI pihak ketiga disalin oleh audit ini.

## 13. Existing Security Boundaries

Boundary existing bersifat domain-level, bukan OS sandbox: process ownership, model catalog path validation, attachments size/magic validation, editing project-root check, deterministic AI CHECK parser, native confirmation dialogs.

`FileSnapshotService.ResolvePath:39-49` membandingkan StartsWith(root.FullName) tanpa separator boundary; sibling prefix (misalnya root `C:/work/app`, target `C:/work/application/file`) bisa lolos secara logis. Junction/symlink containment belum ditangani. Ini temuan source-level; exploit live terhadap data user tidak dijalankan.

LLM output saat ini tidak terhubung ke generic tool execution, sehingga dokumen/prompt belum dapat otomatis mengeksekusi arbitrary writes melalui Agent. Policy authority, risk classification, path allowlist untuk tools, approval binding terhadap exact validated arguments, output bounds, dan secret redaction belum ada sebagai sistem umum.

## 14. Existing Cancellation / Timeout

| Operation | Evidence | Existing support / limitation |
|---|---|---|
| Server start | `LlamaServerManager:269-465` | token overload + bounded health/start; workflow/Generic engine API start tidak meneruskan caller token |
| Server stop | manager `:467-530` | 5 s process wait; lifecycle lock wait tanpa token |
| Chat stream | pipeline `:34-113`; VM `:2747,2806-2815` | request token sampai SendAsync/read/SSE; tidak ada overall/idle body deadline eksplisit |
| Client stream | client `:80-94,179-197` | linked CTS per request; CancelStreaming global membatalkan semua requests |
| AI CHECK | VM `:813,832,972-1003,1084-1085`; Judge `:297-337` | operation token + per-batch timeout; cancellation -> unresolved di Judge |
| Images | workflows/pipelines/PipelineJobScope | token di pipeline boundaries/pre/post; generic engine load/run signature tanpa token, native Session.Run sinkron |
| Generic ONNX | `GenericImageEngine:38-42,120-143` | gate max 2 **per session**, bukan global pool; gate tidak cancelable |
| PDF | engine `:80-107`; conversion/merge/split interfaces | token + Task.Run dan page checks; preview/page count sinkron |
| Persistence/edit/file organize | storage/edit/file utility interfaces | banyak Async tanpa token; future Agent tidak boleh mengklaim cancelable hanya karena mengembalikan Task |

Default shared HttpClient mempunyai default timeout, tetapi `ResponseHeadersRead` tidak menyediakan overall timeout untuk seluruh SSE body. EOF pada pipeline yield-break walaupun belum `[DONE]`; truncated stream tidak selalu classified failure.

## 15. Existing Logging / Telemetry

`ILoggerService`/`CrashLoggerService` menyediakan category/level/timestamp, console dan locked append crash log; exception write ditelan. `App.xaml.cs:224-239` menulis server event lifecycle log (instance, PID, state, elapsed, thread) sinkron. Log lifecycle dioverwrite tiap startup.

Ada Trace/Debug timings pada image workflows/pipelines dan AI Judge, TTFT di LlamaClient, memory metrics di generic engines. LlamaClient TTFT dimulai **setelah response headers**, dan production Chat pipeline tidak memakai client TTFT path. `stream_options.include_usage` dikirim tetapi helper belum memetakan usage ke telemetry.

Tidak ditemukan end-to-end task/session/tool correlation, latency histograms/P95, durable approvals/checkpoints, retry counters, atau secret-redaction boundary umum. Reuse logger/event primitives; tambah minimum Agent-specific events ketika Agent benar-benar ada.

## 16. Existing Tests

Sebelas test project ditemukan/dijalankan melalui solution. Evidence detail/count/failures: `docs/phase0-test-results/*.trx` dan BASELINE.

- Initial run: **551 passed, 4 failed dari 555 completed result records**, integration host aborted; ini **bukan seluruh test integration selesai**.
- Test domains meliputi contracts, config/filesystem/reference data, history/index/temporary chat, snapshot/proposal/writer/validator/rollback, LLM transport/catalog/judge/budgeting, image pipelines/native-model tests, PDF conversion/extraction/merge/split, settings, WPF/UI/governance.
- Isolated feature inventory: 241 pass, 6 fail dalam 247 results; terpisah dari initial run, tidak dijumlahkan sebagai unique coverage.
- Failure reproducible terisolasi: architecture governance 3, design-system governance 3. Missing model default test root bukan otomatis production outage.
- `WpfAppHost` mencoba singleton app host, tetapi tiga kelas punya custom STA `new Nexus.App.App`; full run crash menunjukkan ownership/lifetime belum konsisten.
- Real-model tests dapat `return` ketika model hilang (`InpaintPipelineRealModelEndToEndTests:19-22,154-158`) sehingga Passed/Skipped counts bukan jaminan native execution. Run ini mencatat native inpaint/OCR durations; jangan generalisasikan portability fixture ke mesin lain.

Tidak ada Agent policy/budget/recovery tests karena subsystem Agent umum belum ada. Tidak menambah test baru pada fase audit.

## 17. Existing Performance Characteristics

Measured: restore 6,348 s; Debug incremental solution build 25,043 s wall (MSBuild 24,76 s); initial test 71,195 s. Build cache sudah tersedia; bukan cold-build benchmark.

Native runtime CPU selected, Ready 22,255 s; health 503 saat model loading lalu 200/status ok; one-shot 16-token completion 1,787 s. Observed server working set saat loading 5.381.537.792 bytes. Sampel tunggal, bukan P95 atau Chat SLA.

PDF dense10 136 ms initial / 109 ms isolated (gate <100); dense50 654 ms initial / 319 ms isolated (gate <500). Triage 202 ms initial / 28 ms isolated (gate <100). Suite concurrency/native load mempengaruhi timing; isolated pass tidak menghapus initial failure.

Bounded context sudah ada: `AttachmentContextBudgetService:16-80`, char/3 estimate, context cap 32768, reserve <=4096, immutable system/current prompt + protected latest turn, oldest-first pruning; `WaterFillingBudgetAllocator` membagi attachment headroom. **Estimator bukan tokenizer dan protected context bisa tetap oversize.** Runtime start 8192 sementara budget menerima model context; alignment perlu diuji sebelum Agent context reuse. Tool results/agent iteration transcript belum didukung.

## 18. Reusable Components

Reuse utama: DI container; owned LlamaServerManager/GenericLlamaEngine; Llama transport helper/pipeline/client; model catalog; contracts/selection registry; conversation JSON DTO/storage; reference manager; context budget/allocator; EditingLite deterministic operations; file manifests/undo; PDF/image workflows; native theme tokens, dialogs/snackbar; logger/server events; regression tests.

Reuse bersyarat: mutable ActiveConversation, synchronous config, path helpers, EditingLite writer, global client CancelStreaming, unbounded task methods. Existing function perlu boundary validation/lifetime yang sesuai, bukan automatic Agent tool exposure.

## 19. Duplicate / Overlapping Components

1. Dua HTTP/SSE loops (`LlamaClientService` dan `LlamaConversationPipeline`) menggunakan helper sama, tetapi sanitize/empty-chunk/TTFT behavior berbeda dan diuji. **Overlap transport, bukan duplicate server.** Jangan unify agresif.
2. LegacyConversationRuntimeWorkflow overlap lifecycle dengan production workflow; compatibility constructor/test consumers nyata. Tidak terdaftar di production DI; REMOVE belum dibenarkan.
3. GenericImageEngine + older image services + OCR/SAM native sessions memiliki domain ownership berbeda; cache bisa menambah memory. Belum terbukti duplicate same-model runtime; UNKNOWN untuk concurrent load/options consistency.
4. Phase5Runner source linking/client berbeda dari production. Benchmark drift risk, bukan application dependency.
5. Vendored UI Pack/reference directories overlap UI source area tetapi bukan App project reference. Jangan hapus user/reference work.

## 20. Technical Debt

Priority nyata: unreliable WPF integration host; governance reflection overload ambiguity; physical ModelPath access melanggar gate; missing model fixture root; design token debt; eager OCR coupling startup; cancellation propagation incomplete; Async naming untuk synchronous IO; conversation concurrency/detail-index consistency; EditingLite no optimistic conflict check; security-sensitive path checks terlalu lemah; lifecycle logs synchronous/unbounded.

Working tree dirty/untracked dan beberapa build artifacts tracked menyulitkan reproducibility. Baseline commit saja tidak cukup merekonstruksi source audit; perlu checkpoint source lokal yang disengaja sebelum implementation.

## 21. Performance Risks

- Nonvirtualized Chat/history dan expensive message rendering pada long history.
- Per-chunk Dispatcher updates + string concatenation; perlu measured latency/frame profile, bukan rewrite renderer.
- Agent requests akan contend slot server `-np 1`; singleton server tidak berarti scheduler/admission control sudah ada.
- Workflow model switch/stop dapat mengganggu in-flight consumers; lifecycle semaphore bukan generation lease.
- Generic ONNX model cache check/create tidak per-path atomic (`:77-99`); dua loads bisa membuat sessions. Options tidak masuk path cache key. Source-level risk; belum concurrency benchmark proof.
- Gate per session membatasi dua inference, tidak membatasi jumlah loaded models/native sessions secara global.
- OCR/reference initialization/model catalog load dan config IO dapat berjalan di Dispatcher. `await` tidak otomatis memindahkan native synchronous work ke background.

## 22. Security Risks

- Prefix-root traversal check dan unresolved reparse points sebelum exposing read/write tools.
- No expected-current-hash check sebelum AtomicReplace; concurrent user edit bisa tertimpa; rollback overwrite dapat juga menimpa perubahan eksternal. Read-back hanya memeriksa hash berubah, bukan desired hash equality.
- Conversation/dataset identifiers/path helpers bukan trust-boundary validation universal.
- Build signing hook memakai shell dan current-user certificate; future DotnetBuild tool memerlukan process timeout/output cap/worktree scope dan isolation assessment.
- External files/OCR/link content harus tetap data saat Agent mulai punya tool authority; LLM judgment bukan authorization.
- Generic logs tidak punya secret scrubber/retention/output size limit. Jangan log prompt/file payload penuh pada Agent telemetry.

Tidak ada live exploit atau destructive verification dilakukan. Severity implementasi tergantung exposure; existing user-driven operations dipertahankan.

## 23. Architecture Gaps

| Gap terbukti | Evidence | Smallest prospective boundary |
|---|---|---|
| Gate belum hijau | full/isolated TRX failures + host crash | perbaiki baseline/testing dahulu |
| Agent orchestration absent | source inventory + existing fixed workflows | request/result/run context minimal, di logical area existing |
| No model tool protocol | helper `:19-61,78-122` hanya text/reasoning/finish | optional Agent-only tool schema/call parsing + contract tests; Chat payload tetap kompatibel |
| No generic tool authority | domain services/IImageToolModule saja | explicit read-only registry + immutable policy + input limits |
| No Agent execution budgets | no run context/budget authority | finite iterations/tools/context/time/cancellation **sebelum loop publik** |
| Shared runtime lacks request lease | lifecycle locks tanpa in-flight Agent admission | minimum admission/ownership for Agent; verify Chat priority |
| ActiveConversation not run isolation | manager single mutable state | Agent run snapshot + session ID; jangan ganti DB |
| Safe write not conflict-safe | EditingWorkflow/FileSnapshot sources | containment + expected hash + desired-state verification sebelum exposure |
| No Agent observability/recovery | logger category/events only, transient edit backups | correlated run/tool events dahulu; journal/resume hanya saat requirement |

MAF workflow/harness, semantic memory, skills, self-improvement dan ToolHost **belum merupakan gap requirement wajib**. Tidak mengklaim semuanya harus dibangun.

## 24. Keep / Reuse / Refactor / Wrap / Replace / Remove / Unknown

Semua decisions berikut rekomendasi, belum perubahan implementasi. Owner adalah area/class pengendali; tidak mengarang owner tim/person.

### Shared LLM ownership
Component: `LlamaServerManager` / `GenericLlamaEngine`.
Current role: lifecycle/process/model identity/metrics.
Owner / lifetime: AI.Llama, application DI singleton; PID hidup hanya saat workspace aktif.
Used by / consumers: ConversationRuntimeWorkflow, ModelCatalogService, App shutdown.
Dependencies: HttpClient internal health, Process/WMI/filesystem; injected manager alias sama.
Threading / persistence / cancellation / failure: lifecycle semaphore; no conversation persistence; token start overload, bounded startup/stop; failure cleanup/events.
Evidence: App `:116-133`; manager `:269-530`; smoke PID/health.
Decision: **KEEP / REUSE**.
Reason: ownership/model verification sudah ada dan runtime smoke berhasil.
Risk: Agent bisa menghentikan/switch model saat Chat aktif jika lifetime disalahgunakan.
Proposed change / implementation: Agent consumer saja; admission/lifecycle lease teruji bila simultaneous use dibutuhkan.

### Generation transport
Component: `LlamaConversationPipeline`, `ILlamaClientService`, `LlamaTransportHelper`.
Current role: text/reasoning HTTP SSE, dua semantic behaviors.
Owner / lifetime: AI.Llama, singleton consumers dari App.
Used by: production Chat pipeline, AI CHECK client, compatibility constructors.
Dependencies: shared DI HttpClient; model selection/engine untuk pipeline.
Threading / persistence / cancellation / failure: async IO; stateless transcript payload, tokens; malformed JSON/HTTP errors propagate, premature EOF may complete.
Evidence: pipeline `:34-113`, client `:74-176`, helper `:19-122`.
Decision: **REUSE / WRAP**.
Reason: inference foundation cukup; missing Agent tool protocol tidak membenarkan mengganti Chat/Judge.
Risk: sanitize, finish/usage semantics, global cancel, endpoint divergence.
Proposed change: optional adapter/protocol extension untuk Agent dengan exact compatibility tests, bukan generation runtime kedua.

### Chat application workflow
Component: `ConversationRuntimeWorkflow` + ConversationViewModel.
Current role: lazy runtime navigation/switch + streaming/persistence UX.
Owner / lifetime: App singleton; generation CTS per request.
Used by: native AI Workspace.
Dependencies: catalog, selection policy, generic llama engine, pipeline, history manager.
Threading / persistence / cancellation / failure: model/navigation locks; background stream + Dispatcher; JSON through manager; lifecycle start has no caller token; user-facing failures.
Evidence: workflow `:60-183`; VM `:983-1016,2735-2949`.
Decision: **KEEP**, bounded adapter only for shared lifetime.
Reason: isolated compatibility tests PASS, live workspace load PASS.
Risk: future background Agent must not depend on navigating Chat to stay alive.
Proposed change: smallest shared readiness/run admission boundary if needed, preserving navigation behavior.

### AI CHECK
Component: AiCheckViewModel / PpOcrEngine / EvidenceEngine / AiCheckJudgeService.
Current role: deterministic perception/triage/judgment/findings.
Owner / lifetime: App/Judge/OCR singleton; evidence indexes per run.
Used by: AI Workspace AI CHECK mode.
Dependencies: reference manager, ONNX/OCR native models, shared LlamaClient, prompts/settings.
Threading / persistence / cancellation / failure: OCR/evidence background, UI mapping; reference persistence separately; token checks + unresolved fallback.
Evidence: VM `:770-1085`, Judge `:76-409`, App `:70-94`.
Decision: **KEEP**; **REFACTOR** availability/cancellation boundary only if verified.
Reason: identity/order/noise safeguards meaningful and covered by tests.
Risk: changing prompts/thresholds/batching changes product behavior.
Proposed change: no Agent replacement; lazy OCR assessment and cancellation distinction as separate regression-tested fixes.

### Conversation persistence/session
Component: ConversationStorage/Index/Manager services.
Current role: current conversation + durable JSON/history index.
Owner / lifetime: Conversation module DI singletons, manager mutable current UI state.
Used by: ConversationViewModel; index sidebar.
Dependencies: BCL JSON/files, storage -> index/manager.
Threading / persistence / cancellation / failure: async file IO without tokens or writer lock; tmp move; invalid detail rebuild skipped; no detail/index transaction.
Evidence: storage `:45,71-106`; manager `:26-101`; index `:53-117`.
Decision: **REUSE / WRAP**; targeted **REFACTOR** if parallel writes introduced.
Reason: persistence already exists; second conversation DB unnecessary.
Risk: run isolation, identifier validation, lost updates, same tmp filename.
Proposed change: retain records/source of truth; serialize writes per target and use explicit run/session ownership when needed; defer Agent schema.

### Deterministic editing
Component: EditingLite snapshot/proposal/syntax/writer/workflow.
Current role: single-file replacement + ephemeral rollback.
Owner / lifetime: EditingLite DI singleton services, per-edit session/backups.
Used by: tests; production registration found, App call site not found.
Dependencies: BCL hashes/IO/XML/JSON; PowerShell syntax parser only.
Threading / persistence / cancellation / failure: async IO + synchronous hashes/validation/replace; no tokens; backup rollback on failure.
Evidence: FileSnapshot `:39-109`; EditingWorkflow `:32-103`; FileWriter `:43-58`.
Decision: **REUSE after REFACTOR**, not expose writes now.
Reason: safe-write foundation already exists but does not satisfy untrusted tool boundary.
Risk: prefix containment, TOCTOU overwrite, rollback external edits, no C# build validation.
Proposed change: containment/reparse rules, expected hash, desired hash verification, cancel before atomic apply; operation-class checkpoints/build gate later.

### Image/color/PDF
Component: GenericImageEngine, ImageSubsystemWorkflow/pipelines, IImageToolModule, ColorManagement, PdfUtilities.
Current role: ONNX/domain transforms/output/ICC; PDFium render/text + PDF manipulation.
Owner / lifetime: image/PDF singleton; color service transient; native sessions operation/cache-specific.
Used by: ImageSubsystemViewModel, PDF VM, attachments, AI CHECK perception.
Dependencies: ONNX/System.Drawing/Docnet/PdfSharp/SkiaSharp; registry/policy/contracts.
Threading / persistence / cancellation / failure: pipeline/background tokens, per-session engine gate; output files/profile config; native validation/fallback; no universal rollback.
Evidence: App `:135-178`; GenericImageEngine `:38-143`; workflow `:64-163`; PdfEngine `:25-107`; image module interface.
Decision: **KEEP / REUSE structured services conditionally**.
Reason: specialized stacks and real tests exist; unrelated rebuild not justified.
Risk: native cancellation, memory/concurrency, output path collisions, performance gate failure.
Proposed change: no rendering/engine replacement; tool adapters only after feature requirements and read-only proof.

### Configuration/reference/model catalog
Component: ConfigService/SettingsManager/PromptRegistry; ReferenceDataManager; ModelCatalogService.
Current role: distinct authoritative configuration, domain reference, model selection data.
Owner / lifetime: Foundation/HomeSettings/AI.Llama singleton; reference/cat caches.
Used by: startup/settings/Chat/Judge/image selection.
Dependencies: JSON/files, catalog server verification; no central DB.
Threading / persistence / cancellation / failure: synchronous config; reference/catalog serialized methods; tokens on reference/catalog methods where declared; config fallback/save bool.
Evidence: ConfigService `:13-75`; SettingsManager `:29-66,79-115`; reference `:20-48`; catalog tests/TRX.
Decision: **KEEP / REUSE** with **WRAP** for Agent inputs.
Reason: source of truth already established by responsibility.
Risk: mutable config state, sync IO, unsafe arbitrary IDs/root setters exposed as tools.
Proposed change: immutable approved snapshot/read tools first; no Agent-owned duplicate config/reference DB.

### Native UI/design system
Component: MainWindow/MainViewModel, themes, ThemeManager, dialogs/snackbar, message presenter.
Current role: native shell/navigation/design resources/feedback.
Owner / lifetime: App singleton shell/VMs; resources WPF Application; renderer per visual.
Used by: all modules.
Dependencies: WPF and existing semantic/compatibility dictionaries, settings.
Threading / persistence / cancellation / failure: Dispatcher for resources/UI; settings persists selected theme; dictionary restore on replace failure but some silent returns.
Evidence: App.xaml `:8-24`; ThemeManager `:19-99`; ConversationView.xaml `:688-692`; isolated design tests.
Decision: **KEEP / REUSE**; **REFACTOR** long-history realization only with measured need.
Reason: native modern tokens/templates already exist.
Risk: nonvirtualized long history, primitive-token debt, resource precedence.
Proposed change: future Agent panel uses existing controls; semantic token adapters; no WebView/Electron migration/library addition.

### Logging and tests
Component: ILoggerService, server events, regression projects/WpfAppHost.
Current role: crash/runtime diagnostics and verification.
Owner / lifetime: singleton logger/server events; test-host AppDomain/STA.
Used by: App/services/tests.
Dependencies: IO/Trace/Debug, xUnit/native fixtures.
Threading / persistence / cancellation / failure: sync locked append logs; WPF host collision aborts suite; native fixture return can masquerade as pass.
Evidence: CrashLogger `:26-43`; App `:224-239`; baseline TRX; WpfAppHost `:48-86`.
Decision: **REUSE**, targeted **REFACTOR** harness before Agent.
Reason: verification foundation present but not currently reliable globally.
Risk: false confidence from subset pass, truncated coverage, nonportable models.
Proposed change: single test Application owner/model fixtures, deterministic governance reflection; minimal correlated bounded Agent logs later.

### Legacy/reference/build shell
Component: LegacyConversationRuntimeWorkflow, net48 AIEngine, UI Pack, Phase5Runner, signing hook.
Current role: compatibility, external legacy/reference/benchmark/build tasks.
Owner / lifetime: independent boundaries; source consumers incomplete outside active graph.
Used by: legacy constructor/tests; production use of external projects UNKNOWN; build hook executes on solution builds.
Evidence: legacy `:7-126`; external `.csproj`; Directory.Build.targets `:2-3`.
Decision: **UNKNOWN / KEEP pending evidence**; no REMOVE/REPLACE decision justified.
Reason: not safe to equate duplicate-looking source with unused code.
Risk: benchmark drift, packaging provenance, signing side effects.
Proposed change: document consumers/provenance; isolation assessment only when external execution becomes Agent capability.

## 25. Recommended Agent Integration Boundary

**Proceed only after reviewing this audit and repairing the failed baseline gate. Do not start Phase 1 automatically.**

Logical Agent area can live inside existing App/services and reuse AI.Llama/contracts where types truly cross boundaries. Agent owns run context/budgets/state/policy-mediated orchestration; application owns server/model lifetime/config/history/UI. It must not own an independent model manager, Http inference runtime, or database.

First proof should be one bounded read-only run using approved structured tools and current shared runtime, with external-data/tool-output treated as data. Native tool-call protocol is a real missing transport capability; exact llama build + active model support must be experimentally verified before selecting JSON schema/tool-call mode. A text answer is not proof of tool capability.

Hermes reference examined on 2026-10-01: `NousResearch/hermes-agent`, reported main commit `5a2a300d99a156c6b286ba2376e7025015894eee`; `run_agent.py` from main and `tools/registry.py` from that pinned commit. No source copied. Patterns visible: callback lifecycle for tool start/complete/status, explicit session resets/ownership, optional checkpoints, bounded tool workers, registry schema/handler/toolset single source of truth, bounded error/result handling, dispatch normalization and exceptions. `run_agent.py` defaults max_iterations to sys.maxsize: **do not adopt that default**. Imports indicate budget/compression/approval/recovery modules, but those implementations were not exhaustively audited; pinned iteration-budget/LICENSE fetches returned HTTP 429. License verification is required before any source adaptation.

Adaptation: C# finite per-run budget, explicit registry whitelist, schema validation, bounded result size, correlation events, session snapshots; retain Nexus-owned verification and policy. Do not copy Python mixin/plugin/profile/terminal ecosystem or its folder hierarchy.

MAF evaluation source: Microsoft Learn Agent Framework overview (`https://learn.microsoft.com/en-us/agent-framework/overview/`, accessed 2026-10-01). Capabilities documented: agents/tools, sessions/context/middleware, explicit workflows/harness. **No comparative spike, license/dependency/native-cost review, or measurable benefit proven for Nexus yet.** Candidate boundary only: optional orchestration adapter behind a Nexus-owned contract if later durable workflows materially outperform a small native implementation. Initial read-only proof does not justify adding MAF; framework is not authority over Nexus policy/runtime/persistence.

### Phase report

- PHASE: 0 + 0.5 audit/baseline evidence captured; implementation not started.
- Files authored: ARCHITECTURE_AUDIT.md, BASELINE.md, IMPLEMENTATION_PLAN.md. Execution artifacts: docs/phase0-restore.log, docs/phase0-build.log, docs/phase0-test-results/.
- Components reused in verification: current DI/runtime, existing tests, WPF shell; added/refactored production components: none.
- Dependencies added/removed: none. Build: PASS. Tests: FAIL/ABORTED initial run; isolated evidence recorded.
- Regression Gate: **FAIL / incomplete E2E coverage**, with known pre-Agent failures; Chat/AI CHECK/Image/PDF/UI/runtime details in BASELINE.
- Performance/security impact of audit: no intended production behavior change; build/test/startup generate normal output/log/data artifacts. Risks discovered, not silently repaired.
- Checkpoint: audit branch at existing HEAD plus dirty working tree, no commit/stash/reset. Build can regenerate tracked artifacts; preserve preexisting source before any later implementation.
- Next recommended phase: baseline repair and focused verification, then explicit approval of smallest Phase 1 slice.
