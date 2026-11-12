using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Services;
using Nexus.Conversation.Index;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Storage;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.FileUtilities.Services;
using Nexus.Foundation.Config;
using Nexus.Foundation.Logging;
using Nexus.Foundation.State;
using Nexus.Foundation.Utils;
using Nexus.PdfUtilities;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class IntegrationSmokeTests
    {
        [Fact]
        public async Task AppStartup_InitializesAndStartsExistingLlamaServerManager()
        {
            var modelPath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.gguf");
            await File.WriteAllBytesAsync(modelPath, new byte[] { 0x47 });
            var manager = new Mock<ILlamaServerManager>();
            manager
                .Setup(service => service.StartServerAsync(modelPath, "auto", 8192))
                .Returns(Task.CompletedTask);

            try
            {
                var started = await InvokeStartupAsync(manager.Object, "engine", modelPath);

                Assert.True(started);
                manager.Verify(service => service.Initialize("engine", 8080), Times.Once);
                manager.Verify(service => service.StartServerAsync(modelPath, "auto", 8192), Times.Once);
                manager.Verify(service => service.StopServerAsync(), Times.Never);
            }
            finally
            {
                File.Delete(modelPath);
            }
        }

        [Fact]
        public async Task AppStartup_WhenModelFileDoesNotExist_DoesNotStartLlamaServer()
        {
            var missingModelPath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.gguf");
            var manager = new Mock<ILlamaServerManager>();

            var started = await InvokeStartupAsync(
                manager.Object,
                "engine",
                missingModelPath);

            Assert.False(started);
            manager.Verify(
                service => service.Initialize(It.IsAny<string>(), It.IsAny<int>()),
                Times.Never);
            manager.Verify(
                service => service.StartServerAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>()),
                Times.Never);
        }

        [Fact]
        public async Task AppStartup_WhenServerFails_CleansOwnedProcessAndReturnsFailure()
        {
            var modelPath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.gguf");
            await File.WriteAllBytesAsync(modelPath, new byte[] { 0x47 });
            var manager = new Mock<ILlamaServerManager>();
            manager
                .Setup(service => service.StartServerAsync(modelPath, "auto", 8192))
                .ThrowsAsync(new TimeoutException("server timeout"));
            manager.Setup(service => service.StopServerAsync()).Returns(Task.CompletedTask);

            try
            {
                var started = await InvokeStartupAsync(manager.Object, "engine", modelPath);

                Assert.False(started);
                manager.Verify(service => service.StopServerAsync(), Times.Once);
            }
            finally
            {
                File.Delete(modelPath);
            }
        }

        private static async Task<bool> InvokeStartupAsync(
            ILlamaServerManager manager,
            string engineDir,
            string modelPath)
        {
            var method = typeof(Nexus.App.App).GetMethod(
                "StartAiServerAsync",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = method.Invoke(null, new object[] { manager, engineDir, modelPath }) as Task<bool>;
            Assert.NotNull(task);
            return await task;
        }

        [Fact]
        public async Task CompositionRoot_ResolvesAllServicesAndRunsIntegrationFlow()
        {
            var configPath = Path.Combine(Path.GetTempPath(), $"nexus-smoke-{Guid.NewGuid():N}.json");
            try
            {
            // 1. Setup Composition Root with ServiceCollection
            var services = new ServiceCollection();

            // Foundation
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<IConfigService>(_ => new ConfigService(configPath));
            services.AddSingleton<ILoggerService, CrashLoggerService>();
            services.AddSingleton<IEngineStateService, EngineStateService>();

            // Conversation
            services.AddSingleton<IConversationStorageService, ConversationStorageService>();
            services.AddSingleton<IConversationIndexService, ConversationIndexService>();
            services.AddSingleton<IConversationManagerService, ConversationManagerService>();

            // AI Editing Lite
            services.AddSingleton<IFileSnapshotService, FileSnapshotService>();
            services.AddSingleton<IProposalService, ProposalService>();
            services.AddSingleton<IDiffGeneratorService, DiffGeneratorService>();
            services.AddSingleton<ISyntaxValidatorService, SyntaxValidatorService>();
            services.AddSingleton<IFileWriterService, FileWriterService>();
            services.AddSingleton<IEditingWorkflowService, EditingWorkflowService>();

            // File Utilities
            services.AddSingleton<IFileMetadataService, FileMetadataService>();
            services.AddSingleton<IFileManifestService, FileManifestService>();
            services.AddSingleton<ICleanDownloadsService, CleanDownloadsService>();
            services.AddSingleton<IRapikanFileService, RapikanFileService>();

            // Llama AI Provider
            services.AddSingleton<ILlamaServerManager, LlamaServerManager>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton<ILlamaClientService, LlamaClientService>();

            // Image Subsystem
            services.AddSingleton<IImagePipelineService, ImagePipelineService>();
            services.AddSingleton<IOnnxModelManager, OnnxModelManager>();
            services.AddSingleton<IRemoveBackgroundService, RemoveBackgroundService>();
            services.AddSingleton<IUpscaleService, UpscaleService>();

            // PDF Utilities
            services.AddSingleton<IPdfEngineService, PdfEngineService>();
            services.AddSingleton<IPdfConversionService, PdfConversionService>();
            services.AddSingleton<IPdfMergeSplitService, PdfMergeSplitService>();

            // Home & Settings
            services.AddSingleton<IHomeDashboardService, HomeDashboardService>();
            services.AddSingleton<ISettingsManagerService, SettingsManagerService>();

            // Build Provider
            using var provider = services.BuildServiceProvider();

            // 2. Integration Smoke Test Verifications
            var configService = provider.GetRequiredService<IConfigService>();
            var config = configService.GetConfig();
            Assert.NotNull(config);

            var homeService = provider.GetRequiredService<IHomeDashboardService>();
            await homeService.UpdateActivityAsync("FileUtilities", 5, 102400);
            var lastActivity = homeService.GetLastActivity();
            Assert.Equal("FileUtilities", lastActivity.Module);

            var settingsService = provider.GetRequiredService<ISettingsManagerService>();
            Assert.Equal("Dark", settingsService.GetTheme());

            var pdfEngine = provider.GetRequiredService<IPdfEngineService>();
            pdfEngine.Initialize();

            var llamaServer = provider.GetRequiredService<ILlamaServerManager>();
            var backend = llamaServer.DetectBackend();
            Assert.True(backend == "cuda" || backend == "cpu");

            using var imagePipeline = provider.GetRequiredService<IImagePipelineService>();
            var root = imagePipeline.CreatePipeline("C:\\TestPipeline");
            Assert.Equal("C:\\TestPipeline", root);
            }
            finally { File.Delete(configPath); }
        }
    }
}
