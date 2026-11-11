using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class RawContextTests
{
    [Fact]
    public void Existing_raw_forms_use_explicit_context_and_failure_preserves_node()
    {
        Assert.IsType<CsExpression>(Cs.RawExpression("a + 2").Parse(new(ParseKind.Expression)).Root);
        Assert.IsType<CsBody>(Cs.RawBody("return 2;").Parse(new(ParseKind.Statements)).Root);
        Assert.IsType<CsMethod>(Cs.RawMember("int M() { return 2; }").Parse(new(ParseKind.Member)).Root);
        var raw = Cs.RawMember("int P { get; init; }");
        var unsupported = raw.Parse(new(ParseKind.Member, CsLanguageVersion.CSharp7_3));
        Assert.Same(raw, unsupported.Root);
        Assert.Contains(unsupported.Diagnostics, d => d.Code == "GEN073");
        var partial = new CsRaw("class C {} delegate void D();");
        Assert.Same(partial, partial.Parse(new(ParseKind.Document)).Root);
    }
}
