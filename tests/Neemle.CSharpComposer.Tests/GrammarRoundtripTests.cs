using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class GrammarRoundtripTests
{
    [Theory]
    [InlineData("using System; [assembly: Marker(1, Name = \"x\")] [assembly: Other] public class C { }")]
    [InlineData("namespace Demo { [Marker] public partial class Box<T> : Base where T : class, new() { int value; public Box(int x) : base(x) { value = x; } public T Item { get; init; } public int Count => value + 1; public event Action Changed; public class Nested {} public enum E { A = 1, B = 2, } } }")]
    [InlineData("public record R(int X); public record class C(string Name) { } public readonly record struct S(int Value); public interface I<out T> { T Get(); int Count { get; } } public struct V { public int X; }")]
    [InlineData("class C { public U Echo<U>(U item) where U : class { using (var scope = Open()) { try { if (item != null) { return item; } else { throw Create(); } } catch (Exception error) { throw; } finally { Close(); } } } }")]
    [InlineData("unsafe class C { int* Pointer; (int X, bool Y)? Pair; global::System.Collections.Generic.List<string[]> Items; public event Action E { add { Register(value); } remove { Remove(value); } } }")]
    public void Supported_model_constructs_are_typed_and_lossless(string source)
    {
        var result = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Typed, result.Coverage);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.Root.Descendants(), x => x.Node is CsRaw or CsRawMember);
        Assert.Equal(source, result.Root.Render());
    }

    [Fact]
    public void Constructed_document_parses_into_editable_property_and_statement_nodes()
    {
        var built = Cs.Document().Class("C").Public().Property<int>("Value").Get().Init().End()
            .Method<int>("Compute").Public().Body(Cs.Var("total", Cs.Binary(CsOperator.Multiply, Cs.Literal(3), Cs.Literal(4))),
                Cs.Return(Cs.Id("total"))).End().End().Build();
        var parsed = Cs.Parse(built.Render());
        Assert.Empty(parsed.Diagnostics);
        Assert.IsType<CsProperty>(parsed.Root.Types[0].Members[0]);
        var method = Assert.IsType<CsMethod>(parsed.Root.Types[0].Members[1]);
        Assert.Equal(StatementKind.Local, method.Body!.Statements[0].Kind);
        Assert.Equal(built.Render(), parsed.Root.Render());
    }
}
