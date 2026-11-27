using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal sealed record LayoutScope(SourceOrigin Origin, LayoutScope? Parent)
{
    // Whether text written in this scope is inside a property's accessor or a query expression, where some
    // contextual keywords read as keywords (Names.Enter).
    internal bool Accessor { get; } = Parent?.Accessor == true || Names.BacksField(Origin.Original);

    internal bool Query { get; } = Parent?.Query == true || Origin.Original is CsExpression { Kind: ExpressionKind.Query };
}
