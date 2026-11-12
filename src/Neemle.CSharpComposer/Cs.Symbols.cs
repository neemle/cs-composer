using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer;

public static partial class Cs
{
    public static CsSymbol Symbol(string name, CsSymbolOptions? options = null) => new(name, SymbolOptions(options ?? new()));
    public static CsSymbol Symbol(CsSymbolOptions options) => new(null, SymbolOptions(options));
    static CsSymbolOptions SymbolOptions(CsSymbolOptions options) =>
        (options.AsNew || options.AsCall || options.AsIndex || options.AsArray) && options.Arguments == null ? options with { Arguments = Compound() } : options;

    public static CsSymbol Compound(params CsNode[] parts) => new(null, new() { AsCompound = true, Parts = parts });
    public static CsSymbol Value(object? value) => Symbol(new() { Value = Literal(value) });
    public static CsInstruction Instruction(InstructionKind kind, CsInstructionOptions? options = null) => new(kind, options ?? new());
    public static CsTree Tree(params CsNode[] items) => new(items);
    public static ParseResult<CsTree> ParseTree(string source, ParseOptions options) => SymbolParser.Parse(source, options.LanguageVersion, options.PreprocessorSymbols);
    public static ParseResult<CsTree> ParseTree(string source, CsLanguageVersion version = CsLanguageVersion.CSharp15) => SymbolParser.Parse(source, version);
}
