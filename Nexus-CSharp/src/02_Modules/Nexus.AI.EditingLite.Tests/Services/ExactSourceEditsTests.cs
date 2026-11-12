using System.Diagnostics;
using System.Text;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class ExactSourceEditsTests
{
    [Theory]
    [InlineData("A B C","A","X","B","Y","X Y C")]
    [InlineData("ABC","A","XX","BC","Y","XXY")]
    [InlineData("long short","long","l","short","longer","l longer")]
    [InlineData("A B C","A","B","B","D","B D C")]
    [InlineData("α\r\nβ\n😀!","α","ζ","β","γ","ζ\r\nγ\n😀!")]
    public void DisjointOriginalChanges_AreCanonicalAndPreserveUntouchedText(string source,string old1,string new1,string old2,string new2,string desired)
    {
        var first=new SourceTextEdit(old1,new1);var second=new SourceTextEdit(old2,new2);
        var resolved=ExactSourceEdits.Resolve(source,[first,second]);var reversed=ExactSourceEdits.Resolve(source,[second,first]);
        Assert.Equal(desired,resolved.DesiredContent);Assert.Equal(desired,reversed.DesiredContent);Assert.Equal(resolved.Edits.ToArray(),reversed.Edits.ToArray());
        Assert.Equal(new[] { 0,1 },resolved.Edits.Select(e=>e.Ordinal));
        foreach(var e in resolved.Edits) Assert.Equal(e.OldText,source.Substring(e.Start,e.Length));
    }
    [Fact]
    public void OneEdit_AndAllStartCounting_AreExact()
    {
        Assert.Equal("A X C",ExactSourceEdits.Resolve("A B C",[new("B","X")]).DesiredContent);
        Assert.Equal(2,new ProposalService().CountLiteralMatches("aaa","aa"));
    }
    [Theory]
    [InlineData("A B","Z","X","A","Y","NoMatch")]
    [InlineData("A A B","A","X","B","Y","MultipleMatches")]
    [InlineData("aaa B","aa","X","B","Y","MultipleMatches")]
    [InlineData("ABC","AB","X","BC","Y","OverlappingEdits")]
    [InlineData("ABC","ABC","X","B","Y","OverlappingEdits")]
    [InlineData("ABC","AB","X","AB","Y","OverlappingEdits")]
    [InlineData("ABC","AB","X","AB","X","DuplicateEdit")]
    [InlineData("A B C","A","X","X","Y","NoMatch")]
    [InlineData("ABC","A","AB","BC","C","NoChange")]
    [InlineData("ABC","A","A","B","Y","InvalidLiteral")]
    [InlineData("ABC","","X","B","Y","InvalidLiteral")]
    [InlineData("ABC","A","","B","Y","InvalidLiteral")]
    public void InvalidConstituent_FailsWholeResolution(string source,string old1,string new1,string old2,string new2,string reason)
        =>Assert.Equal(reason,Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve(source,[new(old1,new1),new(old2,new2)])).ReasonCode);
    [Fact]
    public void CountsBytesEncodingAndSize_AreBounded()
    {
        Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve("ABC",[]));
        Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve("ABC",[new("A","X"),new("B","Y"),new("C","Z")]));
        Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve("😀",[new("\ud83d","X")]));
        Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve("A",[new("A",new string('X',257))]));
        Assert.Equal("LiteralByteLimit",Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve(new string('α',256),[new(new string('α',256),new string('β',256))],1023)).ReasonCode);
        Assert.Equal("SourceSizeLimit",Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve(new string('A',4097),[new("A","B")])).ReasonCode);
        Assert.Equal("DesiredSizeLimit",Assert.Throws<SourceEditValidationException>(()=>ExactSourceEdits.Resolve("X"+new string('A',4095),[new("X","XX")])).ReasonCode);
    }
    [Fact]
    public async Task BomMixedNewlines_ProposalIntegrityAndLimits()
    {
        using var f=await SourcePlanFixture.Create();var file=Path.Combine(f.Root,"Product/Alpha.cs");
        var source="namespace SourceFixture;\r\npublic static class Alpha { public static int Value() { return 1; } }\n";
        File.WriteAllText(file,source,new UTF8Encoding(true,true));
        var workspace=ApprovedTextWorkspace.ForSourceFiles(f.Root,["Product/Alpha.cs","Product/Beta.cs"]);
        var hash=new FileSnapshotService().ComputeSha256Hash(file);
        var edits=new[] { new SourceTextEdit("int Value()","int Value() => Core(); private static int Core()"),new SourceTextEdit("return 1;","return 2;") };
        var proposal=await f.Editing.PrepareSourceWriteAsync(workspace,"Product/Alpha.cs",hash,edits,Guid.NewGuid(),"test",new(),CancellationToken.None);
        var before=File.ReadAllBytes(file);var approval=WriteApprovalDecision.For(proposal,true);
        var applied=await f.Editing.ApplyApprovedSafeWriteAsync(proposal,approval,CancellationToken.None);
        Assert.Equal(SafeWriteOutcome.Success,applied.Outcome);Assert.True(File.ReadAllBytes(file).AsSpan().StartsWith(new byte[] { 239,187,191 }));
        Assert.Contains("\r\n",File.ReadAllText(file));Assert.EndsWith("\n",File.ReadAllText(file));Assert.Equal(proposal.DesiredHash,new FileSnapshotService().ComputeSha256Hash(file));
        Assert.Equal(before,File.ReadAllBytes(file+".nexus-edit-"+proposal.OperationId.ToString("N")+".bak"));
        var tampered=proposal with { ResolvedEdits=proposal.ResolvedEdits.SetItem(0,proposal.ResolvedEdits[0] with { Start=1 }) };
        Assert.Equal(SafeWriteOutcome.ApprovalDenied,(await f.Editing.ApplyApprovedSafeWriteAsync(tampered,WriteApprovalDecision.For(tampered,true),CancellationToken.None)).Outcome);
    }
    [Fact]
    public void ResolverCost_IsMeasuredWithoutMutation()
    {
        var watch=Stopwatch.StartNew();for(var i=0;i<10000;i++) ExactSourceEdits.Resolve("int Value() { return 1; }",[new("int Value()","int Core()"),new("return 1;","return 2;")]);
        Assert.True(watch.Elapsed<TimeSpan.FromSeconds(5));
    }
}
