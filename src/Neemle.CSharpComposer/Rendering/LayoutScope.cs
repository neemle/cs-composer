using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal sealed record LayoutScope(SourceOrigin Origin, LayoutScope? Parent)
{
    // Whether text written in this scope is inside a property's accessor, a query expression or a constant pattern,
    // where some contextual keywords read as keywords (Names.Enter).
    internal bool Accessor { get; } = Parent?.Accessor == true || Names.BacksField(Origin.Original);

    internal bool Query { get; } = Parent?.Query == true || IsQuery(Origin.Original);

    internal bool Pattern { get; } = Parent?.Pattern == true || IsConstant(Origin.Original);

    // The context of text written for `node` in `scope`: inside every accessor, query and constant pattern around it,
    // and inside the node itself when it is one.
    internal static Names.Context Enter(LayoutScope? scope, CsNode node) => Names.Enter(
        scope?.Accessor == true || Names.BacksField(node),
        scope?.Query == true || IsQuery(node),
        scope?.Pattern == true || IsConstant(node));

    static bool IsQuery(CsNode node) => node is CsExpression { Kind: ExpressionKind.Query };

    static bool IsConstant(CsNode node) => node is CsPattern { Kind: PatternKind.Constant };
}
