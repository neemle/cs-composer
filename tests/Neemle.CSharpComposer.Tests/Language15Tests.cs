using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class Language15Tests
{
    [Theory]
    [InlineData(703)]
    [InlineData(800)]
    [InlineData(900)]
    [InlineData(1000)]
    [InlineData(1100)]
    [InlineData(1200)]
    [InlineData(1300)]
    [InlineData(1400)]
    [InlineData(1500)]
    public void Every_explicit_supported_target_still_builds_the_typed_subset(int numeric)
    {
        var version = (CsLanguageVersion)numeric;
        var document = Cs.Document(version).Class("C").Public().Field<int>("Value").Public().End().End().Build();
        Assert.Equal(version, document.LanguageVersion);
        Assert.Empty(document.Validate());
        Assert.Contains("public int Value;", document.Render());
    }

    [Fact]
    public void Fifteen_is_the_default_but_unmodeled_syntax_remains_opaque()
    {
        Assert.Equal(CsLanguageVersion.CSharp15, new CsDocument().LanguageVersion);
        Assert.Equal(CsLanguageVersion.CSharp15, new ParseOptions().LanguageVersion);
        Assert.Equal(CsLanguageVersion.CSharp15, new ParseContext(ParseKind.Document).LanguageVersion);
        var unknown = Cs.Parse("public union Pet(Cat, Dog);");
        Assert.NotEqual(ParseCoverage.Typed, unknown.Coverage);
        Assert.Contains(unknown.Root.Items, item => item is CsRaw);
        Assert.Equal("public union Pet(Cat, Dog);", unknown.Root.Render());
        Assert.Throws<GenValidationException>(() => Cs.Document((CsLanguageVersion)1600));
    }
}
