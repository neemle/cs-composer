using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class IndependenceTests
{
    [Fact(DisplayName = "[SEC][UC-003] Engine and unit assembly references exclude Roslyn and generator")]
    public void Target_assemblies_remain_independent()
    {
        foreach (var assembly in new[] { typeof(CsDocument).Assembly, typeof(IndependenceTests).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
                reference.Name.StartsWith("Neemle.GeneratorEngine", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(CsDocument).GetMethods(), method => method.Name == "ToSourceText");
    }
}
