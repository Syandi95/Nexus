using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent.Tools;

namespace Nexus.App.Services.Agent;

// Pure deterministic admission only. No writer, approval, execution or model retry.
internal static class NaturalLanguageSourceProposal
{
    internal static ImmutableArray<SourceFileEdit> Validate(string raw,IReadOnlyList<FileSnapshot> files,IReadOnlyList<string> targets,IReadOnlyList<string> anchors)
    {
        if(files.Count!=2 || targets.Count!=2 || anchors.Count!=2 || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=2 ||
            !SourcePlanArgumentValidator.TryValidate(raw,out var args) || args.EnumerateObject().Count()!=2)
            throw new ArgumentException("InvalidSourceProposal");
        var result=ImmutableArray.CreateBuilder<SourceFileEdit>();var changedLines=0;
        for(var i=0;i<2;i++)
        {
            var file=files[i];var source=file.Content;var utf8=new UTF8Encoding(false,true);
            if(file.Encoding.CodePage!=Encoding.UTF8.CodePage || utf8.GetByteCount(source)+file.Encoding.GetPreamble().Length>4096 ||
                Convert.ToHexString(SHA256.HashData(file.Encoding.GetPreamble().Concat(utf8.GetBytes(source)).ToArray())).ToLowerInvariant()!=file.Hash)
                throw new ArgumentException("SourceEncodingOrHashMismatch");
            var newline=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
            if(source.Replace(newline,"").IndexOfAny(['\r','\n'])>=0)throw new ArgumentException("MixedSourceNewline");
            var requested=SourcePlanArgumentValidator.Read(args.GetProperty(i==0?"a":"b"));
            var resolved=ExactSourceEdits.Resolve(source,requested);
            foreach(var edit in resolved.Edits)
            {
                // Original-coordinate authority is exactly the presented declaration.
                // Only append one adjacent source line; retain the entire declaration.
                if(edit.OldText!=anchors[i] || edit.Start!=source.IndexOf(anchors[i],StringComparison.Ordinal) ||
                    !edit.NewText.StartsWith(edit.OldText+newline,StringComparison.Ordinal))throw new ArgumentException("EditRangeDenied");
                var inserted=edit.NewText[(edit.OldText.Length+newline.Length)..];
                if(string.IsNullOrWhiteSpace(inserted) || inserted.IndexOfAny(['\r','\n'])>=0)throw new ArgumentException("EditRangeOrNewlineDenied");
            }
            changedLines+=ExactSourceEdits.ChangedLines(resolved.Edits);
            result.Add(new(targets[i],file.Hash,requested.ToImmutableArray()));
        }
        if(changedLines>6)throw new ArgumentException("ChangedLineLimit");
        return result.ToImmutable();
    }
}

// Host-only immutable capability, bound once to the actual run. Never model data.
public sealed class SourceProposalScope
{
    private readonly SourceVerificationProfile profile;
    private readonly ImmutableArray<ProjectExactSource> sources;
    private readonly ImmutableArray<FileSnapshot> snapshots;
    private readonly ImmutableArray<string> anchors;
    private readonly Action checkRead;
    private readonly Action<int> consumeRead;
    private readonly CancellationToken cancellation;
    private Guid runId;
    private bool accepted;
    internal string Session {get;}
    internal string ProfileId=>profile.Id;
    internal string ProfileDigest=>profile.Digest;
    internal bool AllowsExtendedPilot(AgentRunLimits limits)=>ReviewedProjectCatalog.RunBudget(profile)==ReviewedProjectCatalog.PilotExecutionBudget &&
        limits is {MaxModelTurns:2,MaxToolCalls:1,MaxSourcePlans:1,MaxRepairPlans:0,MaxRetries:0,MaxBuildStarts:1,MaxTestStarts:1} &&
        limits.MaxRunDuration>TimeSpan.Zero && limits.MaxRunDuration<=ReviewedProjectCatalog.PilotExecutionBudget &&
        (limits with {RunDuration=TimeSpan.FromSeconds(120)}).IsValid;
    internal string Payload=>JsonSerializer.Serialize(sources.Select((s,i)=>new {slot=i==0?"a":"b",source=s.Text,newline=s.Text.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n",allowedOriginalDeclaration=anchors[i]}));
    internal Tools.ToolDescriptor CompactProposalDescriptor(Tools.ToolDescriptor descriptor)
    {
        var properties=new System.Text.Json.Nodes.JsonObject();
        for(var i=0;i<2;i++)
        {
            var key=i==0?"a":"b";
            var item=new System.Text.Json.Nodes.JsonObject { ["type"]="object",["properties"]=new System.Text.Json.Nodes.JsonObject
            {
                ["old"]=new System.Text.Json.Nodes.JsonObject { ["type"]="string",["minLength"]=1,["maxLength"]=256,["enum"]=new System.Text.Json.Nodes.JsonArray(anchors[i]) },
                ["new"]=new System.Text.Json.Nodes.JsonObject { ["type"]="string",["minLength"]=1,["maxLength"]=256 }
            },["required"]=new System.Text.Json.Nodes.JsonArray("old","new"),["additionalProperties"]=false };
            properties[key]=new System.Text.Json.Nodes.JsonObject { ["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=item };
        }
        var schema=new System.Text.Json.Nodes.JsonObject { ["type"]="object",["properties"]=properties,["required"]=new System.Text.Json.Nodes.JsonArray("a","b"),["additionalProperties"]=false };
        return descriptor with { Description="Return only one native nexus_apply_source_plan call with exact slots a and b (one edit per slot, old equals allowedOriginalDeclaration, new appends one C# computed getter line).",ArgumentSchema=JsonSerializer.SerializeToElement(schema) };
    }
    internal SourceProposalScope(SourceVerificationProfile profile,string session,ImmutableArray<ProjectExactSource> sources,IReadOnlyList<string> anchors,Action checkRead,Action<int> consumeRead,CancellationToken cancellation)
    {
        this.profile=profile;Session=session;this.sources=sources;this.anchors=anchors.ToImmutableArray();this.checkRead=checkRead;this.consumeRead=consumeRead;this.cancellation=cancellation;
        if(sources.Length!=2 || !sources.Select(s=>s.Target).SequenceEqual(profile.Targets) || this.anchors.Length!=2)throw new ArgumentException("ProposalScopeMappingDenied");
        snapshots=sources.Select(s=>new FileSnapshot {Content=s.Text,Hash=s.Hash,Encoding=new UTF8Encoding(s.Utf8Bom,true),AbsolutePath=Path.Combine(profile.Root,s.Target)}).ToImmutableArray();
        CheckOriginal();
    }
    internal void Bind(AgentRunContext run)
    {
        if(runId!=Guid.Empty || run.RunId==Guid.Empty || run.SessionId!=Session || run.SourceIntent is not null ||
            run.Limits is not {MaxSourcePlans:1,MaxRepairPlans:0,MaxRetries:0,MaxModelTurns:2,MaxToolCalls:1})throw new ArgumentException("SourceProposalRunDenied");
        CheckOriginal();runId=run.RunId;
    }
    internal void Check(AgentRunContext run)
    {
        cancellation.ThrowIfCancellationRequested();run.Cancellation.ThrowIfCancellationRequested();checkRead();
        if(run.RunId!=runId || run.SessionId!=Session || run.RemainingDuration<=TimeSpan.Zero || !profile.IsIntact())throw new ToolProtocolException("SourceProposalScopeChanged");
        foreach(var source in sources)
        {
            using var lease=new SafePathLease(profile.Root,source.Target);using var stream=lease.OpenTarget();consumeRead(checked((int)stream.Length));
            var identity=ReadOnlyPathScope.Capture(stream.SafeFileHandle);var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var plan=run.Progress?.SourcePlan;var result=run.Progress?.SourceResult;
            if(hash==source.Hash)
            {
                var ownedRecovery=plan is not null && result?.HasMatchingApproval(plan)==true && result.BaselineVerified &&
                    result.Operations.Any(o=>o.Target==source.Target && o.CommitReached && o.Recovery==SafeWriteRecovery.Restored);
                if(identity!=source.Identity && !ownedRecovery)throw new ToolProtocolException("SourceProposalIdentityChanged");
            }
            else if(plan?.RunId!=run.RunId || plan.Profile.Digest!=profile.Digest || run.Progress?.AcceptedSourceIntent is null ||
                !plan.Entries.Any(e=>e.Target==source.Target && e.BeforeHash==source.Hash && e.DesiredHash==hash))
                throw new ToolProtocolException("SourceProposalHashChanged");
        }
    }
    private void CheckOriginal()
    {
        cancellation.ThrowIfCancellationRequested();checkRead();if(!profile.IsIntact())throw new ArgumentException("ProposalProfileChanged");
        foreach(var source in sources)
        {
            using var lease=new SafePathLease(profile.Root,source.Target);using var stream=lease.OpenTarget();
            consumeRead(checked((int)stream.Length));
            if(ReadOnlyPathScope.Capture(stream.SafeFileHandle)!=source.Identity ||
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=source.Hash || profile.InitialSourceHashes[source.Target]!=source.Hash)
                throw new ArgumentException("ProposalSourceChanged");
        }
    }
    internal SourcePlanIntent Accept(AgentRunContext run,string raw)
    {
        Check(run);if(accepted || run.Progress is not {SourcePlanCount:0,ModelTurnCount:1})throw new ToolProtocolException("SourceProposalAlreadyConsumed");
        CheckOriginal();ImmutableArray<SourceFileEdit> files;
        try {files=NaturalLanguageSourceProposal.Validate(raw,snapshots,profile.Targets,anchors);}
        catch(ArgumentException){throw new ToolProtocolException("InvalidSourceProposal");}
        Check(run);CheckOriginal();accepted=true;
        return new(profile.Id,files);
    }
}
