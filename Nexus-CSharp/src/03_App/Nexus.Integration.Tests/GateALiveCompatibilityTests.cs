using System.Diagnostics;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts.ReferenceData;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Storage;
using Nexus.Conversation.Index;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Foundation.Config;
using Nexus.Foundation.ReferenceData;
using Nexus.PdfUtilities;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

// Explicit native fixtures, genuine HTTP/ONNX/PDF/WPF; never a fake pass when
// assets are absent. Run in a fresh process with the environment in Gate A report.
public sealed class GateALiveFactAttribute : FactAttribute
{
    public GateALiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXUS_GATE_A_LIVE") != "1")
            Skip = "Explicit live model/design/PDF fixtures required; see GATE_A_REPORT.md.";
    }
}

public sealed class GateALiveCompatibilityTests(ITestOutputHelper output)
{
    [GateALiveFact]
    public async Task LiveChatHistoryAiCheckAndModelLifecycle_UseOneOwnedRuntime()
    {
        Assert.Empty(AliveLlamaPids());
        await using var fixture = new LiveFixture("runtime");
        var records = new ConcurrentBag<object>();
        var server = fixture.Provider.GetRequiredService<LlamaServerManager>();
        server.OnEvent += e => records.Add(new { e.Timestamp, e.EventType, e.Pid, e.State, e.ElapsedMs });
        await WpfAppHost.RunAsync(async () =>
        {
            var provider = fixture.Provider;
            var shell = provider.GetRequiredService<Nexus.App.MainWindow>();
            var main = provider.GetRequiredService<MainViewModel>();
            var chat = provider.GetRequiredService<ConversationViewModel>();
            var runtime = provider.GetRequiredService<IConversationRuntimeWorkflow>();
            var catalog = provider.GetRequiredService<IModelCatalogService>();
            var manager = provider.GetRequiredService<IConversationManagerService>();
            var storage = provider.GetRequiredService<IConversationStorageService>();
            var original = (catalog.CurrentSnapshot ?? catalog.Load()).ActiveModel!;
            Assert.True(original.IsAvailable);
            shell.Show();
            try
            {
                main.CurrentViewModel = chat;
                await chat.OnNavigatedToAsync();
                Assert.True(runtime.IsReady, chat.ApplicationStatus);
                Assert.Same(provider.GetRequiredService<LlamaServerManager>(), server);
                var pid = RequireOnePid(server);
                await runtime.StartActiveModelAsync();
                Assert.Equal(pid, RequireOnePid(server));
                records.Add(new { stage = "same-model-reuse", pid, server.RuntimeModelPath });

                chat.MaxTokens = 64;
                chat.IsDeepThinkEnabled = false;
                chat.InputText = "Jawab singkat satu kalimat: apa fungsi tombol batal?";
                var sawStream = false;
                var watch = Stopwatch.StartNew();
                await chat.ExecuteSendAsync();
                await Until(() =>
                {
                    sawStream |= chat.IsSending && chat.Messages.Any(m => m.IsAssistant && m.Content.Length > 0);
                    return !chat.IsSending;
                }, 90);
                Assert.Equal(2, chat.Messages.Count);
                Assert.NotEmpty(chat.Messages.Last().Content);
                Assert.NotNull(chat.Messages.Last().RenderedDocument);
                Assert.True(sawStream);
                shell.UpdateLayout();
                var id = manager.ActiveConversation!.Id;
                var detail = await storage.LoadAsync(id);
                Assert.Equal(2, detail!.Messages.Count);
                Assert.Equal(chat.Messages.Last().Content, detail.Messages.Last().Content);
                records.Add(new { stage = "send-stream-render-save", id, elapsedMs = watch.ElapsedMilliseconds, sawStream });

                await chat.ExecuteNewConversationAsync();
                await chat.OpenConversationAsync(id);
                Assert.Equal(2, chat.Messages.Count);
                chat.RegenerateMessageCommand.Execute(chat.Messages.Last());
                await Until(() => !chat.IsSending, 90);
                Assert.Equal(2, (await storage.LoadAsync(id))!.Messages.Count);
                chat.EditMessageCommand.Execute(chat.Messages.First());
                chat.InputText = "Jawab satu kalimat pendek tentang tombol simpan.";
                await chat.ExecuteSendAsync();
                await Until(() => !chat.IsSending, 90);
                Assert.Equal(2, (await storage.LoadAsync(id))!.Messages.Count);
                Assert.Contains("tombol simpan", (await storage.LoadAsync(id))!.Messages[0].Content);
                records.Add(new { stage = "reopen-regenerate-edit", messages = manager.ActiveConversation!.Messages.Count });

                chat.MaxTokens = 512;
                chat.InputText = "Tuliskan angka satu sampai tiga ratus, satu angka per baris, tanpa penjelasan.";
                await chat.ExecuteSendAsync();
                await Until(() => chat.Messages.Any(m => m.IsAssistant && m.Content.Length > 0 && chat.IsSending), 60);
                chat.CancelGenerationCommand.Execute(null);
                await Until(() => !chat.IsSending, 20);
                Assert.Contains("dibatalkan", chat.ApplicationStatus, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, (await storage.LoadAsync(id))!.Messages.Count);
                Assert.Equal(pid, RequireOnePid(server));
                records.Add(new { stage = "cancel-midstream", pid });
                chat.MaxTokens = 64;

                await chat.ToggleTemporaryChatAsync();
                var temporaryId = manager.ActiveConversation!.Id;
                chat.InputText = "Jawab singkat: halo.";
                await chat.ExecuteSendAsync();
                await Until(() => !chat.IsSending, 90);
                Assert.False(File.Exists(storage.GetJsonPath(temporaryId)));
                await chat.ToggleTemporaryChatAsync();
                await chat.OpenConversationAsync(id);
                var item = chat.ConversationList.First(i => i.Id == id);
                await chat.DeleteConversationAsync(item);
                Assert.False(File.Exists(storage.GetJsonPath(id)));
                Assert.DoesNotContain(await provider.GetRequiredService<IConversationIndexService>().GetIndexAsync(), i => i.Id == id);
                foreach (var indexItem in await provider.GetRequiredService<IConversationIndexService>().GetIndexAsync())
                    Assert.NotNull(await storage.LoadAsync(indexItem.Id));
                records.Add(new { stage = "temporary-delete-index-integrity", temporaryId });

                chat.IsAiCheckModeActive = true;
                var check = chat.AiCheck!;
                await check.LoadFileDirectlyAsync(Required("NEXUS_TEST_DESIGN_PATH"));
                Assert.True(check.CanRunCheck);
                var callsBefore = fixture.Http.Calls;
                await InvokeTask(check, "ExecuteRunCheckAsync").WaitAsync(TimeSpan.FromMinutes(4));
                Assert.True(check.IsInspectionCompleted, check.SummaryText);
                Assert.True(check.OverlayBoxes.Count > 0);
                Assert.True(fixture.Http.Calls > callsBefore, "Representative design must reach the actual AI Judge, not early-exit.");
                Assert.Equal(pid, RequireOnePid(server));
                Assert.All(check.Findings, finding => Assert.Contains(check.OverlayBoxes, b => b.Id == finding.BoxId));
                records.Add(new { stage = "uploaded-ocr-evidence-judge-mapping", pid,
                    boxes = check.OverlayBoxes.Count, findings = check.Findings.Count, judgeCalls = fixture.Http.Calls - callsBefore });
                var cancelledCheck = InvokeTask(check, "ExecuteRunCheckAsync");
                check.CancelActiveCheck();
                await cancelledCheck.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.False(check.IsAnalyzing);
                Assert.False(check.IsInspectionCompleted);
                Assert.Empty(check.Findings);
                chat.IsChatModeActive = true;
                Assert.Equal(pid, RequireOnePid(server));

                var other = catalog.CurrentSnapshot!.Models.First(m => m.IsAvailable && m.Id != original.Id);
                await InvokeTask(chat, "SwitchModelAsync", other);
                var otherPid = RequireOnePid(server);
                Assert.NotEqual(pid, otherPid);
                Assert.Equal(Path.GetFullPath(other.ResolvedGgufPath!), server.RuntimeModelPath);
                await InvokeTask(chat, "SwitchModelAsync", original);
                records.Add(new { stage = "model-switch-and-restore", otherPid, original = original.Id, restoredPid = RequireOnePid(server) });

                await chat.OnNavigatedFromAsync();
                main.CurrentViewModel = main.HomeVM;
                Assert.Null(server.OwnedPid);
                Assert.Empty(AliveLlamaPids());
                main.CurrentViewModel = chat;
                await chat.OnNavigatedToAsync();
                Assert.True(runtime.IsReady);
                records.Add(new { stage = "navigation-back", pid = RequireOnePid(server) });
                await chat.OnNavigatedFromAsync();
                main.CurrentViewModel = main.HomeVM;
                Assert.Null(server.OwnedPid);
                Assert.Empty(AliveLlamaPids());

                var listener = new TcpListener(IPAddress.Loopback, 8080);
                listener.Start();
                try
                {
                    var error = await Record.ExceptionAsync(() => server.StartServerAsync(original.ResolvedGgufPath!, "auto", 8192));
                    Assert.NotNull(error);
                    Assert.True(listener.Server.IsBound);
                    Assert.Null(server.OwnedPid);
                    Assert.Empty(AliveLlamaPids());
                    records.Add(new { stage = "foreign-port-preserved", failure = error.GetType().Name });
                }
                finally { listener.Stop(); }
            }
            finally
            {
                await server.StopServerAsync();
                if (catalog.CurrentSnapshot?.ActiveModel?.Id != original.Id && server.State == ServerState.Stopped)
                {
                    await runtime.SwitchModelAsync(original);
                    await runtime.StopAsync();
                }
                shell.DataContext = null;
                shell.Close();
                fixture.Save("runtime.json", records);
            }
        });
        Assert.Empty(AliveLlamaPids());
        output.WriteLine("LIVE_RUNTIME " + JsonSerializer.Serialize(records));
    }

    [GateALiveFact]
    public async Task LiveNativeImagePdfThemeFocusAndDpi_UseExistingServices()
    {
        await using var fixture = new LiveFixture("artifacts");
        var records = new List<object>();
        var source = Required("NEXUS_TEST_DESIGN_PATH");
        var sourceHash = Hash(source);
        var input = Path.Combine(fixture.Root, "input.png");
        using (var original = new Bitmap(source))
        using (var small = new Bitmap(original, new System.Drawing.Size(128, 128)))
            small.Save(input, ImageFormat.Png);
        var workflow = fixture.Provider.GetRequiredService<IImageSubsystemWorkflow>();
        var policy = fixture.Provider.GetRequiredService<ImageApplicationModelSelectionPolicy>();
        var remove = Path.Combine(fixture.Root, "removed.png");
        await Task.Run(() => workflow.RemoveBackgroundAsync(input, "PhotosSeg", remove, CancellationToken.None));
        VerifyImage(remove, 128, 128, records);
        var upscale = Path.Combine(fixture.Root, "upscaled.png");
        await Task.Run(() => workflow.UpscaleAsync(input, 2, upscale, CancellationToken.None));
        VerifyImage(upscale, 256, 256, records);
        policy.UseInpaintModel("LaMa");
        using (var mask = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
        {
            using var g = Graphics.FromImage(mask);
            g.Clear(System.Drawing.Color.Black);
            g.FillRectangle(System.Drawing.Brushes.White, 48, 48, 16, 16);
            var inpaint = Path.Combine(fixture.Root, "inpaint.png");
            await workflow.InpaintAsync(input, mask, InpaintExecutionTier.FastSinglePass, inpaint, CancellationToken.None);
            VerifyImage(inpaint, 128, 128, records);
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var forbiddenOutput = Path.Combine(fixture.Root, "cancelled.png");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.RemoveBackgroundAsync(input, "PhotosSeg", forbiddenOutput, cancelled.Token));
            Assert.False(File.Exists(forbiddenOutput));
        }
        Assert.Equal(sourceHash, Hash(source));

        var pdfSource = Required("NEXUS_TEST_PDF_PATH");
        var pdfHash = Hash(pdfSource);
        var engine = fixture.Provider.GetRequiredService<IPdfEngineService>();
        var pageCount = engine.GetPageCount(pdfSource);
        Assert.True(pageCount > 0);
        Assert.NotNull(engine.GetPageImage(pdfSource, 300));
        Assert.NotEmpty(await engine.ExtractTextAsync(pdfSource));
        var converted = Path.Combine(fixture.Root, "converted");
        await fixture.Provider.GetRequiredService<IPdfConversionService>().ConvertAsync(pdfSource, converted, "png", 72, 90, CancellationToken.None);
        var images = Directory.GetFiles(converted, "*.png");
        Assert.Equal(pageCount, images.Length);
        foreach (var path in images) using (var image = new Bitmap(path)) Assert.True(image.Width > 0 && image.Height > 0);
        var merged = Path.Combine(fixture.Root, "merged.pdf");
        var merge = fixture.Provider.GetRequiredService<IPdfMergeSplitService>();
        await merge.MergePdfAsync([pdfSource, pdfSource], merged, CancellationToken.None);
        Assert.Equal(pageCount * 2, engine.GetPageCount(merged));
        var split = Path.Combine(fixture.Root, "split");
        await merge.SplitPdfAsync(merged, split, CancellationToken.None);
        var splitFiles = Directory.GetFiles(split, "*.pdf");
        Assert.Equal(pageCount * 2, splitFiles.Length);
        Assert.All(splitFiles, file => Assert.Equal(1, engine.GetPageCount(file)));
        Assert.Equal(pdfHash, Hash(pdfSource));
        records.Add(new { stage = "pdf-render-convert-merge-split", pageCount, converted = images.Length, split = splitFiles.Length, sourceHash = pdfHash });

        await WpfAppHost.RunAsync(() =>
        {
            var shell = fixture.Provider.GetRequiredService<Nexus.App.MainWindow>();
            var main = fixture.Provider.GetRequiredService<MainViewModel>();
            shell.Show();
            try
            {
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.HighContrast })
                {
                    ThemeManager.ApplyTheme(mode);
                    Assert.Equal(mode, ThemeManager.CurrentTheme);
                    foreach (var key in new[] { "NexusContentBorder", "NexusModalBackdrop", "NexusComparisonDivider", "NexusComparisonHandle", "NexusComparisonGlyph", "NexusRoundActionHover", "NexusRoundActionPressed" })
                        Assert.IsType<SolidColorBrush>(Application.Current.FindResource(key));
                    foreach (var vm in new ViewModelBase[] { main.HomeVM, main.ConversationVM, main.PdfUtilitiesVM, main.ImageSubsystemVM, main.SettingsVM })
                    {
                        main.CurrentViewModel = vm;
                        shell.UpdateLayout();
                    }
                    main.CurrentViewModel = main.ConversationVM;
                    shell.UpdateLayout();
                    var focus = (Button)shell.FindName("HomeNavButton");
                    Keyboard.Focus(focus);
                    Assert.True(focus.IsKeyboardFocused);
                    focus.IsEnabled = false;
                    Assert.False(focus.IsEnabled);
                    focus.IsEnabled = true;
                    foreach (var dpi in new[] { 96d, 144d })
                    {
                        var bitmap = new RenderTargetBitmap((int)(shell.ActualWidth * dpi / 96), (int)(shell.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                        bitmap.Render(shell);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(fixture.Root, $"theme-{mode}-{dpi}.png"));
                        encoder.Save(file);
                        records.Add(new { stage = "theme-navigation-focus-dpi", mode = mode.ToString(), dpi, bitmap.PixelWidth, bitmap.PixelHeight });
                    }
                }
            }
            finally { ThemeManager.ApplyTheme(ThemeMode.Dark); shell.DataContext = null; shell.Close(); }
            return Task.CompletedTask;
        });
        fixture.Save("artifacts.json", records);
        output.WriteLine("LIVE_ARTIFACTS " + JsonSerializer.Serialize(records));
    }

    private static int RequireOnePid(ILlamaServerManager server)
    {
        var pid = Assert.Single(AliveLlamaPids());
        Assert.Equal(server.OwnedPid, pid);
        return pid;
    }

    private static int[] AliveLlamaPids()
    {
        var ids = new List<int>();
        foreach (var process in Process.GetProcessesByName("llama-server"))
        {
            using (process)
            {
                try { if (!process.HasExited) ids.Add(process.Id); }
                catch (InvalidOperationException) { /* exited during enumeration */ }
            }
        }
        return ids.ToArray();
    }

    private static async Task Until(Func<bool> condition, int seconds)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timer.Elapsed.TotalSeconds < seconds, $"Live operation exceeded {seconds}s.");
            await Task.Delay(20);
        }
    }

    private static Task InvokeTask(object target, string method, params object[] args) =>
        (Task)target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Explicit fixture {name} is required.");
        return value!;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void VerifyImage(string path, int width, int height, List<object> records)
    {
        Assert.True(File.Exists(path));
        using var bitmap = new Bitmap(path);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        Assert.Equal(ImageFormat.Png.Guid, bitmap.RawFormat.Guid);
        records.Add(new { stage = "native-image-save", file = Path.GetFileName(path), width, height, sha256 = Hash(path), bytes = new FileInfo(path).Length });
    }

    private sealed class LiveFixture : IAsyncDisposable
    {
        public string Root { get; }
        public ServiceProvider Provider { get; }
        public CountingHttpHandler Http { get; } = new();
        public LiveFixture(string area)
        {
            var evidence = Required("NEXUS_GATE_A_EVIDENCE");
            Assert.True(Directory.Exists(evidence));
            Root = Path.Combine(evidence, area + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(Root);
            var services = ArchitectureGovernanceTests.CreateProductionServices();
            var config = new ConfigService(Path.Combine(Root, "config.json"));
            config.SaveConfig(new AppConfigRecord { ModelRootFolder = Required("NEXUS_TEST_MODEL_ROOT"), EngineRootFolder = Required("NEXUS_TEST_ENGINE_ROOT"), ConfirmExit = false, ConfirmModule = false });
            services.RemoveAll<IConfigService>();
            services.AddSingleton<IConfigService>(config);
            services.RemoveAll<IReferenceDataManager>();
            services.AddSingleton<IReferenceDataManager>(new ReferenceDataManager(Path.Combine(Root, "references")));
            var storage = new ConversationStorageService();
            // The test fixture relocates the existing store; no production schema,
            // database, storage implementation or user history is changed.
            typeof(ConversationStorageService).GetField("_rootPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(storage, Path.Combine(Root, "history"));
            services.RemoveAll<IConversationStorageService>();
            services.AddSingleton<IConversationStorageService>(storage);
            services.RemoveAll<HttpClient>();
            services.AddSingleton(new HttpClient(Http));
            Provider = services.BuildServiceProvider();
        }
        public void Save(string name, object value) => File.WriteAllText(Path.Combine(Root, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class CountingHttpHandler : DelegatingHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public CountingHttpHandler() : base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal(8080, request.RequestUri.Port);
            Interlocked.Increment(ref _calls);
            return base.SendAsync(request, token);
        }
    }
}
