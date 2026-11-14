# NEXUS

**NEXUS** is a Windows-first desktop application built on **.NET 8 (`net8.0-windows` / WPF)** combining an AI Workspace, high-fidelity PDF-to-Office conversion, document/image processing pipelines, and CMYK color management.

---

## For AI Agents & Contributors

Before modifying any code, **always read**:
1. [`AGENTS.md`](./AGENTS.md) — Canonical repository map, portability rules, single-node MSBuild rules, and multi-agent workspace hygiene.
2. [`GEMINI.md`](./GEMINI.md) — Core engineering principles (simplicity, surgical edits, verification).
3. [`Nexus-CSharp/README.md`](./Nexus-CSharp/README.md) — Design system governance and migration workspace reference.

---

## Repository Layout

```text
NEXUS/
├── AGENTS.md                  # Permanent architecture & agent guidelines
├── GEMINI.md                  # Behavioral & coding rules
├── global.json                # Pinned .NET SDK version (8.0.424)
├── Dev_Run.cmd                # Developer build & launch script
├── Jalankan_NEXUS.cmd         # Application launcher script
├── scratch/                   # Code-pinned test fixtures (Gate I / I2-B)
└── Nexus-CSharp/
    ├── Directory.Build.props  # MSBuild exclusions & warning rules
    ├── Directory.Build.targets
    ├── docs/                  # Architecture, specs, roadmaps, contracts, archive
    ├── tools/                 # Official helper/acceptance scripts
    └── src/                   # 4-layer C# solution (Nexus.sln)
        ├── 00_Contracts/      # Interfaces, DTOs, pure contracts
        ├── 01_Foundation/     # Config, logging, registry, foundation
        ├── 02_Modules/        # Independent feature modules & unit tests
        └── 03_App/            # WPF Desktop App (Nexus.App) & Integration Tests
```

> **Note:** `Engine/`, `Models/`, `nexus_data/`, `Nexus-CSharp/docs/`, and all `bin/` / `obj/` build folders are intentionally excluded from Git via `.gitignore`.

---

## Prerequisites & Build

- **OS:** Windows 10/11 (x64)
- **.NET SDK:** `8.0.424` (pinned in [`global.json`](./global.json))

### Build & Test (Single-Node MSBuild)

Per [`AGENTS.md`](./AGENTS.md) Section 3, build and test using single-node MSBuild flags:

```powershell
dotnet restore Nexus-CSharp/src/Nexus.sln
dotnet build Nexus-CSharp/src/Nexus.sln -c Release --no-restore -m:1 -p:BuildInParallel=false
```

To build and launch in Debug mode on Windows:

```cmd
Dev_Run.cmd
```

---

## External Runtime Folders (`Engine/` & `Models/`)

Large external binaries (`LlamaServer`, `NexusConverterEngine` runtime, ONNX/PDFium DLLs) and AI model weights (`GGUF` / `ONNX`, ~16 GB combined) are not stored in Git.

To run full AI, OCR, Inpaint, RemoveBackground, and PDF conversion features locally, place them at the repository root following the portable layout in [`AGENTS.md`](./AGENTS.md):

```text
NEXUS/
├── Engine/
│   ├── Convert from PDF/
│   └── LlamaServer/
└── Models/
    ├── AI Workspace/
    ├── RemoveBackground/
    └── Inpaint/
```
