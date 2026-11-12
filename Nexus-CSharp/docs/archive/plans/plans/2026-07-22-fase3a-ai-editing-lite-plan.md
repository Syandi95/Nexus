# [Fase 3A] Quick Win #1: AI Editing Lite C# Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Memindahkan modul *stateless* AI Editing (`AIFileReader.ps1`, `AIEditProposal.ps1`, `AIEditValidator.ps1`, `AIEditWriter.ps1`, `AIEditWorkflow.ps1`) ke C# Library `Nexus.AI.EditingLite.csproj` dan xUnit test `Nexus.AI.EditingLite.Tests.csproj` di `Nexus-CSharp/src/02_Modules/` dengan paritas fitur 100%, State Machine resmi, Failure Matrix lengkap, dan 0 regresi.

**Architecture:** Menerapkan Clean Architecture dengan 5 decoupled C# Services (`FileSnapshotService`, `ProposalService`, `DiffGeneratorService`, `SyntaxValidatorService`, `EditingWorkflowService`) yang mengeksekusi *atomic replace*, *mandatory backup*, dan *automatic rollback* jika sintaks atau validasi gagal.

**Tech Stack:** C# 12 / .NET 8, `System.Security.Cryptography.SHA256`, `System.Management.Automation.Language.Parser`, `System.Xml.XmlDocument`, `System.Text.Json`, xUnit.

---

## Global Constraints & Deprecation Rules

* ❌ **JANGAN MEMINDAHKAN SOURCE POWERSHELL**: Seluruh skrip PowerShell di `AI/EditingLite/*.ps1` **TETAP BERADA PADA LOKASI AKTIFNYA**.
* 📌 **STATUS MODUL POWERSHELL**: Seluruh skrip PowerShell tersebut berstatus **"Candidate for Deprecation"** (tetap aktif sebagai baseline).
* 📌 **STATE MACHINE & FAILURE MATRIX MANDATORY**: Implementasi C# wajib mematuhi transisi State Machine dan Failure Matrix error codes yang telah disetujui.
* 📌 **FREEZE HANYA SETELAH QUALITY GATE**: Freeze dilakukan HANYA setelah `dotnet build`, `dotnet test`, dan seluruh Quality Gate lulus 100%.

---

## 1. STATE MACHINE AI EDITING WORKFLOW

```
[Idle] ──(New-Session)──> [AwaitingApproval] ──(Apply Rejected)──> [Cancelled]
                                │
                         (Apply Approved)
                                │
                                ▼
                           [BackedUp]
                                │
                       (Atomic Replace)
                                │
                                ▼
                           [Applied] ──(Final Validation Fail / Replace Fail)──> [RolledBack]
                                │                                                    │
                      (Final Validation Pass)                                 (Rollback Fail)
                                │                                                    │
                                ▼                                                    ▼
                           [Completed]                                           [Failed]
```

---

## 2. FAILURE MATRIX (19 ERROR CODES & MITIGASI)

| Kode Error | Kategori | Deskripsi Penyebab | Tindakan Mitigasi C# |
| :--- | :--- | :--- | :--- |
| `PathOutsideProjectRoot` | Security | Path target berada di luar folder project root (Path Traversal attempt). | Tolak operasi, batalkan sesi tanpa I/O write. |
| `InvalidPath` | I/O | Format path file Windows tidak valid. | Tolak operasi, kembalikan `Result.Fail`. |
| `FileNotFound` | I/O | File target tidak ditemukan di disk. | Tolak operasi sebelum membaca byte. |
| `FileTooLarge` | I/O | Ukuran file melebihi batas `MaxBytes` (1 MB). | Tolak operasi untuk mencegah exhaustion RAM. |
| `BinaryFileRejected` | Validation | File mengandung byte NUL (`0x00`) (bukan file teks). | Tolak edit pada file biner. |
| `OldTextRequired` | Validation | Parameter `OldText` bernilai null atau string kosong. | Tolak pembuatan proposal. |
| `NoChange` | Logic | `OldText` dan `NewText` identik secara komparasi. | Tolak proposal (no-op edit). |
| `OldTextNotFound` | Logic | `OldText` tidak ditemukan pada konten file target. | Tolak proposal. |
| `OldTextNotUnique` | Logic | `OldText` ditemukan > 1 kali pada file (ambigu). | Tolak proposal; target wajib tepat 1 match. |
| `ApprovalRequired` | Security | User tidak memberikan konfirmasi persetujuan `Approved = true`. | Batalkan sesi, ubah status ke `Cancelled`. |
| `InvalidSessionState` | State | Status sesi bukan `AwaitingApproval` saat Apply dipanggil. | Tolak Apply, kembalikan `InvalidSessionState`. |
| `FreshnessCheckFailed` | Concurrency| File target telah diubah oleh proses eksternal sebelum Apply. | Tolak Apply, cegah overwrite data baru. |
| `BackupFailed` | I/O | Pembuatan file backup mandatory `.nexus-edit-*.bak` gagal. | Abort sesi sebelum file temp ditulis. |
| `TemporaryWriteFailed` | I/O | Penulisan file sementara `.nexus-edit-*.tmp` gagal. | Hapus temp file jika ada, batalkan Apply. |
| `TemporaryValidationFailed`| Syntax | Validasi sintaks (PS/XML/JSON) atau text pada temp file gagal. | Hapus temp file, batalkan Apply. |
| `FinalFreshnessCheckFailed`| Concurrency| File target diubah pada milidetik tepat sebelum atomic replace. | Hapus temp file, batalkan Apply. |
| `AtomicReplaceFailed` | I/O | `File.Replace` API gagal mengeksekusi atomic swap. | Jalankan `RestoreOriginal` dari `.bak`. |
| `FinalValidationFailed` | Syntax | Validasi sintaks/hash pada target file pasca replace gagal. | Eksekusi otomatis `RestoreOriginal` dari `.bak`. |
| `CriticalRollbackFailed` | Critical I/O| Gagal me-replace target DAN pemulihan dari `.bak` juga gagal. | Catat error kritis ke `CrashLogger`, set status `Failed`. |

---

## 3. MIGRATION MATRIX (18 FUNGSI POWERSHELL TO C#)

| Fungsi PowerShell (Asal) | Target C# Method & Interface | Unit Test Coverage | Status Modul PowerShell |
| :--- | :--- | :--- | :---: |
| `Get-AIEditLiteFileHash` | `IFileSnapshotService.ComputeSha256Hash(path)` | `ComputeSha256Hash_ReturnsValidHash` | Candidate for Deprecation |
| `Resolve-AIEditLitePath` | `IFileSnapshotService.ResolvePath(root, path)` | `ResolvePath_ValidAndPathTraversalCheck` | Candidate for Deprecation |
| `Get-AIEditLiteEncodingInfo`| `IFileSnapshotService.DetectEncoding(bytes)` | `DetectEncoding_Utf8BomUtf16SystemDefault` | Candidate for Deprecation |
| `Read-AIEditLiteFile` | `IFileSnapshotService.ReadSnapshotAsync(...)` | `ReadSnapshotAsync_ReadsContentAndMetadata` | Candidate for Deprecation |
| `Get-AIEditLiteLiteralMatchCount`| `IProposalService.CountLiteralMatches(content, text)`| `CountLiteralMatches_LiteralCaseSensitive` | Candidate for Deprecation |
| `New-AIEditLiteProposal` | `IProposalService.CreateProposal(...)` | `CreateProposal_UniqueMatchRequired` | Candidate for Deprecation |
| `New-AIEditLiteDiff` | `IDiffGeneratorService.CreateDiff(orig, proposed)` | `CreateDiff_GeneratesContextDiffText` | Candidate for Deprecation |
| `Test-AIEditLiteContentSyntax` | `ISyntaxValidatorService.ValidateSyntax(path, ext)` | `ValidateSyntax_PsXmlJsonValidation` | Candidate for Deprecation |
| `Test-AIEditLiteFile` | `IValidationService.ValidateFile(path, text, hash)`| `ValidateFile_TextAndSyntaxCheck` | Candidate for Deprecation |
| `Get-AIEditLiteEncoding` | `IFileWriterService.GetEncoding(encodingName)` | `GetEncoding_ReturnsEncodingInstance` | Candidate for Deprecation |
| `ConvertTo-AIEditLiteLineEnding`| `IFileWriterService.NormalizeLineEndings(...)` | `NormalizeLineEndings_CrlfLfCr` | Candidate for Deprecation |
| `Write-AIEditLiteTemporaryFile`| `IFileWriterService.WriteTempFileAsync(session)` | `WriteTempFileAsync_AtomicTempFile` | Candidate for Deprecation |
| `Invoke-AIEditLiteAtomicReplace`| `IFileWriterService.AtomicReplace(temp, target)` | `AtomicReplace_FileReplaceApi` | Candidate for Deprecation |
| `Remove-AIEditLiteTemporaryFile`| `IFileWriterService.DeleteTempFile(path)` | `DeleteTempFile_SafelyDeletes` | Candidate for Deprecation |
| `New-AIEditLiteSession` | `IEditingWorkflowService.CreateSessionAsync(...)` | `CreateSessionAsync_ReturnsAwaitingApproval` | Candidate for Deprecation |
| `New-AIEditLiteMandatoryBackup`| `IEditingWorkflowService.CreateBackup(session)` | `CreateBackup_CreatesVerifiedBakFile` | Candidate for Deprecation |
| `Restore-AIEditLiteOriginal` | `IEditingWorkflowService.RestoreBackup(session)` | `RestoreBackup_RestoresOriginalFile` | Candidate for Deprecation |
| `Invoke-AIEditLiteApply` | `IEditingWorkflowService.ApplyEditAsync(...)` | `ApplyEditAsync_FullLifecycleWithRollback` | Candidate for Deprecation |

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding `Nexus.AI.EditingLite` & `Nexus.AI.EditingLite.Tests` Projects
- Create `Nexus-CSharp/src/02_Modules/Nexus.AI.EditingLite/Nexus.AI.EditingLite.csproj`
- Create `Nexus-CSharp/src/02_Modules/Nexus.AI.EditingLite.Tests/Nexus.AI.EditingLite.Tests.csproj`
- Add references to `Nexus.Foundation` & `Nexus.Contracts`, register to `Nexus.sln`.

### Task 2: Implement File Snapshot & Encoding Services
- Implement `IFileSnapshotService` & `FileSnapshotService` in `Nexus.AI.EditingLite/Services/`.

### Task 3: Implement Proposal, Diff Generator, & Syntax Validator Services
- Implement `IProposalService`, `IDiffGeneratorService`, and `ISyntaxValidatorService`.

### Task 4: Implement File Writer & Atomic Replace Services
- Implement `IFileWriterService` with `.tmp` write pattern and `File.Replace` atomic swap.

### Task 5: Implement Complete Editing Workflow Service & State Machine
- Implement `IEditingWorkflowService` with full lifecycle (`NewSession`, `CreateBackup`, `RestoreBackup`, `ApplyEditAsync`).

### Task 6: Implement xUnit Unit Tests & Verify Quality Gate
- Write 20+ unit tests covering all 19 Failure Matrix error codes.
- Run `dotnet build` & `dotnet test`.
- Verify 100% PASS with 0 errors and 0 warnings.
