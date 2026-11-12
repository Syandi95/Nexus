using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Contracts;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.AI.Llama.Services.Extractors;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Controls.Snackbar;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Conversation.Index;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Storage;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.FileUtilities.Services;
using Nexus.Foundation.Config;
using Nexus.Foundation.Logging;
using Nexus.Foundation.Registry;
using Nexus.Foundation.State;
using Nexus.Foundation.Utils;
using Nexus.PdfUtilities;
using Nexus.Engine.ColorManagement;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Foundation.ReferenceData;
using Nexus.Engine.ImageSubsystem.Triage;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.App;

public partial class App : Application
{
    private IServiceProvider _serviceProvider;
    private ILoggerService _crashLogger;

    internal IServiceProvider Services => _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _crashLogger = _serviceProvider.GetRequiredService<ILoggerService>();

        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Foundation Services
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IPromptRegistry, PromptRegistry>();
        services.AddSingleton<ILoggerService, CrashLoggerService>();
        services.AddSingleton<IEngineStateService, EngineStateService>();
        services.AddSingleton<IApplicationVisualPreferences, ApplicationVisualPreferences>();
        services.AddSingleton<SnackbarService>();
        services.AddSingleton<ISnackbarService>(sp => sp.GetRequiredService<SnackbarService>());
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<SnackbarService>());
        services.AddSingleton<ContentDialogService>();
        services.AddSingleton<IContentDialogService>(sp => sp.GetRequiredService<ContentDialogService>());

        // Reference Data & AI Check Services
        services.AddSingleton<IReferenceDataManager, ReferenceDataManager>();
        services.AddSingleton<ThreeTierTriageFilter>();
        services.AddSingleton<IAiCheckJudgeService>(provider =>
            new AiCheckJudgeService(
                provider.GetRequiredService<ILlamaClientService>(),
                provider.GetRequiredService<IPromptRegistry>(),
                () =>
                {
                    var vm = provider.GetRequiredService<AiCheckViewModel>();
                    return new LlamaGenerationOptions
                    {
                        Temperature = vm.Temperature,
                        TopP = vm.TopP,
                        TopK = vm.TopK,
                        MinP = vm.MinP
                    };
                }));
        services.AddSingleton<PpOcrEngine>(provider =>
        {
            var settings = provider.GetRequiredService<ISettingsManagerService>();
            var modelDir = Path.Combine(settings.ModelRootFolder, "AI Workspace", "PP-OCRv6");
            return new PpOcrEngine(modelDir);
        });
        services.AddSingleton(provider => new AiCheckViewModel(
            provider.GetRequiredService<IReferenceDataManager>(),
            provider.GetRequiredService<IAiCheckJudgeService>(),
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<ISettingsManagerService>(),
            ocrEngineFactory: () => provider.GetRequiredService<PpOcrEngine>()));

        // Conversation Services
        services.AddSingleton<IConversationStorageService, ConversationStorageService>();
        services.AddSingleton<IConversationIndexService, ConversationIndexService>();
        services.AddSingleton<IConversationManagerService, ConversationManagerService>();

        // AI Editing Lite Services
        services.AddSingleton<IFileSnapshotService, FileSnapshotService>();
        services.AddSingleton<IProposalService, ProposalService>();
        services.AddSingleton<IDiffGeneratorService, DiffGeneratorService>();
        services.AddSingleton<ISyntaxValidatorService, SyntaxValidatorService>();
        services.AddSingleton<IFileWriterService, FileWriterService>();
        services.AddSingleton<EditingWorkflowService>();
        services.AddSingleton<IEditingWorkflowService>(sp=>sp.GetRequiredService<EditingWorkflowService>());
        services.AddSingleton(new ApprovedTextWorkspace());
        services.AddSingleton<IWriteApprovalProvider,DenyWriteApprovalProvider>();
        services.AddSingleton(new Nexus.App.Services.Agent.Tools.SourcePlanCatalog());

        // File Utilities Services
        services.AddSingleton<IFileMetadataService, FileMetadataService>();
        services.AddSingleton<IFileManifestService, FileManifestService>();
        services.AddSingleton<ICleanDownloadsService, CleanDownloadsService>();
        services.AddSingleton<IRapikanFileService, RapikanFileService>();

        // Llama AI Services
        services.AddSingleton<LlamaServerManager>(provider =>
            new LlamaServerManager(
                Path.Combine(
                    provider.GetRequiredService<ISettingsManagerService>().EngineRootFolder,
                    "LlamaServer")));
        services.AddSingleton<ILlamaServerManager>(provider => provider.GetRequiredService<LlamaServerManager>());
        services.AddSingleton<HttpClient>();
        services.AddSingleton<LlamaClientService>();
        services.AddSingleton<ILlamaClientService>(provider => provider.GetRequiredService<LlamaClientService>());
        services.AddSingleton<GenericLlamaEngine>();
        services.AddSingleton<IGenericLlamaEngine>(
            provider => provider.GetRequiredService<GenericLlamaEngine>());
        services.AddSingleton<IModelCatalogService>(provider =>
            new ModelCatalogService(
                Path.Combine(
                    provider.GetRequiredService<ISettingsManagerService>().ModelRootFolder,
                    "AI Workspace"),
                provider.GetRequiredService<ILlamaServerManager>()));

        // Image Subsystem Services
        services.AddSingleton<GenericImageEngine>();
        services.AddSingleton<IGenericImageEngine>(
            provider => provider.GetRequiredService<GenericImageEngine>());
        services.AddSingleton<ModelMetadataParser>();
        services.AddSingleton<ModelRegistry>();
        services.AddSingleton<IModelRegistry>(
            provider => provider.GetRequiredService<ModelRegistry>());
        services.AddSingleton(provider =>
        {
            var modelsRoot = provider.GetRequiredService<ISettingsManagerService>().ModelRootFolder;
            return new ImageApplicationModelSelectionPolicy(
                provider.GetRequiredService<IModelRegistry>(),
                provider.GetRequiredService<ModelMetadataParser>(),
                Path.Combine(modelsRoot, "RemoveBackground"),
                Path.Combine(
                    modelsRoot,
                    "Upscale",
                    "RealESR General WDN x4 v3",
                    "metadata.json"),
                inpaintModelsRoot: Path.Combine(modelsRoot, "Inpaint"));
        });
        services.AddSingleton<IModelSelectionPolicy>(
            provider => provider.GetRequiredService<ImageApplicationModelSelectionPolicy>());
        services.AddSingleton<LlamaConversationPipeline>();
        services.AddSingleton<ConversationRuntimeWorkflow>();
        services.AddSingleton<IConversationRuntimeWorkflow>(provider => provider.GetRequiredService<ConversationRuntimeWorkflow>());
        services.AddSingleton<Nexus.App.Services.Agent.INexusAgent, Nexus.App.Services.Agent.SharedRuntimeAgent>();
        // Gate C explicit allowlist: no reflection/service discovery and no writes.
        services.AddSingleton<Nexus.App.Services.Agent.Tools.GetModelSnapshotTool>();
        services.AddSingleton<Nexus.App.Services.Agent.Tools.GetRuntimeSnapshotTool>();
        services.AddSingleton<Nexus.App.Services.Agent.Tools.ApplyTextPatchTool>();
        services.AddSingleton<Nexus.App.Services.Agent.Tools.ApplySourcePlanTool>();
        services.AddSingleton(provider => new Nexus.App.Services.Agent.Tools.ToolRegistry(
            new Nexus.App.Services.Agent.Tools.INexusTool[] { provider.GetRequiredService<Nexus.App.Services.Agent.Tools.GetModelSnapshotTool>(), provider.GetRequiredService<Nexus.App.Services.Agent.Tools.GetRuntimeSnapshotTool>(), provider.GetRequiredService<Nexus.App.Services.Agent.Tools.ApplyTextPatchTool>(), provider.GetRequiredService<Nexus.App.Services.Agent.Tools.ApplySourcePlanTool>() }));
        services.AddSingleton(provider=>new Nexus.App.Services.Agent.Tools.ReadOnlyToolPolicy(
            new[] { Nexus.App.Services.Agent.Tools.GetModelSnapshotTool.ToolName, Nexus.App.Services.Agent.Tools.GetRuntimeSnapshotTool.ToolName },
            new[] { Nexus.App.Services.Agent.Tools.ApplyTextPatchTool.ToolName },provider.GetRequiredService<ApprovedTextWorkspace>(),provider.GetRequiredService<Nexus.App.Services.Agent.Tools.SourcePlanCatalog>()));
        services.AddSingleton<Nexus.App.Services.Agent.Tools.ToolExecutor>();
        services.AddSingleton<Nexus.App.Services.Agent.NativeToolModelAdapter>();
        services.AddSingleton<Nexus.App.Services.Agent.NativeToolCapability>();
        services.AddSingleton<Nexus.App.Services.Agent.AgentOrchestrator>();
        services.AddSingleton<IAgentWorkspaceWorkflow,AgentWorkspaceWorkflow>();
        services.AddSingleton<ReviewedProjectCatalog>();
        services.AddSingleton<AgentWorkspaceViewModel>();
        services.AddSingleton(provider => new RemoveBackgroundPipeline(
            provider.GetRequiredService<IModelSelectionPolicy>(),
            provider.GetRequiredService<IGenericImageEngine>(),
            provider.GetRequiredService<ImageApplicationModelSelectionPolicy>().ResolveBackgroundRemovalFallback));
        services.AddSingleton<UpscalePipeline>();
        services.AddSingleton<InpaintPipeline>();
        services.AddSingleton<IOnnxModelManager, OnnxModelManager>();
        services.AddSingleton<IImageSubsystemWorkflow, ImageSubsystemWorkflow>();
        services.AddSingleton<IMobileSamService>(provider =>
            new MobileSamService(provider.GetRequiredService<ISettingsManagerService>().ModelRootFolder));

        // Color Management Services
        services.AddTransient<IColorManagementService, WpfColorManagementService>();
        services.AddSingleton<ICmykProfileManager, CmykProfileManager>();
        services.AddSingleton<Func<IColorManagementService>>(
            sp => () => sp.GetRequiredService<IColorManagementService>());

        // PDF Utilities Services
        services.AddSingleton<IPdfEngineService, PdfEngineService>();
        services.AddSingleton<IPdfConversionService, PdfConversionService>();
        services.AddSingleton<IPdfOfficeToolService>(provider => new PdfOfficeToolService(() =>
            Path.Combine(provider.GetRequiredService<ISettingsManagerService>().EngineRootFolder,
                "Convert from PDF", "NexusConverterEngine.dll")));
        services.AddSingleton<IPdfMergeSplitService, PdfMergeSplitService>();

        // Home & Settings Services
        services.AddSingleton<IHomeDashboardService, HomeDashboardService>();
        services.AddSingleton<ISettingsManagerService, SettingsManagerService>();

        // Attachment Management & Context Budget Services (Fase 1)
        services.AddSingleton<ITextContentExtractor, TextContentExtractor>();
        services.AddSingleton<IPdfContentExtractor>(sp => new PdfContentExtractor(sp.GetRequiredService<IPdfEngineService>()));
        services.AddSingleton<IAttachmentManagerService, AttachmentManagerService>();
        services.AddSingleton<AttachmentContextBudgetService>();
        services.AddSingleton<IProjectReadContextWorkflow,ProjectReadContextWorkflow>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton(provider => new ConversationViewModel(
            provider.GetRequiredService<IConversationManagerService>(),
            provider.GetRequiredService<IConversationRuntimeWorkflow>(),
            provider.GetRequiredService<IConversationIndexService>(),
            provider.GetRequiredService<IModelCatalogService>(),
            provider.GetRequiredService<IAttachmentManagerService>(),
            provider.GetRequiredService<AttachmentContextBudgetService>(),
            provider.GetRequiredService<AiCheckViewModel>(),
            provider.GetRequiredService<IPromptRegistry>(),
            provider.GetRequiredService<AgentWorkspaceViewModel>(),
            provider.GetRequiredService<IProjectReadContextWorkflow>()));
        services.AddSingleton<FileManagementViewModel>();
        services.AddSingleton<CleanDownloadsViewModel>();
        services.AddSingleton<RapikanFileViewModel>();
        services.AddSingleton<PdfWorkspaceViewModel>();
        services.AddSingleton<ImageSubsystemViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Lazy factories for deferred ViewModel creation
        services.AddSingleton<Func<ImageSubsystemViewModel>>(
            sp => () => sp.GetRequiredService<ImageSubsystemViewModel>());
        services.AddSingleton(
            sp => new Lazy<ImageSubsystemViewModel>(sp.GetRequiredService<Func<ImageSubsystemViewModel>>()));

        // MainWindow
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterTypographyCompatibilityResources();

        var serverLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_lifecycle.txt");
        try { File.WriteAllText(serverLogPath, $"=== Server Lifecycle Log ===\nStarted at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n\n"); } catch { }

        var serverManager = _serviceProvider.GetRequiredService<LlamaServerManager>();
        serverManager.OnEvent += evt =>
        {
            try
            {
                var line = $"[{evt.Timestamp:HH:mm:ss.fff}] [{evt.ServerInstanceId}] [{evt.EventType,-22}] +{evt.ElapsedMs,5}ms {evt.Message} | Thread={evt.ThreadId}" +
                           (evt.Pid.HasValue ? $" PID={evt.Pid}" : "") +
                           (evt.State.HasValue ? $" State={evt.State}" : "");
                System.Diagnostics.Debug.WriteLine(line);
                File.AppendAllText(serverLogPath, line + Environment.NewLine);
            }
            catch { }
        };

        ModelCatalogSnapshot? catalogSnapshot = null;
        string? catalogError = null;
        try
        {
            catalogSnapshot = _serviceProvider
                .GetRequiredService<IModelCatalogService>()
                .Load();
        }
        catch (ModelCatalogException error)
        {
            catalogError = error.UserMessage;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        var conversation = _serviceProvider.GetRequiredService<ConversationViewModel>();
        if (catalogError is not null)
        {
            conversation.ApplicationStatus = catalogError;
            return;
        }

        var activeModel = catalogSnapshot?.ActiveModel;
        if (activeModel is null || !activeModel.IsAvailable)
        {
            conversation.ApplicationStatus = activeModel?.StatusMessage
                ?? "Model AI tidak tersedia.";
        }
    }

    private void RegisterTypographyCompatibilityResources()
    {
        var compatibilityKeys = new (string Compatibility, string Canonical)[]
        {
            ("NexusFontDisplay", "NexusTypographyDisplaySize"),
            ("NexusFontHeader", "NexusTypographyTitleSize"),
            ("NexusFontTitle", "NexusTypographySubtitleSize"),
            ("NexusFontBody", "NexusTypographyBodySize"),
            ("NexusFontLabel", "NexusTypographyCaptionSize"),
            ("NexusFontCaption", "NexusTypographyBodySmallSize")
        };

        foreach (var (compatibility, canonical) in compatibilityKeys)
        {
            var value = TryFindResource(canonical);
            if (value is not double)
            {
                throw new InvalidOperationException(
                    $"Typography resource '{canonical}' must resolve to Double.");
            }

            Resources[compatibility] = value;
        }
    }


    internal static async Task<bool> StartAiServerAsync(
        ILlamaServerManager serverManager,
        string engineDir,
        string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            return false;
        }

        try
        {
            serverManager.Initialize(engineDir, 8080);
            await serverManager.StartServerAsync(modelPath, "auto", 8192);
            return true;
        }
        catch
        {
            await serverManager.StopServerAsync();
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _serviceProvider
                .GetRequiredService<IConversationRuntimeWorkflow>()
                .StopAsync()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable
                    .DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            else if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }

    private static bool _isDisplayingCrashDialog = false;

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _crashLogger.LogError("AppCrash", $"Unhandled exception: {e.Exception}");
        if (!_isDisplayingCrashDialog)
        {
            _isDisplayingCrashDialog = true;
            MessageBox.Show($"An unexpected error occurred:\n{e.Exception.Message}\n\nThe details have been logged to diagnostic_crash.log.", "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isDisplayingCrashDialog = false;
        }
        e.Handled = true;
    }
}
