using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class CSharp15SyntaxTests
{
    [Theory]
    [InlineData("public union Result(int, string);")]
    [InlineData("public closed record class State; public record Ready : State;")]
    [InlineData("static class E { extension(string text) { public char this[int i] => text[i]; } }")]
    public void New_declarations_parse_and_validate(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        Assert.Empty(Cs.Parse(parsed.Root.Reformat().Render()).Diagnostics);
        Assert.Contains(Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.CSharp14 }).Diagnostics, d => d.Code == "GEN073");
    }

    [Fact]
    public void Collection_arguments_are_typed_and_versioned()
    {
        const string source = "List<int> values = [with(capacity: 8), 1, 2];";
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(Cs.ParseTree(parsed.Root.Reformat().Render()).Diagnostics);
        Assert.Contains(Cs.ParseTree(source, CsLanguageVersion.CSharp14).Diagnostics, d => d.Code == "GEN073");
        Assert.NotEmpty(Cs.ParseTree("List<int> values = [1, with(8)];").Diagnostics);
    }

    [Fact]
    public void Labeled_jump_binding_is_limited_to_enclosing_constructs_and_function_boundaries()
    {
        const string source = "outer: for (int i=0; i<3; i++) { while (true) { if(i==1) { continue outer; } break outer; } }";
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(Cs.ParseTree(parsed.Root.Reformat().Render()).Diagnostics);
        Assert.Contains(Cs.ParseTree(source, CsLanguageVersion.CSharp14).Diagnostics, d => d.Code == "GEN073");
        Assert.NotEmpty(Cs.ParseTree("while(true) { break missing; }").Diagnostics);
        Assert.NotEmpty(Cs.ParseTree("s: switch(x) { default: continue s; }").Diagnostics);
        Assert.NotEmpty(Cs.ParseTree("outer: while(true) { Action a = () => { break outer; }; }").Diagnostics);
    }

    [Fact]
    public async Task CSharp15_union_collection_arguments_and_labeled_jumps_compile_and_execute()
    {
        const string source = "using System; using System.Collections.Generic; public union Result(int, string); " +
            "class Program { static int Run() { List<int> values = [with(capacity: 8), 1, 2]; int sum=0; " +
            "outer: for(int i=0; i<3; i++) { for(int j=0; j<3; j++) { if(j==1) { continue outer; } sum++; } } " +
            "Result result = sum; return (result switch { int n => n, string s => s.Length }) + values.Capacity; } " +
            "static void Main() { if(Run()!=11) throw new Exception(Run().ToString()); } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "15.0");
    }
}
