using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class InstructionPrimitiveTests
{
    [Theory]
    [InlineData("lock (gate) { Work(); }")]
    [InlineData("unsafe { fixed (int* p = data) { *p = 1; } }")]
    [InlineData("checked { x += 1; } unchecked { x += 1; }")]
    [InlineData("yield return 1; yield break;")]
    [InlineData("goto done; done: return;")]
    [InlineData("static int Add(int x, int y) { return x + y; } return Add(1, 2);")]
    [InlineData("using (var ms = new MemoryStream()) { ms.WriteByte(1); }")]
    [InlineData("try { Run(); } catch (Exception e) when (e.Message != null) { Recover(); } finally { Cleanup(); }")]
    [InlineData("switch (kind) { case 1: Work(); break; default: return; }")]
    public void Statements_parse_into_common_instructions_and_format(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        Assert.All(parsed.Root.Items, item => Assert.IsType<CsInstruction>(item));
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.ParseTree(formatted).Diagnostics);
    }

    [Fact]
    public async Task Instructions_preserve_flow_and_exception_filters()
    {
        const string body = "int total = 0; int Add(int x, int y) => x + y; " +
            "lock (gate) { checked { total = Add(2, 3); } } " +
            "try { throw new Exception(); } catch (Exception e) when (e != null) { total += 2; } finally { total += 3; } " +
            "switch (total) { case 10: goto done; default: return 0; } done: return total;";
        var parsed = Cs.ParseTree(body);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run("using System; class Program { static object gate = new object(); static int Run() { " +
            parsed.Root.Reformat().Render() + " } static void Main() { if(Run()!=10) throw new Exception(); } }", "14.0");
    }

    [Fact]
    public void Instruction_options_are_validated_without_silently_losing_payloads()
    {
        Assert.NotEmpty(Cs.Tree(Cs.Instruction(InstructionKind.Lock)).Validate());
        Assert.NotEmpty(Cs.Tree(Cs.Instruction(InstructionKind.YieldBreak, new() { Value = Cs.Value(1) })).Validate());
        Assert.NotEmpty(Cs.Tree(Cs.Instruction(InstructionKind.Goto, new() { Name = "bad name" })).Validate());
        Assert.NotEmpty(Cs.Tree(Cs.Instruction(InstructionKind.Try, new() { Body = Cs.Tree() })).Validate());
    }
}
