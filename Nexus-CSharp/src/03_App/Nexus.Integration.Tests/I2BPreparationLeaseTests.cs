using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class I2BPreparationLeaseTests
{
    [Fact]
    public void Preparation_ProtectsVerifiedDependencies_UntilLastExistingLeaseDrains()
    {
        using var f=new Fixture();var probe=Dependencies()[0];using(ExclusiveRead(probe)){}
        var prepared=Prepare(f.Workflow,CancellationToken.None);
        try
        {
            Assert.True(Blocked(probe),"Preparation returned without protecting its verified dependency handle.");
            Assert.Null(prepared.Selection.ReviewedAuthority);Assert.Empty(prepared.Selection.Files);
            Assert.Equal(ReviewedProjectCatalog.DisposableRoot,prepared.Selection.Profile.Root);
            using(var execution=prepared.Selection.Profile.AcquireExecutionLease())
            {
                prepared.Lease.Dispose();Assert.True(Blocked(probe));Assert.True(prepared.Selection.Profile.IsIntact());
            }
            using(ExclusiveRead(probe)){}prepared.Lease.Dispose();
        }
        finally{prepared.Lease.Dispose();}
    }

    [Fact]
    public void Preparation_DependencyConflictOrPreCancellation_LeavesNoPartialLease()
    {
        using var f=new Fixture();var paths=Dependencies();var conflict=paths.Last(p=>p.EndsWith(".targets",StringComparison.Ordinal));
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>Prepare(f.Workflow,cancellation.Token));
        }
        // Loaded runtime DLLs belong to testhost; contend on an unopened pinned data file.
        using(ExclusiveRead(conflict))
            Assert.ThrowsAny<IOException>(()=>Prepare(f.Workflow,CancellationToken.None));
        using(ExclusiveRead(paths[0])){}using(ExclusiveRead(conflict)){}
    }

    [Fact]
    public async Task Preparation_CancelAfterFirstDependencyIsHeld_DrainsPartialAcquisition()
    {
        using var f=new Fixture();var first=Dependencies()[0];using var cancellation=new CancellationTokenSource();
        var preparing=Task.Run(()=>Prepare(f.Workflow,cancellation.Token));
        try
        {
            var observed=false;var elapsed=Stopwatch.StartNew();
            while(!preparing.IsCompleted && elapsed.Elapsed<TimeSpan.FromSeconds(10))
            {if(Blocked(first)){observed=true;break;}await Task.Delay(10);}
            cancellation.Cancel();
            if(preparing.IsCompletedSuccessfully){var returned=await preparing;returned.Lease.Dispose();}
            Assert.True(observed,"Did not observe partial dependency acquisition.");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await preparing);
            using(ExclusiveRead(first)){}
        }
        finally
        {
            cancellation.Cancel();
            try{var returned=await preparing;returned.Lease.Dispose();}catch(OperationCanceledException){}
        }
    }

    [Fact]
    public async Task PreparedWorkflow_UsesExistingGrantAndRunAuthority_WithoutTransportOrWrite()
    {
        using var f=new Fixture();await f.Runtime.Workflow.StartActiveModelAsync(); // fake engine only
        var prepared=Prepare(f.Workflow,CancellationToken.None);
        try
        {
            var profile=prepared.Selection.Profile;var baseline=profile.InitialSourceHashes;
            var grant=await f.Reader.GrantAsync(profile.Root,"lease-run",CancellationToken.None);
            var authorized=f.Workflow.AuthorizeReviewedEdit(prepared.Selection,grant);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>f.Run(authorized,"foreign-session"));
            Assert.False(grant.IsActive);
            grant=await f.Reader.GrantAsync(profile.Root,"lease-run",CancellationToken.None);
            authorized=f.Workflow.AuthorizeReviewedEdit(prepared.Selection,grant);
            var result=await f.Run(authorized,"lease-run");
            Assert.NotEqual(AgentRunOutcome.Completed,result.Outcome);Assert.Equal(0,f.Transport.Calls);
            Assert.False(grant.IsActive);Assert.Null(f.Reader.Current);Assert.True(Blocked(Dependencies()[0]));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>f.Run(authorized,"lease-run"));
            Assert.All(baseline,p=>Assert.Equal(p.Value,new FileSnapshotService().ComputeSha256Hash(Path.Combine(profile.Root,p.Key))));
        }
        finally{prepared.Lease.Dispose();}
        using(ExclusiveRead(Dependencies()[0])){}
    }

    private static string[] Dependencies()=>ReviewedProjectVerificationManifest.LoadPinned(ReviewedProjectCatalog.DisposableRoot,ReviewedProjectCatalog.DisposableManifestSha256)
        .Dependencies.Keys.OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    private static FileStream ExclusiveRead(string p)=>new(p,FileMode.Open,FileAccess.Read,FileShare.None);
    private static bool Blocked(string p){try{using var stream=ExclusiveRead(p);return false;}catch(IOException){return true;}}
    private static (AgentWorkspaceSelection Selection,IDisposable Lease) Prepare(AgentWorkspaceWorkflow workflow,CancellationToken token)
    {
        // Old production path exposes the missing protected preparation as observable RED.
        var method=typeof(AgentWorkspaceWorkflow).GetMethod("PrepareReviewedPilot",BindingFlags.Instance|BindingFlags.NonPublic);
        if(method is null)return(workflow.Select(ReviewedProjectCatalog.DisposableRoot),new MemoryStream());
        try{return ((AgentWorkspaceSelection,IDisposable))method.Invoke(workflow,[token])!;}
        catch(TargetInvocationException e) when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private sealed class Fixture:IDisposable
    {
        public ProjectReadContextWorkflow Reader {get;}=new();
        public SharedRuntimeAgentTests.Fixture Runtime {get;}=new();
        public DenyTransport Transport {get;}=new();private readonly HttpClient http;
        public AgentWorkspaceWorkflow Workflow {get;}
        public Fixture()
        {
            http=new(Transport);var server=new Mock<ILlamaServerManager>();
            server.SetupGet(s=>s.State).Returns(()=>Runtime.State);server.SetupGet(s=>s.OwnedPid).Returns(123);
            server.SetupGet(s=>s.RuntimeModelPath).Returns(Runtime.Original.ResolvedGgufPath);
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            Workflow=new(Runtime.Workflow,server.Object,http,editing,new NativeToolCapability(server.Object),Mock.Of<ILoggerService>(),new ReviewedProjectCatalog(Reader));
        }
        public Task<AgentResult> Run(AgentWorkspaceSelection selection,string session)=>Workflow.RunAsync(selection,session,"offline lease admission",ImmutableArray<SourceFileEdit>.Empty,
            DateTimeOffset.UtcNow.AddSeconds(180),_=>{},(_,_)=>throw new InvalidOperationException("No proposal authorized"),CancellationToken.None);
        public void Dispose(){Reader.Dispose();http.Dispose();Runtime.Dispose();}
    }
    private sealed class DenyTransport:HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;throw new InvalidOperationException("No live inference or network permitted");}
    }
}
