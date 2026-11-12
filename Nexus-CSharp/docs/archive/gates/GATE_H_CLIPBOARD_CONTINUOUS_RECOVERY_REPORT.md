# Gate H — Clipboard Continuous Recovery

Tanggal: 12 Oktober 2026. Metode: **OBSERVE → HYPOTHESIZE → TEST → FIX → VERIFY**.

## 1. Hasil utama

**CLIPBOARD READER ISOLATION DEFECT RESOLVED — REQUEST NEW GATE H FREEZE**

Kelemahan isolasi pada native **test reader** terbukti dan diperbaiki: reader memakai
`OpenClipboard(NULL)`, sehingga opener NULL lain dapat membuka clipboard yang sama dan
menutup open-state milik reader pertama. Hasilnya `GetClipboardData(13)` gagal dengan
`ERROR_CLIPBOARD_NOT_OPEN=1418`, walaupun data/format dan sequence tidak berubah.

Fix minimal menggunakan **message-only HWND milik test** hanya selama read scope.
Production native writer, owner-selection, CF_UNICODETEXT, movable global memory,
legacy SetText, OCR success-only status dan retry contract tetap unchanged.

Ada tiga lapis bukti: natural reproduction pada original first payload, controlled
interleaving yang menghasilkan signature sama, dan regression test **RED sebelum fix →
GREEN sesudah fix** pada interleaving identik. Post-fix targeted/impacted verification
mencakup **77 unique xUnit cases PASS**, ordered predecessor **12/12 PASS**, dan original
STA cases **3/3 PASS**.

**Batas klaim:** aktor/interleaving persis dalam failed full-suite invocation historis
tidak direkam; tidak diklaim telah mengidentifikasi aplikasi Windows penyebabnya.
Yang terbukti adalah defect reader yang dapat menghasilkan signature sama, serta
natural reproduction baru yang cocok dengan mekanismenya. Historical failure tetap
failure; **Gate H belum certified dan Gate I tetap HOLD**.

## 2. Audit kondisi yang dilanjutkan

Baseline terbaru berisi **650 source/document entries**: previous recovery telemetry
dan laporan sebelumnya dipertahankan. Semua baseline hashes cocok, tanpa unknown
source conflict. Git status/staged/unstaged patches dan before affected source/harness
tersimpan di external evidence. Staged patch tetap:

`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.

Saat user meminta melanjutkan dari tool terakhir, pemeriksaan read-only proses tidak
menemukan `dotnet`, `testhost`, atau `llama-server` aktif. Tidak ada foreign process
termination. Semua invocation memakai unchanged verified anonymous Job helper dan
recorded membership kosong sesudah cleanup.

Consumed approval, failed TRX, old diagnostic markers dan frozen archives tidak diubah.
Tidak ada reset/restore/clean/stash/commit/merge, rollback UI/UX atau production patch.

## 3. Validity ordered-predecessor evidence

Dependency closure external harness diperiksa: existing STJ v10 dan Encodings.Web v10.
Dry run melakukan actual JSON serialization/parsing atas snapshot helper tanpa model
generation, test method execution atau clipboard write. Dry PASS.

Ordered attempt pertama menemukan setup defect berbeda: external base directory tidak
memiliki trailing separator, sehingga WPF SiteOfOrigin asset URI mencari
`bin/Debug/Assets/MyImage.jpg` alih-alih `bin/Debug/net8.0-windows/Assets/MyImage.jpg`.
Failure terjadi pada first predecessor setup; bukan native clipboard failure. Artifact
disimpan sebagai `ordered-results.json`/`ordered.log`, tidak direlabel PASS.

Fix eksternal satu baris menambahkan directory separator. Dry berikutnya memverifikasi
WPF startup serta serialization. New identity `clipboard-continuous-ordered-wpf-1`
kemudian menghasilkan valid 12-step record. Setiap method dipanggil secara synchronous/
awaited reflection; ordinal, start/end timestamp dan exception dicatat per step.
**VSTest filter tidak digunakan atau diklaim menjamin urutan.**

Urutan yang benar-benar selesai:

1. `AIWorkspaceRenderingTests.Streaming_OpenFenceKeepsCodeTableAndCopyUsesLatestContent`
2. `AIWorkspaceRenderingTests.CodeBlockHeader_CopyActionCopiesOnlyCodeContent`
3. `ConversationViewModelTests.CopyMessageCommand_CopiesPlainTextWithoutMarkdown`
4. `ConversationViewModelTests.CopyMessageCommand_IsAvailableForUserAndAssistant`
5. `UnifiedFeedbackTests.AiCheck_CopyCorrection_Success_SetsIsCopiedOnly_NoSnackbar`
6. `UnifiedFeedbackTests.AiCheck_CopyCorrection_Failure_ShowsSnackbarError`
7. `FileManagementShellTests.FileManagementView_ReusesOneHostAndBothChildViewsWhilePreservingState`
8. `FileManagementShellTests.SwitchingTabs_PreservesChildViewAndVisualStateIdentity`
9. `FileManagementShellTests.SwitchingTabs_PreservesIndependentChildViewModelState`
10. Original live native test — single-line payload.
11. Original live native test — Unicode/symbol/multiline payload.
12. Original live native test — mixed CRLF/LF payload.

Pre-fix sequence **12/12 PASS** pada 08:18:21.898–08:18:24.051 UTC; post-fix sequence
**12/12 PASS**. Verifier memastikan ordinal1–12 dan setiap previous end≤next start.
Ini representative bounded WPF/copy predecessor evidence, bukan rekonstruksi seluruh
1479 detik full-suite history. Original methods/assembly dipakai; bukan mock-success.

## 4. Hypotheses dan evidence pembeda

### H1 — clipboard berubah setelah write

Previous controlled EmptyClipboard menghasilkan sequence change dan CF13 loss; itu
valid positive control, tidak membuktikan external interference historis. Natural
reproduction terbaru justru menunjukkan sequence1752 stabil dan CF13 tetap tersedia,
sehingga **clear/overwrite bukan penjelasan untuk observed read failure terbaru**.

### H2 — data ada tetapi native read gagal

Follow-up STA probe menjalankan tiga original live payloads sekali. Payload pertama
gagal tanpa injection, dua berikutnya PASS; failed output dipertahankan:

```text
Before write: sequence1747
Native write: true
After write: sequence1752; ownerHWND1379292; ownerPID18228; CF13=true
Before read: sequence1752; owner sama; CF13=true; apartmentSTA
OpenClipboard: true, attempt0
GetClipboardData(13): NULL, immediate error1418
Read-locked snapshot: sequence1752; owner sama; CF13=true
Expected: "Single line native text"; Actual: null
```

Microsoft mendefinisikan1418 sebagai **thread does not have a clipboard open**. GlobalLock
belum dipanggil karena handle NULL. Jadi bukan bukti kehilangan data atau GlobalLock
allocation failure; open-state yang diasumsikan helper tidak bertahan.

Probe held-null-open awal tidak menghasilkan expected open failure: opener kedua juga
sukses. Hypothesis bahwa NULL opener otomatis mengisolasi reader **dibantah**, dan
result `all=false` tersebut dipertahankan (`locked-read-results.json`). Classification
text di artifact itu adalah expected hypothesis, bukan established outcome.

Follow-up terarah memakai interleaving eksplisit, satu invocation:

```text
write=true; first OpenClipboard(NULL)=true
second thread OpenClipboard(NULL)=true; second CloseClipboard=true
first GetClipboardData(13)=NULL/error1418
sequence1887 →1887; CF13=true
```

### H3 — apartment/global-state predecessor

Valid ordered MTA sequence PASS, sedangkan natural STA first-case failure di atas
menunjukkan **sekadar pindah ke STA bukan solusi**. WPF/AI CHECK predecessors tidak
terbukti sebagai penyebab deterministik. Root mechanism dipersempit ke NULL-HWND open
scope, bukan perubahan architecture WPF, dispatcher, OCR atau native writer.

Control pada interleaving identik menggunakan distinct test-owned reader HWND:

```text
first OpenClipboard(readerHWND)=true
second OpenClipboard(NULL)=false; second Close tidak dipanggil
first GetClipboardData(13)=valid handle; error0
sequence1908 →1908; CF13=true
```

Ini bukan delay/retry atau fake success; read scope yang benar mencegah competitor
mengambil bagian dalam open-state yang sama.

## 5. Exact minimal fix

Satu repository executable source file berubah pada task ini:

`src/03_App/Nexus.Integration.Tests/NexusClipboardTests.cs`

- `ClipboardReaderWindow` kecil, private dan test-only membuat native message-only
  `STATIC` HWND pada thread pemanggil dengan `HWND_MESSAGE` (-3).
- `ReadClipboardNative` memakai `OpenClipboard(readerWindow.Handle)` dan membuang HWND
  setelah CloseClipboard/read scope selesai; creation/destruction failure tetap error.
- Existing failure-only telemetry mencatat `readerHwnd` pada read-open record.
- Optional private `afterOpen` callback digunakan regression test untuk exact competing
  opener schedule; original live tests tidak memasang callback.
- Satu mandatory `[Fact]` baru:
  `NativeRead_OpenScope_CannotBeClosedByAnotherNullWindowOpener`.

Regression memakai real native write, actual competing thread OpenClipboard(NULL), lalu
assert competitor denied, exact Unicode/newline payload dan CF13. **RED** dengan reader
NULL lama: `Assert.False(competingOpen)` gagal karena actual=true, completed failed TRX.
Setelah one surgical fix, regression **GREEN** dalam all-clipboard run. Fix attempts=1.

Semua **30 original assertions** dipertahankan sebagai ordered subsequence. Native
payloads, format exclusions (`DataObject`, `Ole Private Data`), reader10 attempts/20ms
sleep, writer existing retries dan null-failure criteria tidak diubah. Live test tetap
real Win32; tidak ada mock-only replacement, fallback atau read=null PASS.

Production `NexusClipboard.cs`, OCR native copy path/status, legacy SetText, P6 authority,
all UI/theme/rendering source dan original xUnit serialization settings unchanged.

## 6. Verification results

| Execution | Result |
|---|---|
| Pre-fix mandatory regression | **1 FAIL**, expected RED retained in TRX |
| Post-fix original live test | **3/3 PASS** |
| Post-fix all NexusClipboardTests, including regression | **20/20 PASS** |
| OCR selected/all + AI CHECK copy success/failure | **3/3 PASS** |
| Relevant Image Tool/OCR/geometry/ViewModel impacted | **54/54 PASS** |
| Original WPF/copy ordered predecessors → native payloads | **12/12 PASS**, explicit order checked |
| Same STA context that produced natural failure | **3/3 PASS** |
| Actual 11-assembly discovery | **1451 cases: 1450 mandatory + 1 diagnostic** |

Post-fix xUnit total80 PASS entries, **77 unique cases** (original3 overlap with all20).
No post-fix failure, skip or abort. Ordered and STA harness calls are separately reported,
not added to xUnit totals. These passes verify the changed read helper and controlled
failure mechanism, not blanket reliability across every external clipboard actor.

Original13 required Gate A–H live fixtures remain mandatory. Inventory increase is exactly
one new native-reader isolation regression. P1 diagnostic selection/acceptance unchanged.
No live model run, full certification, consumed approval reuse or Gate I advancement.

All builds/tests/diagnostics/discovery ran through verified `CertificationJob`; each
invocation record has empty final membership. Successful source diff review and
`git diff --check` produced no whitespace errors.

## 7. Open-source decisions

| Source / pattern | Decision / rationale |
|---|---|
| Existing Nexus native writer, telemetry reader, xUnit, WpfAppHost | **USE / ENHANCE** only test read ownership; existing C#/.NET8/WPF infrastructure |
| Microsoft Win32 Clipboard API guidance | **USE** explicit HWND, CloseClipboard lifetime, movable memory and immediate error contracts |
| Microsoft PowerToys local reference, MIT | **REVIEW / REJECT direct transplantation**; test helper uses FormsClipboard STA and swallowed/default errors/retries inappropriate for strict native mandatory proof |
| PowerToys MeasureTool native clipboard example | **REVIEW / NOT NEEDED**; not a drop-in replacement for Nexus checked memory-transfer contract |
| New clipboard service, OLE path, framework/dependency | **NOT NEEDED** |

Sources reviewed:
- [Microsoft GetClipboardSequenceNumber](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber): sequence tracks changed/emptied content for a window station, not read-open ownership.
- [Microsoft SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata): movable memory/ownership transfer.
- [Microsoft Using the Clipboard](https://learn.microsoft.com/en-us/windows/win32/dataxchg/using-the-clipboard): explicit HWND open/read/close examples.
- [Microsoft system error1418](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-).
- `D:\NEXUS\Audit\Referensi\PowerToys-main\src\common\UITestAutomation.Next\ClipboardHelper.cs`,
  `src\modules\MeasureTool\MeasureToolCore\Clipboard.cpp`, repository MIT LICENSE.

PowerToys adalah mature Microsoft project; local snapshot dipakai untuk pattern/license
comparison, bukan klaim audit semua versi/security. Tidak ada copied component atau new
product dependency. Web raw-path404 yang ditemui tidak diperlakukan sebagai source evidence.

## 8. Preservation dan hashes

Baseline650: **649 entries unchanged**, satu affected test changed; laporan ini menjadi
entry baru pada recovery source checkpoint. Staged/unstaged/untracked user work selain
authorized test edit dipertahankan; frozen historical archives tidak disentuh.

| Input | SHA-256 |
|---|---|
| Test sebelum continuous recovery | `3fcf79ce485bcae449a523026fb9ebdf7d189a908f49e41536da2db87d948113` |
| Test setelah minimal fix | `e32cc5c11d4661171b34bd5418d865d9f29baee5f15141e021a87d02ef2b2fe1` |
| Unchanged production NexusClipboard.cs | `0bf96de04e69787a4244f23f390d102db2fd4f51171ac585b62923621c7c5688` |
| Staged patch | `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046` |
| Current test inventory | `000f127f91202959da0137053c660989178352b82287d9d90c9833d7092b7164` |

Evidence: `C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-clipboard-continuous\`.
Contains plans, all setup failures, natural STA failure, controlled before/after, RED TRX,
post-fix TRX, both valid ordered records, invocation cleanup, before source/harness,
surgical diff, current inventory and `verification.json`. Final source/evidence ZIP
and report hashes are in `continuous-recovery-seal.json` after report creation.

## 9. Review untuk new Gate H source freeze

Recovery source checkpoint mencakup **651 source/document entries**, termasuk laporan
ini. External `new-freeze-review.json` mencatat exact source checkpoint SHA, test/input
delta, current binary hashes, inventory partition dan required next preflight. Ini
**review input untuk freeze baru**, bukan reuse old executable candidate/approval atau
full certification result.

Candidate lama `gate-h-workflow-v2-20261012-112450` tidak cocok dengan changed test/source
dan rebuilt Integration binary; consumed approval tidak dapat dipakai. New freeze perlu
fresh evaluated input/binary/PDB/runtime/fixture reconciliation, current inventory
**1451**, exact source/acceptance/tooling hashes, dan explicit owner approval
untuk execution sesuai Option B. Historical counts1449 mandatory tidak dipaksakan:
mandatory baru **1450** karena regression tambahan.

Recommended next action: **siapkan dan review source freeze baru dari checkpoint recovery,
lalu minta approval baru sebelum mandatory certification**. Preserve named P1 diagnostic
suite dan explicit separately reported outcome sesuai Option B. Seluruh1450 mandatory
cases/13 live fixtures, genuine F/G/H, deadlines dan cleanup tetap harus PASS pada new
certification; targeted recovery tidak cukup untuk Gate H signoff.

## 10. Remaining risks dan STOP boundary

- Historical external actor tidak diketahui; older full-suite failure tidak mempunyai
  error/sequence telemetry. Natural reproduced signature dan controlled mechanism cukup
  untuk memperbaiki actual reader defect, bukan membuktikan semua historical causes.
- HWND reader mengisolasi **read-open scope**, tidak membuat write-close→read-open gap
  menjadi atomic terhadap arbitrary external writers. Failure-only telemetry dan strict
  assertions tetap menangkap changed content/format jika terjadi.
- CorelDRAW manual paste belum dieksekusi; production format/memory/status behavior
  unchanged, jadi tidak ada compatibility claim baru di luar evidence ini.
- Historical ONNX/transport reliability limitations tetap mandatory dan tidak mendapat
  exemption clipboard/Option B tambahan.

**Reader isolation fix dan targeted/impacted verification selesai. REQUEST NEW GATE H
SOURCE FREEZE. STOP sebelum full certification; Gate H BLOCKED pending certification,
Gate I HOLD, tidak ada automatic Gate J.**
