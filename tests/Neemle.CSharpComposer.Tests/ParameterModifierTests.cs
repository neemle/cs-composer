using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class ParameterModifierTests
{
    [Theory]
    [InlineData("ref readonly")]
    [InlineData("scoped ref")]
    [InlineData("scoped in")]
    public void Published_compound_parameter_modifiers_parse_typed_and_keep_version_guards(string modifier)
    {
        string source = "class C { void M(" + modifier + " int value) {} }";
        var result = Cs.Parse(source);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(modifier, Assert.IsType<CsMethod>(result.Root.Types[0].Members[0]).Parameters[0].Modifier);
        Assert.Equal(source, result.Root.Render());
        var older = Cs.Parse(source, new ParseOptions { LanguageVersion = CsLanguageVersion.CSharp7_3 });
        Assert.Contains(older.Diagnostics, error => error.Code == "GEN073");
    }
}
