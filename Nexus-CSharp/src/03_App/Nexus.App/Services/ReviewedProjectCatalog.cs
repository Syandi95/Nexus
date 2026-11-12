using System.Collections.Immutable;
using System.IO;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;

namespace Nexus.App.Services;

public sealed class ReviewedProjectEditAuthority
{
    private readonly IProjectReadContextWorkflow reader;
    internal ProjectReadGrant ReadGrant {get;}
    internal string ProfileDigest {get;}
    private int consumed;
    public bool IsActive=>ReadGrant.IsActive && ReferenceEquals(reader.Current,ReadGrant) && Volatile.Read(ref consumed)==0;
    internal ReviewedProjectEditAuthority(IProjectReadContextWorkflow reader,ProjectReadGrant grant,string digest)
    {this.reader=reader;ReadGrant=grant;ProfileDigest=digest;}
    internal void Check(SourceVerificationProfile profile,string session)
    {
        if(!ReadGrant.IsActive || !ReferenceEquals(reader.Current,ReadGrant) || ProfileDigest!=profile.Digest || session!=ReadGrant.Session || ReadGrant.Root!=profile.Root)
            throw new InvalidOperationException("Reviewed edit/read authority changed.");
        ReadGrant.Check(session);
    }
    internal void Consume(SourceVerificationProfile profile,string session)
    {Check(profile,session);if(Interlocked.Exchange(ref consumed,1)!=0)throw new InvalidOperationException("Reviewed authority already consumed.");}
}

// Exact compiled admission entries. Selecting a directory never creates authority.
public sealed class ReviewedProjectCatalog(IProjectReadContextWorkflow reader)
{
    public const string Root=@"D:\NEXUS\scratch\I2B-ReviewedContracts";
    public const string ManifestSha256="0ba3b70b62b2f3f83a691769a4031670969f87d78df4a1d940a3d90dd06b480b";
    public const string DisposableRoot=@"D:\NEXUS\scratch\recovery-first-20261017\reviewed-i2b-v2";
    public const string DisposableManifestSha256="adda3d3cd92af3d7c034a45fd8e133f75d489a711a26a0f9f188aa63b9ab31a2";
    internal static readonly TimeSpan PilotExecutionBudget=TimeSpan.FromSeconds(240);
    internal static readonly TimeSpan PilotApprovalTtl=TimeSpan.FromSeconds(600);
    internal static readonly string[] Anchors=["    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;","    public int ActiveSessionsCount { get; init; }"];
    private static bool SameRoot(string candidate,string admitted)=>string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),admitted,StringComparison.OrdinalIgnoreCase);
    internal static bool Matches(string root)=>SameRoot(root,Root)||SameRoot(root,DisposableRoot);
    private static string PinFor(string root)=>SameRoot(root,Root)?ManifestSha256:SameRoot(root,DisposableRoot)?DisposableManifestSha256:throw new InvalidOperationException("Unknown reviewed project.");
    internal static TimeSpan RunBudget(SourceVerificationProfile profile)=>
        SameRoot(profile.Root,DisposableRoot) && profile.Id==ReviewedProjectVerificationManifest.ProfileId &&
        profile.ReviewedManifest?.ManifestHash==DisposableManifestSha256 ? PilotExecutionBudget : TimeSpan.FromSeconds(120);
    internal AgentWorkspaceSelection Select(string root)
    {
        var pin=PinFor(root);
        var profile=new SourceVerificationProfile(ReviewedProjectVerificationManifest.LoadPinned(pin==ManifestSha256?Root:DisposableRoot,pin));
        return new(profile,[]); // Exact source content requires the separate current read grant.
    }
    internal (AgentWorkspaceSelection Selection,IDisposable Lease) PreparePilot(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var manifest=ReviewedProjectVerificationManifest.LoadPinned(DisposableRoot,DisposableManifestSha256);
        var lease=manifest.LockDependencies(cancellation);
        try
        {
            var profile=new SourceVerificationProfile(manifest);
            cancellation.ThrowIfCancellationRequested();
            // Caller holds this run-scoped reference until RunAsync and protected recovery drain.
            return(new(profile,[]),lease);
        }
        catch {lease.Dispose();throw;}
    }
    internal AgentWorkspaceSelection Authorize(AgentWorkspaceSelection selection,ProjectReadGrant grant)
    {
        if(!Matches(selection.Profile.Root) || selection.Profile.Id!=ReviewedProjectVerificationManifest.ProfileId || selection.Profile.ReviewedManifest?.ManifestHash!=PinFor(selection.Profile.Root) ||
            !ReferenceEquals(reader.Current,grant) || grant.Root!=selection.Profile.Root)throw new InvalidOperationException("Reviewed edit admission denied.");
        grant.Check(grant.Session);
        return selection with {ReviewedAuthority=new(reader,grant,selection.Profile.Digest)};
    }
    internal async Task<SourceProposalScope> CreateScopeAsync(AgentWorkspaceSelection selection,string session,CancellationToken token)
    {
        var authority=selection.ReviewedAuthority??throw new InvalidOperationException("Explicit reviewed edit/verification consent required.");
        if(!Matches(selection.Profile.Root)||selection.Profile.ReviewedManifest?.ManifestHash!=PinFor(selection.Profile.Root))throw new InvalidOperationException("Unreviewed execution profile.");
        authority.Consume(selection.Profile,session);
        var sources=await reader.ReadExactForEditAsync(authority.ReadGrant,session,selection.Profile.Targets,token).ConfigureAwait(false);
        return new(selection.Profile,session,sources,Anchors,()=>authority.Check(selection.Profile,session),authority.ReadGrant.Consume,authority.ReadGrant.Cancellation);
    }
    internal void RevokeAfterRun(AgentWorkspaceSelection selection)
    {if(ReferenceEquals(reader.Current,selection.ReviewedAuthority?.ReadGrant))reader.Revoke();}
}
