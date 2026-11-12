using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class FullSyntaxCompilerTests
{
    [Fact]
    public async Task Unsafe_code_function_pointers_ref_values_fixed_buffers_and_native_integers_compile()
    {
        const string source = "using System; unsafe struct Buffer { public fixed int Data[4]; } unsafe class Program { " +
            "static int Add(int x, int y) => x+y; static int Run() { delegate* managed<int,int,int> f = &Add; int value=2; " +
            "TypedReference tr = __makeref(value); __refvalue(tr,int)=4; Buffer buffer=default; buffer.Data[0]=3; " +
            "nint native=1; return f(value,buffer.Data[0]) + (int)native + (__reftype(tr)==typeof(int) ? 1 : 0); } " +
            "static void Main() { if(Run()!=9) throw new Exception(); } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "14.0");
    }

    [Fact]
    public async Task Field_backed_properties_unbound_nameof_and_partial_members_compile()
    {
        const string source = "using System; partial class C { public partial int Value {get;set;} public partial C(); } " +
            "partial class C { public partial int Value { get => field; set { field=value; } } public partial C() { Value=3; } } " +
            "class Program { static void Main() { var c=new C(); if(c.Value!=3 || nameof(System.Collections.Generic.List<>)!=\"List\") throw new Exception(); } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "14.0");
    }

    [Fact]
    public async Task Preview_memory_safety_expression_has_an_explicit_version_gate()
    {
        const string source = "class Program { static int value = unsafe(Read()); static unsafe int Read() => 3; static void Main() { if(value!=3) throw new System.Exception(); } }";
        Assert.Contains(Cs.Parse(source).Diagnostics, d => d.Code == "GEN073");
        var parsed = Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.Preview });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "preview");
    }
}
