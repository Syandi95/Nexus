using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateDLiveFactAttribute : FactAttribute
{
    public GateDLiveFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXUS_GATE_D_LIVE")!="1") Skip="Explicit Gate D shared-model orchestration fixtures required.";
    }
}

public sealed class GateDAgentLiveTests(ITestOutputHelper output)
{
    [GateDLiveFact]
    public Task TwoReadOnlySources_VerifyWithSharedRuntimeAndCancellationIsolation()=>new GateCToolLiveTests(output).ExecuteProof(true);
}
