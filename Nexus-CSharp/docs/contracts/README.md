# Nexus-CSharp Data Contracts Specification

Dokumen ini berisi spesifikasi resmi kontrak JSON Schema dan model C# DTO Record yang dibekukan pada **Fase 0B (Contract & Schema Freeze)** sesuai pedoman **Nexus Migration Constitution v1.0**.

---

## 1. Daftar Dokumen Skema & Model C# DTO

Seluruh file spesifikasi tersimpan di `Nexus-CSharp/docs/contracts/`:

| Nama Kontrak Data | JSON Schema Draft-07 | C# DTO Record Model | Status Verifikasi Data Nyata |
| :--- | :--- | :--- | :---: |
| **App Configuration** | [`config.schema.json`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/config.schema.json) | [`models/ConfigRecord.cs`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/models/ConfigRecord.cs) |  PASS (1/1) |
| **Conversation Index** | [`conversation_index.schema.json`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/conversation_index.schema.json) | [`models/ConversationIndexRecord.cs`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/models/ConversationIndexRecord.cs) |  PASS (1/1) |
| **Conversation Detail** | [`conversation_detail.schema.json`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/conversation_detail.schema.json) | [`models/ConversationDetailRecord.cs`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/models/ConversationDetailRecord.cs) |  PASS (36/36) |
| **AI Edit Proposal** | *Embedded in Detail Schema* | [`models/EditProposalRecord.cs`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/models/EditProposalRecord.cs) |  PASS (Embedded) |
| **AI Last Session** | [`last_session.schema.json`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/last_session.schema.json) | [`models/LastSessionRecord.cs`](file:///C:/Users/Syand/3D%20Objects/Nexus/Nexus-CSharp/docs/contracts/models/LastSessionRecord.cs) |  PASS (1/1) |

---

## 2. Hasil Verifikasi Data Nyata (`38 / 38 Passed 100%`)

Seluruh file data nyata pada `nexus_data/conversations/` (36 file detail + 1 file `index.json`) serta `AI/last_session.json` (1 file) diuji menggunakan runner verifikasi `ContractVerification.ps1`:

```powershell
# Eksekusi Runner Verifikasi Kontrak Data Nyata
powershell -ExecutionPolicy Bypass -File "C:\Users\Syand\3D Objects\Nexus\Nexus-CSharp\docs\contracts\tests\ContractVerification.ps1"
```

**Hasil Evaluasi**:
- Zero breaking changes.
- 100% kompatibel dengan parser C# .NET 8 `System.Text.Json`.
- Seluruh bidang opsional/belum terpakai di C# telah ditandai `// candidate_eval: [penjelasan]`.

---

## 3. Status Fase Migrasi

Fase 0B telah dinyatakan **DONE & FROZEN**. Eksekusi migrasi selanjutnya langsung berpindah ke **Fase 1 (Foundation Core C# Library)**.
