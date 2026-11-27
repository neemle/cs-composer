using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-27: regressions for the README "Known limitations". Phase 1 (D3, D5, D7, D8, D9, D10, D13) rows reproduced their
// defect on 44606b7, phase 2 (D1, D2, D4, D6) rows on d605cb6, phase 3 rows on 194c591 and phase 4 (gene27.p4.*) rows
// on af570e4; the expected outcome is the fixed behaviour. Guard and coverage rows are green on their base too.
public static class KnownLimitationScenarios
{
    static readonly RenderOptions NoCheck = new() { Validate = false };

    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    static CsDocument Edit(string source, Func<CsNode, bool> match, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = Find(document, match);
        return document.Replace(path, change(document.At(path)));
    }

    // The kinds of the parsed statements, in document order, then the reformatted document.
    static string Statements(string source)
    {
        var document = Src(source);
        return string.Join(",", document.Descendants().Select(n => n.Node).OfType<CsStatement>().Select(s => s.Kind)) +
            "\n" + document.Reformat().Render();
    }

    static CsComment Trailing(string text) => new(text, text.StartsWith("//", StringComparison.Ordinal) ? CommentKind.Line : CommentKind.Block, CommentPlacement.Trailing);

    // The `nth` node (in document order) that matches, replaced.
    static CsDocument EditAt(string source, Func<CsNode, bool> match, int nth, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = document.Descendants().Where(n => match(n.Node)).ElementAt(nth).Path;
        return document.Replace(path, change(document.At(path)));
    }

    // The innermost container with more than one item, its list replaced by the items `order` picks from it (a
    // negative index inserts `int Z;`).
    static CsDocument Members(string source, params int[] order)
    {
        var document = Src(source);
        var path = document.Descendants().Last(n => n.Node is CsTypeDeclaration { Members.Count: > 1 } or CsNamespace { Items.Count: > 1 } or CsBody { Statements.Count: > 1 }).Path;
        CsNode Pick(IReadOnlyList<CsNode> items, int i) => i < 0 ? new CsField(Cs.Int, "Z") : items[i];
        return document.Replace(path, document.At(path) switch
        {
            CsTypeDeclaration t => t with { Members = new(order.Select(i => (CsMember)Pick(t.Members, i))) },
            CsNamespace n => n with { Items = new(order.Select(i => Pick(n.Items, i))) },
            var b => ((CsBody)b) with { Statements = new(order.Select(i => (CsStatement)Pick(((CsBody)b).Statements, i))) }
        });
    }

    // Phase 2 sources: a directive owned by a statement, a directive owned by a binary operand pair, multi-line
    // member-access chains (plain, and with comments between the links) and containers with comments of their own.
    internal const string StatementDirective = "class C\n{\n    void M()\n    {\n        x = a\n#if X\n            + b\n#endif\n            ;\n    }\n}\n";
    internal const string OperandDirective = "class C\n{\n    int F = a\n#if X\n#endif\n        + b;\n}\n";
    internal const string ChainSource = "class C\n{\n    void M()\n    {\n        var r = source\n            .Where(x => x)\n            .Select(x => x)\n            .ToList();\n    }\n}\n";
    internal const string CommentedChain = "class C\n{\n    void M()\n    {\n        var r = source /*c*/\n            .Where(x) // step\n            .Select(x) // step\n            .ToList();\n    }\n}\n";
    internal const string TypeComments = "class C\n{\n    int A;\n\n    // about B\n\n    int B;\n\n    // container tail\n\n    int D;\n}\n";
    internal const string BlockComments = "class C\n{\n    void M()\n    {\n        a();\n\n        // middle\n\n        b();\n        // tail\n    }\n}\n";
    internal const string NamespaceComments = "namespace N\n{\n    class A { }\n\n    // between\n\n    class B { }\n}\n";

    internal const string ElseStatement = "class C\n{\n    void M()\n    {\n        if (c)\n            a();\n        else if (d)\n            b();\n    }\n}\n";
    internal const string OwnLineArguments = "class C\n{\n    int F = G(\n        a,\n        b);\n}\n";
    internal const string PatternDirective = "class C\n{\n    int M(object o) => o switch\n    {\n        (1,\n#if X\n#endif\n            2) => 0,\n        _ => 1\n    };\n}\n";
    internal const string BinaryChain = "class C\n{\n    int F = a + b\n        + c;\n}\n";
    internal const string DirectiveChain = "class C\n{\n    void M()\n    {\n        a.B(1,\n#if X\n#endif\n            2)\n            .C();\n    }\n}\n";

    internal const string DirectiveStatements = "class C\n{\n    void M()\n    {\n#if DEBUG\n        a++;\n#endif\n        b++;\n    }\n}\n";

    internal const string TwoTypes = "class A\n{\n    int X;\n\n    // moved\n\n    int Y;\n}\nclass B\n{\n    int Z;\n}\n";

    static CsDocument Operand(string source, string text, CsExpression replacement, int nth = 0) =>
        EditAt(source, n => n is CsExpression { Kind: ExpressionKind.Identifier } e && e.Text == text, nth, _ => replacement);

    static CsDocument Constraint(string source, string kind, CsConstraint replacement) =>
        Edit(source, n => n is CsConstraint c && c.Kind == kind, _ => replacement);

    // Phase 3 sources: brace-less embedded statements, a chain link with a trailing block comment, an indented region
    // directive inside an operand pair, a lambda with its body on its own line, interpolated strings and an argument
    // whose inactive branch closes the call.
    internal const string Embedded = "class C\n{\n    void M()\n    {\n        if (c) x = 1;\n        if (d)\n            a();\n        else\n            b();\n        for (;;) e();\n        foreach (var v in w) f();\n        while (g) h();\n        do i(); while (j);\n        using (k) l();\n        lock (m) n();\n    }\n}\n";
    internal const string CommentedLink = "class C\n{\n    void M()\n    {\n        var r = source\n            .Select(x => x * 2) /* s */\n            .ToList();\n    }\n}\n";
    internal const string IndentedRegion = "class C\n{\n    int G = b\n        #region Q\n        + c;\n        #endregion\n}\n";
    internal const string LambdaBody = "class C\n{\n    Func<int, int> F = x =>\n        x + 1;\n}\n";
    internal const string Interpolated = "class C\n{\n    string S = $\"a{y,5}b{ w :x2}\";\n    string R = $\"\"\"\n        a {y}\n          b {w}\n        \"\"\";\n}\n";
    internal const string InactiveArgument = "class C\n{\n    void M()\n    {\n        h(a,\n#if X\n          b);\n#else\n          c);\n#endif\n    }\n}\n";
    internal const string Headers = "namespace N\n{\n    // about C\n    [A]\n    class C<T>  where T : class\n    {\n        int  A;\n\n        int  B;\n    }\n}\n";

    static CsExpression OrElse(string left, string right) => Cs.Binary(CsOperator.OrElse, Cs.Id(left), Cs.Id(right));

    // The type declaration of `source`, changed.
    static CsDocument Type(string source, Func<CsTypeDeclaration, CsTypeDeclaration> change) =>
        Edit(source, n => n is CsTypeDeclaration, n => change((CsTypeDeclaration)n));

    // A document parsed back from `rendered`, as the kinds of the container comments each type or body owns.
    static string Owners(string rendered) => rendered + "owners: " + string.Join(" ", Src(rendered).Descendants().Select(n => n.Node)
        .Where(n => n is CsTypeDeclaration or CsBody).Select(n => n.GetType().Name + "[" + string.Join(",", n.Comments.Select(c => c.Placement)) + "]"));

    // QA F1 sources: a brace-less then-branch followed by an `else`, in the shapes a dangling `else` can arise in.
    static string InM(string statements) => "class C\n{\n    void M()\n    {\n" + statements + "    }\n}\n";
    static readonly string ThenElse = InM("        if (a) x(); else y();\n");
    static readonly string ThenElseLines = InM("        if (a)\n            x();\n        else\n            y();\n");
    static CsStatement ElselessIf() => Cs.If(Cs.Id("b"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("z")))));
    static CsDocument Statement(string source, int nth, CsStatement replacement) =>
        EditAt(source, n => n is CsStatement { Kind: StatementKind.Expression }, nth, _ => replacement);

    // Phase 4 sources and helpers (GENE-27 L1-L3, interpolation holes, header comments, top-level statements).
    static readonly string CommentBeforeElse = InM("        if (a) x(); // c\n        // lead\n        else y();\n");
    internal const string Holes = "class C\n{\n    string S = $\"a{$\"b{x,2}\"}c\";\n    string R = $\"\"\"\n        {x +\n            y}\n        \"\"\";\n    string T = $\"a{f(w)}b{y,-w}c{ v :x2}\";\n}\n";
    internal const string RawHoles = "class C\n{\n    string R = $\"\"\"a{y:x2}b\"\"\";\n    string M = $\"\"\"\n        a {y}\n        b\n        \"\"\";\n}\n";

    // A parse as its coverage, its diagnostics and the raw statements it kept.
    static string Parsed(string source)
    {
        var parsed = Cs.Parse(source);
        return parsed.Coverage + ": " + string.Join(" | ", parsed.Diagnostics.Select(d => d.Code + " " + d.Message)) + "\n" +
            string.Join("\n", parsed.Root.Descendants().Select(n => n.Node).OfType<CsStatement>().Where(s => s.Kind == StatementKind.Raw).Select(s => s.Text));
    }

    static CsNode Detached(string statement) => Cs.ParseTree(statement).Root.Items[0].Detach();

    static CsStatement Labeled(params CsStatement[] statements) => new(StatementKind.Label, Text: "L") { Body = Cs.Body(statements) };

    // The `nth` call in a document of top-level statements, replaced.
    static CsDocument Call(string source, int nth, CsNode replacement) =>
        EditAt(source, n => n is CsSymbol { Options.AsCall: true }, nth, _ => replacement);

    // The interpolation part `nth` (in document order) of the parts that match, changed.
    static CsDocument Part(string source, Func<CsExpression, bool> match, int nth, Func<CsExpression, CsExpression> change) =>
        EditAt(source, n => n is CsExpression e && match(e), nth, n => change((CsExpression)n));

    // The literals `1` of a document, the first `count` of them changed to `7`.
    static CsDocument Literal(string source, int count = 1)
    {
        var document = Src(source);
        for (int i = 0; i < count; i++)
        {
            var path = document.Descendants().First(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "1" }).Path;
            document = document.Replace(path, ((CsExpression)document.At(path)) with { Text = "7" });
        }
        return document;
    }

    static CsExpression Text(string text) => new(ExpressionKind.StringText, text, Values<CsExpression>.Empty);

    static CsComment Foreign() => Src("// far\nclass D\n{\n}\n").Descendants().Select(n => n.Node).OfType<CsComment>().First() with { Placement = CommentPlacement.Container };

    public static IEnumerable<Scenario> All()
    {
        // D9: the internal InvalidModel exception never escapes; an invalid indent is rejected like a document's.
        yield return new("gene27.d9.typed-tree-indent", () => Cs.Tree(Cs.Symbol("Run", new() { AsCall = true, Arguments = Cs.Compound() })).Render(new RenderOptions { Indent = "x" }), "throws ArgumentException: Indent must contain only spaces or tabs. (Parameter 'options')");
        yield return new("gene27.d9.unchecked-document", () => new CsDocument { Items = new CsNode[] { new CsGlobalStatement(Cs.Instruction((InstructionKind)99)) } }.Render(NoCheck), "throws GenValidationException: SYM001 Tree.Children[0]: Unknown instruction kind.");

        // D10: an unchecked render of a designation whose operation misses its operand raises the public exception.
        yield return new("gene27.d10.designation-without-operand", () => Value(new CsExpression(ExpressionKind.Is, "", new[] { Cs.Id("o") }) { Pattern = new CsPattern(PatternKind.Var) { Designation = Cs.Symbol(new() { Operation = SymbolOperation.NullForgiving }) } }).Render(NoCheck), "throws GenValidationException: SYM001 Designation.Value: Operation requires a value operand.");
        yield return new("gene27.d10.designation-renders", () => Value(new CsExpression(ExpressionKind.Is, "", new[] { Cs.Id("o") }) { Pattern = new CsPattern(PatternKind.Var) { Designation = Cs.Symbol("y") } }).Render(NoCheck), "class C\n{\n    void M()\n    {\n        object x = o is var y;\n    }\n}\n");

        // D13: the GEN034 message names what is wrong for a member that is not a type.
        yield return new("gene27.d13.nested-file-field", () => InClass(new CsField(Cs.Int, "F") { Modifiers = new[] { "file" } }), "invalid: GEN034 Only a top-level type can be file-local. | GEN031 Modifier 'file' is not valid for this construct.");
        yield return new("gene27.d13.nested-file-delegate", () => InClass(new CsDelegate(Cs.Void, "D") { Modifiers = new[] { "file" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers.");
        yield return new("gene27.d13.parsed-file-field", () => Cs.Parse("class C { file int F; }").Diagnostics.Single().Message, "Only a top-level type can be file-local.");

        // D8: `await x;` is an await expression statement in every async context, and a declaration elsewhere.
        yield return new("gene27.d8.async-method", () => Statements("class C\n{\n    async Task M(Task x)\n    {\n        await x;\n    }\n}\n"), "Expression\nclass C\n{\n    async Task M(Task x)\n    {\n        await x;\n    }\n}\n");
        yield return new("gene27.d8.async-lambda", () => Statements("class C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async () => { await x; };\n    }\n}\n"), "Local,Expression\nclass C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async () =>\n        {\n            await x;\n        };\n    }\n}\n");
        yield return new("gene27.d8.async-anonymous-method", () => Statements("class C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async delegate { await x; };\n    }\n}\n"), "Local,Expression\nclass C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async delegate\n        {\n            await x;\n        };\n    }\n}\n");
        yield return new("gene27.d8.async-local-function", () => Statements("class C\n{\n    void M(Task x)\n    {\n        async Task L() { await x; }\n    }\n}\n"), "LocalFunction,Expression\nclass C\n{\n    void M(Task x)\n    {\n        async Task L()\n        {\n            await x;\n        }\n    }\n}\n");
        yield return new("gene27.d8.top-level", () => Cs.ParseTree("await x;").Root.Reformat().Render(), "await x;\n");
        yield return new("gene27.d8.not-async", () => Statements("class C\n{\n    void M()\n    {\n        await x;\n    }\n}\n"), "Local\nclass C\n{\n    void M()\n    {\n        @await x;\n    }\n}\n");
        yield return new("gene27.d8.untouched", () => Src("class C\n{\n    async Task M(Task x)\n    {\n        await  x ;\n    }\n}\n").Render(), "class C\n{\n    async Task M(Task x)\n    {\n        await x;\n    }\n}\n");

        // D3: a fresh region that ends in a directive keeps the directive's line end, so `;` never joins `#endif` (since
        // phase 2 the blanked operand is replaced in place, so `;` keeps that operand's own line indentation).
        yield return new("gene27.d3.last-operand-blanked", () => Edit("class C\n{\n    int F = a +\n#if X\n#endif\n        b;\n}\n", n => n is CsExpression { Text: "b" }, _ => Cs.RawExpression("")).Render(NoCheck), "class C\n{\n    int F = a +\n#if X\n#endif\n        ;\n}\n");
        yield return new("gene27.d3.owner-rendered-again", () => EditAt("class C\n{\n    int F = a +\n#if X\n#endif\n        b;\n}\n", n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0], Cs.RawExpression("") }, Comments = new[] { new CsComment("/* c */", CommentKind.Block) } }).Render(NoCheck), "class C\n{\n    int F = /* c */ a +\n#if X\n#endif\n        ;\n}\n");
        yield return new("gene27.d3.hash-in-literal", () => Edit("class C\n{\n    string F = \"#\" + b;\n}\n", n => n is CsExpression { Text: "b" }, _ => Cs.Id("c")).Render(NoCheck), "class C\n{\n    string F = \"#\" + c;\n}\n");

        // D5: trailing comments of a constructed document are rendered, and a trailing line comment ends its line.
        yield return new("gene27.d5.types-document", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// tail") } }, "class C\n{\n} // tail\n");
        yield return new("gene27.d5.builder-document", () => Cs.Document().Class("C").End().Build() with { Comments = new[] { Trailing("/* tail */") } }, "class C\n{\n} /* tail */\n");
        yield return new("gene27.d5.items-document", () => new CsDocument { Items = new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// a"), Trailing("// b") } }, "invalid: EMIT001 Rendered output does not read back as the model: comment // b reads back on another owner.");
        yield return new("gene27.d5.directive-first", () => new CsDocument { Items = new CsNode[] { new CsDirective(DirectiveKind.Region), new CsDirective(DirectiveKind.EndRegion), new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// tail") } }, "#region\n#endregion\n\nclass C\n{\n} // tail\n");
        yield return new("gene27.d5.member-line-then-block", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { Trailing("// a"), Trailing("/* b */") } }), "invalid: EMIT001 Rendered output does not read back as the model: comment /* b */ reads back on another owner.");
        yield return new("gene27.d5.type-line-with-newline", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Comments = new[] { Trailing("// a\n"), Trailing("/* b */"), Trailing("/* c */") } } } }, "invalid: EMIT001 Rendered output does not read back as the model: comment /* b */ reads back on another owner.");

        // D7: a replaced constraint shows in Render() without Reformat, and only its where clause changes.
        yield return new("gene27.d7.type", () => Constraint("class C<T> where T : class\n{\n    int  F;\n}\n", "class", Cs.Constraint("struct")).Render(), "class C<T>\n    where T : struct\n{\n    int F;\n}\n");
        yield return new("gene27.d7.method", () => Constraint("class C\n{\n    void M<T>()  where T : class, new() { }\n}\n", "new()", Cs.Constraint(Cs.Type("System.IDisposable"))).Render(), "class C\n{\n    void M<T>()\n        where T : class, System.IDisposable\n    {\n    }\n}\n");
        yield return new("gene27.d7.second-parameter", () => Constraint("class C<[A] T, U>\n    where T : class\n    where U : new()\n{\n}\n", "new()", Cs.Constraint("struct")).Render(), "class C<[A] T, U>\n    where T : class\n    where U : struct\n{\n}\n");
        yield return new("gene27.d7.delegate", () => Constraint("delegate void D<T>(T x) where T : class;\n", "class", Cs.Constraint("notnull")).Render(), "delegate void D<T>(T x)\n    where T : notnull\n;\n");
        yield return new("gene27.d7.extension", () => Constraint("static class E\n{\n    extension<T>(T x) where T : class\n    {\n    }\n}\n", "class", Cs.Constraint("struct")).Render(), "static class E\n{\n    extension<T>(T x)\n        where T : struct\n    {\n    }\n}\n");
        yield return new("gene27.d7.local-function", () => Constraint("class C\n{\n    void M()\n    {\n        void L<T>() where T : class { }\n    }\n}\n", "class", Cs.Constraint("struct")).Render(), "class C\n{\n    void M()\n    {\n        void L<T>()\n            where T : struct\n        {\n        }\n    }\n}\n");
        yield return new("gene27.d7.renamed-parameter", () => Edit("class C<T> where T : class\n{\n}\n", n => n is TypeParameter, n => ((TypeParameter)n) with { Name = "U" }).Render(), "class C<U>\n    where U : class\n{\n}\n");
        yield return new("gene27.d7.renamed-unconstrained", () => Edit("class C<T>\n{\n    T  F;\n}\n", n => n is TypeParameter, n => ((TypeParameter)n) with { Name = "U" }).Render(), "class C<U>\n{\n    T F;\n}\n");
        yield return new("gene27.d7.owner-renamed", () => Edit("class C<T> where T : IComparable< T >\n{\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Name = "D" }).Render(), "class D<T>\n    where T : IComparable<T>\n{\n}\n");
        yield return new("gene27.d7.last-constraint-removed", () => { var d = Src("class C<T> where T : class\n{\n}\n"); return d.Remove(Find(d, n => n is CsConstraint)).Render(); }, "class C<T>\n{\n}\n");
        yield return new("gene27.d7.constraint-added", () => Edit("class C<T>\n{\n}\n", n => n is TypeParameter, n => ((TypeParameter)n) with { Constraints = new[] { Cs.Constraint("class") } }).Render(), "class C<T>\n    where T : class\n{\n}\n");
        yield return new("gene27.d7.parameter-replaced", () => Edit("class C<T> where T : class\n{\n}\n", n => n is TypeParameter, _ => new TypeParameter("T") { Constraints = new[] { Cs.Constraint("struct") } }).Render(), "class C<T>\n    where T : struct\n{\n}\n");

        // D1: a newly built node next to an embedded directive replaces only its own text; the directive keeps its line.
        // A replacement that needs parentheses there, or a change to the owner itself, renders the owner again with
        // its directive at a line start.
        yield return new("gene27.d1.statement-directive", () => Operand(StatementDirective, "a", Cs.Id("y")), StatementDirective.Replace("x = a", "x = y"));
        yield return new("gene27.d1.left-operand", () => Operand(OperandDirective, "a", Cs.Id("y")), OperandDirective.Replace("= a", "= y"));
        yield return new("gene27.d1.right-operand", () => Operand(OperandDirective, "b", Cs.Id("y")), OperandDirective.Replace("+ b", "+ y"));
        yield return new("gene27.d1.bound-operand", () => EditAt(OperandDirective, n => n is CsExpression { Text: "a" }, 0, n => ((CsExpression)n) with { Text = "y" }), OperandDirective.Replace("= a", "= y"));
        yield return new("gene27.d1.lower-precedence", () => Operand(OperandDirective, "a", Cs.Binary(CsOperator.Multiply, Cs.Id("c"), Cs.Id("d"))), OperandDirective.Replace("= a", "= c * d"));
        yield return new("gene27.d1.needs-parentheses", () => Operand(OperandDirective, "b", Cs.Binary(CsOperator.Subtract, Cs.Id("c"), Cs.Id("d"))), OperandDirective.Replace("+ b", "+ (c - d)"));
        yield return new("gene27.d1.statement-lower-precedence", () => Operand(StatementDirective, "a", Cs.Binary(CsOperator.Multiply, Cs.Id("c"), Cs.Id("d"))), StatementDirective.Replace("x = a", "x = c * d"));
        yield return new("gene27.d1.crlf", () => Operand(OperandDirective.Replace("\n", "\r\n"), "a", Cs.Id("y")), OperandDirective.Replace("= a", "= y").Replace("\n", "\r\n"));
        yield return new("gene27.d1.changed-owner", () => EditAt(OperandDirective, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Subtract, Arguments = new[] { Cs.Id("y"), ((CsExpression)n).Arguments[1] } }), OperandDirective.Replace("= a", "= y").Replace("+ b", "- b"));
        yield return new("gene27.d1.else-statement", () => EditAt(ElseStatement, n => n is CsStatement { Kind: StatementKind.If } s && s.Else != null, 0, n => ((CsStatement)n) with { Else = Cs.If(Cs.Id("e"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("z"))))) }), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            a();\n        }\n        else if (e)\n        {\n            z();\n        }\n    }\n}\n");
        yield return new("gene27.d1.own-line-argument", () => Operand(OwnLineArguments, "a", Cs.Id("y")), "class C\n{\n    int F = G(y, b);\n}\n");
        yield return new("gene27.d1.pattern-directive", () => Src(PatternDirective), "class C\n{\n    int M(object o) => o switch { (1,\n#if X\n#endif\n        2) => 0, _ => 1 };\n}\n");

        // D2: Reformat keeps a line after an embedded directive indented one level past the line its owner starts on.
        yield return new("gene27.d2.reformat-document", () => Src(OperandDirective).Reformat(), OperandDirective);
        yield return new("gene27.d2.reformat-statement-owner", () => Src(StatementDirective).Reformat(), StatementDirective);
        yield return new("gene27.d2.reformat-directive-statement", () => { var d = Src(DirectiveStatements); return d.Reformat(Find(d, n => n is CsStatement { Kind: StatementKind.Directive })); }, DirectiveStatements);
        yield return new("gene27.d2.reformat-disabled-text", () => { var d = Src(DirectiveStatements.Replace("#if DEBUG", "#if NEVER")); return d.Reformat(Find(d, n => n is CsStatement { Kind: StatementKind.DisabledText })); }, DirectiveStatements.Replace("#if DEBUG", "#if NEVER"));

        // D6: a newly built node inside a multi-line chain replaces only its own text; the links keep their lines. A link
        // whose own shape changed renders again without the links below it, unless it cannot (then it renders whole).
        yield return new("gene27.d6.head", () => Operand(ChainSource, "source", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        var r = y.Where(x => x).Select(x => x).ToList();\n    }\n}\n");
        yield return new("gene27.d6.argument", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Lambda }, 0, _ => Cs.Id("f")), "class C\n{\n    void M()\n    {\n        var r = source.Where(f).Select(x => x).ToList();\n    }\n}\n");
        yield return new("gene27.d6.last-argument", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Lambda }, 1, _ => Cs.Id("f")), "class C\n{\n    void M()\n    {\n        var r = source.Where(x => x).Select(f).ToList();\n    }\n}\n");
        yield return new("gene27.d6.link-rebuilt", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 2 && e.Arguments[0].Text == "Where", 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Id("z"))) }), "class C\n{\n    void M()\n    {\n        var r = source.Where(x => x, z).Select(x => x).ToList();\n    }\n}\n");
        yield return new("gene27.d6.block-comment", () => Operand(CommentedChain, "x", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        var r = source /*c*/.Where(y) // step\n            .Select(x) // step\n            .ToList();\n    }\n}\n");
        yield return new("gene27.d6.line-comment", () => Operand(CommentedChain, "x", Cs.Id("y"), 1), "class C\n{\n    void M()\n    {\n        var r = source /*c*/.Where(x) // step\n            .Select(y) // step\n            .ToList();\n    }\n}\n");
        yield return new("gene27.d6.commented-head", () => Operand(CommentedChain, "source", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        var r = y.Where(x) // step\n            .Select(x) // step\n            .ToList();\n    }\n}\n");
        yield return new("gene27.d6.commented-head-crlf", () => Operand(CommentedChain.Replace("\n", "\r\n"), "source", Cs.Id("y")), "class C\r\n{\r\n    void M()\r\n    {\r\n        var r = y.Where(x) // step\r\n            .Select(x) // step\r\n            .ToList();\r\n    }\r\n}\r\n");
        yield return new("gene27.d6.binary-link", () => EditAt(BinaryChain, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Subtract }), "class C\n{\n    int F = a + b - c;\n}\n");
        yield return new("gene27.d6.receiver-parenthesized", () => EditAt(BinaryChain, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Multiply }), "class C\n{\n    int F = (a + b) * c;\n}\n");
        yield return new("gene27.d6.receiver-moved", () => { var d = Src(ChainSource); var head = Find(d, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "Where" }); var top = Find(d, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "ToList" }); return d.Replace(top, ((CsExpression)d.At(top)) with { Arguments = new[] { (CsExpression)d.At(head) } }); }, "class C\n{\n    void M()\n    {\n        var r = source.Where.ToList();\n    }\n}\n");
        yield return new("gene27.d6.link-owns-directive", () => EditAt(DirectiveChain, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 3, 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Literal(3))) }), "class C\n{\n    void M()\n    {\n        a.B(1,\n#if X\n#endif\n            2, 3).C();\n    }\n}\n");

        // D4: container comments keep their place and blank lines after member Remove, Insert and reorder.
        yield return new("gene27.d4.remove-first", () => Members(TypeComments, 1, 2), "class C\n{\n    // about B\n\n    int B;\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.remove-middle", () => Members(TypeComments, 0, 2), "class C\n{\n    int A;\n    // about B\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.remove-last", () => Members(TypeComments, 0, 1), "class C\n{\n    int A;\n    // about B\n\n    int B;\n    // container tail\n}\n");
        yield return new("gene27.d4.remove-two", () => Members(TypeComments, 0), "class C\n{\n    int A;\n    // about B\n    // container tail\n}\n");
        yield return new("gene27.d4.remove-first-two", () => Members(TypeComments, 2), "class C\n{\n    // about B\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.multi-line-comment-added", () => { var d = Src(TypeComments); var p = Find(d, n => n is CsTypeDeclaration); var t = (CsTypeDeclaration)d.At(p); return d.Replace(p, t with { Members = new[] { t.Members[0], t.Members[2] }, Comments = new(t.Comments.Append(new CsComment("/* a\n   b */", CommentKind.Block, CommentPlacement.Container))) }); }, "class C\n{\n    int A;\n    // about B\n    // container tail\n\n    int D;\n    /* a\n   b */\n}\n");
        yield return new("gene27.d4.comment-moved-in", () => { var d = Src(TwoTypes); var a = Find(d, n => n is CsTypeDeclaration { Name: "A" }); var b = Find(d, n => n is CsTypeDeclaration { Name: "B" }); var source = (CsTypeDeclaration)d.At(a); var t = (CsTypeDeclaration)d.At(b); d = d.Replace(a, source with { Comments = Values<CsComment>.Empty }); return d.Replace(Find(d, n => n is CsTypeDeclaration { Name: "B" }), t with { Members = new CsMember[] { t.Members[0], new CsField(Cs.Int, "W") }, Comments = new[] { source.Comments[0] } }); }, "class A\n{\n    int X;\n    int Y;\n}\n\nclass B\n{\n    // moved\n\n    int Z;\n    int W;\n}\n");
        yield return new("gene27.d4.reorder", () => Members(TypeComments, 2, 0, 1), "class C\n{\n    // container tail\n\n    int D;\n    int A;\n    // about B\n\n    int B;\n}\n");
        yield return new("gene27.d4.insert", () => Members(TypeComments, 0, 1, -1, 2), "class C\n{\n    int A;\n    // about B\n\n    int B;\n    int Z;\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.replace-first", () => Members(TypeComments, -1, 1, 2), "class C\n{\n    int Z;\n    // about B\n\n    int B;\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.block-remove-first", () => Members(BlockComments, 1), "class C\n{\n    void M()\n    {\n        // middle\n\n        b();\n        // tail\n    }\n}\n");
        yield return new("gene27.d4.block-remove-last", () => Members(BlockComments, 0), "class C\n{\n    void M()\n    {\n        a();\n        // middle\n        // tail\n    }\n}\n");
        yield return new("gene27.d4.namespace-remove-first", () => Members(NamespaceComments, 1), "namespace N\n{\n    // between\n\n    class B\n    {\n    }\n}\n");
        yield return new("gene27.d4.namespace-remove-last", () => Members(NamespaceComments, 0), "namespace N\n{\n    class A\n    {\n    }\n    // between\n}\n");
        // Reformat keeps its own blank lines; a comment stays before the item that followed it in source, a blank line
        // apart from it (phase 3, R3).
        yield return new("gene27.d4.reformat-after-insert", () => { var d = Members(TypeComments, -1, 0, 1, 2); return d.Reformat(Find(d, n => n is CsTypeDeclaration)); }, "class C\n{\n    int Z;\n    int A;\n    // about B\n\n    int B;\n    // container tail\n\n    int D;\n}\n");

        // Phase 3 (residual pre-existing defects, reproduced on 194c591). R1: a type header change made in the same edit
        // as a member list change renders, and so does a header change on its own, with the members kept byte for byte.
        yield return new("gene27.r1.modifier-and-remove", () => Type("class C\n{\n    int  A;\n    int  B;\n}\n", t => t with { Modifiers = new[] { "public" }, Members = new[] { t.Members[1] } }), "public class C\n{\n    int B;\n}\n");
        yield return new("gene27.r1.modifier-only", () => Type("class C\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C\n{\n    int A;\n}\n");
        yield return new("gene27.r1.base-and-insert", () => Type("class C\n{\n    int  A;\n}\n", t => t with { BaseTypes = new[] { Cs.Type("B") }, Members = new(t.Members.Append(new CsField(Cs.Int, "Z"))) }), "class C : B\n{\n    int A;\n    int Z;\n}\n");
        yield return new("gene27.r1.nested-header", () => Type(Headers, t => t with { Modifiers = new[] { "internal" }, Members = new[] { t.Members[1] } }), "namespace N\n{\n    // about C\n    [A]\n    internal class C<T>\n        where T : class\n    {\n        int B;\n    }\n}\n");
        yield return new("gene27.r1.attribute-and-reorder", () => Type(Headers, t => t with { Attributes = new[] { new CsAttribute(Cs.Type("B")) }, Members = new[] { t.Members[1], t.Members[0] } }), "namespace N\n{\n    // about C\n    [B]\n    class C<T>\n        where T : class\n    {\n        int B;\n        int A;\n    }\n}\n");
        yield return new("gene27.r1.renamed-and-removed", () => Type(Headers, t => t with { Name = "D", Members = new[] { t.Members[0] } }), "namespace N\n{\n    // about C\n    [A]\n    class D<T>\n        where T : class\n    {\n        int A;\n    }\n}\n");
        yield return new("gene27.r1.enum", () => Type("enum E\n{\n    A,\n    B\n}\n", t => t with { Modifiers = new[] { "public" }, Members = new[] { t.Members[0] } }), "public enum E\n{\n    A,\n}\n");
        yield return new("gene27.r1.comment-added", () => Type("class C\n{\n    int  A;\n    int  B;\n}\n", t => t with { Comments = new(t.Comments.Append(new CsComment("// new", CommentKind.Line))), Members = new[] { t.Members[1] } }), "// new\nclass C\n{\n    int B;\n}\n");
        yield return new("gene27.r1.container-comment-removed", () => Type(TypeComments, t => t with { Modifiers = new[] { "public" }, Comments = new[] { t.Comments[1] }, Members = new[] { t.Members[0], t.Members[2] } }), "public class C\n{\n    int A;\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.r1.switch-value-and-section", () => EditAt("class C\n{\n    void M()\n    {\n        switch (a)\n        {\n            case 1:  break;\n            case 2:  break;\n        }\n    }\n}\n", n => n is CsStatement { Kind: StatementKind.Switch }, 0, n => ((CsStatement)n) with { Expression = ((CsStatement)n).Expression! with { Text = "b" }, Sections = new[] { ((CsStatement)n).Sections[1] } }), "class C\n{\n    void M()\n    {\n        switch (b)\n        {\n            case 2:\n                break;\n        }\n    }\n}\n");

        // R2: an edit inside a brace-less embedded statement replaces only what changed; the statement stays brace-less.
        yield return new("gene27.r2.if-same-line", () => Operand(Embedded, "x", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            y = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.literal", () => EditAt(Embedded, n => n is CsExpression { Kind: ExpressionKind.Literal }, 0, _ => Cs.Literal(2)), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 2;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.if-next-line", () => Operand(Embedded, "a", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            y();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.else", () => Operand(Embedded, "b", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            y();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.for", () => Operand(Embedded, "e", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            y();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.foreach", () => Operand(Embedded, "f", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            y();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.while", () => Operand(Embedded, "h", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            y();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.do", () => Operand(Embedded, "i", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            y();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.using", () => Operand(Embedded, "l", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            y();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.lock", () => Operand(Embedded, "n", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.r2.statement-replaced", () => EditAt(Embedded, n => n is CsStatement { Kind: StatementKind.Expression }, 1, _ => Cs.Statement(Cs.Call(Cs.Id("z")))), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            z();\n        }\n        else\n        {\n            b();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.else-statement-replaced", () => EditAt(Embedded, n => n is CsStatement { Kind: StatementKind.Expression }, 2, _ => Cs.Statement(Cs.Call(Cs.Id("z")))), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            x = 1;\n        }\n        if (d)\n        {\n            a();\n        }\n        else\n        {\n            z();\n        }\n        for (; ; )\n        {\n            e();\n        }\n        foreach (var v in w)\n        {\n            f();\n        }\n        while (g)\n        {\n            h();\n        }\n        do\n        {\n            i();\n        }\n        while (j);\n        using (k)\n        {\n            l();\n        }\n        lock (m)\n        {\n            n();\n        }\n    }\n}\n");
        yield return new("gene27.r2.trailing-comment", () => Operand("class C\n{\n    void M()\n    {\n        if (c) a(); // note\n        if (d) /* x */ e();\n    }\n}\n", "a", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            y();\n        } // note\n        /* x */\n        if (d)\n        {\n            e();\n        }\n    }\n}\n");
        yield return new("gene27.r2.condition-and-statement", () => EditAt("class C\n{\n    void M()\n    {\n        while (g)  h();\n    }\n}\n", n => n is CsStatement { Kind: StatementKind.While }, 0, n => ((CsStatement)n) with { Expression = Cs.Id("y"), Body = ((CsStatement)n).Body! with { Statements = new[] { Cs.Statement(Cs.Call(Cs.Id("z"))) } } }), "class C\n{\n    void M()\n    {\n        while (y)\n        {\n            z();\n        }\n    }\n}\n");
        // A brace-less body that can no longer stand without braces (two statements, a declaration, a moved statement)
        // renders its owner again, with braces, as before.
        yield return new("gene27.r2.two-statements", () => EditAt("class C\n{\n    void M()\n    {\n        if (c)  a();\n    }\n}\n", n => n is CsBody { Statements.Count: 1 } b && b.Statements[0].Kind == StatementKind.Expression, 0, n => ((CsBody)n) with { Statements = new(((CsBody)n).Statements.Append(Cs.Statement(Cs.Call(Cs.Id("z"))))) }), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            a();\n            z();\n        }\n    }\n}\n");
        yield return new("gene27.r2.declaration", () => EditAt("class C\n{\n    void M()\n    {\n        if (c)  a();\n    }\n}\n", n => n is CsStatement { Kind: StatementKind.Expression }, 0, _ => Cs.Local(Cs.Int, "z", Cs.Literal(1))), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            int z = 1;\n        }\n    }\n}\n");
        yield return new("gene27.r2.moved-statement", () => { var d = Src("class C\n{\n    void M()\n    {\n        if (c)  a();\n        b();\n    }\n}\n"); var b = d.Descendants().Where(n => n.Node is CsStatement { Kind: StatementKind.Expression }).Last().Path; var moved = d.At(b); d = d.Remove(b); return d.Replace(Find(d, n => n is CsStatement { Kind: StatementKind.Expression }), moved); }, "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            b();\n        }\n    }\n}\n");
        yield return new("gene27.r2.reformat", () => Src("class C\n{\n    void M()\n    {\n        if (c)  a();\n    }\n}\n").Reformat(), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            a();\n        }\n    }\n}\n");

        // R3: Reformat keeps a blank line between a container comment and the item after it, so the comment reads back
        // as the container's own.
        yield return new("gene27.r3.type", () => Owners(Src(TypeComments).Reformat().Render()), "class C\n{\n    int A;\n    // about B\n\n    int B;\n    // container tail\n\n    int D;\n}\nowners: CsTypeDeclaration[Container,Container]");
        yield return new("gene27.r3.block", () => Owners(Src(BlockComments).Reformat().Render()), "class C\n{\n    void M()\n    {\n        a();\n        // middle\n\n        b();\n        // tail\n    }\n}\nowners: CsTypeDeclaration[] CsBody[Container,Container]");
        yield return new("gene27.r3.first-item", () => Owners(Src("class C\n{\n    // head\n\n    int A;\n}\n").Reformat().Render()), "class C\n{\n    // head\n\n    int A;\n}\nowners: CsTypeDeclaration[Container]");

        // QA O1: a reshaped chain link with a trailing block comment leaves no trailing whitespace.
        yield return new("gene27.o1.trailing-block-comment", () => EditAt(CommentedLink, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments[0].Text == "Select", 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Id("z"))) }), "class C\n{\n    void M()\n    {\n        var r = source.Select(x => x * 2, z) /* s */ .ToList();\n    }\n}\n");
        yield return new("gene27.o1.same-line-comment", () => EditAt("class C\n{\n    void M()\n    {\n        var r = source.Select(x) /* s */ .ToList();\n    }\n}\n", n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments[0].Text == "Select", 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Id("z"))) }), "class C\n{\n    void M()\n    {\n        var r = source.Select(x, z) /* s */ .ToList();\n    }\n}\n");

        // QA O2a: a replacement that needs parentheses is replaced in place, so an indented directive keeps its line;
        // an owner rendered again keeps the directive's source indentation too.
        yield return new("gene27.o2a.lower-precedence", () => Operand(IndentedRegion, "b", OrElse("p", "q")), "class C\n{\n    int G = (p || q)\n#region Q\n        + c;\n\n    #endregion\n}\n");
        yield return new("gene27.o2a.owner-changed", () => EditAt(IndentedRegion, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Subtract }), "class C\n{\n    int G = b\n#region Q\n        - c;\n\n    #endregion\n}\n");
        yield return new("gene27.o2a.right-operand", () => Operand(IndentedRegion, "c", OrElse("p", "q")), "class C\n{\n    int G = b\n#region Q\n        + (p || q);\n\n    #endregion\n}\n");
        yield return new("gene27.o2a.argument", () => Operand("class C\n{\n    int G = F(a,\n        b);\n}\n", "b", Cs.Binary(CsOperator.Subtract, Cs.Id("p"), Cs.Id("q"))), "class C\n{\n    int G = F(a, p - q);\n}\n");

        // QA O2b: a lambda's body is replaced in place; the parameter form and the line break stay.
        yield return new("gene27.o2b.body", () => EditAt(LambdaBody, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, _ => Cs.Id("y")), "class C\n{\n    Func<int, int> F = x => y;\n}\n");
        yield return new("gene27.o2b.lower-precedence-body", () => EditAt(LambdaBody, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, _ => OrElse("p", "q")), "class C\n{\n    Func<int, int> F = x => p || q;\n}\n");
        yield return new("gene27.o2b.operand", () => Operand(LambdaBody, "x", Cs.Id("y")), "class C\n{\n    Func<int, int> F = x => y + 1;\n}\n");
        yield return new("gene27.o2b.block-body", () => Operand("class C\n{\n    Action F = () =>\n    {\n        a();\n    };\n}\n", "a", Cs.Id("y")), "class C\n{\n    Action F = () =>\n    {\n        y();\n    };\n}\n");
        yield return new("gene27.o2b.parameter-changed", () => EditAt(LambdaBody, n => n is CsExpression { Kind: ExpressionKind.Lambda }, 0, n => ((CsExpression)n) with { IsStatic = true }), "class C\n{\n    Func<int, int> F = static x => x + 1;\n}\n");

        // QA O2c: an edit of an interpolation hole replaces only that hole's expression; the literal keeps its text.
        yield return new("gene27.o2c.alignment-hole", () => Operand(Interpolated, "y", Cs.Id("z")), "class C\n{\n    string S = $\"a{z, 5}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.format-hole", () => Operand(Interpolated, "w", Cs.Id("z")), "class C\n{\n    string S = $\"a{y, 5}b{z:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.raw-hole", () => Operand(Interpolated, "y", Cs.Id("z"), 1), "class C\n{\n    string S = $\"a{y, 5}b{w:x2}\";\n    string R = $\"\"\"\na {z}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.raw-second-hole", () => Operand(Interpolated, "w", Cs.Id("z"), 1), "class C\n{\n    string S = $\"a{y, 5}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {z}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.conditional-hole", () => Operand(Interpolated, "y", new CsExpression(ExpressionKind.Conditional, "", new[] { Cs.Id("p"), Cs.Id("q"), Cs.Id("r") })), "class C\n{\n    string S = $\"a{(p ? q : r), 5}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.alignment", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.Literal, Text: "5" }, 0, _ => Cs.Literal(7)), "class C\n{\n    string S = $\"a{y, 7}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.text-changed", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.StringText, Text: "a" }, 0, _ => new CsExpression(ExpressionKind.StringText, "c", Values<CsExpression>.Empty)), "class C\n{\n    string S = $\"c{y, 5}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");

        // QA O2d: an edit never moves text across an inactive branch; the replaced argument keeps its place after it.
        yield return new("gene27.o2d.argument", () => Operand(InactiveArgument, "c", OrElse("p", "q")), "class C\n{\n    void M()\n    {\n        h(a,\n#if X\n          b);\n#else\n            p || q);\n#endif\n    }\n}\n");
        yield return new("gene27.o2d.owner-changed", () => EditAt(InactiveArgument, n => n is CsExpression { Kind: ExpressionKind.Invocation }, 0, n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0], ((CsExpression)n).Arguments[1], OrElse("p", "q") }, Comments = new[] { new CsComment("/* c */", CommentKind.Block) } }), "class C\n{\n    void M()\n    {\n        /* c */ h(a,\n#if X\n          b);\n#else\n            p || q);\n#endif\n    }\n}\n");

        // Phase 3 guards and coverage: what falls back to rendering whole, and each slot a replacement can take (since
        // phase 4 a header's own comment or directive keeps its place, and a top-level embedded statement its braces).
        yield return new("gene27.r1.header-comment", () => Type("class C /* x */\n{\n    int  A;\n    int  B;\n}\n", t => t with { Modifiers = new[] { "public" }, Members = new[] { t.Members[1] } }), "public class C\n{\n    /* x */\n\n    int B;\n}\n");
        yield return new("gene27.r1.no-body", () => Type("record R(int  X);\n", t => t with { Modifiers = new[] { "public" } }), "public record R(int X)\n{\n}\n");
        yield return new("gene27.r1.header-directive", () => Type("class C\n#if X\n    : B\n#endif\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C\n#if X\n    : B\n#endif\n{\n    int A;\n}\n");
        yield return new("gene27.r1.renamed-with-modifier", () => Type("class C\n{\n    int  A;\n}\n", t => t with { Name = "D", Modifiers = new[] { "sealed" } }), "sealed class D\n{\n    int A;\n}\n");
        yield return new("gene27.r1.base-replaced", () => Edit("class C : A\n{\n    int  X;\n}\n", n => n is CsType { Name: "A" }, _ => Cs.Type("B")), "class C : B\n{\n    int X;\n}\n");
        yield return new("gene27.r1.trailing-comment", () => Type("class C\n{\n    int  A;\n    int  B;\n} // t\n", t => t with { Modifiers = new[] { "public" }, Members = new[] { t.Members[0] } }), "public class C\n{\n    int A;\n} // t\n");
        yield return new("gene27.r2.reformat-lambda-body", () => { var d = Src("class C\n{\n    Func<int, int> F = x =>\n        x  +  1;\n}\n"); return d.Reformat(Find(d, n => n is CsBody { Expression: not null })); }, "class C\n{\n    Func<int, int> F = x => x + 1;\n}\n");
        yield return new("gene27.r2.top-level", () => { var d = Src("if (a) F(1);\nG(2);\n"); var p = Find(d, n => n is CsExpression { Text: "1" }); return d.Replace(p, ((CsExpression)d.At(p)) with { Text = "7" }); }, "if (a)\n{\n    F(7);\n}\nG(2);\n");
        yield return new("gene27.splice.coalesce-left", () => Operand("class C\n{\n    int G = a  ??  b;\n}\n", "a", Cs.Binary(CsOperator.Coalesce, Cs.Id("p"), Cs.Id("q"))), "class C\n{\n    int G = (p ?? q) ?? b;\n}\n");
        yield return new("gene27.splice.coalesce-throw", () => Operand("class C\n{\n    int G = a  ??  b;\n}\n", "b", new CsExpression(ExpressionKind.Throw, "", new[] { Cs.Id("e") })), "class C\n{\n    int G = a ?? throw e;\n}\n");
        yield return new("gene27.splice.invocation-receiver", () => Operand("class C\n{\n    int G = F( a );\n}\n", "F", Cs.Id("H")), "class C\n{\n    int G = H(a);\n}\n");
        yield return new("gene27.splice.index-argument", () => Operand("class C\n{\n    int G = a[ b ];\n}\n", "b", OrElse("p", "q")), "class C\n{\n    int G = a[p || q];\n}\n");
        yield return new("gene27.splice.condition", () => Operand("class C\n{\n    int G = c  ?  a : b;\n}\n", "c", new CsExpression(ExpressionKind.Assignment, "", new[] { Cs.Id("p"), Cs.Id("q") })), "class C\n{\n    int G = (p = q) ? a : b;\n}\n");
        yield return new("gene27.splice.branch", () => Operand("class C\n{\n    int G = c  ?  a : b;\n}\n", "a", new CsExpression(ExpressionKind.Assignment, "", new[] { Cs.Id("p"), Cs.Id("q") })), "class C\n{\n    int G = c ? p = q : b;\n}\n");
        yield return new("gene27.splice.assigned", () => Operand("class C\n{\n    void M()\n    {\n        x  =  a;\n    }\n}\n", "a", new CsExpression(ExpressionKind.Assignment, "", new[] { Cs.Id("p"), Cs.Id("q") })), "class C\n{\n    void M()\n    {\n        x = p = q;\n    }\n}\n");
        yield return new("gene27.splice.assignee", () => Operand("class C\n{\n    void M()\n    {\n        x  =  a;\n    }\n}\n", "x", Cs.Id("y")), "class C\n{\n    void M()\n    {\n        y = a;\n    }\n}\n");
        yield return new("gene27.splice.unary-operand", () => Operand("class C\n{\n    int G = - a;\n}\n", "a", Cs.Binary(CsOperator.Add, Cs.Id("p"), Cs.Id("q"))), "class C\n{\n    int G = -(p + q);\n}\n");
        yield return new("gene27.splice.returned", () => Operand("class C\n{\n    int M()\n    {\n        return  a;\n    }\n}\n", "a", Cs.Id("b")), "class C\n{\n    int M()\n    {\n        return b;\n    }\n}\n");
        yield return new("gene27.splice.returned-lower", () => Operand("class C\n{\n    int M()\n    {\n        return  a;\n    }\n}\n", "a", OrElse("p", "q")), "class C\n{\n    int M()\n    {\n        return p || q;\n    }\n}\n");
        yield return new("gene27.splice.initializer", () => EditAt("class C\n{\n    object G = new D()  { X = 1 };\n}\n", n => n is CsExpression { Kind: ExpressionKind.Initializer }, 0, _ => new CsExpression(ExpressionKind.Initializer, "", new[] { new CsExpression(ExpressionKind.Assignment, "", new[] { Cs.Id("Y"), Cs.Literal(2) }) })), "class C\n{\n    object G = new D() { Y = 2 };\n}\n");
        yield return new("gene27.o2c.parts-changed", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.InterpolatedString }, 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Take(2)) }), "class C\n{\n    string S = $\"a{y, 5}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.style-changed", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.InterpolatedString }, 0, n => ((CsExpression)n) with { StringStyle = CsStringStyle.Verbatim }), "class C\n{\n    string S = $@\"a{y, 5}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.alignment-removed", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.Interpolation }, 0, n => ((CsExpression)n) with { Alignment = null }), "class C\n{\n    string S = $\"a{y}b{w:x2}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");
        yield return new("gene27.o2c.format-changed", () => EditAt(Interpolated, n => n is CsExpression { Kind: ExpressionKind.Interpolation }, 1, n => ((CsExpression)n) with { Format = "x4" }), "class C\n{\n    string S = $\"a{y, 5}b{w:x4}\";\n    string R = $\"\"\"\na {y}\n  b {w}\n\"\"\";\n}\n");

        // QA F1: a then-branch that now ends in an if without an else, followed by the owner's `else`, renders the owner
        // whole (with braces) instead of being replaced in place, so the `else` keeps its `if`.
        yield return new("gene27.f1.then-replaced", () => Statement(ThenElse, 0, ElselessIf()), InM("        if (a)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.f1.then-replaced-lines", () => Statement(ThenElseLines, 0, ElselessIf()), InM("        if (a)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.f1.nested-else-removed", () => EditAt(InM("        if (a) if (b) z(); else w(); else y();\n"), n => n is CsStatement { Kind: StatementKind.If, Expression.Text: "b" }, 0, n => ((CsStatement)n) with { Else = null }), InM("        if (a)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.f1.through-loop", () => Statement(InM("        if (a) while (c) x(); else y();\n"), 0, ElselessIf()), InM("        if (a)\n        {\n            while (c)\n            {\n                if (b)\n                {\n                    z();\n                }\n            }\n        }\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.f1.else-if-middle", () => Statement(InM("        if (a) x(); else if (d) y(); else w();\n"), 1, ElselessIf()), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            x();\n        }\n        else if (d)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n        else\n        {\n            w();\n        }\n    }\n}\n");
        // Guards: a then-branch that still ends in a complete statement is replaced in place.
        yield return new("gene27.f1.if-with-else", () => Statement(ThenElse, 0, Cs.If(Cs.Id("b"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("z")))), Cs.Block(Cs.Statement(Cs.Call(Cs.Id("w")))))), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            if (b)\n            {\n                z();\n            }\n            else\n            {\n                w();\n            }\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.f1.loop-statement", () => Operand(InM("        if (a) while (c) x(); else y();\n"), "x", Cs.Id("q")), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            while (c)\n            {\n                q();\n            }\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.f1.braced-loop", () => Operand(InM("        if (a) while (c) { x(); } else y();\n"), "x", Cs.Id("q")), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            while (c)\n            {\n                q();\n            }\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.f1.new-loop", () => Statement(ThenElse, 0, Cs.While(Cs.Id("c"), Cs.Body(ElselessIf()))), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            while (c)\n            {\n                if (b)\n                {\n                    z();\n                }\n            }\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.f1.two-statements", () => EditAt(ThenElse, n => n is CsBody { Statements.Count: 1 } b && b.Statements[0].Kind == StatementKind.Expression, 0, n => ((CsBody)n) with { Statements = new(((CsBody)n).Statements.Append(Cs.Statement(Cs.Call(Cs.Id("z"))))) }), InM("        if (a)\n        {\n            x();\n            z();\n        }\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.f1.same-line-block", () => Statement(InM("        if (c)  a();\n"), 0, ElselessIf()), "class C\n{\n    void M()\n    {\n        if (c)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n    }\n}\n");

        // Phase 4 (GENE-27 leftovers, reproduced on af570e4). L1: a brace-less embedded statement that is a declaration
        // or a labeled statement is not valid C# (CS1023), so it is not parsed typed: the statement holding it is kept
        // as raw, together with its `else`.
        yield return new("gene27.p4.l1.label", () => Parsed(InM("        if (a) L: x(); else y();\n")), "Partial: PARSE002 An embedded statement cannot be a declaration or a labeled statement.\nif (a) L: x(); else y();");
        yield return new("gene27.p4.l1.declaration", () => Parsed(InM("        while (c) int x = 1;\n")), "Partial: PARSE002 An embedded statement cannot be a declaration or a labeled statement.\nwhile (c) int x = 1;");
        yield return new("gene27.p4.l1.else-label", () => Parsed(InM("        if (a) { } else L: y();\n")), "Partial: PARSE002 An embedded statement cannot be a declaration or a labeled statement.\nif (a) { } else L: y();");
        yield return new("gene27.p4.l1.local-function", () => Parsed(InM("        if (a) void F() { }\n")), "Partial: PARSE002 An embedded statement cannot be a declaration or a labeled statement.\nif (a) void F() { }");
        yield return new("gene27.p4.l1.top-level", () => Cs.Parse("if (a) L: x(); else y();\n"), "Partial; diagnostics: PARSE001 Error @0+24 1:1 An embedded statement cannot be a declaration or a labeled statement.; if (a) L: x(); else y();\n");
        yield return new("gene27.p4.l1.tree", () => { var t = Cs.ParseTree("if (a) L: x(); else y();"); return t.Coverage + " " + t.Diagnostics.Single().Message + " " + t.Root.Render(); }, "Opaque An embedded statement cannot be a declaration or a labeled statement. if (a) L: x(); else y();");
        yield return new("gene27.p4.l1.top-level-block", () => Cs.Parse("if (a) L: { x(); } else y();\n").Diagnostics.Single().Message, "An embedded statement cannot be a declaration or a labeled statement.");
        yield return new("gene27.p4.l1.malformed-then", () => Parsed(InM("        if (a) x(@@); else y();\n")), "Partial: PARSE001 Unterminated literal or comment. | PARSE002 Expected ')'.\nif (a) x(@@); else y();");
        yield return new("gene27.p4.l1.malformed-block-then", () => Parsed(InM("        if (a @@) { } else { z(); }\n        w();\n")), "Partial: PARSE001 Unterminated literal or comment. | PARSE002 Expected ')'.\nif (a @@) { } else { z(); }");
        yield return new("gene27.p4.l1.label-statement", () => Parsed(InM("        L: x();\n        if (a) { L2: y(); }\n")), "Typed: \n");
        // Defence in depth: a label put in a brace-less then-branch by an edit writes what it labels after it, so the
        // owner renders whole (with braces) whatever the label holds.
        yield return new("gene27.p4.l1.label-holds-if", () => Statement(ThenElse, 0, Labeled(ElselessIf())), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            L:\n            if (b)\n            {\n                z();\n            }\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l1.label-holds-call", () => Statement(ThenElse, 0, Labeled(Cs.Statement(Cs.Call(Cs.Id("z"))))), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            L:\n            z();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l1.label-empty", () => Statement(ThenElse, 0, Labeled()), "invalid: EMIT001 Rendered output does not read back as the model: statement Label reads back with 0 parts instead of 1.");

        // L2: an `if` put after `else` on the same line keeps the else-if chain's layout.
        yield return new("gene27.p4.l2.else-if", () => Statement(ThenElse, 1, ElselessIf()), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n    }\n}\n");
        yield return new("gene27.p4.l2.else-if-tabs", () => Statement(ThenElse.Replace("    ", "\t"), 1, ElselessIf()), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n    }\n}\n");
        yield return new("gene27.p4.l2.else-while", () => Statement(ThenElse, 1, Cs.While(Cs.Id("c"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("z")))))), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            while (c)\n            {\n                z();\n            }\n        }\n    }\n}\n");

        // L3: a comment between an if's then-branch and its `else` stays before `else` when the if renders whole, and
        // Reformat keeps it there too.
        yield return new("gene27.p4.l3.then-replaced", () => Statement(CommentBeforeElse, 0, ElselessIf()), InM("        if (a)\n        {\n            if (b)\n            {\n                z();\n            }\n        }\n        // lead\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.p4.l3.reformat", () => Src(CommentBeforeElse).Reformat(), InM("        if (a)\n        {\n            x(); // c\n        }\n        // lead\n        else\n        {\n            y();\n        }\n"));
        yield return new("gene27.p4.l3.leading", () => Src(InM("        // head\n        if (a) x();\n        else y();\n")).Reformat(), "class C\n{\n    void M()\n    {\n        // head\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.else-if", () => Src(InM("        if (a) x();\n        // lead\n        else if (b) y();\n")).Reformat(), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            x();\n        }\n        // lead\n        else if (b)\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.header-comment", () => Src(InM("        if /* h */ (a) x();\n        else y();\n")).Reformat(), "class C\n{\n    void M()\n    {\n        /* h */\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.else-removed", () => EditAt(CommentBeforeElse, n => n is CsStatement { Kind: StatementKind.If }, 0, n => ((CsStatement)n) with { Else = null }), "class C\n{\n    void M()\n    {\n        // lead\n        if (a)\n        {\n            x(); // c\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.new-comment", () => EditAt(ThenElse, n => n is CsStatement { Kind: StatementKind.If }, 0, n => ((CsStatement)n) with { Comments = new[] { new CsComment("// new", CommentKind.Line, CommentPlacement.Container) } }).Reformat(), "class C\n{\n    void M()\n    {\n        // new\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.foreign-comment", () => EditAt(ThenElse, n => n is CsStatement { Kind: StatementKind.If }, 0, n => ((CsStatement)n) with { Comments = new[] { Foreign() } }).Reformat(), "class C\n{\n    void M()\n    {\n        // far\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");
        yield return new("gene27.p4.l3.built-if", () => InMethod(Cs.If(Cs.Id("a"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("x")))), Cs.Block(Cs.Statement(Cs.Call(Cs.Id("y"))))) with { Comments = new[] { Foreign() } }), "class C\n{\n    void M()\n    {\n        // far\n        if (a)\n        {\n            x();\n        }\n        else\n        {\n            y();\n        }\n    }\n}\n");

        // Interpolation holes: an edit inside a hole's expression changes only the edited node, and a changed text
        // segment or format is replaced on its own.
        yield return new("gene27.p4.hole.nested", () => Operand(Holes, "x", Cs.Id("zz")), "class C\n{\n    string S = $\"a{$\"b{zz, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.raw-lines", () => Operand(Holes, "x", Cs.Id("zz"), 1), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{zz + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.argument", () => Operand(Holes, "w", new CsExpression(ExpressionKind.Conditional, "", new[] { Cs.Id("p"), Cs.Id("q"), Cs.Id("r") })), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(p ? q : r)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.alignment-operand", () => Operand(Holes, "w", Cs.Id("u"), 1), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -u}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.kept-conditional", () => EditAt(Holes, n => n is CsExpression { Kind: ExpressionKind.Invocation }, 0, n => ((CsExpression)n) with { Kind = ExpressionKind.Conditional, Arguments = new[] { Cs.Id("p"), Cs.Id("q"), Cs.Id("r") } }), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{(p ? q : r)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.text", () => Part(Holes, e => e is { Kind: ExpressionKind.StringText, Text: "b" }, 0, _ => Text("q\"{")), "class C\n{\n    string S = $\"a{$\"q\\\"{{{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.format", () => Part(Holes, e => e.Format == "x2", 0, e => e with { Format = "x4" }), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v:x4}\";\n}\n");
        yield return new("gene27.p4.hole.verbatim-text", () => Part("class C\n{\n    string S = $@\"a{y}\";\n}\n", e => e.Kind == ExpressionKind.StringText, 0, _ => Text("\"b")), "class C\n{\n    string S = $@\"\"\"b{y}\";\n}\n");
        yield return new("gene27.p4.hole.raw-text", () => Part(RawHoles, e => e is { Kind: ExpressionKind.StringText, Text: "a" }, 0, _ => Text("c")), "class C\n{\n    string R = $\"\"\"c{y:x2}b\"\"\";\n    string M = $\"\"\"\na {y}\nb\n\"\"\";\n}\n");
        yield return new("gene27.p4.hole.raw-format", () => Part(RawHoles, e => e.Format == "x2", 0, e => e with { Format = "x4" }), "class C\n{\n    string R = $\"\"\"a{y:x4}b\"\"\";\n    string M = $\"\"\"\na {y}\nb\n\"\"\";\n}\n");
        yield return new("gene27.p4.hole.raw-text-brace", () => Part(RawHoles, e => e is { Kind: ExpressionKind.StringText, Text: "a" }, 0, _ => Text("{")), "class C\n{\n    string R = $$\"\"\"{{{y:x2}}b\"\"\";\n    string M = $\"\"\"\na {y}\nb\n\"\"\";\n}\n");
        yield return new("gene27.p4.hole.raw-format-brace", () => Part(RawHoles, e => e.Format == "x2", 0, e => e with { Format = "\"" }), "invalid: EMIT001 Rendered output does not read back as the model: CsTypeDeclaration reads back as CsRaw.");
        yield return new("gene27.p4.hole.raw-lines-text", () => Part(RawHoles, e => e.Kind == ExpressionKind.StringText, 3, _ => Text("c")), "class C\n{\n    string R = $\"\"\"a{y:x2}b\"\"\";\n    string M = $\"\"\"a {y}c\"\"\";\n}\n");
        yield return new("gene27.p4.hole.text-comment", () => Part(Holes, e => e is { Kind: ExpressionKind.StringText, Text: "b" }, 0, e => e with { Comments = new[] { new CsComment("/* t */", CommentKind.Block) } }), "invalid: EMIT001 Rendered output does not read back as the model: comment /* t */ reads back on another owner.");
        yield return new("gene27.p4.hole.text-to-hole", () => Part(Holes, e => e is { Kind: ExpressionKind.StringText, Text: "b" }, 0, _ => new CsExpression(ExpressionKind.Interpolation, "", new[] { Cs.Id("k") })), "class C\n{\n    string S = $\"a{$\"{k}{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v:x2}\";\n}\n");
        yield return new("gene27.p4.hole.format-removed", () => Part(Holes, e => e.Format == "x2", 0, e => e with { Format = null }), "class C\n{\n    string S = $\"a{$\"b{x, 2}\"}c\";\n    string R = $\"\"\"{x + y}\"\"\";\n    string T = $\"a{f(w)}b{y, -w}c{v}\";\n}\n");
        yield return new("gene27.p4.hole.part-comment", () => Part(Holes, e => e.Format == "x2", 0, e => e with { Comments = new[] { new CsComment("/* p */", CommentKind.Block) } }), "invalid: EMIT001 Rendered output does not read back as the model: comment /* p */ reads back on another owner.");
        yield return new("gene27.p4.hole.anonymous-method", () => Operand("class C\n{\n    string S = $\"{F(delegate (int q) { return  q; })}\";\n}\n", "q", Cs.Id("r"), 0), "class C\n{\n    string S = $\"{F(delegate(int q)\n{\nreturn r;\n})}\";\n}\n");

        // A type header that holds its own comment or directive renders only the header again, merged into its source
        // token by token, so the comment or directive keeps its place and the members keep their text.
        yield return new("gene27.p4.header.modifier", () => Type("class C /* x */ : B\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C : B\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.base-removed", () => Type("class C /* x */ : B\n{\n    int  A;\n}\n", t => t with { BaseTypes = Values<CsType>.Empty }), "class C\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.base-added", () => Type("class C /* x */ : B\n{\n    int  A;\n}\n", t => t with { BaseTypes = new[] { Cs.Type("B"), Cs.Type("D") } }), "class C : B, D\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.type-parameter", () => Type("class C /* x */ : B\n{\n    int  A;\n}\n", t => t with { TypeParameters = new[] { new TypeParameter("T") } }), "class C<T> : B\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.line-comment", () => Type("class C : B // why\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "sealed" }, BaseTypes = new[] { t.BaseTypes[0], Cs.Type("D") } }), "sealed class C : B // why\n    , D\n{\n    int A;\n}\n");
        yield return new("gene27.p4.header.modifier-removed", () => Type("public /* x */ class C\n{\n    int  A;\n}\n", t => t with { Modifiers = Values<string>.Empty }), "class C\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.renamed-and-removed", () => Type("class C /* x */ : B\n{\n    int  A;\n    int  B;\n}\n", t => t with { Name = "D", Members = new[] { t.Members[1] } }), "class D : B\n{\n    /* x */\n\n    int B;\n}\n");
        yield return new("gene27.p4.header.where-added", () => Type("class C<T> /* x */\n{\n    int  A;\n}\n", t => t with { TypeParameters = new[] { t.TypeParameters[0] with { Constraints = new[] { Cs.Constraint("class") } } } }), "class C<T>\n    where T : class\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.directive", () => Type("class C\n#if X\n    : B\n#endif\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C\n#if X\n    : B\n#endif\n{\n    int A;\n}\n");
        yield return new("gene27.p4.header.inactive-branch", () => Type("class C\n#if X\n    : B\n#else\n    : D\n#endif\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C\n#if X\n    : B\n#else\n    : D\n#endif\n{\n    int A;\n}\n");
        // A change among directives could move text into or out of a branch, and a header child's own comment cannot be
        // placed token by token: the type renders whole, as before.
        yield return new("gene27.p4.header.change-in-branch", () => Type("class C\n#if !X\n    : B\n#endif\n{\n    int  A;\n}\n", t => t with { BaseTypes = Values<CsType>.Empty }), "class C\n#if !X\n#endif\n{\n    int A;\n}\n");
        yield return new("gene27.p4.header.child-comment", () => Type("class C<T /* t */> /* x */\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C<T /* t */>\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.part-directive", () => Type("class C : B<\n#if X\n#endif\n    int>\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" } }), "public class C : B<\n#if X\n#endif\n    int>\n{\n    int A;\n}\n");
        yield return new("gene27.p4.header.tab", () => Type("public\t/* x */ class C\n{\n    int  A;\n}\n", t => t with { Modifiers = Values<string>.Empty }), "class C\n{\n    /* x */\n\n    int A;\n}\n");
        yield return new("gene27.p4.header.part-comment-removed", () => Type("class C : B // why\n{\n    int  A;\n}\n", t => t with { BaseTypes = new[] { Cs.Type("B") } }), "class C : B\n{\n    int A;\n}\n");
        yield return new("gene27.p4.header.trivia-removed", () => Type("class C\n#if X\n    : B\n#endif\n{\n    int  A;\n}\n", t => t with { Modifiers = new[] { "public" }, SyntaxTrivia = Values<CsSyntaxTrivia>.Empty }), "public class C\n{\n    int A;\n}\n");

        // Top-level statements: an edit inside a brace-less embedded statement keeps it brace-less, and a dangling `else`
        // is guarded as in member bodies.
        yield return new("gene27.p4.top.if", () => Literal("if (a)  F(1); // t\nG(2);\n"), "if (a)\n{\n    F(7);\n} // t\nG(2);\n");
        yield return new("gene27.p4.top.braced", () => Literal("if (a)  {  F(1);  }\n"), "if (a)\n{\n    F(7);\n}\n");
        yield return new("gene27.p4.top.while", () => Literal("while (a)\n    F(1);\n"), "while (a)\n{\n    F(7);\n}\n");
        yield return new("gene27.p4.top.else", () => Literal("if (a) F(2); else  F(1);\n"), "if (a)\n{\n    F(2);\n}\nelse\n{\n    F(7);\n}\n");
        yield return new("gene27.p4.top.else-if", () => Literal("if (a) F(2); else if (b)  F(1);\n"), "if (a)\n{\n    F(2);\n}\nelse\n{\n    if (b)\n    {\n        F(7);\n    }\n}\n");
        yield return new("gene27.p4.top.braced-else", () => Literal("if (a) F(2); else  { F(1); }\n"), "if (a)\n{\n    F(2);\n}\nelse\n{\n    F(7);\n}\n");
        yield return new("gene27.p4.top.loops", () => Literal("for (;;)  F(1);\nforeach (var v in w)  F(1);\ndo  F(1); while (c);\nusing (r)  F(1);\nlock (r)  F(1);\n", 5), "for (; ; )\n{\n    F(7);\n}\nforeach (var v in w)\n{\n    F(7);\n}\ndo\n{\n    F(7);\n}\nwhile (c);\nusing (r)\n{\n    F(7);\n}\nlock (r)\n{\n    F(7);\n}\n");
        yield return new("gene27.p4.top.then-dangles", () => Call("if (a) x(); else y();\n", 0, Detached("if (b) z();")), "if (a)\n{\n    if (b)\n    {\n        z();\n    }\n}\nelse\n{\n    y();\n}\n");
        yield return new("gene27.p4.top.else-replaced", () => Call("if (a) x(); else y();\n", 1, Detached("if (b) z();")), "if (a)\n{\n    x();\n}\nelse\n{\n    if (b)\n    {\n        z();\n    }\n}\n");
        yield return new("gene27.p4.top.nested-else-removed", () => EditAt("if (a) if (b) z(); else w(); else y();\n", n => n is CsInstruction, 1, n => ((CsInstruction)n) with { Options = ((CsInstruction)n).Options with { Else = null } }), "if (a)\n{\n    if (b)\n    {\n        z();\n    }\n}\nelse\n{\n    y();\n}\n");
        yield return new("gene27.p4.top.comment-before-else", () => Call("if (a) x(); // c\n// lead\nelse y();\n", 0, Detached("if (b) z();")), "if (a)\n{\n    if (b)\n    {\n        z();\n    }\n}\n// lead\nelse\n{\n    y();\n}\n");
        yield return new("gene27.p4.top.two-statements", () => EditAt("while (a) x();\n", n => n is CsTree { Items.Count: 1 }, 0, n => ((CsTree)n) with { Items = new(((CsTree)n).Items.Append(Detached("z();"))) }), "while (a)\n{\n    x();\n    z();\n}\n");
        // An anonymous method's parameters have no source span of their own; its top-level statement still parses typed.
        yield return new("gene27.p4.top.anonymous-method", () => Cs.Parse("F(delegate (int q) { return q; });\n"), "Typed; diagnostics: ; F(delegate(int q)\n{\n    return q;\n});\n");
        yield return new("gene27.p4.top.tree", () => { var t = Cs.ParseTree("if (a)  F(1);\nelse  G(2);\n").Root; var p = t.Descendants().First(n => n.Node is CsExpression { Text: "2" }).Path; return t.Replace(p, ((CsExpression)t.At(p)) with { Text = "3" }); }, "if (a)\n{\n    F(1);\n}\nelse\n{\n    G(3);\n}\n");
    }
}
