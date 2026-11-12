using System.Reflection;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Conversation.Manager;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class AiCheckRunIsolationTests
{
    [Theory]
    [InlineData("user", "empty")]
    [InlineData("entity", "empty")]
    [InlineData("domain", "empty")]
    [InlineData("structured", "empty")]
    [InlineData("user", "delete")]
    [InlineData("entity", "delete")]
    [InlineData("domain", "delete")]
    [InlineData("structured", "delete")]
    [InlineData("user", "change")]
    [InlineData("entity", "change")]
    [InlineData("domain", "change")]
    [InlineData("structured", "change")]
    public Task NextRun_UsesCurrentReferences(string kind, string mutation) => OnUiAsync(async () =>
    {
        var references = new Dictionary<string, ReferenceDataset>();
        string id = kind switch
        {
            "user" => ReferenceConstants.UserDictionaryId,
            "entity" => ReferenceConstants.EntityReferenceId,
            "domain" => ReferenceConstants.DomainFoodRetailId,
            _ => "places"
        };
        var dataset = new ReferenceDataset { Id = id, Entries = [new("menyediakn")] };
        references[id] = dataset;
        var vm = CreateViewModel(references);
        vm.Read = _ => Task.FromResult(Boxes("menyediakn pelaynan"));

        await RunAsync(vm);
        Assert.DoesNotContain(vm.Findings, f => f.OriginalText == "menyediakn");
        Assert.Contains(vm.Findings, f => f.OriginalText == "pelaynan");

        if (mutation == "delete") references.Remove(id);
        else dataset.Entries = mutation == "empty" ? [] : [new("pelaynan")];

        await RunAsync(vm);
        Assert.Contains(vm.Findings, f => f.OriginalText == "menyediakn");
        Assert.Equal(mutation != "change", vm.Findings.Any(f => f.OriginalText == "pelaynan"));
        Assert.True(vm.IsInspectionCompleted);
    });

    [Theory]
    [InlineData("reset")]
    [InlineData("navigation")]
    [InlineData("chat")]
    public Task Invalidation_DuringOcr_IgnoresOldResults(string action) => OnUiAsync(async () =>
    {
        var vm = CreateViewModel();
        var workspace = Workspace(vm);
        workspace.IsChatModeActive = false;
        var pending = new TaskCompletionSource<List<OcrBoxModel>>();
        vm.Read = _ => pending.Task;
        var run = RunAsync(vm);
        Assert.True(vm.IsAnalyzing);

        await InvalidateAsync(vm, workspace, action);
        bool wasAnalyzingAfterInvalidation = vm.IsAnalyzing;
        pending.SetResult(Boxes("menyediakn"));
        await run;

        Assert.False(wasAnalyzingAfterInvalidation);
        Assert.Empty(vm.Findings);
        Assert.Empty(vm.OverlayBoxes);
        Assert.False(vm.IsInspectionCompleted);
        Assert.Empty(vm.AnalysisStageText);
    });

    [Theory]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("navigation", false)]
    [InlineData("navigation", true)]
    [InlineData("chat", false)]
    [InlineData("chat", true)]
    public Task InvalidationDuringJudge_OldCompletionCannotOverwriteLaterRun(string action, bool failOldRun) => OnUiAsync(async () =>
    {
        var first = new TaskCompletionSource<IReadOnlyList<AiCheckJudgment>>();
        var second = new TaskCompletionSource<IReadOnlyList<AiCheckJudgment>>();
        var firstEntered = new TaskCompletionSource();
        var secondEntered = new TaskCompletionSource();
        var judge = new Mock<IAiCheckJudgeService>();
        CancellationToken oldToken = default;
        IReadOnlyList<TriageCandidateDto> oldCandidates = [];
        IReadOnlyList<TriageCandidateDto> newCandidates = [];
        int calls = 0;
        judge.Setup(j => j.EvaluateFindingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TriageCandidateDto>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<TriageCandidateDto> candidates, CancellationToken ct) =>
            {
                if (++calls == 1)
                {
                    oldToken = ct;
                    oldCandidates = candidates;
                    firstEntered.SetResult();
                    return first.Task;
                }
                newCandidates = candidates;
                secondEntered.SetResult();
                return second.Task;
            });
        var vm = CreateViewModel(judge: judge);
        var workspace = Workspace(vm);
        workspace.IsChatModeActive = false;
        var oldRun = RunAsync(vm);
        await firstEntered.Task;
        await InvalidateAsync(vm, workspace, action);
        Assert.True(oldToken.IsCancellationRequested);
        SetFile(vm);
        vm.Read = _ => Task.FromResult(Boxes("pelaynan"));
        var newRun = RunAsync(vm);
        await secondEntered.Task;

        if (failOldRun) first.SetException(new InvalidOperationException("obsolete failure"));
        else first.SetResult(Judgments(oldCandidates, AiCheckJudgmentStatus.Koreksi));
        await oldRun;
        Assert.True(vm.IsAnalyzing);
        Assert.NotEmpty(vm.AnalysisStageText);
        Assert.Equal("pelaynan", Assert.Single(vm.Findings).OriginalText);
        Assert.DoesNotContain("obsolete failure", vm.SummaryText);

        second.SetResult(Judgments(newCandidates, AiCheckJudgmentStatus.PerluVerifikasi));
        await newRun;
        Assert.False(vm.IsAnalyzing);
        Assert.True(vm.IsInspectionCompleted);
        Assert.Equal("pelaynan", Assert.Single(vm.Findings).OriginalText);
        Assert.Equal(1, vm.SnapshotFindingCount);
    });

    private static TestViewModel CreateViewModel(Dictionary<string, ReferenceDataset>? datasets = null, Mock<IAiCheckJudgeService>? judge = null)
    {
        datasets ??= new();
        var manager = new Mock<IReferenceDataManager>();
        manager.Setup(m => m.GetBaseLanguageLexicon()).Returns(new HashSet<string> { "MENYEDIAKAN", "PELAYANAN" });
        manager.Setup(m => m.GetDatasetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken _) => Task.FromResult(datasets.GetValueOrDefault(id)));
        manager.Setup(m => m.GetAllDatasetsMetadataAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<IReadOnlyList<ReferenceDatasetMetadata>>(datasets.Values.Select(d => new ReferenceDatasetMetadata { Id = d.Id, Category = d.Category }).ToList()));
        if (judge == null)
        {
            judge = new Mock<IAiCheckJudgeService>();
            judge.Setup(j => j.EvaluateFindingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TriageCandidateDto>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, IReadOnlyList<TriageCandidateDto> candidates, CancellationToken _) => Task.FromResult(Judgments(candidates, AiCheckJudgmentStatus.PerluVerifikasi)));
        }
        var vm = new TestViewModel(manager.Object, judge.Object);
        SetFile(vm);
        return vm;
    }

    private static IReadOnlyList<AiCheckJudgment> Judgments(IReadOnlyList<TriageCandidateDto> candidates, AiCheckJudgmentStatus status) =>
        candidates.Select(c => new AiCheckJudgment { CandidateId = c.CandidateId, BoxId = c.BoxId, BoxIds = c.BoxIds, OriginalText = c.OriginalText, RecommendedText = "correction", Status = status }).ToList();

    private static List<OcrBoxModel> Boxes(string text) => [new() { Id = 1, Text = text, X2 = 500, Y2 = 50, DetScore = 1, RecConfidence = 1 }];

    private static void SetFile(AiCheckViewModel vm)
    {
        typeof(AiCheckViewModel).GetProperty(nameof(vm.UploadedFilePath))!.SetValue(vm, "design.png");
        typeof(AiCheckViewModel).GetProperty(nameof(vm.PreviewImage))!.SetValue(vm, new System.Windows.Media.Imaging.BitmapImage());
    }

    private static Task RunAsync(AiCheckViewModel vm) => (Task)typeof(AiCheckViewModel).GetMethod("ExecuteRunCheckAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;

    private static ConversationViewModel Workspace(AiCheckViewModel vm) => new(new Mock<IConversationManagerService>().Object, new Mock<IConversationRuntimeWorkflow>().Object, aiCheck: vm);

    private static async Task InvalidateAsync(AiCheckViewModel vm, ConversationViewModel workspace, string action)
    {
        if (action == "reset") vm.ResetCommand.Execute(null);
        else if (action == "chat") workspace.IsChatModeActive = true;
        else await workspace.OnNavigatedFromAsync();
    }

    private static Task OnUiAsync(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WpfAppHost.Run(async () =>
        {
            try { await action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class TestViewModel(IReferenceDataManager references, IAiCheckJudgeService judge) : AiCheckViewModel(references, judge)
    {
        public Func<string, Task<List<OcrBoxModel>>> Read { get; set; } = _ => Task.FromResult(Boxes("menyediakn"));
        protected override Task<List<OcrBoxModel>> ReadOcrBoxesAsync(string filePath) => Read(filePath);
    }
}
