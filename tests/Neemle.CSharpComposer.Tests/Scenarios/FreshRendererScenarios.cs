using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-29 S0.5: defects of the fresh renderer that the S0 corpus gate found with node.Detach().Reformat() (AD 84939297
// I3 as amended). Each row reproduced its defect on 1d72674; the expected outcome is the fixed behaviour, and a
// "reads back" row is the gate's P3 for that source: the reformatted text parses to a model equal to the original.
public static class FreshRendererScenarios
{
    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    // The first node that matches, reformatted in place.
    static CsDocument Reformat(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Reformat(Find(document, match));
    }

    // The `nth` node (in document order) that matches, replaced.
    static CsDocument EditAt(string source, Func<CsNode, bool> match, int nth, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = document.Descendants().Where(n => match(n.Node)).ElementAt(nth).Path;
        return document.Replace(path, change(document.At(path)));
    }

    // Whether the source reformatted as a whole, and with each node reformatted on its own, parses back to the model
    // it was parsed to (the gate's P3).
    static string ReadsBack(string source)
    {
        var document = Src(source);
        var differ = document.Descendants().Prepend((Path: new NodePath(), Node: (CsNode)document))
            .Where(n => !Cs.Parse(n.Path.Segments.Count == 0 ? document.Reformat().Render() : document.Reformat(n.Path).Render()).Root.Equals(document))
            .Select(n => "[" + string.Join(",", n.Path.Segments) + "]" + n.Node.GetType().Name).ToArray();
        return differ.Length == 0 ? "reads back" : "differs at " + string.Join(" ", differ);
    }

    static bool IsLiteral(CsNode node, string text) => node is CsExpression { Kind: ExpressionKind.Literal } literal && literal.Text == text;

    internal const string ContextualNames =
        "class C\n{\n    int value;\n\n    void M(int add, string from, bool async)\n    {\n        var group = add;\n        value = group + add;\n" +
        "        if (from is string alias) { }\n        System.Func<int, int> f = field => field + value;\n    }\n}\n";

    internal const string WhenPattern = "class C { bool M(object o) => o is 1 + 0 or A.B; bool N(object o) => o is 0; }";

    internal const string Comments = "class C\n{\n    void M()\n    {\n        // lead\n        int a = 1; // tail\n        Run();\n    }\n}\n";

    internal const string Lambdas =
        "class C\n{\n    void M()\n    {\n        F(t => t, (t) => t, (int t) => t, (a, b) => a, () => 0, [A] (t) => t, int (t) => t, (ref int t) => t, async t => t, static t => t);\n    }\n}\n";

    internal const string IndexInitializer = "class C\n{\n    D d = new()\n    {\n        [\"a\"] = 1,\n        [k] = v,\n    };\n}\n";

    internal const string Enum = "enum E\n{\n    A,\n    B = 2\n}\n";

    internal const string Modifiers = "class C : B\n{\n    public new void N() { }\n\n    new public void O() { }\n\n    new static public int P;\n}\n";

    public static IEnumerable<Scenario> All()
    {
        // 1. A contextual keyword used as a name is written as it is; a name written with `@` keeps it.
        yield return new("gene29.s05.names.contextual-reformat", () => Src(ContextualNames).Reformat(),
            "class C\n{\n    int value;\n\n    void M(int add, string from, bool async)\n    {\n        var group = add;\n        value = group + add;\n" +
            "        if (from is string alias)\n        {\n        }\n        System.Func<int, int> f = field => field + value;\n    }\n}\n");
        yield return new("gene29.s05.names.contextual-reads-back", () => ReadsBack(ContextualNames), "reads back");
        yield return new("gene29.s05.names.verbatim-kept", () => Src("class C { int @value; void M() { @value = @class + @x; } }").Reformat(),
            "class C\n{\n    int @value;\n\n    void M()\n    {\n        @value = @class + @x;\n    }\n}\n");
        yield return new("gene29.s05.names.expression-words", () => Value(E(ExpressionKind.Tuple, Cs.Id("await"), Cs.Id("nameof"), Cs.Id("var"),
                Cs.Id("and"), Cs.Id("when"), Cs.Id("value"), Cs.Id("select"), Cs.Id("field"), Cs.Id("class"))),
            "class C\n{\n    void M()\n    {\n        object x = (@await, @nameof, @var, @and, when, value, select, field, @class);\n    }\n}\n");
        yield return new("gene29.s05.names.designation", () => EditAt("class C { bool M(object o) => o is int x; }", n => n is CsPattern { Kind: PatternKind.Declaration }, 0,
            n => ((CsPattern)n) with { Name = "or" }).Reformat(), "class C\n{\n    bool M(object o) => o is int @or;\n}\n");
        yield return new("gene29.s05.names.type-context", () => InClass(new CsField(new CsType("value"), "F"), new CsField(new CsType("var"), "G"), new CsField(new CsType("Box"), "H")),
            "class C\n{\n    @value F;\n\n    var G;\n\n    Box H;\n}\n");
        yield return new("gene29.s05.names.var-type-reads-back", () => ReadsBack("class C { void M() { var a, b; } }"), "reads back");
        // In a pattern `when` would start a case guard: a constant or a designation named so is escaped, in place or fresh.
        yield return new("gene29.s05.names.when-pattern-edit", () => EditAt(WhenPattern, n => IsLiteral(n, "0"), 0, _ => Cs.Id("when")),
            "class C { bool M(object o) => o is 1 + @when or A.B; bool N(object o) => o is 0; }");
        yield return new("gene29.s05.names.when-constant-edit", () => EditAt(WhenPattern, n => IsLiteral(n, "0"), 1, _ => Cs.Id("when")),
            "class C { bool M(object o) => o is 1 + 0 or A.B; bool N(object o) => o is @when; }");
        yield return new("gene29.s05.names.when-pattern-fresh", () => EditAt(WhenPattern, n => IsLiteral(n, "0"), 0, _ => Cs.Id("when")).Reformat(),
            "class C\n{\n    bool M(object o) => o is 1 + @when or A.B;\n\n    bool N(object o) => o is 0;\n}\n");
        yield return new("gene29.s05.names.when-designation", () => EditAt("class C { bool M(object o) => o is int x; }", n => n is CsPattern { Kind: PatternKind.Declaration }, 0,
            n => ((CsPattern)n) with { Name = "when" }).Reformat(), "class C\n{\n    bool M(object o) => o is int @when;\n}\n");
        // `field` is the backing field in a property's accessor, and a name in an event's accessor or a method.
        yield return new("gene29.s05.names.field-fresh", () => InClass(
                new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get") { Body = Cs.Body(Cs.Return(E(ExpressionKind.Tuple, Cs.Id("field"), Cs.Id("x")))) } } },
                new CsEvent(Cs.Type("System.Action"), "E") { Accessors = new[] { new CsAccessor("add") { Body = Cs.Body(Cs.Statement(Cs.Id("field"))) }, new CsAccessor("remove") { Body = Cs.Body() } } },
                new CsMethod(Cs.Int, "M") { Body = Cs.Body(Cs.Return(Cs.Id("field"))) }) with { LanguageVersion = CsLanguageVersion.CSharp15 },
            "class C\n{\n    int P\n    {\n        get\n        {\n            return (@field, x);\n        }\n    }\n\n    event System.Action E\n    {\n        add\n        {\n            field;\n        }\n        remove\n        {\n        }\n    }\n\n    int M()\n    {\n        return field;\n    }\n}\n");
        yield return new("gene29.s05.names.field-in-accessor-edit", () => EditAt("class C { int P { get { return 0; } } int M() { return 0; } }", n => IsLiteral(n, "0"), 0, _ => Cs.Id("field")),
            "class C { int P { get { return @field; } } int M() { return 0; } }");
        yield return new("gene29.s05.names.field-in-method-edit", () => EditAt("class C { int P { get { return 0; } } int M() { return 0; } }", n => IsLiteral(n, "0"), 1, _ => Cs.Id("field")),
            "class C { int P { get { return 0; } } int M() { return field; } }");
        yield return new("gene29.s05.names.field-in-event-edit", () => EditAt("class C { event System.Action E { add { Run(0); } remove { } } }", n => IsLiteral(n, "0"), 0, _ => Cs.Id("field")),
            "class C { event System.Action E { add { Run(field); } remove { } } }");
        // A query keyword is a name outside a query and is escaped inside one, edited in place or written fresh.
        yield return new("gene29.s05.names.query-edit", () => EditAt("class C { object M() => from x in xs where x > 0 select x; }", n => IsLiteral(n, "0"), 0, _ => Cs.Id("select")),
            "class C { object M() => from x in xs where x > @select select x; }");
        yield return new("gene29.s05.names.query-fresh", () => EditAt("class C { object M() => from x in xs where x > 0 select x.group; }", n => IsLiteral(n, "0"), 0, _ => Cs.Id("select")).Reformat(),
            "class C\n{\n    object M() => from x in xs where x > @select select x.group;\n}\n");
        // 2. A comment before a local declaration belongs to it once; Reformat writes it once.
        yield return new("gene29.s05.comments.local-once", () =>
        {
            var document = Src(Comments);
            var local = document.Descendants().Select(n => n.Node).OfType<CsStatement>().First(s => s.Kind == StatementKind.Local);
            return string.Join(",", local.Comments.Select(c => c.Placement + " " + c.Text)) + "\n" + document.Reformat().Render();
        }, "Leading // lead,Trailing // tail\n" + Comments);
        yield return new("gene29.s05.comments.reads-back", () => ReadsBack(Comments), "reads back");
        // 3. An enum member reformats on its own.
        yield return new("gene29.s05.enum-member.reformat", () => Reformat(Enum, n => n is CsEnumMember), Enum);
        yield return new("gene29.s05.enum-member.reformat-last", () => Reformat(Enum, n => n is CsEnumMember { Name: "B" }), "enum E\n{\n    A,\n    B = 2,\n}\n");
        yield return new("gene29.s05.enum-member.reads-back", () => ReadsBack(Enum), "reads back");
        // 4. An index initializer keeps its `=` when its target renders again.
        yield return new("gene29.s05.index-initializer.reformat-target", () => Reformat(IndexInitializer, n => n is CsExpression { Kind: ExpressionKind.ImplicitIndex }), IndexInitializer);
        yield return new("gene29.s05.index-initializer.reads-back", () => ReadsBack(IndexInitializer), "reads back");
        // 5. One parameter that is just a name is written without parentheses.
        yield return new("gene29.s05.lambda.parameter-list", () => Src(Lambdas).Reformat(),
            "class C\n{\n    void M()\n    {\n        F(t => t, t => t, (int t) => t, (a, b) => a, () => 0, [A] (t) => t, int (t) => t, (ref int t) => t, async t => t, static t => t);\n    }\n}\n");
        yield return new("gene29.s05.lambda.reads-back", () => ReadsBack(Lambdas), "reads back");
        // 6. An implicitly typed array creation is `new[]`.
        yield return new("gene29.s05.implicit-array", () => Reformat("class C\n{\n    object F = new[] { 1 };\n}\n", n => n is CsField), "class C\n{\n    object F = new[] { 1 };\n}\n");
        // 7. Modifiers are written in the canonical order: accessibility, static, then new.
        yield return new("gene29.s05.modifiers.new", () => Src(Modifiers).Reformat(),
            "class C : B\n{\n    public new void N()\n    {\n    }\n\n    public new void O()\n    {\n    }\n\n    public static new int P;\n}\n");
    }
}
