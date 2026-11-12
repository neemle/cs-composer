namespace Neemle.CSharpComposer.Model;

internal static class SymbolModes
{
    internal static bool TypeExpression(CsSymbolOptions o) => o.Operation is SymbolOperation.As or SymbolOperation.SizeOf or SymbolOperation.StackAlloc or SymbolOperation.Declaration or SymbolOperation.TypeReference or SymbolOperation.RefValue || o.AsNew || o.AsCast || o.AsTypeOf || o.AsDefault || o.AsIs || o.AsArray;

    internal static int Count(CsSymbolOptions o) => (o.Operation == SymbolOperation.None ? 0 : 1) + new[] { o.AsNew, o.AsCall, o.AsCompound, o.AsAssignment,
        o.AsIndex, o.AsCast, o.AsTypeOf, o.AsDefault, o.AsIs, o.AsConditional, o.AsArray, o.AsInitializer, o.AsParenthesized }.Count(x => x);
}
