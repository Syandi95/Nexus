using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.Contracts.AiCheck;
using Nexus.Conversation.Manager;
using Nexus.Conversation.Storage;
using Nexus.Foundation.Config;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateBSharedRuntimeLiveTests(ITestOutputHelper output)
{
    [GateBProbeFact]
    public async Task ProductionAgent_BorrowsOneRuntime_IsolatesSessionsCancellationAndLifecycle()
    {
        Assert.Empty(AlivePids());
        var parent=Environment.GetEnvironmentVariable("NEXUS_GATE_B_EVIDENCE")!;
        Assert.True(Directory.Exists(parent));
        var directory=Path.Combine(parent,"consumer-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services=ArchitectureGovernanceTests.CreateProductionServices();
        var config=new ConfigService(Path.Combine(directory,"fixture-config.json"));
        config.SaveConfig(new AppConfigRecord
        {
            ModelRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_MODEL_ROOT")!,
            EngineRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_ENGINE_ROOT")!
        });
        services.RemoveAll<IConfigService>(); services.AddSingleton<IConfigService>(config);
        var storage=new ConversationStorageService();
        typeof(ConversationStorageService).GetField("_rootPath",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(storage,Path.Combine(directory,"chat-fixture"));
        services.RemoveAll<IConversationStorageService>(); services.AddSingleton<IConversationStorageService>(storage);
        var observer=new RequestObserver();
        services.RemoveAll<HttpClient>(); services.AddSingleton(new HttpClient(observer));
        await using var provider=services.BuildServiceProvider();
        var workflow=provider.GetRequiredService<ConversationRuntimeWorkflow>();
        var server=provider.GetRequiredService<ILlamaServerManager>();
        var agent=provider.GetRequiredService<INexusAgent>();
        var manager=provider.GetRequiredService<IConversationManagerService>();
        var catalog=provider.GetRequiredService<IModelCatalogService>();
        var evidence=new List<object>();
        ModelCatalogItem? original=null;
        try
        {
            var inactive=await agent.ExecuteAsync(new AgentRequest("inactive","Reply OK."),CancellationToken.None);
            Assert.Equal(AgentRunOutcome.Rejected,inactive.Outcome);
            Assert.Empty(AlivePids());
            original=await workflow.StartActiveModelAsync();
            var pid=RequirePid(server);
            Assert.Same(workflow,provider.GetRequiredService<IConversationRuntimeWorkflow>());
            Assert.Same(workflow,Field(agent,"_runtime"));
            Assert.Same(server,Field(agent,"_server"));
            Assert.Same(provider.GetRequiredService<ILlamaClientService>(),Field(provider.GetRequiredService<IAiCheckJudgeService>(),"_llamaClient"));
            await manager.NewConversationAsync("fixture",original.Id,"llama");
            await manager.AddMessageAsync("user","Chat-owned fixture state must not be used by Agent.");
            var conversationBefore=JsonSerializer.Serialize(manager.ActiveConversation);
            var chatPath=storage.GetJsonPath(manager.ActiveConversation!.Id);
            var bytesBefore=await File.ReadAllBytesAsync(chatPath);

            const string prompt="Jawab hanya dua kata: sistem siap.";
            var sw=Stopwatch.StartNew();
            await Chat(workflow,prompt,CancellationToken.None);
            evidence.Add(new { mode="ChatWithoutAgentRun",durationMs=sw.ElapsedMilliseconds,pid });
            sw.Restart();
            var alone=await agent.ExecuteAsync(new AgentRequest("isolated-one",prompt),CancellationToken.None);
            Assert.Equal(AgentRunOutcome.Completed,alone.Outcome);
            Assert.Equal(pid,alone.Context.Model!.Pid);
            Assert.Equal(RuntimeHelpers.GetHashCode(server),alone.Context.Model.RuntimeIdentity);
            evidence.Add(new { mode="AgentAlone",durationMs=sw.ElapsedMilliseconds,alone.Context,alone.Outcome });
            sw.Restart();
            var observerCount=observer.Count;
            var coexisting=agent.ExecuteAsync(new AgentRequest("isolated-two",prompt),CancellationToken.None);
            await Until(()=>observer.Count>observerCount);
            await Chat(workflow,prompt,CancellationToken.None);
            var coexistResult=await coexisting;
            Assert.Equal(AgentRunOutcome.Completed,coexistResult.Outcome);
            Assert.NotEqual(alone.Context.RunId,coexistResult.Context.RunId);
            Assert.Equal("isolated-two",coexistResult.Context.SessionId);
            evidence.Add(new { mode="ChatWithOneAgentRun",durationMs=sw.ElapsedMilliseconds,coexistResult.Context,coexistResult.Outcome,pid=RequirePid(server) });

            using(var cancelAgent=new CancellationTokenSource())
            {
                var count=observer.Count;
                var activeAgent=agent.ExecuteAsync(new AgentRequest("cancel-agent","Tuliskan angka 1 sampai 300 satu per baris.",MaxOutputTokens:256),cancelAgent.Token);
                await Until(()=>observer.Count>count);
                var chat=Chat(workflow,prompt,CancellationToken.None);
                await Until(()=>observer.Count>=count+2);
                cancelAgent.Cancel();
                var cancelled=await activeAgent.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(AgentRunOutcome.Cancelled,cancelled.Outcome);
                await chat.WaitAsync(TimeSpan.FromSeconds(90));
                evidence.Add(new { mode="CancelAgentChatSurvives",cancelled.Context,cancelled.Outcome,cancelled.ErrorCategory,pid=RequirePid(server) });
            }
            using(var cancelChat=new CancellationTokenSource())
            {
                var firstToken=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var chat=Chat(workflow,"Tuliskan angka 1 sampai 300 satu per baris.",cancelChat.Token,firstToken,256);
                await firstToken.Task.WaitAsync(TimeSpan.FromSeconds(60));
                var count=observer.Count;
                var activeAgent=agent.ExecuteAsync(new AgentRequest("cancel-chat-survivor",prompt),CancellationToken.None);
                await Until(()=>observer.Count>count);
                cancelChat.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>chat);
                var survived=await activeAgent.WaitAsync(TimeSpan.FromSeconds(90));
                Assert.Equal(AgentRunOutcome.Completed,survived.Outcome);
                evidence.Add(new { mode="CancelChatAgentSurvives",survived.Context,survived.Outcome,pid=RequirePid(server) });
            }
            Assert.Equal(conversationBefore,JsonSerializer.Serialize(manager.ActiveConversation));
            Assert.Equal(bytesBefore,await File.ReadAllBytesAsync(chatPath));
            evidence.Add(new { mode="SessionIsolation",chatId=manager.ActiveConversation!.Id,unchanged=true });

            var requests=observer.Count;
            var navigating=agent.ExecuteAsync(new AgentRequest("navigation","Tuliskan angka 1 sampai 300 satu per baris.",MaxOutputTokens:256),CancellationToken.None);
            await Until(()=>observer.Count>requests);
            await workflow.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var stopped=await navigating;
            Assert.Equal("WorkspaceRuntimeDeactivated",stopped.ErrorCategory);
            Assert.Empty(AlivePids());
            evidence.Add(new { mode="NavigationDeactivatesAgent",stopped.Context,stopped.Outcome,stopped.ErrorCategory });

            await workflow.StartActiveModelAsync();
            requests=observer.Count;
            var switching=agent.ExecuteAsync(new AgentRequest("switch","Tuliskan angka 1 sampai 300 satu per baris.",MaxOutputTokens:256),CancellationToken.None);
            await Until(()=>observer.Count>requests);
            var other=catalog.CurrentSnapshot!.Models.First(m=>m.IsAvailable && m.Id!=original.Id);
            await workflow.SwitchModelAsync(other);
            var switched=await switching;
            Assert.Equal("WorkspaceRuntimeDeactivated",switched.ErrorCategory);
            Assert.Equal(other.ResolvedGgufPath,server.RuntimeModelPath);
            evidence.Add(new { mode="ModelSwitchDeactivatesAgent",switched.Context,switched.Outcome,switched.ErrorCategory,nextModel=other.Id,pid=RequirePid(server) });
            await workflow.SwitchModelAsync(original);
            var after=await agent.ExecuteAsync(new AgentRequest("after-switch",prompt),CancellationToken.None);
            Assert.Equal(AgentRunOutcome.Completed,after.Outcome);
            Assert.Equal(original.Id,after.Context.Model!.ModelId);
            Assert.Equal(conversationBefore,JsonSerializer.Serialize(manager.ActiveConversation));
        }
        finally
        {
            if(original is not null && catalog.CurrentSnapshot?.ActiveModel?.Id!=original.Id)
                await workflow.SwitchModelAsync(original);
            await workflow.StopAsync();
            Assert.Null(server.OwnedPid);
            Assert.Empty(AlivePids());
            File.WriteAllText(Path.Combine(directory,"consumer-evidence.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions { WriteIndented=true }));
            output.WriteLine($"GATE_B_CONSUMER {directory}");
        }
    }

    private static async Task Chat(ConversationRuntimeWorkflow workflow,string prompt,CancellationToken token,TaskCompletionSource? firstToken=null,int maxTokens=64)
    {
        var content=new System.Text.StringBuilder();
        await foreach(var chunk in workflow.StreamAsync([new ChatMessagePayload { Role="system",Content="Answer the user briefly." },new ChatMessagePayload { Role="user",Content=prompt }],false,
            new LlamaGenerationOptions { MaxTokens=maxTokens,Temperature=0 },token))
        {
            content.Append(chunk.Content);
            if(chunk.Content.Length>0) firstToken?.TrySetResult();
        }
        Assert.NotEmpty(content.ToString());
    }
    private static async Task Until(Func<bool> condition)
    {
        var sw=Stopwatch.StartNew();
        while(!condition()) { Assert.True(sw.Elapsed<TimeSpan.FromSeconds(20),"Request was not observed."); await Task.Delay(10); }
    }
    private static object? Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
    private static int RequirePid(ILlamaServerManager server)
    {
        var id=Assert.Single(AlivePids()); Assert.Equal(server.OwnedPid,id); return id;
    }
    private static int[] AlivePids()
    {
        var ids=new List<int>();
        foreach(var p in Process.GetProcessesByName("llama-server")) using(p)
        { try { if(!p.HasExited) ids.Add(p.Id); } catch(InvalidOperationException) { } }
        return ids.ToArray();
    }
    private sealed class RequestObserver : DelegatingHandler
    {
        private int _count;
        public int Count=>Volatile.Read(ref _count);
        public RequestObserver():base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("127.0.0.1",request.RequestUri!.Host);
            Assert.Equal(8080,request.RequestUri.Port);
            Interlocked.Increment(ref _count);
            return base.SendAsync(request,token);
        }
    }
}
