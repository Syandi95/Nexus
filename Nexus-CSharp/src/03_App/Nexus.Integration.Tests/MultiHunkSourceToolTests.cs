using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class MultiHunkSourceToolTests
{
    internal const string Signature="int Value() => Core(); private static int Core()";
    internal static ImmutableArray<SourceFileEdit> Edits(SourcePlanTestWorkspace f,bool fail=false)=>[
        new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),new[] { new SourceTextEdit("int Value()",Signature),new SourceTextEdit("return 1;",fail ? "return \"two\";" : "return 2;") }),
        new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")];
    internal static string Arguments(IEnumerable<SourceFileEdit> files)=>JsonSerializer.Serialize(files.Select((f,i)=>new { key=((char)('a'+i)).ToString(),edits=f.Edits.Select(e=>new { old=e.OldText,@new=e.NewText }) }).ToDictionary(f=>f.key,f=>f.edits));

    [Fact]
    public async Task ExplicitArrayRegistration_ClosedArguments_NeverPrepareOnInvalidInput()
    {
        using var f=await SourcePlanTestWorkspace.Create();var approval=new Approval();var log=Mock.Of<ILoggerService>();
        var catalog=new SourcePlanCatalog(f.Profile);var tool=new ApplySourcePlanTool(catalog,f.Editing,approval,log);
        var registry=new ToolRegistry([tool]);var executor=new ToolExecutor(registry,new ReadOnlyToolPolicy([],sourceCatalog:catalog),log);
        var good=Arguments(Edits(f));Assert.True(SourcePlanArgumentValidator.TryValidate(good,out _));
        var single=Arguments([new SourceFileEdit("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),Edits(f)[1]]);
        Assert.True(SourcePlanArgumentValidator.TryValidate(single,out _));
        Assert.Throws<ArgumentException>(()=>ToolArgumentValidator.ValidateSchema(tool.Descriptor.ArgumentSchema));
        var a="{\"old\":\"return 1;\",\"new\":\"return 2;\"}";
        var invalid=new[] {
            "{bad", "{\"a\":[],\"b\":["+a+"]}",
            "{\"a\":["+a+","+a+","+a+"],\"b\":["+a+"]}",
            "{\"a\":["+a+",{\"old\":\"int Value()\",\"new\":\"int Core()\"}],\"b\":["+a+",{\"old\":\"int Value()\",\"new\":\"int Core()\"}]}",
            single.Replace("\"old\":\"return 1;\"","\"old\":null"),single.Replace("\"old\":\"return 1;\"","\"old\":42"),
            single.Replace("\"old\":\"return 1;\"","\"old\":true"),single.Replace("\"old\":\"return 1;\"","\"old\":{}"),
            single.Replace("\"old\":\"return 1;\"","\"old\":\"\""),single.Replace("\"new\":\"return 2;\"","\"new\":\"\""),
            single.Replace("\"old\":\"return 1;\"","\"old\":\"return 1;\",\"old\":\"return 1;\""),
            single.Replace("\"a\":","\"a\":null,\"a\":"),single.Replace("\"b\":","\"z\":"),
            single.Replace("\"old\":\"return 1;\"","\"old\":\"return 1;\",\"path\":\"foreign.cs\""),
            single[..^1]+",\"approval\":true}",single[..^1]+",\"profile\":\"foreign\"}",single[..^1]+",\"limits\":100}",single[..^1]+",\"command\":\"build\"}",
            single[..^1]+",\"c\":["+a+"]}" };
        foreach(var raw in invalid)
        {
            var run=NativeToolSelectionTests.Context() with { SourceIntent=new(f.Profile.Id,Edits(f)) };
            var result=await executor.ExecuteAsync(new(run),"bad-call",ApplySourcePlanTool.ToolName,raw,CancellationToken.None);
            Assert.Equal(ToolOutcome.InvalidArguments,result.Result.Outcome);Assert.Null(run.Progress!.SourcePlan);
            Assert.Equal(0,approval.Calls);Assert.Equal(0,run.Progress.BuildStartCount);Assert.Equal(0,run.Progress.TestStartCount);
            Assert.All(Edits(f),e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
        }
    }
    internal sealed class Approval : IWriteApprovalProvider
    {
        internal int Calls;internal SourcePlanApproval? First;
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)
        { token.ThrowIfCancellationRequested();Calls++;var approval=SourcePlanApproval.For(plan,true);if(First is not null) Assert.False(First.Matches(plan));First??=approval;return Task.FromResult(approval); }
    }
}
