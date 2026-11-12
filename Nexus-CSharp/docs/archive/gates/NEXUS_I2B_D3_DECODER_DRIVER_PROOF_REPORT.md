# NEXUS — I2-B-D3 Bounded Exact Decoder Driver Proof

17 Oktober 2026 · OpenCode · external decoder-only experiment, conditional on toolchain preflight.

## 1. Keputusan eksperimen

**Tahap A BLOCKED: tidak ada verified Windows x64/MSVC-ABI development toolchain yang ditemukan dalam discovery scope. Tahap B tidak dijalankan. Decoder result INCONCLUSIVE.**

Tidak ada driver C++ ditulis/dikompilasi, positive/negative control dieksekusi, DLL parser dipanggil, atau model dijalankan. Ini **bukan positive control rejected**, bukan defect decoder, dan bukan root cause I2-B. Approval bersyarat tidak dipakai untuk menginstal compiler, mengambil dependency, menebak ABI, atau mencoba driver kedua.

**Decoder experiment ditutup untuk environment saat ini.** Tidak meminta putaran decoder investigation baru. Satu rekomendasi engineering berbiaya terbatas untuk menuju actual I2-B ada di §7; pelaksanaannya membutuhkan allocation terpisah. **I2-B HOLD; Gate J HOLD.**

## 2. Baseline dan metode

- Dibaca: `Nexus-CSharp/AGENTS.md`, `docs/NEXUS_ENGINEERING_POLICY.md`, `NEXUS_I2B_D2_DECODER_COMPATIBILITY_REPORT.md`, D2 seal/validation dan control record.
- Evidence D3 **E3**: `C:\Users\Syand\AppData\Local\Temp\opencode\i2b-d3-toolchain-preflight`.
- Evidence D2 **E2**: `C:\Users\Syand\AppData\Local\Temp\opencode\i2b-d2-20261016`.
- Timestamp collector: `2026-10-17T09:43:42.922424+07:00` pada `E3/baseline.json`.
- Exact source commit yang diwajibkan: **`59657a613ab0fa4ab327d6c790123dff30bfbd67`**. Pinned deployed chain: `D:\NEXUS\Engine\LlamaServer\b11057` (**R**).
- D2 report SHA diverifikasi **`a00bbc323dc74fdf3c0090ade25a199e4b54d0a965f585fdd4335de09d69418a`**; evidence archive SHA **`184e2392838fd3a07e70dd94f597441d42b108faad3b8ed4396a0633caa85970`** cocok seal.
- D2 static source/GGUF/PE/runtime proofs **direuse**, bukan diulang. Tidak ada metadata GGUF dibaca kembali, PE exports diparse lagi, runtime `--help/--version` diulang, source diunduh, atau audit Harness/REA.
- Fresh activity hanya bounded toolchain discovery, read-only registry/environment inspection, source/evidence preservation checks dan report/evidence writing. Tidak menjalankan installer/unpacker/compiler/runtime, mengakses server HTTP pengguna, atau mengubah PATH/registry/config.

## 3. Tahap A — toolchain feasibility

### 3.1 Fresh discovery results

`E3/toolchain-preflight.json` mencatat raw discovery dan verdict **`BLOCKED_NO_VERIFIED_MSVC_ABI_TOOLCHAIN`**.

| Pemeriksaan | Actual result | Implikasi |
|---|---|---|
| PATH resolution | `cl.exe`, `clang-cl.exe`, `clang++.exe`, `lld-link.exe`, `link.exe`, `lib.exe`, `llvm-lib.exe`, `cmake.exe`, `ninja.exe`, `vswhere.exe`, `g++.exe`, `gcc.exe`: seluruhnya unresolved | Tidak ada compiler/linker/library tool yang dapat dipanggil dan diverifikasi untuk driver. CMake/Ninja sendiri bukan syarat ABI, tetapi juga tidak tersedia pada discovery ini. |
| Developer environment | `VCToolsInstallDir`, `VCINSTALLDIR`, `VSINSTALLDIR`, `WindowsSdkDir`, `WindowsSDKVersion`, `INCLUDE`, `LIB`, `LIBPATH`: unset | Tidak ada configured MSVC STL/CRT/SDK include dan library paths. Unset environment saja tidak dipakai sebagai satu-satunya proof. |
| Registry | **36 probes = 9 keys × HKLM/HKCU × 32/64-bit view; semuanya not_present, 0 unreadable** | Tidak ditemukan registration development roots atau App Paths yang bisa memulihkan compiler di luar PATH. |
| Standard installation roots | Visual Studio di Program Files/x86, LLVM, Windows Kits di Program Files/x86, `C:\BuildTools`, `C:\LLVM`, `C:\msys64`, `C:\mingw64`, user Scoop, ProgramData Chocolatey, user Programs/LLVM: absent | Tidak ada candidate development installation dari lokasi yang diperiksa. |
| Bounded dedicated Glob searches | Compiler/linker/build-tool filenames dicari di user `AppData\Local\EngineeringTools`, user `AppData\Local\Programs`, dan `D:\Software`: **0 matches** | Tidak ditemukan portable toolchain dalam root yang diperiksa; arsip/installer tidak dibuka atau dieksekusi. |
| Installed product registry | 27 matching runtime/hosting entries, termasuk **VC++2022 x64 Minimum/Additional Runtime14.44.35211** dan **VSTA2017/2022 hosting support** | Runtime redistributable dan Visual Studio Tools for Applications **bukan bukti** tersedianya native C++ compiler/STL/SDK/linker. Tidak dianggap preflight PASS. |

Registration keys mencakup `VisualStudio\SxS\VC7`, `SxS\VS7`, `Setup\Instances`, `Windows Kits\Installed Roots`, `Microsoft SDKs\Windows\v10.0`, serta App Paths untuk cl/clang-cl/cmake/vswhere. Scope dan hasil file searches disimpan di `E3/discovery-scope.json`.

**Batas kesimpulan:** bounded discovery ini tidak membuktikan tidak ada compiler di setiap file/drive/arsip PC. Ia membuktikan **tidak ada candidate compiler + SDK development environment yang dapat diverifikasi dari roots/registrations tersebut**. Tidak ada hidden/custom path diberikan owner yang dapat dijadikan evidence baru. Eksperimen tidak memperluas pencarian ke seluruh data pribadi untuk memaksakan kandidat.

### 3.2 ABI dan dependency requirements — evidence reuse, bukan PASS baru

D2 sudah membuktikan:

- R adalah Windows x64, **build11057 / commit59657a613 / Clang20.1.8**.
- `llama-common.dll` SHA **`45c7ca4be152ce8da7145351d167151a0eb6aec0ed074ce43d5c0f7f46a42ab4`** mengekspor decorated C++ `common_chat_templates_init/apply`, `common_chat_parse`, dan Gemma4/PEG functions. Imported MSVC runtime membuat native C++/STL ABI compatibility relevan; machine=x64 saja tidak cukup.
- Exact headers memakai `std::string`, `std::vector`, smart pointers, parser parameters dan PEG arena. Mereka tidak menyediakan generic C `parse(json)->json` bridge.
- D2 selected source/header receipts dipin ke commit exact; **full compile-time include closure dan MSVC STL/CRT/Windows SDK compatibility belum dibuktikan**. D3 tidak mencampurnya dengan header dari unrelated master dan tidak mengunduh dependency tambahan.
- Lifecycle allocator/deallocator dan release/debug library configuration harus cocok. D2 tidak mempunyai full CMakeCache/compile flags untuk installed build; compiler command line yang sekadar bernama clang tidak akan menjadi proof ABI.

Karena tidak ada candidate compiler/linker, **x64 code generation, C++ linking, STL layout/runtime compatibility, exact include closure dan driver DLL load-path verification tidak dapat dijalankan**. Nilainya NOT VERIFIED, bukan incompatibility yang dibuktikan oleh crash dan bukan alasan mencoba raw ctypes/PInvoke.

### 3.3 Legitimate lifecycle yang seharusnya diuji jika feasible

Berikut adalah contractual sequence dari D2 exact source/header evidence, **bukan driver yang telah dibuat**:

1. Native C++ caller menggunakan exact declarations/types, bukan manual struct layout. Template diinisialisasi melalui `common_chat_templates_init` dengan explicit embedded template dan BOS/EOS inputs; null-model path tidak boleh dianggap otomatis mewarisi seluruh loaded-model flags.
2. `common_chat_templates_ptr` dan custom deleter/common free menjaga lifecycle template. Tool schema dan `common_chat_templates_inputs` dibentuk native dan dicatat, termasuk reasoning/tool choice/parallel settings yang dipilih.
3. `common_chat_templates_apply` menghasilkan `common_chat_params`, format, serialized parser dan generation prompt. Driver harus menyimpan state itu, bukan menebak prefix dari response text.
4. `common_chat_parser_params` harus memiliki format/parameters yang benar dan **arena dimuat dari generated parser**. Constructor/default empty arena bukan proof bahwa specialized Gemma4 decoder dipakai.
5. `common_chat_parse(input,false,params)` menghasilkan `common_chat_msg`; recognition/errors hanya direkam sebagai data. Strings/vectors/arena/templates didestruksi sesuai native ownership/CRT. Tidak ada model loader, tokenizer generation, HTTP, atau Nexus executor dalam composition.

Sequence ini menunjukkan bahwa probe mungkin secara arsitektural, tetapi **toolchain dan ABI proof lokal belum tersedia**. Tidak ada fresh template/parser arena/generation-prompt state dibuat di D3.

## 4. Tahap B — condition unmet, tidak ada driver/test execution

`E3/execution-not-started.json` adalah **non-execution record**, bukan fabricated execution/cleanup receipt.

| Artifact/action | Actual status |
|---|---|
| Driver `.cpp`, object, executable/import bridge | **Tidak dibuat**; identity/hash N/A |
| Native compiler/linker invocations | **0 /0** |
| Deployed decoder/runtime invocations | **0 /0** |
| Parser arena/generation prompt materialization | **Tidak dilakukan** |
| Driver write-denial/access-isolation proof untuk Nexus/pilot | **NOT EXECUTED**, karena driver tidak ada; tidak mengklaim Job Object membatasi filesystem authority |
| Driver Job creation / cleanup | **Tidak dibuat / N/A**; tidak ada owned/foreign native process untuk dihentikan |
| Model load/inference/actual Agent runs | **0 /0 /0** |
| Driver-to-production executor forwarding | **Tidak ada** |

Jika Stage B berjalan, approved anonymous Job runner dan verified restricted source-write boundary tetap mandatory. Karena gate toolchain gagal sebelum driver ditulis, D3 tidak mengklaim sudah membuktikan isolation/lifecycle execution itu.

### Controls disposition

| Required control | Exact input/state | Actual recognition |
|---|---|---|
| Positive | D2 `control-gemma4-closed-call.txt`: `<|tool_call>call:get_time{city:<|"|>London<|"|>}<tool_call|>`; SHA **`8dc36fbf2b8e980a53d5e2dca3aa797d350ff9004e2078653b014ec848e9351d`**. Source-golden schema/expectation direuse. D3 generated state tidak ada. | **NOT RUN / INCONCLUSIVE**, tidak recognized/rejected |
| Malformed native call | Input baru tidak dimaterialisasi karena preflight gagal | **NOT RUN** |
| Incomplete tool-call marker | Input baru tidak dimaterialisasi | **NOT RUN** |
| Undeclared tool name | Dipilih sebagai negative name-contract case; input tidak dimaterialisasi. Tidak mengasumsikan generic decoder menegakkan seluruh Nexus proposal schema. | **NOT RUN** |

Tidak ada exception parser/compiler yang dihasilkan; absence of execution tidak diberi label PASS. Tidak membuat driver alternatif, mengganti DLL, atau membangun ulang llama.cpp untuk mendapatkan hasil.

## 5. Historical comparison dan causal limitations

Empat original request/response capture hashes cocok sebelum/sesudah D3. D2 classification dipakai kembali: request required-tool/cap256/streamfalse, respons1 prompt1033/completion256 dan respons2 prompt1021/completion256, keduanya **finish length, no native tool_calls**. Content call-like tetap passive data; tidak ditransformasikan menjadi accepted call.

D3 tidak mereplay captures melalui decoder, tidak memperoleh original raw generated token stream/parser state, dan tidak menambah bukti bahwa model generation atau decoder mismatch adalah penyebab tunggal. **Toolchain blocker adalah blocker eksperimen lokal, bukan penjelasan native failure I2-B.**

Keberhasilan hypothetical driver nantinya hanya membuktikan recognition pada configuration yang diuji; ia tetap tidak otomatis menyelesaikan historical causal attribution atau actual I2-B natural-language editing. Tidak ada alasan dari D3 untuk cap escalation, runtime/template replacement, parser fallback atau weakening policy.

## 6. Preservation dan regression debt

`E3/preservation.json` mencatat:

- **2.584/2.584** baseline source/document hashes unchanged, termasuk C1.1 UI dan D1/S1 fixes; **139/139** default-output identities unchanged.
- Empat historical capture hashes unchanged; **11 protected reports/seals/archives** cocok, termasuk D2 report/evidence dan D1/S1/H/I2-B artifacts. Original staged/unstaged/untracked snapshots disimpan sebelum preflight.
- Staged patch tetap **`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`**.
- D2 cryptographic runtime/model identities **direuse**, tidak dihitung ulang. D3 size+mtime fence untuk **50 runtime files** dan GGUF unchanged. Ini within-session metadata preservation check, **bukan fresh cryptographic runtime/model attestation**.
- No reset/restore/stash/clean/commit/merge, source/pilot write, dependency download/install, environment/registry mutation, native-model/server/Agent process, atau foreign-process cleanup.

Report ini dan external preflight/evidence tooling adalah satu-satunya deliverables baru. Python collector bukan native decoder driver. Tidak ada capability audit, metadata/PE re-audit, source fetch atau regression rerun untuk source yang tidak berubah.

Empat existing impacted failures tetap **unresolved regression debt**, tidak dikecualikan dari future certification:

1. `GateIChatFirstWorkspaceTests.CF1_ExplicitCodeContextEntersChatPayload_QuestionTextNeverApproves_AndEditExcludesHistory`.
2. `ConversationViewModelTests.ConversationView_TopBarMenuButton_HidesInAiCheckMode`.
3. `ConversationViewModelTests.I2A_ExplicitGrantInjectsActualSource_ExcludesHistoryAndAttachments_RevokeBlocksNextRequest`.
4. `NativeToolSelectionTests.BoundCompilerRepair_FiltersOtherDescriptors_AndFinalNoneStaysNone`.

D1/S1 targeted25/25 dan impacted314/318 adalah historical results; D3 tidak menyatakan empat kasus tersebut PASS atau membuat TRX untuk test yang tidak dijalankan.

## 7. Alternatif berbiaya jelas dan satu rekomendasi engineering

| Opsi | Biaya/prerequisite | Manfaat | Batas/keputusan |
|---|---|---|---|
| A. Provision C++ toolchain/SDK dan build offline driver | Tinggi: compiler/linker/STL/SDK/import bridge dan exact header closure, ABI/lifecycle/write-isolation validation; memerlukan scope instalasi terpisah yang **tidak disetujui sekarang** | Decoder unit compatibility tanpa inference | Tetap tidak merekonstruksi missing historical state. **Tidak direkomendasikan untuk memperpanjang decoder work pada environment ini.** |
| B. Owner-supplied reviewed prebuilt exact-DLL driver | Sedang–tinggi: artifact/source provenance, hash, ABI/CRT/import/load-path checks, isolation dan controls; availability belum ada | Bisa menghilangkan local compilation prerequisite | Bukan evidence yang tersedia sekarang; unknown binary tidak boleh langsung dijalankan. Jangan membuka putaran baru hanya berdasarkan harapan driver ada. |
| **C. Bounded generation/termination diagnostic pada existing pinned runtime** | **1 fresh diagnostic native generation max**, cap256, hard120s, **0 actual Agent runs**, executor/writer tidak dikomposisi; owned isolated process/evidence; allocation dan instrumentation scope baru wajib disetujui | Melihat actual generation/termination dan provider envelope, bukan synthetic parser success; lebih dekat dengan native I2-B blocker | Satu run bukan reliability certification dan tidak menutup actual edit/build/recovery acceptance. No top-up/retry-until-pass; all outcomes retained. |

**Satu rekomendasi:** pilih **C**, bukan putaran decoder investigation berikutnya. Ajukan sebagai paket diagnostik generation/termination yang terpisah dan terbatas; **belum dijalankan atau diautorisasi oleh D3**.

Proposed acceptance untuk paket C:

1. Sebelum menggunakan allocation, collector/instrumentation harus terbukti bisa merekam request identity, effective template/format/parser/generation-prompt state yang tersedia, raw provider response, usage/finish/stop facts dan original token/stream data bila existing diagnostic surfaces mendukungnya. Gunakan already-supported b11057 fields/traces; jangan menjanjikan data yang tidak tersedia, menebak token IDs, atau memasang decoder baru. Jika capture prerequisite tidak memadai untuk discriminator yang disepakati, **jangan konsumsi generation**.
2. Gunakan disposable approved data/captured request context, tidak main Nexus/pilot source writes. Preserve four historical captures. Request diagnostik baru diberi identity dan exact parameter delta tersendiri; tidak mengganti bytes/interpretasi run lama.
3. Jalankan paling banyak **satu fresh native request**, tanpa Agent orchestrator/executor/write approval path; runtime/model/template/parser/policy/cap256/deadline120 tetap. Semua observation/logging setting yang dibutuhkan disebutkan dalam future approved scope. Tidak menjalankan task atau menerima pseudo-call sebagai authority.
4. Bedakan actual raw generation/termination dari server envelope; bila intermediate data tidak cukup, laporkan limitation daripada menyimpulkan model/decoder sebagai root cause. Termination failure, malformed native envelope dan valid native tool call tetap outcome berbeda; tidak ada PASS berdasarkan fragmen text.
5. Historical live allocation **2/4 generations,2/2 Agent runs** tidak dibuka kembali; paket C memerlukan **allocation baru yang eksplisit**, bukan memakai sisa counter sebagai izin. Setelah evidence cukup, pilih satu directed remediation scope berbasis bukti; actual WPF NL proposal → exact approval → source edit → build/tests/recovery tetap harus dibuktikan kemudian.

Ini membatasi biaya inference dan menghentikan audit loop. Goal tetap **actual I2-B native natural-language editing**, bukan menjalankan decoder test sebagai pengganti functional acceptance. Stage J tidak dimulai dan empat regression debts tetap wajib closure.

## 8. Closure

Evidence: `baseline.json`, `toolchain-preflight.json`, `discovery-scope.json`, `execution-not-started.json`, `preservation.json`, status/patch snapshots dan external collector. Archive/report SHA dan per-entry manifest dicatat di `E3/final-seal.json` di luar archive.

**STOP: Stage A blocked; Stage B not started; decoder INCONCLUSIVE. No production remediation, no new decoder-investigation cycle requested. I2-B HOLD; Gate J HOLD.**
