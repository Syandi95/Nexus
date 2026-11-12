using System.IO;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.App.Services;

public sealed class ImageApplicationModelSelectionPolicy : IModelSelectionPolicy
{
    private readonly object _syncRoot = new();
    private readonly IModelRegistry _registry;
    private readonly DefaultModelSelectionPolicy _defaultPolicy;
    private readonly ModelMetadataParser _metadataParser;
    private readonly string _removeBackgroundModelsRoot;
    private readonly string _upscaleMetadataPath;
    private readonly string? _inpaintMetadataPath;
    private readonly string _inpaintModelsRoot;
    private bool _upscaleRegistered;
    private bool _inpaintRegistered;
    private string? _activeBackgroundRemovalDirectory;

    public ImageApplicationModelSelectionPolicy(
        IModelRegistry registry,
        ModelMetadataParser metadataParser,
        string removeBackgroundModelsRoot,
        string upscaleMetadataPath,
        string? inpaintMetadataPath = null,
        string? inpaintModelsRoot = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _defaultPolicy = new DefaultModelSelectionPolicy(registry);
        _metadataParser = metadataParser ?? throw new ArgumentNullException(nameof(metadataParser));
        _removeBackgroundModelsRoot = Path.GetFullPath(
            removeBackgroundModelsRoot ?? throw new ArgumentNullException(nameof(removeBackgroundModelsRoot)));
        _upscaleMetadataPath = Path.GetFullPath(
            upscaleMetadataPath ?? throw new ArgumentNullException(nameof(upscaleMetadataPath)));
        _inpaintMetadataPath = inpaintMetadataPath != null ? Path.GetFullPath(inpaintMetadataPath) : null;
        _inpaintModelsRoot = inpaintModelsRoot != null
            ? Path.GetFullPath(inpaintModelsRoot)
            : Path.GetFullPath(Path.Combine(_removeBackgroundModelsRoot, "..", "Inpaint"));
    }

    public void UseBackgroundRemovalModel(string modelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);

        var modelDirectory = Path.IsPathRooted(modelReference)
            ? Path.GetFullPath(modelReference)
            : Path.GetFullPath(Path.Combine(_removeBackgroundModelsRoot, modelReference));
        var metadataPath = Path.Combine(modelDirectory, "metadata.json");
        var descriptor = _metadataParser.ParseOnnxMetadata(metadataPath);
        EnsureModelFileExists(descriptor);

        lock (_syncRoot)
        {
            _registry.RegisterModel(AICapability.BackgroundRemovalGeneral, descriptor);
            _registry.SetDefaultModel(
                AICapability.BackgroundRemovalGeneral,
                descriptor.ModelId);
            _activeBackgroundRemovalDirectory = modelDirectory;
        }
    }

    public void UseInpaintModel(string modelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);

        var modelDirectory = Path.IsPathRooted(modelReference)
            ? Path.GetFullPath(modelReference)
            : Path.GetFullPath(Path.Combine(_inpaintModelsRoot, modelReference));
        var metadataPath = Path.Combine(modelDirectory, "metadata.json");
        var descriptor = _metadataParser.ParseOnnxMetadata(metadataPath);
        EnsureModelFileExists(descriptor);

        lock (_syncRoot)
        {
            _registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
            _registry.SetDefaultModel(
                AICapability.InpaintingGeneral,
                descriptor.ModelId);
            _inpaintRegistered = true;
        }
    }

    // Filesystem discovery belongs to the application model selection boundary,
    // not the headless image pipeline. Preserve the PhotosSeg -> MVANet fallback
    // using the configured model root rather than a developer-machine path.
    public ResolvedModelDescriptor? ResolveBackgroundRemovalFallback()
    {
        try
        {
            string? siblingRoot;
            lock (_syncRoot)
                siblingRoot = _activeBackgroundRemovalDirectory is null
                    ? null : Path.GetDirectoryName(_activeBackgroundRemovalDirectory);
            foreach (var root in new[] { siblingRoot, _removeBackgroundModelsRoot }.Distinct())
            {
                if (root is null) continue;
                var metadataPath = Path.Combine(root, "MVANet", "metadata.json");
                if (!File.Exists(metadataPath)) continue;
                var descriptor = _metadataParser.ParseOnnxMetadata(metadataPath);
                EnsureModelFileExists(descriptor);
                return descriptor;
            }
            return null;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine($"[WARN] Failed to resolve MVANet fallback: {error.Message}");
            return null;
        }
    }

    public ResolvedModelDescriptor SelectModel(
        AICapability capability,
        EngineSessionOptions sessionOptions)
    {
        if (capability == AICapability.UpscaleGeneral)
        {
            EnsureUpscaleRegistered();
        }
        else if (capability == AICapability.InpaintingGeneral)
        {
            EnsureInpaintRegistered();
        }

        return _defaultPolicy.SelectModel(capability, sessionOptions);
    }

    public void UseChatModel(
        string modelId,
        string modelPath,
        string? displayName)
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = modelId,
            ModelPath = Path.GetFullPath(modelPath),
            ModelType = ModelType.Gguf,
            DisplayName = displayName
        };
        EnsureModelFileExists(descriptor);

        lock (_syncRoot)
        {
            _registry.RegisterModel(AICapability.ChatCompletion, descriptor);
            _registry.SetDefaultModel(AICapability.ChatCompletion, descriptor.ModelId);
        }
    }

    private void EnsureUpscaleRegistered()
    {
        lock (_syncRoot)
        {
            if (_upscaleRegistered)
            {
                return;
            }

            var descriptor = _metadataParser.ParseOnnxMetadata(_upscaleMetadataPath);
            EnsureModelFileExists(descriptor);
            _registry.RegisterModel(AICapability.UpscaleGeneral, descriptor);
            _registry.SetDefaultModel(AICapability.UpscaleGeneral, descriptor.ModelId);
            _upscaleRegistered = true;
        }
    }

    private void EnsureInpaintRegistered()
    {
        lock (_syncRoot)
        {
            if (_inpaintRegistered)
            {
                return;
            }

            // Check slots_mapping.json in _inpaintModelsRoot first (Single Source of Truth)
            var slotsPath = Path.Combine(_inpaintModelsRoot, "slots_mapping.json");
            string modelToLoad = "LaMa";
            if (File.Exists(slotsPath))
            {
                try
                {
                    var json = File.ReadAllText(slotsPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Selected", out var selElem) &&
                        !string.IsNullOrWhiteSpace(selElem.GetString()))
                    {
                        modelToLoad = selElem.GetString()!;
                    }
                }
                catch
                {
                    modelToLoad = "LaMa";
                }
            }

            try
            {
                UseInpaintModel(modelToLoad);
            }
            catch
            {
                // Fallback strictly to LaMa if the requested model fails to load
                if (!string.Equals(modelToLoad, "LaMa", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        UseInpaintModel("LaMa");
                        File.WriteAllText(
                            slotsPath,
                            System.Text.Json.JsonSerializer.Serialize(
                                new Dictionary<string, string> { ["Selected"] = "LaMa" },
                                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch { }
                }
                else
                {
                    var path = _inpaintMetadataPath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try
                        {
                            var descriptor = _metadataParser.ParseOnnxMetadata(path);
                            EnsureModelFileExists(descriptor);
                            _registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
                            _registry.SetDefaultModel(AICapability.InpaintingGeneral, descriptor.ModelId);
                            _inpaintRegistered = true;
                        }
                        catch { }
                    }
                }
            }
        }
    }

    private static void EnsureModelFileExists(ResolvedModelDescriptor descriptor)
    {
        if (!File.Exists(descriptor.ModelPath))
        {
            throw new FileNotFoundException(
                $"Model file declared by metadata was not found: '{descriptor.ModelPath}'.",
                descriptor.ModelPath);
        }
    }
}
