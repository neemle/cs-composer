using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsBody? Body(int depth)
    {
        Guard(depth);
        int start = p;
        if (Take(";")) return null;
        if (Take("=>")) { var expression = Expression(0, depth + 1); Expect(";"); return Bind(CsBody.FromExpression(expression), start); }
        int open = Expect("{").End;
        var statements = new List<CsStatement>();
        while (Current.Text is not ("}" or ""))
        {
            cancellation.ThrowIfCancellationRequested();
            int before = p, deepTypes = tooDeepTypes;
            try { Site(); statements.Add(Fit(Statement(depth + 1), depth + 1)); Parsed(before, deepTypes); }
            catch (ParseFailure failure) { p = before; statements.Add(RawStatement(FailureMessage(failure, deepTypes))); }
            catch (NestingFailure) when (depth + 1 < Limits.MaxNestingDepth) { p = before; statements.Add(RawStatement(NestingMessage)); }
            Advanced(p, before, "Statement parser did not advance.");
        }
        int close = Expect("}").Start;
        var body = new CsBody(statements.ToArray());
        body = body with { Comments = ContainerComments(open, close, body.Children) };
        return Bind(body, start, listStart: open, listEnd: close);
    }

    CsStatement RawStatement(string message)
    {
        var raw = RecoverStatement(message);
        return new CsStatement(StatementKind.Raw, Text: raw.Text) { Origin = raw.Origin };
    }

    CsStatement Statement(int depth)
    {
        Guard(depth);
        int start = p;
        if (Current.Text.StartsWith('#')) return Bind(new CsStatement(StatementKind.Directive) { Directive = Directive() with { Origin = null } }, start);
        if (Current.Text == "{") return Bind(new CsStatement(StatementKind.Block) { Body = Body(depth + 1) }, start);
        if (Take(";")) return Bind(Cs.Empty(), start);
        if (Current.Text == "await" && Peek(1) is "foreach" or "using")
        { p++; return Bind(Statement(depth) with { IsAwait = true, Origin = null }, start); }
        if (Current.Text is "lock" or "fixed" || Current.Text is "checked" or "unchecked" or "unsafe" && Peek(1) == "{")
            return ScopedStatement(start, depth);
        if (Take("yield"))
        {
            bool returns = Take("return"); if (!returns) Expect("break");
            var value = returns ? Expression(0, depth + 1) : null; Expect(";");
            return Bind(new CsStatement(returns ? StatementKind.YieldReturn : StatementKind.YieldBreak, value), start);
        }
        if (Peek(1) == ":" && Rendering.Names.IsIdentifier(Current.Text))
        {
            string label = Identifier(); Expect(":");
            return Bind(new CsStatement(StatementKind.Label, Text: label) { Body = new CsBody(Statement(depth + 2)) }, start);
        }
        if (Take("for")) return ForStatement(start, depth);
        if (Take("foreach")) return ForEachStatement(start, depth);
        if (Take("while")) return WhileStatement(start, depth);
        if (Take("do")) return DoStatement(start, depth);
        if (Take("continue")) { string name = Current.Text == ";" ? "" : Identifier(); Expect(";"); return Bind(Cs.Continue() with { Text = name }, start); }
        if (Take("switch")) return SwitchStatement(start, depth);
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
        { var value = Current.Text == ";" ? null : Expression(0, depth + 1); Expect(";"); return Bind(Cs.Return(value), start); }
        if (Take("throw"))
        {
            var value = Current.Text == ";" ? null : Expression(0, depth + 1); Expect(";");
            return Bind(value == null ? Cs.Rethrow() : Cs.Throw(value), start);
        }
        if (Take("if"))
        {
            Expect("("); var condition = Expression(0, depth + 1); Expect(")");
            var body = Embedded(depth + 1);
            int thenEnd = body.Origin!.End, elseEnd = Current.End;
            if (!Take("else")) return Bind(Cs.If(condition, body), start);
            var alternative = Embeddable(Statement(depth + 1));
            if (alternative.Kind is not (StatementKind.Block or StatementKind.If))
                alternative = Wrapped(new CsStatement(StatementKind.Block) { Body = Wrapped(new CsBody(alternative)) });
            // GENE-27 L3: the gap from the then-branch through `else`, so the writer keeps the if's comments in it there.
            var bound = Bind(Cs.If(condition, body, alternative), start);
            return bound with { Origin = bound.Origin! with { Bindings = new(bound.Origin.Bindings.Append(new TokenBinding("Else", thenEnd, elseEnd, source.Text[thenEnd..elseEnd]))) } };
        }
        if (Take("try")) return TryStatement(start, depth);
        if (Take("using")) return UsingStatement(start, depth);
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
        // In an async context `await` is an operator, never a type name, so `await x;` is an await expression
        // rather than a declaration of `x` (GENE-27 D8). Elsewhere it stays a valid declaration of type `await`.
        if (!(inAsync && Current.Text == "await"))
        {
            try
            {
                var local = Local(depth);
                Expect(";");
                return Bind(local, start);
            }
            catch (ParseFailure) { p = before; }
        }
        var expression = Expression(0, depth + 1);
        Expect(";");
        return Bind(Cs.Statement(expression), start);
    }

    CsStatement ScopedStatement(int start, int depth)
    {
        string keyword = tokens[p++].Text;
        CsExpression? value = null; CsStatement? resource = null;
        if (keyword == "lock") { Expect("("); value = Expression(0, depth + 1); Expect(")"); }
        if (keyword == "fixed") { Expect("("); resource = Local(depth + 1); Expect(")"); }
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

    CsBody Embedded(int depth) => Current.Text == "{" ? Body(depth)! : Wrapped(new CsBody(Embeddable(Statement(depth + 1))));

    // A brace-less embedded statement (a loop's, an if's or an else's) can be neither a declaration nor a labeled
    // statement (CS1023). Such source is not valid C#, so it is not parsed typed: the statement holding it is kept as
    // raw source (GENE-27 L1). Braced, the same statement is valid, and that is how a typed model writes it.
    static CsStatement Embeddable(CsStatement statement) => Array.IndexOf(NotEmbeddable, statement.Kind) < 0 ? statement :
        throw new ParseFailure("An embedded statement cannot be a declaration or a labeled statement.");

    static readonly StatementKind[] NotEmbeddable = [StatementKind.Local, StatementKind.LocalGroup, StatementKind.LocalFunction,
        StatementKind.UsingDeclaration, StatementKind.Label];

    // A node that only wraps another one gets an embedded origin over the inner node's source (GENE-27 phase 3).
    static T Wrapped<T>(T wrapper) where T : CsNode => (T)(wrapper with { Origin = SourceOrigin.Wrap(wrapper, SourceOrigin.Inner(wrapper).Origin!) });

    CsStatement Local(int depth)
    {
        int start = p;
        var modifiers = VariableModifiers();
        bool isVar = Take("var"); var type = isVar ? null : TypeName(depth + 1);
        var variables = new List<CsVariableDeclarator>(); var name = Current;
        do
        {
            int variableStart = p; var token = Current; Identifier();
            var value = Take("=") ? Expression(0, depth + 1) : null;
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
            var declaration = Local(depth + 1); Expect(";");
            return Bind(new CsStatement(StatementKind.UsingDeclaration) { Resource = declaration }, start);
        }
        Expect("(");
        int before = p;
        CsStatement? local = null;
        CsExpression? expression = null;
        try { local = Local(depth + 1); }
        catch (ParseFailure) { p = before; }
        if (local == null) expression = Expression(0, depth + 1);
        Expect(")");
        var body = Embedded(depth + 1);
        return Bind(local == null ? Cs.Using(expression!, body) : Cs.Using(local, body), start);
    }

    CsStatement TryStatement(int start, int depth)
    {
        var body = Body(depth + 1) ?? throw new ParseFailure("try requires a block.");
        var catches = new List<CsCatch>();
        while (Take("catch"))
        {
            int catchStart = p - 1;
            CsType? type = null; string? name = null;
            if (Take("(")) { type = TypeName(depth + 2); if (Current.Text != ")") name = Identifier(); Expect(")"); }
            CsExpression? filter = null;
            if (Take("when")) { Expect("("); filter = Expression(0, depth + 2); Expect(")"); }
            catches.Add(Bind(new CsCatch(Body(depth + 2) ?? throw new ParseFailure("catch requires a block."), type, name) { Filter = filter }, catchStart));
        }
        var final = Take("finally") ? Body(depth + 1) : null;
        if (catches.Count == 0 && final == null) throw new ParseFailure("try requires catch or finally.");
        return Bind(Cs.Try(body, new(catches), final), start);
    }
}
