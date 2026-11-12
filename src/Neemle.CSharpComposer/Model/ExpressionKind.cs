using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public enum ExpressionKind
{
    Identifier,
    Literal,
    Member,
    Invocation,
    Await,
    Assignment,
    Raw,
    Binary,
    Unary,
    Switch,
    Throw,
    PreIncrement,
    PostIncrement,
    PreDecrement,
    PostDecrement,
    Lambda,
    New,
    Index,
    Cast,
    TypeOf,
    Default,
    Is,
    Conditional,
    Array,
    Initializer,
    Parenthesized,
    This, Base, As, SizeOf, NameOf, Checked, Unchecked, Ref, NullForgiving, Argument, Tuple,
    Range, Collection, Spread, AnonymousObject, StackAlloc, With, ImplicitIndex, Declaration, Omitted, Query, AnonymousMethod, CollectionArguments, TypeReference, Field, MakeRef, RefType, RefValue, StringLiteral, StringText, Interpolation, InterpolatedString, Unsafe
}
