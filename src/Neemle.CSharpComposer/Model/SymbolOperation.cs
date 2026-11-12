namespace Neemle.CSharpComposer.Model;

/// <summary>A semantic operation whose operands are nested symbols, never source tokens.</summary>
public enum SymbolOperation
{
    None, This, Base, As, SizeOf, NameOf, Checked, Unchecked, Ref, NullForgiving,
    PreIncrement, PreDecrement, PostIncrement, PostDecrement, Argument, Tuple,
    Range, Collection, Spread, AnonymousObject, StackAlloc, With, ImplicitIndex,
    Declaration, Query, Lambda, AnonymousMethod, CollectionArguments, TypeReference, Field, MakeRef, RefType, RefValue, StringLiteral, StringText, Interpolation, InterpolatedString, Unsafe, Parameter, QueryClause
}

public enum ArgumentModifier { None, Ref, Out, In }
