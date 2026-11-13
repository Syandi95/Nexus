using System.Reflection;
using Nexus.Contracts.BrowserAgent;
using Xunit;

namespace Nexus.Contracts.Tests;

public sealed class BrowserAgentContractArchitectureTests
{
    [Fact]
    public void BrowserAgentContracts_ResideInLayer0ContractsAssembly_WithZeroWpfDependency()
    {
        var contractsAsm = typeof(IBrowserAutomation).Assembly;
        Assert.Equal("Nexus.Contracts", contractsAsm.GetName().Name);
        Assert.Equal(contractsAsm, typeof(ISearchProvider).Assembly);
        Assert.Equal(contractsAsm, typeof(IWorkspaceFileService).Assembly);
        Assert.Equal(contractsAsm, typeof(BrowserActionOutcome).Assembly);
        Assert.Equal(contractsAsm, typeof(ActionVerificationStatus).Assembly);
        Assert.Equal(contractsAsm, typeof(BrowserActionBarrier).Assembly);
        Assert.Equal(contractsAsm, typeof(AdGuardPersistentConfig).Assembly);

        var referencedNames = contractsAsm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(referencedNames, n => n.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedNames, n => n.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BrowserActionBarrier_EnforcesAdmissionAndReleaseInLayer0()
    {
        var barrier = new BrowserActionBarrier();
        Assert.False(barrier.IsClosed);
        using (var lease = barrier.EnterOrThrow())
        {
            Assert.Equal(1, barrier.PendingCount);
        }
        Assert.Equal(0, barrier.PendingCount);
    }
}
