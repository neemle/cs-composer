using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsBody? Body(int depth = 0)
    {
        Guard(depth);
        int start = p;
        if (Take(";")) return null;
        if (Take("=>")) { var expression = Expression(); Expect(";"); return Bind(CsBody.FromExpression(expression), start); }
        int open = Expect("{").End;
        var statements = new List<CsStatement>();
        while (Current.Text is not ("}" or ""))
        {
            int before = p;
            try { statements.Add(Statement(depth + 1)); }
            catch (ParseFailure failure)
            {
                p = before;
                var raw = RecoverNode(failure.Message, false);
                statements.Add(new CsStatement(StatementKind.Raw, Text: raw.Text) { Origin = raw.Origin });
            }
            if (p <= before) throw new InvalidOperationException("Statement parser did not advance.");
        }
        int close = Expect("}").Start;
        var body = new CsBody(statements.ToArray());
        body = body with { Comments = ContainerComments(open, close, body.Children) };
        return Bind(body, start, listStart: open, listEnd: close);
    }

    CsStatement Statement(int depth = 0)
    {
        Guard(depth);
        int start = p;
        if (Current.Text == "{") return Bind(new CsStatement(StatementKind.Block) { Body = Body(depth + 1) }, start);
        if (Take(";")) return Bind(Cs.Empty(), start);
        if (Current.Text == "await" && Peek(1) is "foreach" or "using")
        { p++; return Bind(Statement(depth + 1) with { IsAwait = true, Origin = null }, start); }
        if (Take("for")) return ForStatement(start, depth + 1);
        if (Take("foreach")) return ForEachStatement(start, depth + 1);
        if (Take("while")) return WhileStatement(start, depth + 1);
        if (Take("do")) return DoStatement(start, depth + 1);
        if (Take("continue")) { Expect(";"); return Bind(Cs.Continue(), start); }
        if (Take("switch")) return SwitchStatement(start, depth + 1);
        if (Take("break")) { Expect(";"); return Bind(Cs.Break(), start); }
        if (Take("goto"))
        {
            CsStatement jump;
            if (Take("case")) jump = Cs.GotoCase(Expression(0, depth + 1));
            else { Expect("default"); jump = Cs.GotoDefault(); }
            Expect(";"); return Bind(jump, start);
        }
        if (Take("return"))
        { var value = Current.Text == ";" ? null : Expression(); Expect(";"); return Bind(Cs.Return(value), start); }
        if (Take("throw"))
        {
            var value = Current.Text == ";" ? null : Expression(); Expect(";");
            return Bind(value == null ? Cs.Rethrow() : Cs.Throw(value), start);
        }
        if (Take("if"))
        {
            Expect("("); var condition = Expression(); Expect(")");
            var body = Embedded(depth + 1);
            CsStatement? alternative = Take("else") ? Statement(depth + 1) : null;
            if (alternative != null && alternative.Kind is not (StatementKind.Block or StatementKind.If))
                alternative = Cs.Block(alternative);
            return Bind(Cs.If(condition, body, alternative), start);
        }
        if (Take("try")) return TryStatement(start, depth + 1);
        if (Take("using")) return UsingStatement(start, depth + 1);
        int before = p;
        try
        {
            var local = Local();
            Expect(";");
            return Bind(local, start);
        }
        catch (ParseFailure) { p = before; }
        var expression = Expression();
        Expect(";");
        return Bind(Cs.Statement(expression), start);
    }

    CsBody Embedded(int depth) => Current.Text == "{" ? Body(depth) ?? new() : new CsBody(Statement(depth));

    CsStatement Local()
    {
        int start = p;
        bool isVar = Take("var");
        var type = isVar ? null : TypeName();
        var name = Current;
        Identifier();
        var value = Take("=") ? Expression() : null;
        if (Current.Text is not (";" or ")")) throw new ParseFailure("Local declaration expected.");
        if (isVar && value == null) throw new ParseFailure("var requires an initializer.");
        return Bind(new CsStatement(StatementKind.Local, value, name.Text) { IsVar = isVar, Type = type },
            start, "Text", name.Start, name.Text.Length);
    }

    CsStatement UsingStatement(int start, int depth)
    {
        if (Current.Text != "(")
        {
            var declaration = Local(); Expect(";");
            return Bind(new CsStatement(StatementKind.UsingDeclaration) { Resource = declaration }, start);
        }
        Expect("(");
        int before = p;
        CsStatement? local = null;
        CsExpression? expression = null;
        try { local = Local(); }
        catch (ParseFailure) { p = before; expression = Expression(); }
        Expect(")");
        var body = Embedded(depth);
        return Bind(local == null ? Cs.Using(expression!, body) : Cs.Using(local, body), start);
    }

    CsStatement TryStatement(int start, int depth)
    {
        var body = Body(depth) ?? throw new ParseFailure("try requires a block.");
        var catches = new List<CsCatch>();
        while (Take("catch"))
        {
            int catchStart = p - 1;
            CsType? type = null; string? name = null;
            if (Take("(")) { type = TypeName(); if (Current.Text != ")") name = Identifier(); Expect(")"); }
            catches.Add(Bind(new CsCatch(Body(depth) ?? throw new ParseFailure("catch requires a block."), type, name), catchStart));
        }
        var final = Take("finally") ? Body(depth) : null;
        if (catches.Count == 0 && final == null) throw new ParseFailure("try requires catch or finally.");
        return Bind(Cs.Try(body, new(catches), final), start);
    }
}
