using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-29 F6 (AD 84939297 v2): the residuals F5 left. Every row here that renders is also a fixed point (Outcome.Settled),
// and each row failed on 69ae8a7 or covers a new branch:
// - built comments on a node other than their canonical owner (AD B2) move, in a checked render, to the owner the parser
//   gives them in the output, so the render is what the render of its re-parse is;
// - a positional pattern of one unnamed subpattern needs a type, a property clause or a designation, and the parser reads
//   `(p) { }` and `(p) d` as positional patterns;
// - the later lines of a comment that follows a directive inserted into its line start at that line's indentation;
// - an attribute argument spans its name, and attribute argument lists break like other argument lists.
public static class FinishScenarios
{
    static CsComment Block(string text, CommentPlacement placement = CommentPlacement.Leading) => new("/* " + text + " */", CommentKind.Block, placement);

    static CsComment Line(string text, CommentPlacement placement = CommentPlacement.Leading) => new("// " + text, CommentKind.Line, placement);

    static CsExpression Is(CsPattern pattern) => new(ExpressionKind.Is, "", new[] { Cs.Id("o") }) { Pattern = pattern };

    static CsPattern Positional(params CsPattern[] patterns) => new(PatternKind.Recursive) { HasPositional = true, Patterns = patterns };

    static CsDocument RemoveFirst(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Remove(document.Descendants().First(n => match(n.Node)).Path);
    }

    public static IEnumerable<Scenario> All()
    {
        // 1. Ownership normalised in the checked render (AD B2): the comment moves to its canonical owner, in the order the
        // output holds it, with the owner's own comments.
        yield return new("gene29.f6.owner.statement-and-own-comments", () => InMethod(Cs.Statement(Cs.Call(Cs.Id("F") with { Comments = new[] { Block("c") } })) with { Comments = new[] { Line("s"), Line("t", CommentPlacement.Trailing) } }),
            "class C\n{\n    void M()\n    {\n        // s\n        /* c */\n        F(); // t\n    }\n}\n");
        yield return new("gene29.f6.owner.two-comments", () => InMethod(Cs.Statement(Cs.Call(Cs.Id("F") with { Comments = new[] { Block("a"), Block("b") } }))),
            "class C\n{\n    void M()\n    {\n        /* a */\n        /* b */\n        F();\n    }\n}\n");
        // A statement compared in a canonical form (a group of one declarator as a local) keeps its own comments.
        yield return new("gene29.f6.owner.canonical-statement", () => RemoveFirst("class C\n{\n    void M()\n    {\n        // c\n        int low = 0, high = 1; // t\n    }\n}\n", n => n is CsVariableDeclarator { Name: "low" }),
            "class C\n{\n    void M()\n    {\n        // c\n        int high = 1; // t\n    }\n}\n");
        yield return new("gene29.f6.owner.tree", () => Cs.Tree(Cs.Statement(Cs.Call(Cs.Id("F") with { Comments = new[] { Block("c") } }))), "/* c */\nF();\n");
        // A container comment over several lines on a name is written before it and reads back as its leading comment.
        yield return new("gene29.f6.owner.multi-line-container", () => Value(Cs.Id("y") with { Comments = new[] { Block("a\nb", CommentPlacement.Container) } }),
            "class C\n{\n    void M()\n    {\n        object x = /* a\n        b */ y;\n    }\n}\n");
        // Unchecked, nothing is read back: the comment is written at the node that holds it.
        yield return new("gene29.f6.owner.unchecked", () => InMethod(Cs.Statement(Cs.Call(Cs.Id("F") with { Comments = new[] { Block("c") } }))).Render(new RenderOptions { Validate = false }),
            "class C\n{\n    void M()\n    {\n        /* c */ F();\n    }\n}\n");
        // A node the model holds in two places is written, compared and normalised in each place on its own.
        yield return new("gene29.f6.owner.reused-node", () => Cs.Call(Cs.Id("F") with { Comments = new[] { Block("c") } }) is var call ? InMethod(Cs.Statement(call), Cs.Local(Cs.Type("object"), "x", call)) : null,
            "class C\n{\n    void M()\n    {\n        /* c */\n        F();\n        object x = /* c */ F();\n    }\n}\n");
        // A document's trailing and end-of-file comments with no item read back as end-of-file comments.
        yield return new("gene29.f6.owner.comment-only-document", () => new CsDocument { Comments = new[] { Line("t", CommentPlacement.Trailing), Line("c", CommentPlacement.Container) } }, "// t\n// c\n");
        // A header the render writes reads back as an end-of-file comment of a document with no item; the model has no node
        // to move it from.
        yield return new("gene29.f6.owner.header-only", () => new CsDocument { Header = "/* h */" }, "/* h */\n");
        // End-of-file comments of a document whose items go into a file-scoped namespace read back as the namespace's,
        // which the render adds: they stay the document's, and both write one blank line before them.
        yield return new("gene29.f6.owner.namespace-end-of-file", () => new CsDocument { Namespace = "N", Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") }, Comments = new[] { Line("end", CommentPlacement.Container) } },
            "namespace N;\n\nclass C\n{\n}\n\n// end\n");
        yield return new("gene29.f6.owner.file-scoped-end-of-file", () => Src("namespace N;\n\nclass C\n{\n}\n// end\n"), "namespace N;\n\nclass C\n{\n}\n\n// end\n");

        // 2. A positional pattern of one unnamed subpattern (the corpus remove variant: MeaningGuard.cs).
        yield return new("gene29.f6.pattern.remove-to-one-position", () => RemoveFirst("class C\n{\n    bool M((object, object) t) => t is (int a, int b);\n}\n", n => n is CsPattern { Kind: PatternKind.Declaration }),
            "throws GenValidationException: GEN076 Types[0].Members[0].Body.Expression.Pattern: Positional pattern with one unnamed subpattern needs a type, a property clause or a designation.");
        yield return new("gene29.f6.pattern.one-position", () => Value(Is(Positional(Cs.ConstantPattern(Cs.Literal(1))))),
            "invalid: GEN076 Positional pattern with one unnamed subpattern needs a type, a property clause or a designation.");
        yield return new("gene29.f6.pattern.one-named-position", () => Value(Is(Positional(new CsPattern(PatternKind.Property) { Name = "a", Patterns = new[] { Cs.ConstantPattern(Cs.Literal(1)) } }))),
            "class C\n{\n    void M()\n    {\n        object x = o is (a: 1);\n    }\n}\n");
        yield return new("gene29.f6.pattern.one-null-position", () => Value(Is(Positional(new CsPattern[] { null! }))),
            "invalid: GEN002 Required node or collection is null.");
        yield return new("gene29.f6.pattern.null-positions", () => Value(Is(new CsPattern(PatternKind.Recursive) { HasPositional = true, Patterns = null! })),
            "invalid: GEN002 Required node or collection is null.");
        yield return new("gene29.f6.pattern.one-typed-position", () => Value(Is(Positional(Cs.ConstantPattern(Cs.Literal(1))) with { Type = Cs.Type("T") })),
            "class C\n{\n    void M()\n    {\n        object x = o is T(1);\n    }\n}\n");
        yield return new("gene29.f6.pattern.one-position-designated", () => Value(Is(Positional(Cs.ConstantPattern(Cs.Literal(1))) with { Name = "d" })),
            "class C\n{\n    void M()\n    {\n        object x = o is (1) d;\n    }\n}\n");
        yield return new("gene29.f6.pattern.one-position-properties", () => Value(Is(Positional(Cs.ConstantPattern(Cs.Literal(1))) with { HasProperties = true })),
            "class C\n{\n    void M()\n    {\n        object x = o is (1) { };\n    }\n}\n");
        yield return new("gene29.f6.pattern.no-position", () => Value(Is(Positional())), "class C\n{\n    void M()\n    {\n        object x = o is ();\n    }\n}\n");
        yield return new("gene29.f6.pattern.parsed-no-position", () => Src("class C\n{\n    bool A(object o) => o is () or () { } && o is () d;\n}\n"), "class C\n{\n    bool A(object o) => o is () or () { } && o is () d;\n}\n");
        yield return new("gene29.f6.pattern.parsed-tails", () => Src("class C\n{\n    bool A(object o) => o is (1) { };\n    bool B(object o) => o is (1) d;\n    bool D(object o) => o is (int a) { };\n    bool E(object o) => o is (int a) e;\n    int F(object o) => o switch { (1) when true => 1, (int b) and > 2 => b, _ => 0 };\n}\n"),
            "class C\n{\n    bool A(object o) => o is (1) { };\n\n    bool B(object o) => o is (1) d;\n\n    bool D(object o) => o is (int a) { };\n\n    bool E(object o) => o is (int a) e;\n\n    int F(object o) => o switch { (1) when true => 1, (int b) and > 2 => b, _ => 0 };\n}\n");
        yield return new("gene29.f6.pattern.empty-property-clauses", () => Src("class C\n{\n    bool A(object o) => o is { } x && o is T { } && o is (1, 2) { };\n}\n"),
            "class C\n{\n    bool A(object o) => o is { } x && o is T { } && o is (1, 2) { };\n}\n");
        yield return new("gene29.f6.pattern.property-clause-comment", () => Value(Is(new CsPattern(PatternKind.Recursive) { HasProperties = true, Comments = new[] { Block("c", CommentPlacement.Container) } })),
            "class C\n{\n    void M()\n    {\n        object x = o is { /* c */ };\n    }\n}\n");

        // 3. A comment over several lines after code on a line a directive is inserted into: its later lines keep their
        // place relative to the line it starts on, in a statement, in a field and in an argument list.
        yield return new("gene29.f6.margin.statement", () => Src("class C\n{\n    void M()\n    {\n        x = a +\n#if X\n#endif\n            b /* c\n                 d */;\n    }\n}\n"),
            "class C\n{\n    void M()\n    {\n        x = a +\n#if X\n#endif\n            b /* c\n                 d */;\n    }\n}\n");
        yield return new("gene29.f6.margin.field", () => Src("class C\n{\n    int F = a\n#if X\n#endif\n        + b; /* c\n                d */\n}\n"),
            "class C\n{\n    int F = a\n#if X\n#endif\n        + b; /* c\n                d */\n}\n");
        yield return new("gene29.f6.margin.arguments", () => Src("class C\n{\n    void M()\n    {\n        F(a,\n#if X\n#endif\n          b, /* c\n                d */\n          e);\n    }\n}\n"),
            "class C\n{\n    void M()\n    {\n        F(a,\n#if X\n#endif\n            b, /* c\n                  d */ e);\n    }\n}\n");

        // 4. Attribute arguments: a comment before a named argument is the argument's, and a long list breaks.
        yield return new("gene29.f6.attribute.named-argument-comment", () => Src("[A(N = 1,\n  // c\n  c: 2)]\nclass C\n{\n}\n"), "[A(\n    N = 1,\n    // c\n    c: 2)]\nclass C\n{\n}\n");
        yield return new("gene29.f6.attribute.long-arguments", () => Src("[Obsolete(\"" + new string('x', 110) + "\", false)]\nclass C\n{\n}\n"),
            "[Obsolete(\n    \"" + new string('x', 110) + "\",\n    false)]\nclass C\n{\n}\n");
        // A document header that starts with a directive starts the output (there is no line before it to continue), and
        // reads back as the directive item it is, with its comments.
        yield return new("gene29.f6.raw.directive-header", () => new CsDocument { Header = "#pragma warning disable CS1591\n", Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } },
            "#pragma warning disable CS1591\n\nclass C\n{\n}\n");
        yield return new("gene29.f6.raw.commented-directive-header", () => new CsDocument { Header = "// a\n#pragma warning disable CS1591\n", Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } },
            "// a\n#pragma warning disable CS1591\n\nclass C\n{\n}\n");
        // A raw unit that starts with a directive continues the line of a block lambda's brace.
        yield return new("gene29.f6.raw.directive-in-lambda-statement", () => Cs.Parse("class C { object F = () => { x(); #region y\n}; }\n"),
            "Partial; diagnostics: PARSE002 Warning @34+10 1:35 A directive must begin its line.; class C\n{\n    object F = () =>\n    {\n        x(); #region y\n    };\n}\n");
    }
}
