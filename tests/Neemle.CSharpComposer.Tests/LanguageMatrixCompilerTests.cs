using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LanguageMatrixCompilerTests
{
    public static IEnumerable<object[]> Programs()
    {
        yield return new object[] { CsLanguageVersion.CSharp7_3, "7.3", "using System; class Program { static void Main() { int[] a=new int[] {1,2}; object boxed=a[0]; Func<int,int> f=x=>x+1; if((boxed is int n ? f(n) : default(int))!=2) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp8, "8.0", "using System; class Program { static void Main() { using var ms=new System.IO.MemoryStream(); string text=null; text ??= \"ok\"; if(text.Length!=2 || new[]{1,2,3}[1..].Length!=2) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp9, "9.0", "using System; record R(int Value); class Program { static void Main() { var a=new R(1); var b=a with { Value=2 }; Func<int,int> f=static x=>x+1; if(b is not {Value: > 1} || f(1)!=2) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp10, "10.0", "global using System; namespace Demo; record struct R(int Value); class Program { static void Main() { var f=(int x)=>x+1; if(f(new R(1).Value)!=2) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp11, "11.0", "using System; class C { public required int Value {get;init;} } class Program { static void Main() { var c=new C {Value=2}; var text=\"\"\"raw\"\"\"; var bytes=\"hi\"u8; if(c.Value+bytes.Length+text.Length!=7) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp12, "12.0", "using System; class C(int x) { public int Value=>x; } class Program { static void Main() { int[] values=[1,2]; var f=(int x=1)=>x+1; if(new C(values[1]).Value+f()!=4) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp13, "13.0", "using System; class Program { static int Sum(params ReadOnlySpan<int> values) => values[0]+values[1]; static void Accept<T>(T value) where T : allows ref struct { } static void Main() { Accept(new Span<int>(new int[1])); if(Sum(1,2)!=3) throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp14, "14.0", "using System; class C { public int Value {get=>field;set{field=value;}} } static class E { extension(C source) { public int Twice=>source.Value*2; } } class Program { static void Main() { C c=null; c?.Value=3; c=new C {Value=2}; if(c.Twice!=4 || nameof(System.Collections.Generic.List<>)!=\"List\") throw new Exception(); } }" };
        yield return new object[] { CsLanguageVersion.CSharp15, "15.0", "using System; using System.Collections.Generic; union Result(int,string); class Program { static void Main() { List<int> values=[with(capacity:4),1,2]; Result r=values[1]; int n=r switch {int x=>x,string s=>s.Length}; if(n+values.Capacity!=6) throw new Exception(); } }" };
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task Each_stable_language_target_compiles_and_executes_its_structured_features(CsLanguageVersion version, string language, string source)
    {
        var parsed = Cs.Parse(source, new() { LanguageVersion = version });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), language);
    }
}
