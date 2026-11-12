using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class DeclarationSyntaxTests
{
    [Theory]
    [InlineData("public delegate TResult Factory<in T, out TResult>(T value) where T : class;")]
    [InlineData("class C { public int this[int i] { get => i; set { } } }")]
    [InlineData("class C : I { int I.this[int i] => i; int I.Value => 1; void I.Run() { } }")]
    [InlineData("class C { ~C() { Cleanup(); } }")]
    [InlineData("struct C { public static C operator +(C x, C y) => x; public static explicit operator int(C x) => 1; }")]
    [InlineData("struct C { public static explicit operator checked int(C x) => 1; public void operator +=(int x) { } }")]
    [InlineData("class C(int x) : Base(x) { public int Value => x; }")]
    [InlineData("static class E { extension<T>(System.Collections.Generic.IEnumerable<T> source) { public int Count => source.Count(); } }")]
    [InlineData("unsafe class C { delegate* unmanaged[Cdecl]<int, void> callback; }")]
    [InlineData("class C { object M() => typeof(System.Collections.Generic.Dictionary<,>); }")]
    [InlineData("class C { Outer<int>.Inner<string> field; ref readonly int M() => ref field.Value; }")]
    public void Declarations_are_typed_and_reformat_without_raw(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        Assert.Equal(source, parsed.Root.Render());
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
    }

    [Fact]
    public async Task Indexers_explicit_interfaces_operators_and_primary_constructors_compile()
    {
        const string source = "using System; public delegate int Map(int x); interface I { int this[int i] {get;} int Value {get;} } " +
            "class Base(int x) { public int X = x; } class C(int x) : Base(x), I { int I.this[int i] => x + i; int I.Value => x; } " +
            "struct Number(int x) { public int Value = x; public static Number operator +(Number x, Number y) => new Number(x.Value + y.Value); " +
            "public static explicit operator int(Number x) => x.Value; } " +
            "class Program { static void Main() { I c = new C(3); Map f = x => x + 1; if(c[2] + c.Value + (int)(new Number(2) + new Number(3)) + f(1) != 15) throw new Exception(); } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "14.0");
    }
}
