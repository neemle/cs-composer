using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class QueryWriter
{
    internal static string Write(Values<CsQueryClause> clauses, string comments = "") => string.Join(" ", clauses.Select((q, i) => (i == 1 ? comments : "") + Clause(q)));
    static string Inside(CsQueryClause q) => string.Concat(q.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));
    internal static string Clause(CsQueryClause q) => InlineComments.Apply(q with { Comments = new(q.Comments.Where(c => c.Placement != CommentPlacement.Container)) }, q.Kind switch
    {
        QueryClauseKind.From => "from " + Inside(q) + (q.Type == null ? "" : Names.Type(q.Type) + " ") + Names.Id(q.Name!) + " in " + Names.Expression(q.Value!),
        QueryClauseKind.Let => "let " + Inside(q) + Names.Id(q.Name!) + " = " + Names.Expression(q.Value!),
        QueryClauseKind.Where => "where " + Inside(q) + Names.Expression(q.Value!),
        QueryClauseKind.Join => "join " + Inside(q) + (q.Type == null ? "" : Names.Type(q.Type) + " ") + Names.Id(q.Name!) + " in " + Names.Expression(q.Value!) +
            " on " + Names.Expression(q.Key!) + " equals " + Names.Expression(q.EqualsKey!) + (q.Into == null ? "" : " into " + Names.Id(q.Into)),
        QueryClauseKind.OrderBy => "orderby " + Inside(q) + string.Join(", ", q.Clauses.Select(Clause)),
        QueryClauseKind.Order => Names.Expression(q.Value!) + " " + Inside(q) + (q.Descending ? "descending" : "ascending"),
        QueryClauseKind.Select => "select " + Inside(q) + Names.Expression(q.Value!),
        QueryClauseKind.Group => "group " + Inside(q) + Names.Expression(q.Value!) + " by " + Names.Expression(q.Key!),
        QueryClauseKind.Into => "into " + Inside(q) + Names.Id(q.Name!) + " " + Write(q.Clauses),
        _ => throw new ArgumentOutOfRangeException(nameof(q))
    });
}
