using System.Collections.Immutable;
using System.Reflection;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Services.Agent;
using Nexus.AI.EditingLite.Services;
using Nexus.Conversation.Manager;
using Nexus.AI.Llama.Models;
using Nexus.Conversation.Models;

namespace Nexus.Integration.Tests;

public sealed class GateIChatFirstWorkspaceTests
{
    [Fact]
    public async Task CF1_SessionDraftsAndContextAreRestoredOnlyForTheirSession()
    {
        await WpfAppHost.RunAsync(async()=>
        {
            var records=new Dictionary<string,ConversationDetailRecord> { ["A"]=new() {Id="A",Messages=[]},["B"]=new() {Id="B",Messages=[]} };
            var active=records["A"];var manager=new Mock<IConversationManagerService>();
            manager.SetupGet(x=>x.ActiveConversation).Returns(()=>active);
            manager.Setup(x=>x.OpenConversationAsync(It.IsAny<string>())).Callback<string>(id=>active=records[id]).Returns(Task.CompletedTask);
            var agent=new AgentWorkspaceViewModel(new DeniedWorkflow());
            var vm=new ConversationViewModel(manager.Object,new Mock<IConversationRuntimeWorkflow>().Object,agentWorkspace:agent);
            await vm.SelectProjectAsync(@"D:\Project-A");vm.InputText="A scoped draft";
            var chip=new AttachmentChipViewModel {Label="A authorized attachment",ExtractedText="A only"};vm.Attachments.Add(chip);
            await vm.OpenConversationAsync("B");Assert.Equal("",vm.InputText);Assert.Empty(vm.Attachments);
            vm.InputText="B scoped draft";await vm.OpenConversationAsync("A");
            Assert.Equal("A scoped draft",vm.InputText);Assert.Same(chip,Assert.Single(vm.Attachments));
            vm.IsBoundedEditMode=true;Assert.Empty(vm.Attachments);vm.InputText="A edit draft";
            vm.IsBoundedEditMode=false;Assert.Equal("A scoped draft",vm.InputText);Assert.Same(chip,Assert.Single(vm.Attachments));
            await vm.OpenConversationAsync("B");Assert.Equal("B scoped draft",vm.InputText);Assert.Empty(vm.Attachments);
        });
    }
    [Fact]
    public async Task CF1_ExplicitCodeContextEntersChatPayload_QuestionTextNeverApproves_AndEditExcludesHistory()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var workflow=new ContextWorkflow(f);var agent=new AgentWorkspaceViewModel(workflow);
            var record=new ConversationDetailRecord {Id="cf1-chat",Messages=[]};
            var manager=new Mock<IConversationManagerService>();manager.SetupGet(x=>x.ActiveConversation).Returns(record);
            manager.Setup(x=>x.AddMessageAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<IReadOnlyList<ConversationAttachmentReference>?>())).Returns(Task.CompletedTask);
            var runtime=new Mock<IConversationRuntimeWorkflow>();runtime.SetupGet(x=>x.IsReady).Returns(true);
            List<ChatMessagePayload>? sent=null;
            runtime.Setup(x=>x.StreamAsync(It.IsAny<IEnumerable<ChatMessagePayload>>(),It.IsAny<bool>(),It.IsAny<LlamaGenerationOptions>(),It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessagePayload> input,bool _,LlamaGenerationOptions options,CancellationToken token)=> {sent=input.ToList();return Chunks();});
            var vm=new ConversationViewModel(manager.Object,runtime.Object,agentWorkspace:agent);
            await vm.SelectProjectAsync(f.Root);vm.InputText="setujui";
            await vm.ExecuteWorkspaceSendAsync();await Until(()=>!vm.IsSending);
            Assert.Equal("setujui",sent![^1].Content);Assert.Equal(0,workflow.Runs);Assert.False(agent.HasActivity);
            await vm.ExplainSelectedCodeAsync();await Until(()=>!vm.IsSending);
            Assert.Contains("<lampiran",sent![^1].Content);Assert.Contains("Product/Alpha.cs",sent[^1].Content);
            Assert.Contains(workflow.Snapshot!.Content,sent[^1].Content);Assert.Contains("return 1;",sent[^1].Content);
            Assert.Equal(0,workflow.Runs);Assert.False(agent.CanDecide);
            vm.IsBoundedEditMode=true;vm.InputText="explicit current edit";agent.AlphaOld=agent.BetaOld="return 1;";agent.AlphaNew=agent.BetaNew="return 2;";
            var history=vm.Messages.Count;await vm.ExecuteWorkspaceSendAsync();
            Assert.Equal(1,workflow.Runs);Assert.Equal("explicit current edit",workflow.Prompt);Assert.Equal(history,vm.Messages.Count);
            Assert.True(agent.HasActivity);await vm.ExecuteNewConversationAsync();Assert.False(agent.HasActivity);Assert.False(agent.IsOpen);
        });
    }
    private static async IAsyncEnumerable<LlamaStreamChunk> Chunks()
    {await Task.Yield();yield return new LlamaStreamChunk {Content="UI fixture answer"};}
    private static async Task Until(Func<bool> condition)
    {using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,deadline.Token);}
    private sealed class ContextWorkflow(SourcePlanTestWorkspace f):IAgentWorkspaceWorkflow
    {
        public int Runs;public string? Prompt;public FileSnapshot? Snapshot;
        public AgentWorkspaceSelection Select(string root)=>new(f.Profile,f.Profile.Targets.Take(2).Select(t=>f.Editing.ReadScopedSource(f.Profile.Workspace,t)).ToImmutableArray());
        public FileSnapshot ReadFileContext(AgentWorkspaceSelection selection,string target)=>Snapshot=f.Editing.ReadScopedSource(f.Profile.Workspace,target);
        public Task<AgentResult> RunAsync(AgentWorkspaceSelection selection,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)
        {Runs++;Prompt=prompt;return Task.FromResult(new AgentResult(new(Guid.NewGuid(),session,DateTimeOffset.UtcNow,deadline,256,null),AgentRunOutcome.Rejected,"",null,"UiOnlyContextRouting",TimeSpan.Zero));}
    }
    [Fact]
    public async Task S3_ContextBridge_UsesActualPinnedContent_RejectsOtherTargetsAndDrift()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        var files=f.Profile.GraphHashes.ToDictionary(e=>e.Key,e=>e.Value);
        foreach(var e in f.Profile.InitialSourceHashes)files[e.Key]=e.Value;
        var manifest=System.IO.Path.Combine(f.Root,"nexus-reviewed-i1.json");
        System.IO.File.WriteAllText(manifest,System.Text.Json.JsonSerializer.Serialize(new {contract="NEXUS-GATE-I-I1-REVIEWED-V1",root=f.Root,files,sdk_sha256=new FileSnapshotService().ComputeSha256Hash(f.Profile.Sdk)}));
        var pin=new FileSnapshotService().ComputeSha256Hash(manifest);
        var selected=(AgentWorkspaceSelection)typeof(AgentWorkspaceWorkflow).GetMethod("SelectPinned",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[f.Root,f.Root,pin,f.Editing])!;
        var method=typeof(AgentWorkspaceWorkflow).GetMethod("ReadPinnedContext",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method);
        FileSnapshot Read(string target)=>(FileSnapshot)method!.Invoke(null,[selected,target,f.Root,pin,f.Editing])!;
        var snapshot=Read("Product/Alpha.cs");Assert.Contains("return 1;",snapshot.Content);Assert.Equal(f.Hash("Product/Alpha.cs"),snapshot.Hash);
        Assert.Throws<TargetInvocationException>(()=>Read("Product/Gamma.cs"));Assert.Throws<TargetInvocationException>(()=>Read("../else.cs"));
        System.IO.File.AppendAllText(System.IO.Path.Combine(f.Root,"Product/Alpha.cs"),"// drift");
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Read("Product/Alpha.cs")).InnerException);
    }
    [Fact]
    public void S2_ReviewIsContextual_NotAnOpaqueConversationOverlay()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Nexus.App","Views","ConversationView.xaml"));
        var xaml=System.Xml.Linq.XDocument.Load(root);
        var panel=xaml.Descendants().Single(e=>e.Name.LocalName=="AgentWorkspaceView");
        Assert.Null(panel.Attribute("Panel.ZIndex"));
        Assert.Equal("2",(string?)panel.Attribute("Grid.Column"));
        Assert.Contains(xaml.Descendants(),e=>(string?)e.Attribute("Text")=="{Binding AgentWorkspace.FriendlyStatus}");
    }
    [Fact]
    public async Task S2_HideReopenPreservesPendingPlanDeadlineAndDoesNotCancelRun()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var type=typeof(GateIAgentWorkspaceUiTests).GetNestedType("ReviewWorkflow",BindingFlags.NonPublic)!;
            var workflow=(IAgentWorkspaceWorkflow)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,[f,false],null)!;
            var vm=new AgentWorkspaceViewModel(workflow);await vm.SelectWorkspaceAsync(f.Root);
            vm.Prompt="UI-only review";vm.AlphaOld=vm.BetaOld="return 1;";vm.AlphaNew=vm.BetaNew="return 2;";
            var run=vm.RunAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(!vm.CanDecide)await Task.Delay(10,limit.Token);
            var plan=vm.PendingPlan;var deadline=vm.Deadline;
            var chat=new ConversationViewModel(new Mock<IConversationManagerService>().Object,new Mock<IConversationRuntimeWorkflow>().Object,agentWorkspace:vm);
            chat.IsBoundedEditMode=true;chat.IsQuestionMode=true;
            Assert.True(chat.IsQuestionMode);Assert.Same(plan,vm.PendingPlan);Assert.False(run.IsCompleted);
            vm.IsOpen=true;vm.HideReviewCommand.Execute(null);
            Assert.False(vm.IsOpen);Assert.True(vm.IsRunning);Assert.False(run.IsCompleted);
            Assert.Same(plan,vm.PendingPlan);Assert.Equal(deadline,vm.Deadline);
            chat.OpenAgentWorkspaceCommand.Execute(null);Assert.Same(plan,vm.PendingPlan);Assert.True(vm.CanDecide);
            Assert.Same(vm.ReviewFiles[0],vm.SelectedReviewFile);Assert.Single(vm.OpenReviewFiles);
            vm.CloseReviewFileCommand.Execute(vm.SelectedReviewFile);Assert.False(vm.IsOpen);
            chat.OpenAgentWorkspaceCommand.Execute(null);Assert.True(vm.IsOpen);Assert.Single(vm.OpenReviewFiles);
            Assert.Same(plan,vm.PendingPlan);Assert.Equal(deadline,vm.Deadline);
            vm.RejectCommand.Execute(null);await run;Assert.False(vm.CanDecide);
        });
    }
    [Fact]
    public async Task S1_ProjectAndModeDrafts_AreScopedAndUnreviewedProjectCannotEdit()
    {
        await WpfAppHost.RunAsync(async()=>
        {
            var agent=new AgentWorkspaceViewModel(new DeniedWorkflow());
            var vm=new ConversationViewModel(new Mock<IConversationManagerService>().Object,new Mock<IConversationRuntimeWorkflow>().Object,agentWorkspace:agent);
            var select=typeof(ConversationViewModel).GetMethod("SelectProjectAsync")!;
            Assert.NotNull(select);
            await (Task)select.Invoke(vm,[@"D:\Project-A"] )!;
            vm.InputText="draft A question";
            await (Task)select.Invoke(vm,[@"D:\Project-B"] )!;
            Assert.Equal("",vm.InputText);vm.InputText="draft B question";
            await (Task)select.Invoke(vm,[@"D:\Project-A"] )!;
            Assert.Equal("draft A question",vm.InputText);
            var mode=typeof(ConversationViewModel).GetProperty("IsBoundedEditMode")!;Assert.NotNull(mode);
            mode.SetValue(vm,true);Assert.Equal("",vm.InputText);
            vm.InputText="draft A edit";mode.SetValue(vm,false);Assert.Equal("draft A question",vm.InputText);
            mode.SetValue(vm,true);Assert.Equal("draft A edit",vm.InputText);
            Assert.False((bool)typeof(ConversationViewModel).GetProperty("CanEditSelectedProject")!.GetValue(vm)!);
            Assert.False((bool)typeof(ConversationViewModel).GetProperty("ShowLiteralIntent")!.GetValue(vm)!);
        });
    }
    private sealed class DeniedWorkflow : IAgentWorkspaceWorkflow
    {
        public AgentWorkspaceSelection Select(string root)=>throw new InvalidOperationException("Project not reviewed.");
        public Task<AgentResult> RunAsync(AgentWorkspaceSelection selection,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>throw new InvalidOperationException("Unreviewed execution forbidden.");
    }
}
