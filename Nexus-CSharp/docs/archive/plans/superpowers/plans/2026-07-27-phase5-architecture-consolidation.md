# Phase 5 Architecture Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the two approved Phase 5 freeze blockers: pipeline knowledge of physical model paths and incompatible ONNX metadata producer/parser schemas.

**Architecture:** Model selection continues to return an immutable `ResolvedModelDescriptor`; pipelines pass that descriptor unchanged to `IGenericImageEngine`, and only the engine reads its physical path. `ModelMetadataParser` owns the canonical M2.2 ONNX JSON schema for both writing and reading, while `OnnxModelManager` only inspects ONNX technical metadata and delegates serialization.

**Tech Stack:** .NET 8, C#, xUnit, Moq, System.Text.Json, ONNX Runtime, WPF.

## Global Constraints

- Do not perform legacy cleanup, unrelated refactoring, roadmap changes, new abstractions, or compatibility layers.
- Replace the string-based image model-loading contract; do not retain an overload.
- Treat `ResolvedModelDescriptor` as immutable and never mutate it in pipelines or the engine.
- The returned `string` is a documented opaque `SessionId`, never a path.
- M2.2 metadata is the only active ONNX schema.

---

### Task 1: Descriptor-Based Model Loading

**Files:**
- Modify: `src/00_Contracts/Nexus.Contracts/IGenericImageEngine.cs`
- Modify: `src/02_Modules/Nexus.Engine.ImageSubsystem/Services/GenericImageEngine.cs`
- Modify: `src/02_Modules/Nexus.Engine.ImageSubsystem/Pipelines/RemoveBackgroundPipeline.cs`
- Modify: `src/02_Modules/Nexus.Engine.ImageSubsystem/Pipelines/UpscalePipeline.cs`
- Test: `src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/RemoveBackgroundPipelineTests.cs`
- Test: `src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/UpscalePipelineTests.cs`
- Test: `src/03_App/Nexus.Integration.Tests/ImageSubsystemWorkflowTests.cs`

**Interfaces:**
- Consumes: `ResolvedModelDescriptor`, `EngineSessionOptions`.
- Produces: `Task<string> LoadModelAsync(ResolvedModelDescriptor descriptor, EngineSessionOptions? options = null)`, where the result is an opaque SessionId.

- [ ] **Step 1: Write failing contract and pipeline tests**

Assert that `IGenericImageEngine.LoadModelAsync` accepts `ResolvedModelDescriptor`, has no string-path overload, receives the same descriptor instance, and returns an opaque SessionId.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/Nexus.Engine.ImageSubsystem.Tests.csproj --filter "FullyQualifiedName~PipelineTests"
```

Expected: compilation/test failure because the contract still accepts `string modelPath`.

- [ ] **Step 3: Implement the minimal contract replacement**

Replace the old signature, pass the descriptor unchanged from both pipelines, and let `GenericImageEngine` validate `descriptor.ModelType`, read `descriptor.ModelPath`, and create the session. Document the return as an opaque SessionId.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the pipeline test command again and require zero failures.

### Task 2: Canonical M2.2 Metadata Round Trip

**Files:**
- Modify: `src/01_Foundation/Nexus.Foundation/Registry/ModelMetadataParser.cs`
- Modify: `src/02_Modules/Nexus.Engine.ImageSubsystem/Services/OnnxModelManager.cs`
- Test: `src/01_Foundation/Nexus.Foundation.Tests/ModelMetadataParserTests.cs`
- Test: `src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/OnnxModelManagerTests.cs`

**Interfaces:**
- Consumes: an inspected `ResolvedModelDescriptor`.
- Produces: `WriteOnnxMetadata(string metadataPath, ResolvedModelDescriptor descriptor)`, emitting the canonical nested M2.2 schema.

- [ ] **Step 1: Write failing canonical-schema and round-trip tests**

Require camel-case `modelId`, relative `modelFile`, nested `input`/`output`, and successful write-read descriptor equivalence. Require generator output to parse through `ModelMetadataParser`.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test src/01_Foundation/Nexus.Foundation.Tests/Nexus.Foundation.Tests.csproj --filter "FullyQualifiedName~ModelMetadataParserTests"
dotnet test src/02_Modules/Nexus.Engine.ImageSubsystem.Tests/Nexus.Engine.ImageSubsystem.Tests.csproj --filter "FullyQualifiedName~OnnxModelManagerTests"
```

Expected: failure because no canonical writer exists and the current generator emits the flat legacy schema.

- [ ] **Step 3: Implement the minimal canonical writer and producer delegation**

Add writing to the existing concrete parser, remove the private flat metadata schema from `OnnxModelManager`, construct an immutable descriptor from ONNX inspection, write through the parser, and parse it back before reporting success.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run both metadata test commands again and require zero failures.

### Task 3: Phase 5 Freeze Verification

**Files:**
- Modify only the official roadmap status after every required verification passes.

**Interfaces:**
- Consumes: the completed changes from Tasks 1 and 2.
- Produces: fresh build, scoped-test, architecture-test, round-trip, pipeline, and graceful smoke evidence.

- [ ] **Step 1: Build the solution**

```powershell
dotnet build
```

- [ ] **Step 2: Run scoped and architecture tests**

Run Foundation metadata tests, ImageSubsystem tests, and the relevant integration architecture/workflow tests serially.

- [ ] **Step 3: Run graceful startup/shutdown smoke**

Start Nexus, verify startup without exception, close through the application exit path, and verify both `Nexus.App` and `llama-server` process counts return to zero.

- [ ] **Step 4: Update freeze status only on complete PASS**

If and only if every command passes, update the official roadmap to `Phase 5 Freeze: COMPLETE/FROZEN`. Otherwise report the exact blocker without changing freeze status.
