using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;

namespace Nexus.Integration.Tests;

public sealed class GateI2BNaturalLanguageEditTests
{
    private const string ReviewedRoot=@"D:\NEXUS\scratch\I2B-ReviewedContracts";
    private const string ReviewedPin="0ba3b70b62b2f3f83a691769a4031670969f87d78df4a1d940a3d90dd06b480b";
    [Theory]
    [InlineData("success")]
    [InlineData("build-failure")]
    [InlineData("test-failure")]
    [InlineData("reject")]
    [InlineData("expire")]
    [InlineData("cancel")]
    [InlineData("conflict")]
    public async Task ReviewedOfflineExecution_UsesExistingSafeWriteAndRecovery_WithoutAncestorExec(string mode)
    {
        var parent=Path.Combine(Path.GetTempPath(),"nexus-i2b-execution-"+Guid.NewGuid().ToString("N"));var root=Path.Combine(parent,"reviewed");Directory.CreateDirectory(root);
        try
        {
            var marker=Path.Combine(parent,"ancestor-executed.marker");
            File.WriteAllText(Path.Combine(parent,"Directory.Build.targets"),"<Project><Target Name=\"UnreviewedAncestor\" BeforeTargets=\"Build;CoreCompile\"><Exec Command=\"cmd.exe /d /c &amp;quot;echo unsafe&amp;gt;"+marker+"&amp;quot;\" /></Target></Project>".Replace("&amp;","&"));
            var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(ReviewedRoot,"nexus-reviewed-i2b.json")))!;
            foreach(var path in json["graph"]!.AsObject().Select(p=>p.Key).Concat(json["sources"]!.AsObject().Select(p=>p.Key)).Concat(json["artifacts"]!.AsObject().Select(p=>p.Key)))
            {var dest=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(Path.Combine(ReviewedRoot,path),dest);}
            json["root"]=root;var manifestPath=Path.Combine(root,"nexus-reviewed-i2b.json");File.WriteAllText(manifestPath,json.ToJsonString());
            var profile=new SourceVerificationProfile(ReviewedProjectVerificationManifest.LoadPinned(root,new FileSnapshotService().ComputeSha256Hash(manifestPath)));
            using var dependencyLease=profile.AcquireExecutionLease();
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            using var cts=new CancellationTokenSource();var deadline=DateTimeOffset.UtcNow.AddSeconds(120);
            var control=new SourceRunControl(()=>deadline-DateTimeOffset.UtcNow,cts.Token,()=>{});
            var files=profile.Targets.Select((t,i)=>new SourceFileEdit(t,profile.InitialSourceHashes[t],i==0?A:B,
                (i==0?A:B)+"\n    public bool "+(i==0?mode=="build-failure"?"IsCpu => MissingSymbol;":mode=="test-failure"?"IsCpu => ExecutionProvider != ExecutionProvider.Cpu;":"IsCpu => ExecutionProvider == ExecutionProvider.Cpu;":"HasActiveSessions => ActiveSessionsCount > 0;"))).ToArray();
            // This is deterministic host testing, not a native/model acceptance run.
            var plan=await editing.PrepareSourcePlanAsync(profile,files,Guid.NewGuid(),"i2b-deterministic",new(),control);
            var requested=false;
            var approval=new PlanApproval(p=>
            {
                requested=true;Assert.Same(plan,p);Assert.All(p.Entries,e=>Assert.Equal(e.BeforeHash,new FileSnapshotService().ComputeSha256Hash(Path.Combine(root,e.Target))));
                return mode!="reject";
            });
            if(mode is "cancel" or "conflict")typeof(EditingWorkflowService).GetProperty("SourcePlanProbe",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(editing,
                (Action<string,SourceEditPlan,int>)((stage,p,index)=>
                {
                    if(mode=="cancel" && stage=="SourceApplyCompleted" && index==0)cts.Cancel();
                    if(mode=="conflict" && stage=="SourcePreflightPassed")File.AppendAllText(Path.Combine(root,p.Entries[0].Target),"// external edit retained");
                }));
            SourcePlanResult result;
            if(mode=="expire")
            {
                using var expired=new Nexus.App.Services.AgentWorkspaceApprovalProvider(profile,DateTimeOffset.UtcNow.AddSeconds(-1),_=>{});expired.BindRun(plan.RunId);
                result=await editing.ExecuteSourcePlanAsync(plan,expired,control);
            }
            else result=await editing.ExecuteSourcePlanAsync(plan,approval,control);
            Assert.False(File.Exists(marker));
            foreach(var original in profile.InitialSourceHashes.Where(p=>!profile.Targets.Contains(p.Key)))Assert.Equal(original.Value,new FileSnapshotService().ComputeSha256Hash(Path.Combine(root,original.Key)));
            if(mode=="success")
            {
                if(result.Outcome!=SourcePlanOutcome.Success)
                {
                    var evidence=Path.Combine(Path.GetTempPath(),"opencode","gate-i2b","deterministic-failure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
                    File.WriteAllText(Path.Combine(evidence,"source-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
                    foreach(var trx in Directory.EnumerateFiles(root,"*.trx",SearchOption.AllDirectories))File.Copy(trx,Path.Combine(evidence,Path.GetFileName(Path.GetDirectoryName(trx))+"-"+Path.GetFileName(trx)));
                }
                Assert.True(result.Outcome==SourcePlanOutcome.Success,JsonSerializer.Serialize(new {result.Outcome,result.Build,result.Tests}));Assert.True(result.FinalVerified);Assert.True(result.Build!.Passed);Assert.True(result.Tests!.Passed);Assert.Equal(3,result.Tests.TestsPassed);
                Assert.All(result.Operations,o=>Assert.True(o.CommitReached));Assert.True(editing.VerifySourcePlanState(plan,result));
            }
            else if(mode=="conflict")
            {
                Assert.NotEqual(SourcePlanOutcome.Success,result.Outcome);Assert.Contains("external edit retained",File.ReadAllText(Path.Combine(root,plan.Entries[0].Target)));
                Assert.All(result.Operations,o=>Assert.False(o.CommitReached));
            }
            else
            {
                Assert.Equal(mode switch {"build-failure"=>SourcePlanOutcome.BuildFailed,"test-failure"=>SourcePlanOutcome.TestFailed,"cancel"=>SourcePlanOutcome.Cancelled,_=>SourcePlanOutcome.ApprovalDenied},result.Outcome);
                Assert.True(result.BaselineVerified);Assert.All(profile.Targets,t=>Assert.Equal(profile.InitialSourceHashes[t],new FileSnapshotService().ComputeSha256Hash(Path.Combine(root,t))));
                if(mode is "build-failure" or "test-failure" or "cancel")Assert.Contains(result.Operations,o=>o.Recovery==SafeWriteRecovery.Restored);
                if(mode=="test-failure")
                {
                    Assert.Equal("RequiredTestsFailed",result.Tests!.Category);Assert.Equal(2,result.Tests.TestsPassed);
                    var trx=Directory.GetFiles(root,"required.trx",SearchOption.AllDirectories).Single();
                    var xml=System.Xml.Linq.XDocument.Load(trx);System.Xml.Linq.XNamespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                    var rows=xml.Descendants(ns+"UnitTestResult").ToArray();Assert.Equal(3,rows.Length);
                    Assert.Contains(rows,r=>r.Attribute("testName")?.Value=="I2BContracts.RequiredFacts.CpuFlag"&&r.Attribute("outcome")?.Value=="Failed");
                }
                if(mode is "reject" or "expire") {Assert.Empty(result.Operations);Assert.Null(result.Build);Assert.Null(result.Tests);}
            }
            Assert.Equal(mode!="expire",requested);
        }
        finally{Directory.Delete(parent,true);}
    }
    private sealed class PlanApproval(Func<SourceEditPlan,bool> decide):IWriteApprovalProvider
    {
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>Task.FromResult(WriteApprovalDecision.For(p,false));
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(p,decide(p)));
    }
    [Fact]
    public void ReviewedProfile_CompleteClosureAndReadLockedDependencies_AreIntact()
    {
        var manifest=ReviewedProjectVerificationManifest.LoadPinned(ReviewedRoot,ReviewedPin);
        var p=new SourceVerificationProfile(manifest);
        Assert.Equal(Targets,p.Targets);Assert.Equal(17,p.InitialSourceHashes.Count);Assert.True(p.IsIntact());
        using(p.AcquireExecutionLease())Assert.True(p.IsIntact());
        Assert.Equal(TimeSpan.FromSeconds(15),p.BuildTimeout);Assert.Equal(TimeSpan.FromSeconds(15),p.TestTimeout);
    }
    [Theory]
    [InlineData("exec")]
    [InlineData("import")]
    [InlineData("linked-source")]
    [InlineData("analyzers")]
    [InlineData("missing-assets")]
    [InlineData("readonly-source")]
    [InlineData("user-hook")]
    public void ReviewedProfile_MaliciousOrDriftedGraph_IsDeniedInDisposableCopy(string mode)
    {
        var temp=Path.Combine(Path.GetTempPath(),"nexus-i2b-graph-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var original=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(ReviewedRoot,"nexus-reviewed-i2b.json")))!;
            foreach(var path in original["graph"]!.AsObject().Select(p=>p.Key).Concat(original["sources"]!.AsObject().Select(p=>p.Key)))
            {var dest=Path.Combine(temp,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(Path.Combine(ReviewedRoot,path),dest);}
            original["root"]=temp;var manifestPath=Path.Combine(temp,"nexus-reviewed-i2b.json");File.WriteAllText(manifestPath,original.ToJsonString());
            var pin=new FileSnapshotService().ComputeSha256Hash(manifestPath);
            var project=Path.Combine(temp,".nexus-verification","Contracts.csproj");var xml=File.ReadAllText(project);
            switch(mode)
            {
                case "exec":File.WriteAllText(project,xml.Replace("</Project>","<Target Name=\"Malicious\" BeforeTargets=\"Build\"><Exec Command=\"unapproved\" /></Target></Project>"));break;
                case "import":File.WriteAllText(project,xml.Replace("</Project>","<Import Project=\"outside.targets\" /></Project>"));break;
                case "linked-source":File.WriteAllText(project,xml.Replace("../Nexus.Contracts/EngineSessionOptions.cs","../../outside.cs"));break;
                case "analyzers":File.WriteAllText(project,xml.Replace("<RunAnalyzers>false</RunAnalyzers>","<RunAnalyzers>true</RunAnalyzers>"));break;
                case "missing-assets":File.Delete(Path.Combine(temp,".nexus-verification/obj/Tests/project.assets.json"));break;
                case "readonly-source":File.AppendAllText(Path.Combine(temp,"Nexus.Contracts/ExecutionProvider.cs"),"// drift");break;
                case "user-hook":File.WriteAllText(Path.Combine(temp,".nexus-verification/Contracts.csproj.user"),"<Project><Target Name=\"Hook\" BeforeTargets=\"Build\"><Exec Command=\"unapproved\" /></Target></Project>");break;
            }
            Assert.ThrowsAny<Exception>(()=>ReviewedProjectVerificationManifest.LoadPinned(temp,pin));
            Assert.False(File.Exists(Path.Combine(temp,"executed.marker")));
        }
        finally{Directory.Delete(temp,true);}
    }
    [Fact]
    public void ReviewedVerification_IsAdditive_AndCannotUseArbitraryProject()
    {
        var type=typeof(SourceVerificationProfile).Assembly.GetType("Nexus.AI.EditingLite.Services.ReviewedProjectVerificationManifest");
        Assert.NotNull(type);
        var load=type.GetMethod("LoadPinned",BindingFlags.Public|BindingFlags.Static);Assert.NotNull(load);
        var error=Assert.Throws<TargetInvocationException>(()=>load.Invoke(null,[@"D:\NEXUS\Nexus-CSharp",new string('0',64)]));
        Assert.NotNull(error.InnerException);
    }
    private const string A="    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;";
    private const string B="    public int ActiveSessionsCount { get; init; }";
    private static readonly string[] Targets=["Nexus.Contracts/EngineSessionOptions.cs","Nexus.Contracts/EngineMemoryMetrics.cs"];
    private static FileSnapshot Snapshot(string text,Encoding? encoding=null)
    {
        encoding??=new UTF8Encoding(false,true);
        return new() {Content=text,Encoding=encoding,Hash=Convert.ToHexString(SHA256.HashData(encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray())).ToLowerInvariant(),AbsolutePath=@"C:\synthetic\target.cs"};
    }
    private static string Proposal(string oldA=A,string? newA=null,string oldB=B,string? newB=null)=>JsonSerializer.Serialize(new
    {a=new[] {new {old=oldA,@new=newA??A+"\n    public bool IsCpu => ExecutionProvider == ExecutionProvider.Cpu;"}},
     b=new[] {new {old=oldB,@new=newB??B+"\n    public bool HasActiveSessions => ActiveSessionsCount > 0;"}}});
    private static ImmutableArray<SourceFileEdit> Validate(string raw,string newline="\n",Encoding? encoding=null,string prefix="")
    {
        var type=typeof(Nexus.App.App).Assembly.GetType("Nexus.App.Services.Agent.NaturalLanguageSourceProposal");
        Assert.NotNull(type);
        var method=type.GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.NotNull(method);
        var files=new[] {Snapshot(prefix+"class A"+newline+"{"+newline+A+newline+"}"+newline,encoding),Snapshot("class B"+newline+"{"+newline+B+newline+"}"+newline,encoding)};
        try{return (ImmutableArray<SourceFileEdit>)method.Invoke(null,[raw,files,Targets,new[] {A,B}])!;}
        catch(TargetInvocationException e) when(e.InnerException is ArgumentException){throw (ArgumentException)e.InnerException;}
    }
    [Fact]
    public void Proposal_CreatesTwoHostMappedIntents_WithoutUserReplacementLiterals()
    {
        var files=Validate(Proposal());Assert.Equal(Targets,files.Select(f=>f.Target));
        Assert.Contains("IsCpu",files[0].Edits.Single().NewText);Assert.Contains("HasActiveSessions",files[1].Edits.Single().NewText);
        Assert.All(files,f=>Assert.Equal(64,f.BeforeHash.Length));
    }
    [Theory]
    [InlineData("{invalid")]
    [InlineData("{}")]
    [InlineData("{\"a\":[],\"b\":[]}")]
    [InlineData("{\"a\":[],\"a\":[],\"b\":[]}")]
    [InlineData("{\"a\":[{\"old\":\"x\",\"new\":\"y\",\"path\":\"outside.cs\"}],\"b\":[]}")]
    public void MalformedDuplicateOrUnknownFields_AreDenied(string raw)=>Assert.ThrowsAny<ArgumentException>(()=>Validate(raw));
    [Theory]
    [InlineData("unknown-slot")]
    [InlineData("third-slot")]
    [InlineData("no-match")]
    [InlineData("out-of-range")]
    [InlineData("no-op")]
    [InlineData("rewrite-declaration")]
    [InlineData("duplicate-edit")]
    [InlineData("overlap")]
    [InlineData("mixed-newline")]
    [InlineData("encoding")]
    [InlineData("invalid-unicode")]
    public void ProposalSecurityNegatives_DoNotBecomeHostIntents(string mode)
    {
        var raw=Proposal();
        switch(mode)
        {
            case "unknown-slot":raw=raw.Replace("\"b\":","\"targetPath\":");break;
            case "third-slot":raw=raw[..^1]+",\"c\":[{\"old\":\"x\",\"new\":\"y\"}]}";break;
            case "no-match":raw=Proposal(oldA:"missing");break;
            case "out-of-range":raw=Proposal(oldA:"class A",newA:"class A\n    public bool Unexpected => true;");break;
            case "no-op":raw=Proposal(newA:A);break;
            case "rewrite-declaration":raw=Proposal(newA:A.Replace("= ExecutionProvider.Cpu","= ExecutionProvider.Cuda")+"\n    public bool IsCpu => true;");break;
            case "duplicate-edit":case "overlap":
                raw=JsonSerializer.Serialize(new {a=new[] {new {old=A,@new=A+"\n    public bool IsCpu => true;"},new {old=mode=="overlap"?A.Trim():A,@new=A+"\n    public bool Other => true;"}},b=new[] {new {old=B,@new=B+"\n    public bool HasActiveSessions => true;"}}});break;
            case "mixed-newline":raw=Proposal(newA:A+"\r\n    public bool IsCpu => true;");break;
            case "invalid-unicode":raw=raw.Replace("IsCpu","Is\\ud800Cpu");break;
        }
        Assert.ThrowsAny<ArgumentException>(()=>Validate(raw,encoding:mode=="encoding"?Encoding.Unicode:null));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactUnicodeCrLfAndUtf8Bom_ArePreserved(bool bom)
    {
        var raw=Proposal(newA:A+"\r\n    public bool IsCpu => ExecutionProvider == ExecutionProvider.Cpu;",newB:B+"\r\n    public bool HasActiveSessions => ActiveSessionsCount > 0;");
        var edits=Validate(raw,"\r\n",new UTF8Encoding(bom,true),"// café 😀\r\n");
        Assert.All(edits,f=>Assert.Contains("\r\n",f.Edits.Single().NewText));
    }
}
