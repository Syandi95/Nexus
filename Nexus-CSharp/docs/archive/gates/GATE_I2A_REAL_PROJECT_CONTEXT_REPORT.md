# Gate I2-A — authorized real C# project context

14 Oktober 2026 · approved `I2A-RO-V1`.

**IMPLEMENTED; actual real-project pilot PASS. Full acceptance HOLD:** direct
symbolic-link fixture test gagal karena Windows tidak memberi creation privilege.
Failure dipertahankan, tidak di-skip/dilemahkan. Tidak ada I2-B/Gate J progression.

## Scope/source deltas

Tepat enam existing files sesuai approval: `ConversationViewModel.cs`,
`ConversationView.xaml`/`.xaml.cs`, `App.xaml.cs`, `ConversationViewModelTests.cs`,
`ArchitectureGovernanceTests.cs` di `src/03_App/`.

Tepat lima new files:
- `src/02_Modules/Nexus.AI.EditingLite/Services/ReadOnlyPathScope.cs`
- `src/03_App/Nexus.App/Services/ProjectReadContracts.cs`
- `src/03_App/Nexus.App/Services/ProjectReadContextWorkflow.cs`
- `src/02_Modules/Nexus.AI.EditingLite.Tests/Services/ReadOnlyPathScopeTests.cs`
- `src/03_App/Nexus.Integration.Tests/GateI2AProjectReadTests.cs`

Existing composer/runtime/budget dan SafePath native validation direuse. Tidak ada
package/Roslyn/MSBuildWorkspace/indexer, filesystem tool ke model, mutation profile,
executor atau perubahan I1/Agent/SafeWrite/recovery/SDK/Job/caps. UI tambahan hanya
izin/status/catalog/provenance yang diperlukan workflow; visual polish tetap backlog.

## A/B/C yang diimplementasikan

- **A:** project selection tetap display-only. Dialog native **Izinkan baca proyek**
  menunjukkan root, sesi,10 menit, types/limits/exclusions; default No. Opaque grant
  in-memory binds root identity dan host session. Cabut/expiry/navigation/project/
  session switch membatalkan akses; expiry melepas directory pins. Read authority
  tidak masuk I1 edit profile/provider atau model-defined allowlist.
- **Discovery:** local fixed NTFS root, pinned directory/ancestor handles; bounded
  direct-child traversal (depth6,128 directories,4096 entries),256 source/32 metadata,
  explicit exclusions. `.csproj/.slnx` XML DTD prohibited/resolver null; `.sln` entry
  text dan literal references statis. Tidak ada imports/targets/conditions/globs/task
  evaluation, builds, restore, shell/Git, downloads atau external project reads.
- **B:** strict UTF-8/BOM dan UTF-16 BOM; unsupported encoding/binary/control chars,
  recognizable secret material, oversized/unauthorized/stale files ditolak. Existing
  final-path/reparse/single-link validation direuse; read handles deny writer/delete
  sharing. Identity/size/mtime + raw-byte SHA; max256 KiB/file,8 MiB cumulative grant,
 32 candidates/2 MiB per question. No persistent repository source index/cache.
- **Context:** explicit catalog IDs + bounded lexical selection;≤6 files/160 lines
  per fragment,≤24 KiB UTF-8 dan3072 conservative estimated tokens termasuk envelope,
  turun menurut existing headroom. Exact path/line/hash/encoding/time/truncation
  provenance. Snapshot dispatch valid≤60 s dan re-hashes metadata/source under leases.
  Repository text adalah passive untrusted data, bukan system instruction/grant.
- **C:** existing Chat receives actual evidence, tanpa old project history/manual
  attachments pada project-aware request. Source payload tidak dipersist sebagai
  ordinary filesystem attachment. Read/project CTS terpisah dari Agent CTS; captured
  revision/session fences mencegah late answer/context/persistence pada scope baru.
  Source reload/history resend meminta pertanyaan baru secara eksplisit agar source
  tidak dire-extract otomatis dari old paths. Citations hanya dinilai lokasi/range
  dalam supplied fragments; bukan semantic correctness guarantee.

## Actual WPF/model proof

Actual `App`/`MainWindow`, production DI, native folder picker dan native read-consent
dialog. Pilot: `D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.AI.Llama`.

| Proof | Actual evidence |
|---|---|
| Select sebelum consent | `HasProjectReadGrant=false`, `CanEditSelectedProject=false` |
| Explicit UI consent |22 genuine `.cs` +1 `.csproj`; Foundation/Contracts/PdfUtilities references berada di luar root, seluruhnya `InRoot=false`, unread |
| Actual model question | Existing Gemma-4-E4B-It shared owner, temporary Chat, **64-token output cap**, tanpa refill/top-up |
| Actual source | `Services/LlamaClientService.cs`, supplied lines87–234; requested/cited99–105; UTF-8; SHA `cff5a37ac20eefeb744c29b85628d92741b841bf0476298f09c9de3cf82d9049` |
| Payload provenance | Context SHA `89260b99edb2e5f47bc323e05732c9b53e5637550cf270ca8d22257dfc2e2c54`; raw source tidak ditulis ke normal telemetry |
| Actual answer | Mengidentifikasi `_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)` dalam `LlamaClientService`, dengan `[Services/LlamaClientService.cs:L99-L105]`; host range validation=true |
| Revoke | Pertanyaan berikutnya ditolak tanpa message/model dispatch tambahan |
| Preservation |23 pilot source/metadata hashes before/after sama;0 pilot source writes,0 pilot builds |

**Satu actual model generation selesai**, bukan dua. Dua proof slots dicadangkan:
slot1 collector attempt berhenti pada disabled Send karena ordinary `.Text` assignment
mengganti binding; tidak melakukan generation. `SetCurrentValue` memperbaiki collector;
slot2 menghasilkan satu actual response. Marker/failed invocation tidak dihapus atau
diulang. Runtime/input diagnostic membuktikan ready tanpa generation. Wrapper awal
mode visual mencatat generation=null; reconciliation menentukan actual count1.

Proof paths, relatif ke `C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2a`:
- `actual-live-slot2/proof.json`, `actual-answer-2.png` — actual response, bukan replay.
- `final-wpf-denials/proof.json`/PNG — actual production WPF denial projection:
  disposable shadow source external modification setelah discovery ditolak;
  unauthorized ID/outside path ditolak; expired grant tidak dispatch; session switch
  membersihkan grant/catalog/provenance. Shadow adalah owned test data; pilot tidak
  dimodifikasi. Pilot consent memakai UI; sebagian repeat shadow grants memakai
  explicit test-host calls, tidak diklaim sebagai semua click-user interactions.
- `proof-native-window/` — picker→explicit grant→catalog→revoke,0 generation.

## Tests dan Continuous Recovery

| Run | Result |
|---|---|
| A/B/C RED | Missing contract tests FAIL, raw TRX retained |
| A/B early GREEN |1/1 lalu4/4 PASS |
| Reader/security/Chat/DI targeted |12/12 PASS |
| Native pin/junction/hardlink/ADS/traversal/unreadable/cleanup |3/3 PASS |
| Impacted Chat/architecture/I1/session/AI Check |**202/202 PASS**, sekali stable integration run |
| Post-integration budget/reload targeted |17/17 PASS |
| Final grant retirement targeted |11/11 PASS |
| Final reader empty `.sln`/SDK, file-count/byte-budget cases |13/13 PASS |
| Direct symbolic-link fixture extension |**3 PASS /1 FAIL**, environmental privilege blocker below |

Tests cover UTF-8/UTF-16 Unicode/line provenance, unsupported encoding/oversized,
secret names/material, misleading repo instructions as passive data, malicious DTD,
no target evaluation, external references, empty project, depth/file/read limits,
source/metadata/snapshot drift/expiry, revoked/different-session grants, payload
excludes history/attachments, no raw source storage and canceled late answer safety.
Real junction/hardlink and deny-read ACL fixtures are disposable; no global ACL/token
change. App/module/tests builds exit0; commands/runsettings/TRX/logs retained.
Development builds compile Nexus implementation, **not the selected pilot project**.

Directed defects fixed with evidence: misplaced source patch anchor/build failure;
new permission-state notification; collector matching WPF button instead of native
dialog (same title), fixed Window filter; collector binding overwrite; context cap
reserves envelope bytes/tokens; grant retirement idempotent and expiry releases pins.
Final cap/lease/list refinements have targeted/zero-generation WPF checks; actual
model is not regenerated just to match a later cosmetic/lifecycle build identity.

## Blocker and limitations

`A_SymbolicFileEscape_IsRejectedByTheSameReparseBoundary` failed at
`File.CreateSymbolicLink`, **before reader dispatch**, with:
`System.IO.IOException: A required privilege is not held by the client`.
Direct symlink escape remains unproven here; junction reparse rejection is proven.
No assertion/test disabled, skipped or rewritten to obtain PASS. Enabling privilege,
elevation or global Developer Mode is outside current approved environment changes.
**Full I2-A acceptance cannot be called PASS until this retained failure is closed
using explicitly authorized test-fixture creation or an approved prepared fixture.**

Static candidate inventory is not exact compiled graph; no semantic symbol/call-graph
parser. Relevance is bounded lexical/manual selection, not whole-repository knowledge.
Secret detection is conservative/recognizable-pattern based, not an all-secret
guarantee. Large/dynamic projects may be refused/partially unsupported. Source reload
requires a fresh explicit question. Only one simple live source question is proved;
complex-question model quality is not certified. Physical IME, OS DPI, screen-reader,
full CF1 keyboard/focus acceptance remain HOLD/backlog.

## Hashes/preservation and handoff

**665 baseline source/report files unchanged**, exactly6 existing +5 new code/test
files. Staged SHA remains
`b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
Gate H certified checkpoint and CF2 evidence archive identities reverified unchanged.
Full source before/after SHA, new-file hashes, binary identities, pilot hashes,
model-count reconciliation and failed TRX are in external `reconciliation.json`,
`identity.json`, `seal.json` and new evidence ZIP. No reset/restore/clean/stash/commit.
Verified anonymous Job cleanup uses only owned membership; live invocation's remaining
owned native member was terminated by its own outer Job, never PID/name heuristics.

Engineering implementation complete; acceptance **HOLD** pending the one privileged
symlink-fixture proof. Request authorization narrowly for that test environment,
not production authority/runtime/source-edit changes. **I2-B/Gate J remain HOLD.**
