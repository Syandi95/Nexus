using System.Text;
using System.Text.Json;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;

namespace Nexus.AI.Llama.Tests;

public sealed class ModelCatalogServiceTests : IDisposable
{
    private readonly List<string> _temporaryRoots = new();

    [Fact]
    public void Load_ValidCatalog_PreservesIndexOrderAndActiveModel()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        var snapshot = service.Load();

        Assert.Equal("gemma-4-e2b", snapshot.ActiveModelId);
        Assert.Collection(
            snapshot.Models,
            model =>
            {
                Assert.Equal("gemma-4-e2b", model.Id);
                Assert.Equal("Gemma 4 E2B", model.DisplayName);
                Assert.True(model.IsAvailable);
                Assert.True(model.IsActive);
            },
            model =>
            {
                Assert.Equal("qwen3-4b", model.Id);
                Assert.Equal("Qwen3 4B Instruct", model.DisplayName);
                Assert.True(model.IsAvailable);
                Assert.False(model.IsActive);
            });
        Assert.Same(snapshot.Models[0], snapshot.ActiveModel);
        Assert.Equal(snapshot, service.CurrentSnapshot);
    }

    [Fact]
    public void Load_CorruptInactiveMetadata_DisablesOnlyThatModel()
    {
        var root = CreateValidCatalog();
        File.WriteAllText(
            Path.Combine(root, "Qwen3-4B", "model.json"),
            "{broken");

        var snapshot = CreateService(root).Load();

        Assert.True(snapshot.ActiveModel!.IsAvailable);
        Assert.Equal(
            ModelCatalogItemStatus.MetadataInvalid,
            snapshot.Models[1].Status);
        Assert.False(snapshot.Models[1].IsAvailable);
    }

    [Fact]
    public void Load_CorruptActiveMetadata_KeepsSnapshotButBlocksActiveModel()
    {
        var root = CreateValidCatalog();
        File.WriteAllText(
            Path.Combine(root, "Gemma-4-E2B", "model.json"),
            "{broken");

        var snapshot = CreateService(root).Load();

        Assert.NotNull(snapshot.ActiveModel);
        Assert.False(snapshot.ActiveModel!.IsAvailable);
        Assert.Equal(
            ModelCatalogItemStatus.MetadataInvalid,
            snapshot.ActiveModel.Status);
        Assert.True(snapshot.Models[1].IsAvailable);
    }

    [Fact]
    public void Load_MissingInactiveGguf_DisablesOnlyThatModel()
    {
        var root = CreateValidCatalog();
        File.Delete(Path.Combine(root, "Qwen3-4B", "qwen.gguf"));

        var snapshot = CreateService(root).Load();

        Assert.True(snapshot.ActiveModel!.IsAvailable);
        Assert.Equal(
            ModelCatalogItemStatus.GgufNotFound,
            snapshot.Models[1].Status);
    }

    [Fact]
    public void Load_MetadataIdMismatch_DisablesOnlyThatModel()
    {
        var root = CreateValidCatalog();
        WriteModel(
            root,
            "Qwen3-4B",
            """
            {
              "schemaVersion": 1,
              "id": "different-id",
              "displayName": "Qwen",
              "ggufFile": "qwen.gguf"
            }
            """);

        var snapshot = CreateService(root).Load();

        Assert.Equal(
            ModelCatalogItemStatus.IdMismatch,
            snapshot.Models[1].Status);
        Assert.True(snapshot.ActiveModel!.IsAvailable);
    }

    [Fact]
    public void Load_GgufPathTraversal_DisablesOnlyThatModel()
    {
        var root = CreateValidCatalog();
        WriteModel(
            root,
            "Qwen3-4B",
            """
            {
              "schemaVersion": 1,
              "id": "qwen3-4b",
              "displayName": "Qwen",
              "ggufFile": "../qwen.gguf"
            }
            """);

        var snapshot = CreateService(root).Load();

        Assert.Equal(
            ModelCatalogItemStatus.GgufPathInvalid,
            snapshot.Models[1].Status);
        Assert.True(snapshot.ActiveModel!.IsAvailable);
    }

    [Fact]
    public void Load_ActiveModelNotRegistered_ThrowsCatalogError()
    {
        var root = CreateValidCatalog();
        ReplaceIndexProperty(root, "activeModelId", "missing-model");

        var error = Assert.Throws<ModelCatalogException>(
            () => CreateService(root).Load());

        Assert.Equal(
            ModelCatalogErrorCode.ActiveModelNotRegistered,
            error.Code);
        Assert.Equal("Model aktif tidak terdaftar.", error.UserMessage);
    }

    [Fact]
    public void Load_UnsafeMetadataPath_ThrowsCatalogError()
    {
        var root = CreateValidCatalog();
        var indexPath = Path.Combine(root, "model-index.json");
        File.WriteAllText(
            indexPath,
            """
            {
              "schemaVersion": 1,
              "activeModelId": "gemma-4-e2b",
              "models": [
                {
                  "id": "gemma-4-e2b",
                  "metadataFile": "../outside/model.json"
                }
              ]
            }
            """);

        var error = Assert.Throws<ModelCatalogException>(
            () => CreateService(root).Load());

        Assert.Equal(ModelCatalogErrorCode.IndexInvalid, error.Code);
    }

    [Fact]
    public void Load_DuplicateIds_ThrowsCatalogError()
    {
        var root = CreateValidCatalog();
        var indexPath = Path.Combine(root, "model-index.json");
        File.WriteAllText(
            indexPath,
            """
            {
              "schemaVersion": 1,
              "activeModelId": "gemma-4-e2b",
              "models": [
                {
                  "id": "gemma-4-e2b",
                  "metadataFile": "Gemma-4-E2B/model.json"
                },
                {
                  "id": "gemma-4-e2b",
                  "metadataFile": "Qwen3-4B/model.json"
                }
              ]
            }
            """);

        var error = Assert.Throws<ModelCatalogException>(
            () => CreateService(root).Load());

        Assert.Equal(ModelCatalogErrorCode.IndexInvalid, error.Code);
    }

    [Fact]
    public async Task UpdateActiveModelAsync_PreservesUnknownFieldsAndOrder()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root, ServerState.Stopped);
        service.Load();

        var snapshot = await service.UpdateActiveModelAsync("qwen3-4b");

        Assert.Equal("qwen3-4b", snapshot.ActiveModelId);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "model-index.json")));
        var rootElement = document.RootElement;
        Assert.Equal(
            "unknown fields are forward-compatible",
            rootElement.GetProperty("catalogNote").GetString());
        var models = rootElement.GetProperty("models");
        Assert.Equal("gemma-4-e2b", models[0].GetProperty("id").GetString());
        Assert.Equal("preserve-me", models[0].GetProperty("futureEntryField").GetString());
        Assert.Equal("qwen3-4b", models[1].GetProperty("id").GetString());
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task UpdateActiveModelAsync_AfterVerifiedRuntimeIsReady_CommitsAtomically()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root, ServerState.Ready);
        service.Load();

        var snapshot = await service.UpdateActiveModelAsync("qwen3-4b");

        Assert.Equal("qwen3-4b", snapshot.ActiveModelId);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "model-index.json")));
        Assert.Equal(
            "qwen3-4b",
            document.RootElement.GetProperty("activeModelId").GetString());
    }

    [Fact]
    public async Task UpdateActiveModelAsync_InvalidTarget_DoesNotChangeIndex()
    {
        var root = CreateValidCatalog();
        File.Delete(Path.Combine(root, "Qwen3-4B", "qwen.gguf"));
        var service = CreateService(root, ServerState.Stopped);
        service.Load();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateActiveModelAsync("qwen3-4b"));

        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "model-index.json")));
        Assert.Equal(
            "gemma-4-e2b",
            document.RootElement.GetProperty("activeModelId").GetString());
    }

    [Fact]
    public void GgufMetadataReader_InvalidMagic_ReturnsFalseAndThrows()
    {
        var invalidBytes = new byte[32]; // all zeroes
        using var stream = new MemoryStream(invalidBytes);

        Assert.False(GgufMetadataReader.IsValidGguf(stream));
        Assert.Throws<InvalidDataException>(() => GgufMetadataReader.ReadMetadata(stream));
    }

    [Fact]
    public void GgufMetadataReader_ValidGguf_ZeroMetadataKv_ReturnsNullFields()
    {
        var validEmptyKv = BuildGgufBytes(); // 24 bytes valid GGUF header with 0 KV
        using var stream = new MemoryStream(validEmptyKv);

        Assert.True(GgufMetadataReader.IsValidGguf(stream));
        var metadata = GgufMetadataReader.ReadMetadata(stream);

        Assert.Null(metadata.Name);
        Assert.Null(metadata.Architecture);
        Assert.Null(metadata.ContextLength);
    }

    [Fact]
    public void GgufMetadataReader_ValidGguf_WithFullMetadata_ExtractsAccurately()
    {
        var fullKv = BuildGgufBytes(
            name: "Qwen 2.5 3B",
            architecture: "qwen2",
            contextLength: 32768);
        using var stream = new MemoryStream(fullKv);

        Assert.True(GgufMetadataReader.IsValidGguf(stream));
        var metadata = GgufMetadataReader.ReadMetadata(stream);

        Assert.Equal("Qwen 2.5 3B", metadata.Name);
        Assert.Equal("qwen2", metadata.Architecture);
        Assert.Equal(32768, metadata.ContextLength);
    }

    [Fact]
    public void Load_DoesNotPerformDirectoryEnumerationOrGgufScanning()
    {
        var root = CreateValidCatalog();
        var unindexedDir = Path.Combine(root, "UnindexedModel");
        Directory.CreateDirectory(unindexedDir);
        File.WriteAllBytes(Path.Combine(unindexedDir, "unindexed.gguf"), BuildGgufBytes());

        var service = CreateService(root);
        var snapshot = service.Load();

        // Snapshot only contains the 2 models in index
        Assert.Equal(2, snapshot.Models.Count);
        Assert.DoesNotContain(snapshot.Models, m => m.Id == "unindexedmodel");
        // model.json was NOT generated for unindexed dir (Load is purely passive)
        Assert.False(File.Exists(Path.Combine(unindexedDir, "model.json")));
        Assert.False(File.Exists(Path.Combine(unindexedDir, "manifest.json")));
    }

    [Fact]
    public async Task SyncCatalogAsync_SingleGguf_GeneratesMetadataWithFallback()
    {
        var root = CreateValidCatalog();
        var newModelDir = Path.Combine(root, "Gemma-4-E4B");
        Directory.CreateDirectory(newModelDir);
        File.WriteAllBytes(Path.Combine(newModelDir, "gemma-4-E4B-it.gguf"), BuildGgufBytes());

        var service = CreateService(root);
        var snapshot = await service.SyncCatalogAsync();

        Assert.Equal(3, snapshot.Models.Count);
        var added = snapshot.Models.FirstOrDefault(m => m.Id == "gemma-4-e4b");
        Assert.NotNull(added);
        Assert.Equal("Gemma 4 E4B", added.DisplayName);

        // Verify manifest fallback
        var manifestPath = Path.Combine(newModelDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal("Gemma", doc.RootElement.GetProperty("family").GetString());
        Assert.Equal(8192, doc.RootElement.GetProperty("context").GetInt32());
        Assert.Equal("fallback", doc.RootElement.GetProperty("_contextSource").GetString());
    }

    [Fact]
    public async Task SyncCatalogAsync_ZeroGgufInFolder_IgnoresFolder()
    {
        var root = CreateValidCatalog();
        var emptyDir = Path.Combine(root, "EmptyDir");
        Directory.CreateDirectory(emptyDir);

        var service = CreateService(root);
        var snapshot = await service.SyncCatalogAsync();

        Assert.Equal(2, snapshot.Models.Count);
        Assert.DoesNotContain(snapshot.Models, m => m.Id == "emptydir");
        Assert.False(File.Exists(Path.Combine(emptyDir, "model.json")));
    }

    [Fact]
    public async Task SyncCatalogAsync_MultipleGgufInFolder_RejectsAmbiguousFolder()
    {
        var root = CreateValidCatalog();
        var multiDir = Path.Combine(root, "MultiGguf");
        Directory.CreateDirectory(multiDir);
        File.WriteAllBytes(Path.Combine(multiDir, "model1.gguf"), BuildGgufBytes());
        File.WriteAllBytes(Path.Combine(multiDir, "model2.gguf"), BuildGgufBytes());

        var service = CreateService(root);
        var snapshot = await service.SyncCatalogAsync();

        Assert.Equal(2, snapshot.Models.Count);
        Assert.DoesNotContain(snapshot.Models, m => m.Id == "multigguf");
        Assert.False(File.Exists(Path.Combine(multiDir, "model.json")));
    }

    [Fact]
    public async Task SyncCatalogAsync_ActiveModelMissingFromDisk_RevalidatesDeterministically()
    {
        var root = CreateValidCatalog();
        // Delete active model directory
        Directory.Delete(Path.Combine(root, "Gemma-4-E2B"), recursive: true);

        var service = CreateService(root);
        var snapshot = await service.SyncCatalogAsync();

        Assert.Single(snapshot.Models);
        Assert.Equal("qwen3-4b", snapshot.ActiveModelId);
        Assert.Equal("qwen3-4b", snapshot.ActiveModel?.Id);
    }

    [Fact]
    public async Task SyncCatalogAsync_AllModelsMissingFromDisk_SetsActiveModelIdToNull()
    {
        var root = CreateValidCatalog();
        Directory.Delete(Path.Combine(root, "Gemma-4-E2B"), recursive: true);
        Directory.Delete(Path.Combine(root, "Qwen3-4B"), recursive: true);

        var service = CreateService(root);
        var snapshot = await service.SyncCatalogAsync();

        Assert.Empty(snapshot.Models);
        Assert.Null(snapshot.ActiveModelId);
        Assert.Null(snapshot.ActiveModel);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "model-index.json")));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("activeModelId").ValueKind);
    }

    [Fact]
    public async Task DeleteModelAsync_LastModel_SetsActiveModelIdToNull()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        await service.DeleteModelAsync("qwen3-4b");
        var snapshot = await service.DeleteModelAsync("gemma-4-e2b");

        Assert.Empty(snapshot.Models);
        Assert.Null(snapshot.ActiveModelId);
        Assert.Null(snapshot.ActiveModel);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "model-index.json")));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("activeModelId").ValueKind);
    }

    [Fact]
    public async Task DeleteModelAsync_ActiveModel_SwitchesToFirstRemainingValidModel()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        var snapshot = await service.DeleteModelAsync("gemma-4-e2b");

        Assert.Single(snapshot.Models);
        Assert.Equal("qwen3-4b", snapshot.ActiveModelId);
        Assert.Equal("qwen3-4b", snapshot.ActiveModel?.Id);
        Assert.False(Directory.Exists(Path.Combine(root, "Gemma-4-E2B")));
    }

    [Fact]
    public async Task DeleteModelAsync_PathTraversal_RejectsUnauthorizedAccess()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        // Attempting to delete with invalid ID
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.DeleteModelAsync("../outside"));
    }

    [Fact]
    public async Task DeleteModelAsync_FolderAlreadyMissingOnDisk_CleansIndexGracefully()
    {
        var root = CreateValidCatalog();
        Directory.Delete(Path.Combine(root, "Qwen3-4B"), recursive: true);
        var service = CreateService(root);

        var snapshot = await service.DeleteModelAsync("qwen3-4b");

        Assert.Single(snapshot.Models);
        Assert.DoesNotContain(snapshot.Models, m => m.Id == "qwen3-4b");
    }

    [Fact]
    public async Task AddModelAsync_ValidGguf_CopiesFileAndUpdatesCatalog()
    {
        var root = CreateValidCatalog();
        var tempSourceGguf = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(tempSourceGguf, BuildGgufBytes("Phi 3 Mini", "phi3", 4096));

        try
        {
            var service = CreateService(root);
            double lastProgress = 0;
            var progress = new Progress<double>(p => lastProgress = p);

            var snapshot = await service.AddModelAsync(tempSourceGguf, "Phi 3 Mini", progress);

            Assert.Equal(3, snapshot.Models.Count);
            var added = snapshot.Models.FirstOrDefault(m => m.Id == "phi-3-mini");
            Assert.NotNull(added);
            Assert.Equal("Phi 3 Mini", added.DisplayName);
            Assert.True(File.Exists(added.ResolvedGgufPath));
        }
        finally
        {
            if (File.Exists(tempSourceGguf)) File.Delete(tempSourceGguf);
        }
    }

    [Fact]
    public async Task AddModelAsync_DuplicateCollision_ThrowsWithoutOverwriting()
    {
        var root = CreateValidCatalog();
        var tempSourceGguf = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(tempSourceGguf, BuildGgufBytes());

        try
        {
            var service = CreateService(root);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.AddModelAsync(tempSourceGguf, "Gemma 4 E2B"));
        }
        finally
        {
            if (File.Exists(tempSourceGguf)) File.Delete(tempSourceGguf);
        }
    }

    [Fact]
    public async Task AddModelAsync_Cancellation_CleansUpPartiallyCopiedFiles()
    {
        var root = CreateValidCatalog();
        var tempSourceGguf = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(tempSourceGguf, BuildGgufBytes());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        try
        {
            var service = CreateService(root);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.AddModelAsync(tempSourceGguf, "CancelledModel", null, cts.Token));

            Assert.False(Directory.Exists(Path.Combine(root, "cancelledmodel")));
        }
        finally
        {
            if (File.Exists(tempSourceGguf)) File.Delete(tempSourceGguf);
        }
    }

    [Fact]
    public async Task ConcurrentOperations_SerializedByLock()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        // Run sync and update active model concurrently
        var task1 = service.SyncCatalogAsync();
        var task2 = service.UpdateActiveModelAsync("qwen3-4b");

        await Task.WhenAll(task1, task2);

        var snapshot = service.CurrentSnapshot;
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.ActiveModelId);
    }

    [Fact]
    public async Task ReorderModelAsync_MoveUp_SwapsWithPreviousModelAndSavesIndex()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        // Initially: index 0 is gemma-4-e2b, index 1 is qwen3-4b
        var initialSnapshot = service.Load();
        Assert.Equal("gemma-4-e2b", initialSnapshot.Models[0].Id);
        Assert.Equal("qwen3-4b", initialSnapshot.Models[1].Id);

        // Move qwen3-4b up
        var updated = await service.ReorderModelAsync("qwen3-4b", ModelOrderDirection.Up);
        Assert.Equal("qwen3-4b", updated.Models[0].Id);
        Assert.Equal("gemma-4-e2b", updated.Models[1].Id);

        // Fresh service reading from disk to verify persistence
        var service2 = CreateService(root);
        var reloaded = service2.Load();
        Assert.Equal("qwen3-4b", reloaded.Models[0].Id);
        Assert.Equal("gemma-4-e2b", reloaded.Models[1].Id);
    }

    [Fact]
    public async Task ReorderModelAsync_MoveToTop_MovesDirectlyToFirst()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        // Add 3rd model
        var srcPath = Path.Combine(root, "temp3.gguf");
        File.WriteAllBytes(srcPath, BuildGgufBytes("Model Three", "llama", 4096));
        await service.AddModelAsync(srcPath, "Model Three");

        var snapshot3 = service.Load();
        Assert.Equal(3, snapshot3.Models.Count);
        var lastModelId = snapshot3.Models[2].Id;

        // Move last model to top
        var updated = await service.ReorderModelAsync(lastModelId, ModelOrderDirection.ToTop);
        Assert.Equal(lastModelId, updated.Models[0].Id);

        // Reload from disk
        var service2 = CreateService(root);
        var reloaded = service2.Load();
        Assert.Equal(lastModelId, reloaded.Models[0].Id);
    }

    [Fact]
    public async Task ReorderModelAsync_MoveDown_SwapsWithNextModel()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        var initialSnapshot = service.Load();
        var firstId = initialSnapshot.Models[0].Id;
        var secondId = initialSnapshot.Models[1].Id;

        // Move first model down
        var updated = await service.ReorderModelAsync(firstId, ModelOrderDirection.Down);
        Assert.Equal(secondId, updated.Models[0].Id);
        Assert.Equal(firstId, updated.Models[1].Id);
    }

    [Fact]
    public async Task ReorderModelAsync_BoundsCheck_DoesNotThrowWhenAlreadyAtTopOrBottom()
    {
        var root = CreateValidCatalog();
        var service = CreateService(root);

        var initialSnapshot = service.Load();
        var firstId = initialSnapshot.Models[0].Id;
        var lastId = initialSnapshot.Models[^1].Id;

        // Move top model up / to top -> no-op, no throw
        var res1 = await service.ReorderModelAsync(firstId, ModelOrderDirection.Up);
        Assert.Equal(firstId, res1.Models[0].Id);

        var res2 = await service.ReorderModelAsync(firstId, ModelOrderDirection.ToTop);
        Assert.Equal(firstId, res2.Models[0].Id);

        // Move bottom model down -> no-op, no throw
        var res3 = await service.ReorderModelAsync(lastId, ModelOrderDirection.Down);
        Assert.Equal(lastId, res3.Models[^1].Id);
    }

    private static byte[] BuildGgufBytes(string? name = null, string? architecture = null, int? contextLength = null)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);
        writer.Write("GGUF"u8.ToArray());
        writer.Write((uint)3); // version
        writer.Write((ulong)0); // tensor count

        var kvList = new List<Action>();
        if (name != null)
        {
            kvList.Add(() =>
            {
                WriteGgufString(writer, "general.name");
                writer.Write((uint)8); // STRING
                WriteGgufString(writer, name);
            });
        }
        if (architecture != null)
        {
            kvList.Add(() =>
            {
                WriteGgufString(writer, "general.architecture");
                writer.Write((uint)8); // STRING
                WriteGgufString(writer, architecture);
            });
        }
        if (contextLength.HasValue && architecture != null)
        {
            kvList.Add(() =>
            {
                WriteGgufString(writer, $"{architecture}.context_length");
                writer.Write((uint)4); // UINT32
                writer.Write((uint)contextLength.Value);
            });
        }

        writer.Write((ulong)kvList.Count);
        foreach (var action in kvList)
        {
            action();
        }

        return ms.ToArray();
    }

    private static void WriteGgufString(BinaryWriter writer, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }

    public void Dispose()
    {
        foreach (var root in _temporaryRoots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private string CreateValidCatalog()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "NexusModelCatalogTests",
            Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ModelCatalog",
            "Valid");
        CopyDirectory(fixtureRoot, root);
        File.WriteAllBytes(
            Path.Combine(root, "Gemma-4-E2B", "gemma.gguf"),
            new byte[] { 0x47 });
        File.WriteAllBytes(
            Path.Combine(root, "Qwen3-4B", "qwen.gguf"),
            new byte[] { 0x47 });
        _temporaryRoots.Add(root);
        return root;
    }

    private static ModelCatalogService CreateService(
        string root,
        ServerState state = ServerState.Stopped)
    {
        var server = new Mock<ILlamaServerManager>();
        server.SetupGet(manager => manager.State).Returns(state);
        return new ModelCatalogService(root, server.Object);
    }

    private static void ReplaceIndexProperty(
        string root,
        string property,
        string value)
    {
        var path = Path.Combine(root, "model-index.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(path))!.AsObject();
        node[property] = value;
        File.WriteAllText(
            path,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteModel(
        string root,
        string directory,
        string json)
    {
        File.WriteAllText(
            Path.Combine(root, directory, "model.json"),
            json);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                directory.Replace(source, destination));
        }

        foreach (var file in Directory.GetFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.Copy(
                file,
                file.Replace(source, destination));
        }
    }
}
