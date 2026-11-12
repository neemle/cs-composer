using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    bool QueryAhead()
    {
        if (Current.Text != "from") return false;
        int before = p;
        try { p++; QueryVariable(0); Expect("in"); return true; }
        catch (ParseFailure) { return false; }
        finally { p = before; }
    }

    (CsType? Type, string Name) QueryVariable(int depth)
    {
        if (Peek(1) == "in") return (null, Identifier());
        var type = TypeName(depth + 1); return (type, Identifier());
    }

    CsExpression QueryExpression(int depth)
    {
        Guard(depth);
        int start = p;
        var clauses = QueryBody(depth + 1, first: true);
        return Bind(new CsExpression(ExpressionKind.Query, "", Values<CsExpression>.Empty) { QueryClauses = clauses }, start);
    }

    Values<CsQueryClause> QueryBody(int depth, bool first = false)
    {
        Guard(depth);
        var clauses = new List<CsQueryClause>();
        if (first && Current.Text != "from") throw new ParseFailure("Query requires an initial from clause.");
        while (true)
        {
            int start = p;
            CsQueryClause clause;
            if (Take("from"))
            {
                var variable = QueryVariable(depth + 1); Expect("in");
                clause = new(QueryClauseKind.From) { Type = variable.Type, Name = variable.Name, Value = Expression(0, depth + 1) };
            }
            else if (Take("let"))
            { string name = Identifier(); Expect("="); clause = new(QueryClauseKind.Let) { Name = name, Value = Expression(0, depth + 1) }; }
            else if (Take("where")) clause = new(QueryClauseKind.Where) { Value = Expression(0, depth + 1) };
            else if (Take("join"))
            {
                var variable = QueryVariable(depth + 1); Expect("in"); var source = Expression(0, depth + 1);
                Expect("on"); var key = Expression(0, depth + 1); Expect("equals"); var equal = Expression(0, depth + 1);
                clause = new(QueryClauseKind.Join)
                {
                    Type = variable.Type,
                    Name = variable.Name,
                    Value = source,
                    Key = key,
                    EqualsKey = equal,
                    Into = Take("into") ? Identifier() : null
                };
            }
            else if (Take("orderby"))
            {
                var orders = new List<CsQueryClause>();
                do
                {
                    int orderStart = p; var key = Expression(0, depth + 1);
                    bool descending = Take("descending"); if (!descending) Take("ascending");
                    orders.Add(Bind(new CsQueryClause(QueryClauseKind.Order) { Value = key, Descending = descending }, orderStart));
                } while (Take(","));
                clause = new(QueryClauseKind.OrderBy) { Clauses = new(orders) };
            }
            else if (Take("select")) clause = new(QueryClauseKind.Select) { Value = Expression(0, depth + 1) };
            else if (Take("group"))
            { var value = Expression(0, depth + 1); Expect("by"); clause = new(QueryClauseKind.Group) { Value = value, Key = Expression(0, depth + 1) }; }
            else throw new ParseFailure("Query requires select or group.");
            clauses.Add(Bind(clause, start));
            if (clause.Kind is QueryClauseKind.Select or QueryClauseKind.Group)
            {
                if (Take("into"))
                {
                    int intoStart = p - 1; string name = Identifier();
                    clauses.Add(Bind(new CsQueryClause(QueryClauseKind.Into) { Name = name, Clauses = QueryBody(depth + 1) }, intoStart));
                }
                return new(clauses);
            }
        }
    }
}
