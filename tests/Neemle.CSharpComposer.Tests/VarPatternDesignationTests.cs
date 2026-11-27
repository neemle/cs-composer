using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
public sealed class VarPatternDesignationTests
{
    [Theory]
    [InlineData("return pair is var (first, second);")]
    [InlineData("return pair is var ((first, second), third);")]
    public void Var_patterns_have_recursive_symbol_designations(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        FormatCheck.FixedPoint(parsed.Root);
        Assert.Empty(Cs.ParseTree(parsed.Root.Reformat().Render()).Diagnostics);
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsPattern { Designation: not null });
    }

    // Integration: these tests build or run composed source with the real compiler host.
    [Trait("Category", "Integration")]
    public sealed class Compiler
    {
        [Fact]
        public async Task Deconstructing_var_patterns_compile_and_execute()
        {
            const string body = "var pair=(1,2); if(pair is var (first,second)) { return first+second; } return 0;";
            var parsed = Cs.ParseTree(body, CsLanguageVersion.CSharp8);
            Assert.Empty(parsed.Diagnostics);
            await CompilerFixture.Run("class Program { static int Run() { " + parsed.Root.Reformat().Render() +
                " } static void Main() { if(Run()!=3) throw new System.Exception(); } }", "8.0");
        }
    }
}
