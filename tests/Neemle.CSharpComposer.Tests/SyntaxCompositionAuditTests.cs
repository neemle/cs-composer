using System.Text;
using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SyntaxCompositionAuditTests
{
    public static IEnumerable<object[]> DirectiveBoundaries()
    {
        yield return new object[] { "using System; using System.Linq; class Program { static int F(int x)=>x; static void Main() { int[] values={1,2,3}; var result=(from x in values where x>1 orderby x descending select x).ToArray(); Func<int,int> f=static x=>x+1; for(int i=0;i<values.Length;i++) { values[i]+=1; } if(result.Length!=2 || f(values[0])!=3) throw new Exception(); } }" };
        yield return new object[] { "using System; [Obsolete] class C<T> where T:class,new() { public int this[int index] { get=>index; set { } } public C() {} public event Action E; public T M<U>(ref U x, int y=1) where U:struct => new T(); } class Program { static void Main() {} }" };
        yield return new object[] { "using System; class Program { static int F(object value)=>value switch { int x when x>0=>x, string {Length:>0} s=>s.Length, _=>0 }; static void Main() { var x=F(1); while(x<3) { if(x==2) break; x++; } try { if(x!=2) throw new Exception(); } catch(Exception e) when(e.Message.Length>0) { throw; } finally { x=0; } } }" };
    }

    [Theory]
    [MemberData(nameof(DirectiveBoundaries))]
    public async Task Every_token_boundary_accepts_conditional_trivia_and_opaque_inactive_literals(string source)
    {
        // These fixtures contain no quoted literals. The test inserts conditional
        // trivia around each lexical item, including punctuation and keywords.
        var tokens = Regex.Matches(source, @"[A-Za-z_]\w*|\d+|=>|!=|==|<=|>=|\+=|\+\+|\|\||&&|[^\s]");
        string wrapped = string.Concat(tokens.Select(t => "\n#if true\n" + t.Value + "\n#else\ninvalid \" text\n#endif\n"));
        var parsed = Cs.Parse(wrapped, new() { LanguageVersion = CsLanguageVersion.CSharp14 });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.Equal(wrapped, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        var reparsed = Cs.Parse(formatted, new() { LanguageVersion = CsLanguageVersion.CSharp14 });
        Assert.Empty(reparsed.Diagnostics);
        Assert.Empty(reparsed.Root.Validate());
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
        var detached = (CsDocument)parsed.Root.Detach();
        Assert.Empty(detached.Validate());
        Assert.Equal(formatted, detached.Render());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember or CsExpression { Kind: ExpressionKind.Raw } or CsStatement { Kind: StatementKind.Raw });
        await CompilerFixture.Run(formatted, "14.0");
    }

    [Theory]
    [MemberData(nameof(SyntaxCoverageTests.Corpus), MemberType = typeof(SyntaxCoverageTests))]
    public void Every_exposed_child_in_the_grammar_corpus_can_be_replaced(string id, string mode, int version, string source)
    {
        if (mode != "document") return;
        var document = Cs.Parse(source, new() { LanguageVersion = (CsLanguageVersion)version }).Root;
        foreach (var location in document.Descendants().Where(n => n.Node is not (CsComment or CsDocument)))
        {
            var replacement = location.Node with { Comments = new(location.Node.Comments.Append(new CsComment("/*replacement*/", CommentKind.Block))) };
            var changed = document.Replace(location.Path, replacement);
            Assert.Equal(replacement, changed.At(new NodePath(location.Path.Segments)));
        }
    }

    [Fact]
    public async Task Numeric_and_boolean_operator_combinations_preserve_runtime_results()
    {
        var numeric = new (string Token, Func<int, int, int> Evaluate)[] {
            ("+", (a,b)=>unchecked(a+b)), ("-", (a,b)=>unchecked(a-b)), ("*", (a,b)=>unchecked(a*b)),
            ("/", (a,b)=>a/b), ("%", (a,b)=>a%b), ("<<", (a,b)=>a<<b), (">>", (a,b)=>a>>b),
            (">>>", (a,b)=>a>>>b), ("&", (a,b)=>a&b), ("|", (a,b)=>a|b), ("^", (a,b)=>a^b) };
        var boolean = new (string Token, Func<bool, bool, bool> Evaluate)[] {
            ("&&", (a,b)=>a&&b), ("||", (a,b)=>a||b), ("&", (a,b)=>a&b), ("|", (a,b)=>a|b),
            ("^", (a,b)=>a^b), ("==", (a,b)=>a==b), ("!=", (a,b)=>a!=b) };
        var statements = new StringBuilder("int a=7,b=3,c=2; bool x=true,y=false,z=true;");
        foreach (var outer in numeric) foreach (var inner in numeric)
            {
                int right = inner.Evaluate(3, 2);
                if (outer.Token is not ("/" or "%") || right != 0)
                    statements.Append($"Check(a {outer.Token} (b {inner.Token} c), {outer.Evaluate(7, right)});");
                statements.Append($"Check((a {inner.Token} b) {outer.Token} c, {outer.Evaluate(inner.Evaluate(7, 3), 2)});");
            }
        foreach (var outer in boolean) foreach (var inner in boolean)
            {
                statements.Append($"Test(x {outer.Token} (y {inner.Token} z), {outer.Evaluate(true, inner.Evaluate(false, true)).ToString().ToLowerInvariant()});");
                statements.Append($"Test((x {inner.Token} y) {outer.Token} z, {outer.Evaluate(inner.Evaluate(true, false), true).ToString().ToLowerInvariant()});");
            }
        string source = "class Program { static void Check(int a,int b) { if(a!=b) throw new System.Exception(); } static void Test(bool a,bool b) { if(a!=b) throw new System.Exception(); } static void Main() {" + statements + "} }";
        var parsed = Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.CSharp14 });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        string formatted = parsed.Root.Reformat().Render();
        Assert.Equal(formatted, Cs.Parse(formatted, new() { LanguageVersion = CsLanguageVersion.CSharp14 }).Root.Reformat().Render());
        await CompilerFixture.Run(formatted, "14.0");
    }

    [Fact]
    public async Task Array_variable_initializers_and_nested_queries_compile_and_execute()
    {
        const string source = "using System; using System.Linq; class Program { static int[] first={1,2}, second={3,4}; int[] Value {get;}={5,6}; static void Main() { int[,] matrix={{1,2},{3,4}}; for(int[] a={7,8}, b={9,10}; a[0]<8; a[0]++) { if(b[0]!=9) throw new Exception(); } var result=(from x in first from y in second where x+y>4 select x+y).ToArray(); if(result.Length!=3 || matrix[1,1]!=4 || new Program().Value[0]!=5) throw new Exception(); } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "14.0");
    }
}
