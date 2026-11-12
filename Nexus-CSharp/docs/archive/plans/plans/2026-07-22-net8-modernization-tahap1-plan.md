# [.NET 8 Modernization] Tahap 1: Profiling & Hotspot Identification Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Melakukan profiling mendalam pada aplikasi `Nexus.exe` (.NET 8 Native Host) untuk mengidentifikasi hotspot CPU dan alokasi memori GC (Garbage Collection) pada alur kerja AI Workspace, PDF Utilities, Image Subsystem, dan File Utilities.

**Modernization Workflow Cycle:**
$$\text{Profiling} \longrightarrow \text{Implementasi} \longrightarrow \text{Benchmark} \longrightarrow \text{Regression Test} \longrightarrow \text{Freeze}$$

---

## Roadmap Modernisasi .NET 8

- [x] **Migrasi PowerShell $\rightarrow$ Native C# WPF** (**VERIFIED & FROZEN**)
- [ ] **Tahap 1: Profiling & Identifikasi Hotspot CPU/Memori** (Aktif)
- [ ] **Tahap 2: Memory Efficiency (`Span<T>`, `Memory<T>`, `ArrayPool<byte>`)**
- [ ] **Tahap 3: I/O Optimization & Streaming Pipelines**
- [ ] **Tahap 4: ONNX Runtime Optimization & DirectML/CUDA**
- [ ] **Tahap 5: UX Responsiveness & UI Virtualization**

---

## Identifikasi Hotspot Profiling (Target Metrics)

1. **Memory Allocation & GC Pressure (Tahap 1 Focus)**:
   - Encoding gambar SkiaSharp saat konversi PDF ke JPEG/PNG (alokasi buffer piksel BGRA).
   - Penguraian JSON stream SSE pada `ILlamaClientService` (`LlamaStreamChunk` serialization).
   - Pemindaian direktori dan penyusunan list pada `IRapikanFileService`.

2. **CPU Execution Cycle**:
   - Algoritma pencarian literal string dan diff line-by-line pada `IProposalService` & `IDiffGeneratorService`.
   - Iterasi pemindahan file paralel pada `ICleanDownloadsService`.

---

## Immediate Next Steps (Tahap 1 Execution)

1. Menyiapkan harness profiling memory & CPU execution time.
2. Mencatat baseline alokasi byte dan GC Gen0/Gen1/Gen2 collections untuk setiap skenario utama.
3. Menyusun Laporan Hotspot Profiling dengan bukti empiris.
