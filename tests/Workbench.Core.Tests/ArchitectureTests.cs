namespace Workbench.Core.Tests;

using Workbench.Core;
using Xunit;

public sealed class ArchitectureTests
{
    [Fact]
    public void CoreHasNoInfrastructureDependencies()
    {
        Assert.DoesNotContain(
            typeof(Foundation).Assembly.GetReferencedAssemblies(),
            reference => reference.Name is not null &&
                (reference.Name.StartsWith("Workbench.", StringComparison.Ordinal) ||
                 reference.Name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                 reference.Name == "System.CommandLine"));
    }
}
