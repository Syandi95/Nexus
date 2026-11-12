using System.Text.Json;

namespace Nexus.App.Services.Agent.Tools;

// Nexus-owned immutable authority. Model arguments cannot modify this allowlist,
// descriptor risk, permission, timeout, or read-only classification.
public sealed class ReadOnlyToolPolicy
{
    private readonly HashSet<string> _allowlist;
    private readonly HashSet<string> _safeWrites;
    private readonly Nexus.AI.EditingLite.Services.ApprovedTextWorkspace? _workspace;
    private readonly SourcePlanCatalog? _sourceCatalog;
    public ReadOnlyToolPolicy(IEnumerable<string> explicitAllowedNames,IEnumerable<string>? safeWriteNames=null,
        Nexus.AI.EditingLite.Services.ApprovedTextWorkspace? workspace=null,SourcePlanCatalog? sourceCatalog=null)
    { _allowlist=new(explicitAllowedNames,StringComparer.Ordinal); _safeWrites=new(safeWriteNames ?? [],StringComparer.Ordinal); _workspace=workspace; _sourceCatalog=sourceCatalog; }
    public ToolPolicyDecision Evaluate(ToolDescriptor descriptor, ToolExecutionContext context, JsonElement validatedArguments)
    {
        if(descriptor.Risk==ToolRisk.SourcePlan && !descriptor.ReadOnly)
        {
            var run=context.Run; var profile=_sourceCatalog?.Profile;
            var first=run.Progress?.SourcePlanCount==1 && run.Progress.SourceResult is null;
            var second=run.Progress is { SourcePlanCount:2,RepairPlanCount:1,RepairPlanning:true } && _sourceCatalog?.RepairAvailable(run)==true;
            if(descriptor.Name!=ApplySourcePlanTool.ToolName || profile is null || run.SourceIntent is null || !run.SourceIntent.IsValid ||
                run.SourceIntent.ProfileId!=profile.Id || (!first && !second) || (run.Limits?.MaxSourcePlans!=1 && _sourceCatalog?.RepairAvailable(run)!=true) ||
                run.Model is null || run.RunId==Guid.Empty || run.RemainingDuration<=TimeSpan.Zero || !profile.IsIntact() ||
                run.SourceIntent.Files.Any(f=>!profile.Targets.Contains(f.Target,StringComparer.Ordinal))) return new(false,"SourcePlanAuthorityDenied");
            return new(true,"SourcePlanRequiresApproval");
        }
        if(descriptor.Risk==ToolRisk.SafeWrite && !descriptor.ReadOnly)
        {
            var run=context.Run;
            var intent=run.WriteIntent;
            if(!_safeWrites.Contains(descriptor.Name) || _workspace?.Root is null || intent is null || run.Progress is null ||
                run.Model is null || run.RunId==Guid.Empty || string.IsNullOrWhiteSpace(run.SessionId) || run.RemainingDuration<=TimeSpan.Zero ||
                run.Progress.WriteOperationCount!=1 || run.Progress.WriteOperations.Count!=0 || run.Limits?.MaxWriteOperations!=1) return new(false,"SafeWriteAuthorityDenied");
            if(validatedArguments.GetProperty("target").GetString()!=intent.Target || validatedArguments.GetProperty("expected_sha256").GetString()!=intent.ExpectedSha256 ||
                validatedArguments.GetProperty("old_text").GetString()!=intent.OldText || validatedArguments.GetProperty("new_text").GetString()!=intent.NewText) return new(false,"WriteIntentMismatch");
            try { _workspace.ResolveTarget(intent.Target,run.Limits.MaxTargetFileBytes); }
            catch { return new(false,"PathDenied"); }
            return new(true,"SafeWriteRequiresApproval"); // Admission is not permission to commit.
        }
        if (!descriptor.ReadOnly || descriptor.Risk != ToolRisk.ReadOnly) return new(false, "WriteOrProcessNotAllowed");
        if (!_allowlist.Contains(descriptor.Name)) return new(false, "PolicyDenied");
        if (context.Run.RunId == Guid.Empty || string.IsNullOrWhiteSpace(context.Run.SessionId) || context.Run.Model is null ||
            context.Run.RemainingDuration <= TimeSpan.Zero || validatedArguments.ValueKind != JsonValueKind.Object)
            return new(false, "InvalidRunContext");
        return new(true, "AllowedReadOnlyTool");
    }
    internal bool IsAvailable(ToolDescriptor descriptor) => descriptor.ReadOnly && descriptor.Risk==ToolRisk.ReadOnly && _allowlist.Contains(descriptor.Name);
    internal bool IsAvailable(ToolDescriptor descriptor,AgentRunContext context)=>IsAvailable(descriptor) ||
        descriptor.Name==ApplySourcePlanTool.ToolName && descriptor.Risk==ToolRisk.SourcePlan && _sourceCatalog?.Profile is not null &&
        (context.SourceIntent?.ProfileId==_sourceCatalog.Profile.Id || context.ProposalScope?.ProfileDigest==_sourceCatalog.Profile.Digest && context.ProposalScope.Session==context.SessionId) && (context.Limits?.MaxSourcePlans==1 || _sourceCatalog.RepairAvailable(context)) ||
        descriptor.Risk==ToolRisk.SafeWrite && !descriptor.ReadOnly && _safeWrites.Contains(descriptor.Name) && _workspace?.Root is not null &&
        context.WriteIntent is not null && context.Limits?.MaxWriteOperations==1;
    internal bool IsWriteStillVerified(Nexus.AI.EditingLite.Services.WriteOperationRecord operation)
    {
        try
        {
            if(_workspace?.Root is null || operation.PolicyId!=_workspace.PolicyId) return false;
            using var lease=new Nexus.AI.EditingLite.Services.SafePathLease(_workspace.Root,operation.Target);
            using var target=lease.OpenTarget();
            if(Nexus.AI.EditingLite.Services.FileWriterService.Hash(target)!=operation.DesiredHash) return false;
            if(!operation.CommitReached) return operation.BeforeHash==operation.DesiredHash;
            var relative=operation.Target+".nexus-edit-"+operation.OperationId.ToString("N")+".bak";
            if(System.IO.Path.GetFileName(relative)!=operation.BackupRef) return false;
            using var backupLease=new Nexus.AI.EditingLite.Services.SafePathLease(_workspace.Root,relative);
            using var backup=backupLease.OpenTarget();
            return Nexus.AI.EditingLite.Services.FileWriterService.Hash(backup)==operation.BeforeHash;
        }
        catch { return false; }
    }
}
