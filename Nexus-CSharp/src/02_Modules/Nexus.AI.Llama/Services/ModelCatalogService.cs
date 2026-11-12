using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Services;

public sealed partial class ModelCatalogService : IModelCatalogService
{
    private const int SupportedSchemaVersion = 1;
    private readonly string _catalogRoot;
    private readonly string _indexPath;
    private readonly ILlamaServerManager _serverManager;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public ModelCatalogService(
        string catalogRoot,
        ILlamaServerManager serverManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        _catalogRoot = Path.GetFullPath(catalogRoot);
        _indexPath = Path.Combine(_catalogRoot, "model-index.json");
        _serverManager = serverManager
            ?? throw new ArgumentNullException(nameof(serverManager));
    }

    public ModelCatalogSnapshot? CurrentSnapshot { get; private set; }

    public ModelCatalogSnapshot Load()
    {
        CurrentSnapshot = LoadCore();
        return CurrentSnapshot;
    }

    public async Task<ModelCatalogSnapshot> UpdateActiveModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = LoadCore();
            var target = snapshot.Models.FirstOrDefault(model =>
                string.Equals(model.Id, modelId, StringComparison.Ordinal));
            if (target is null || !target.IsAvailable)
            {
                throw new InvalidOperationException(
                    "Model yang dipilih tidak tersedia.");
            }

            JsonObject index;
            try
            {
                index = JsonNode.Parse(
                    await File.ReadAllTextAsync(
                        _indexPath,
                        cancellationToken).ConfigureAwait(false))?.AsObject()
                    ?? throw new JsonException("Root JSON bukan object.");
            }
            catch (Exception error) when (
                error is JsonException or InvalidOperationException)
            {
                throw CatalogError(
                    ModelCatalogErrorCode.IndexInvalid,
                    "Katalog model AI rusak.",
                    error);
            }

            index["activeModelId"] = modelId;
            await CommitIndexAsync(index, cancellationToken).ConfigureAwait(false);

            CurrentSnapshot = LoadCore();
            return CurrentSnapshot;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task CommitIndexAsync(
        JsonObject index,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            _catalogRoot,
            $".model-index-{Guid.NewGuid():N}.tmp");

        try
        {
            var json = index.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken).ConfigureAwait(false);

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Open,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_indexPath))
            {
                File.Replace(temporaryPath, _indexPath, null);
            }
            else
            {
                File.Move(temporaryPath, _indexPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Ignore cleanup error
                }
            }
        }
    }

    private ModelCatalogSnapshot LoadCore()
    {
        if (!Directory.Exists(_catalogRoot))
        {
            throw CatalogError(
                ModelCatalogErrorCode.RootNotFound,
                "Folder model AI tidak ditemukan.");
        }

        if (!File.Exists(_indexPath))
        {
            throw CatalogError(
                ModelCatalogErrorCode.IndexNotFound,
                "Katalog model AI tidak ditemukan.");
        }

        using var document = ParseIndex();
        var root = document.RootElement;
        RequireObject(root);
        var schemaVersion = RequireInt32(root, "schemaVersion");
        if (schemaVersion != SupportedSchemaVersion)
        {
            throw CatalogError(
                ModelCatalogErrorCode.IndexVersionUnsupported,
                "Versi katalog model AI tidak didukung.");
        }

        string? activeModelId = null;
        if (root.TryGetProperty("activeModelId", out var activeProp))
        {
            if (activeProp.ValueKind == JsonValueKind.String)
            {
                activeModelId = RequireIdentifier(root, "activeModelId");
            }
            else if (activeProp.ValueKind != JsonValueKind.Null)
            {
                throw InvalidIndex();
            }
        }

        if (!root.TryGetProperty("models", out var modelsElement) ||
            modelsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidIndex();
        }

        var entries = new List<IndexEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entryElement in modelsElement.EnumerateArray())
        {
            RequireObject(entryElement);
            var id = RequireIdentifier(entryElement, "id");
            var metadataFile = RequireString(entryElement, "metadataFile");
            if (!ids.Add(id) || !TryResolveUnderRoot(metadataFile, out var metadataPath))
            {
                throw InvalidIndex();
            }

            entries.Add(new IndexEntry(id, metadataFile, metadataPath));
        }

        if (activeModelId is not null)
        {
            if (!ids.Contains(activeModelId))
            {
                throw CatalogError(
                    ModelCatalogErrorCode.ActiveModelNotRegistered,
                    "Model aktif tidak terdaftar.");
            }
        }
        else
        {
            if (entries.Count > 0)
            {
                throw InvalidIndex();
            }
        }

        var models = entries
            .Select(entry => LoadModel(
                entry,
                activeModelId is not null && string.Equals(entry.Id, activeModelId, StringComparison.Ordinal)))
            .ToArray();
        return new ModelCatalogSnapshot(activeModelId, models);
    }

    private JsonDocument ParseIndex()
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(_indexPath));
        }
        catch (Exception error) when (
            error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw CatalogError(
                ModelCatalogErrorCode.IndexInvalid,
                "Katalog model AI rusak.",
                error);
        }
    }

    private ModelCatalogItem LoadModel(IndexEntry entry, bool isActive)
    {
        if (!File.Exists(entry.MetadataPath))
        {
            return InvalidItem(
                entry,
                ModelCatalogItemStatus.MetadataNotFound,
                "Metadata model tidak ditemukan.",
                isActive);
        }

        JsonDocument metadata;
        try
        {
            metadata = JsonDocument.Parse(File.ReadAllText(entry.MetadataPath));
        }
        catch (Exception error) when (
            error is JsonException or IOException or UnauthorizedAccessException)
        {
            return InvalidItem(
                entry,
                ModelCatalogItemStatus.MetadataInvalid,
                "Metadata model rusak.",
                isActive);
        }

        using (metadata)
        {
            var root = metadata.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetInt32(root, "schemaVersion", out var schemaVersion) ||
                !TryGetString(root, "id", out var metadataId) ||
                !TryGetString(root, "displayName", out var displayName) ||
                !TryGetString(root, "ggufFile", out var ggufFile))
            {
                return InvalidItem(
                    entry,
                    ModelCatalogItemStatus.MetadataInvalid,
                    "Metadata model tidak lengkap.",
                    isActive);
            }

            if (schemaVersion != SupportedSchemaVersion)
            {
                return InvalidItem(
                    entry,
                    ModelCatalogItemStatus.MetadataVersionUnsupported,
                    "Versi metadata model tidak didukung.",
                    isActive,
                    displayName);
            }

            if (!string.Equals(entry.Id, metadataId, StringComparison.Ordinal))
            {
                return InvalidItem(
                    entry,
                    ModelCatalogItemStatus.IdMismatch,
                    "ID metadata model tidak cocok.",
                    isActive,
                    displayName);
            }

            if (!IsSafeGgufFileName(ggufFile))
            {
                return InvalidItem(
                    entry,
                    ModelCatalogItemStatus.GgufPathInvalid,
                    "Path GGUF tidak valid.",
                    isActive,
                    displayName);
            }

            var metadataDirectory = Path.GetDirectoryName(entry.MetadataPath)!;
            var ggufPath = Path.GetFullPath(
                Path.Combine(metadataDirectory, ggufFile));
            if (!File.Exists(ggufPath))
            {
                return new ModelCatalogItem(
                    entry.Id,
                    displayName,
                    entry.MetadataFile,
                    ggufPath,
                    ModelCatalogItemStatus.GgufNotFound,
                    "File GGUF tidak ditemukan.",
                    isActive);
            }

            return new ModelCatalogItem(
                entry.Id,
                displayName,
                entry.MetadataFile,
                ggufPath,
                ModelCatalogItemStatus.Valid,
                null,
                isActive);
        }
    }

    private static ModelCatalogItem InvalidItem(
        IndexEntry entry,
        ModelCatalogItemStatus status,
        string message,
        bool isActive,
        string? displayName = null) =>
        new(
            entry.Id,
            string.IsNullOrWhiteSpace(displayName) ? entry.Id : displayName,
            entry.MetadataFile,
            null,
            status,
            message,
            isActive);

    public async Task<ModelCatalogSnapshot> SyncCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_catalogRoot))
            {
                Directory.CreateDirectory(_catalogRoot);
            }

            JsonObject index;
            if (File.Exists(_indexPath))
            {
                try
                {
                    index = JsonNode.Parse(
                        await File.ReadAllTextAsync(_indexPath, cancellationToken).ConfigureAwait(false))?.AsObject()
                        ?? throw new JsonException("Root bukan object");
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    throw CatalogError(ModelCatalogErrorCode.IndexInvalid, "Katalog model AI rusak.", ex);
                }
            }
            else
            {
                index = new JsonObject
                {
                    ["schemaVersion"] = SupportedSchemaVersion,
                    ["activeModelId"] = null,
                    ["models"] = new JsonArray()
                };
            }

            var existingModels = index["models"] as JsonArray ?? new JsonArray();

            var subdirs = Directory.GetDirectories(_catalogRoot);
            foreach (var dir in subdirs)
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;

                var ggufFiles = Directory.GetFiles(dir, "*.gguf");
                if (ggufFiles.Length == 0)
                {
                    continue;
                }
                if (ggufFiles.Length > 1)
                {
                    continue;
                }

                var ggufPath = ggufFiles[0];
                var ggufFileName = Path.GetFileName(ggufPath);
                var modelJsonPath = Path.Combine(dir, "model.json");
                var manifestJsonPath = Path.Combine(dir, "manifest.json");

                GgufTechnicalMetadata? techMeta = null;
                if (!File.Exists(modelJsonPath) || !File.Exists(manifestJsonPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(ggufPath);
                        if (GgufMetadataReader.IsValidGguf(stream))
                        {
                            techMeta = GgufMetadataReader.ReadMetadata(stream);
                        }
                    }
                    catch
                    {
                    }
                }

                string modelId;
                string displayName;
                if (File.Exists(modelJsonPath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
                        var root = doc.RootElement;
                        modelId = root.GetProperty("id").GetString()!;
                        displayName = root.TryGetProperty("displayName", out var dp) ? dp.GetString() ?? dirName : dirName;
                    }
                    catch
                    {
                        modelId = Slugify(dirName);
                        displayName = techMeta?.Name ?? FormatDisplayName(dirName);
                        WriteModelJson(modelJsonPath, modelId, displayName, ggufFileName);
                    }
                }
                else
                {
                    modelId = Slugify(dirName);
                    displayName = techMeta?.Name ?? FormatDisplayName(dirName);
                    WriteModelJson(modelJsonPath, modelId, displayName, ggufFileName);
                }

                if (!File.Exists(manifestJsonPath))
                {
                    var family = techMeta?.Architecture ?? InferFamily(ggufFileName, dirName);
                    var context = techMeta?.ContextLength ?? 8192;
                    var contextSource = techMeta?.ContextLength.HasValue == true ? "native" : "fallback";
                    WriteManifestJson(manifestJsonPath, displayName, ggufFileName, family, context, contextSource);
                }
            }

            var updatedModelsArray = new JsonArray();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var remainingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in existingModels)
            {
                if (node is JsonObject entryObj &&
                    entryObj.TryGetPropertyValue("id", out var idNode) &&
                    entryObj.TryGetPropertyValue("metadataFile", out var mfNode))
                {
                    var id = idNode?.ToString();
                    var relPath = mfNode?.ToString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(relPath))
                    {
                        var fullMetaPath = Path.Combine(_catalogRoot, relPath);
                        if (File.Exists(fullMetaPath))
                        {
                            if (seenIds.Add(id))
                            {
                                updatedModelsArray.Add(entryObj.DeepClone());
                                remainingIds.Add(id);
                            }
                        }
                    }
                }
            }

            foreach (var dir in subdirs)
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;

                var ggufFiles = Directory.GetFiles(dir, "*.gguf");
                if (ggufFiles.Length != 1) continue;

                var modelJsonPath = Path.Combine(dir, "model.json");
                if (!File.Exists(modelJsonPath)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
                    if (doc.RootElement.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString();
                        if (!string.IsNullOrEmpty(id) && seenIds.Add(id))
                        {
                            var relPath = $"{dirName}/model.json";
                            var newEntry = new JsonObject
                            {
                                ["id"] = id,
                                ["metadataFile"] = relPath
                            };
                            updatedModelsArray.Add(newEntry);
                            remainingIds.Add(id);
                        }
                    }
                }
                catch
                {
                }
            }

            index["models"] = updatedModelsArray;

            var currentActiveId = index["activeModelId"]?.ToString();
            if (string.IsNullOrEmpty(currentActiveId) || !remainingIds.Contains(currentActiveId))
            {
                if (remainingIds.Count > 0)
                {
                    string? candidate = null;
                    foreach (var item in updatedModelsArray)
                    {
                        var candidateId = item?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(candidateId) && remainingIds.Contains(candidateId))
                        {
                            candidate = candidateId;
                            break;
                        }
                    }
                    index["activeModelId"] = candidate;
                }
                else
                {
                    index["activeModelId"] = null;
                }
            }

            await CommitIndexAsync(index, cancellationToken).ConfigureAwait(false);
            CurrentSnapshot = LoadCore();
            return CurrentSnapshot;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<ModelCatalogSnapshot> AddModelAsync(
        string sourceGgufPath,
        string? customDisplayName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGgufPath);
        if (!File.Exists(sourceGgufPath))
        {
            throw new FileNotFoundException("File model GGUF sumber tidak ditemukan.", sourceGgufPath);
        }

        if (!string.Equals(Path.GetExtension(sourceGgufPath), ".gguf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File harus memiliki ekstensi .gguf.", nameof(sourceGgufPath));
        }

        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? createdTargetDir = null;

        try
        {
            GgufTechnicalMetadata techMeta;
            using (var checkStream = File.OpenRead(sourceGgufPath))
            {
                if (!GgufMetadataReader.IsValidGguf(checkStream))
                {
                    throw new InvalidDataException("File bukan merupakan format GGUF yang valid.");
                }
                techMeta = GgufMetadataReader.ReadMetadata(checkStream);
            }

            var sourceFileName = Path.GetFileName(sourceGgufPath);
            var displayName = !string.IsNullOrWhiteSpace(customDisplayName)
                ? customDisplayName
                : (techMeta.Name ?? FormatDisplayName(Path.GetFileNameWithoutExtension(sourceGgufPath)));

            var modelId = Slugify(displayName);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = "model-" + Guid.NewGuid().ToString("N")[..8];
            }

            var targetDir = Path.Combine(_catalogRoot, modelId);
            if (Directory.Exists(targetDir))
            {
                throw new InvalidOperationException($"Model dengan ID '{modelId}' sudah terdaftar.");
            }

            if (File.Exists(_indexPath))
            {
                var snap = LoadCore();
                if (snap.Models.Any(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Model dengan ID '{modelId}' sudah terdaftar di indeks.");
                }
            }

            Directory.CreateDirectory(targetDir);
            createdTargetDir = targetDir;

            var destGgufPath = Path.Combine(targetDir, sourceFileName);

            const int bufferSize = 2 * 1024 * 1024;
            var fileLength = new FileInfo(sourceGgufPath).Length;
            long totalBytesRead = 0;

            await using (var sourceStream = new FileStream(
                sourceGgufPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
            await using (var destStream = new FileStream(
                destGgufPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                var buffer = new byte[bufferSize];
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    totalBytesRead += bytesRead;
                    if (fileLength > 0 && progress != null)
                    {
                        progress.Report((double)totalBytesRead / fileLength);
                    }
                }
                await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var modelJsonPath = Path.Combine(targetDir, "model.json");
            WriteModelJson(modelJsonPath, modelId, displayName, sourceFileName);

            var manifestJsonPath = Path.Combine(targetDir, "manifest.json");
            var family = techMeta.Architecture ?? InferFamily(sourceFileName, modelId);
            var context = techMeta.ContextLength ?? 8192;
            var contextSource = techMeta.ContextLength.HasValue ? "native" : "fallback";
            WriteManifestJson(manifestJsonPath, displayName, sourceFileName, family, context, contextSource);

            JsonObject index;
            if (File.Exists(_indexPath))
            {
                index = JsonNode.Parse(
                    await File.ReadAllTextAsync(_indexPath, cancellationToken).ConfigureAwait(false))?.AsObject()
                    ?? throw new JsonException();
            }
            else
            {
                index = new JsonObject
                {
                    ["schemaVersion"] = SupportedSchemaVersion,
                    ["activeModelId"] = modelId,
                    ["models"] = new JsonArray()
                };
            }

            var modelsArray = index["models"] as JsonArray ?? new JsonArray();
            var newEntry = new JsonObject
            {
                ["id"] = modelId,
                ["metadataFile"] = $"{modelId}/model.json"
            };
            modelsArray.Add(newEntry);
            index["models"] = modelsArray;

            var currentActive = index["activeModelId"]?.ToString();
            if (string.IsNullOrEmpty(currentActive))
            {
                index["activeModelId"] = modelId;
            }

            await CommitIndexAsync(index, cancellationToken).ConfigureAwait(false);
            createdTargetDir = null;

            CurrentSnapshot = LoadCore();
            return CurrentSnapshot;
        }
        catch
        {
            if (createdTargetDir != null && Directory.Exists(createdTargetDir))
            {
                try
                {
                    Directory.Delete(createdTargetDir, recursive: true);
                }
                catch
                {
                }
            }
            throw;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<ModelCatalogSnapshot> DeleteModelAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = LoadCore();
            var target = snapshot.Models.FirstOrDefault(m =>
                string.Equals(m.Id, modelId, StringComparison.Ordinal));
            if (target is null)
            {
                throw new KeyNotFoundException($"Model dengan ID '{modelId}' tidak ditemukan di katalog.");
            }

            if (!TryResolveUnderRoot(target.MetadataFile, out var metadataPath))
            {
                throw new UnauthorizedAccessException("Akses path di luar katalog ditolak.");
            }

            var modelDir = Path.GetDirectoryName(metadataPath)!;
            var rootWithSep = _catalogRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
            if (!modelDir.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(modelDir, _catalogRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Akses path di luar katalog ditolak.");
            }

            if (Directory.Exists(modelDir))
            {
                Directory.Delete(modelDir, recursive: true);
            }

            JsonObject index;
            try
            {
                index = JsonNode.Parse(
                    await File.ReadAllTextAsync(_indexPath, cancellationToken).ConfigureAwait(false))?.AsObject()
                    ?? throw new JsonException();
            }
            catch (Exception ex)
            {
                throw CatalogError(ModelCatalogErrorCode.IndexInvalid, "Katalog model AI rusak.", ex);
            }

            var modelsArray = index["models"] as JsonArray ?? new JsonArray();
            var remainingModels = new JsonArray();
            var remainingIds = new List<string>();

            foreach (var node in modelsArray)
            {
                if (node is JsonObject obj && obj.TryGetPropertyValue("id", out var idNode))
                {
                    var id = idNode?.ToString();
                    if (!string.Equals(id, modelId, StringComparison.Ordinal))
                    {
                        remainingModels.Add(obj.DeepClone());
                        if (!string.IsNullOrEmpty(id)) remainingIds.Add(id);
                    }
                }
            }

            index["models"] = remainingModels;

            var currentActive = index["activeModelId"]?.ToString();
            if (string.Equals(currentActive, modelId, StringComparison.Ordinal))
            {
                if (remainingIds.Count > 0)
                {
                    var nextValid = snapshot.Models
                        .FirstOrDefault(m => !string.Equals(m.Id, modelId, StringComparison.Ordinal) && m.IsAvailable);
                    index["activeModelId"] = nextValid?.Id ?? remainingIds[0];
                }
                else
                {
                    index["activeModelId"] = null;
                }
            }

            await CommitIndexAsync(index, cancellationToken).ConfigureAwait(false);
            CurrentSnapshot = LoadCore();
            return CurrentSnapshot;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<ModelCatalogSnapshot> ReorderModelAsync(
        string modelId,
        ModelOrderDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_indexPath))
            {
                throw CatalogError(ModelCatalogErrorCode.IndexNotFound, "Katalog model AI tidak ditemukan.");
            }

            JsonObject index;
            try
            {
                index = JsonNode.Parse(
                    await File.ReadAllTextAsync(_indexPath, cancellationToken).ConfigureAwait(false))?.AsObject()
                    ?? throw new JsonException();
            }
            catch (Exception ex)
            {
                throw CatalogError(ModelCatalogErrorCode.IndexInvalid, "Katalog model AI rusak.", ex);
            }

            var modelsArray = index["models"] as JsonArray ?? new JsonArray();
            int currentIndex = -1;
            JsonNode? targetNode = null;

            for (int i = 0; i < modelsArray.Count; i++)
            {
                if (modelsArray[i] is JsonObject obj &&
                    obj.TryGetPropertyValue("id", out var idNode) &&
                    string.Equals(idNode?.ToString(), modelId, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    targetNode = obj;
                    break;
                }
            }

            if (currentIndex == -1 || targetNode == null)
            {
                throw new KeyNotFoundException($"Model dengan ID '{modelId}' tidak ditemukan di katalog.");
            }

            int targetIndex = direction switch
            {
                ModelOrderDirection.ToTop => 0,
                ModelOrderDirection.Up => Math.Max(0, currentIndex - 1),
                ModelOrderDirection.Down => Math.Min(modelsArray.Count - 1, currentIndex + 1),
                _ => currentIndex
            };

            if (targetIndex != currentIndex)
            {
                var clone = targetNode.DeepClone();
                modelsArray.RemoveAt(currentIndex);
                modelsArray.Insert(targetIndex, clone);
                index["models"] = modelsArray;

                await CommitIndexAsync(index, cancellationToken).ConfigureAwait(false);
                CurrentSnapshot = LoadCore();
            }

            return CurrentSnapshot ?? LoadCore();
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return clean;
    }

    private static string FormatDisplayName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var parts = text.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private static string InferFamily(string fileName, string dirName)
    {
        var combined = $"{fileName} {dirName}".ToLowerInvariant();
        if (combined.Contains("gemma")) return "Gemma";
        if (combined.Contains("qwen")) return "Qwen";
        if (combined.Contains("llama")) return "Llama";
        if (combined.Contains("mistral")) return "Mistral";
        if (combined.Contains("phi")) return "Phi";
        if (combined.Contains("deepseek")) return "DeepSeek";
        return "General";
    }

    private static void WriteModelJson(string path, string id, string displayName, string ggufFile)
    {
        var modelObj = new JsonObject
        {
            ["schemaVersion"] = SupportedSchemaVersion,
            ["id"] = id,
            ["displayName"] = displayName,
            ["ggufFile"] = ggufFile
        };
        File.WriteAllText(path, modelObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteManifestJson(string path, string name, string file, string family, int context, string contextSource)
    {
        var manifestObj = new JsonObject
        {
            ["name"] = name,
            ["provider"] = "llama.cpp",
            ["family"] = family,
            ["context"] = context,
            ["file"] = file,
            ["_contextSource"] = contextSource
        };
        File.WriteAllText(path, manifestObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool TryResolveUnderRoot(
        string relativePath,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_catalogRoot, relativePath));
        var rootWithSeparator = _catalogRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    private static bool IsSafeGgufFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
        string.Equals(Path.GetExtension(fileName), ".gguf", StringComparison.OrdinalIgnoreCase) &&
        !fileName.Contains("..", StringComparison.Ordinal);

    private static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidIndex();
        }
    }

    private static int RequireInt32(JsonElement element, string property)
    {
        if (!TryGetInt32(element, property, out var value))
        {
            throw InvalidIndex();
        }

        return value;
    }

    private static string RequireIdentifier(JsonElement element, string property)
    {
        var value = RequireString(element, property);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw InvalidIndex();
        }

        return value;
    }

    private static string RequireString(JsonElement element, string property)
    {
        if (!TryGetString(element, property, out var value))
        {
            throw InvalidIndex();
        }

        return value;
    }

    private static bool TryGetInt32(
        JsonElement element,
        string property,
        out int value)
    {
        value = default;
        return element.TryGetProperty(property, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetInt32(out value);
    }

    private static bool TryGetString(
        JsonElement element,
        string property,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = propertyElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static ModelCatalogException InvalidIndex() =>
        CatalogError(
            ModelCatalogErrorCode.IndexInvalid,
            "Katalog model AI rusak.");

    private static ModelCatalogException CatalogError(
        ModelCatalogErrorCode code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    private sealed record IndexEntry(
        string Id,
        string MetadataFile,
        string MetadataPath);
}
