using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Rendering of edited parsed documents: renamed tree symbols, chain edits, the obsolete Reformat, list edits with comments,
// and render options. GENE-29 F4 (AD 84939297 v2): every render formats the whole document, so the rows that pinned kept
// source layout (renamed scalars, one reformatted node of each kind) were deleted with the preserving renderer, and the
// rest pin the formatted output.
public static class PreservedScenarios
{
    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    // Replaces the first node matching `match` with `change(node)` and renders through the public edit API.
    static CsDocument Edit(string source, Func<CsNode, bool> match, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = Find(document, match);
        return document.Replace(path, change(document.At(path)));
    }

    static CsDocument Reformat(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Reformat(Find(document, match));
    }

    const string Chain = "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(3);\n    }\n}\n";

    public static IEnumerable<Scenario> All()
    {
        // Renamed symbols of a parsed tree.
        yield return new("scalar.tree-symbol-name", () => { var t = Cs.ParseTree("Run();").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "Run" }).Path; return t.Replace(path, (CsSymbol)t.At(path) with { Name = "Stop" }); }, "Stop();\n");
        yield return new("scalar.tree-symbol-keyword", () => { var t = Cs.ParseTree("Run();").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "Run" }).Path; return t.Replace(path, (CsSymbol)t.At(path) with { Name = "@class" }); }, "@class();\n");
        yield return new("scalar.tree-symbol-unbound", () => { var t = Cs.ParseTree("var x = 1;").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "x" }).Path; var s = (CsSymbol)t.At(path); return t.Replace(path, s with { Name = "y", Options = s.Options with { AsVar = true } }); }, "var y = 1;\n");

        // Edited chains.
        yield return new("chain.root-identifier", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("x")), "class C\n{\n    void M()\n    {\n        x.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.middle-argument", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Literal, Text: "2" }, n => Cs.Literal(5)), "class C\n{\n    void M()\n    {\n        a.B(1).C(5).D(3);\n    }\n}\n");
        yield return new("chain.middle-member", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" }, n => (CsExpression)n with { Text = "Z" }), "class C\n{\n    void M()\n    {\n        a.B(1).Z(2).D(3);\n    }\n}\n");
        yield return new("chain.comment-on-link", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" }, n => n with { Comments = new[] { new CsComment("/* c */", CommentKind.Block) } }), "class C\n{\n    void M()\n    {\n        /* c */ a.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.top-replaced", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 2 && e.Arguments[1].Text == "3", n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0], Cs.Literal(9) } }), "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(9);\n    }\n}\n");
        yield return new("chain.receiver-detached", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "D" }, n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0].Detach() as CsExpression ?? Cs.Id("q") } }), "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.receiver-swapped", () =>
        {
            var document = Src(Chain);
            var d = Find(document, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "D" });
            var c = Find(document, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" });
            var cNode = (CsExpression)document.At(c);
            var dNode = (CsExpression)document.At(d);
            return document.Replace(d, dNode with { Arguments = new[] { (CsExpression)cNode.Arguments[0].Detach() } });
        }, "class C\n{\n    void M()\n    {\n        a.B(1).D(3);\n    }\n}\n");
        yield return new("chain.conditional", () => Edit("class C { object M(object a) => a?.B?.C?.D; }", n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("x")), "class C\n{\n    object M(object a) => x?.B?.C?.D;\n}\n");
        yield return new("chain.long-and-edit", () =>
        {
            string chain = "a" + string.Concat(Enumerable.Range(0, 40).Select(i => ".M" + i + "()"));
            return Edit("class C { void M() { " + chain + "; } }", n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("b"));
        }, "class C\n{\n    void M()\n    {\n        b.M0()\n            .M1()\n            .M2()\n            .M3()\n            .M4()\n            .M5()\n            .M6()\n            .M7()\n            .M8()\n            .M9()\n            .M10()\n            .M11()\n            .M12()\n            .M13()\n            .M14()\n            .M15()\n            .M16()\n            .M17()\n            .M18()\n            .M19()\n            .M20()\n            .M21()\n            .M22()\n            .M23()\n            .M24()\n            .M25()\n            .M26()\n            .M27()\n            .M28()\n            .M29()\n            .M30()\n            .M31()\n            .M32()\n            .M33()\n            .M34()\n            .M35()\n            .M36()\n            .M37()\n            .M38()\n            .M39();\n    }\n}\n");

        // Reformat is obsolete and returns its input unchanged (GENE-29 F4): the content below renders as any render does.
        yield return new("reformat.returns-input", () =>
        {
            var document = Src("class C { int  F; }");
            var path = Find(document, n => n is CsField);
            var tree = Cs.ParseTree("Run();").Root;
            var symbol = tree.Descendants().First().Path;
            return ReferenceEquals(document.Reformat(new RenderOptions { Indent = "\t" }), document) && ReferenceEquals(document.Reformat(path), document) &&
                ReferenceEquals(document.At(path).Reformat(), document.At(path)) && ReferenceEquals(tree.Reformat(), tree) && ReferenceEquals(tree.Reformat(symbol), tree);
        }, "true");
        yield return new("reformat.path-checked", () => Src("class C { }").Reformat(new NodePath(new[] { 3 })), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'path')");
        yield return new("reformat.tree-path-checked", () => Cs.ParseTree("Run();").Root.Reformat(new NodePath(new[] { 3 })), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'path')");
        yield return new("reformat.xml", () => Reformat("/// <summary>a  b</summary>\nclass C { }\n", n => n is CsXmlNode { Name: "summary" }), "/// <summary>a  b</summary>\nclass C\n{\n}\n");
        yield return new("reformat.cref", () => Reformat("/// <see cref=\"M( int )\"/>\nclass C { }\n", n => n is CsCref), "/// <see cref=\"M(int)\" />\nclass C\n{\n}\n");
        yield return new("reformat.trivia", () => Reformat("class C { int M() => (\n#if true\n1\n#endif\n); }", n => n is CsSyntaxTrivia), "class C\n{\n    int M() => (\n#if true\n        1\n#endif\n        );\n}\n");
        yield return new("reformat.statement-in-section", () => Reformat("class C { void M(int a) { switch (a) { case 1: Run( ); break; } } }", n => n is CsSwitchSection), "class C\n{\n    void M(int a)\n    {\n        switch (a)\n        {\n            case 1:\n                Run();\n                break;\n        }\n    }\n}\n");
        yield return new("reformat.global-statement", () => Reformat("System.Console.WriteLine( 1 );\n", n => n is CsGlobalStatement), "System.Console.WriteLine(1);\n");
        yield return new("reformat.document", () => Src("namespace N { class C { int  F; } }").Reformat(), "namespace N\n{\n    class C\n    {\n        int F;\n    }\n}\n");
        yield return new("reformat.document-file-namespace", () => Src("namespace N;\nclass C { int  F; }\n").Reformat(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped, Indent = "\t" }), "namespace N;\n\nclass C\n{\n    int F;\n}\n");

        // List edits with comments.
        yield return new("list.container-comment-added", () => Edit("class C\n{\n    int F;\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Comments = new[] { new CsComment("// tail", CommentKind.Line, CommentPlacement.Container) }, Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    int F;\n    int G;\n    // tail\n}\n");
        yield return new("list.container-comment-kept", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    int F;\n    int G;\n    // tail\n}\n");
        yield return new("list.container-comment-removed", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Comments = Values<CsComment>.Empty, Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    int F;\n    int G;\n}\n");
        yield return new("list.container-comment-changed", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Comments = new(t.Comments.Select(c => c with { Text = "// changed" })), Members = new(t.Members.Append(new CsField(Cs.Int, "G"))) }; }), "class C\n{\n    int F;\n    int G;\n    // changed\n}\n");
        yield return new("list.inline-comment-removed", () => Edit("class C\n{\n    int F; // keep\n    /* gone */ int G;\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Members = new(new CsMember[] { t.Members[1] with { Comments = Values<CsComment>.Empty }, t.Members[0] }) }; }), "class C\n{\n    int G;\n    int F; // keep\n}\n");
        yield return new("list.member-removed-with-comment", () => Edit("class C\n{\n    // about F\n    int F;\n    int G;\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Members = new(new[] { t.Members[1] }) }; }), "class C\n{\n    int G;\n}\n");
        yield return new("list.empty-body-insert", () => Edit("class C { }\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new CsMember[] { new CsField(Cs.Int, "F") } }), "class C\n{\n    int F;\n}\n");
        yield return new("list.empty-multiline-body-insert", () => Edit("class C\n{\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new CsMember[] { new CsField(Cs.Int, "F") } }), "class C\n{\n    int F;\n}\n");
        yield return new("list.document-items", () => Edit("using System;\nclass C { }\n", n => n is CsDocument, n => ((CsDocument)n) with { Items = new(((CsDocument)n).Items.Append(new CsTypeDeclaration(TypeKind.Class, "D"))) }), "using System;\n\nclass C\n{\n}\n\nclass D\n{\n}\n");
        yield return new("list.file-namespace-items", () => Edit("namespace N;\nclass C { }\n", n => n is CsNamespace, n => ((CsNamespace)n) with { Items = new(((CsNamespace)n).Items.Append(new CsTypeDeclaration(TypeKind.Class, "D"))) }), "namespace N;\n\nclass C\n{\n}\n\nclass D\n{\n}\n");
        yield return new("list.same-line-insert", () => Edit("class C { int F; }", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new(new CsMember[] { new CsField(Cs.Int, "E") }.Concat(((CsTypeDeclaration)n).Members)) }), "class C\n{\n    int E;\n    int F;\n}\n");

        // Render options.
        yield return new("options.bad-indent", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Indent = "--" }), "throws ArgumentException: Indent must contain only spaces or tabs. (Parameter 'options')");
        yield return new("options.null-indent", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Indent = null! }), "throws ArgumentException: Indent must contain only spaces or tabs. (Parameter 'options')");
        yield return new("options.unknown-nullable", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Nullable = (NullableMode)9 }), "throws ArgumentException: Unknown nullable mode. (Parameter 'options')");
        yield return new("options.unknown-namespace-style", () => Cs.Document().Namespace("N").Class("C").End().Build().Render(new RenderOptions { NamespaceStyle = (NamespaceStyle)9 }), "throws ArgumentException: Unknown namespace style. (Parameter 'options')");
        yield return new("options.block-namespace", () => Cs.Document().Namespace("N").Using("System").Class("C").End().Build().Render(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped, SortUsings = false }), "using System;\n\nnamespace N\n{\n    class C\n    {\n    }\n}\n");
        yield return new("options.empty-document", () => new CsDocument().Render(), "\n");
        yield return new("options.header-only", () => new CsDocument { Header = "// h" }.Render(), "// h\n");
        yield return new("options.nullable-document", () => new CsDocument { NullableContext = NullableMode.Disable, Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } }.Render(new RenderOptions { Nullable = NullableMode.Enable }), "#nullable enable\n\nclass C\n{\n}\n");
        yield return new("options.namespace-empty-block", () => new CsDocument { Namespace = "N" }.Render(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped }), "namespace N\n{\n}\n");
    }
}
