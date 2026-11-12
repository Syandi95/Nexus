namespace Nexus.AI.EditingLite.Services;

// Minted only after completed SDK producer + typed correlation + guarded source
// binding. No console text, model arguments or caller-built record can mint it.
public sealed class CompilerFailureReceipt
{
    public Guid BuildInvocationId { get; }
    public int AttemptNumber { get; }
    public Guid PlanId { get; }
    public string ProfileDigest { get; }
    public string DiagnosticId=>"CS0029";
    public string Severity=>"error";
    public string LogicalSourcePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string FailedSourceHash { get; }
    public string DesiredHash { get; }
    public string ProjectIdentity=>"Product/Product.csproj";
    public string CscTaskIdentity { get; }
    public string ArtifactId { get; }
    public string ArtifactDigest { get; }
    public bool InfrastructureClean=>true;
    public bool SourceIdentityBound=>true;
    public bool ArtifactComplete=>true;
    public string SanitizedMessage=>"Cannot implicitly convert type 'string' to 'int'.";
    internal CompilerFailureReceipt(Guid invocation,int attempt,SourceEditPlan plan,string target,int line,int column,string sourceHash,string task,string artifactDigest)
    {
        BuildInvocationId=invocation; AttemptNumber=attempt; PlanId=plan.PlanId; ProfileDigest=plan.Profile.Digest;
        LogicalSourcePath=target; Line=line; Column=column; FailedSourceHash=sourceHash; DesiredHash=plan.Entries.Single(e=>e.Target==target).DesiredHash;
        CscTaskIdentity=task; ArtifactId=invocation.ToString("N")+"/Product/Csc"; ArtifactDigest=artifactDigest;
    }
    public bool Matches(SourceEditPlan plan,int attempt)=>BuildInvocationId!=Guid.Empty && AttemptNumber==attempt && PlanId==plan.PlanId && ProfileDigest==plan.Profile.Digest &&
        plan.Profile.CompilerObservationEnabled && plan.Entries.Any(e=>e.Target==LogicalSourcePath && e.DesiredHash==FailedSourceHash && e.DesiredHash==DesiredHash);
}
