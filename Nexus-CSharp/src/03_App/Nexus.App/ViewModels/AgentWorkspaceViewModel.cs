using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;

namespace Nexus.App.ViewModels;

public sealed class AgentWorkspaceViewModel : ViewModelBase
{
    private readonly IAgentWorkspaceWorkflow workflow;
    private AgentWorkspaceSelection? selection;
    private ProjectReadGrant? readGrant;
    private bool reviewedEditAuthorized;
    private AgentWorkspaceApprovalProvider? approval;
    private SourceEditPlan? plan;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? deadlineCancellation;
    private bool elapsedDeadlineReached;
    private Task? active;
    private readonly System.Diagnostics.Stopwatch runClock=new();
    private DispatcherTimer? timer;
    private int revision;
    private bool isOpen, isRunning;
    private string workspace="Belum ada proyek dipilih.", status="Idle", resultText="", reviewText="";
    private string prompt="", alphaOld="",alphaNew="",betaOld="",betaNew="";
    private string diffText="",planDetails="";
    private SourceEditPlan? reviewedPlan;
    private IReadOnlyList<ReviewFileViewModel> reviewFiles=[];
    private IReadOnlyList<ReviewFileViewModel> openReviewFiles=[];
    private ReviewFileViewModel? selectedReviewFile;
    public AgentWorkspaceViewModel(IAgentWorkspaceWorkflow? workflow = null)
    {
        this.workflow=workflow!;
        RunCommand=new AsyncRelayCommand(RunAsync,()=>CanRun);
        CancelCommand=new RelayCommand(()=>cancellation?.Cancel(),()=>IsRunning);
        ApproveCommand=new RelayCommand(()=>Decide(true),()=>CanDecide);
        RejectCommand=new RelayCommand(()=>Decide(false),()=>CanDecide);
        CloseCommand=new AsyncRelayCommand(async()=>{ await CancelAndDrainAsync(); IsOpen=false; });
        HideReviewCommand=new RelayCommand(()=>IsOpen=false);
        SelectReviewFileCommand=new RelayCommand<ReviewFileViewModel>(file=>
        {
            if(file is null || !ReviewFiles.Contains(file))return;
            if(!openReviewFiles.Contains(file))
            {openReviewFiles=Array.AsReadOnly(openReviewFiles.Append(file).ToArray());OnPropertyChanged(nameof(OpenReviewFiles));}
            SelectedReviewFile=file;file.IsExpanded=true;IsOpen=true;Refresh();
        });
        CloseReviewFileCommand=new RelayCommand<ReviewFileViewModel>(CloseReviewFile);
        PreviousReviewFileCommand=new RelayCommand(()=>MoveReviewFile(-1),()=>CanMoveReviewFile(-1));
        NextReviewFileCommand=new RelayCommand(()=>MoveReviewFile(1),()=>CanMoveReviewFile(1));
        ExpandAllFilesCommand=new RelayCommand(()=>{foreach(var file in ReviewFiles)file.IsExpanded=true;});
        CollapseAllFilesCommand=new RelayCommand(()=>{foreach(var file in ReviewFiles)file.IsExpanded=false;});
    }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand HideReviewCommand { get; }
    public ICommand SelectReviewFileCommand { get; }
    public ICommand CloseReviewFileCommand { get; }
    public ICommand PreviousReviewFileCommand { get; }
    public ICommand NextReviewFileCommand { get; }
    public ICommand ExpandAllFilesCommand { get; }
    public ICommand CollapseAllFilesCommand { get; }
    public IReadOnlyList<ReviewFileViewModel> ReviewFiles=>reviewFiles;
    public IReadOnlyList<ReviewFileViewModel> OpenReviewFiles=>openReviewFiles;
    public bool HasReviewFiles=>ReviewFiles.Count>0;
    public bool CanOpenReview=>HasReview || HasResult;
    public ReviewFileViewModel? SelectedReviewFile
    {
        get=>selectedReviewFile;
        set
        {
            if(value is not null && !ReviewFiles.Contains(value) || ReferenceEquals(value,selectedReviewFile))return;
            if(selectedReviewFile is not null)selectedReviewFile.IsSelected=false;
            selectedReviewFile=value;if(value is not null)value.IsSelected=true;OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public int TotalAdditions=>ReviewFiles.Sum(f=>f.Additions);
    public int TotalDeletions=>ReviewFiles.Sum(f=>f.Deletions);
    public string ChangeTotals=>$"+{TotalAdditions} −{TotalDeletions} baris";
    public string ChangeSummary=>!HasReviewFiles ? "Belum ada perubahan untuk ditinjau" :
        IsVerifiedChange && LastResult?.Context.Progress?.SourceResult is { } result && reviewedPlan is { } exact && result.PlanId==exact.PlanId && result.PlanDigest==exact.PlanDigest
        ? $"{ReviewFiles.Count} file berhasil diubah dan diverifikasi" : PendingPlan is not null && IsRunning ? $"{ReviewFiles.Count} file akan diubah" : $"{ReviewFiles.Count} file dalam rencana perubahan";
    public bool IsVerifiedChange=>LastResult is {Outcome:AgentRunOutcome.Completed,Verification.Passed:true,Context.Progress.SourceResult: {FinalVerified:true,Build.Passed:true,Tests.Passed:true,Tests.TestsPassed:3}};
    public bool HasActivity=>IsRunning || HasResult;
    public string FriendlyStatus=>IsRunning ? CanDecide ? "Perubahan siap ditinjau — belum ada file diubah" : Status switch
    {"Analyzing"=>"Sedang memeriksa kode", "Verifying"=>"Memeriksa hasil perubahan", "Recovering"=>"Sedang memulihkan file",
        "ExecutingTool — approved exact plan."=>"Plan disetujui — menunggu hasil verifikasi host",
        var message when message.StartsWith("Approval ditolak:",StringComparison.Ordinal)=>"Persetujuan ditolak — tinjauan sudah tidak berlaku", _=>"Tugas sedang diproses"} :
        !HasResult ? "Belum ada perubahan kode" : elapsedDeadlineReached ? "Waktu perubahan habis" :
        LastResult is not { } result ? Status.StartsWith("Cancelled",StringComparison.Ordinal) ? "Tugas dibatalkan" : "Perubahan tidak dapat diselesaikan. Lihat Detail teknis." :
        IsVerifiedChange ? "Perubahan berhasil diverifikasi" :
        result.Context.Progress?.SourceResult?.Outcome==SourcePlanOutcome.RecoveryConflict ? "File berubah di luar proses; perubahan tersebut dipertahankan" :
        result.Context.Progress?.SourceResult?.BaselineVerified==true && result.Context.Progress.SourceResult.Operations.Any(o=>o.Recovery==SafeWriteRecovery.Restored) ? "File berhasil dipulihkan" :
        result.ErrorCategory=="OutputLimit" ? "Respons AI belum lengkap; tugas tidak berhasil" : result.Outcome==AgentRunOutcome.Cancelled ? "Tugas dibatalkan" :
        result.Outcome==AgentRunOutcome.TimedOut ? "Waktu perubahan habis" : result.Outcome==AgentRunOutcome.Rejected ? "Perubahan ditolak — belum ada file diubah" : "Perubahan gagal — lihat Detail teknis";
    public bool IsOpen { get=>isOpen; set=>SetProperty(ref isOpen,value); }
    public bool IsRunning { get=>isRunning; private set { SetProperty(ref isRunning,value); Refresh(); } }
    public bool CanEdit=>!IsRunning;
    public bool IsNaturalLanguage=>selection?.Profile.ReviewedManifest is not null;
    public bool CanAuthorizeReviewedEdit=>IsNaturalLanguage && !IsRunning && readGrant?.IsActive==true;
    public bool ReviewedEditAuthorized
    {
        get=>reviewedEditAuthorized;
        set
        {
            if(IsRunning || !IsNaturalLanguage)return;
            if(value)
            {
                try {if(readGrant is null)throw new InvalidOperationException("Izin baca diperlukan secara terpisah.");selection=workflow.AuthorizeReviewedEdit(selection!,readGrant);reviewedEditAuthorized=true;Status="Ready — proposal bahasa alami, persetujuan exact diff tetap wajib.";}
                catch(Exception error){reviewedEditAuthorized=false;Status="Reviewed authority ditolak: "+error.Message;}
            }
            else {reviewedEditAuthorized=false;if(selection is not null)selection=selection with {ReviewedAuthority=null};}
            Refresh();
        }
    }
    public void BindReadGrant(ProjectReadGrant grant)
    {
        if(IsRunning)throw new InvalidOperationException("Drain Agent sebelum mengganti izin baca.");
        if(!IsNaturalLanguage)return;
        if(grant.Root!=selection!.Profile.Root)throw new InvalidOperationException("Read/project scope mismatch.");
        readGrant=grant;reviewedEditAuthorized=false;selection=selection with {ReviewedAuthority=null};Refresh();
    }
    public void RevokeReadGrant()
    {
        if(!IsNaturalLanguage)return;
        cancellation?.Cancel();approval?.Deny("ReadGrantRevoked");readGrant=null;reviewedEditAuthorized=false;
        selection=selection! with {ReviewedAuthority=null};Refresh();
    }
    public bool CanRun=>selection is not null && !IsRunning && !string.IsNullOrWhiteSpace(Prompt) &&
        (IsNaturalLanguage?reviewedEditAuthorized && selection.ReviewedAuthority?.IsActive==true:!string.IsNullOrEmpty(AlphaOld) && !string.IsNullOrEmpty(BetaOld));
    private TimeSpan RunBudget=>selection is null?TimeSpan.FromSeconds(120):ReviewedProjectCatalog.RunBudget(selection.Profile);
    private bool SeparateReview=>RunBudget==ReviewedProjectCatalog.PilotExecutionBudget;
    public bool CanDecide=>IsRunning && plan is not null && approval is not null &&
        (SeparateReview?approval.CanDecide:runClock.Elapsed<RunBudget && DateTimeOffset.UtcNow<Deadline);
    public string Workspace { get=>workspace; private set=>SetProperty(ref workspace,value); }
    public string Status { get=>status; private set=>SetProperty(ref status,value); }
    public string ResultText { get=>resultText; private set { SetProperty(ref resultText,value);OnPropertyChanged(nameof(HasResult));OnPropertyChanged(nameof(ResultSummary)); } }
    public string ReviewText { get=>reviewText; private set { SetProperty(ref reviewText,value);OnPropertyChanged(nameof(HasReview)); } }
    public string DiffText { get=>diffText; private set=>SetProperty(ref diffText,value); }
    public string PlanDetails { get=>planDetails; private set=>SetProperty(ref planDetails,value); }
    public bool HasWorkspace=>selection is not null;
    public bool HasReview=>!string.IsNullOrEmpty(ReviewText);
    public bool HasResult=>!string.IsNullOrEmpty(ResultText);
    public IReadOnlyList<string> AllowlistedFiles=>selection?.Profile.Targets.Take(2).ToArray() ?? [];
    public string ResultSummary=>!HasResult ? "" : LastResult?.Context.Progress?.SourceResult is not { } source ? FriendlyStatus :
        $"{FriendlyStatus}\nPemeriksaan kode: {(source.Build is null ? "belum dijalankan" : source.Build.Passed ? "berhasil" : "tidak berhasil")} · Uji: {(source.Tests is null ? "belum dijalankan" : $"{source.Tests.TestsPassed}/3 berhasil")}";
    public string Prompt { get=>prompt; set { if(!IsRunning) { SetProperty(ref prompt,value); Refresh(); } } }
    public string AlphaOld { get=>alphaOld; set { if(!IsRunning) { SetProperty(ref alphaOld,value); Refresh(); } } }
    public string AlphaNew { get=>alphaNew; set { if(!IsRunning) SetProperty(ref alphaNew,value); } }
    public string BetaOld { get=>betaOld; set { if(!IsRunning) { SetProperty(ref betaOld,value); Refresh(); } } }
    public string BetaNew { get=>betaNew; set { if(!IsRunning) SetProperty(ref betaNew,value); } }
    public DateTimeOffset Deadline { get; private set; }
    public string RemainingText=>SeparateReview ? IsRunning ?
        $"Sisa eksekusi: {Math.Max(0,approval?.RemainingExecution.TotalSeconds ?? RunBudget.TotalSeconds-runClock.Elapsed.TotalSeconds):F0} detik"+
            (plan is not null && approval is not null?$" · Review: {Math.Max(0,approval.RemainingReview.TotalSeconds):F0} detik":" · waktu review terpisah") :
        $"Batas eksekusi {RunBudget.TotalSeconds:F0} detik · review maksimal 600 detik, satu kali." :
        IsRunning ? $"Sisa waktu: {Math.Max(0,RunBudget.TotalSeconds-runClock.Elapsed.TotalSeconds):F0} detik · termasuk persetujuan" : $"Batas waktu tugas {RunBudget.TotalSeconds:F0} detik, termasuk persetujuan.";
    public AgentResult? LastResult { get; private set; }
    public SourceEditPlan? PendingPlan=>plan;
    public FileSnapshot ReadFileContext(string target)=>selection is null ? throw new InvalidOperationException("Pilih proyek yang sudah direview.") : workflow.ReadFileContext(selection,target);

    // ============================================================
    // Fase 2 (v18): Native Windows 11 WebView2 Chromium Browser Bridge
    // ============================================================
    public event Func<string, Task<string>>? BrowserNavigateRequested;
    public string LastBrowserUrl { get; set; } = string.Empty;
    public string LastBrowserTitle { get; set; } = string.Empty;

    public async Task<string> NavigateBrowserFromAgentAsync(string url)
    {
        IsOpen = true;
        LastBrowserUrl = url;
        if (BrowserNavigateRequested is not null)
        {
            return await BrowserNavigateRequested.Invoke(url);
        }
        return url;
    }

    public async Task SelectWorkspaceAsync(string root)
    {
        await ClearActivityAsync();
        var version=++revision;
        selection=null; plan=null; approval=null; ReviewText=""; ResultText="";DiffText="";PlanDetails="";
        readGrant=null;reviewedEditAuthorized=false;
        Workspace="Memeriksa kesiapan proyek..."; Status="Validating";
        Refresh();
        try
        {
            var admitted=await Task.Run(()=>workflow.Select(root));
            if(version!=revision) return;
            selection=admitted;
            Workspace=admitted.Profile.Root+"\n"+admitted.Profile.Id+"\n"+string.Join(" • ",admitted.Profile.Targets);
            Status=IsNaturalLanguage?"Berikan izin baca dan izin edit/verifikasi reviewed secara terpisah.":"Ready — literal edits eksplisit diperlukan.";
        }
        catch(Exception error) { if(version==revision) { Workspace="Workspace ditolak."; Status="Rejected: "+error.Message; } }
        Refresh();
    }

    public Task RunAsync()
    {
        if(!CanRun) return Task.CompletedTask;
        var captured=selection!;
        var session=IsNaturalLanguage?readGrant!.Session:"i1-"+Guid.NewGuid().ToString("N");
        var version=++revision;
        var request=Prompt;
        var edits=IsNaturalLanguage?ImmutableArray<SourceFileEdit>.Empty:ImmutableArray.Create(new SourceFileEdit("Product/Alpha.cs",captured.Files[0].Hash,AlphaOld,AlphaNew),
            new SourceFileEdit("Product/Beta.cs",captured.Files[1].Hash,BetaOld,BetaNew));
        elapsedDeadlineReached=false;
        deadlineCancellation=new CancellationTokenSource();
        cancellation=CancellationTokenSource.CreateLinkedTokenSource(deadlineCancellation.Token,IsNaturalLanguage?readGrant!.Cancellation:CancellationToken.None);
        Deadline=DateTimeOffset.UtcNow+RunBudget;
        runClock.Restart();if(!SeparateReview)deadlineCancellation.CancelAfter(RunBudget);
        LastResult=null; ReviewText=""; ResultText="";DiffText="";PlanDetails=""; plan=null; approval=null;
        ClearReviewFiles();
        IsRunning=true; Status="Analyzing";
        timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(200) };
        timer.Tick+=(_,_)=>{ if(!SeparateReview && (runClock.Elapsed>=RunBudget || DateTimeOffset.UtcNow>=Deadline)) approval?.Deny("ApprovalExpired"); Refresh(); };
        timer.Start();
        active=ExecuteAsync();
        return active;

        async Task ExecuteAsync()
        {
            try
            {
                var result=await Task.Run(()=>workflow.RunAsync(captured,session,request,edits,Deadline,
                    state=>Ui(()=>{ if(version==revision && IsRunning) Status=state; }),
                    (exact,provider)=>Ui(()=>
                    {
                        if(version!=revision || !IsRunning || cancellation?.IsCancellationRequested!=false) { provider.Deny("StaleWorkspaceSession"); return; }
                        plan=exact; approval=provider;
                        ReviewText=$"Run {exact.RunId}\nPlan {exact.PlanId}\nDigest {exact.PlanDigest}\nWorkspace identity {exact.WorkspaceIdentity}\nProfile {exact.VerificationProfileId}\n{exact.Profile.Root}\n"+
                            $"{exact.EditCount} edits; {exact.DiffBytes} diff bytes; {exact.ChangedLines} changed lines\nBuild/tests fixed reviewed profile; no automatic repair.\n\n"+
                            string.Join("\n\n",exact.Entries.Select(e=>$"{e.Target}\nBefore {e.BeforeHash}\nDesired {e.DesiredHash}\n{e.Diff}"));
                        PlanDetails=$"Run {exact.RunId}\nPlan {exact.PlanId}\nDigest {exact.PlanDigest}\nWorkspace identity {exact.WorkspaceIdentity}\nProfile {exact.VerificationProfileId}\n{exact.Profile.Root}\n{exact.EditCount} edits · {exact.DiffBytes} diff bytes · {exact.ChangedLines} changed lines\nFixed build/tests · no automatic repair";
                        PlanDetails+="\n"+string.Join("\n",exact.Entries.Select(e=>$"{e.Target}\nBefore {e.BeforeHash}\nDesired {e.DesiredHash}"));
                        DiffText=string.Join("\n\n",exact.Entries.Select(e=>$"{e.Target}\n{e.Diff}"));
                        reviewedPlan=exact;
                        reviewFiles=Array.AsReadOnly(exact.Entries.Select(e=>new ReviewFileViewModel(e.Target,e.Diff)).ToArray());
                        openReviewFiles=[];selectedReviewFile=null;OnPropertyChanged(nameof(SelectedReviewFile));OnPropertyChanged(nameof(OpenReviewFiles));
                        NotifyReviewFiles();
                        Status="AwaitingApproval — disk belum diubah."; Refresh();
                    }),cancellation.Token));
                LastResult=result;
                if(version!=revision) return;
                var verified=result.Outcome==AgentRunOutcome.Completed && result.Verification?.Passed==true &&
                    result.Context.Progress?.SourceResult is { FinalVerified:true,Build.Passed:true,Tests.Passed:true,Tests.TestsPassed:3 };
                elapsedDeadlineReached=!verified && (deadlineCancellation?.IsCancellationRequested==true || result.Failure==AgentFailureCategory.DeadlineExceeded);
                Status=verified ? "Completed — source, build dan 3 required tests verified." : elapsedDeadlineReached ? "TimedOut: ExecutionDeadline" :
                    $"{(result.Outcome==AgentRunOutcome.Completed ? AgentRunOutcome.Failed : result.Outcome)}: {result.ErrorCategory ?? "VerificationFailed"}";
                ResultText=JsonSerializer.Serialize(new { result.Context.RunId,result.Context.SessionId,result.Outcome,result.ErrorCategory,
                    result.Duration,result.Verification,result.Context.Progress?.SourceSummary,uiDeadlineExpired=elapsedDeadlineReached,
                    build=result.Context.Progress?.SourceResult?.Build,tests=result.Context.Progress?.SourceResult?.Tests,
                    baselineVerified=result.Context.Progress?.SourceResult?.BaselineVerified,
                     recoveredBaseDigest=result.Context.Progress?.SourceResult?.RecoveredBaseDigest,
                     sourceResult=result.Context.Progress?.SourceResult },
                    new JsonSerializerOptions { WriteIndented=true,Converters={new JsonStringEnumConverter()} });
            }
            catch(OperationCanceledException error)
            {
                if(version==revision)
                {
                    elapsedDeadlineReached=deadlineCancellation?.IsCancellationRequested==true || error.Data["ExecutionDeadline"] is true;
                    Status=elapsedDeadlineReached?"TimedOut: ExecutionDeadline":"Cancelled";
                    ResultText=JsonSerializer.Serialize(new {uiDeadlineExpired=elapsedDeadlineReached,error=error.ToString()});
                }
            }
            catch(Exception error) { if(version==revision) { Status="Failed: "+error.Message; ResultText=error.ToString(); } }
            finally
            {
                approval?.Deny("RunEnded"); approval=null; plan=null;
                timer?.Stop(); timer=null;
                cancellation?.Dispose(); cancellation=null;
                deadlineCancellation?.Dispose();deadlineCancellation=null;
                IsRunning=false; Refresh();
                if(IsNaturalLanguage){reviewedEditAuthorized=false;OnPropertyChanged(nameof(ReviewedEditAuthorized));}
            }
        }
    }

    private void Decide(bool approve)
    {
        if(plan is not { } exact || approval is null) return;
        if(!approval.Decide(exact.RunId,exact.PlanId,exact.PlanDigest,approve)) { Status="Approval ditolak: stale/expired."; Refresh();return; }
        plan=null;if(!SeparateReview)approval=null;
        Status=approve ? "ExecutingTool — approved exact plan." : "ApprovalRejected — zero write.";
        Refresh();
    }
    public async Task ClearActivityAsync()
    {
        await CancelAndDrainAsync();
        LastResult=null;ReviewText="";ResultText="";DiffText="";PlanDetails="";IsOpen=false;
        ClearReviewFiles();
        Status=HasWorkspace ? "Ready" : "Idle";Refresh();
    }
    public async Task CancelAndDrainAsync()
    {
        cancellation?.Cancel(); approval?.Deny("ApprovalCancelled");
        if(active is not null) await active;
        ++revision; plan=null; approval=null; Refresh();
    }
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanRun)); OnPropertyChanged(nameof(CanDecide)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsNaturalLanguage));OnPropertyChanged(nameof(CanAuthorizeReviewedEdit));OnPropertyChanged(nameof(ReviewedEditAuthorized));
        OnPropertyChanged(nameof(RemainingText)); OnPropertyChanged(nameof(PendingPlan));
        OnPropertyChanged(nameof(HasWorkspace));OnPropertyChanged(nameof(AllowlistedFiles));OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(HasActivity));OnPropertyChanged(nameof(FriendlyStatus));
        OnPropertyChanged(nameof(CanOpenReview));OnPropertyChanged(nameof(ChangeSummary));
        CommandManager.InvalidateRequerySuggested();
    }
    private static void Ui(Action action)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(action);
        else action();
    }
    private void ClearReviewFiles()
    {reviewedPlan=null;reviewFiles=[];openReviewFiles=[];selectedReviewFile=null;IsOpen=false;OnPropertyChanged(nameof(SelectedReviewFile));OnPropertyChanged(nameof(OpenReviewFiles));NotifyReviewFiles();}
    private void CloseReviewFile(ReviewFileViewModel? file)
    {
        if(file is null || !openReviewFiles.Contains(file))return;
        var index=openReviewFiles.ToList().IndexOf(file);
        openReviewFiles=Array.AsReadOnly(openReviewFiles.Where(f=>!ReferenceEquals(f,file)).ToArray());
        if(ReferenceEquals(file,SelectedReviewFile))SelectedReviewFile=openReviewFiles.Count==0 ? null : openReviewFiles[Math.Max(0,index-1)];
        OnPropertyChanged(nameof(OpenReviewFiles));
        if(openReviewFiles.Count==0)IsOpen=false;
    }
    private bool CanMoveReviewFile(int direction)
    {
        var index=ReviewFiles.ToList().IndexOf(SelectedReviewFile!);
        return index>=0 && index+direction>=0 && index+direction<ReviewFiles.Count;
    }
    private void MoveReviewFile(int direction)
    {if(CanMoveReviewFile(direction))SelectReviewFileCommand.Execute(ReviewFiles[ReviewFiles.ToList().IndexOf(SelectedReviewFile!)+direction]);}
    private void NotifyReviewFiles()
    {foreach(var name in new[] {nameof(ReviewFiles),nameof(HasReviewFiles),nameof(TotalAdditions),nameof(TotalDeletions),nameof(ChangeTotals),nameof(ChangeSummary)})OnPropertyChanged(name);}
}

// Read-only presentation adapter. No filesystem access, source parsing, grants,
// or diff regeneration. The original bytes/text stay available as RawDiff.
public sealed class ReviewFileViewModel : ViewModelBase
{
    private bool isExpanded=true,isSelected;
    public string Target { get; }
    public string FileName=>System.IO.Path.GetFileName(Target);
    public string RawDiff { get; }
    public IReadOnlyList<ReviewDiffLine> Lines { get; }
    public int Additions=>Lines.Count(l=>l.Kind==ReviewDiffLineKind.Added);
    public int Deletions=>Lines.Count(l=>l.Kind==ReviewDiffLineKind.Deleted);
    public string Statistics=>$"+{Additions} −{Deletions}";
    public bool HasDiffChanges=>Additions>0 || Deletions>0;
    public string DisplayStatistics=>HasDiffChanges?$"+{Additions} −{Deletions}":string.Empty;
    public string FileExtensionBadge=>System.IO.Path.GetExtension(Target).ToLowerInvariant() switch
    {
        ".cs"=>"C#",
        ".md"=>"MD",
        ".xaml" or ".xml" or ".csproj" or ".sln" or ".slnx"=>"<>",
        ".json"=>"{}",
        ".cmd" or ".bat" or ".ps1" or ".sh"=>">_",
        ".py"=>"PY",
        ".js" or ".ts"=>"JS",
        _=>"TXT"
    };
    public string DirectoryPrefix=>System.IO.Path.GetDirectoryName(Target) is {Length:>0} dir ? dir+System.IO.Path.DirectorySeparatorChar : string.Empty;
    public bool IsMarkdownFile=>string.Equals(System.IO.Path.GetExtension(Target),".md",StringComparison.OrdinalIgnoreCase);
    public string CodeBannerLanguage=>System.IO.Path.GetExtension(Target).ToLowerInvariant() switch
    {
        ".cs"=>"csharp",
        ".md"=>"markdown",
        ".xaml" or ".xml" or ".csproj" or ".sln" or ".slnx"=>"xml",
        ".json"=>"json",
        ".cmd" or ".bat" or ".ps1" or ".sh"=>"powershell",
        ".py"=>"python",
        ".js" or ".ts"=>"typescript",
        _=>"plaintext"
    };
    public string FileLanguageLabel=>System.IO.Path.GetExtension(Target).ToLowerInvariant() switch
    {
        ".md"=>"Markdown",
        ".xaml" or ".xml"=>"XAML",
        ".json"=>"JSON",
        ".cmd" or ".bat" or ".ps1" or ".sh"=>"Script",
        _=>"Code"
    };
    public string AccessibleName=>$"{Target}, {Additions} baris ditambahkan, {Deletions} baris dihapus";
    public bool IsExpanded { get=>isExpanded;set=>SetProperty(ref isExpanded,value); }
    public bool IsSelected { get=>isSelected;set=>SetProperty(ref isSelected,value); }
    public ReviewFileViewModel(string target,string rawDiff)
    {
        Target=target;RawDiff=rawDiff;var lines=new List<ReviewDiffLine>();
        // Source I1's trusted formatter puts an explanation and JSON-edits line
        // before its context diff. Keep these in raw technical view, not code UI.
        var metadataLines=rawDiff.StartsWith("Exact original-snapshot edits (JSON escaped):\n",StringComparison.Ordinal) ? 2 : 0;
        for(int offset=0,ordinal=0;offset<rawDiff.Length;ordinal++)
        {
            var end=rawDiff.IndexOfAny(['\r','\n'],offset);if(end<0)end=rawDiff.Length;
            var text=rawDiff[offset..end];var next=end;
            if(next<rawDiff.Length && rawDiff[next]=='\r')next++;
            if(next<rawDiff.Length && rawDiff[next]=='\n')next++;
            var newline=rawDiff[end..next];offset=next;
            if(ordinal<metadataLines)continue;
            var kind=text.StartsWith("+ ",StringComparison.Ordinal) ? ReviewDiffLineKind.Added : text.StartsWith("- ",StringComparison.Ordinal) ? ReviewDiffLineKind.Deleted : ReviewDiffLineKind.Context;
            var code=text.Length>=2 && (kind!=ReviewDiffLineKind.Context || text.StartsWith("  ",StringComparison.Ordinal)) ? text[2..] : text;
            lines.Add(new(kind,code,text,newline));
        }
        Lines=lines.AsReadOnly();
    }
}
public enum ReviewDiffLineKind {Context,Added,Deleted}
public sealed record ReviewDiffLine(ReviewDiffLineKind Kind,string Code,string RawText,string Newline)
{
    public string Marker=>Kind==ReviewDiffLineKind.Added ? "+" : Kind==ReviewDiffLineKind.Deleted ? "−" : " ";
}
