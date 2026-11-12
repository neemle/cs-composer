using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SymbolCompilerTests
{
    static CsSymbol Ref(string name) => Cs.Symbol(name);
    static CsSymbol Op(CsOperator op, params CsNode[] parts) => Cs.Symbol(new() { Operator = op, Parts = parts });
    static CsSymbol Assign(string name, CsNode value, CsOperator op = CsOperator.None) =>
        Cs.Symbol(name, new() { AsAssignment = true, Operator = op, Value = value });
    static CsSymbol Call(string name, CsNode? target = null, params CsNode[] args) =>
        Cs.Symbol(name, new() { AsCall = true, Target = target, Arguments = Cs.Compound(args) });
    static CsSymbol Var(string name, CsNode value) => Cs.Symbol(name, new() { AsVar = true, Value = value });
    static CsInstruction Return(CsNode value) => Cs.Instruction(InstructionKind.Return, new() { Value = value });

    static CsTree SyncBody() => Cs.Tree(
        Cs.Symbol("resource", new()
        {
            AsVar = true,
            Using = true,
            Value = Cs.Symbol(new()
            {
                AsNew = true,
                Type = Cs.Type("Probe"),
                Arguments = Cs.Compound(Call("Max", Ref("Math"), Cs.Value(2), Cs.Value(3)))
            })
        }),
        Var("sum", Cs.Value(0)),
        Cs.Instruction(InstructionKind.For, new()
        {
            Initializer = Var("i", Cs.Value(0)),
            Condition = Op(CsOperator.LessThan, Ref("i"), Cs.Value(8)),
            Iterators = Cs.Compound(Assign("i", Cs.Value(2), CsOperator.Add)),
            Body = Cs.Tree(Cs.Instruction(InstructionKind.If, new()
            {
                Condition = Op(CsOperator.Equal, Ref("i"), Cs.Value(4)),
                Body = Cs.Tree(Cs.Instruction(InstructionKind.Continue))
            }), Assign("sum", Ref("i"), CsOperator.Add))
        }),
        Return(Op(CsOperator.Add, Ref("sum"), Cs.Symbol("Capacity", new() { Target = Ref("resource") }))));

    static CsTree AsyncBody() => Cs.Tree(
        Cs.Symbol("resource", new()
        {
            AsVar = true,
            Using = true,
            Await = true,
            Value = Cs.Symbol(new()
            {
                AsNew = true,
                Type = Cs.Type("Probe"),
                Arguments = Cs.Compound(Cs.Value(5))
            })
        }),
        Var("sum", Cs.Value(0)),
        Cs.Instruction(InstructionKind.ForEach, new()
        {
            Await = true,
            Symbol = Cs.Symbol("item", new() { AsVar = true }),
            Value = Ref("items"),
            Body = Cs.Tree(Assign("sum", Ref("item"), CsOperator.Add))
        }),
        Return(Ref("sum")));

    [Theory]
    [InlineData(CsLanguageVersion.CSharp8, "8.0")]
    [InlineData(CsLanguageVersion.CSharp14, "14.0")]
    public async Task Symbols_compound_arguments_and_instructions_compile_and_execute_with_correct_lifetimes(CsLanguageVersion version, string language)
    {
        var sync = SyncBody() with { LanguageVersion = version };
        var asyncBody = AsyncBody() with { LanguageVersion = version };
        Assert.Empty(sync.Validate());
        Assert.Empty(asyncBody.Validate());
        string Document(string name, CsTree syncTree, CsTree asyncTree) => Cs.Document(version).Using("System").Class(name).Public().Static()
            .Method<int>("Sync").Public().Static().Body(syncTree).End()
            .Method(Cs.Type("System.Threading.Tasks.Task").Generic(Cs.Int), "Async").Public().Static().Async()
            .Parameter(Cs.Type("System.Collections.Generic.IAsyncEnumerable").Generic(Cs.Int), "items").Body(asyncTree).End()
            .End().Build().Render();
        var parsedSync = Cs.ParseTree(sync.Render(), version);
        var parsedAsync = Cs.ParseTree(asyncBody.Render(), version);
        Assert.Empty(parsedSync.Diagnostics);
        Assert.Empty(parsedAsync.Diagnostics);
        // Keep using directives at compilation-unit start when joining the fixtures.
        string source = Document("Demo", sync, asyncBody) + Document("ParsedDemo", parsedSync.Root.Reformat(), parsedAsync.Root.Reformat()).Replace("using System;", "");
        const string harness = "public sealed class Probe : System.IDisposable, System.IAsyncDisposable { " +
            "public static int Disposed; public static int AsyncDisposed; public int Capacity {get;} " +
            "public Probe(int capacity) { Capacity=capacity; } public void Dispose() { Disposed += Capacity; } " +
            "public System.Threading.Tasks.ValueTask DisposeAsync() { AsyncDisposed += Capacity; return default; } } " +
            "class Program { static async System.Threading.Tasks.Task Main() { " +
            "if(Demo.Sync()!=11 || Probe.Disposed!=3 || ParsedDemo.Sync()!=11 || Probe.Disposed!=6) throw new System.Exception(\"sync symbols\"); " +
            "if(await Demo.Async(Items())!=6 || Probe.AsyncDisposed!=5 || await ParsedDemo.Async(Items())!=6 || Probe.AsyncDisposed!=10) throw new System.Exception(\"async symbols\"); } " +
            "static async System.Collections.Generic.IAsyncEnumerable<int> Items() { yield return 1; await System.Threading.Tasks.Task.Yield(); yield return 2; yield return 3; } }";
        await CompilerFixture.Run(source + harness, language);
    }

    [Fact]
    public async Task The_original_using_memory_stream_example_is_entirely_symbols_and_arguments_are_compound()
    {
        var ms = Cs.Symbol("ms", new() { AsVar = true, Using = true, Value = SymbolModelTests.NewStream() });
        var tree = Cs.Tree(ms, Call("WriteByte", Ref("ms"), Cs.Value(42)),
            Return(Cs.Symbol("Length", new() { Target = Ref("ms") })));
        string generated = Cs.Document(CsLanguageVersion.CSharp8).Class("Demo").Public().Static()
            .Method<long>("Run").Public().Static().Body(tree).End().End().Build().Render();
        await CompilerFixture.Run(generated + "class Program { static void Main() { if(Demo.Run()!=1) throw new System.Exception(); } }", "8.0");
    }
}
