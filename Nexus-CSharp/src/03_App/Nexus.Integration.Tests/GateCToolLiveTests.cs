using System.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Nexus.AI.EditingLite.Services;
using Nexus.Foundation.Logging;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.App.ViewModels;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Storage;
using Nexus.Foundation.Config;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateCLiveFactAttribute : FactAttribute
{
    public GateCLiveFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXUS_GATE_C_LIVE")!="1") Skip="Explicit Gate C shared-model tool proof fixtures required.";
    }
}

public sealed class GateCToolLiveTests(ITestOutputHelper output)
{
    [GateCLiveFact]
    public Task NativeReadOnlyModelSnapshot_UsesPolicyAndMatchingIdWithoutChangingChat()=>ExecuteProof(false);

    internal async Task ExecuteProof(bool gateD,bool gateE=false,bool gateE1=false,bool gateF=false,bool gateG=false,bool probeOnly=false)
    {
        Assert.Empty(AlivePids());
        var parent=Environment.GetEnvironmentVariable(gateG ? "NEXUS_GATE_G_EVIDENCE" : gateF ? "NEXUS_GATE_F_EVIDENCE" : gateE1 ? "NEXUS_GATE_E1_EVIDENCE" : gateE ? "NEXUS_GATE_E_EVIDENCE" : gateD ? "NEXUS_GATE_D_EVIDENCE" : "NEXUS_GATE_C_EVIDENCE")!;
        Assert.True(Directory.Exists(parent));
        var directory=Path.Combine(parent,"live-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services=ArchitectureGovernanceTests.CreateProductionServices();
        using var sourceFixture=gateE1 || gateF || gateG ? await SourcePlanTestWorkspace.Create() : null;
        if(gateG) sourceFixture!.ObserveCompiler();
        var sourceCatalog=sourceFixture is null ? null : new SourcePlanCatalog(sourceFixture.Profile);
        GateE1ProofApproval? sourceApproval=null;
        GateFProofApproval? repairApproval=null;
        if(sourceFixture is not null)
        {
            services.RemoveAll<SourcePlanCatalog>(); services.AddSingleton(sourceCatalog!);
            services.RemoveAll<EditingWorkflowService>(); services.AddSingleton(sourceFixture.Editing);
            services.RemoveAll<IWriteApprovalProvider>();
            if(gateF || gateG) { repairApproval=new GateFProofApproval(gateG); services.AddSingleton<IWriteApprovalProvider>(repairApproval); }
            else { sourceApproval=new GateE1ProofApproval(); services.AddSingleton<IWriteApprovalProvider>(sourceApproval); }
        }
        string? writeRoot=null;
        GateEProofApproval? writeApproval=null;
        var mutationEvents=new System.Collections.Concurrent.ConcurrentQueue<string>();
        if(gateE)
        {
            writeRoot=Path.Combine(directory,"approved-workspace"); Directory.CreateDirectory(writeRoot);
            File.WriteAllText(Path.Combine(writeRoot,"sample.txt"),"hello\r\n",new System.Text.UTF8Encoding(false));
            services.RemoveAll<ApprovedTextWorkspace>(); services.AddSingleton(new ApprovedTextWorkspace(writeRoot));
            writeApproval=new GateEProofApproval(); services.RemoveAll<IWriteApprovalProvider>(); services.AddSingleton<IWriteApprovalProvider>(writeApproval);
            var logger=new Mock<ILoggerService>();
            logger.Setup(l=>l.LogInfo(It.IsAny<string>(),It.IsAny<string>())).Callback<string,string>((category,message)=>{ if(category.StartsWith("Agent",StringComparison.Ordinal)) mutationEvents.Enqueue(message); });
            services.RemoveAll<ILoggerService>(); services.AddSingleton(logger.Object);
        }
        var config=new ConfigService(Path.Combine(directory,"fixture-config.json"));
        config.SaveConfig(new AppConfigRecord { ModelRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_MODEL_ROOT")!,EngineRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_ENGINE_ROOT")! });
        services.RemoveAll<IConfigService>(); services.AddSingleton<IConfigService>(config);
        var storage=new ConversationStorageService();
        typeof(ConversationStorageService).GetField("_rootPath",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(storage,Path.Combine(directory,"chat-fixture"));
        services.RemoveAll<IConversationStorageService>(); services.AddSingleton<IConversationStorageService>(storage);
        var observer=new Observer();
        services.RemoveAll<HttpClient>(); services.AddSingleton(new HttpClient(observer));
        await using var provider=services.BuildServiceProvider();
        var workflow=provider.GetRequiredService<ConversationRuntimeWorkflow>();
        var server=provider.GetRequiredService<ILlamaServerManager>();
        var agent=provider.GetRequiredService<INexusAgent>();
        var manager=provider.GetRequiredService<IConversationManagerService>();
        var records=new List<object>();
        try
        {
            var model=await workflow.StartActiveModelAsync();
            var pid=Assert.Single(AlivePids());
            Assert.Equal(pid,server.OwnedPid);
            var registry=provider.GetRequiredService<ToolRegistry>();
            var capability=provider.GetRequiredService<NativeToolCapability>();
            Assert.Equal(4,registry.Descriptors.Count);
            var modelDescriptor=Assert.Single(registry.Descriptors,d=>d.Name==GetModelSnapshotTool.ToolName);
            Assert.Equal(TimeSpan.FromSeconds(2),modelDescriptor.Timeout);
            Assert.Equal(2048,modelDescriptor.MaxResultSize);
            Assert.All(registry.Descriptors.Where(d=>d.Risk==ToolRisk.ReadOnly),d=>{ Assert.True(d.ReadOnly); Assert.Equal(ToolRisk.ReadOnly,d.Risk); });
            Assert.Equal(ToolRisk.SafeWrite,Assert.Single(registry.Descriptors,d=>d.Name==ApplyTextPatchTool.ToolName).Risk);
            var currentIndex=Path.Combine(config.GetConfig().ModelRootFolder,"AI Workspace","model-index.json");
            var indexBefore=await File.ReadAllBytesAsync(currentIndex);
            var configBefore=await File.ReadAllBytesAsync(Path.Combine(directory,"fixture-config.json"));
            var designPath=Environment.GetEnvironmentVariable("NEXUS_TEST_DESIGN_PATH");
            var designHash=designPath is null ? null : SHA256.HashData(await File.ReadAllBytesAsync(designPath));
            await manager.NewConversationAsync("fixture",model.Id,"llama");
            await manager.AddMessageAsync("user","Chat owned context must stay private and unchanged.");
            var activeBefore=JsonSerializer.Serialize(manager.ActiveConversation);
            var detailPath=storage.GetJsonPath(manager.ActiveConversation!.Id);
            var detailBefore=await File.ReadAllBytesAsync(detailPath);

            await WpfAppHost.RunAsync(async () =>
            {
                var chat=provider.GetRequiredService<ConversationViewModel>();
                await chat.LoadConversationsAsync();
                var chatBefore=JsonSerializer.Serialize(chat.Messages.Select(m=>new { m.Role,m.Content }));
                var selectedBefore=chat.SelectedConversation?.Id;
                var timer=Stopwatch.StartNew();
                var noTool=await agent.ExecuteAsync(new AgentRequest("no-tool","Reply READY only."),CancellationToken.None);
                Assert.Equal(AgentRunOutcome.Completed,noTool.Outcome);
                Assert.True(noTool.Verification!.Passed);
                Assert.Equal(1,noTool.Context.Progress!.ModelTurnCount);
                records.Add(new { scenario="AgentNoTool",durationMs=timer.ElapsedMilliseconds,noTool.Context,fingerprintComputations=capability.FingerprintComputationCount });
                observer.Requests.Clear();
                timer.Restart();
                var result=await agent.ExecuteAsync(new AgentRequest("tool-proof","Tell me which Nexus model is currently active. Use the available tool rather than guessing. Return the exact model ID.",
                    MaxOutputTokens:128,Timeout:TimeSpan.FromMinutes(2),EnableReadOnlyToolCall:true),CancellationToken.None);
                records.Add(new { scenario="OneToolDiagnostic",result.Outcome,result.ErrorCategory,result.Verification,result.Context });
                Assert.Equal(AgentRunOutcome.Completed,result.Outcome);
                Assert.True(result.Verification!.Passed);
                var call=Assert.IsType<ToolDispatchResult>(result.Tool);
                Assert.Equal(GetModelSnapshotTool.ToolName,call.ToolName);
                Assert.Equal("Valid",call.ValidationResult);
                Assert.True(call.Policy.Allowed);
                Assert.Equal("AllowedReadOnlyTool",call.Policy.ReasonCode);
                Assert.Equal(ToolOutcome.Success,call.Result.Outcome);
                Assert.Equal(model.Id,call.Result.Data!.Value.GetProperty("modelId").GetString());
                Assert.Equal("Ready",call.Result.Data.Value.GetProperty("runtimeState").GetString());
                Assert.True(call.ResultLength<=modelDescriptor.MaxResultSize);
                var envelope=ToolExecutor.SerializeResult(call.Result);
                Assert.DoesNotContain(model.ResolvedGgufPath!,envelope,StringComparison.OrdinalIgnoreCase);
                Assert.Contains(model.Id,result.Content,StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2,observer.Requests.Count);
                var follow=JsonNode.Parse(observer.Requests.Last())!["messages"]!.AsArray();
                Assert.Equal(call.ToolCallId,follow[^1]!["tool_call_id"]!.GetValue<string>());
                Assert.Equal(call.ToolCallId,follow[^2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
                Assert.Equal(envelope,follow[^1]!["content"]!.GetValue<string>());
                Assert.Equal(1,capability.FingerprintComputationCount);
                records.Add(new { scenario="AgentOneReadOnlyTool",durationMs=timer.ElapsedMilliseconds,result.Context,tool=call,finalMatchesDeterministicModel=true,
                    fingerprintComputations=capability.FingerprintComputationCount,firstFingerprintCost=true });

                if(gateD || gateE)
                {
                    observer.Requests.Clear(); timer.Restart();
                    var two=await agent.ExecuteAsync(new AgentRequest("two-sources",
                        "Read the current model identity using the model snapshot first. Then check current workspace readiness using the runtime snapshot in a separate turn. Return the exact model ID, runtime state and readiness facts as JSON.",
                        MaxOutputTokens:128,EnableReadOnlyToolCall:true,Verification:AgentVerificationRequirement.ModelAndRuntime),CancellationToken.None);
                    records.Add(new { scenario="TwoToolDiagnostic",two.Outcome,two.ErrorCategory,two.Verification,two.Context });
                    Assert.Equal(AgentRunOutcome.Completed,two.Outcome); Assert.True(two.Verification!.Passed);
                    Assert.Equal(AgentRunState.Completed,two.Context.Progress!.State);
                    Assert.Equal(3,two.Context.Progress.ModelTurnCount); Assert.Equal(2,two.Context.Progress.ToolCallCount);
                    Assert.Equal(GetModelSnapshotTool.ToolName,two.ToolDispatches![0].ToolName);
                    Assert.Equal(GetRuntimeSnapshotTool.ToolName,two.ToolDispatches[1].ToolName);
                    Assert.All(two.ToolDispatches,d=>Assert.Equal(ToolOutcome.Success,d.Result.Outcome));
                    Assert.Equal(3,observer.Requests.Count);
                    var twoFollow=JsonNode.Parse(observer.Requests.Last())!["messages"]!.AsArray();
                    foreach(var dispatch in two.ToolDispatches)
                    {
                        Assert.Contains(twoFollow,m=>m?["role"]?.GetValue<string>()=="tool" && m["tool_call_id"]?.GetValue<string>()==dispatch.ToolCallId && m["content"]?.GetValue<string>()==ToolExecutor.SerializeResult(dispatch.Result));
                        Assert.Contains(twoFollow,m=>m?["role"]?.GetValue<string>()=="assistant" && m["tool_calls"]?[0]?["id"]?.GetValue<string>()==dispatch.ToolCallId);
                    }
                    Assert.True(JsonNode.Parse(two.Content)!["ready"]!.GetValue<bool>());
                    Assert.Equal(1,capability.FingerprintComputationCount);
                    records.Add(new { scenario="AgentTwoReadOnlySources",durationMs=timer.ElapsedMilliseconds,two.Context,two.ToolDispatches,two.Verification,
                        fingerprintComputations=capability.FingerprintComputationCount,cacheHits=capability.CacheHitCount,firstFingerprintCost=false });
                }

                if(gateE)
                {
                    var target=Path.Combine(writeRoot!,"sample.txt"); var snapshot=new FileSnapshotService();
                    var original=await File.ReadAllBytesAsync(target); var beforeHash=snapshot.ComputeSha256Hash(target);
                    observer.Requests.Clear(); timer.Restart();
                    var mutation=await agent.ExecuteAsync(new AgentRequest("safe-write-proof","Propose and apply the exact bounded literal text patch. Obtain independent approval, then report the verified operation facts.",
                        MaxOutputTokens:256,EnableReadOnlyToolCall:true,Verification:AgentVerificationRequirement.SafeTextChange,
                        WriteIntent:new("sample.txt",beforeHash,"hello","hello nexus")),CancellationToken.None);
                    records.Add(new { scenario="SafeWriteDiagnostic",mutation.Outcome,mutation.ErrorCategory,mutation.Verification,mutation.Context });
                    Assert.Equal(AgentRunOutcome.Completed,mutation.Outcome); Assert.True(mutation.Verification!.Passed);
                    var operation=Assert.Single(mutation.Context.Progress!.WriteOperations);
                    Assert.True(operation.CommitReached); Assert.True(operation.Verified); Assert.NotNull(operation.ApprovalId);
                    Assert.Equal("hello nexus\r\n",await File.ReadAllTextAsync(target));
                    Assert.Equal(operation.DesiredHash,snapshot.ComputeSha256Hash(target));
                    Assert.Equal(original,await File.ReadAllBytesAsync(Path.Combine(writeRoot!,operation.BackupRef!)));
                    Assert.Equal(1,writeApproval!.Requests); Assert.Equal(1,mutation.Context.Progress.WriteOperationCount);
                    Assert.Equal(2,mutation.Context.Progress.ModelTurnCount); Assert.Equal(1,mutation.Context.Progress.ToolCallCount);
                    Assert.Equal(2,observer.Requests.Count); Assert.Empty(Directory.GetFiles(writeRoot!,"*.tmp"));
                    var finalMessages=JsonNode.Parse(observer.Requests.Last())!["messages"]!.AsArray();
                    var dispatch=Assert.Single(mutation.ToolDispatches!);
                    Assert.Equal(dispatch.ToolCallId,operation.ToolCallId);
                    Assert.Equal(dispatch.ToolCallId,finalMessages[^1]!["tool_call_id"]!.GetValue<string>());
                    Assert.Equal(dispatch.ToolCallId,finalMessages[^2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
                    Assert.Equal(1,capability.FingerprintComputationCount);
                    Assert.DoesNotContain(mutationEvents,m=>m.Contains("hello nexus",StringComparison.Ordinal)||m.Contains("- hello",StringComparison.Ordinal));
                    records.Add(new { scenario="AgentOneApprovedSafeWrite",durationMs=timer.ElapsedMilliseconds,mutation.Context,mutation.ToolDispatches,mutation.Verification,
                        beforeHash,desiredHash=operation.DesiredHash,actualHash=snapshot.ComputeSha256Hash(target),backupHash=snapshot.ComputeSha256Hash(Path.Combine(writeRoot!,operation.BackupRef!)),
                        approvalBoundToExactProposal=true,fingerprintComputations=capability.FingerprintComputationCount,firstFingerprintCost=false });
                }

                if(gateE1)
                {
                    var edits=new[] { "Product/Alpha.cs","Product/Beta.cs" }.Select(t=>new SourceFileEdit(t,sourceFixture!.Hash(t),"return 1;","return 2;")).ToImmutableArray();
                    observer.Requests.Clear(); timer.Restart();
                    var source=await agent.ExecuteAsync(new AgentRequest("source-plan-proof","Apply the exact approved source plan and return verified plan facts.",
                        MaxOutputTokens:256,Timeout:TimeSpan.FromSeconds(120),EnableReadOnlyToolCall:true,Verification:AgentVerificationRequirement.SourcePlan,
                        SourceIntent:new(sourceFixture!.Profile.Id,edits)),CancellationToken.None);
                    records.Add(new { scenario="SourcePlanDiagnostic",source.Outcome,source.ErrorCategory,source.Context,source.Verification });
                    Assert.Equal(AgentRunOutcome.Completed,source.Outcome); Assert.True(source.Verification!.Passed);
                    var facts=source.Context.Progress!.SourceResult!;
                    Assert.Equal(SourcePlanOutcome.Success,facts.Outcome); Assert.True(facts.Build!.Passed); Assert.Equal(3,facts.Tests!.TestsPassed);
                    Assert.Equal(2,facts.Operations.Length); Assert.Equal(1,sourceApproval!.Requests);
                    Assert.Equal(1,source.Context.Progress.SourcePlanCount); Assert.Equal(0,source.Context.Progress.WriteOperationCount);
                    Assert.Equal(1,source.Context.Progress.ToolCallCount); Assert.Equal(2,source.Context.Progress.ModelTurnCount);
                    Assert.All(source.Context.Progress.SourcePlan!.Entries,e=>Assert.Equal(e.DesiredHash,sourceFixture.Hash(e.Target)));
                    Assert.Equal(2,observer.Requests.Count); Assert.Equal(1,capability.FingerprintComputationCount);
                    records.Add(new { scenario="AgentApprovedSourcePlan",durationMs=timer.ElapsedMilliseconds,source.Context,facts,sameRuntimePid=pid,sourceApproval.Requests });
                }
                if(gateF || gateG)
                {
                    var probe=await GateFRepairLiveProof.Probe(sourceFixture!,workflow,server,registry,repairApproval!,records,gateG);
                    if(probeOnly) return;
                    sourceCatalog!.EnableRepair(probe);
                    observer.Requests.Clear(); timer.Restart();
                    var repair=await agent.ExecuteAsync(GateFRepairLiveProof.Request(sourceFixture!,gateG),CancellationToken.None);
                    records.Add(new { scenario="GateFRepairDiagnostic",repair.Outcome,repair.ErrorCategory,repair.Context,repair.Verification });
                    Assert.Equal(AgentRunOutcome.Completed,repair.Outcome); Assert.True(repair.Verification!.Passed);
                    var p=repair.Context.Progress!;
                    Assert.Equal(2,p.Attempts.Count); Assert.Equal(1,p.RepairPlanCount); Assert.Equal(2,p.BuildStartCount); Assert.Equal(gateG ? 1 : 2,p.TestStartCount);
                    Assert.Equal(3,p.ModelTurnCount); Assert.Equal(2,p.ToolCallCount); Assert.Equal(2,repairApproval!.Calls);
                    Assert.Equal(3,observer.Requests.Count);
                    foreach(var request in observer.Requests.Take(2))
                    {
                        var body=JsonNode.Parse(request)!;
                        Assert.Equal("required",body["tool_choice"]!.GetValue<string>());
                        Assert.Equal(ApplySourcePlanTool.ToolName,Assert.Single(body["tools"]!.AsArray())!["function"]!["name"]!.GetValue<string>());
                    }
                    Assert.Equal("none",JsonNode.Parse(observer.Requests.Last())!["tool_choice"]!.GetValue<string>());
                    Assert.True(p.Attempts[0].Result.BaselineVerified); Assert.Equal(gateG ? SourcePlanOutcome.BuildFailed : SourcePlanOutcome.TestFailed,p.Attempts[0].Result.Outcome);
                    if(gateG) { Assert.Equal("CS0029",repair.DiagnosticId); Assert.Equal("CompilerError",repair.Attempt1FailureCategory); Assert.Null(p.Attempts[0].Result.Tests); Assert.True(repair.Duration<TimeSpan.FromSeconds(120)); }
                    Assert.Equal(SourcePlanOutcome.Success,p.Attempts[1].Result.Outcome); Assert.True(p.FinalSuccessAccepted);
                    Assert.All(p.SourcePlan!.Entries,e=>Assert.Equal(e.DesiredHash,sourceFixture!.Hash(e.Target)));
                    records.Add(new { scenario=gateG ? "AgentBoundCompilerRepair" : "AgentOneBoundedRepair",durationMs=timer.ElapsedMilliseconds,repair.Context,repair.Verification,
                        facts=p.Attempts.Select(a=>new { a.AttemptNumber,a.PlanId,a.ParentPlanId,a.Result.Outcome,a.Result.Elapsed,a.Result.BaselineVerified,
                            build=a.Result.Build?.Elapsed,tests=a.Result.Tests?.Elapsed }),sameRuntimePid=pid });
                }
                timer.Restart();
                await Chat(workflow,"Reply READY only.",CancellationToken.None);
                records.Add(new { scenario="ChatNormal",durationMs=timer.ElapsedMilliseconds });
                timer.Restart();
                var proof=agent.ExecuteAsync(new AgentRequest("coexisting-tool","Use the available tool to report the active model ID.",MaxOutputTokens:128,EnableReadOnlyToolCall:true),CancellationToken.None);
                await Chat(workflow,"Reply READY only.",CancellationToken.None);
                var coexisting=await proof;
                Assert.Equal(AgentRunOutcome.Completed,coexisting.Outcome);
                records.Add(new { scenario="ChatWithOneToolAgent",durationMs=timer.ElapsedMilliseconds,coexisting.Context,fingerprintComputations=capability.FingerprintComputationCount,firstFingerprintCost=false });

                using(var cancelAgent=new CancellationTokenSource())
                {
                    var count=observer.Count;
                    var pending=agent.ExecuteAsync(new AgentRequest("cancel-agent","Use the tool then list all numbers 1 through 300.",MaxOutputTokens:256,EnableReadOnlyToolCall:true),cancelAgent.Token);
                    await Until(()=>observer.Count>count);
                    var concurrentChat=Chat(workflow,"Reply READY only.",CancellationToken.None);
                    cancelAgent.Cancel();
                    Assert.Equal(AgentRunOutcome.Cancelled,(await pending).Outcome);
                    await concurrentChat.WaitAsync(TimeSpan.FromSeconds(90));
                }
                using(var cancelChat=new CancellationTokenSource())
                {
                    var firstToken=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pendingChat=Chat(workflow,"List all numbers 1 through 300.",cancelChat.Token,firstToken);
                    await firstToken.Task.WaitAsync(TimeSpan.FromSeconds(60));
                    var count=observer.Count;
                    var pending=agent.ExecuteAsync(new AgentRequest("cancel-chat","Use the tool to report active model ID.",MaxOutputTokens:128,EnableReadOnlyToolCall:true),CancellationToken.None);
                    await Until(()=>observer.Count>count);
                    cancelChat.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pendingChat);
                    Assert.Equal(AgentRunOutcome.Completed,(await pending).Outcome);
                }
                Assert.Equal(chatBefore,JsonSerializer.Serialize(chat.Messages.Select(m=>new { m.Role,m.Content })));
                Assert.Equal(selectedBefore,chat.SelectedConversation?.Id);
                Assert.Equal(activeBefore,JsonSerializer.Serialize(manager.ActiveConversation));
                Assert.Equal(detailBefore,await File.ReadAllBytesAsync(detailPath));
                Assert.Equal(indexBefore,await File.ReadAllBytesAsync(currentIndex));
                Assert.Equal(configBefore,await File.ReadAllBytesAsync(Path.Combine(directory,"fixture-config.json")));
                if(designHash is not null) Assert.Equal(designHash,SHA256.HashData(await File.ReadAllBytesAsync(designPath!)));
                Assert.Equal(1,capability.FingerprintComputationCount);
                Assert.Equal(pid,Assert.Single(AlivePids()));
                records.Add(new { scenario="CancellationAndSessionIsolation",pid,chatUnchanged=true,storedDetailUnchanged=true,configAndCatalogUnchanged=true,designFixtureUnchanged=true,
                    fingerprintComputations=capability.FingerprintComputationCount,cacheHits=capability.CacheHitCount });
            });
        }
        finally
        {
            await workflow.StopAsync();
            Assert.Null(server.OwnedPid);
            Assert.Empty(AlivePids());
            File.WriteAllText(Path.Combine(directory,"tool-proof.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions { WriteIndented=true }));
            File.WriteAllText(Path.Combine(directory,"synthetic-native-responses.json"),JsonSerializer.Serialize(observer.Responses,new JsonSerializerOptions { WriteIndented=true }));
            if(gateE) File.WriteAllText(Path.Combine(directory,"mutation-events.json"),JsonSerializer.Serialize(mutationEvents,new JsonSerializerOptions { WriteIndented=true }));
            output.WriteLine($"{(gateG ? "GATE_G_LIVE" : gateF ? "GATE_F_LIVE" : gateE1 ? "GATE_E1_LIVE" : gateE ? "GATE_E_LIVE" : gateD ? "GATE_D_LIVE" : "GATE_C_LIVE")} {directory}");
        }
    }

    private static async Task Chat(ConversationRuntimeWorkflow workflow,string prompt,CancellationToken token,TaskCompletionSource? first=null)
    {
        var content=new System.Text.StringBuilder();
        await foreach(var chunk in workflow.StreamAsync([new ChatMessagePayload { Role="user",Content=prompt }],false,new LlamaGenerationOptions { MaxTokens=256,Temperature=0 },token))
        { content.Append(chunk.Content); if(chunk.Content.Length>0) first?.TrySetResult(); }
        Assert.NotEmpty(content.ToString());
    }
    private static async Task Until(Func<bool> condition)
    { var timer=Stopwatch.StartNew(); while(!condition()) { Assert.True(timer.Elapsed<TimeSpan.FromSeconds(30)); await Task.Delay(10); } }
    private static int[] AlivePids()
    {
        var ids=new List<int>();
        foreach(var p in Process.GetProcessesByName("llama-server")) using(p)
        { try { if(!p.HasExited) ids.Add(p.Id); } catch(InvalidOperationException) { } }
        return ids.ToArray();
    }
    private sealed class Observer : DelegatingHandler
    {
        private int _count;
        public int Count=>Volatile.Read(ref _count);
        public List<string> Requests { get; }=[];
        public List<string> Responses { get; }=[];
        public Observer():base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("127.0.0.1",request.RequestUri!.Host);
            Assert.Equal(8080,request.RequestUri.Port);
            var body=await request.Content!.ReadAsStringAsync(token);
            lock(Requests) Requests.Add(body);
            Interlocked.Increment(ref _count);
            var response=await base.SendAsync(request,token);
            if(JsonNode.Parse(body)?["stream"]?.GetValue<bool>()==false)
            {
                var raw=await response.Content.ReadAsStringAsync(token);
                lock(Responses) Responses.Add(raw);
            }
            return response;
        }
    }
}
