using System.IO;
using System.Reflection;
using Nexus.App.Services;
using Nexus.AI.EditingLite.Services;

namespace Nexus.Integration.Tests;

public sealed class RecoveryFirstAdmissionTests
{
    private const string Root=@"D:\NEXUS\scratch\recovery-first-20261017\reviewed-i2b-v2";
    private const string Pin="adda3d3cd92af3d7c034a45fd8e133f75d489a711a26a0f9f188aa63b9ab31a2";
    private static object Call(object target,string method,params object[] args)
    {
        try{return target.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args)!;}
        catch(TargetInvocationException e) when(e.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    [Fact]
    public async Task RevisedDisposable_RequiresCurrentHostGrant_AndExactSession()
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var selected=(AgentWorkspaceSelection)Call(catalog,"Select",Root);
        Assert.Equal(Pin,selected.Profile.ReviewedManifest!.ManifestHash);
        await Assert.ThrowsAnyAsync<Exception>(async()=>await (Task)Call(catalog,"CreateScopeAsync",selected,"recovery-session",CancellationToken.None));
        var grant=await reader.GrantAsync(Root,"recovery-session",CancellationToken.None);
        var authorized=(AgentWorkspaceSelection)Call(catalog,"Authorize",selected,grant);
        await Assert.ThrowsAnyAsync<Exception>(async()=>await (Task)Call(catalog,"CreateScopeAsync",authorized,"foreign-session",CancellationToken.None));
        await (Task)Call(catalog,"CreateScopeAsync",authorized,"recovery-session",CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(async()=>await (Task)Call(catalog,"CreateScopeAsync",authorized,"recovery-session",CancellationToken.None));
        reader.Revoke();
        Assert.ThrowsAny<Exception>(()=>Call(catalog,"Authorize",selected,grant));
    }
    [Fact]
    public async Task RevisedDisposable_RejectsPreviousGrantRevision()
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var selected=(AgentWorkspaceSelection)Call(catalog,"Select",Root);
        var stale=await reader.GrantAsync(Root,"same-session",CancellationToken.None);
        await reader.GrantAsync(Root,"same-session",CancellationToken.None);
        Assert.ThrowsAny<Exception>(()=>Call(catalog,"Authorize",selected,stale));
    }
    [Theory]
    [InlineData(@"D:\NEXUS\Nexus-CSharp")]
    [InlineData(@"D:\NEXUS\scratch\recovery-first-20261017")]
    [InlineData(@"D:\NEXUS\scratch\recovery-first-20261017\reviewed-i2b-v2-other")]
    public void Catalog_RejectsUnadmittedRoots(string root)
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        Assert.ThrowsAny<Exception>(()=>Call(catalog,"Select",root));
    }
    [Theory]
    [InlineData("../Nexus.Contracts/EngineSessionOptions.cs")]
    [InlineData("Nexus.Contracts/../../outside.cs")]
    [InlineData("C:/Windows/Temp/unadmitted.cs")]
    public void AdmittedProfile_DoesNotAuthorizeEscapingTargets(string relative)
    {
        var profile=new SourceVerificationProfile(ReviewedProjectVerificationManifest.LoadPinned(Root,Pin));
        Assert.ThrowsAny<Exception>(()=>profile.Workspace.ResolveTarget(relative));
    }
}
