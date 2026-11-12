using Xunit;

namespace Nexus.Integration.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class WpfApplicationCollection
{
    public const string Name = "Nexus:WpfApplication";
}