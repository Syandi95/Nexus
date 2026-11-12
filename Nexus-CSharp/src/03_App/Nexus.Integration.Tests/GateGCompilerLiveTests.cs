using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateGLiveFactAttribute : FactAttribute
{
    public GateGLiveFactAttribute() { if(Environment.GetEnvironmentVariable("NEXUS_GATE_G_LIVE")!="1") Skip="Explicit Gate G structured and timing prerequisite required."; }
}
public sealed class GateGCompilerLiveTests(ITestOutputHelper output)
{
    [GateGLiveFact]
    public Task ExactCompactReadOnlyCompilerProbe_ColdWarmAndChat()=>new GateCToolLiveTests(output).ExecuteProof(true,gateG:true,probeOnly:true);
    [GateGLiveFact]
    public Task BoundCS0029_OneGenuineRepair_Below120Seconds_PreservesChat()=>new GateCToolLiveTests(output).ExecuteProof(true,gateG:true);
}
