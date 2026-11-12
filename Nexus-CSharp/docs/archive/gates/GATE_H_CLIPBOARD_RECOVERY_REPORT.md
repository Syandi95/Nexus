# Gate H — Clipboard Blocker Recovery Report

Tanggal: 12 Oktober 2026. Scope: native clipboard regression recovery saja.

## 1. Verdict dan exact cause status

**PARTIAL — ROOT CAUSE UNVERIFIED**

Historical mandatory failure tetap:

```text
NexusClipboardTests.SetPlainTextNative_LiveWin32Clipboard_PreservesContentAndProvidesCfUnicodeText
inputText: "Single line native text"
native write=true; expected read-back="Single line native text"; actual=null
original assertion: NexusClipboardTests.cs:188
```

Failure-only telemetry telah diimplementasikan dan verified tanpa melemahkan assertion.
Historical failure **belum** berhasil dibedakan sebagai production defect, test isolation
defect atau external interference. Satu controlled interference menghasilkan bentuk
`write=true/read=null` yang sama dan telemetry menjelaskan sebab kontrolnya, tetapi
**simulation bukan bukti causal mechanism historical invocation**. Tidak ada proven
production/test-isolation fix. **Gate H BLOCKED; Gate I HOLD.**

Tidak ada audit P1/parser/model/Agent runtime, model generation, full certification,
automatic rerun atau Gate I implementation dalam recovery ini.

## 2. Baseline dan affected-file ownership

Baseline current working tree direkonsiliasi terhadap 647 frozen source/document
entries plus dua unchanged review/blocker reports: **649 entries**, zero source conflict,
zero unknown change/addition. Previous failed execution/report/evidence digunakan
langsung; tidak memulai investigasi historical Gate H dari nol.

Git status dan staged/unstaged patches ditangkap sebelum edit. Staged digest tetap:

```text
b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046
```

Source writer tunggal: recovery task ini. Tidak ada reset, restore, clean, stash,
commit, merge, deletion atau rollback. Before source dan relevant Integration generated
outputs tersimpan terpisah sebelum test assembly rebuild.

## 3. Actual files changed

Satu executable repository source file berubah:

`src/03_App/Nexus.Integration.Tests/NexusClipboardTests.cs`

Perubahan:

- Existing xUnit `ITestOutputHelper` injection untuk mempertahankan failure trace dalam
  test output/TRX; tidak ada framework/dependency baru.
- Snapshot clipboard sebelum write, segera setelah native write, sebelum read dan
  saat clipboard read lock masih dipegang.
- Owner HWND/PID/thread, CF13 availability, clipboard sequence dan current apartment.
- `OpenClipboard` result dan last error pada failed attempt; existing attempt/sleep
  limits tetap.
- `GetClipboardData(13)` handle dan immediate last error saat NULL.
- `GlobalLock` pointer dan immediate last error saat NULL, jika data handle tersedia.
- Detailed observations hanya ditulis oleh catch failure dengan prefix
  `GATE_H_CLIPBOARD_FAILURE`; original exception tetap rethrow. Passing tests tidak
  mengeluarkan detailed telemetry.

Repository document baru: `GATE_H_CLIPBOARD_RECOVERY_REPORT.md` (laporan ini).
External development scripts/sequence harness berada di
`C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-recovery\` dan parent tooling
files, bukan product runtime.

**Tidak ada production edit** pada `NexusClipboard.cs`, OCR ViewModel, legacy SetText,
WPF host, assembly isolation, AI CHECK, rendering atau mutation/approval authority.

## 4. Bounded diagnostic plan dan actual results

`diagnostic-plan.json` ditulis sebelum diagnostic execution. Maksimum **3 diagnostic
invocations**, satu per D1/D2/D3; zero model generations; tanpa full suite. Root-cause
fix budget ≤2 directed changes; actual root-cause fix attempts=0.

| ID | Fixed scope | Actual result |
|---|---|---|
| D1 | Original live native test, ketiga original payloads, existing assertions/retry limits | **3 executed / 3 PASS / 0 FAIL / 0 SKIP**, completed TRX |
| D2 | Ordered original WPF code/message copy, AI CHECK feedback, File Management STA predecessors → original native payloads, tanpa Agent/model fixtures | **INVALID/UNVERIFIED**; external harness final-result serialization mengalami dependency binding error; tidak diulang |
| D3 | Tepat satu test-owned `EmptyClipboard` interleaving setelah successful native write, sebelum unchanged original read helper | Controlled `write=true/read=null` reproduced; telemetry menjelaskan kehilangan format pada skenario kontrol |

D2 bukan PASS atau valid historical predecessor proof. Error artifact tetap disimpan:

```text
System.TypeInitializationException: System.Text.Json.JsonSerializer
System.IO.FileLoadException: System.Text.Encodings.Web, Version=10.0.0.0
at external sequence Program.cs:61 (final result serialization)
```

External sequence harness memakai current installed `System.Text.Json` v10 tetapi
dependency `System.Text.Encodings.Web` belum direferensikan secara eksplisit. Minimal
external-only closure correction dilakukan sekali menggunakan DLL v10 yang sudah ada
di existing test output, untuk menyelesaikan D3. Tidak ada package/framework/product
upgrade. D2 marker tetap consumed; tidak replay untuk mencari PASS. Result detail D2
tidak tersedia, sehingga tidak diasumsikan semua predecessors atau clipboard assertions
berhasil.

## 5. Win32 evidence — apa yang terbukti dan belum

### Native writer

Current unchanged production method hanya mengembalikan true pada native path setelah
`OpenClipboard`, `EmptyClipboard`, dan nonzero `SetClipboardData(CF_UNICODETEXT,hGlobal)`.
Live test memastikan `CustomNativeSetTextAction=null`. Source memakai movable global
memory, UTF-16 LE + null terminator, `GlobalUnlock`, transfer ownership saat SetClipboardData
sukses, dan `GlobalFree` hanya bila ownership belum transferred. Tidak ada OLE fallback.

D1 ketiga original payloads melewati native success, exact content, CF13 dan no-OLE-private-
format assertions. Passing observations sengaja tidak dicetak; ini tidak memberi
sequence/owner proof untuk historical failed write.

### Controlled D3

```text
native write success: true
sequence after write: 1076
sequence after explicit test-owned EmptyClipboard: 1077
sequence before/inside read: 1077
owner HWND/PID: 0/0
CF_UNICODETEXT available: false
OpenClipboard: true (attempt0), error0
GetClipboardData(13): handle0, immediate error1168
read text: null; formats=[]
```

`GlobalLock` tidak dipanggil karena data handle NULL; tidak dilaporkan sebagai GlobalLock
failure. Error1168 adalah observed `ERROR_NOT_FOUND`, bukan stale error dari operasi
yang berhasil. Scenario ini membuktikan diagnostic helper dapat membedakan read-open
sukses dari format/data yang hilang setelah explicit clear. Actor kontrol berasal dari
test harness, **bukan observed aplikasi Windows lain**. Prior Unicode payload dikembalikan
dengan existing native writer jika tersedia; bukan full rich-format snapshot restoration.

### Historical cause

Original failing invocation tidak merekam sequence, owner, CF13 availability, native
handles atau errors. Jadi belum diketahui apakah:

- format/data hilang antara successful write dan read;
- owner berubah oleh external actor;
- `GetClipboardData` gagal sementara format masih ada;
- `GlobalLock` gagal pada valid handle;
- previous WPF/Dispatcher/global-state sequence material terhadap failure.

Assembly integration sudah serial; original live method berjalan tanpa explicit STA
marshalling. Win32 Clipboard APIs tidak disamakan dengan WPF OLE Clipboard STA contract.
MTA sendiri tidak terbukti defect: D1 live native assertions PASS dan D3 berhasil pada
MTA. Tidak mengubah dispatcher/apartment/owner HWND berdasarkan hipotesis.

Relevant WPF copy tests menggunakan existing `WpfAppHost.Run`/STA dan beberapa OLE
clipboard save/restore paths. Mereka merupakan kandidat predecessor yang relevan,
bukan bukti bahwa asynchronous/external overwrite benar-benar terjadi saat failure.

## 6. Targeted / impacted verification

Integration test assembly dibangun sekali dengan existing references, `--no-restore`,
`BuildProjectReferences=false`; production assemblies tidak dibangun ulang.

| Verification | Actual |
|---|---|
| D1 original live payload/format assertions | **3/3 PASS**, completed TRX |
| All NexusClipboardTests | **19/19 PASS** pada telemetry impacted run |
| OCR selected/all test | **1/1 PASS**; selected three words, all126 words, zero legacy path calls, success-only status dan restored hooks |
| AI CHECK copy success/failure | **2/2 PASS**; correct IsCopied/error feedback |
| Combined telemetry impacted run | **22 executed / 22 PASS / 0 FAIL / 0 SKIP**, completed TRX |
| Valid complete predecessor regression | **Belum ada**; D2 output invalid, tidak diklaim PASS |
| Broad Image Tool post-fix regression | Tidak dijalankan; proven fix belum ada, STOP rule berlaku |

Impacted run memverifikasi surgical telemetry/constructor change dan preserved clipboard
copy contracts; **bukan** post-fix proof bahwa historical blocker resolved. No failure-only
telemetry emitted pada passing xUnit runs. Semua subprocess melalui verified anonymous
`CertificationJob`; final membership kosong, tanpa foreign process/name/ancestry termination.

## 7. Original assertions dan test isolation correctness

Before/after comparison mempertahankan **semua 30 original Assert statements dalam
urutan yang sama**. Expected text, Unicode/newline payloads, CF13 inclusion serta
DataObject/Ole Private Data exclusions unchanged. Reader tetap maksimum10 OpenClipboard
attempts dengan existing20ms sleep; writer retry contract unchanged. Tidak ada retry pada
content/format assertion, skipped test, mocked live success atau tolerant null-return PASS.

Existing `AssemblyInfo.cs` DisableTestParallelization dan `WpfAppHost.cs` unchanged.
OCR selected/all hook isolation fix unchanged. Tidak ada STA/Dispatcher/isolation
correction diterapkan tanpa causal proof.

## 8. Enhanced Open Source reuse dan compatibility risk

- **USE:** existing NexusClipboard, native read helper dan xUnit output/isolation.
- **USE:** Microsoft Win32 API contracts untuk OpenClipboard/GetClipboardData/GlobalLock
  dan immediate errors; no handle free atau use-after-close dari native reader.
- **USE:** existing .NET/WPF host dan verified Job Object tooling.
- **ADAPT:** external ordered reflection probe dan diagnostic output, development-only.
- **NOT NEEDED:** clipboard service/framework/dependency baru, OLE replacement, model/runtime
  changes atau permanent production logging.

Referensi Microsoft: [GetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata)
mensyaratkan clipboard dibuka dan handle copied segera tanpa free/lock lifetime leakage.
New telemetry tidak mengubah native data transfer atau format ownership. CLR/WPF/xUnit
dependencies yang sudah terpasang dipakai; external dependency correction tidak menjadi
runtime Nexus baru.

CorelDRAW compatibility objective tetap plain CF_UNICODETEXT tanpa WPF OLE metadata.
No change to owner HWND, native memory policy, legacy SetText, OCR success-only status
atau approved UI/UX. Actual CorelDRAW paste belum dilakukan dan tidak diklaim PASS.
Shared desktop clipboard tetap mutable oleh external apps; belum ada evidence yang
memungkinkan test isolation fix aman untuk historical occurrence.

## 9. User work dan source/test hashes

Baseline649: **648 unchanged entries**, hanya affected test berubah. Staged patch
byte-identical; no unrelated production/source/UI edits. Before Integration generated
outputs diarsipkan sebelum rebuild; changed test DLL/PDB tidak dianggap source rollback.

| File / artifact | SHA-256 |
|---|---|
| NexusClipboardTests.cs before | `93519f60d1bb9c056bfa5fcdca3aab7807104b9893313053186945d722488cea` |
| NexusClipboardTests.cs after | `3fcf79ce485bcae449a523026fb9ebdf7d189a908f49e41536da2db87d948113` |
| Unchanged NexusClipboard.cs | `0bf96de04e69787a4244f23f390d102db2fd4f51171ac585b62923621c7c5688` |
| Staged patch | `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046` |

Evidence root: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-recovery\`.
Utama: baseline, diagnostic-plan, D1 TRX, D2 error log, D3-results, telemetry-impacted TRX,
invocation/cleanup records, surgical diff, original source backups dan `verification.json`.
Final source/evidence hashes dan report hash dicatat pada external seal setelah laporan.

## 10. Next recommendation dan STOP

**Historical root cause belum terbedakan; jangan menerapkan speculative production atau
test-isolation patch.** Failure-only telemetry sekarang tersedia untuk next correctly
bounded predecessor reproduction. External predecessor harness closure telah dikoreksi,
tetapi D2 belum replayed; run baru memerlukan plan diagnostic yang baru dan eksplisit,
bukan retry-until-pass atau full-suite cause hunting.

Pertahankan failure telemetry dan original assertions. Saat natural failure terobservasi,
gunakan sequence/owner/availability/handle/error untuk memilih minimal proven fix,
kemudian jalankan original, all clipboard, OCR/AI CHECK, relevant Image Tool dan bounded
predecessor verification. Jika resolved, siapkan new source freeze dan minta explicit
mandatory certification approval sesuai Option B. Targeted tests tidak cukup untuk
Gate H PASS; previous certification approval tetap consumed.

Tidak menyiapkan candidate resolved atau meminta full certification rerun pada state
yang belum proven. **STOP. Gate H BLOCKED; Gate I HOLD.**

**PARTIAL — ROOT CAUSE UNVERIFIED**
