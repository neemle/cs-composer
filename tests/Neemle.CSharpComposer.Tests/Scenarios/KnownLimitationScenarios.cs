using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-27: regressions for the README "Known limitations". Phase 1 (D3, D5, D7, D8, D9, D10, D13) rows reproduced their
// defect on 44606b7, phase 2 (D1, D2, D4, D6) rows on d605cb6; the expected outcome is the fixed behaviour.
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
        yield return new("gene27.d8.async-lambda", () => Statements("class C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async () => { await x; };\n    }\n}\n"), "Local,Expression\nclass C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async () => {\n    await x;\n};\n    }\n}\n");
        yield return new("gene27.d8.async-anonymous-method", () => Statements("class C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async delegate { await x; };\n    }\n}\n"), "Local,Expression\nclass C\n{\n    void M(Task x)\n    {\n        Func<Task> f = async delegate {\n    await x;\n};\n    }\n}\n");
        yield return new("gene27.d8.async-local-function", () => Statements("class C\n{\n    void M(Task x)\n    {\n        async Task L() { await x; }\n    }\n}\n"), "LocalFunction,Expression\nclass C\n{\n    void M(Task x)\n    {\n        async Task L()\n        {\n            await x;\n        }\n    }\n}\n");
        yield return new("gene27.d8.top-level", () => Cs.ParseTree("await x;").Root.Reformat().Render(), "await x;\n");
        yield return new("gene27.d8.not-async", () => Statements("class C\n{\n    void M()\n    {\n        await x;\n    }\n}\n"), "Local\nclass C\n{\n    void M()\n    {\n        @await x;\n    }\n}\n");
        yield return new("gene27.d8.untouched", () => Src("class C\n{\n    async Task M(Task x)\n    {\n        await  x ;\n    }\n}\n").Render(), "class C\n{\n    async Task M(Task x)\n    {\n        await  x ;\n    }\n}\n");

        // D3: a fresh region that ends in a directive keeps the directive's line end, so `;` never joins `#endif` (since
        // phase 2 the blanked operand is replaced in place, so `;` keeps that operand's own line indentation).
        yield return new("gene27.d3.last-operand-blanked", () => Edit("class C\n{\n    int F = a +\n#if X\n#endif\n        b;\n}\n", n => n is CsExpression { Text: "b" }, _ => Cs.RawExpression("")).Render(NoCheck), "class C\n{\n    int F = a +\n#if X\n#endif\n        ;\n}\n");
        yield return new("gene27.d3.owner-rendered-again", () => EditAt("class C\n{\n    int F = a +\n#if X\n#endif\n        b;\n}\n", n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0], Cs.RawExpression("") }, Comments = new[] { new CsComment("/* c */", CommentKind.Block) } }).Render(NoCheck), "class C\n{\n    int F = /* c */ a +\n#if X\n#endif\n    ;\n}\n");
        yield return new("gene27.d3.hash-in-literal", () => Edit("class C\n{\n    string F = \"#\" + b;\n}\n", n => n is CsExpression { Text: "b" }, _ => Cs.Id("c")).Render(NoCheck), "class C\n{\n    string F = \"#\" + c;\n}\n");

        // D5: trailing comments of a constructed document are rendered, and a trailing line comment ends its line.
        yield return new("gene27.d5.types-document", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// tail") } }, "class C\n{\n} // tail\n");
        yield return new("gene27.d5.builder-document", () => Cs.Document().Class("C").End().Build() with { Comments = new[] { Trailing("/* tail */") } }, "class C\n{\n} /* tail */\n");
        yield return new("gene27.d5.items-document", () => new CsDocument { Items = new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// a"), Trailing("// b") } }, "class C\n{\n} // a\n// b\n");
        yield return new("gene27.d5.directive-first", () => new CsDocument { Items = new CsNode[] { new CsDirective(DirectiveKind.Region), new CsDirective(DirectiveKind.EndRegion), new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Trailing("// tail") } }, "#region\n#endregion\nclass C\n{\n} // tail\n");
        yield return new("gene27.d5.member-line-then-block", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { Trailing("// a"), Trailing("/* b */") } }), "class C\n{\n    int F; // a\n    /* b */\n}\n");
        yield return new("gene27.d5.type-line-with-newline", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Comments = new[] { Trailing("// a\n"), Trailing("/* b */"), Trailing("/* c */") } } } }, "class C\n{\n} // a\n/* b */ /* c */\n");

        // D7: a replaced constraint shows in Render() without Reformat, and only its where clause changes.
        yield return new("gene27.d7.type", () => Constraint("class C<T> where T : class\n{\n    int  F;\n}\n", "class", Cs.Constraint("struct")).Render(), "class C<T> where T : struct\n{\n    int  F;\n}\n");
        yield return new("gene27.d7.method", () => Constraint("class C\n{\n    void M<T>()  where T : class, new() { }\n}\n", "new()", Cs.Constraint(Cs.Type("System.IDisposable"))).Render(), "class C\n{\n    void M<T>()  where T : class, System.IDisposable { }\n}\n");
        yield return new("gene27.d7.second-parameter", () => Constraint("class C<[A] T, U>\n    where T : class\n    where U : new()\n{\n}\n", "new()", Cs.Constraint("struct")).Render(), "class C<[A] T, U>\n    where T : class\n    where U : struct\n{\n}\n");
        yield return new("gene27.d7.delegate", () => Constraint("delegate void D<T>(T x) where T : class;\n", "class", Cs.Constraint("notnull")).Render(), "delegate void D<T>(T x) where T : notnull;\n");
        yield return new("gene27.d7.extension", () => Constraint("static class E\n{\n    extension<T>(T x) where T : class\n    {\n    }\n}\n", "class", Cs.Constraint("struct")).Render(), "static class E\n{\n    extension<T>(T x) where T : struct\n    {\n    }\n}\n");
        yield return new("gene27.d7.local-function", () => Constraint("class C\n{\n    void M()\n    {\n        void L<T>() where T : class { }\n    }\n}\n", "class", Cs.Constraint("struct")).Render(), "class C\n{\n    void M()\n    {\n        void L<T>() where T : struct { }\n    }\n}\n");
        yield return new("gene27.d7.renamed-parameter", () => Edit("class C<T> where T : class\n{\n}\n", n => n is TypeParameter, n => ((TypeParameter)n) with { Name = "U" }).Render(), "class C<U> where U : class\n{\n}\n");
        yield return new("gene27.d7.renamed-unconstrained", () => Edit("class C<T>\n{\n    T  F;\n}\n", n => n is TypeParameter, n => ((TypeParameter)n) with { Name = "U" }).Render(), "class C<U>\n{\n    T  F;\n}\n");
        yield return new("gene27.d7.owner-renamed", () => Edit("class C<T> where T : IComparable< T >\n{\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Name = "D" }).Render(), "class D<T> where T : IComparable< T >\n{\n}\n");
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
        yield return new("gene27.d1.else-statement", () => EditAt(ElseStatement, n => n is CsStatement { Kind: StatementKind.If } s && s.Else != null, 0, n => ((CsStatement)n) with { Else = Cs.If(Cs.Id("e"), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("z"))))) }), "class C\n{\n    void M()\n    {\n        if (c)\n            a();\n        else if (e)\n        {\n            z();\n        }\n    }\n}\n");
        yield return new("gene27.d1.own-line-argument", () => Operand(OwnLineArguments, "a", Cs.Id("y")), OwnLineArguments.Replace("a,", "y,"));
        yield return new("gene27.d1.pattern-directive", () => Src(PatternDirective), PatternDirective);

        // D2: Reformat keeps a line after an embedded directive indented one level past the line its owner starts on.
        yield return new("gene27.d2.reformat-member", () => { var d = Src(OperandDirective); return d.Reformat(Find(d, n => n is CsField)); }, OperandDirective);
        yield return new("gene27.d2.reformat-document", () => Src(OperandDirective).Reformat(), OperandDirective);
        yield return new("gene27.d2.reformat-statement-owner", () => Src(StatementDirective).Reformat(), StatementDirective);
        yield return new("gene27.d2.reformat-tabs", () => Src(OperandDirective).Reformat(new RenderOptions { Indent = "\t" }), OperandDirective.Replace("    ", "\t"));
        yield return new("gene27.d2.reformat-directive-statement", () => { var d = Src(DirectiveStatements); return d.Reformat(Find(d, n => n is CsStatement { Kind: StatementKind.Directive })); }, DirectiveStatements);
        yield return new("gene27.d2.reformat-disabled-text", () => { var d = Src(DirectiveStatements.Replace("#if DEBUG", "#if NEVER")); return d.Reformat(Find(d, n => n is CsStatement { Kind: StatementKind.DisabledText })); }, DirectiveStatements.Replace("#if DEBUG", "#if NEVER"));

        // D6: a newly built node inside a multi-line chain replaces only its own text; the links keep their lines. A link
        // whose own shape changed renders again without the links below it, unless it cannot (then it renders whole).
        yield return new("gene27.d6.head", () => Operand(ChainSource, "source", Cs.Id("y")), ChainSource.Replace("source", "y"));
        yield return new("gene27.d6.argument", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Lambda }, 0, _ => Cs.Id("f")), ChainSource.Replace("Where(x => x)", "Where(f)"));
        yield return new("gene27.d6.last-argument", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Lambda }, 1, _ => Cs.Id("f")), ChainSource.Replace("Select(x => x)", "Select(f)"));
        yield return new("gene27.d6.link-rebuilt", () => EditAt(ChainSource, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 2 && e.Arguments[0].Text == "Where", 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Id("z"))) }), ChainSource.Replace("Where(x => x)", "Where(x => x, z)"));
        yield return new("gene27.d6.block-comment", () => Operand(CommentedChain, "x", Cs.Id("y")), CommentedChain.Replace("Where(x)", "Where(y)"));
        yield return new("gene27.d6.line-comment", () => Operand(CommentedChain, "x", Cs.Id("y"), 1), CommentedChain.Replace("Select(x)", "Select(y)"));
        yield return new("gene27.d6.commented-head", () => Operand(CommentedChain, "source", Cs.Id("y")), CommentedChain.Replace("source /*c*/", "y"));
        yield return new("gene27.d6.commented-head-crlf", () => Operand(CommentedChain.Replace("\n", "\r\n"), "source", Cs.Id("y")), CommentedChain.Replace("source /*c*/", "y").Replace("\n", "\r\n"));
        yield return new("gene27.d6.binary-link", () => EditAt(BinaryChain, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Subtract }), BinaryChain.Replace("+ c", "- c"));
        yield return new("gene27.d6.receiver-parenthesized", () => EditAt(BinaryChain, n => n is CsExpression { Kind: ExpressionKind.Binary }, 0, n => ((CsExpression)n) with { Operator = CsOperator.Multiply }), "class C\n{\n    int F = (a + b) * c;\n}\n");
        yield return new("gene27.d6.receiver-moved", () => { var d = Src(ChainSource); var head = Find(d, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "Where" }); var top = Find(d, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "ToList" }); return d.Replace(top, ((CsExpression)d.At(top)) with { Arguments = new[] { (CsExpression)d.At(head) } }); }, "class C\n{\n    void M()\n    {\n        var r = source.Where.ToList();\n    }\n}\n");
        yield return new("gene27.d6.link-owns-directive", () => EditAt(DirectiveChain, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 3, 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Literal(3))) }), "class C\n{\n    void M()\n    {\n        a.B(1,\n#if X\n#endif\n            2, 3)\n            .C();\n    }\n}\n");

        // D4: container comments keep their place and blank lines after member Remove, Insert and reorder.
        yield return new("gene27.d4.remove-first", () => Members(TypeComments, 1, 2), "class C\n{\n    // about B\n\n    int B;\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.remove-middle", () => Members(TypeComments, 0, 2), "class C\n{\n    int A;\n\n    // about B\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.remove-last", () => Members(TypeComments, 0, 1), "class C\n{\n    int A;\n\n    // about B\n\n    int B;\n\n    // container tail\n}\n");
        yield return new("gene27.d4.remove-two", () => Members(TypeComments, 0), "class C\n{\n    int A;\n\n    // about B\n\n    // container tail\n}\n");
        yield return new("gene27.d4.remove-first-two", () => Members(TypeComments, 2), "class C\n{\n    // about B\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.multi-line-comment-added", () => { var d = Src(TypeComments); var p = Find(d, n => n is CsTypeDeclaration); var t = (CsTypeDeclaration)d.At(p); return d.Replace(p, t with { Members = new[] { t.Members[0], t.Members[2] }, Comments = new(t.Comments.Append(new CsComment("/* a\n   b */", CommentKind.Block, CommentPlacement.Container))) }); }, "class C\n{\n    /* a\n   b */\n    int A;\n\n    // about B\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.comment-moved-in", () => { var d = Src(TwoTypes); var a = Find(d, n => n is CsTypeDeclaration { Name: "A" }); var b = Find(d, n => n is CsTypeDeclaration { Name: "B" }); var source = (CsTypeDeclaration)d.At(a); var t = (CsTypeDeclaration)d.At(b); d = d.Replace(a, source with { Comments = Values<CsComment>.Empty }); return d.Replace(Find(d, n => n is CsTypeDeclaration { Name: "B" }), t with { Members = new CsMember[] { t.Members[0], new CsField(Cs.Int, "W") }, Comments = new[] { source.Comments[0] } }); }, "class A\n{\n    int X;\n\n    int Y;\n}\nclass B\n{\n    // moved\n    int Z;\n    int W;\n}\n");
        yield return new("gene27.d4.reorder", () => Members(TypeComments, 2, 0, 1), "class C\n{\n    // container tail\n\n    int D;\n    int A;\n\n    // about B\n\n    int B;\n}\n");
        yield return new("gene27.d4.insert", () => Members(TypeComments, 0, 1, -1, 2), "class C\n{\n    int A;\n\n    // about B\n\n    int B;\n    int Z;\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.replace-first", () => Members(TypeComments, -1, 1, 2), "class C\n{\n    int Z;\n\n    // about B\n\n    int B;\n\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.block-remove-first", () => Members(BlockComments, 1), "class C\n{\n    void M()\n    {\n        // middle\n\n        b();\n        // tail\n    }\n}\n");
        yield return new("gene27.d4.block-remove-last", () => Members(BlockComments, 0), "class C\n{\n    void M()\n    {\n        a();\n\n        // middle\n\n        // tail\n    }\n}\n");
        yield return new("gene27.d4.namespace-remove-first", () => Members(NamespaceComments, 1), "namespace N\n{\n    // between\n\n    class B { }\n}\n");
        yield return new("gene27.d4.namespace-remove-last", () => Members(NamespaceComments, 0), "namespace N\n{\n    class A { }\n\n    // between\n}\n");
        // Reformat keeps its own blank lines; a comment stays before the item that followed it in source.
        yield return new("gene27.d4.reformat-after-remove", () => { var d = Members(TypeComments, 1, 2); return d.Reformat(Find(d, n => n is CsTypeDeclaration)); }, "class C\n{\n    // about B\n    int B;\n    // container tail\n\n    int D;\n}\n");
        yield return new("gene27.d4.reformat-after-reorder", () => { var d = Members(TypeComments, 2, 0, 1); return d.Reformat(Find(d, n => n is CsTypeDeclaration)); }, "class C\n{\n    // container tail\n    int D;\n\n    int A;\n    // about B\n\n    int B;\n}\n");
        yield return new("gene27.d4.reformat-after-remove-last", () => { var d = Members(TypeComments, 0, 1); return d.Reformat(Find(d, n => n is CsTypeDeclaration)); }, "class C\n{\n    int A;\n    // about B\n\n    int B;\n    // container tail\n}\n");
        yield return new("gene27.d4.reformat-after-insert", () => { var d = Members(TypeComments, -1, 0, 1, 2); return d.Reformat(Find(d, n => n is CsTypeDeclaration)); }, "class C\n{\n    int Z;\n\n    int A;\n    // about B\n\n    int B;\n    // container tail\n\n    int D;\n}\n");
    }
}
