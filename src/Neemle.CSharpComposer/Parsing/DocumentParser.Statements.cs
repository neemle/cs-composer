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
        if (Current.IsDisabled) { string text = tokens[p++].Text; return Bind(new CsStatement(StatementKind.DisabledText) { Disabled = new(text) }, start); }
        if (Current.Text.StartsWith('#')) return Bind(new CsStatement(StatementKind.Directive) { Directive = Directive() with { Origin = null } }, start);
        if (Current.Text == "{") return Bind(new CsStatement(StatementKind.Block) { Body = Body(depth + 1) }, start);
        if (Take(";")) return Bind(Cs.Empty(), start);
        if (Current.Text == "await" && Peek(1) is "foreach" or "using")
        { p++; return Bind(Statement(depth + 1) with { IsAwait = true, Origin = null }, start); }
        if (Current.Text is "lock" or "fixed" || Current.Text is "checked" or "unchecked" or "unsafe" && Peek(1) == "{")
            return ScopedStatement(start, depth + 1);
        if (Take("yield"))
        {
            bool returns = Take("return"); if (!returns) Expect("break");
            var value = returns ? Expression(0, depth + 1) : null; Expect(";");
            return Bind(new CsStatement(returns ? StatementKind.YieldReturn : StatementKind.YieldBreak, value), start);
        }
        if (Peek(1) == ":" && Rendering.Names.IsIdentifier(Current.Text))
        {
            string label = Identifier(); Expect(":");
            return Bind(new CsStatement(StatementKind.Label, Text: label) { Body = new CsBody(Statement(depth + 1)) }, start);
        }
        if (Take("for")) return ForStatement(start, depth + 1);
        if (Take("foreach")) return ForEachStatement(start, depth + 1);
        if (Take("while")) return WhileStatement(start, depth + 1);
        if (Take("do")) return DoStatement(start, depth + 1);
        if (Take("continue")) { string name = Current.Text == ";" ? "" : Identifier(); Expect(";"); return Bind(Cs.Continue() with { Text = name }, start); }
        if (Take("switch")) return SwitchStatement(start, depth + 1);
        if (Take("break")) { string name = Current.Text == ";" ? "" : Identifier(); Expect(";"); return Bind(Cs.Break() with { Text = name }, start); }
        if (Take("goto"))
        {
            CsStatement jump;
            if (Take("case")) jump = Cs.GotoCase(Expression(0, depth + 1));
            else if (Take("default")) jump = Cs.GotoDefault();
            else jump = new CsStatement(StatementKind.Goto, Text: Identifier());
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
        if (Current.Text is not ("new" or "await" or "this" or "base"))
        {
            try
            {
                var candidate = Member("", TypeKind.Class, depth + 1);
                if (candidate is not CsMethod function || function.Body == null && !function.Modifiers.Contains("extern")) throw new ParseFailure("Local function expected.");
                return Bind(new CsStatement(StatementKind.LocalFunction) { Function = function }, start);
            }
            catch (ParseFailure) { p = before; }
        }
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

    CsStatement ScopedStatement(int start, int depth)
    {
        string keyword = tokens[p++].Text;
        CsExpression? value = null; CsStatement? resource = null;
        if (keyword == "lock") { Expect("("); value = Expression(0, depth + 1); Expect(")"); }
        if (keyword == "fixed") { Expect("("); resource = Local(); Expect(")"); }
        var kind = keyword switch
        {
            "lock" => StatementKind.Lock,
            "fixed" => StatementKind.Fixed,
            "unsafe" => StatementKind.Unsafe,
            "checked" => StatementKind.Checked,
            _ => StatementKind.Unchecked
        };
        return Bind(new CsStatement(kind, value) { Resource = resource, Body = Embedded(depth + 1) }, start);
    }

    CsBody Embedded(int depth) => Current.Text == "{" ? Body(depth) ?? new() : new CsBody(Statement(depth));

    CsStatement Local()
    {
        int start = p;
        var modifiers = VariableModifiers();
        bool isVar = Take("var"); var type = isVar ? null : TypeName();
        var variables = new List<CsVariableDeclarator>(); var name = Current;
        do
        {
            int variableStart = p; var token = Current; Identifier();
            var value = Take("=") ? Expression() : null;
            variables.Add(Bind(Cs.Variable(token.Text, value), variableStart, "Name", token.Start, token.Text.Length));
        } while (Take(","));
        if (Current.Text is not (";" or ")")) throw new ParseFailure("Local declaration expected.");
        // A non-inferred declaration can name an actual type called var. Binding
        // that name is a compiler responsibility; its syntax must remain typed.
        if (isVar && (variables.Count != 1 || variables[0].Initializer == null)) { isVar = false; type = new CsType("var"); }
        if (variables.Count > 1)
            return Bind(new CsStatement(StatementKind.LocalGroup) { ForInitializer = new() { Type = type, IsVar = isVar, VariableModifiers = modifiers, Variables = new(variables) } }, start);
        return Bind(new CsStatement(StatementKind.Local, variables[0].Initializer, name.Text) { IsVar = isVar, Type = type, VariableModifiers = modifiers },
            start, "Text", name.Start, name.Text.Length);
    }

    CsVariableModifiers VariableModifiers()
    {
        var result = CsVariableModifiers.None;
        while (Current.Text is "const" or "ref" or "readonly" or "scoped")
        {
            result |= Current.Text switch { "const" => CsVariableModifiers.Const, "ref" => CsVariableModifiers.Ref, "readonly" => CsVariableModifiers.ReadOnly, _ => CsVariableModifiers.Scoped }; p++;
        }
        return result;
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
            CsExpression? filter = null;
            if (Take("when")) { Expect("("); filter = Expression(0, depth + 1); Expect(")"); }
            catches.Add(Bind(new CsCatch(Body(depth) ?? throw new ParseFailure("catch requires a block."), type, name) { Filter = filter }, catchStart));
        }
        var final = Take("finally") ? Body(depth) : null;
        if (catches.Count == 0 && final == null) throw new ParseFailure("try requires catch or finally.");
        return Bind(Cs.Try(body, new(catches), final), start);
    }
}
