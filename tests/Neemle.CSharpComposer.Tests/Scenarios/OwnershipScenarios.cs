using System.Reflection;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-29 F5 (AD 84939297 v2 B2-B4): the comment ownership pass, the formatter's placement of each owner's comments and
// the meaning guard's comparison of comment owners. Every parsed row is also a fixed point (Outcome.Settled); each row
// failed before F5 or covers a new branch.
public static class OwnershipScenarios
{
    static CsComment Block(string text, CommentPlacement placement = CommentPlacement.Leading) => new("/* " + text + " */", CommentKind.Block, placement);

    static CsComment Line(string text, CommentPlacement placement = CommentPlacement.Leading) => new("// " + text, CommentKind.Line, placement);

    // Every comment of a tree with its owner's kind and its placement, in a depth-first walk.
    static string Owners(CsNode root)
    {
        var found = new List<string>();
        var stack = new Stack<CsNode>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            string kind = node is CsExpression e ? "CsExpression." + e.Kind : node is CsStatement s ? "CsStatement." + s.Kind : node.GetType().Name;
            foreach (var comment in node.Comments) found.Add(kind + " " + comment.Placement + " " + comment.Text.Replace("\n", "\\n"));
            foreach (var child in node.Children.Reverse()) if (child is not CsComment) stack.Push(child);
        }
        return string.Join(" | ", found);
    }

    static ParseResult<CsDocument> P(string source) => Cs.Parse(source);

    static Func<object?> Faulted(Func<string, string> change, Func<string> render) => () =>
    {
        var inject = typeof(Cs).Assembly.GetType("Neemle.CSharpComposer.Rendering.MeaningGuard")!.GetMethod("Inject", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = inject.Invoke(null, [change]);
        try { return render(); }
        finally { inject.Invoke(null, [previous]); }
    };

    static CsExpression Negate(CsExpression operand) => Cs.Unary(CsOperator.Negate, operand);

    public static IEnumerable<Scenario> All()
    {
        // B2 rule 1: a comment starting on the line where a trailing comment ends joins its owner.
        yield return new("gene29.f5.own.trailing-joins", () => Owners(Src("class C\n{\n    int F; /* a\n       b */ /* c */\n    int G;\n}\n")), "CsField Trailing /* a\\n   b */ | CsField Trailing /* c */");
        yield return new("gene29.f5.own.trailing-joins-render", () => Src("class C\n{\n    int F; /* a\n       b */ /* c */\n    int G;\n}\n"), "class C\n{\n    int F; /* a\n       b */ /* c */\n    int G;\n}\n");
        // B2 rule 3: a comment before a closing parenthesis or bracket is its construct's own, written inside it.
        yield return new("gene29.f5.own.before-close-paren", () => Src("class C\n{\n    void M()\n    {\n        F(a\n            /* c */);\n    }\n}\n"), "class C\n{\n    void M()\n    {\n        F(/* c */ a);\n    }\n}\n");
        yield return new("gene29.f5.own.before-close-bracket", () => Src("class C\n{\n    int M(int[] a) => a[0\n        /* c */];\n}\n"), "class C\n{\n    int M(int[] a) => a[/* c */ 0];\n}\n");
        // B2 rule 4: no node ends at the token before and no child follows: the statement's or member's own comment.
        yield return new("gene29.f5.own.return-block", () => Src("class C\n{\n    void M()\n    {\n        return /* c */;\n    }\n}\n"), "class C\n{\n    void M()\n    {\n        return /* c */;\n    }\n}\n");
        yield return new("gene29.f5.own.return-line", () => Src("class C\n{\n    void M()\n    {\n        return // c\n        ;\n    }\n}\n"), "class C\n{\n    void M()\n    {\n        return // c\n            ;\n    }\n}\n");
        yield return new("gene29.f5.own.return-owners", () => Owners(Src("class C\n{\n    void M()\n    {\n        return /* c */;\n    }\n}\n")), "CsStatement.Return Container /* c */");
        yield return new("gene29.f5.own.using-line", () => Src("using // c\n    System;\n\nclass C\n{\n}\n"), "using System // c\n    ;\n\nclass C\n{\n}\n");
        // An expression body spans from its `=>`: a comment before it is the body's.
        yield return new("gene29.f5.own.expression-body-leading", () => Src("class C\n{\n    int M() /* c */ => 1;\n}\n"), "class C\n{\n    int M() /* c */ => 1;\n}\n");
        // AD B3: only a comment run followed by a blank line is the file preamble.
        yield return new("gene29.f5.own.preamble-two", () => Owners(Src("// a\n// b\n\nclass C\n{\n}\n")), "CsDocument Leading // a | CsDocument Leading // b");
        yield return new("gene29.f5.own.preamble-none", () => Owners(Src("// a\n\n// b\nclass C\n{\n}\n")), "CsDocument Leading // a | CsTypeDeclaration Leading // b");
        // A comment before a directive in a statement list is the directive's.
        yield return new("gene29.f5.own.before-body-directive", () => Owners(Src("class C\n{\n    void M()\n    {\n        // c\n#if X\n        F();\n#endif\n    }\n}\n")), "CsStatement.Directive Leading // c");
        // AD B4: a comment over several lines keeps its later lines' indentation past its own column, tabs too.
        yield return new("gene29.f5.own.tab-dedent", () => Owners(Src("class C\n{\n\t/* a\n\t   b */\n\tint F;\n}\n")), "CsField Leading /* a\\n   b */");
        yield return new("gene29.f5.own.empty-comment-line", () => Src("class C\n{\n    /* a\n\n       b */\n    int F;\n}\n"), "class C\n{\n    /* a\n\n       b */\n    int F;\n}\n");
        // AD B3: comments in an interpolation hole belong to the hole's expressions.
        yield return new("gene29.f5.own.hole", () => Owners(Src("class C\n{\n    string M(int x) => $\"{/* a */ x /* b */, /* w */ 5:x2}\";\n}\n")), "CsExpression.Literal Leading /* w */ | CsExpression.Identifier Leading /* a */ | CsExpression.Identifier Trailing /* b */");
        yield return new("gene29.f5.own.hole-lambda", () => Src("class C\n{\n    string M(int x) => $\"{F(y => y) /* c */}\";\n}\n"), "class C\n{\n    string M(int x) => $\"{F(y => y) /* c */}\";\n}\n");
        // A fragment's comments before and after its root are the root's.
        yield return new("gene29.f5.own.fragment", () => Owners(new CsRaw("/* a */ x + y /* b */").Parse(new ParseContext(ParseKind.Expression)).Root), "CsExpression.Binary Leading /* a */ | CsExpression.Binary Trailing /* b */");
        yield return new("gene29.f5.own.tree-else", () => Cs.ParseTree("if (a) x();\n// lead\nelse y();\n"), "Typed; diagnostics: ; if (a)\n{\n    x();\n}\n// lead\nelse\n{\n    y();\n}\n");
        yield return new("gene29.f5.own.tree-directive", () => Cs.ParseTree("x();\n// c\n#if X\ny();\n#endif\n"), "Typed; diagnostics: ; x();\n// c\n#if X\ny();\n#endif\n");
        // Comments inside raw source are part of its text.
        yield return new("gene29.f5.own.raw-item", () => P("int x = /* c */ ;\n"), "Partial; diagnostics: PARSE001 Error @0+17 1:1 Expected ';'.; int x = /* c */ ;\n");
        yield return new("gene29.f5.own.raw-member", () => P("class C\n{\n    int F = /* c */ ;\n}\n"), "Partial; diagnostics: PARSE002 Warning @14+17 3:5 Expression expected.; class C\n{\n    int F = /* c */ ;\n}\n");
        yield return new("gene29.f5.own.raw-statement", () => P("class C\n{\n    void M()\n    {\n        int x = /* c */ ;\n    }\n}\n"), "Partial; diagnostics: PARSE002 Warning @37+17 5:9 Expected ';'.; class C\n{\n    void M()\n    {\n        int x = /* c */ ;\n    }\n}\n");
        // A switch statement's and an extension's own comments end their lists.
        yield return new("gene29.f5.own.switch-container", () => Src("class C\n{\n    void M(int a)\n    {\n        switch (a)\n        {\n            case 1:\n                break;\n            // c\n        }\n    }\n}\n"), "class C\n{\n    void M(int a)\n    {\n        switch (a)\n        {\n            case 1:\n                break;\n            // c\n        }\n    }\n}\n");
        yield return new("gene29.f5.own.extension-container", () => Src("static class E\n{\n    extension(int i)\n    {\n        // c\n    }\n}\n"), "static class E\n{\n    extension(int i)\n    {\n        // c\n    }\n}\n");

        // Raw source that starts with a directive continues the line before it, unless that line cannot go on.
        yield return new("gene29.f5.raw.directive-member", () => P("class C { #region x\n}\n"), "Partial; diagnostics: PARSE002 Warning @10+10 1:11 A directive must begin its line.; class C\n{ #region x\n}\n");
        yield return new("gene29.f5.raw.directive-statement", () => P("class C { void M() { #region x\n} }\n"), "Partial; diagnostics: PARSE002 Warning @21+10 1:22 A directive must begin its line.; class C\n{\n    void M()\n    { #region x\n    }\n}\n");
        yield return new("gene29.f5.raw.directive-first-item", () => new CsDocument { Items = new CsNode[] { new CsRaw("#region x\n") } }, "#region x\n");
        yield return new("gene29.f5.raw.directive-after-directive", () => InClass(new CsDirective(DirectiveKind.Region), new CsRawMember("#region x\n"), new CsDirective(DirectiveKind.EndRegion)), "invalid: EMIT001 Rendered output does not read back as the model: CsTypeDeclaration reads back as CsRaw.");
        yield return new("gene29.f5.raw.directive-after-line-comment", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { Line("t", CommentPlacement.Trailing) } }, new CsRawMember("#region x\n")), "invalid: EMIT001 Rendered output does not read back as the model: CsTypeDeclaration reads back as CsRaw.");
        yield return new("gene29.f5.raw.directive-in-lambda", () => P("class C { object F = () => { #region x\n}; }\n"), "Partial; diagnostics: PARSE002 Warning @29+10 1:30 A directive must begin its line.; invalid: EMIT001 Rendered output does not read back as the model: CsTypeDeclaration reads back as CsRaw.");

        // A built statement's own comment in a block lambda goes before it.
        yield return new("gene29.f5.dangling.nested", () => Init(Cs.Lambda(Cs.Body(Cs.Return(Cs.Id("x")) with { Comments = new[] { Block("c", CommentPlacement.Container) } }))), "class C\n{\n    object F = () =>\n    {\n        /* c */\n        return x;\n    };\n}\n");
        // An else-if with comments of its own after `else` is braced.
        yield return new("gene29.f5.else-if.trailing", () => InMethod(Cs.If(Cs.Id("a"), Cs.Body(), Cs.If(Cs.Id("b"), Cs.Body()) with { Comments = new[] { Line("b", CommentPlacement.Trailing) } })), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n        }\n        else\n        {\n            if (b)\n            {\n            } // b\n        }\n    }\n}\n");

        // The meaning guard: a container comment, written inside its owner, may read back on a node inside it.
        yield return new("gene29.f5.guard.container-reads-inside", () => Init(Negate(Negate(Cs.Id("a")) with { Comments = new[] { Block("c") } }) with { Comments = new[] { Block("c", CommentPlacement.Container) } }), "class C\n{\n    object F = -/* c */ /* c */ -a;\n}\n");
        yield return new("gene29.f5.guard.two-containers", () => Init(Negate(Negate(Cs.Id("a"))) with { Comments = new[] { Block("c", CommentPlacement.Container), Block("c", CommentPlacement.Container) } }), "class C\n{\n    object F = -/* c */ /* c */ -a;\n}\n");
        yield return new("gene29.f5.guard.container-read-outside", Faulted(t => t.Replace("-/* c */ -a", "- -a /* c */", StringComparison.Ordinal),
            () => Init(Negate(Negate(Cs.Id("a"))) with { Comments = new[] { Block("c", CommentPlacement.Container) } }).Render()), "throws GenValidationException: EMIT001 CsExpression: Rendered output does not read back as the model: comment /* c */ reads back on another owner.");
        yield return new("gene29.f5.guard.same-leading-twice", () => Src("class C\n{\n    int F = /* c */ /* c */ 1;\n}\n"), "class C\n{\n    int F = /* c */ /* c */ 1;\n}\n");

        // Rule 4: an own-line comment before a token that neither starts a node nor closes one trails the node before it.
        yield return new("gene29.f5.own.fallback-trailing", () => Src("class C\n{\n    int x = 1\n    // c\n    ;\n}\n"), "class C\n{\n    int x = 1 // c\n        ;\n}\n");
        // AD B4: the later lines of a comment after code keep their place relative to the line it starts on.
        yield return new("gene29.f5.own.trailing-block-lines", () => Src("class C\n{\n    void M()\n    {\n        x = 1; /* a\n                  b */\n    }\n}\n"), "class C\n{\n    void M()\n    {\n        x = 1; /* a\n                  b */\n    }\n}\n");
        // A comment over several lines, an empty one among them, in text that directives are inserted into.
        yield return new("gene29.f5.own.lines-among-directives", () => Src("class C\n{\n    void M()\n    {\n        F(1,\n#if X\n#endif\n            /* a\n\n               b */\n            2);\n    }\n}\n"), "class C\n{\n    void M()\n    {\n        F(\n            1,\n            /* a\n\n               b */\n#if X\n#endif\n            2);\n    }\n}\n");
        // A built container comment of an expression body is written before the expression; of a block body, at its end
        // (its statement's before the `;`, which reads back trailing the expression inside it).
        yield return new("gene29.f5.dangling.expression-body", () => Init(Cs.Lambda(CsBody.FromExpression(Cs.Id("x")) with { Comments = new[] { Block("c", CommentPlacement.Container) } })), "class C\n{\n    object F = () => /* c */ x;\n}\n");
        yield return new("gene29.f5.dangling.block-body", () => Init(Cs.Lambda(Cs.Body(Cs.Return(Cs.Id("x")) with { Comments = new[] { Block("d", CommentPlacement.Container) } }) with { Comments = new[] { Block("c", CommentPlacement.Container) } })), "class C\n{\n    object F = () =>\n    {\n        /* d */\n        return x;\n        /* c */\n    };\n}\n");
        // The meaning guard: a container comment read back before its owner, and a using's comment changed.
        yield return new("gene29.f5.guard.container-read-before", Faulted(t => t.Replace("-/* c */ -a", "- -a", StringComparison.Ordinal).Replace("object F", "/* c */ object F", StringComparison.Ordinal),
            () => Init(Negate(Negate(Cs.Id("a"))) with { Comments = new[] { Block("c", CommentPlacement.Container) } }).Render()), "throws GenValidationException: EMIT001 CsField: Rendered output does not read back as the model: comment /* c */ reads back on another owner.");
        yield return new("gene29.f5.guard.using-comment-changed", Faulted(t => t.Replace("// c", "// d", StringComparison.Ordinal), () => Src("// c\nusing System;\n\nclass C\n{\n}\n").Render()), "throws GenValidationException: EMIT001 CsUsing: Rendered output does not read back as the model: comment // d reads back on another owner.");
        // Directives and inactive text a model places after a file-scoped namespace (a parse puts them inside it).
        yield return new("gene29.f5.guard.items-after-file-scoped-namespace", () => Src("namespace N;\n\nclass C\n{\n}\n\n#if X\nclass D { }\n#endif\n") is var d && d.Items[0] is CsNamespace ns
            ? d with { Items = new(new CsNode[] { ns with { Items = new(ns.Items.Take(1).ToArray()) } }.Concat(ns.Items.Skip(1)).ToArray()) } : null, "namespace N;\n\nclass C\n{\n}\n\n#if X\nclass D { }\n#endif\n");
        // A type parameter read back with its constraint reshaped is compared by name.
        yield return new("gene29.f5.guard.type-parameter", () => Method(new CsMethod(Cs.Void, "M") { TypeParameters = new[] { new TypeParameter("T"), new TypeParameter("U") { Constraints = new[] { Cs.Constraint(Cs.TypeParameter("T")) } } }, Body = Cs.Body() }), "class C\n{\n    void M<T, U>()\n        where U : T\n    {\n    }\n}\n");

        // Detached copies drop their source spans, except a directive's and inactive text's.
        yield return new("gene29.f5.detach.directives", () => (CsDocument)Src("#region R\nclass C\n{\n}\n#endregion\n#if X\nclass D { }\n#endif\n").Detach(), "#region R\n\nclass C\n{\n}\n\n#endregion\n#if X\nclass D { }\n#endif\n");
        // FORMAT001 positions: an opaque node of the document's own parse, and others.
        yield return new("gene29.f5.diagnostics.own-raw", () => P("class C\n{\n    int F = ;\n}\n").Root.EmissionDiagnostics, "diagnostics: FORMAT001 Warning @14+9 3:5 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
        yield return new("gene29.f5.diagnostics.built-raw", () => Src("class C\n{\n}\n") is var d ? (d with { Items = new(d.Items.Append(new CsRaw("x"))) }).EmissionDiagnostics : null, "diagnostics: FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
        yield return new("gene29.f5.diagnostics.foreign-raw", () => Src("class C\n{\n}\n") is var d ? (d with { Items = new(d.Items.Append(P("\n\nint x = ;\n").Root.Items[0])) }).EmissionDiagnostics : null, "diagnostics: FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
    }
}
