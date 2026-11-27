using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Layout of edited parsed documents (list regions, removed comments, conditional trivia carried through reformatting),
// emission diagnostics, edit refusals, deep paths, symbol lowering and the remaining validation rules.
public static class LayoutScenarios
{
    static readonly RenderOptions NoCheck = new() { Validate = false };

    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    static CsDocument Edit(string source, Func<CsNode, bool> match, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = Find(document, match);
        return document.Replace(path, change(document.At(path)));
    }

    static CsDocument Remove(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Remove(Find(document, match));
    }

    static CsDocument Insert(string source, Func<CsNode, bool> parent, int index, CsNode child)
    {
        var document = Src(source);
        var path = Find(document, parent);
        return document.Insert(path, index < 0 ? document.At(path).Children.Count + 1 + index : index, child);
    }

    static CsDocument Reformat(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Reformat(Find(document, match));
    }

    static CsComment Block(string text, CommentPlacement placement = CommentPlacement.Leading) => new(text, CommentKind.Block, placement);

    // Every token of a program wrapped in an active conditional with an inactive alternative, reformatted and
    // detached: the conditionals follow their tokens and the result parses back to the same layout.
    static string Wrapped(string program)
    {
        var tokens = Regex.Matches(program, @"[A-Za-z_]\w*|\d+|=>|!=|==|<=|>=|\+=|\+\+|\|\||&&|[^\s]");
        string wrapped = string.Concat(tokens.Select(t => "\n#if true\n" + t.Value + "\n#else\ninvalid \" text\n#endif\n"));
        var options = new ParseOptions { LanguageVersion = CsLanguageVersion.CSharp14 };
        var parsed = Cs.Parse(wrapped, options);
        string formatted = parsed.Root.Reformat().Render();
        var reparsed = Cs.Parse(formatted, options);
        string detached = ((CsDocument)parsed.Root.Detach()).Render();
        return "diagnostics " + parsed.Diagnostics.Count + ", round trip " + (parsed.Root.Render() == wrapped) + ", reformatted lines " +
            formatted.Split('\n').Length + ", reparsed diagnostics " + reparsed.Diagnostics.Count + ", stable " +
            (reparsed.Root.Reformat().Render() == formatted) + ", detached same " + (detached == formatted);
    }

    // A conditional inside an expression whose tokens change around it: the conditional keeps its place among them.
    static CsDocument Retokened(string source, params (string From, string To)[] renames)
    {
        var document = Src(source);
        foreach (var (from, to) in renames)
        {
            var path = Find(document, n => n is CsExpression { Kind: ExpressionKind.Identifier } e && e.Text == from);
            document = document.Replace(path, Cs.Id(to));
        }
        return document;
    }

    static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    static object Fragment(string text, ParseKind kind)
    {
        var parsed = new CsRaw(text).Parse(new ParseContext(kind));
        return parsed.Coverage + "; " + string.Join(" | ", parsed.Diagnostics.Select(d => d.Code + " " + d.Message)) + "; " + parsed.Root.GetType().Name;
    }

    static object Tree(params CsNode[] items) => Cs.Tree(items) is var tree && tree.Validate() is { Count: > 0 } errors ? errors : tree.Render();

    static CsExpression Str(string text, CsStringStyle style) => new(ExpressionKind.StringLiteral, text, Values<CsExpression>.Empty) { StringStyle = style };

    static string Paths(IEnumerable<GenDiagnostic> errors) => string.Join(" | ", errors.Select(e => e.Code + " " + e.Path));

    static CsSymbol Var(string name, CsNode value) => Cs.Symbol(name, new() { AsVar = true, Value = value });

    static CsSymbol Typed(string name, CsType type, CsNode? value = null) => new(name, new() { Type = type, Value = value });

    public static IEnumerable<Scenario> All()
    {
        // Conditional trivia around every token follows its token through reformatting.
        yield return new("layout.trivia.wrapped-linq", () => Wrapped("using System; using System.Linq; class Program { static int F(int x)=>x; static void Main() { int[] values={1,2,3}; var result=(from x in values where x>1 orderby x descending select x).ToArray(); Func<int,int> f=static x=>x+1; for(int i=0;i<values.Length;i++) { values[i]+=1; } if(result.Length!=2 || f(values[0])!=3) throw new Exception(); } }"), "diagnostics 0, round trip True, reformatted lines 648, reparsed diagnostics 0, stable True, detached same True");
        yield return new("layout.trivia.wrapped-members", () => Wrapped("using System; [Obsolete] class C<T> where T:class,new() { public int this[int index] { get=>index; set { } } public C() {} public event Action E; public T M<U>(ref U x, int y=1) where U:struct => new T(); } class Program { static void Main() {} }"), "diagnostics 0, round trip True, reformatted lines 426, reparsed diagnostics 0, stable True, detached same True");
        yield return new("layout.trivia.wrapped-flow", () => Wrapped("using System; class Program { static int F(object value)=>value switch { int x when x>0=>x, string {Length:>0} s=>s.Length, _=>0 }; static void Main() { var x=F(1); while(x<3) { if(x==2) break; x++; } try { if(x!=2) throw new Exception(); } catch(Exception e) when(e.Message.Length>0) { throw; } finally { x=0; } } }"), "diagnostics 0, round trip True, reformatted lines 605, reparsed diagnostics 0, stable True, detached same True");
        yield return new("layout.trivia.retokened-around", () => Retokened("class C\n{\n    int F = a + b\n#if X\n#endif\n        + c;\n}\n", ("a", "x"), ("c", "y")), "class C\n{\n    int F = x + b\n#if X\n#endif\n+ y;\n}\n");
        yield return new("layout.trivia.retokened-after", () => Retokened("class C\n{\n    int F = a + b\n#if X\n#endif\n        + c + d;\n}\n", ("c", "y")), "class C\n{\n    int F = a + b\n#if X\n#endif\n+ y + d;\n}\n");
        yield return new("layout.trivia.retokened-longer", () => Retokened("class C\n{\n    int F = a + b\n#if X\n#endif\n        + c;\n}\n", ("a", "x"), ("b", "y"), ("c", "z")), "class C\n{\n    int F = x + y\n#if X\n#endif\n+ z;\n}\n");
        yield return new("layout.trivia.reformat-nested", () => Reformat("class C\n{\n    int M() => F(a\n#if X\n        , b\n#endif\n#if Y\n        , c\n#endif\n        );\n}\n", n => n is CsMember), "class C\n{\n    int M() => F(a\n#if X\n        , b\n#endif\n#if Y\n        , c\n#endif\n);\n}\n");
        yield return new("layout.trivia.reformat-trailing", () => Reformat("class C\n{\n    int F = 1 +\n#if X\n    2\n#else\n    3\n#endif\n    ;\n}\n", n => n is CsMember), "class C\n{\n    int F = 1 +\n#if X\n    2\n#else\n3\n#endif\n;\n}\n");
        yield return new("layout.trivia.reformat-crlf", () => Reformat("class C\r\n{\r\n    int F = 1 +\r\n#if X\r\n    2\r\n#endif\r\n    + 3;\r\n}\r\n", n => n is CsMember), "class C\r\n{\r\n    int F = 1 +\r\n#if X\r\n    2\r\n#endif\r\n+3;\r\n}\r\n");
        yield return new("layout.trivia.reformat-tabs", () => Reformat("class C\n{\n\tint F = 1 +\n#if X\n\t\t2 +\n#endif\n\t\t3;\n}\n", n => n is CsMember), "class C\n{\n    int F = 1 +\n#if X\n\t\t2 +\n#endif\n3;\n}\n");
        yield return new("layout.trivia.invalid-content", () => InClass(new CsField(Cs.Int, "F") { Initializer = Cs.Literal(1) with { SyntaxTrivia = new[] { new CsSyntaxTrivia(Cs.Int) } } }).Render(NoCheck), "throws GenValidationException: GEN075 Root.Children[0].Children[0].Children[1].Children[0]: Syntax trivia requires a directive or inactive text and a valid placement.");
        yield return new("layout.trivia.fresh-directive", () => InClass(new CsField(Cs.Int, "F") { Initializer = Cs.Literal(1) with { SyntaxTrivia = new[] { new CsSyntaxTrivia(new CsDirective(DirectiveKind.Region) { Text = "R" }), new CsSyntaxTrivia(new CsDirective(DirectiveKind.EndRegion), SyntaxTriviaPlacement.Trailing) } } }), "class C\n{\n    int F = \n#region R\n1\n#endregion\n;\n}\n");
        yield return new("layout.trivia.fresh-disabled", () => InClass(new CsField(Cs.Int, "F") { Initializer = Cs.Literal(1) with { SyntaxTrivia = new[] { new CsSyntaxTrivia(new CsDirective(DirectiveKind.If) { Condition = Cs.Id("X") }), new CsSyntaxTrivia(new CsDisabledText("old")), new CsSyntaxTrivia(new CsDirective(DirectiveKind.EndIf), SyntaxTriviaPlacement.Trailing) } } }), "class C\n{\n    int F = \n#if X\nold\n1\n#endif\n;\n}\n");

        // List regions: removed items, their comments, inserted comments and fresh items.
        yield return new("layout.list.remove-commented-member", () => Remove("class C\n{\n    int A;\n    // about B\n    int B;\n    int D;\n}\n", n => n is CsField { Name: "B" }), "class C\n{\n    int A;\n    int D;\n}\n");
        yield return new("layout.list.remove-commented-last", () => Remove("class C\n{\n    int A;\n    // about B\n    int B;\n}\n", n => n is CsField { Name: "B" }), "class C\n{\n    int A;\n}\n");
        yield return new("layout.list.remove-comment-own-line", () => Remove("class C\n{\n    int A;\n    // gone\n    int B;\n}\n", n => n is CsComment), "class C\n{\n    int A;\n    int B;\n}\n");
        yield return new("layout.list.remove-comment-after-code", () => Remove("class C\n{\n    int A; /* gone */\n    int B;\n}\n", n => n is CsComment), "class C\n{\n    int A;\n    int B;\n}\n");
        yield return new("layout.list.remove-comment-before-code", () => Remove("class C\n{\n    int A;\n    /* gone */ int B;\n}\n", n => n is CsComment), "class C\n{\n    int A;\n    int B;\n}\n");
        yield return new("layout.list.remove-comment-at-end", () => Remove("class C\n{\n    int A;\n    /* gone */}", n => n is CsComment), "class C\n{\n    int A;\n}");
        yield return new("layout.list.edit-comment", () => Edit("class C\n{\n    int A;\n    // old\n    int B;\n}\n", n => n is CsComment, _ => new CsComment("// new")), "class C\n{\n    int A;\n    // new\n    int B;\n}\n");
        yield return new("layout.list.insert-container-comment", () => Insert("class C\n{\n    int A;\n}\n", n => n is CsTypeDeclaration, 0, Block("/* first */", CommentPlacement.Container)), "class C\n{\n    /* first */\n    int A;\n}\n");
        yield return new("layout.list.insert-into-document", () => Insert("// lead\nclass A { }\n", n => n is CsDocument, -1, new CsTypeDeclaration(TypeKind.Class, "B")), "// lead\nclass A { }\nclass B\n{\n}\n");
        yield return new("layout.list.replace-only-document-item", () => Edit("  class A { }", n => n is CsTypeDeclaration, _ => new CsTypeDeclaration(TypeKind.Class, "B")), "  class B\n  {\n  }\n");
        yield return new("layout.list.insert-file-namespace", () => Insert("namespace N;\nclass A { }\n", n => n is CsNamespace, -1, new CsTypeDeclaration(TypeKind.Class, "B")), "namespace N;\nclass A { }\nclass B\n{\n}\n");
        yield return new("layout.list.replace-file-namespace-only", () => Edit("namespace N;\nclass A { }\n", n => n is CsTypeDeclaration, _ => new CsTypeDeclaration(TypeKind.Class, "B")), "namespace N;\nclass B\n{\n}\n");
        yield return new("layout.list.insert-empty-inline", () => Insert("class C { }", n => n is CsTypeDeclaration, 0, new CsField(Cs.Int, "F")), "class C { \n    int F;\n}");
        yield return new("layout.list.insert-empty-lines", () => Insert("class C\n{\n}\n", n => n is CsTypeDeclaration, 0, new CsField(Cs.Int, "F")), "class C\n{\n    int F;\n}\n");
        yield return new("layout.list.insert-same-line", () => Insert("class C { int A; int B; }", n => n is CsTypeDeclaration, 1, new CsField(Cs.Int, "F")), "class C { int A;\n    int F;\n int B; }");
        yield return new("layout.list.remove-and-insert", () => { var d = Src("class C\n{\n    int A;\n    // b\n    int B;\n    int D;\n}\n"); d = d.Remove(Find(d, n => n is CsField { Name: "B" })); return d.Insert(Find(d, n => n is CsTypeDeclaration), 2, new CsField(Cs.Int, "E")); }, "class C\n{\n    int A;\n    int D;\n    int E;\n}\n");

        // Emission diagnostics: opaque nodes inside a reformatted scope.
        yield return new("layout.emission.builder-raw-member", () => Cs.Document().Class("C").Add(Cs.RawMember("int x = 1;")).End().Build().Reformat().EmissionDiagnostics, "diagnostics: FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
        yield return new("layout.emission.untouched", () => Cs.Document().Class("C").Add(Cs.RawMember("int x = 1;")).End().Build().EmissionDiagnostics, "diagnostics: ");
        yield return new("layout.emission.every-opaque-kind", () => new CsDocument { Items = new CsNode[] { new CsRaw("// raw"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(Cs.RawStatement("Run();"), Cs.Statement(Cs.RawExpression("Go()"))) }, new CsMethod(Cs.Void, "N") { Body = Cs.RawBody("{ }") }, new CsField(Cs.RawType("int"), "F"), Cs.RawMember("int G;") } } } }.Reformat().EmissionDiagnostics, "diagnostics: FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region. | FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region. | FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region. | FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region. | FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region. | FORMAT001 Warning @0+0 1:1 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
        yield return new("layout.emission.out-of-scope", () => new CsDocument { Items = new CsNode[] { new CsRaw("// raw"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { Cs.RawMember("int G;"), new CsField(Cs.Int, "F") } } } }.Reformat(new NodePath(1, 1)).EmissionDiagnostics, "diagnostics: ");
        yield return new("layout.emission.parsed-lines", () => Cs.Parse("class C\r\n{\r    int A;\n    int P => N()++;\r\n    int Q => 1;\r\n}\r\n").Root.Reformat().EmissionDiagnostics, "diagnostics: FORMAT001 Warning @26+15 4:5 Opaque syntax is preserved verbatim; formatting coverage excludes this region.");

        // Edit refusals.
        yield return new("layout.edit.required-kind", () => { var d = Field(Cs.Int); return d.Replace(Find(d, n => n is CsType), Cs.Id("x")); }, "throws ArgumentException: Replacement has an invalid required child kind.");
        yield return new("layout.edit.optional-kind", () => { var d = Method(new CsMethod(Cs.Void, "M") { Body = Cs.Body() }); return d.Replace(Find(d, n => n is CsBody), Cs.Id("x")); }, "throws ArgumentException: Replacement has an invalid optional child kind.");
        yield return new("layout.edit.list-kind", () => InClass(new CsField(Cs.Int, "F")).Replace(new NodePath(0, 0), Cs.Id("x")), "throws ArgumentException: Replacement has an invalid list child kind.");
        yield return new("layout.edit.remove-optional", () => { var d = InClass(new CsField(Cs.Int, "F") { Initializer = Cs.Literal(1) }); return d.Remove(Find(d, n => n is CsExpression)); }, "class C\n{\n    int F;\n}\n");
        yield return new("layout.edit.too-deep", () => { var d = Field(Cs.Int); CsType deep = Cs.Int; for (int i = 0; i < 130; i++) deep = deep.Array(); return d.Replace(Find(d, n => n is CsType), deep); }, "throws GenValidationException: GEN001 Types[0].Members[0].Type.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element.Element: Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("layout.edit.path-too-long", () => Src("class C { }").At(new NodePath(Enumerable.Repeat(0, 200).ToArray())), "throws ArgumentException: Path exceeds128. (Parameter 'path')");
        yield return new("layout.edit.path-missing", () => Src("class C { }").Remove(new NodePath(0, 5)), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'path')");

        // Deep chains: an error deep in a long chain reports a compressed path.
        yield return new("layout.path.long-chain", () => { CsExpression broken = Cs.Id("a") with { Comments = new[] { new CsComment("// c") { Comments = new[] { new CsComment("// d") } } } }; for (int i = 0; i < 130; i++) broken = broken.Member("b"); return Paths(InMethod(Cs.Return(broken)).Validate()); }, "GEN075 Root.Children[0].Children[0].Children[1].Children[0].Children[0].Arguments[0]×129.Children[0].Children[0]");
        yield return new("layout.path.long-symbol-chain", () => { CsSymbol chain = new(null, new() { Value = E((ExpressionKind)99) }); for (int i = 0; i < 130; i++) chain = new CsSymbol("b", new() { Target = chain }); return Paths(Cs.Tree(Var("x", chain)).Validate()); }, "GEN014 Types[0].Members[0].Body.Statements[0].Expression.Arguments[0]×129.Arguments[0]");

        // Fragments whose too-deep type reading was abandoned.
        yield return new("layout.fragment.misread-expression", () => Fragment("a.F<" + Repeat("L<", 126) + "int" + Repeat(">", 126) + ">(1)", ParseKind.Expression), "Typed; ; CsExpression");
        yield return new("layout.fragment.misread-expression-nullable", () => Fragment("a.F<" + Repeat("L<", 126) + "int?" + Repeat(">", 126) + ">(1)", ParseKind.Expression), "Typed; ; CsExpression");
        yield return new("layout.fragment.misread-statement", () => Fragment("a.F<" + Repeat("L<", 125) + "int" + Repeat(">", 125) + ">(1);", ParseKind.Statements), "Typed; ; CsBody");
        yield return new("layout.fragment.misread-statement-nullable", () => Fragment("a.F<" + Repeat("L<", 125) + "int?" + Repeat(">", 125) + ">(1);", ParseKind.Statements), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("layout.fragment.misread-member", () => Fragment("object M(C a) => a.F<" + Repeat("L<", 125) + "int" + Repeat(">", 125) + ">(1);", ParseKind.Member), "Typed; ; CsMethod");
        yield return new("layout.fragment.misread-member-deeper", () => Fragment("object M(C a) => a.F<" + Repeat("L<", 128) + "int" + Repeat(">", 128) + ">(1);", ParseKind.Member), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("layout.fragment.cancellable", () => new CsRaw("x").Parse(new ParseContext(ParseKind.Expression), CancellationToken.None), "Typed; diagnostics: ; CsExpression");

        // Qualified type names carry their inner comments after the qualifier.
        yield return new("layout.names.qualified-comment", () => Field(Cs.Type("System.Int32") with { Comments = new[] { Block("/* c */", CommentPlacement.Container) } }), "class C\n{\n    System./* c */ Int32 F;\n}\n");
        yield return new("layout.names.alias-comment", () => Field(Cs.Type("global::Int32") with { Comments = new[] { Block("/* c */", CommentPlacement.Container) } }), "class C\n{\n    global::/* c */ Int32 F;\n}\n");
        yield return new("layout.names.simple-comment", () => Field(Cs.Type("Int32") with { Comments = new[] { Block("/* c */", CommentPlacement.Container) } }), "class C\n{\n    /* c */ Int32 F;\n}\n");
        yield return new("layout.names.escaped-identifiers", () => InClass(new CsField(Cs.Int, "\\u0061b"), new CsField(Cs.Int, "@class")), "class C\n{\n    int ab;\n\n    int @class;\n}\n");
        yield return new("layout.names.bad-escape", () => InClass(new CsField(Cs.Int, "\\uZZZZ")), "invalid: GEN003 Invalid identifier.");
        yield return new("layout.names.raw-lines", () => Value(new CsExpression(ExpressionKind.Raw, "F(\r\n#if X\r\n1\r\n#endif\r\n)", Values<CsExpression>.Empty)), "class C\n{\n    void M()\n    {\n        object x = F(\n#if X\n1\n#endif\n);\n    }\n}\n");

        // Scalar renames of parsed symbols and statements keep the surrounding source.
        yield return new("layout.scalar.global-symbol-name", () => Edit("Run();\n", n => n is CsSymbol { Name: "Run" }, n => (CsSymbol)n with { Name = "Stop" }), "Stop();\n");
        yield return new("layout.scalar.goto-label", () => Edit("class C { void M() { goto L; L: ; } }", n => n is CsStatement { Kind: StatementKind.Goto }, n => (CsStatement)n with { Text = "K" }), "class C { void M() { goto K; L: ; } }");
        yield return new("layout.scalar.label-name", () => Edit("class C { void M() { goto L; L: ; } }", n => n is CsStatement { Kind: StatementKind.Label }, n => (CsStatement)n with { Text = "K" }), "class C { void M() { goto L; K:\n; } }");
        yield return new("layout.scalar.local-name", () => Edit("class C { void M() { int a = 1; } }", n => n is CsStatement { Kind: StatementKind.Local }, n => (CsStatement)n with { Text = "b" }), "class C { void M() { int b = 1; } }");
        yield return new("layout.scalar.unchanged-chain", () => { var d = Src("class C { void M() { a.B(1).C(2); } }"); var p = Find(d, n => n is CsExpression { Kind: ExpressionKind.Invocation }); return d.Replace(p, (CsExpression)d.At(p) with { }); }, "class C { void M() { a.B(1).C(2); } }");

        // Statements written fresh.
        yield return new("layout.statement.else-statement", () => InMethod(new CsStatement(StatementKind.If, Cs.Id("a")) { Body = Cs.Body(Cs.Return()), Else = Cs.If(Cs.Id("b"), Cs.Body(Cs.Return())) }), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            return;\n        }\n        else\n        {\n            if (b)\n            {\n                return;\n            }\n        }\n    }\n}\n");
        yield return new("layout.statement.else-block", () => InMethod(new CsStatement(StatementKind.If, Cs.Id("a")) { Body = Cs.Body(Cs.Return()), Else = Cs.Block(Cs.Return()) }), "class C\n{\n    void M()\n    {\n        if (a)\n        {\n            return;\n        }\n        else\n        {\n            return;\n        }\n    }\n}\n");
        yield return new("layout.statement.await-using", () => Method(new CsMethod(Cs.Type("System.Threading.Tasks.Task"), "M") { Modifiers = new[] { "async" }, Body = Cs.Body(new CsStatement(StatementKind.Using, Cs.Id("d")) { Body = Cs.Body(), IsAwait = true }) }), "class C\n{\n    async System.Threading.Tasks.Task M()\n    {\n        await using (d)\n        {\n        }\n    }\n}\n");
        yield return new("layout.statement.disabled-text", () => InMethod(new CsStatement(StatementKind.Directive) { Directive = new CsDirective(DirectiveKind.If) { Condition = Cs.Id("X") } }, new CsStatement(StatementKind.DisabledText) { Disabled = new CsDisabledText("old();") }, new CsStatement(StatementKind.Directive) { Directive = new CsDirective(DirectiveKind.EndIf) }), "class C\n{\n    void M()\n    {\n#if X\nold();\n#endif\n    }\n}\n");
        yield return new("layout.statement.using-resource-kinds", () => InMethod(new CsStatement(StatementKind.Using) { Resource = Cs.Return(), Body = Cs.Body() }), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("layout.statement.using-resource-uninitialized", () => InMethod(new CsStatement(StatementKind.Using) { Resource = new CsStatement(StatementKind.Local, Text: "d") { Type = Cs.Type("D") }, Body = Cs.Body() }), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("layout.statement.using-resource-group", () => InMethod(new CsStatement(StatementKind.Using) { Resource = new CsStatement(StatementKind.LocalGroup) { ForInitializer = Cs.ForDeclaration(Cs.Type("D"), Cs.Variable("a", Cs.Id("x")), Cs.Variable("b")) }, Body = Cs.Body() }), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("layout.statement.using-resource-group-null", () => InMethod(new CsStatement(StatementKind.Using) { Resource = new CsStatement(StatementKind.LocalGroup), Body = Cs.Body() }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.using-declaration-kinds", () => InMethod(new CsStatement(StatementKind.UsingDeclaration) { Resource = Cs.Return() }), "invalid: GEN071 Using declaration needs an initialized local.");
        yield return new("layout.statement.using-declaration-group", () => InMethod(new CsStatement(StatementKind.UsingDeclaration) { Resource = new CsStatement(StatementKind.LocalGroup) { ForInitializer = Cs.ForDeclaration(Cs.Type("D"), Cs.Variable("a", Cs.Id("x")), Cs.Variable("b")) } }), "invalid: GEN071 Using declaration needs an initialized local.");
        yield return new("layout.statement.using-declaration-group-null", () => InMethod(new CsStatement(StatementKind.UsingDeclaration) { Resource = new CsStatement(StatementKind.LocalGroup) }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.for-null-variables", () => InMethod(Cs.For(new CsForInitializer { Type = Cs.Int, Variables = null! }, null, Values<CsExpression>.Empty, Cs.Body())), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.for-assignment-target", () => InMethod(Cs.For(Cs.ForExpressions(Cs.Assign(Cs.Literal(1), Cs.Literal(2))), null, Values<CsExpression>.Empty, Cs.Body())), "invalid: GEN088 An assignment requires a writable target.");
        yield return new("layout.statement.for-assignment-null-arguments", () => InMethod(Cs.For(Cs.ForExpressions(E(ExpressionKind.Assignment) with { Arguments = null! }), null, Values<CsExpression>.Empty, Cs.Body())), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.switch-null-body", () => InMethod(Cs.Switch(Cs.Id("a"), new CsSwitchSection(new[] { Cs.Default() }, null!))), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.switch-null-section", () => InMethod(Cs.Switch(Cs.Id("a"), new CsSwitchSection[] { null! })), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.switch-null-labels", () => InMethod(Cs.Switch(Cs.Id("a"), new CsSwitchSection(null!, Cs.Body(Cs.Break())))), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.switch-expression-null-arms", () => Value(Cs.SwitchExpression(Cs.Id("a")) with { Arms = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.extended-property-pattern", () => Value(E(ExpressionKind.Is, Cs.Id("o")) with { Pattern = new CsPattern(PatternKind.Recursive) { HasProperties = true, Properties = new[] { new CsPattern(PatternKind.Property) { Name = "A.B", Patterns = new[] { Cs.DiscardPattern() } } } } }).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Extended property pattern requires C# 10.");
        yield return new("layout.statement.null-arguments", () => Value(Cs.Id("F").Call() with { Arguments = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.tree-with-body", () => Method(new CsMethod(Cs.Void, "M") { Body = new CsBody { Tree = Cs.Tree(), Statements = null! } }), "invalid: GEN002 Required child collection is null.");
        yield return new("layout.statement.tree-and-expression", () => Method(new CsMethod(Cs.Int, "M") { Body = new CsBody { Tree = Cs.Tree(), Expression = Cs.Literal(1) } }), "invalid: GEN022 A body tree cannot coexist with another body representation.");
        yield return new("layout.statement.inferred-array-null-initializer", () => Value(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer) with { Arguments = null! } }), "invalid: GEN002 Required node or collection is null. | GEN092 An inferred array requires a nonempty initializer and no explicit sizes.");
        yield return new("layout.statement.anonymous-method-null-attributes", () => Value(Cs.Lambda(Cs.Body()) with { Kind = ExpressionKind.AnonymousMethod, LambdaAttributes = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.query-null-first", () => Value(E(ExpressionKind.Query) with { QueryClauses = new CsQueryClause[] { null!, new(QueryClauseKind.Select) { Value = Cs.Id("x") } } }), "invalid: GEN092 Query requires an initial from clause and a terminal select/group. | GEN002 Required node or collection is null.");
        yield return new("layout.statement.nameof-unbound-argument", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("D").Generic(Cs.Int, Cs.Type("L").Unbound()).Nullable() })).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Unbound generic nameof requires C# 14.");
        yield return new("layout.statement.nameof-null-argument", () => Value(E(ExpressionKind.NameOf) with { Arguments = new CsExpression[] { null! } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("layout.statement.xml-values", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { new CsComment("", CommentKind.Documentation) { Documentation = new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { new CsXmlNode(XmlNodeKind.Comment, Value: "a-"), new CsXmlNode(XmlNodeKind.Comment, Value: null), new CsXmlNode(XmlNodeKind.CData, Value: null), new CsXmlNode(XmlNodeKind.ProcessingInstruction, "p", "a?>") } } } } }), "invalid: GEN092 XML value contains its closing delimiter. | GEN002 Required node or collection is null.");
        yield return new("layout.statement.system-value-type", () => InMethod(Cs.Local(Cs.Type("System.Int32"), "a", Cs.Literal(null)), Cs.Local(Cs.Type("global::System.String"), "b", Cs.Literal(null)), Cs.Local(Cs.Type("System.Nope"), "c", Cs.Literal(null))), "class C\n{\n    void M()\n    {\n        System.Int32 a = null;\n        global::System.String b = null;\n        System.Nope c = null;\n    }\n}\n");
        yield return new("layout.statement.fixed-buffer-outside-struct", () => InClass(new CsField(Cs.Int, "F") { Modifiers = new[] { "fixed" } }), "invalid: GEN092 Fixed buffer requires a struct. | GEN092 Fixed buffer needs the fixed modifier, a size and no initializer.");
        yield return new("layout.statement.destructor-in-struct", () => InType(TypeKind.Struct, new CsDestructor("C", Cs.Body())), "invalid: GEN051 Destructor requires its containing class name.");

        // Symbol lowering.
        yield return new("layout.lower.declaration-as-value", () => Tree(Var("x", Typed("y", Cs.Int))), "invalid: SYM001 A declaration or compound symbol cannot be used as a scalar expression.");
        yield return new("layout.lower.compound-as-value", () => Tree(Var("x", Cs.Compound(Cs.Value(1)))), "invalid: SYM001 A declaration or compound symbol cannot be used as a scalar expression.");
        yield return new("layout.lower.using-as-value", () => Tree(Var("x", new CsSymbol("y", new() { Using = true, Type = Cs.Type("D"), Value = Cs.Value(1) }))), "invalid: SYM001 A declaration or compound symbol cannot be used as a scalar expression.");
        yield return new("layout.lower.parameter-as-value", () => Tree(Var("x", new CsSymbol("p", new() { Operation = SymbolOperation.Parameter }))), "invalid: SYM001 Parameter/clause needs a signature/query context.");
        yield return new("layout.lower.member-forms", () => Tree(Var("x", new CsSymbol("b", new() { Target = Cs.Symbol("a"), ConditionalAccess = true })), Var("y", new CsSymbol("b", new() { Target = Cs.Symbol("a"), AliasAccess = true })), Var("z", new CsSymbol(null, new() { Target = Cs.Symbol("a"), AsCall = true, Arguments = Cs.Compound() }))), "var x = a?.b;\nvar y = a::b;\nvar z = a();\n");
        yield return new("layout.lower.expression-value", () => Tree(Var("x", Cs.Id("a").Member("b"))), "var x = a.b;\n");
        yield return new("layout.lower.statement-item", () => Tree(Cs.Return(), Cs.Symbol("F", new() { AsCall = true })), "return;\nF();\n");
        yield return new("layout.lower.parsed-statement-item", () => { var parsed = Cs.ParseTree("lock (o) { F(); }").Root; return Cs.Tree(parsed.Items.ToArray()).Render(); }, "lock (o)\n{\n    F();\n}\n");
        yield return new("layout.lower.declaration-group", () => Tree(Cs.Compound(Typed("a", Cs.Int, Cs.Value(1)), Typed("b", Cs.Int))), "int a = 1, b;\n");
        yield return new("layout.lower.declaration-group-mixed-types", () => Tree(Cs.Compound(Typed("a", Cs.Int, Cs.Value(1)), Typed("b", Cs.Type("long")))), "invalid: SYM001 Declaration group requires one explicit type and consistent modifiers.");
        yield return new("layout.lower.declaration-group-var", () => Tree(Cs.Compound(Var("a", Cs.Value(1)), Var("b", Cs.Value(2)))), "invalid: SYM001 Declaration group requires one explicit type and consistent modifiers.");
        yield return new("layout.lower.declaration-group-modifiers", () => Tree(Cs.Compound(Typed("a", Cs.Int, Cs.Value(1)), new CsSymbol("b", new() { Type = Cs.Int, Value = Cs.Value(1), VariableModifiers = CsVariableModifiers.Const }))), "invalid: SYM001 Declaration group requires one explicit type and consistent modifiers.");
        yield return new("layout.lower.declaration-group-using", () => Tree(Cs.Compound(new CsSymbol("a", new() { Type = Cs.Type("D"), Value = Cs.Value(1), Using = true }), Typed("b", Cs.Type("D"), Cs.Value(1)))), "invalid: SYM001 Declaration group requires one explicit type and consistent modifiers.");
        yield return new("layout.lower.declaration-group-await", () => Tree(Cs.Compound(new CsSymbol("a", new() { Type = Cs.Type("D"), Value = Cs.Value(1), Using = true, Await = true }), new CsSymbol("b", new() { Type = Cs.Type("D"), Value = Cs.Value(1), Using = true }))), "invalid: SYM001 Declaration group requires one explicit type and consistent modifiers.");
        yield return new("layout.lower.declaration-group-empty", () => Tree(Cs.Compound()), "invalid: SYM001 A statement compound requires declaration symbols of the same type.");
        yield return new("layout.lower.for-initializer-node", () => Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Cs.ForDeclaration(Cs.Int, Cs.Variable("i", Cs.Literal(0))), Body = Cs.Tree() })), "for (int i = 0; ; )\n{\n}\n");
        yield return new("layout.lower.for-initializer-expressions", () => Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Cs.Compound(Cs.Symbol("i", new() { AsAssignment = true, Value = Cs.Value(0) }), Cs.Symbol("j", new() { AsAssignment = true, Value = Cs.Value(1) })), Body = Cs.Tree() })), "for (i = 0, j = 1; ; )\n{\n}\n");
        yield return new("layout.lower.for-initializer-expression", () => Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Cs.Symbol("i", new() { AsAssignment = true, Value = Cs.Value(0) }), Body = Cs.Tree() })), "for (i = 0; ; )\n{\n}\n");
        yield return new("layout.lower.for-initializer-declaration", () => Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Var("i", Cs.Value(0)), Body = Cs.Tree() })), "for (var i = 0; ; )\n{\n}\n");
        yield return new("layout.lower.using-declaration-resource", () => Tree(Cs.Instruction(InstructionKind.Using, new() { Initializer = Typed("d", Cs.Type("D"), Cs.Value(null)), Body = Cs.Tree() })), "using (D d = null)\n{\n}\n");
        yield return new("layout.lower.using-value-resource", () => Tree(Cs.Instruction(InstructionKind.Using, new() { Value = Cs.Symbol("d"), Body = Cs.Tree() })), "using (d)\n{\n}\n");
        yield return new("layout.lower.if-else", () => Tree(Cs.Instruction(InstructionKind.If, new() { Condition = Cs.Symbol("a"), Body = Cs.Tree(Cs.Instruction(InstructionKind.Return)), Else = Cs.Tree(Cs.Instruction(InstructionKind.Return)) })), "if (a)\n{\n    return;\n}\nelse\n{\n    return;\n}\n");
        yield return new("layout.lower.lambda-parameter-nodes", () => Tree(Var("f", new CsSymbol(null, new() { Operation = SymbolOperation.Lambda, Value = Cs.Value(1), ParameterSymbols = Cs.Compound(new CsLambdaParameter("x", Cs.Int)) }))), "var f = (int x) => 1;\n");
        yield return new("layout.lower.lambda-parameter-wrong", () => Tree(Var("f", new CsSymbol(null, new() { Operation = SymbolOperation.Lambda, Value = Cs.Value(1), ParameterSymbols = Cs.Compound(Cs.Value(1)) }))), "invalid: SYM001 A signature compound contains parameter symbols.");
        yield return new("layout.lower.query-clause-nodes", () => Tree(Var("q", new CsSymbol(null, new() { Operation = SymbolOperation.Query, Parts = new CsNode[] { new CsQueryClause(QueryClauseKind.From) { Name = "x", Value = Cs.Id("xs") }, new CsQueryClause(QueryClauseKind.Select) { Value = Cs.Id("x") } } }))), "var q = from x in xs select x;\n");
        yield return new("layout.lower.query-clause-wrong", () => Tree(Var("q", new CsSymbol(null, new() { Operation = SymbolOperation.Query, Parts = new CsNode[] { Cs.Value(1) } }))), "invalid: SYM001 A query contains clause symbols.");
        yield return new("layout.lower.range", () => Tree(Var("r", new CsSymbol(null, new() { Operation = SymbolOperation.Range, Value = Cs.Value(1), Target = Cs.Value(2) })), Var("s", new CsSymbol(null, new() { Operation = SymbolOperation.Range }))), "var r = 1..2;\nvar s = ..;\n");
        yield return new("layout.lower.interpolation-conditional", () => Tree(Var("s", new CsSymbol(null, new() { Operation = SymbolOperation.InterpolatedString, Parts = new CsNode[] { new CsSymbol(null, new() { Operation = SymbolOperation.Interpolation, Value = new CsSymbol(null, new() { AsConditional = true, Parts = new CsNode[] { Cs.Symbol("a"), Cs.Value(1), Cs.Value(2) } }) }) } }))), "var s = $\"{(a ? 1 : 2)}\";\n");

        // Builders, factories and small model members.
        yield return new("layout.api.volatile-field", () => Cs.Document().Class("C").Field(Cs.Int, "F").Volatile().End().End().Build(), "class C\n{\n    volatile int F;\n}\n");
        yield return new("layout.api.async-lambda", () => Value(Cs.AsyncLambda(Cs.Body(), Cs.LambdaParameter("x"))), "class C\n{\n    void M()\n    {\n        object x = async (x) => {\n};\n    }\n}\n");
        yield return new("layout.api.symbol-argument-lists", () => Tree(Var("a", Cs.Symbol(new() { AsIndex = true, Target = Cs.Symbol("x") })), Var("b", Cs.Symbol(new() { AsArray = true, Type = Cs.Int }))), "invalid: SYM001 Index access requires a target and at least one index.");
        yield return new("layout.api.symbol-array-list", () => Tree(Var("b", Cs.Symbol(new() { AsArray = true, Type = Cs.Int, Initializer = Cs.Symbol(new() { AsInitializer = true, Parts = new CsNode[] { Cs.Value(1) } }) }))), "var b = new int[] { 1 };\n");
        yield return new("layout.api.nullable-of-nullable", () => Cs.Int.Nullable().Nullable().Form.ToString(), "Nullable");
        yield return new("layout.api.string-styles", () => InClass(new CsField(Cs.Type("string"), "A") { Initializer = Str("a\"b", CsStringStyle.Verbatim) }, new CsField(Cs.Type("string"), "B") { Initializer = Str("a\nb", CsStringStyle.Raw) }, new CsField(Cs.Type("string"), "C") { Initializer = Str("", CsStringStyle.Raw) }, new CsField(Cs.Type("string"), "D") { Initializer = Str("\"q", CsStringStyle.Raw) }, new CsField(Cs.Type("string"), "E") { Initializer = Str("q\"", CsStringStyle.Raw) }, new CsField(Cs.Type("string"), "G") { Initializer = Str("q", CsStringStyle.Raw) }), "class C\n{\n    string A = @\"a\"\"b\";\n\n    string B = \"\"\"\na\nb\n\"\"\";\n\n    string C = \"\"\"\n\n\"\"\";\n\n    string D = \"\"\"\n\"q\n\"\"\";\n\n    string E = \"\"\"\nq\"\n\"\"\";\n\n    string G = \"\"\"q\"\"\";\n}\n");
        yield return new("layout.api.interpolated-conditional", () => Value(E(ExpressionKind.InterpolatedString, E(ExpressionKind.Interpolation, E(ExpressionKind.Conditional, Cs.Id("a"), Cs.Literal(1), Cs.Literal(2))))), "class C\n{\n    void M()\n    {\n        object x = $\"{(a ? 1 : 2)}\";\n    }\n}\n");
        yield return new("layout.api.block-documentation", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { new CsComment("", CommentKind.Documentation) { BlockDocumentation = true, Documentation = new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { new CsXmlNode(XmlNodeKind.Text, Value: " x ") } } } } }), "class C\n{\n    /** x */\n    int F;\n}\n");
        yield return new("layout.api.extension-cref", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { new CsComment("", CommentKind.Documentation) { Documentation = new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { new CsXmlNode(XmlNodeKind.EmptyElement, "see") { Attributes = new[] { new CsXmlAttribute("cref", null, new CsCref(CrefKind.Extension) { Parameters = new[] { new CsFunctionPointerParameter(Cs.Int) }, Member = new CsCref(CrefKind.Name) { Type = Cs.Type("M") } }) } } } } } } }), "class C\n{\n    /// <see cref=\"extension(int).M\" />\n    int F;\n}\n");
        yield return new("layout.api.line-directive-file", () => new CsDocument { Items = new CsNode[] { new CsDirective(DirectiveKind.Line) { Line = 5, File = null }, new CsDirective(DirectiveKind.Line) { Line = 6, File = "a.cs" } } }, "#line 5\n#line 6 \"a.cs\"\n");
        yield return new("layout.api.compound-assignment-operators", () => InMethod(Cs.Statement(E(ExpressionKind.Assignment, Cs.Id("a"), Cs.Literal(1)) with { Operator = CsOperator.LeftShift }), Cs.Statement(E(ExpressionKind.Assignment, Cs.Id("a"), Cs.Literal(1)) with { Operator = CsOperator.Coalesce })), "class C\n{\n    void M()\n    {\n        a <<= 1;\n        a ??= 1;\n    }\n}\n");
        yield return new("layout.api.compound-assignment-loose", () => Value(E(ExpressionKind.Assignment, Cs.Id("a"), Cs.Literal(1)) with { Operator = CsOperator.LessThan }).Render(NoCheck), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'op')");
        yield return new("layout.api.parenthesized-target", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Parenthesized, Cs.Id("a")), Cs.Literal(1))), Cs.Statement(Cs.Assign(E(ExpressionKind.Parenthesized, Cs.Id("a"), Cs.Id("b")), Cs.Literal(1)))), "invalid: GEN088 An assignment requires a writable target. | GEN013 Expression has an invalid number of operands.");
    }
}
