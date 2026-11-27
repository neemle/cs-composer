using System.Text;
using System.Text.RegularExpressions;
using Neemle.CSharpComposer.IO;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Model equality, hashing and text on chains; expression writing of every kind and precedence; writer refusals of
// undefined kinds when validation is off; fresh rendering of each node kind inside a parsed document; byte output.
public static class ModelScenarios
{
    static readonly RenderOptions NoCheck = new() { Validate = false };

    // The value symbol of `_ = source;`.
    static CsSymbol Sym(string source) => (CsSymbol)((CsSymbol)Cs.ParseTree("_ = " + source + ";").Root.Descendants().First(n => n.Node is CsSymbol).Node).Options.Value!;

    static string Compare(object left, object right) =>
        "equal " + (left.Equals(right) ? "yes" : "no") + ", same hash " + (left.GetHashCode() == right.GetHashCode() ? "yes" : "no");

    static string Names(CsSymbol symbol)
    {
        string text = symbol.ToString();
        return string.Join(" ", Regex.Matches(text, @"CsSymbol \{.*?\bName = (\w*)").Select(m => m.Groups[1].Value is "" ? "_" : m.Groups[1].Value)) +
            (text.Contains('\u0001') ? " (marker leaked)" : "");
    }

    static CsExpression Bin(CsOperator op, CsExpression left, CsExpression right) => E(ExpressionKind.Binary, left, right) with { Operator = op };

    static CsExpression A => Cs.Id("a");

    static CsExpression B => Cs.Id("b");

    static string Loose(CsExpression value) => Value(value).Render(NoCheck);

    static CsComment Comment(string text, CommentPlacement placement) => new(text, CommentKind.Block, placement);

    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

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

    // A comment that keeps its parse origin, for attaching to nodes that have none.
    static CsComment Parsed(string text) => (CsComment)Src("class C { " + text + " int F; }").Descendants().First(n => n.Node is CsComment).Node;

    static string Bytes(byte[] bytes) => Convert.ToHexString(bytes);

    sealed class ReadOnlyStream : MemoryStream
    {
        public override bool CanWrite => false;
    }

    sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    public static IEnumerable<Scenario> All()
    {
        // Symbol chains compare, hash and print link by link.
        yield return new("symbol.chain-equal", () => Compare(Sym("a.b(1)[2].c + d"), Sym("a.b(1)[2].c + d")), "equal yes, same hash yes");
        yield return new("symbol.chain-other-head", () => Compare(Sym("a.b.c"), Sym("x.b.c")), "equal no, same hash no");
        yield return new("symbol.chain-other-link", () => Compare(Sym("a.b(1).c"), Sym("a.b(2).c")), "equal no, same hash no");
        yield return new("symbol.chain-other-name", () => Compare(Sym("a.b.c"), Sym("a.d.c")), "equal no, same hash no");
        yield return new("symbol.chain-other-length", () => Compare(Sym("a.b.c"), Sym("b.c")), "equal no, same hash no");
        yield return new("symbol.null", () => Sym("a.b").Equals((CsSymbol?)null) ? "equal" : "different", "different");
        yield return new("symbol.same-instance", () => { var s = Sym("a.b.c"); return Compare(s, s); }, "equal yes, same hash yes");
        yield return new("symbol.expression-receiver", () => Compare(new CsSymbol("c", new() { Target = A.Member("b") }), new CsSymbol("c", new() { Target = A.Member("b") })), "equal yes, same hash yes");
        yield return new("symbol.expression-receiver-differs", () => Compare(new CsSymbol("c", new() { Target = A.Member("b") }), new CsSymbol("c", new() { Target = A.Member("x") })), "equal no, same hash no");
        yield return new("symbol.symbol-or-expression-receiver", () => Compare(Sym("a.b.c"), new CsSymbol("c", new() { Target = A.Member("b") })), "equal no, same hash no");
        yield return new("symbol.operator-chain", () => Compare(Sym("a + b + c"), Sym("a + b + c")), "equal yes, same hash yes");
        yield return new("symbol.operator-chain-differs", () => Compare(Sym("a + b + c"), Sym("a + b - c")), "equal no, same hash no");
        yield return new("symbol.text-chain", () => Names(Sym("a.b(1).c")), "c _ b a _");
        yield return new("symbol.text-operators", () => Names(Sym("a + b * c - d")), "_");
        yield return new("symbol.text-single", () => Names(Cs.Symbol("a")), "a");

        // Expression chains compare and hash link by link; a link without arguments ends the walk.
        yield return new("expression.chain-equal", () => Compare(A.Member("b").Call(Cs.Literal(1)).Member("c"), A.Member("b").Call(Cs.Literal(1)).Member("c")), "equal yes, same hash yes");
        yield return new("expression.chain-differs", () => Compare(A.Member("b").Call(Cs.Literal(1)), A.Member("b").Call(Cs.Literal(2))), "equal no, same hash no");
        yield return new("expression.chain-other-length", () => Compare(A.Member("b"), A.Member("b").Member("b")), "equal no, same hash no");
        yield return new("expression.null-arguments", () => Compare(A with { Arguments = null! }, A with { Arguments = null! }), "equal yes, same hash yes");
        yield return new("expression.null-or-empty-arguments", () => Compare(A with { Arguments = null! }, A), "equal no, same hash no");
        yield return new("expression.empty-or-null-arguments", () => Compare(A, A with { Arguments = null! }), "equal no, same hash no");
        yield return new("expression.null", () => A.Equals((CsExpression?)null) ? "equal" : "different", "different");

        // Values compare by items.
        yield return new("values.equal", () => Compare(new Values<string?>("a", null), new Values<string?>(new List<string?> { "a", null })), "equal yes, same hash yes");
        yield return new("values.differs", () => Compare(new Values<string>("a"), new Values<string>("b")), "equal no, same hash no");
        yield return new("values.other-type", () => new Values<string>("a").Equals((object)"a") ? "equal" : "different", "different");
        yield return new("values.null", () => new Values<string>("a").Equals(null) ? "equal" : "different", "different");
        yield return new("values.null-array", () => new Values<string>((string[])null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'items')");
        yield return new("values.null-sequence", () => new Values<string>((IEnumerable<string>)null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'items')");

        // Expression kinds and precedence.
        yield return new("write.null-forgiving", () => Value(E(ExpressionKind.NullForgiving, A.Member("b"))), "class C\n{\n    void M()\n    {\n        object x = a.b!;\n    }\n}\n");
        yield return new("write.null-forgiving-binary", () => Value(E(ExpressionKind.NullForgiving, Bin(CsOperator.Add, A, B))), "class C\n{\n    void M()\n    {\n        object x = (a + b)!;\n    }\n}\n");
        yield return new("write.post-decrement", () => Value(Cs.PostDecrement(A)), "class C\n{\n    void M()\n    {\n        object x = a--;\n    }\n}\n");
        yield return new("write.post-decrement-binary", () => Loose(Cs.PostDecrement(Bin(CsOperator.Add, A, B))), "class C\n{\n    void M()\n    {\n        object x = (a + b)--;\n    }\n}\n");
        yield return new("write.lambda-operand", () => Loose(Bin(CsOperator.Add, Cs.Lambda(A), B)), "class C\n{\n    void M()\n    {\n        object x = (() => a) + b;\n    }\n}\n");
        yield return new("write.conditional-operand", () => Loose(Bin(CsOperator.Add, E(ExpressionKind.Conditional, A, B, A), B)), "class C\n{\n    void M()\n    {\n        object x = (a ? b : a) + b;\n    }\n}\n");
        yield return new("write.range-operand", () => Loose(Bin(CsOperator.Add, E(ExpressionKind.Range, A, B), B)), "class C\n{\n    void M()\n    {\n        object x = a..b + b;\n    }\n}\n");
        yield return new("write.with-operand", () => Loose(Bin(CsOperator.Add, E(ExpressionKind.With, A) with { Initializer = E(ExpressionKind.Initializer) }, B)), "class C\n{\n    void M()\n    {\n        object x = a with { } + b;\n    }\n}\n");
        yield return new("write.ref-operand", () => Loose(Bin(CsOperator.Add, E(ExpressionKind.Ref, A), B)), "class C\n{\n    void M()\n    {\n        object x = (ref a) + b;\n    }\n}\n");
        yield return new("write.post-increment-operand", () => Loose(Bin(CsOperator.Multiply, Cs.PostIncrement(A), Cs.PostDecrement(B))), "class C\n{\n    void M()\n    {\n        object x = a++ * b--;\n    }\n}\n");
        yield return new("write.throw-operand", () => Loose(Bin(CsOperator.Add, Cs.ThrowExpression(A), B)), "class C\n{\n    void M()\n    {\n        object x = (throw a) + b;\n    }\n}\n");
        yield return new("write.assignment-operand", () => Loose(Bin(CsOperator.Add, Cs.Assign(A, B), B)), "class C\n{\n    void M()\n    {\n        object x = (a = b) + b;\n    }\n}\n");
        yield return new("write.negative-receiver", () => Value(new CsExpression(ExpressionKind.Literal, "-1", Values<CsExpression>.Empty).Member("ToString").Call()), "class C\n{\n    void M()\n    {\n        object x = (-1).ToString();\n    }\n}\n");
        yield return new("write.positive-receiver", () => Value(new CsExpression(ExpressionKind.Literal, "+1", Values<CsExpression>.Empty).Member("ToString").Call()), "class C\n{\n    void M()\n    {\n        object x = (+1).ToString();\n    }\n}\n");
        yield return new("write.coalesce-throw", () => Value(Bin(CsOperator.Coalesce, A, Cs.ThrowExpression(B))), "class C\n{\n    void M()\n    {\n        object x = a ?? throw b;\n    }\n}\n");
        yield return new("write.chain-coalesce-throw", () => Value(Bin(CsOperator.Coalesce, A.Member("b"), Cs.ThrowExpression(B))), "class C\n{\n    void M()\n    {\n        object x = a.b ?? throw b;\n    }\n}\n");
        yield return new("write.chain-coalesce-value", () => Value(Bin(CsOperator.Coalesce, A.Member("b"), Bin(CsOperator.Coalesce, A, B))), "class C\n{\n    void M()\n    {\n        object x = a.b ?? a ?? b;\n    }\n}\n");
        yield return new("write.new-target-typed", () => InClass(new CsField(Cs.Type("C"), "F") { Initializer = E(ExpressionKind.New) }), "class C\n{\n    C F = new();\n}\n");
        yield return new("write.utf8-string", () => Value(new CsExpression(ExpressionKind.StringLiteral, "abc", Values<CsExpression>.Empty) { Utf8 = true }), "class C\n{\n    void M()\n    {\n        object x = \"abc\"u8;\n    }\n}\n");
        yield return new("write.jagged-array", () => Value(E(ExpressionKind.Array, Cs.Literal(3)) with { Type = Cs.Int.Array().Array(2) }), "class C\n{\n    void M()\n    {\n        object x = new int[3][][,];\n    }\n}\n");
        yield return new("write.implicit-array", () => Value(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) }), "class C\n{\n    void M()\n    {\n        object x = new[] { 1 };\n    }\n}\n");
        yield return new("write.omitted-and-field", () => Loose(E(ExpressionKind.Tuple, E(ExpressionKind.Omitted), E(ExpressionKind.Field))), "class C\n{\n    void M()\n    {\n        object x = (, field);\n    }\n}\n");
        yield return new("write.collection-arguments", () => Loose(E(ExpressionKind.Collection, E(ExpressionKind.CollectionArguments, A), B)), "class C\n{\n    void M()\n    {\n        object x = [with(a), b];\n    }\n}\n");
        yield return new("write.global-member", () => Value(Cs.Id("global").Member("System") with { AliasAccess = true }), "class C\n{\n    void M()\n    {\n        object x = global::System;\n    }\n}\n");
        yield return new("write.alias-member", () => Value(Cs.Id("Alias").Member("System") with { AliasAccess = true }), "class C\n{\n    void M()\n    {\n        object x = Alias::System;\n    }\n}\n");
        yield return new("write.pointer-member", () => Loose(A.Member("b") with { PointerAccess = true }), "class C\n{\n    void M()\n    {\n        object x = a->b;\n    }\n}\n");
        yield return new("write.conditional-member", () => Value(A.Member("b") with { ConditionalAccess = true }), "class C\n{\n    void M()\n    {\n        object x = a?.b;\n    }\n}\n");
        yield return new("write.global-chain", () => Value((Cs.Id("global").Member("System") with { AliasAccess = true }).Member("Console").Call()), "class C\n{\n    void M()\n    {\n        object x = global::System.Console();\n    }\n}\n");
        yield return new("write.alias-chain", () => Value((Cs.Id("Alias").Member("System") with { AliasAccess = true }).Member("Console").Call()), "class C\n{\n    void M()\n    {\n        object x = Alias::System.Console();\n    }\n}\n");
        yield return new("write.global-literal-chain", () => Loose((Cs.Literal(1).Member("System") with { AliasAccess = true }).Member("Console").Call()), "class C\n{\n    void M()\n    {\n        object x = 1::System.Console();\n    }\n}\n");
        yield return new("write.link-access-forms", () => Loose((A.Member("b") with { ConditionalAccess = true }).Member("c").Member("d") with { PointerAccess = true }), "class C\n{\n    void M()\n    {\n        object x = a?.b.c->d;\n    }\n}\n");
        yield return new("write.alias-link", () => Loose((A.Member("b").Member("c") with { AliasAccess = true }).Call()), "class C\n{\n    void M()\n    {\n        object x = a.b::c();\n    }\n}\n");
        yield return new("write.conditional-index-link", () => Loose((E(ExpressionKind.Index, A.Member("b"), Cs.Literal(1)) with { ConditionalAccess = true }).Call()), "class C\n{\n    void M()\n    {\n        object x = a.b?[1]();\n    }\n}\n");
        yield return new("write.link-comments", () => Value(A.Member("b").Call() with { Comments = new[] { Comment("/* lead */", CommentPlacement.Leading), Comment("/* trail */", CommentPlacement.Trailing), Comment("/* in */", CommentPlacement.Container) } }), "class C\n{\n    void M()\n    {\n        object x = /* lead */ a.b(/* in */ ) /* trail */ ;\n    }\n}\n");
        yield return new("write.makeref-forms", () => Loose(E(ExpressionKind.Tuple, E(ExpressionKind.MakeRef, A), E(ExpressionKind.RefType, A), E(ExpressionKind.RefValue, A) with { Type = Cs.Int })), "class C\n{\n    void M()\n    {\n        object x = (__makeref(a), __reftype(a), __refvalue(a, int));\n    }\n}\n");
        yield return new("write.switch-fresh-comment", () => Value(Cs.SwitchExpression(A, Cs.Arm(Cs.DiscardPattern(), B)) with { Comments = new[] { Comment("/* c */", CommentPlacement.Container) } }), "class C\n{\n    void M()\n    {\n        object x = a switch\n        {\n            _ => b\n            /* c */\n        };\n    }\n}\n");
        yield return new("write.switch-parsed-comment", () => Value(Cs.SwitchExpression(A, Cs.Arm(Cs.ConstantPattern(Cs.Literal(1)), B), Cs.Arm(Cs.DiscardPattern(), B)) with { Comments = new[] { Parsed("/* c */") with { Placement = CommentPlacement.Container } } }), "class C\n{\n    void M()\n    {\n        object x = a switch { /* c */ 1 => b, _ => b };\n    }\n}\n");
        yield return new("write.initializer-fresh-comment", () => Value(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) with { Comments = new[] { Comment("/* c */", CommentPlacement.Container) } } }), "class C\n{\n    void M()\n    {\n        object x = new[]\n        {\n            1\n            /* c */\n        };\n    }\n}\n");
        yield return new("write.initializer-parsed-comment", () => Value(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1), Cs.Literal(2)) with { Comments = new[] { Parsed("/* c */") with { Placement = CommentPlacement.Container } } } }), "class C\n{\n    void M()\n    {\n        object x = new[] { /* c */ 1, 2 };\n    }\n}\n");

        // Reformatted parsed statements are written fresh, with their comments and trivia.
        yield return new("write.reformat-refs", () => Reformat("class C { void M(int x) { var r = __makeref(x); var t = __reftype(r); int v = __refvalue(r, int); } }", n => n is CsMethod), "class C\n{\n    void M(int x)\n    {\n        var r = __makeref(x);\n        var t = __reftype(r);\n        int v = __refvalue(r, int);\n    }\n}\n");
        yield return new("write.reformat-aliases", () => Reformat("extern alias A;\nclass C { void M() { A::B.C(); global::D.E(); global::F(); } }", n => n is CsMethod), "extern alias A;\n\nclass C\n{\n    void M()\n    {\n        A::B.C();\n        global::D.E();\n        global::F();\n    }\n}\n");
        yield return new("write.reformat-chain-directive", () => Reformat("class C\n{\n    void M()\n    {\n        a\n#if X\n#endif\n            .B().C();\n    }\n}\n", n => n is CsMethod), "class C\n{\n    void M()\n    {\n        a\n#if X\n#endif\n            .B().C();\n    }\n}\n");
        yield return new("write.reformat-switch-comments", () => Reformat("class C { int M(int a) { return a switch { 1 => 2, /* c */ _ => 0 /* d */ }; } }", n => n is CsMethod), "class C\n{\n    int M(int a)\n    {\n        return a switch { 1 => 2, /* c */ _ => 0 /* d */ };\n    }\n}\n");
        yield return new("write.reformat-initializer-comments", () => Reformat("class C { int[] F = { 1, /* c */ 2 /* d */ }; }", n => n is CsField), "class C\n{\n    int[] F = { 1, /* c */ 2 /* d */ };\n}\n");
        yield return new("write.reformat-chain-comments", () => Reformat("class C { void M() { a /* x */ . /* y */ B /* z */ ( /* w */ ) /* v */ ; } }", n => n is CsMethod), "class C\n{\n    void M()\n    {\n        a /* x */./* y */ B /* z */ (/* w */ ) /* v */ ;\n    }\n}\n");

        // With validation off, writers refuse kinds the model does not define.
        yield return new("write.undefined-expression", () => Loose(E((ExpressionKind)99)), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'e')");
        yield return new("write.undefined-operator", () => Loose(Bin((CsOperator)999, A, B)), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'op')");
        yield return new("write.unary-as-binary", () => Loose(Bin(CsOperator.Not, A, B)), "class C\n{\n    void M()\n    {\n        object x = a ! b;\n    }\n}\n");
        yield return new("write.undefined-pattern", () => Loose(E(ExpressionKind.Is, A) with { Pattern = new CsPattern((PatternKind)99) }), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'pattern')");
        yield return new("write.undefined-string-style", () => Loose(new CsExpression(ExpressionKind.StringLiteral, "a", Values<CsExpression>.Empty) { StringStyle = (CsStringStyle)9 }), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'style')");
        yield return new("write.undefined-query-clause", () => Loose(E(ExpressionKind.Query) with { QueryClauses = new[] { new CsQueryClause(QueryClauseKind.From) { Name = "x", Value = A }, new CsQueryClause((QueryClauseKind)99) { Value = A } } }), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'q')");
        yield return new("write.undefined-type-form", () => Field(new CsType("T") { Form = (TypeForm)99 }).Render(NoCheck), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'type')");
        yield return new("write.foreign-member", () => InClass(new ForeignMember()).Render(NoCheck), "throws ArgumentException: Unsupported member node. (Parameter 'member')");
        yield return new("write.undefined-xml", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { new CsComment("", CommentKind.Documentation) { Documentation = new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { new CsXmlNode((XmlNodeKind)99) } } } } }).Render(NoCheck), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'node')");
        yield return new("write.undefined-cref", () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { new CsComment("", CommentKind.Documentation) { Documentation = new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { new CsXmlNode(XmlNodeKind.EmptyElement, "see") { Attributes = new[] { new CsXmlAttribute("cref", null, new CsCref((CrefKind)99)) } } } } } } }).Render(NoCheck), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'reference')");
        yield return new("write.undefined-overloaded-operator", () => InClass(new CsOperatorDeclaration((OverloadableOperator)99, Cs.Int) { Body = Cs.Body() }).Render(NoCheck), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'op')");
        yield return new("write.undefined-directive-condition", () => new CsDocument { Items = new CsNode[] { new CsDirective(DirectiveKind.If) { Condition = A.Call() }, new CsDirective(DirectiveKind.EndIf) } }.Render(NoCheck), "throws ArgumentException: Invalid preprocessor condition.");

        // A fresh node inside a parsed document renders on its own.
        yield return new("fresh.tuple-element", () => Edit("class C { (int A, string B) F; }", n => n is TupleElement, _ => new TupleElement(Cs.Type("long"), "L")), "class C\n{\n    (long L, string B) F;\n}\n");
        yield return new("fresh.tuple-element-unnamed", () => Edit("class C { (int A, string B) F; }", n => n is TupleElement, _ => new TupleElement(Cs.Type("long"))), "class C\n{\n    (long, string B) F;\n}\n");
        yield return new("fresh.attribute-argument", () => Edit("[A(1)] class C { }", n => n is AttributeArgument, _ => new AttributeArgument(Cs.Literal(2))), "[A(2)]\nclass C\n{\n}\n");
        yield return new("fresh.xml-text", () => Edit("/// <summary>x</summary>\nclass C { }", n => n is CsXmlNode { Kind: XmlNodeKind.Text }, _ => new CsXmlNode(XmlNodeKind.Text, Value: "y")), "/// <summary>y</summary>\nclass C\n{\n}\n");
        yield return new("fresh.cref", () => Edit("/// <see cref=\"A\"/>\nclass C { }", n => n is CsCref, _ => new CsCref(CrefKind.Name) { Type = Cs.Type("B") }), "/// <see cref=\"B\" />\nclass C\n{\n}\n");
        yield return new("fresh.symbol", () => Edit("Run(1);\n", n => n is CsSymbol { Name: null, Options.Value: not null }, _ => Cs.Value(2)), "Run(2);\n");
        yield return new("fresh.instruction", () => Edit("if (a) { Run(); }\n", n => n is CsSymbol { Name: "Run" }, _ => Cs.Instruction(InstructionKind.Return)), "throws GenValidationException: SYM001 Items[0].Target: An expression value must be a symbol or expression.");

        yield return new("reformat.global-symbol", () => Reformat("Run( 1 );\n", n => n is CsSymbol { Options.Value: not null }), "Run(1);\n");
        yield return new("reformat.trivia-directive", () => Reformat("class C\n{\n    int F = 1\n#if X\n#endif\n        ;\n}\n", n => n is CsDirective), "class C\n{\n    int F = 1\n#if X\n#endif\n        ;\n}\n");

        // Bytes: byte order marks, encodings and streams.
        yield return new("bytes.no-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = false })), "636C61737320430A7B0A7D0A");
        yield return new("bytes.utf8-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true })), "EFBBBF636C61737320430A7B0A7D0A");
        yield return new("bytes.utf16-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true, Encoding = Encoding.Unicode })), "FFFE63006C00610073007300200043000A007B000A007D000A00");
        yield return new("bytes.utf16be-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true, Encoding = Encoding.BigEndianUnicode })), "FEFF0063006C00610073007300200043000A007B000A007D000A");
        yield return new("bytes.utf32-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true, Encoding = Encoding.UTF32 })), "FFFE0000630000006C00000061000000730000007300000020000000430000000A0000007B0000000A0000007D0000000A000000");
        yield return new("bytes.utf32be-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true, Encoding = new UTF32Encoding(true, true) })), "0000FEFF000000630000006C00000061000000730000007300000020000000430000000A0000007B0000000A0000007D0000000A");
        yield return new("bytes.ascii-bom", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { EmitBom = true, Encoding = Encoding.ASCII })), "throws ArgumentException: The selected encoding has no supported byte order mark. (Parameter 'encoding')");
        yield return new("bytes.encoding-preamble", () => Bytes(Src("class C { }").ToBytes(new EmitOptions { Encoding = new UTF8Encoding(true) })), "EFBBBF636C61737320430A7B0A7D0A");
        yield return new("bytes.source-preamble", () => Bytes(Cs.Parse(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'c', (byte)'l', (byte)'a', (byte)'s', (byte)'s', (byte)' ', (byte)'C', (byte)'{', (byte)'}' }).Root.ToBytes()), "EFBBBF636C61737320430A7B0A7D0A");
        yield return new("bytes.read-only-destination", () => { Src("class C { }").WriteTo(new ReadOnlyStream()); return "written"; }, "throws ArgumentException: Output stream must be writable. (Parameter 'destination')");
        yield return new("bytes.write-only-source", () => Cs.Parse(new WriteOnlyStream()), "throws ArgumentException: Input stream must be readable. (Parameter 'source')");
        yield return new("bytes.bom-conflict", () => Cs.Parse(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'c' }, new Parsing.ParseOptions { Encoding = Encoding.Unicode }), "throws ArgumentException: The supplied encoding conflicts with the byte order mark.");
        yield return new("bytes.bom-match", () => Cs.Parse(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("class C { }")).ToArray(), new Parsing.ParseOptions { Encoding = Encoding.Unicode }), "Typed; diagnostics: ; class C\n{\n}\n");
    }
}
