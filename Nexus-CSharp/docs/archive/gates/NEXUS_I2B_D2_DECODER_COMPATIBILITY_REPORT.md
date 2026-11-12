# NEXUS — I2-B-D2 Exact b11057 Decoder Compatibility

16 Oktober 2026 · OpenCode / `openai/gpt-6.1-sol` · decoder-only investigation, tanpa production edit atau inference.

## 1. Verdict

**INCONCLUSIVE untuk causal decoder replay.** Identitas installed executable/DLL chain, model, embedded template dan tokenizer metadata berhasil dipin. Native decoder entry points dan kebutuhan state-nya ditemukan pada exact commit yang cocok. Namun **tidak tersedia verified ABI-compatible offline driver** untuk menjalankan C++ decoder tersebut dalam tooling yang terverifikasi pada sesi ini; historical captures juga tidak memuat raw decoder input dan parser/generation-prompt state.

Known-valid Gemma4 dialect control disiapkan dari golden test exact commit dan ditandai terpisah. **Control belum dieksekusi oleh deployed decoder: tidak diklaim recognized maupun rejected.** Kedua historical failures tetap `length`/256/tanpa native `tool_calls`; tidak diperbaiki, dilengkapi prefix-nya, atau diinterpretasi ulang sebagai success.

**Tidak ada production remediation yang terbukti. I2-B HOLD dan Gate J HOLD.** Stop pada missing discriminator yang spesifik di §7, sesuai approval. Ini bukan audit Harness ulang atau full certification.

## 2. Scope dan evidence

- Policy `Nexus-CSharp/AGENTS.md` dan `docs/NEXUS_ENGINEERING_POLICY.md` dibaca. Existing D1/S1 report, seal, raw capture identities dan preservation manifest digunakan kembali; targeted/impacted suites yang sudah selesai tidak diulang.
- Evidence root **E**: `C:\Users\Syand\AppData\Local\Temp\opencode\i2b-d2-20261016`.
- Historical capture root **H**: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2b`.
- Installed runtime **R**: `D:\NEXUS\Engine\LlamaServer\b11057`.
- Exact reference commit **C**: `59657a613ab0fa4ab327d6c790123dff30bfbd67`.
- Only executable invocations: **R/llama-server.exe --version** dan **--help**, masing-masing satu kali, tanpa `-m`, tanpa request HTTP, tanpa model load/tensor execution. Keduanya exit0 melalui existing approved `CertificationJob`, cwd E, bounded wait30s, log reader complete, remaining owned membership kosong. Tidak ada server normal, CLI generation, App, atau Agent yang diluncurkan.
- GGUF dibaca sebagai **data biner statis**, bukan melalui llama model-loader: streaming SHA seluruh berkas, lalu decoding 56 metadata entries sebelum tensor descriptors/data. Tidak memetakan tensor atau mengalokasikan inference context.
- Source rujukan dibaca melalui HTTPS pada **full commit C** dan disimpan di E/source dengan URL/size/SHA receipts. Ini source reference untuk static analysis, bukan install dependency atau penggantian runtime. `D:\NEXUS\Audit\llama.cpp-master` tidak dipakai sebagai implementation pengganti yang diasumsikan ekuivalen.
- Tooling external hanya stdlib Python + approved Job helper existing. Tidak ada native driver/wrapper baru yang dikompilasi, dependency diinstall, REA call, model generation, atau actual Agent run.

## 3. Exact deployed identities

### 3.1 Runtime dan build

`E/llama-server-version.log` merekam:

```text
version: 0.4.1-dev (build 11057, commit 59657a613)
built with Clang 20.1.8 for Windows x86_64
```

GitHub ref `https://api.github.com/repos/ggml-org/llama.cpp/git/ref/tags/b11057` resolves ke full commit C. Commit ini cocok dengan version output, static DLL string, dan `system_fingerprint=b11057-59657a613` pada kedua historical responses.

| Artifact di R | SHA-256 | Bukti peran |
|---|---|---|
| `llama-server.exe` | `c72f76994f8d680d7b423ff68662de33857d2a2b001465de55952873d4ef3df3` | Cocok hard-coded Nexus `NativeToolCapability.EngineHash`; PE imports `llama-server-impl.dll`. |
| `llama-server-impl.dll` | `4aa7c3d0374198b745474bbf271581cc8b4a5f4c5f7358ba594d5b3bf961ee45` | Imports `llama-common.dll`, `llama.dll`, ggml dan MTMD; server request/result pipeline. |
| `llama-common.dll` | `45c7ca4be152ce8da7145351d167151a0eb6aec0ed074ce43d5c0f7f46a42ab4` | **3.335 named exports**, termasuk actual decorated C++ template/PEG decoder symbols. |
| `llama.dll` | `ee6bdf05451b980d844540b62a89a80288940bc5ff7d1667293f3d88d367847c` | Model/vocabulary/tokenizer C APIs; bukan common tool-call decoder C API. |
| `llama-tokenize.exe` | `c19638d896fbda97f08073cc0380b8ec6b28903723ea1144b8d6c08cff4e3597` | Installed tokenizer utility, statically inspected; tidak dijalankan untuk retokenize captures. |

PE machine `0x8664`, imports MSVC runtime (`MSVCP140`, `VCRUNTIME140`) dan dynamic ggml/common libraries teramati. Ini konsisten Clang Windows/MSVC ABI; **bukan** bukti bahwa arbitrary guessed struct layout/calling convention aman dipanggil via ctypes/PInvoke.

`E/baseline.json` mengidentifikasi **50 EXE/DLL files** di pinned directory. `E/gate-h-runtime-chain-comparison.json` membandingkan **30/30** runtime files yang tercantum pada Gate H frozen fixture manifest; semuanya cocok, termasuk server/impl/common/llama chain. Ini lebih kuat daripada memeriksa launcher EXE saja. Tidak mengklaim semua system DLL atau historical process environment telah dipin ulang.

**Build configuration yang diketahui:** build number/commit/compiler/OS/architecture, dynamic-library topology, exported API dan file hashes. **Belum tersedia:** installed CMakeCache/compile_commands/full build flags/linker/PDB provenance. Upstream source at C dan matching build-info bukan byte-for-byte reproducible-build attestation. Dua tebakan nama workflow upstream menghasilkan HTTP404; disimpan sebagai failed fetch receipts, tidak dijadikan bukti build flags.

### 3.2 Model, embedded template dan tokenizer

Model: `D:\NEXUS\Models\AI Workspace\Gemma-4-E4B\gemma-4-E4B-it-Q4_K_M.gguf`.

| Identity | Observed value |
|---|---|
| File SHA-256 | `519b9793ed6ce0ff530f1b7c96e848e08e49e7af4d57bb97f76215963a54146d` — cocok pin Nexus |
| File bytes / GGUF | 4.977.169.568 bytes; GGUFv3; 720 tensors declared; 56 metadata entries |
| Architecture/tokenizer | `general.architecture=gemma4`; `tokenizer.ggml.model=gemma4`; 262.144 vocabulary pieces |
| Embedded template | `tokenizer.chat_template`, 16.804 characters; UTF-8 content SHA **`33204f1acb5bd0002713e16a593847f24ceeafe711ed88bda2a352dc996a3373`** |
| Template copy | `E/embedded-chat-template.jinja`, raw string bytes tanpa template execution |
| BOS / EOS metadata | ID2 `<bos>`; ID106 `<turn|>`; add_bos=true; add_space_prefix=false |

`E/gguf-metadata.json` menyimpan offset/length/type dan SHA **encoded KV entry** untuk tabel tokenizer. Ini berbeda dari hash konten template di atas:

| Encoded metadata entry | SHA-256 |
|---|---|
| `tokenizer.ggml.tokens` | `44ae27c2a2e79a7931d1a06af86d59ab09c7da8120c510bd0969ddfbb5c0a130` |
| `tokenizer.ggml.scores` | `7b5666c53a2ae0f181d734e2234ba9f2dde11149d7452bda1d490129fda6b0a9` |
| `tokenizer.ggml.token_type` | `82783d20412a9738d83a0aac7b0dde23db3da6bea342406ea1baaffa35d9984a` |
| `tokenizer.ggml.merges` | `43cb60b011c0cc8e8b9a98aad7d325717b5b71dc309893550fede277396f4ff4` |
| `tokenizer.chat_template` | `6b57a9981784dccbd9b60dd92d7ba7af923f6167d0316090a49bae8591232de3` |

Relevant pieces: ID48 `<|tool_call>`, ID49 `<tool_call|>`, ID50 `<|tool_response>`, ID52 `<|"|>`, ID100 `<|channel>`, ID101 `<channel|>`, ID105 `<|turn>`, ID106 `<turn|>`. Stored types48–52 adalah USER_DEFINED4; types2/105/106 CONTROL3. **Stored types bukan final runtime EOG/control map.** Exact `src/llama-vocab.cpp:2110–2149` memilih BPE+Gemma4 pre-tokenizer; `:2869–3025` mengonstruksi EOG map dan compatibility overrides. `<tool_call|>` menutup sintaks call; itu sendiri bukan bukti EOG/finish success. Existing 8-line R/server_err.log memuat token-type override warnings, tetapi tidak diberi attribution ke dua I2-B runs tanpa receipt yang mengikatnya.

### 3.3 Intended configuration versus captured effective state

Nexus `GenericLlamaEngine.cs:72` memakai `StartServerAsync(modelPath,"auto",contextSize)`. `LlamaServerManager.cs:174–190` membentuk `--jinja` dengan `--reasoning auto --reasoning-format deepseek`, thread/batch/parallel fixed tuning dan context≤32768; tidak memasok explicit template override pada route auto.

Pada C, `common/chat.cpp:757–854` membaca default/tool_use template dari model bila override kosong; default source pada GGUF di atas mengandung exact Gemma4 detector literal. `:1193–1201` memilih `common_chat_params_init_gemma4`. Embedded template juga memiliki OpenAI tool-message branch, sehingga legacy workaround detector tidak boleh diasumsikan aktif hanya karena nama model.

Kedua request tersegel menyatakan `tool_choice=required`, `enable_thinking=false`, `stream=false`, `max_tokens=256`, sole tool `nexus_apply_source_plan`. C `server-common.cpp:1288–1382` meneruskan inputs/tools/kwargs, generates parser dan grammar, lalu `server-schema.cpp:295–324` memuatnya ke task parser.

**Batas pembuktian:** tidak ada historical `/props`/per-request `chat_parser`, `generation_prompt`, atau full launch environment dalam empat captures. Nexus launcher tidak membersihkan inherited environment pada source `LlamaServerManager.cs:334–344`; runtime C `common/arg.cpp:768–804` menerapkan system config/environment sebelum CLI. Karena itu selection embedded adalah **source-supported intended path**, bukan pengganti snapshot effective parser state dari kedua run. Tidak ada env/config pengguna yang diubah atau current user server yang di-query untuk mengisi kekosongan historis.

## 4. Decoder entry points dan replay feasibility

Source receipt files memakai URL `https://raw.githubusercontent.com/ggml-org/llama.cpp/C/<path>` dengan C diganti full commit di §2. Bytes/SHA dan failed fetches tersimpan di E/*-fetch.json. Selected source references berikut berasal dari commit itu, bukan master yang lebih baru.

### 4.1 Native pipeline yang ditemukan

```text
oaicompat_chat_params_parse(request, effective server_chat_params)
  -> common_chat_templates_apply(...)
     -> Gemma4 specialized template/parser/grammar generation
  -> task.chat_parser_params {format, generation_prompt, parser arena, ...}
  -> task_result_state::update_chat_msg(generated_text, is_partial)
     -> common_chat_parse
        -> common_chat_peg_parse(parser, generation_prompt + input, flags)
        -> Gemma4 mapper -> common_chat_msg
  -> server_task_result_cmpl_final::to_json_oaicompat_chat
```

- `common/parsers/gemma4.cpp:151–302`: exact Gemma4 parser creation, preserved markers, required-tool grammar; call syntax `<|tool_call>call:<name>{...}<tool_call|>`.
- `common/chat.cpp:1439–1519`: decoder accepts **text + parser params**, prepends generation_prompt; empty parser arena falls back to pure-content parsing *inside the shipped implementation*. Audit ini tidak menggunakan branch itu sebagai fallback untuk menerima call text.
- `tools/server/server-task.cpp:162–176`, `server-task.h:365–367`: accumulated generated text dan exact task parser params masuk decode.
- `server-task.cpp:414–430`: final finish default `length`; hanya EOS/stop-word mengubahnya menjadi `stop`/`tool_calls`, bergantung parsed message. Complete-looking call fragment bukan otomatis native-success.

Actual export dari `llama-common.dll`:

```text
?common_chat_parse@@YA?AUcommon_chat_msg@@AEBV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@_NAEBUcommon_chat_parser_params@@@Z
```

`common_chat_peg_parse`, `common_chat_templates_init`, `common_chat_templates_apply`, dan Gemma4 initializer juga diekspor sebagai decorated C++ symbols. Presence dibuktikan dengan static PE export-table inspection, **tidak dipanggil menggunakan guessed ABI**.

### 4.2 API/metode yang benar-benar tersedia

| Surface | Feasibility dalam scope ini |
|---|---|
| `common_chat_templates_init(nullptr, explicit_template, bos, eos)` + apply + parse | Source menunjukkan pure text/template path memungkinkan driver tanpa inference; tidak perlu mengganti decoder. Tetapi typed C++ structs/STL/arena lifecycle membutuhkan compiled ABI-compatible caller yang terverifikasi. Driver itu tidak tersedia dalam pinned distribution/tooling yang diverifikasi. |
| `llama-common.dll` C++ exports | Ada, namun bukan C ABI `parse(json)->json`. Tidak sah membangun `std::string`/vector/arena manual dengan asumsi layout atau menganggap raw pointer call cukup. |
| `llama.dll` `llama_chat_apply_template` | C export ada, tetapi legacy formatter, **bukan tool-call decoder** dan bukan pengganti specialized common Gemma4 parser. |
| `llama-server --help/--version` | Benar-benar dieksekusi tanpa model; tidak ada decoder replay mode pada help yang diperiksa. Tidak mencoba unknown flags atau menjalankan server/model. |
| Server `/apply-template` | C `server-context.cpp:5059–5069` mengembalikan **prompt saja**; bukan parsed tool call atau serialized arena. Bergantung initialized server metadata; tidak dipanggil pada aplikasi pengguna. |
| Server `/tokenize` / installed tokenize CLI | Tokenization bukan tool-call decode; retokenization text tidak mengembalikan original sampled token IDs atau stripping/stop history. Tidak digunakan sebagai rekonstruksi. |
| `tests/test-chat.cpp` pure parser tests | Source ada di C, tetapi tidak ada test-chat executable terverifikasi pada R. Compiler/driver suitable tidak ditemukan di PATH/standard Visual Studio locations yang diperiksa; ini bukan inventory seluruh PC. Tidak menginstall toolchain. |

R tidak memiliki `llama-template-analysis.exe`; tool bernama itu ada di parent non-pinned directory, **tidak digunakan**. Tidak memakai binary/runtime versi lain lalu mengklaim ekuivalensi.

## 5. Known-valid control dan historical capture classification

### 5.1 Separately labelled control

`E/control-gemma4-closed-call.txt` berisi exact dialect sample dari `tests/test-chat.cpp:3007–3012` pada C:

```text
<|tool_call>call:get_time{city:<|"|>London<|"|>}<tool_call|>
```

SHA-256 **`8dc36fbf2b8e980a53d5e2dca3aa797d350ff9004e2078653b014ec848e9351d`**. Golden tool schema `get_time(city:string)` dan expected `{city:"London"}` tercantum pada `E/control-classification.json` serta source `:511–524`. Sample menggunakan marker/string syntax yang juga dirender embedded E4B template `:243–257`.

Call syntax lengkap/tertutup, bukan prefix parsial. Golden family test menggunakan Gemma4 31B template; **golden source expectation tidak diklaim sebagai execution dengan embedded E4B ini**. Tidak ada generation, EOG observation, atau synthetic `finish_reason=tool_calls` yang direkayasa. Control tidak dikirim ke Nexus tool executor atau layanan nyata.

| Possible outcome | Actual status |
|---|---|
| Valid control recognized by deployed decoder | **NOT ESTABLISHED** |
| Valid control rejected by deployed decoder | **NOT ESTABLISHED** |
| Inconclusive: verified decoder driver/state unavailable | **YES — decoder invocation count0** |

### 5.2 Captures tidak diubah

| Capture | Immutable file SHA-256 |
|---|---|
| `native-1-request.json` | `097ddec7c2d22451f124229ce2d4facde3dd3739522b78bf956a12efbc2887b9` |
| `native-1-response.json` | `7527db87acc4f455cb23e31980166ad06f64b2ba607dd3bf5a4ecbbf5648a201` |
| `native-2-request.json` | `b12184b205679fcdc2308e464167ca2e653d66d2b27ef07b7b19db076c70e737` |
| `native-2-response.json` | `6369fa77733497a2656beb5027bcd26fc71f7f7f295d64a335012bddf9315ca8` |

`E/historical-capture-classification.json` records both envelopes and literal observations:

| Fact | Run1 | Run2 |
|---|---:|---:|
| Prompt / completion tokens | 1033 /256 | 1021 /256 |
| finish_reason / native tool_calls | length /absent | length /absent |
| Decoded JSON content characters | 1000 | 841 |
| Content starts with bare `nexus_apply_source_plan{` | yes | yes |
| Literal opening `<|tool_call>call:` present | no | no |
| Literal closing `<tool_call|>` count | 1 | 1 |
| Token IDs, serialized parser, generation_prompt recorded | no | no |
| New deployed decoder replay result | **none/inconclusive** | **none/inconclusive** |

Character counts/hash of decoded JSON strings are labelled separately from original capture-byte hashes. Literal-marker observations **are not a parser, accepted argument extraction or proof of the original token stream**. Tidak menambahkan missing marker/prefix, memotong text sesudah closing marker, mengubah finish_reason, atau merakit synthetic historical native calls.

`message.content` adalah output **setelah** server result formatting. Source `server-task.cpp:414–430` dapat memakai parsed message atau fallback raw content; empat captures tidak menentukan intermediate path/state-nya. Mengasumsikan content itu persis seluruh pre-decoder generated_text tanpa bukti akan membuat replay spekulatif.

## 6. Hypotheses — supported versus unresolved

| Hypothesis | Evidence | Verdict |
|---|---|---|
| Output benar-benar mencapai generation cap/termination limit | Both native responses length/256; exact serializer default/stop handling cocok. | **Established immediate failure**, bukan causal explanation mengapa termination tepat tidak terjadi. |
| Nexus strict wire parser salah menolak valid native tool_calls | Captures tidak memiliki tool_calls; D1 accepted known-good envelope/fake tool dan rejected lengths. | Tidak didukung oleh captures; no parser weakening warranted. D1 proof tetap historical, tidak dijalankan ulang. |
| Deployed b11057 sama sekali tidak mendukung Gemma4 tool parsing | Actual exports include Gemma4 initializer/mapper; exact source specialized grammar/parser dan golden cases ada. | Static evidence menolak klaim “tidak ada implementasi”; **tidak membuktikan runtime recognition/reliability**. |
| Runtime DLL chain diam-diam berbeda dari certified reference | 30 current files cocok Gate H frozen runtime manifest, launcher/model pins cocok. | Tidak ada observed drift pada chain yang dibandingkan; tidak menyatakan semua environment/config historis diketahui. |
| Template/decoder mismatch pada effective I2-B request | Embedded template detector cocok, tetapi effective arena/generation_prompt/config tidak ditangkap dan driver belum ada. | **Open/inconclusive**; source route saja tidak menutup hipotesis. |
| Model menghasilkan dialect salah atau berhenti tidak tepat | Bare tool name/closing marker lalu continuation terlihat di content; opening marker tidak terlihat. | Plausible, **belum causal proof** karena raw pre-decoder string/token/stop-preservation history tidak tersedia. |
| Metadata token type/EOG mismatch menyebabkan masalah | GGUF stored control/user-defined types dan source runtime overrides teridentifikasi. | Mechanism yang perlu dibedakan, **bukan proven bug**. Stored types saja tidak menentukan runtime flags; no token IDs untuk dua generations. |
| Menaikkan cap, mengganti model/runtime/template, atau stop-string baru menyelesaikan root cause | Tidak ada controlled comparison/replay yang membuktikan itu. | **Not established; tidak diimplementasikan atau diautorisasi otomatis.** |

Native text decoder memakai text+params, bukan token IDs secara langsung. Jadi token IDs **bukan syarat mutlak** bagi sebuah decoder unit test bila exact pre-decoder text dan state tersedia. Token IDs diperlukan untuk menelusuri generation/detokenization/special-token/EOG hypothesis; retokenization captures tidak menggantikan evidence itu.

## 7. Exact missing discriminator dan next decision

Missing discriminator sekarang spesifik: **hasil actual exact-DLL decoder untuk source-grounded closed-call control, menggunakan request-derived Gemma4 parser arena/generation_prompt state yang tercatat dan valid.** Untuk menyebut replay historical exact, diperlukan original pre-decoder generated_text + state atau pembuktian equivalence/regenerasi state; empat HTTP captures saja belum memenuhi itu.

**Tidak ada minimal production remediation yang dapat dipilih secara evidence-based dari D2 ini.** Pertahankan parser/default-deny/cap/runtime/template. Observational artifact yang dibutuhkan adalah verified native driver, bukan patch writer/approval atau penggantian runtime.

**Satu keputusan owner yang diperlukan bila ingin melanjutkan:** setujui scope terpisah untuk **penyediaan dan verifikasi ABI-compatible decoder-only driver/test artifact** yang memakai exact R/llama-common.dll chain dan headers pada C, menghasilkan parser-state receipt serta menjalankan satu control di §5. Model loader/generation tidak boleh terhubung. Driver/toolchain harus tersedia dan diverifikasi sebelum eksekusi; toolchain/dependency baru, runtime rebuild/replacement, produksi patch dan live-generation allocation **tidak termasuk** keputusan ini. Jika driver tersebut tidak dapat disediakan dalam batas itu, tetap berhenti pada inconclusive ini—tidak mengulang audit yang sama.

## 8. Preservation, regression debt, dan seal

`E/preservation.json` memverifikasi **0 perubahan** pada:

- **2.584** current Nexus source/document identities, termasuk C1.1 dan tiga D1/S1 paths; **139** default output identities.
- **50** pinned runtime EXE/DLL hashes; model GGUF hash tetap `519b9793…`.
- Empat original historical capture hashes, delapan protected reports/archives termasuk D1/S1 source/evidence seals dan Gate H/I2-B archives.
- Staged patch tetap **`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`**. Full pre-inspection status/staged/unstaged snapshots ada di E. Tidak ada reset/restore/stash/clean/commit/merge.

Hanya report ini dan external evidence/tooling baru yang ditulis. Static source downloads diberi full-commit URLs/SHA receipts; tidak dipasang ke Nexus atau runtime. Two no-model CLI inspection receipts/logs, static PE exports, metadata, control dan classifications disimpan. Source-fetch404 bukan inspection success; tidak ada fabricated decoder PASS/TRX.

Existing **empat impacted failures** dari D1/S1 tetap unresolved regression debt dan wajib ditutup sebelum future certification:

1. `GateIChatFirstWorkspaceTests.CF1_ExplicitCodeContextEntersChatPayload_QuestionTextNeverApproves_AndEditExcludesHistory` — missing attachment context.
2. `ConversationViewModelTests.ConversationView_TopBarMenuButton_HidesInAiCheckMode` — current XAML element lookup.
3. `ConversationViewModelTests.I2A_ExplicitGrantInjectsActualSource_ExcludesHistoryAndAttachments_RevokeBlocksNextRequest` — status expectation setelah revoke.
4. `NativeToolSelectionTests.BoundCompilerRepair_FiltersOtherDescriptors_AndFinalNoneStaysNone` — null compiler observation.

Tidak diulang, dikecualikan, dilemahkan, atau dilabeli PASS oleh D2. D1/S1 impacted result tetap **314/318**, targeted25/25; bukan fresh execution sesi ini. Historical I2-B allocation tetap2/4 generations,2/2 Agent runs consumed; **D2 menambah0/0**.

Evidence archive dan report hash dicatat di `E/final-seal.json` di luar archive. **STOP — decoder causal result inconclusive; no automatic remediation. I2-B HOLD, Gate J HOLD.**
