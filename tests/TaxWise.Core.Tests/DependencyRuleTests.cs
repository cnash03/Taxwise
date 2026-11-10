using TaxWise.Core;

namespace TaxWise.Core.Tests;

public sealed class DependencyRuleTests
{
    // BUILD_PLAN §2: Core is pure domain code and must not reference any SDK (Anthropic, Voyage, Qdrant, ...).
    [Fact]
    public void Core_ReferencesOnlyFrameworkAndShared()
    {
        var references = typeof(CoreAssembly).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                        && n != "netstandard"
                        && n != "Microsoft.CSharp");

        Assert.All(references, name => Assert.Equal("TaxWise.Shared", name));
    }
}
