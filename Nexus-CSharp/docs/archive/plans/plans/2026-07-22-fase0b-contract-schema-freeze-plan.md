# [Fase 0B] Contract & Schema Freeze Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Membekukan seluruh spesifikasi kontrak JSON Schema dan definisi C# DTO (Data Transfer Object) Models/Records pada lokasi workspace baru `Nexus-CSharp/docs/contracts/` untuk menjamin interop dan interoperabilitas penuh (100% kompatibilitas mundur) tanpa ada kehilangan data (*zero data loss*) saat modul C# membaca/menulis data eksisting Nexus.

**Architecture:** Penyusunan skema JSON terstandar (`draft-07`) dan model C# `record` imutabel modern (`System.Text.Json`) yang memetakan persis struktur JSON `config.json`, `index.json`, `conversations/*.json`, `last_session.json`, serta `AIEditProposal`.

**Tech Stack:** C# 12 / .NET 8 (`record`, `init`-only properties, `JsonSerializerContext`, `JsonConverter`), JSON Schema Draft-07.

---

## Global Constraints

* 📌 **LOKASI TARGET CONTRACT**: Seluruh dokumen kontrak JSON Schema & model C# wajib disimpan di `Nexus-CSharp/docs/contracts/`.
* ❌ **ZONA BEBAS IMPLEMENTASI KODE C# RUNTIME**: Fase 0B hanya menyusun spesifikasi DTO & Schema (`.cs` model definitions & `.schema.json`), belum membuat proyek kompilasi C# DLL runtime.
* ❌ **ZERO BREAKING CHANGE**: Skema C# DTO wajib kompatibel 100% dengan file JSON hasil simpanan skrip PowerShell v1.7.
* 📌 **STRUKTUR PROFILING IMUTABEL**: Menggunakan C# `record` imutabel dengan atribut `[JsonPropertyName]` untuk penanganan insensitivitas nama properti pascal/camel case.

---

## Structure Map & File Manifest (`Nexus-CSharp`)

```
Nexus-CSharp/docs/contracts/
├── README.md                                  # Dokumentasi cara membaca & memverifikasi kontrak DTO
├── config.schema.json                         # JSON Schema untuk nexus_data/config.json
├── conversation_index.schema.json             # JSON Schema untuk nexus_data/conversations/index.json
├── conversation_detail.schema.json            # JSON Schema untuk nexus_data/conversations/{id}.json
├── last_session.schema.json                   # JSON Schema untuk AI/last_session.json
├── models/                                    # C# DTO Record Specification Models
│   ├── ConfigRecord.cs                        # C# Record DTO untuk Aplikasi & AI Configuration
│   ├── ConversationIndexRecord.cs             # C# Record DTO untuk Indeks Percakapan
│   ├── ConversationDetailRecord.cs            # C# Record DTO untuk Percakapan & Pesan
│   ├── EditProposalRecord.cs                  # C# Record DTO untuk AI Editing Proposal & Diff
│   └── LastSessionRecord.cs                   # C# Record DTO untuk Sesi Terakhir AI Workspace
└── tests/
    └── ContractVerification.ps1               # Skrip validasi JSON eksisting terhadap JSON Schema
```

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Application Configuration Contract Specification (`config.schema.json` & `ConfigRecord.cs`)

**Files:**
- Create: `Nexus-CSharp/docs/contracts/config.schema.json`
- Create: `Nexus-CSharp/docs/contracts/models/ConfigRecord.cs`

**Interfaces:**
- Consumes: Key-value pasangan `nexus_data/config.json` (Theme, AccentColor, AIModel, AIProvider, ActiveTab, WindowWidth, WindowHeight, RecentFiles).
- Produces: JSON Schema Draft-07 & C# `record AppConfigRecord`.

- [ ] **Step 1: Spesifikasikan JSON Schema `config.schema.json`**

  ```json
  {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "Nexus AppConfig Schema",
    "type": "object",
    "properties": {
      "theme": { "type": "string", "default": "Dark" },
      "accentColor": { "type": "string", "default": "#007ACC" },
      "activeTab": { "type": "string", "default": "Home" },
      "aiProvider": { "type": "string", "default": "llama.cpp" },
      "aiModel": { "type": "string", "default": "Gemma 4 E2B" },
      "windowWidth": { "type": "number", "default": 1280 },
      "windowHeight": { "type": "number", "default": 800 },
      "recentFiles": { "type": "array", "items": { "type": "string" } }
    },
    "required": ["theme", "aiProvider", "aiModel"]
  }
  ```

- [ ] **Step 2: Spesifikasikan C# DTO Record Model `ConfigRecord.cs`**

  ```csharp
  namespace Nexus.Contracts.Models;

  public record AppConfigRecord(
      [property: JsonPropertyName("theme")] string Theme = "Dark",
      [property: JsonPropertyName("accentColor")] string AccentColor = "#007ACC",
      [property: JsonPropertyName("activeTab")] string ActiveTab = "Home",
      [property: JsonPropertyName("aiProvider")] string AiProvider = "llama.cpp",
      [property: JsonPropertyName("aiModel")] string AiModel = "Gemma 4 E2B",
      [property: JsonPropertyName("windowWidth")] double WindowWidth = 1280,
      [property: JsonPropertyName("windowHeight")] double WindowHeight = 800,
      [property: JsonPropertyName("recentFiles")] IReadOnlyList<string>? RecentFiles = null
  );
  ```

- [ ] **Step 3: Commit kontrak konfigurasi aplikasi**

  ```bash
  git add Nexus-CSharp/docs/contracts/config.schema.json Nexus-CSharp/docs/contracts/models/ConfigRecord.cs
  git commit -m "docs(contract): define app config json schema and C# record DTO specification"
  ```

---

### Task 2: Conversation Index & Detail Contract Specification (`conversation_*.json` & `ConversationRecords.cs`)

**Files:**
- Create: `Nexus-CSharp/docs/contracts/conversation_index.schema.json`
- Create: `Nexus-CSharp/docs/contracts/conversation_detail.schema.json`
- Create: `Nexus-CSharp/docs/contracts/models/ConversationIndexRecord.cs`
- Create: `Nexus-CSharp/docs/contracts/models/ConversationDetailRecord.cs`

**Interfaces:**
- Consumes: Data `nexus_data/conversations/index.json` dan `nexus_data/conversations/*.json`.
- Produces: JSON Schema & C# Records `ConversationIndexRecord`, `ConversationDetailRecord`, `ChatMessageRecord`.

- [ ] **Step 1: Buat spesifikasi schema `conversation_index.schema.json`**

- [ ] **Step 2: Buat spesifikasi schema `conversation_detail.schema.json`**

- [ ] **Step 3: Buat definisi model C# Record `ConversationIndexRecord.cs`**

  ```csharp
  namespace Nexus.Contracts.Models;

  public record ConversationIndexItem(
      [property: JsonPropertyName("id")] string Id,
      [property: JsonPropertyName("title")] string Title,
      [property: JsonPropertyName("createdAt")] string CreatedAt,
      [property: JsonPropertyName("updatedAt")] string UpdatedAt,
      [property: JsonPropertyName("provider")] string Provider,
      [property: JsonPropertyName("model")] string Model,
      [property: JsonPropertyName("modelId")] string ModelId,
      [property: JsonPropertyName("messageCount")] int MessageCount
  );

  public record ConversationIndexRecord(
      [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
      [property: JsonPropertyName("appVersion")] string AppVersion,
      [property: JsonPropertyName("updatedAt")] string UpdatedAt,
      [property: JsonPropertyName("conversations")] IReadOnlyList<ConversationIndexItem> Conversations
  );
  ```

- [ ] **Step 4: Buat definisi model C# Record `ConversationDetailRecord.cs`**

  ```csharp
  namespace Nexus.Contracts.Models;

  public record ChatMessageRecord(
      [property: JsonPropertyName("id")] int Id,
      [property: JsonPropertyName("role")] string Role,
      [property: JsonPropertyName("content")] string Content,
      [property: JsonPropertyName("timestamp")] string Timestamp,
      [property: JsonPropertyName("EditProposal")] EditProposalRecord? EditProposal = null
  );

  public record ConversationDetailRecord(
      [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
      [property: JsonPropertyName("appVersion")] string AppVersion,
      [property: JsonPropertyName("id")] string Id,
      [property: JsonPropertyName("title")] string Title,
      [property: JsonPropertyName("createdAt")] string CreatedAt,
      [property: JsonPropertyName("updatedAt")] string UpdatedAt,
      [property: JsonPropertyName("provider")] string Provider,
      [property: JsonPropertyName("model")] string Model,
      [property: JsonPropertyName("modelId")] string ModelId,
      [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessageRecord> Messages
  );
  ```

- [ ] **Step 5: Commit spesifikasi kontrak percakapan**

  ```bash
  git add Nexus-CSharp/docs/contracts/conversation_*.json Nexus-CSharp/docs/contracts/models/Conversation*.cs
  git commit -m "docs(contract): define conversation index and detail schemas with C# record DTOs"
  ```

---

### Task 3: AI Edit Proposal & Last Session Contract Specification (`EditProposalRecord.cs` & `last_session.schema.json`)

**Files:**
- Create: `Nexus-CSharp/docs/contracts/last_session.schema.json`
- Create: `Nexus-CSharp/docs/contracts/models/EditProposalRecord.cs`
- Create: `Nexus-CSharp/docs/contracts/models/LastSessionRecord.cs`

**Interfaces:**
- Consumes: Struct `EditProposal` pada percakapan dan `AI/last_session.json`.
- Produces: JSON Schema & C# Records `EditProposalRecord`, `FileDiffRecord`, `LastSessionRecord`.

- [ ] **Step 1: Buat spesifikasi C# Record `EditProposalRecord.cs`**

  ```csharp
  namespace Nexus.Contracts.Models;

  public record FileDiffRecord(
      [property: JsonPropertyName("HasChanges")] bool HasChanges,
      [property: JsonPropertyName("DiffText")] string DiffText,
      [property: JsonPropertyName("RemovedLines")] int RemovedLines,
      [property: JsonPropertyName("AddedLines")] int AddedLines,
      [property: JsonPropertyName("Truncated")] bool Truncated
  );

  public record EditProposalSession(
      [property: JsonPropertyName("SessionId")] string SessionId,
      [property: JsonPropertyName("ProjectRoot")] string ProjectRoot,
      [property: JsonPropertyName("TargetPath")] string TargetPath,
      [property: JsonPropertyName("OriginalContent")] string OriginalContent,
      [property: JsonPropertyName("OriginalHash")] string OriginalHash,
      [property: JsonPropertyName("OldText")] string OldText,
      [property: JsonPropertyName("NewText")] string NewText,
      [property: JsonPropertyName("ProposedContent")] string ProposedContent,
      [property: JsonPropertyName("Diff")] FileDiffRecord Diff,
      [property: JsonPropertyName("Status")] string Status
  );

  public record EditProposalRecord(
      [property: JsonPropertyName("MessageIndex")] int MessageIndex,
      [property: JsonPropertyName("Status")] string Status,
      [property: JsonPropertyName("Session")] EditProposalSession Session
  );
  ```

- [ ] **Step 2: Buat spesifikasi C# Record `LastSessionRecord.cs`**

  ```csharp
  namespace Nexus.Contracts.Models;

  public record LastSessionMessage(
      [property: JsonPropertyName("Role")] string Role,
      [property: JsonPropertyName("Content")] string Content
  );

  public record LastSessionRecord(
      [property: JsonPropertyName("Messages")] IReadOnlyList<LastSessionMessage> Messages,
      [property: JsonPropertyName("Timestamp")] string Timestamp,
      [property: JsonPropertyName("ActiveModel")] string ActiveModel
  );
  ```

- [ ] **Step 3: Commit spesifikasi edit proposal & last session**

  ```bash
  git add Nexus-CSharp/docs/contracts/last_session.schema.json Nexus-CSharp/docs/contracts/models/EditProposalRecord.cs Nexus-CSharp/docs/contracts/models/LastSessionRecord.cs
  git commit -m "docs(contract): define AI edit proposal and last session schema contracts"
  ```

---

### Task 4: Contract Verification Script Specification (`ContractVerification.ps1`) & README Documentation

**Files:**
- Create: `Nexus-CSharp/docs/contracts/tests/ContractVerification.ps1`
- Create: `Nexus-CSharp/docs/contracts/README.md`

**Interfaces:**
- Consumes: File JSON nyata pada `nexus_data/` dan `AI/`.
- Produces: Laporan verifikasi 100% kelulusan validasi skema JSON tanpa ada kegagalan parsing.

- [ ] **Step 1: Tulis skrip verifikasi otomatis `ContractVerification.ps1`**

  Skrip memvalidasi 38 file JSON percakapan eksisting dan `last_session.json` terhadap seluruh spesifikasi JSON Schema yang baru disusun.

- [ ] **Step 2: Jalankan skrip verifikasi kontrak**

  Run: `powershell -ExecutionPolicy Bypass -File "Nexus-CSharp/docs/contracts/tests/ContractVerification.ps1"`  
  Expected: All 38 conversation JSON files & session files PASS JSON schema validation without errors.

- [ ] **Step 3: Tulis dokumentasi kontrak di `Nexus-CSharp/docs/contracts/README.md`**

- [ ] **Step 4: Commit dokumentasi & skrip verifikasi kontrak**

  ```bash
  git add Nexus-CSharp/docs/contracts/tests/ContractVerification.ps1 Nexus-CSharp/docs/contracts/README.md
  git commit -m "docs(contract): add contract verification script and comprehensive documentation"
  ```

---

## DEFINITION OF DONE (DoD) FASE 0B

Fase 0B dinyatakan **DONE (SELESAI & LENGKAP)** apabila seluruh kriteria berikut terpenuhi:

1. **Seluruh Kontrak Tersusun**: 4 skema JSON (`config`, `conversation_index`, `conversation_detail`, `last_session`) dan 5 model C# DTO Record (`ConfigRecord`, `ConversationIndexRecord`, `ConversationDetailRecord`, `EditProposalRecord`, `LastSessionRecord`) tersimpan rapi di `Nexus-CSharp/docs/contracts/`.
2. **Validasi 100% Lulus**: Skrip `ContractVerification.ps1` memvalidasi seluruh file JSON nyata eksisting (38 file percakapan + last session) tanpa ada error skema.
3. **Zero Runtime C# Code**: Tidak ada file C# kompilasi runtime atau project `.csproj` yang dibuat mendahului Fase 1.
4. **Dokumentasi Lengkap**: `Nexus-CSharp/docs/contracts/README.md` berisi petunjuk membaca dan menggunakan kontrak C# DTO.

---

## EXECUTION HANDOFF

Rencana implementasi Fase 0B telah tersusun lengkap dan disimpan di `Nexus-CSharp/docs/plans/2026-07-22-fase0b-contract-schema-freeze-plan.md`.

Dua pilihan eksekusi yang dapat dipilih:

1. **Subagent-Driven (Sangat Direkomendasikan)**: Menugaskan subagent khusus untuk menyusun skema JSON, C# Records, dan mengeksekusi skrip verifikasi per task secara aman.
2. **Inline Execution**: Menjalankan eksekusi tugas penyusunan kontrak langsung di dalam sesi ini.

Mohon konfirmasi pendekatan eksekusi yang Anda inginkan.
