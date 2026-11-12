# Gate I2-A — symlink fixture recovery / closure

14 Oktober 2026 · **I2A-RO-V1 initial authorized read-only scope PASS.**
Direct-symlink acceptance blocker ditutup. I2-B/Gate J tetap HOLD; physical IME,
OS-DPI, screen-reader/full CF1 interaction dan Preview UX backlog tidak berubah.

## Recovery dan actual denial

Historical failure terjadi di fixture `File.CreateSymbolicLink`, sebelum reader,
karena normal token tidak memiliki `SeCreateSymbolicLinkPrivilege`. Failed TRX
historis tetap byte-identical (SHA
`8105d0d077ef0bc2480e1d516490626d1edd18e20cdce172ad215df18e2c401b`).

- Synthetic temp fixture dipersiapkan dengan normal token, ownership nonce/marker,
  pinned directory/file identity dan hashes. Target `outside.cs` berada di luar
  read root, tetapi tetap dalam dedicated owned fixture parent; berisi `outside source`.
- Owner membuka Terminal Administrator secara manual dengan consent Windows/UAC
  dan menjalankan **setup helper saja**. Helper tidak mencoba elevation, tidak
  menjalankan Nexus/test suite, tidak mempunyai children, dan memverifikasi self
  anonymous Job membership sebelum membuat satu owned `root/Alias.cs`.
- Setup receipt membuktikan **`IO_REPARSE_TAG_SYMLINK = 0xA000000C`**, link/root/target
  identities, correct target/hash dan follow-read. Setup bukan bukti reader denial.
- Reader test memakai normal non-Administrator context (asserted). Sebelum reader,
  test membuka link dengan `OPEN_REPARSE_POINT`, memeriksa actual reparse tag/identity,
  dan membaca target melalui link secara normal. Jadi denial tidak berasal dari
  unreadable fixture atau kegagalan setup.
- Original assertions tetap: sole catalog entry `IsReparsePoint=true` dan actual
  `scope.FileIdentity("Alias.cs")` ditolak. Tidak diganti junction-only test, tidak
  skip atau melemahkan validator/approval/security contract.

## Fresh results dan reuse

| Verification | Actual result |
|---|---|
| Separate setup + type/identity/readability preconditions | PASS, manual owner/UAC |
| Original direct symbolic-link test, normal context | **1/1 PASS**, exit0 |
| Impacted ReadOnlyPathScopeTests | **4/4 PASS**, exit0; direct symlink, junction, hardlink, ADS/traversal, directory pin/replacement, unreadable ACL and cleanup |
| Verified owned fixture cleanup | PASS; no-follow delete-by-handle, identity/marker/hash fenced, owned empty directories only |

Fresh source/test helper builds exit0. Test subprocesses memakai existing verified
Job launcher: suspended direct root assigned/verified before resume; both complete
with empty remaining membership. No name/PID-historical cleanup. Elevated setup
was owner-launched and self-Job-owned; no elevated reader or Nexus process.

Unchanged I2-A acceptance reused from `GATE_I2A_REAL_PROJECT_CONTEXT_REPORT.md`:
202/202 impacted, final13/13 reader/context targeted; actual WPF native picker and
explicit consent,22 source +1 metadata catalog, bounded real source payload,
**one actual64-token model response** with valid
`[Services/LlamaClientService.cs:L99-L105]`, revoke/expiry/drift/unauthorized/session
denial proof, zero pilot source writes/builds. **0 new model generations** in recovery.
No full Gate H certification or CF1/CF2 matrix rerun.

## Exact source delta, preservation, cleanup

Only existing test file changed:
`src/02_Modules/Nexus.AI.EditingLite.Tests/Services/ReadOnlyPathScopeTests.cs`.
Before SHA `6be21577d1d6baedd105be5da9dc29b89169a70896982c2d0930b301bce4e86c`;
after `6bcfc61dd682d18d7948692a27230f470ee6d0c8ab1a8cb61ae974114571e32c`.
It accepts an explicitly provided, strictly validated prepared-fixture request;
original ordinary setup/assertions remain. Future unprivileged runs require a
fresh properly owned setup fixture; this closure is not a global symlink permission.

**676/677 baseline source/report files unchanged;0 production changes.** Staged SHA
still `b191c020902cad5ee10a1168c98ebec528cc1dac434f047772fe295ed1555046`.
I2-A/CF2 evidence archives and Gate H certified checkpoint hashes unchanged.
No global policy/Developer Mode, registry, ACL/integrity label, production privilege,
SafePath/read-authority/I1/Agent/runtime/SDK/cap change; no reset/restore/clean/stash/commit.

Normal-token cleanup pinned exact root/parent identities; alias deleted by its own
`OPEN_REPARSE_POINT` handle, target hash rechecked unchanged after alias deletion;
target/marker removed only by verified identity, directories only after empty checks.
Dedicated fixture parent no longer exists. Evidence receipts/failed TRX are retained.

## Checkpoints/status

External evidence base:
`C:\Users\Syand\AppData\Local\Temp\opencode\gate-i2a-symlink-recovery`.
`reconciliation.json`, `identity.json`, `setup-receipt.json`, `cleanup.json`, TRX,
normal-token privileges and verified invocation records carry full provenance.

New source checkpoint: `nexus-gate-i2a-closure-source-20261014.zip`.
New evidence checkpoint: `nexus-gate-i2a-symlink-recovery-evidence-20261014.zip`.
Archive/report SHA-256 are in external `seal.json` to avoid circular hashes.
These checkpoints are not full Gate H certification or a future source-freeze approval.

**I2-A approved bounded read-only acceptance PASS**, with the previously documented
static/lexical/manual selection, compiled-symbol/graph, secret-detection and model-
quality limitations retained. No unrestricted arbitrary-project editing claim.
**STOP at this closure; I2-B and Gate J HOLD.**
