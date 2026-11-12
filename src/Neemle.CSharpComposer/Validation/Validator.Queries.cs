using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void Query(Values<CsQueryClause>? clauses, string path, int depth, bool first)
    {
        if (!Depth(depth, path) || !Required(clauses, path)) return;
        if (clauses!.Count == 0 || first && clauses[0]?.Kind != QueryClauseKind.From) Error("GEN092", path, "Query requires an initial from clause and a terminal select/group.");
        bool terminal = false, continuation = false;
        List(clauses, path, (q, child) =>
        {
            if (continuation || terminal && q.Kind != QueryClauseKind.Into || !terminal && q.Kind == QueryClauseKind.Into)
                Error("GEN092", child, "Query clauses are out of order.");
            terminal |= q.Kind is QueryClauseKind.Select or QueryClauseKind.Group;
            continuation |= q.Kind == QueryClauseKind.Into;
            QueryClause(q, child, depth + 1);
        });
        if (!terminal) Error("GEN092", path, "Query requires a terminal select or group clause.");
    }

    void QueryClause(CsQueryClause q, string path, int depth)
    {
        if (!Depth(depth, path)) return;
        if (!Enum.IsDefined(q.Kind)) Error("GEN092", path, "Unknown query clause.");
        bool named = q.Kind is QueryClauseKind.From or QueryClauseKind.Let or QueryClauseKind.Join or QueryClauseKind.Into;
        bool value = q.Kind is not (QueryClauseKind.Into or QueryClauseKind.OrderBy);
        bool key = q.Kind is QueryClauseKind.Join or QueryClauseKind.Group;
        bool typed = q.Kind is QueryClauseKind.From or QueryClauseKind.Join;
        if (named) Identifier(q.Name, path + ".Name");
        if (value) Expression(q.Value, path + ".Value", depth + 1);
        if (key) Expression(q.Key, path + ".Key", depth + 1);
        if (q.Kind == QueryClauseKind.Join) Expression(q.EqualsKey, path + ".EqualsKey", depth + 1);
        if (q.Type != null) Type(q.Type, path + ".Type", depth + 1);
        if (q.Into != null) Identifier(q.Into, path + ".Into");
        if (!named && q.Name != null || !value && q.Value != null || !key && q.Key != null || !typed && q.Type != null ||
            q.Kind != QueryClauseKind.Join && (q.Into != null || q.EqualsKey != null) || q.Descending && q.Kind != QueryClauseKind.Order ||
            q.Clauses?.Count > 0 && q.Kind is not (QueryClauseKind.OrderBy or QueryClauseKind.Into)) Error("GEN065", path, "Payload does not belong to this query clause.");
        if (q.Kind == QueryClauseKind.Into) Query(q.Clauses, path + ".Clauses", depth + 1, first: false);
        else if (q.Kind == QueryClauseKind.OrderBy)
        {
            if (q.Clauses?.Count == 0) Error("GEN092", path, "Orderby requires an ordering.");
            List(q.Clauses, path + ".Clauses", (order, child) => { if (order.Kind != QueryClauseKind.Order) Error("GEN092", child, "Expected an ordering."); QueryClause(order, child, depth + 1); });
        }
        else if (q.Clauses == null) Required(null, path + ".Clauses");
    }
}
