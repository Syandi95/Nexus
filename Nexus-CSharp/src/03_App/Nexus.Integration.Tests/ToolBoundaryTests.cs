using System.Text.Json;
using Moq;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class ToolBoundaryTests
{
    private const string EmptySchema = "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}";
    private const string ScalarSchema = """
        {"type":"object","properties":{"kind":{"type":"string","enum":["active"],"maxLength":16},"count":{"type":"integer","minimum":1,"maximum":3}},"required":["kind"],"additionalProperties":false}
        """;
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);
    private static ToolExecutionContext Context() => new(new AgentRunContext(Guid.NewGuid(), "session", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddSeconds(10),64,new AgentModelSnapshot("logical-model","private-path",123,"private-endpoint",1)));

    [Fact]
    public void ExplicitRegistry_SnapshotsMetadataAndRejectsDuplicate()
    {
        var tool = new TestTool();
        var registry = new ToolRegistry([tool]);
        Assert.Single(registry.Descriptors);
        tool.Metadata = tool.Metadata with { Name="changed", Risk=ToolRisk.Write, ReadOnly=false };
        Assert.Equal("test_snapshot",registry.Descriptors[0].Name);
        Assert.True(registry.Descriptors[0].ReadOnly);
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool(),new TestTool()]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("test_snapshot ")]
    [InlineData("test_snapshot\n")]
    [InlineData("téšt")]
    [InlineData("Test")]
    public void Registry_RejectsInvalidName(string name) =>
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { Name=name } }]));

    [Fact]
    public void Registry_RejectsInvalidSchemaRiskAndLimits()
    {
        foreach(var schema in new[] { "{}", "[]", "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":true}", "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"object\"}},\"required\":[],\"additionalProperties\":false}" })
            Assert.ThrowsAny<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { ArgumentSchema=Json(schema) } }]));
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { Risk=(ToolRisk)99 } }]));
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { ReadOnly=false } }]));
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { Timeout=TimeSpan.Zero } }]));
        Assert.Throws<ArgumentException>(()=>new ToolRegistry([new TestTool { Metadata=new TestTool().Metadata with { MaxResultSize=255 } }]));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("[]")]
    [InlineData("{\"kind\":3}")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"wrong\"}")]
    [InlineData("{\"kind\":\"active\",\"count\":4}")]
    [InlineData("{\"kind\":\"active\",\"kind\":\"active\"}")]
    [InlineData("{\"kind\":\"active\",\"risk\":\"safe\"}")]
    [InlineData("{\"kind\":\"active\",\"allowWrite\":true}")]
    [InlineData("{\"kind\":\"active\",\"requiresApproval\":false}")]
    [InlineData("{\"kind\":\"active\",\"count\":\"2\"}")]
    [InlineData("{\"kind\":\"active\",\"count\":1.5}")]
    [InlineData("{\"kind\":\"aaaaaaaaaaaaaaaaa\"}")]
    [InlineData("{\"kind\":{\"nested\":{\"deep\":{\"value\":\"active\"}}}}")]
    public async Task InvalidArguments_NeverReachPolicyExecution(string raw)
    {
        var tool=new TestTool { Metadata=new TestTool().Metadata with { ArgumentSchema=Json(ScalarSchema) } };
        var executor=Executor(tool,["test_snapshot"]);
        var result=await executor.ExecuteAsync(Context(),"call-1","test_snapshot",raw,CancellationToken.None);
        Assert.Equal(ToolOutcome.InvalidArguments,result.Result.Outcome);
        Assert.Equal("InvalidArguments",result.Policy.ReasonCode);
        Assert.Equal(0,tool.Calls);
    }

    [Theory]
    [InlineData("test_snapshot ")]
    [InlineData("test_snapshot\n")]
    [InlineData("test_snapshοt")]
    [InlineData("test_snapshot_lookalike")]
    [InlineData("powershell")]
    public async Task UnknownOrSimilarName_DefaultDeny(string name)
    {
        var tool=new TestTool();
        var result=await Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"call-1",name,"{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.ToolNotFound,result.Result.Outcome);
        Assert.False(result.Policy.Allowed);
        Assert.Equal(0,tool.Calls);
    }

    [Theory]
    [InlineData(ToolRisk.Write)]
    [InlineData(ToolRisk.Process)]
    [InlineData(ToolRisk.SystemCritical)]
    public async Task WriteOrProcessMetadata_DeniedEvenIfNameAllowlisted(ToolRisk risk)
    {
        var tool=new TestTool { Metadata=new TestTool().Metadata with { Risk=risk,ReadOnly=false } };
        var result=await Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"call-1",tool.Descriptor.Name,"{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.Denied,result.Result.Outcome);
        Assert.Equal("WriteOrProcessNotAllowed",result.Policy.ReasonCode);
        Assert.Equal(0,tool.Calls);
    }

    [Fact]
    public async Task ReadOnlyAllowedAndUnallowedNames_AreIndependentOfPromptOrMetadataArguments()
    {
        var tool=new TestTool();
        var allowed=await Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"call-1",tool.Descriptor.Name,"{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.Success,allowed.Result.Outcome);
        var denied=await Executor(tool,[]).ExecuteAsync(Context(),"call-2",tool.Descriptor.Name,"{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.Denied,denied.Result.Outcome);
        Assert.Equal(1,tool.Calls);
        Assert.True(allowed.ResultLength<=tool.Descriptor.MaxResultSize);
    }

    [Fact]
    public async Task OversizedArgumentAndResult_AreRejectedWithoutSendingLargePayload()
    {
        var tool=new TestTool { Metadata=new TestTool().Metadata with { ArgumentSchema=Json(ScalarSchema) } };
        var rejected=await Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"id",tool.Descriptor.Name,
            "{\"kind\":\""+new string('x',5000)+"\"}",CancellationToken.None);
        Assert.Equal(ToolOutcome.InvalidArguments,rejected.Result.Outcome);
        Assert.Equal(0,tool.Calls);
        tool=new TestTool { Body=(_,_)=>Task.FromResult(new ToolExecutionResult(ToolOutcome.Success,JsonSerializer.SerializeToElement(new string('x',5000)))) };
        var tooLarge=await Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"id",tool.Descriptor.Name,"{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.ResultTooLarge,tooLarge.Result.Outcome);
        Assert.Null(tooLarge.Result.Data);
        Assert.True(tooLarge.ResultLength<tool.Descriptor.MaxResultSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndCancellation_AreTypedAndOperationTokenIsCancelled(bool callerCancel)
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool=new TestTool { Metadata=new TestTool().Metadata with { Timeout=TimeSpan.FromMilliseconds(80) }, Body=async (_,token)=>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite,token); }
            finally { cancelled.SetResult(); }
            return new(ToolOutcome.Success);
        }};
        using var source=new CancellationTokenSource();
        var task=Executor(tool,[tool.Descriptor.Name]).ExecuteAsync(Context(),"id",tool.Descriptor.Name,"{}",source.Token);
        await entered.Task;
        if(callerCancel) source.Cancel();
        var result=await task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(callerCancel ? ToolOutcome.Cancelled : ToolOutcome.Timeout,result.Result.Outcome);
    }

    private static ToolExecutor Executor(TestTool tool,IEnumerable<string> allowed) =>
        new(new ToolRegistry([tool]),new ReadOnlyToolPolicy(allowed),Mock.Of<ILoggerService>());

    [Fact]
    public async Task ExecutionFailureAndLogs_DoNotExposePrivateArgumentsResultsOrExceptionText()
    {
        var logs=new List<string>();
        var logger=new Mock<ILoggerService>();
        logger.Setup(l=>l.LogInfo(It.IsAny<string>(),It.IsAny<string>())).Callback<string,string>((_,message)=>logs.Add(message));
        var tool=new TestTool { Metadata=new TestTool().Metadata with { ArgumentSchema=Json("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\",\"maxLength\":64}},\"required\":[\"value\"],\"additionalProperties\":false}") },
            Body=(_,_)=>throw new InvalidOperationException("private-path-secret") };
        var executor=new ToolExecutor(new ToolRegistry([tool]),new ReadOnlyToolPolicy([tool.Descriptor.Name]),logger.Object);
        var result=await executor.ExecuteAsync(Context(),"call",tool.Descriptor.Name,"{\"value\":\"argument-secret\"}",CancellationToken.None);
        Assert.Equal(ToolOutcome.ExecutionFailure,result.Result.Outcome);
        Assert.DoesNotContain(logs,l=>l.Contains("argument-secret",StringComparison.Ordinal)||l.Contains("private-path-secret",StringComparison.Ordinal));
        Assert.DoesNotContain("private-path-secret",ToolExecutor.SerializeResult(result.Result));
    }
    private sealed class TestTool : INexusTool
    {
        public ToolDescriptor Metadata { get; set; }=new("test_snapshot","Read-only test",ToolRisk.ReadOnly,true,TimeSpan.FromSeconds(2),1024,Json(EmptySchema));
        public ToolDescriptor Descriptor=>Metadata;
        public int Calls;
        public Func<ToolExecutionContext,CancellationToken,Task<ToolExecutionResult>> Body { get; set; }=(_,_)=>Task.FromResult(new ToolExecutionResult(ToolOutcome.Success,Json("{\"logical\":\"value\"}")));
        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context,JsonElement arguments,CancellationToken token) { Calls++; return Body(context,token); }
    }
}
