using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-27 phase 1: regressions for the README "Known limitations" fixed in this phase (D3, D5, D7, D8, D9, D10, D13).
// Each row reproduced its defect on 44606b7; the expected outcome is the fixed behaviour.
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

        // D3: a fresh region that ends in a directive keeps the directive's line end, so `;` never joins `#endif`.
        yield return new("gene27.d3.last-operand-blanked", () => Edit("class C\n{\n    int F = a +\n#if X\n#endif\n        b;\n}\n", n => n is CsExpression { Text: "b" }, _ => Cs.RawExpression("")).Render(NoCheck), "class C\n{\n    int F = a +\n#if X\n#endif\n    ;\n}\n");
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
    }
}
