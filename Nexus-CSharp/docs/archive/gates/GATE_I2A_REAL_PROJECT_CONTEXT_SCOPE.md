# Gate I2-A — real C# project context: scope/read-authority review

14 Oktober 2026 · **Stage 1 complete; Stage 2 menunggu satu owner approval.**
Read-only project understanding, bukan arbitrary-project editing. Visual polish
masuk Preview UX backlog. I2-B/Gate J dan native CF1 interaction gaps tetap HOLD.

## 1. Source findings dan reuse

Review terarah memakai actual source, bukan mengulang certification/OSS audit.

| Existing path di `src/` | Keputusan dan batas reuse |
|---|---|
| `03_App/Nexus.App/Views/ConversationView.xaml[.cs]` | **USE** existing composer/project picker, native controls dan context chips. Tambah izin baca/status/list kecil; tanpa composer kedua atau overlay. |
| `03_App/Nexus.App/ViewModels/ConversationViewModel.cs` | **ENHANCE** project/session revision, draft scopes, cancellation, existing Send/Stream pipeline. Current `SelectProjectAsync` hanya admission I1; belum ada grant baca real-project. |
| `03_App/Nexus.App/Services/AgentWorkspaceWorkflow.cs` | **USE unchanged untuk I1**. `ReadPinnedContext` mem-pin root/manifest dan hanya Alpha/Beta; tidak boleh dipakai sebagai grant real-project. |
| `02_Modules/Nexus.AI.EditingLite/Services/SafePath.cs` | **USE** public `SafePath.ResolveRelative` dan `SafePathLease`: containment, ADS/device/traversal/reparse rejection, NTFS/fixed-drive, handle final-path, single-link checks, directory pinning dan read-only file handles. Missing: standalone directory lease untuk safe discovery dan exported read identity facts. |
| `…/FileSnapshotService.cs`, `EditingWorkflowService.SafeWrite.cs:179–193` | **USE** hash/snapshot conventions. Existing scoped read terikat 2–3 `.cs`/4 KiB dan mutation workspace. Generic `ReadSnapshotAsync` memakai path-based reads; bukan safe project reader. Decoder-nya mengecek NUL sebelum UTF-16 BOM; tidak dipakai untuk klaim UTF-16 support. |
| `02_Modules/Nexus.AI.Llama/Services/{AttachmentContextBudgetService,WaterFillingBudgetAllocator}.cs` | **USE** existing reserved-first budget/history trimming, conservative estimate3 chars/token dan safe slicing. Budget bukan authorization. |
| `…/{AttachmentManagerService,Extractors/TextContentExtractor}.cs` | **USE** passive context envelope/chip conventions dan BOM knowledge. **REJECT untuk automatic project traversal**: attachment extensions termasuk `.env`, ordinary path opens tidak menyediakan project grant; permissive legacy encoding fallback tidak membuktikan byte-faithful source. Existing explicit attachments tetap berfungsi. |
| `03_App/Nexus.App/Services/ConversationRuntimeWorkflow.cs` + Llama pipeline/client | **USE unchanged** single runtime, request-local CTS dan current `StreamAsync`; tidak menambah Agent executor/tool. |
| `03_App/Nexus.App/ViewModels/FileManagementViewModel.cs` + FileUtilities Rapikan | **NOT NEEDED**: shell tab ke cleanup/organize; recursive enumeration utility tidak punya read grant/limits/identity fence, bukan secure source navigator. |

`Microsoft.CodeAnalysis`/`MSBuildWorkspace` tidak ditemukan pada project references.
Tidak ada existing general C# syntax/project reader yang cukup untuk authority ini.
Missing functionality: scoped read grant/revoke, pinned bounded directory discovery,
static metadata catalog, lazy safe source snapshots, bounded relevance/line provenance,
fresh-context dispatch fence dan isolation dari old project answers/context.

## 2. Satu desain: host-owned ProjectReadContextWorkflow

Grant baru **terpisah dari I1**. Runtime tidak menerima filesystem tool baru.

1. Folder dipilih native UI → hanya display path; belum enumerate/read konten.
2. Tombol **“Izinkan baca proyek”** menunjukkan canonical root, types/limits,
   exclusions dan sesi; explicit UI confirmation membuat opaque in-memory grant.
3. Host binds grant ke canonical root + NTFS volume/directory identity + session
   + context revision + expiry + fixed policy. Grant object tidak dibuat dari model
   text/path/Chat prompt, tidak dipersist, tidak masuk tool catalog atau model payload.
4. Satu active root/session. **“Cabut izin baca”**, project/session/navigation change
   atau expiry membatalkan read/preparation/related generation, membuang snapshots
   dan melepas leases. Tidak ada auto-grant untuk sesi/proyek berikutnya.
5. Current I1 edit readiness tetap berasal dari existing approved mutation profile.
   Arbitrary real root boleh memperoleh read grant, **tidak memperoleh edit grant**.

### Fixed host limits — bukan konfigurasi model

| Boundary | Initial limit |
|---|---|
| Root | Satu explicit local fixed NTFS folder; bukan drive root, UNC/network, device path atau reparse root/ancestor |
| Grant lifetime | Maksimum10 menit; snapshot maksimal60 detik, tidak diperpanjang diam-diam |
| Traversal | Maksimum6 directory levels,128 directories,4096 examined entries; cancellation checks; bukan unrestricted recursive scan |
| Catalog | Maksimum256 candidate `.cs`,32 metadata files total (`.sln`, `.slnx`, `.csproj`) |
| File read | Maksimum256 KiB per source/metadata file;8 MiB cumulative read bytes per grant, counted termasuk search/revalidation |
| Per question | Maksimum32 source candidates/2 MiB inspected; maksimum6 files dalam context, masing-masing≤160 selected lines |
| Model context | Maksimum24 KiB UTF-8 **dan**3072 estimated tokens termasuk provenance/envelope; turun sesuai actual existing attachment budget |
| Time | Discovery/preparation≤10 detik per operation dengan CTS; tidak mengubah Agent120 s/SDK15 s atau model caps |

Hard limit tidak diatasi dengan silent wider scan; kegagalan/partial unsupported
state dicatat. User memilih file tambahan dari catalog yang sama jika informasi
kurang. Scope/limit expansion di luar kontrak ini memerlukan approval baru.

Default exclusions, case-insensitive di setiap segment: `.git`, `bin`, `obj`, `.vs`,
`.idea`, `.vscode`, `node_modules`, `packages`, `TestResults`, caches, `.ssh`, `.aws`,
`.azure`, `secrets`, `credentials`; generated `.g.cs`/`.g.i.cs`/`.generated.cs`/
`.designer.cs`, `AssemblyInfo.cs`, `Secrets.cs`, `Credentials.cs`. Hanya empat
extensions di atas; README, JSON/config, `.env*`, `.user`, keys/certificates, binaries,
archives/logs tidak dimuat otomatis. Recognizable private-key material/hard-coded
credential values menyebabkan file ditahan sebelum payload, dengan alasan tanpa
menampilkan nilainya. Filename filters tidak diklaim mendeteksi semua secrets
yang mungkin ditanam di source; batas ini wajib dilaporkan.

### Safe reads dan identity

- Reuse existing file leases tanpa melemahkan validator. Tambah **read-only directory
  lease helper** dalam EditingLite assembly untuk memakai existing native handle
  validation, pin ancestors/current directory sebelum enumerating direct children.
  Reparse entries tidak di-follow; denial dicatat. Hardlinks dengan link count≠1
  ditolak. Path/model requests tidak menerima rooted path, `..`, ADS atau escapes.
- Metadata relative references dinormalisasi sebagai data, containment dicek dahulu;
  external references dicatat tanpa membuka target. Leases/final-path checks tetap
  wajib untuk setiap actual read; lexical containment saja tidak cukup.
- Record volume/file identity, length/mtime dan raw-byte SHA-256. Bounded handle
  reads dengan writer/delete sharing denied, pre/post identity checks dan safe
  root/catalog revision. Unreadable/access-denied/binary/oversized/invalid encoding
  adalah failure jelas; tidak berubah menjadi empty successful content.
- Strict UTF-8 (BOM/no BOM), UTF-16 LE/BE dengan BOM; unsupported/malformed encoding
  ditolak, bukan guessed Latin-1. Existing general attachment decoder tidak diubah.
- Sebelum dispatch, re-open/re-hash selected snapshots dan metadata lalu check
  grant/session/revision/expiry. Stale data ditolak; tidak auto-refresh lalu kirim
  diam-diam. Provenance selalu menyebut snapshot time, bukan klaim live filesystem.

## 3. Discovery/context: metadata data-only, tanpa workspace engine

`.sln` dibaca sebagai bounded solution-entry text; `.slnx`/`.csproj` memakai BCL
`XmlReader` dengan `DtdProcessing.Prohibit`, `XmlResolver=null`, document-size limits.
Literal in-root project/source references + supported SDK-style candidate `.cs`
inventory dikenali secara statis. Imports, targets, conditions/properties/globs,
analyzers, source generators dan compiled project graph **tidak dievaluasi**.
Unsupported/dynamic cases ditampilkan sebagai batasan; candidate source list tidak
diklaim sebagai exact compiled file set. Tidak ada build/restore/script/process,
network discovery/download atau membaca imported external project.

Relevance awal: explicit selected file IDs dari catalog + bounded lexical ranking
filename/path/question tokens; jika perlu, bounded source-text search tanpa index,
embeddings atau extra model selection calls. Cari “network/HTTP” dapat mengarahkan
ke actual client/transport code; login yang belum ditemukan berarti belum terbukti,
bukan dibuat-buat. Tidak membuat C# parser baru: class/method relationships dijelaskan
model dari inspected lines; tidak mengklaim semantic symbol/call graph.

Source fragments membawa relative path, exact line range, encoding, hash, snapshot
time dan read/truncation state. Line mapping dipertahankan pada context yang benar-
benar dialokasikan; potongan/truncation dicatat. Host membungkus repository text
sebagai untrusted data dan instruksi request-scoped meminta citations `[path:Lx-Ly]`,
menyebut files yang dibaca serta mengakui informasi kurang. Model citations dicek
terhadap supplied ranges; invalid citations tidak ditandai verified. Ini bukan
jaminan bahwa semua penjelasan model benar, dan model reliability dilaporkan terpisah.

Project source context bersifat ephemeral, bukan ordinary persisted attachment
yang bisa di-read ulang lewat raw path. Reuse budget/envelope UI, bukan legacy
attachment filesystem entry point. Project-aware request memakai current validated
evidence, **tidak membawa old project source/answers/attachments dari Chat history**.
History tetap terlihat, ordinary Chat/AI Check behavior dipertahankan. Reload/edit
resend wajib menyiapkan evidence baru, atau menolak jika grant sudah tidak berlaku.
Grant revoke/project/session switching menutup dispatch dan late persistence/UI updates
dengan captured session + generation revision; Agent CTS tidak ikut dicancel oleh
Chat/read CTS, existing Agent drain hanya mengikuti project/session navigation.

## 4. Initial real-project proof dan exact implementation ownership

Pilot yang diusulkan, bukan sudah diberi izin melalui picker:
`D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.AI.Llama`.
Actual review menemukan **22 non-generated `.cs` + `Nexus.AI.Llama.csproj`**.
ProjectReference ke Foundation/Contracts/PdfUtilities berada di luar root dan tetap
unread. Pertanyaan sederhana: file mana menangani HTTP, lalu fungsi
`LlamaClientService.StreamChatCompletionsAsync` (`Services/LlamaClientService.cs`,
actual overload mulai line74; HttpClient send line102–105).

Initial app dapat memberi grant pada satu user-selected folder yang memenuhi policy
di atas; proof pertama hanya pilot ini. Bukan fixed Alpha/Beta, bukan izin baca
seluruh `D:\NEXUS`/repository/user profile. Semua actual model proof menggunakan
existing owner/model; dua explicit read-only questions maksimum, bounded120 s per
proof, output64 tokens untuk proof tanpa perubahan production caps. OutputLimit/
insufficient answer dilaporkan, tidak ada retry/top-up untuk mengejar PASS. File
modification denial dibuktikan pada disposable shadow yang berbeda, bukan mengedit
pilot/user source. No project builds/writes; normal Chat persistence/evidence outputs
dibedakan dari source mutation.

**Existing files, tepat enam:**

1. `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs` — optional workflow,
   explicit grant/revoke, asynchronous bounded preparation, provenance/history fence.
2. `src/03_App/Nexus.App/Views/ConversationView.xaml` — functional read controls/status
   dan source list/context references dalam existing composer area; no CF3 redesign.
3. `src/03_App/Nexus.App/Views/ConversationView.xaml.cs` — native permission confirmation
   dan selected file glue, existing input/focus behavior retained.
4. `src/03_App/Nexus.App/App.xaml.cs` — register/inject read workflow only.
5. `src/03_App/Nexus.Integration.Tests/ConversationViewModelTests.cs` — project context
   Send/reload/session/permission integration, preserving existing Chat assertions.
6. `src/03_App/Nexus.Integration.Tests/ArchitectureGovernanceTests.cs` — DI/new scoped
   read interface assertions, unchanged singleton runtime ownership contract.

**New files, tepat lima:**

- `src/02_Modules/Nexus.AI.EditingLite/Services/ReadOnlyPathScope.cs` — additive directory
  pin/identity helpers using existing native validator; existing SafePath/SafeWrite unchanged.
- `src/03_App/Nexus.App/Services/ProjectReadContracts.cs` — grant/limits/catalog/snapshot/
  provenance, no mutation profile or serializable model grant.
- `src/03_App/Nexus.App/Services/ProjectReadContextWorkflow.cs` — discovery/selection/
  read/revalidation/revoke; no runtime executor.
- `src/02_Modules/Nexus.AI.EditingLite.Tests/Services/ReadOnlyPathScopeTests.cs`.
- `src/03_App/Nexus.Integration.Tests/GateI2AProjectReadTests.cs`.

Docs/evidence/external verified-Job proof tooling terpisah. Tidak ada package/csproj,
AgentWorkspaceWorkflow/VM/diff review, provider/policy, orchestrator, shared runtime,
SDK environment atau mutation-contract changes yang diminta. Jika scope tidak cukup,
jangan memperluas diam-diam.

## 5. OSS choices, risks dan bounded acceptance

| Candidate | Decision / supply-chain / execution |
|---|---|
| Nexus + .NET8 BCL XML/encoding/crypto + Win32 handle checks | **USE/ENHANCE**; sudah tersedia, tidak menambah package. XML data-only dan read handles; no MSBuild execution. |
| Pinned OpenCode/Cline context/navigation UX audit | **ADAPT concepts** saja: explicit file context/provenance. Tidak mengambil executor/permissions atau mengulang research. |
| Microsoft Roslyn syntax APIs | **NOT NEEDED initial slice**; MIT, mature .NET ecosystem, standalone syntax parse tidak perlu workspace build. References belum ada; dependency/semantic analysis tidak diusulkan sekarang. |
| MSBuildWorkspace/Microsoft.Build evaluation | **REJECT initial loading**: repository imports/targets/tasks, environment/SDK dependencies, unsafe evaluation surface dan unnecessary supply-chain work. |
| Existing file organizer recursive scan / general attachment `.env` support | **REJECT as project authority**; tidak mempunyai grant/pinning/exclusion boundary ini. |

Focused reference checks: [XmlReader DTD API (.NET8)](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-8.0),
[Roslyn MIT license](https://github.com/dotnet/roslyn/blob/main/License.txt).
Tidak ada OSS install/run/dependency addition pada Stage1.

Risks: filesystem races/replacement (pinned identity + rehash); secrets/source prompt
injection (restrict types/exclusions, sensitive-material rejection, untrusted envelope,
no tools); limit exhaustion (hard bounds/clear partial failures); stale/scope leak
(ephemeral grant/context and dispatch/persistence fences); incomplete graph/symbol
understanding (explicit static/citation limits); model reliability (separate actual
model evidence, no fabricated comprehension).

- **Slice A RED→GREEN:** grant/revoke/session TTL, empty/single SDK project, static
  `.sln/.slnx/.csproj`, literal/external refs, limits/exclusions, malicious DTD/Import
  no execution; root/ancestor/child junction/symlink swaps, ADS/traversal/hardlinks,
  inaccessible directory/file, cancellation/handle cleanup.
- **Slice B RED→GREEN:** strict encoding/Unicode/newline/line provenance, oversized/
  binary, read-time replacement, stale hash/identity/metadata, revoked permission,
  secret filenames/material, misleading repository instructions; final payload
  matches only actual read+allocated fragments, insufficient context explicit.
- **Slice C RED→GREEN:** existing Chat payload receives evidence; no history/attachment
  cross-project leak including reload, new/temporary session and late completion;
  grant/read CTS vs Agent CTS; unsupported edit remains disabled; ordinary Chat/
  AI Check/single-runtime/CF2 review regression when integration stable.
- Actual WPF pilot picker→explicit grant→genuine source list→real class question→
  actual local Chat response with validated references. Deterministic payload tests
  dan two planned live read-only questions direkonsiliasi terpisah; no mock response
  disebut model execution. Show denial/revoke/stale proof and zero source writes,
  project process launches/network discovery/restore/build.

## 6. Approval request dan Stage 1 preservation

**Mohon satu approval atas `I2A-RO-V1`: host-owned read grant/limits/exclusions,
pilot/real-root interface, enam existing + lima new files, tiga slices dan bounded
actual model proof di atas.** Approval coding scope ini tidak menggantikan izin
baca pengguna di actual Nexus UI. Setelah disetujui, implementasikan A/B/C langsung
dengan targeted/impacted checks dan report, tanpa approval loop untuk surgical fixes.

Stage1:670 source/report identities cocok dengan CF2, staged SHA tetap
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
Only scope doc baru; **0 production edits,0 tests/model generations**. Ownership/hash
receipt: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2a-scope\scope-seal.json`.
CF1 native DPI/IME/screen-reader/keyboard HOLD dan visual backlog tidak berubah.
