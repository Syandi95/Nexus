using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services;

public sealed record AgentWorkspaceSelection(SourceVerificationProfile Profile,
    ImmutableArray<FileSnapshot> Files,ReviewedProjectEditAuthority? ReviewedAuthority=null);

public interface IAgentWorkspaceWorkflow
{
    AgentWorkspaceSelection Select(string root);
    FileSnapshot ReadFileContext(AgentWorkspaceSelection selection,string target)=>throw new InvalidOperationException("Authorized file context unavailable.");
    AgentWorkspaceSelection AuthorizeReviewedEdit(AgentWorkspaceSelection selection,ProjectReadGrant grant)=>throw new InvalidOperationException("Reviewed edit authority unavailable.");
    Task<AgentResult> RunAsync(AgentWorkspaceSelection selection, string session, string prompt,
        ImmutableArray<SourceFileEdit> edits, DateTimeOffset deadline, Action<string> state,
        Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review, CancellationToken token);
}

/// <summary>UI admission/composition only. Existing Agent owns every execution.</summary>
public sealed class AgentWorkspaceWorkflow(ConversationRuntimeWorkflow runtime,
    ILlamaServerManager server, HttpClient http, EditingWorkflowService editing,
    NativeToolCapability capability, ILoggerService logger,ReviewedProjectCatalog? reviewed=null,TimeProvider? timeProvider=null) : IAgentWorkspaceWorkflow
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    public const string ReviewedRoot = @"D:\NEXUS\scratch\GateI-ReviewedWorkspace";
    public const string ReviewedManifestSha256 = "ba87c4167848e6b5c8b33b1b0f98d3de0a337a9a384ebea4e9271c00139935ea";
    private readonly SemaphoreSlim admission = new(1,1);
    private static string Hash(string path) { using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }

    public AgentWorkspaceSelection Select(string root)
        =>ReviewedProjectCatalog.Matches(root) && reviewed is not null?reviewed.Select(root):SelectPinned(root,ReviewedRoot,ReviewedManifestSha256,editing);
    internal (AgentWorkspaceSelection Selection,IDisposable Lease) PrepareReviewedPilot(CancellationToken cancellation)
        =>reviewed?.PreparePilot(cancellation)??throw new InvalidOperationException("Reviewed project admission unavailable.");
    public AgentWorkspaceSelection AuthorizeReviewedEdit(AgentWorkspaceSelection selection,ProjectReadGrant grant)
        =>reviewed?.Authorize(selection,grant)??throw new InvalidOperationException("Reviewed project admission unavailable.");
    public FileSnapshot ReadFileContext(AgentWorkspaceSelection selection,string target)
        =>ReadPinnedContext(selection,target,ReviewedRoot,ReviewedManifestSha256,editing);
    internal static FileSnapshot ReadPinnedContext(AgentWorkspaceSelection selection,string target,string hostRoot,string hostPin,EditingWorkflowService editing)
    {
        if(!new[] {"Product/Alpha.cs","Product/Beta.cs"}.Contains(target,StringComparer.Ordinal))throw new InvalidOperationException("File context target denied.");
        var current=SelectPinned(selection.Profile.Root,hostRoot,hostPin,editing);
        if(current.Profile.Digest!=selection.Profile.Digest)throw new InvalidOperationException("File context selection changed.");
        var index=Array.IndexOf(current.Profile.Targets.ToArray(),target);
        if(index<0 || selection.Files[index].Hash!=current.Files[index].Hash)throw new InvalidOperationException("File context snapshot changed.");
        return current.Files[index];
    }

    // Same admission checks used by isolated engineering fixtures. Runtime entry
    // supplies only the immutable production constants above, never model/UI pins.
    internal static AgentWorkspaceSelection SelectPinned(string root,string hostRoot,string hostPin,EditingWorkflowService editing)
    {
        var canonical=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!string.Equals(canonical,Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostRoot)),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workspace belum direview; I1 hanya mendukung project reviewed Alpha/Beta.");
        using var manifestLease=new SafePathLease(canonical,"nexus-reviewed-i1.json");
        using var manifestFile=manifestLease.OpenTarget();
        if(Convert.ToHexString(SHA256.HashData(manifestFile)).ToLowerInvariant()!=hostPin)
            throw new InvalidOperationException("Reviewed workspace manifest berubah.");
        manifestFile.Position=0;
        using var manifest=JsonDocument.Parse(manifestFile);
        var data=manifest.RootElement;
        if(data.GetProperty("contract").GetString()!="NEXUS-GATE-I-I1-REVIEWED-V1" || data.GetProperty("root").GetString()!=hostRoot)
            throw new InvalidOperationException("Workspace identity mismatch.");
        foreach(var file in data.GetProperty("files").EnumerateObject())
        {
            using var lease=new SafePathLease(canonical,file.Name);
            using var stream=lease.OpenTarget();
            if(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=file.Value.GetString())
                throw new InvalidOperationException("Reviewed baseline berubah: "+file.Name+". Review baru diperlukan.");
        }
        var sdk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","dotnet","dotnet.exe");
        var expectedSdkHash=data.GetProperty("sdk_sha256").GetString();
        if(Hash(sdk)!=expectedSdkHash)
        {
            var sideBySide80424=Path.Combine(Path.GetDirectoryName(sdk)!,"sdk","8.0.424","dotnet.dll");
            if(!string.Equals(canonical,Path.TrimEndingDirectorySeparator(Path.GetFullPath(ReviewedRoot)),StringComparison.OrdinalIgnoreCase) || !File.Exists(sideBySide80424))
                throw new InvalidOperationException("Reviewed SDK identity mismatch.");
        }
        var profile=new SourceVerificationProfile("gate-i-i1-reviewed-v1",canonical,sdk,["Product/Alpha.cs","Product/Beta.cs"]);
        return new(profile,profile.Targets.Select(t=>editing.ReadScopedSource(profile.Workspace,t)).ToImmutableArray());
    }

    public async Task<AgentResult> RunAsync(AgentWorkspaceSelection selection, string session, string prompt,
        ImmutableArray<SourceFileEdit> edits, DateTimeOffset deadline, Action<string> state,
        Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review, CancellationToken token)
    {
        if(!await admission.WaitAsync(0,token).ConfigureAwait(false)) throw new InvalidOperationException("Agent workspace sedang berjalan.");
        try
        {
            token.ThrowIfCancellationRequested();
            var natural=selection.Profile.ReviewedManifest is not null;
            var started=clock.GetTimestamp();
            var budget=deadline-clock.GetUtcNow();
            var maximum=ReviewedProjectCatalog.RunBudget(selection.Profile);
            using var progress=maximum==ReviewedProjectCatalog.PilotExecutionBudget && budget>TimeSpan.Zero && budget<=maximum?new AgentRunProgress(budget,clock):null;
            TimeSpan Remaining()=>progress?.RemainingActive ?? budget-clock.GetElapsedTime(started);
            var preflightRemaining=Remaining();
            if(preflightRemaining<=TimeSpan.Zero || preflightRemaining>maximum)throw new InvalidOperationException("Deadline invalid atau expired.");
            using var activeDeadline=progress is null?new CancellationTokenSource(preflightRemaining,clock):null;
            var deadlineToken=progress?.ActiveCancellation ?? activeDeadline!.Token;
            using var preflight=CancellationTokenSource.CreateLinkedTokenSource(token,deadlineToken);
            try
            {
            using var executionLease=selection.Profile.AcquireExecutionLease(preflight.Token);
            SourceProposalScope? proposal=null;
            if(natural)
            {
                if(reviewed is null || !edits.IsEmpty)throw new InvalidOperationException("NL proposal admission denied.");
                proposal=await reviewed.CreateScopeAsync(selection,session,preflight.Token).ConfigureAwait(false);
            }
            preflight.Token.ThrowIfCancellationRequested();
            var current=natural?selection:Select(selection.Profile.Root);
            if(!natural && (current.Profile.Digest!=selection.Profile.Digest || !edits.Select(e=>e.Target).SequenceEqual(selection.Profile.Targets) ||
                edits.Where((e,i)=>e.BeforeHash!=selection.Files[i].Hash).Any())) throw new InvalidOperationException("Workspace snapshot/intent mismatch.");
            var remaining=Remaining();
            if(remaining<=TimeSpan.Zero || remaining>maximum) throw new InvalidOperationException("Deadline invalid atau expired.");
            AgentWorkspaceApprovalProvider? approval=null;
            approval=new(selection.Profile,deadline,p=>review(p,approval!),clock,Remaining(),progress);
            using(approval)
            {
                var runLogger=new AgentWorkspaceRunLogger(logger,session,approval,state);
                var catalog=new SourcePlanCatalog(selection.Profile);
                var registry=new ToolRegistry([new ApplySourcePlanTool(catalog,editing,approval,runLogger)]);
                var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
                var executor=new ToolExecutor(registry,policy,runLogger,clock);
                var orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,executor,capability,runLogger);
                INexusAgent agent=new SharedRuntimeAgent(runtime,server,runLogger,orchestrator,deadlineToken);
                // Only selected source data, never Chat history or attachment context.
                var context=natural?null:JsonSerializer.Serialize(selection.Profile.Targets.Select((target,i)=>new { target,sha256=selection.Files[i].Hash }));
                var result=await agent.ExecuteAsync(new AgentRequest(session,prompt,context,256,
                    EnableReadOnlyToolCall:true,Limits:natural?new AgentRunLimits(MaxModelTurns:2,MaxToolCalls:1,MaxRetries:0,RunDuration:Remaining()):new AgentRunLimits(MaxRetries:0,RunDuration:Remaining()),
                    Verification:AgentVerificationRequirement.SourcePlan,SourceIntent:natural?null:new(selection.Profile.Id,edits),ProposalScope:proposal)
                    {ExecutionProgress=progress},token).ConfigureAwait(false);
                return result;
            }
            }
            catch(OperationCanceledException error) when(progress is not null && deadlineToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                // Preserve the host reason before final grant revocation cancels the caller's linked token.
                error.Data["ExecutionDeadline"]=true;throw;
            }
        }
        finally { if(selection.Profile.ReviewedManifest is not null)reviewed?.RevokeAfterRun(selection);admission.Release(); }
    }
}
