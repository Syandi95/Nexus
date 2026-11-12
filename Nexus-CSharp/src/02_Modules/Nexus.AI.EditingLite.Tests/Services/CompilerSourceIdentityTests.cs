using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class CompilerSourceIdentityTests
{
    [Theory]
    [InlineData("MappedLine")]
    [InlineData("HashMismatch")]
    [InlineData("LinkedCompile")]
    public async Task RemainingSourceIdentityCases_AreIneligible(string scenario)
    {
        using var f=await SourcePlanFixture.Create();
        if(scenario=="LinkedCompile")
        {
            var project=f.PathOf("Product/Product.csproj"); File.WriteAllText(project,File.ReadAllText(project).Replace("<Compile Include=\"Alpha.cs\"/>","<Compile Include=\"../Tests/Alpha.cs\" Link=\"Alpha.cs\"/>"));
            Assert.Throws<ArgumentException>(()=>new SourceVerificationProfile(f.Profile.Id,f.Root,SourcePlanFixture.Sdk,f.Profile.Targets,observeCompiler:true)); return;
        }
        if(scenario=="MappedLine") File.WriteAllText(f.PathOf("Product/Alpha.cs"),"#line 200 \"Beta.cs\"\n"+File.ReadAllText(f.PathOf("Product/Alpha.cs")));
        var profile=new SourceVerificationProfile(f.Profile.Id,f.Root,SourcePlanFixture.Sdk,f.Profile.Targets,observeCompiler:true);
        var run=f.Control(); var change="return \"two\";";
        var plan=await f.Editing.PrepareSourcePlanAsync(profile,[new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;",change),new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],Guid.NewGuid(),"identity-A",new(),run);
        var receiptCaptured=false;
        f.Editing.SourceVerifier.CompilerProbe=artifacts=>
        {
            if(scenario=="HashMismatch") artifacts.LedgerProbe=_=>
            {
                // Build sources are still locked, preventing external changes.
                // A mismatched approved desired identity must never mint a fact.
                var proposal=plan.Entries[0]; typeof(SafeWriteProposal).GetProperty("DesiredHash")!.SetValue(proposal,new string('0',64));
            };
            receiptCaptured=true;
        };
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,new SourceApproval(),run);
        Assert.True(receiptCaptured); Assert.Null(result.Build!.CompilerFailure); Assert.Null(result.Tests);
        Assert.NotEqual(SourcePlanOutcome.Success,result.Outcome);
    }
}
