using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// Insert places each child kind into the one list slot that owns it, at the position implied by its
// siblings; the edited source keeps every untouched byte and shows the new child where it was put.
[Trait("Category", "Unit")]
public sealed class ChildSlotInsertionTests
{
    // Expected is a rendered substring; a cref row also parses the rendered reference back.
    // Prepare changes the parsed model in memory first, for a starting state that parsing does not return as typed nodes.
    sealed record Row(string Source, Func<CsNode, bool> Parent, Func<CsNode, bool>? After, Func<CsNode, CsNode> Child, string Expected, bool Tree = false,
        Func<CsDocument, CsDocument>? Prepare = null);

    static T Clone<T>(CsNode parent, Func<T, bool>? match = null) where T : CsNode =>
        (T)parent.Children.OfType<T>().First(n => match?.Invoke(n) ?? true).Detach();

    static CsDocument AddOnly(CsDocument document)
    {
        var type = document.Types.Single();
        var custom = (CsEvent)type.Members.Single();
        return document with { Types = new[] { type with { Members = new CsMember[] { custom with { Accessors = new[] { custom.Accessors.Single(a => a.Kind == "add") } } } } } };
    }

    static readonly Dictionary<string, Row> Rows = new()
    {
        ["trivia"] = new("class C { }", n => n is CsTypeDeclaration, null,
            _ => new CsSyntaxTrivia(new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = new[] { "CS0168" } }), "#pragma warning disable CS0168"),
        ["comment"] = new("class C { }", n => n is CsTypeDeclaration, null, _ => new CsComment("// note"), "// note\nclass C"),
        ["document"] = new("class C { }", n => n is CsDocument, n => n is CsTypeDeclaration, _ => new CsTypeDeclaration(TypeKind.Class, "D"), "class D"),
        ["namespace"] = new("namespace N { class C { } }", n => n is CsNamespace, n => n is CsTypeDeclaration, _ => new CsTypeDeclaration(TypeKind.Class, "D"), "class D"),
        ["xml-content"] = new("/// <summary>a</summary>\nclass C { }", n => n is CsXmlNode { Name: "summary", Kind: XmlNodeKind.Element }, n => true,
            p => Clone<CsXmlNode>(p) with { Value = "b" }, "<summary>ab</summary>"),
        ["xml-attribute"] = new("/// <param name=\"a\">x</param>\nclass C { }", n => n is CsXmlNode { Name: "param" }, n => n is CsXmlAttribute,
            p => Clone<CsXmlAttribute>(p) with { Name = "kind", Value = "k" }, "name=\"a\" kind=\"k\""),
        ["cref-parameter"] = new("/// <see cref=\"M(int)\"/>\nclass C { void M(int a, int b) { } }", n => n is CsCref { Parameters: not null }, n => n is CsFunctionPointerParameter,
            _ => new CsFunctionPointerParameter(Cs.Int), "cref=\"M(int, int)\""),
        ["cref-first-parameter"] = new("/// <see cref=\"M\"/>\nclass C { void M(int a) { } }", n => n is CsCref, null,
            _ => new CsFunctionPointerParameter(Cs.Int), "cref=\"M(int)\""),
        ["extension-member"] = new("static class E { extension(int i) { public int A => i; } }", n => n is CsExtension, n => n is CsMember,
            p => Clone<CsProperty>(p) with { Name = "B" }, "int B => i;"),
        ["extension-type-parameter"] = new("static class E { extension<T>(T i) { } }", n => n is CsExtension, n => n is TypeParameter,
            p => Clone<TypeParameter>(p) with { Name = "U" }, "extension<T, U>(T i)"),
        ["delegate-parameter"] = new("delegate void D(int a);", n => n is CsDelegate, n => n is CsParameter, _ => new CsParameter(Cs.Int, "b"), "D(int a, int b)"),
        ["delegate-type-parameter"] = new("delegate void D<T>(T a);", n => n is CsDelegate, n => n is TypeParameter, _ => new TypeParameter("U"), "D<T, U>"),
        ["operator-parameter"] = new("class C { public static C operator +(C a) => a; }", n => n is CsOperatorDeclaration, n => n is CsParameter,
            _ => new CsParameter(Cs.Type("C"), "b"), "operator +(C a, C b)"),
        ["indexer-parameter"] = new("class C { int this[int a] => a; }", n => n is CsProperty, n => n is CsParameter, _ => new CsParameter(Cs.Int, "b"), "this[int a, int b]"),
        ["function-pointer-parameter"] = new("unsafe class C { delegate*<int, void> f; }", n => n is CsType { Form: TypeForm.FunctionPointer }, n => n is CsFunctionPointerParameter { Type.Name: "int" },
            _ => new CsFunctionPointerParameter(Cs.Type<long>()), "delegate*<int, long, void>"),
        ["query-clause"] = new("class C { object F = from x in xs select x; }", n => n is CsExpression { Kind: ExpressionKind.Query }, n => n is CsQueryClause { Kind: QueryClauseKind.From },
            _ => new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) }, "where true"),
        ["query-ordering"] = new("class C { object F = from x in xs orderby x select x; }", n => n is CsQueryClause { Kind: QueryClauseKind.OrderBy }, n => n is CsQueryClause,
            p => Clone<CsQueryClause>(p) with { Value = Cs.Id("y") }, ", y ascending select x"),
        ["switch-section"] = new("switch (v) { case 1: break; }", n => n is CsInstructionOptions { Sections.Count: > 0 }, n => n is CsSwitchSection,
            _ => Cs.Section(Cs.Body(Cs.Break()), Cs.Default()), "default:", Tree: true),
        ["catch"] = new("try { } catch (E) { }", n => n is CsInstructionOptions { Catches.Count: > 0 }, n => n is CsCatch, _ => Cs.CatchAll(Cs.Body()), "catch\n", Tree: true),
        ["recursive-property"] = new("class C { bool F(object o) => o is P { A: 1 }; }", n => n is CsPattern { Kind: PatternKind.Recursive }, n => n is CsPattern,
            p => Clone<CsPattern>(p, x => x.Kind == PatternKind.Property) with { Name = "B" }, "{ A: 1, B: 1 }"),
        ["list-element"] = new("class C { bool F(int[] o) => o is [1, 2]; }", n => n is CsPattern { Kind: PatternKind.List }, n => n is CsPattern,
            _ => Cs.ConstantPattern(Cs.Literal(3)), "[1, 2, 3]"),
        ["switch-label"] = new("class C { void M(int v) { switch (v) { case 1: break; } } }", n => n is CsSwitchSection, n => n is CsSwitchLabel,
            _ => Cs.Case(Cs.Literal(2)), "case 2:"),
        ["argument"] = new("class C { object F = G(1); }", n => n is CsExpression { Kind: ExpressionKind.Invocation }, n => n is CsExpression,
            _ => Cs.Literal(2), "G(1, 2)"),
        ["method-parameter"] = new("class C { void M<T>(int a) { } }", n => n is CsMethod, n => n is CsParameter, _ => new CsParameter(Cs.Int, "b"), "M<T>(int a, int b)"),
        ["method-type-parameter"] = new("class C { void M<T>(int a) { } }", n => n is CsMethod, n => n is TypeParameter, _ => new TypeParameter("U"), "M<T, U>(int a)"),
        ["base-type"] = new("class C<T>(int a) : B { }", n => n is CsTypeDeclaration, n => n is CsType, _ => Cs.Type("I"), ": B, I"),
        ["type-parameter"] = new("class C<T>(int a) : B { }", n => n is CsTypeDeclaration, n => n is TypeParameter, _ => new TypeParameter("U"), "C<T, U>"),
        ["primary-parameter"] = new("class C<T>(int a) : B { }", n => n is CsTypeDeclaration, n => n is CsParameter, _ => new CsParameter(Cs.Int, "b"), "(int a, int b)"),
        ["property-accessor"] = new("class C { int P { get; } }", n => n is CsProperty, n => n is CsAccessor, _ => new CsAccessor("set"), "get; set;"),
        // An add-only event is emit-rejected (GEN030), so parsing keeps it as raw source and no validated edit
        // can produce it; the starting model drops remove from a parsed complete event without an edit. The
        // edited accessor list renders canonically, with the clone of the empty add body after add.
        ["event-accessor"] = new("class C { event System.Action E { add { } remove { F(); } } }", n => n is CsEvent, n => n is CsAccessor,
            p => Clone<CsAccessor>(p) with { Kind = "remove" }, "        }\n        remove\n        {\n        }\n    }\n}", Prepare: AddOnly),
        ["lambda-attribute"] = new("var f = [A] (int a) => a;", n => n is CsSymbolOptions { Operation: SymbolOperation.Lambda }, n => n is CsAttribute,
            p => Clone<CsAttribute>(p) with { Type = Cs.Type("B") }, "[A] [B] (int a)", Tree: true),
        ["symbol-type-argument"] = new("var x = F<int>(0);", n => n is CsSymbolOptions { TypeArguments.Count: > 0 }, n => n is CsType,
            _ => Cs.Type<string>(), "F<int, string>(0)", Tree: true),
    };

    public static IEnumerable<object[]> Names() => Rows.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Names))]
    public void Insert_places_the_child_in_its_own_slot(string name)
    {
        var row = Rows[name];
        CsNode root = row.Tree ? Cs.ParseTree(row.Source).Root : row.Prepare is { } prepare ? prepare(Cs.Parse(row.Source).Root) : Cs.Parse(row.Source).Root;
        var located = row.Tree ? ((CsTree)root).Descendants().First(n => row.Parent(n.Node)) : ((CsDocument)root).Descendants().First(n => row.Parent(n.Node));
        var children = located.Node.Children;
        int index = 0;
        for (int i = 0; i < children.Count; i++) if (row.After?.Invoke(children[i]) == true) index = i + 1;
        var child = row.Child(located.Node);
        if (row.Expected.StartsWith("cref=", StringComparison.Ordinal))
        {
            // The cref parameter list is the slot. The edit reaches the rendered comment (GENE-24 pass 2), and
            // parsing that text back gives the edited reference.
            string text = ((CsDocument)root).Insert(located.Path, index, child).Render();
            Assert.Contains(row.Expected, text, StringComparison.Ordinal);
            var cref = Cs.Parse(text).Root.Descendants().Select(n => n.Node).OfType<CsCref>().Single();
            Assert.Equal(row.Expected.Count(c => c == ',') + 1, cref.Parameters!.Count);
            Assert.All(cref.Parameters, p => Assert.Equal("int", p.Type.Name));
            return;
        }
        string rendered = row.Tree ? ((CsTree)root).Insert(located.Path, index, child).Render() : ((CsDocument)root).Insert(located.Path, index, child).Render();
        Assert.True(rendered.Replace("\r\n", "\n").Contains(row.Expected, StringComparison.Ordinal), "rendered: " + rendered);
        Assert.DoesNotContain(row.Expected, row.Source);
    }

    [Fact]
    public void Insert_rejects_a_child_kind_that_has_no_slot_in_the_parent()
    {
        var document = Cs.Parse("class C { void M() { } }").Root;
        var body = document.Descendants().Single(n => n.Node is CsBody).Path;
        var error = Assert.Throws<ArgumentException>(() => document.Insert(body, 0, new CsField(Cs.Int, "F")));
        Assert.Equal("This node kind cannot be inserted into the selected child slot.", error.Message);
    }

    [Fact]
    public void Builder_lambda_and_query_symbols_take_parameters_and_clauses_in_their_own_lists()
    {
        var lambda = Cs.Symbol(new CsSymbolOptions { Operation = SymbolOperation.Lambda, Parameters = new[] { Cs.LambdaParameter("a") }, Value = Cs.Symbol("a") });
        var tree = Cs.Tree(Cs.Symbol("f", new() { Type = Cs.Generic("System.Func", Cs.Int, Cs.Int, Cs.Int), Value = lambda }));
        var options = tree.Descendants().Single(n => ReferenceEquals(n.Node, lambda.Options)).Path;
        var withParameter = tree.Insert(options, lambda.Options.Children.Count, Cs.LambdaParameter("b"));
        Assert.Equal(new[] { "a", "b" }, withParameter.Descendants().Select(n => n.Node).OfType<CsLambdaParameter>().Select(p => p.Name));

        var query = Cs.Symbol(new CsSymbolOptions
        {
            Operation = SymbolOperation.Query,
            QueryClauses = new[] { new CsQueryClause(QueryClauseKind.From) { Name = "x", Value = Cs.Id("xs") }, new CsQueryClause(QueryClauseKind.Select) { Value = Cs.Id("x") } }
        });
        var queryTree = Cs.Tree(Cs.Symbol("q", new() { AsVar = true, Value = query }));
        var queryOptions = queryTree.Descendants().Single(n => ReferenceEquals(n.Node, query.Options)).Path;
        var filtered = queryTree.Insert(queryOptions, 1, new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) });
        Assert.Contains("from x in xs where true select x", filtered.Render());
    }
}
