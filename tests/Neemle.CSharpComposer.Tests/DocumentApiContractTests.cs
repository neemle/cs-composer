using System.Text;
using Neemle.CSharpComposer.IO;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// Public-API contracts of the document and tree edit surface, the emit options and the
// opaque-formatting diagnostics: argument guards, edit index arithmetic and rewrite kind checks.
[Trait("Category", "Unit")]
public sealed class DocumentApiContractTests
{
    static CsDocument FieldDocument(CsExpression? initializer = null) => new()
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(Cs.Int, "F") { Initializer = initializer ?? Cs.Literal(1) } } } }
    };

    static CsExpression DeepNot(int depth)
    {
        CsExpression expression = Cs.Id("x");
        for (int i = 0; i < depth; i++) expression = Cs.Unary(CsOperator.Not, expression);
        return expression;
    }

    sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    [Fact]
    public void Parse_rejects_null_and_unreadable_streams()
    {
        Assert.Throws<ArgumentNullException>(() => Cs.Parse((Stream)null!));
        var error = Assert.Throws<ArgumentException>(() => Cs.Parse(new UnreadableStream()));
        Assert.Equal("source", error.ParamName);
        using var readable = new MemoryStream(Encoding.UTF8.GetBytes("class C { }"));
        Assert.Equal("class C\n{\n}\n", Cs.Parse(readable).Root.Render());
    }

    [Fact]
    public void Async_block_lambda_factory_marks_the_lambda_async_and_keeps_the_block_body()
    {
        var lambda = Cs.AsyncLambda(Cs.Body(Cs.Return()), Cs.LambdaParameter("x"));
        Assert.True(lambda.IsAsync);
        Assert.Equal(ExpressionKind.Lambda, lambda.Kind);
        Assert.Equal(StatementKind.Return, Assert.Single(lambda.LambdaBody!.Statements).Kind);
        Assert.Null(lambda.LambdaBody.Expression);
        Assert.Equal("x", Assert.Single(lambda.LambdaParameters).Name);
        var synchronous = Cs.Lambda(Cs.Body(Cs.Return()), Cs.LambdaParameter("x"));
        Assert.False(synchronous.IsAsync);
        Assert.NotEqual(synchronous, lambda);
    }

    [Fact]
    public void Opaque_builder_nodes_report_format001_at_the_origin_start()
    {
        // GENE-29 F4: every render formats, so every opaque node is reported, with or without the obsolete Reformat.
        var document = Cs.Document().Class("C").Add(Cs.RawMember("int x = 1;")).End().Build();
        Assert.Equal(document.EmissionDiagnostics, document.Reformat().EmissionDiagnostics);
        var diagnostic = Assert.Single(document.EmissionDiagnostics);
        Assert.Equal(("FORMAT001", DiagnosticSeverity.Warning, 0, 0, 1, 1), (diagnostic.Code, diagnostic.Severity, diagnostic.Offset, diagnostic.Length, diagnostic.Line, diagnostic.Column));
    }

    [Fact]
    public void Every_opaque_builder_kind_reports_format001()
    {
        var method = new CsMethod(Cs.Void, "M") { Body = Cs.Body(Cs.RawStatement("Run();"), Cs.Statement(Cs.RawExpression("Go()"))) };
        var raw = new CsMethod(Cs.Void, "N") { Body = Cs.RawBody("{ }") };
        var field = new CsField(Cs.RawType("int"), "F");
        var document = new CsDocument
        {
            Items = new CsNode[] { new CsRaw("// raw"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { method, raw, field, Cs.RawMember("int G;") } } }
        };
        Assert.Equal(6, document.EmissionDiagnostics.Count);
        Assert.All(document.EmissionDiagnostics, d => Assert.Equal("FORMAT001", d.Code));
    }

    [Fact]
    public void Opaque_parsed_statements_report_format001_with_line_and_column_across_mixed_newlines()
    {
        const string source = "class C\r\n{\r void M()\n {\n  return );\n }\n}";
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var raw = parsed.Root.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.Raw });
        Assert.NotNull(raw.Node);
        var diagnostic = Assert.Single(parsed.Root.EmissionDiagnostics);
        int offset = source.IndexOf("return );", StringComparison.Ordinal);
        Assert.Equal(("FORMAT001", offset, "return );".Length, 5, 3), (diagnostic.Code, diagnostic.Offset, diagnostic.Length, diagnostic.Line, diagnostic.Column));
    }

    [Fact]
    public void Document_paths_reject_negative_out_of_range_and_over_deep_segments()
    {
        var document = FieldDocument();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.At(new NodePath(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.At(new NodePath(0, 99)));
        Assert.Throws<ArgumentNullException>(() => document.At(null!));
        var deep = Assert.Throws<ArgumentException>(() => document.At(new NodePath(Enumerable.Repeat(0, 129).ToArray())));
        Assert.Equal("path", deep.ParamName);
        Assert.IsType<CsField>(document.At(new NodePath(0, 0)));
    }

    [Fact]
    public void Tree_paths_reject_negative_out_of_range_foreign_and_over_deep_segments()
    {
        var tree = Cs.Tree(Cs.Instruction(InstructionKind.Return));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.At(new NodePath(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.At(new NodePath(5)));
        Assert.Throws<ArgumentException>(() => tree.At(new NodePath(Enumerable.Repeat(0, 129).ToArray())));
        var foreign = Cs.Tree(Cs.Instruction(InstructionKind.Return)).Descendants().Last().Path;
        Assert.Throws<ArgumentException>(() => tree.At(foreign));
        Assert.IsType<CsInstruction>(tree.At(new NodePath(0)));
    }

    [Fact]
    public void Walking_detaching_and_validating_an_over_deep_graph_fails_with_the_depth_contract()
    {
        var document = FieldDocument(DeepNot(300));
        var walk = Assert.Throws<ArgumentException>(() => document.Descendants().ToList());
        Assert.Contains("exceeds", walk.Message);
        Assert.Throws<ArgumentException>(() => document.Detach());
        Assert.Contains(document.Validate(), d => d.Code == "GEN001");
        var tree = Cs.Tree(Cs.Symbol(new CsSymbolOptions { Value = DeepNot(300) }));
        Assert.Throws<ArgumentException>(() => tree.Descendants().ToList());
        Assert.Contains(tree.Validate(), d => d.Code == "GEN001");
    }

    [Fact]
    public void Document_root_cannot_be_removed_moved_or_replaced_by_a_non_document()
    {
        var document = FieldDocument();
        Assert.Equal("Cannot remove the document root.", Assert.Throws<ArgumentException>(() => document.Remove(NodePath.Root)).Message);
        Assert.Contains("own subtree", Assert.Throws<ArgumentException>(() => document.Move(NodePath.Root, new NodePath(0), 0)).Message);
        Assert.Contains("own subtree", Assert.Throws<ArgumentException>(() => document.Move(new NodePath(0), new NodePath(0, 0), 0)).Message);
        Assert.Equal("Root replacement must be a document.", Assert.Throws<ArgumentException>(() => document.Replace(NodePath.Root, new CsNamespace("N", Values<CsNode>.Empty))).Message);
        Assert.Throws<ArgumentNullException>(() => document.Replace(NodePath.Root, null!));
        Assert.Throws<ArgumentNullException>(() => document.Insert(NodePath.Root, 0, null!));
        Assert.Equal(document, document.Replace(NodePath.Root, document));
    }

    [Fact]
    public void Tree_root_cannot_be_removed_moved_or_replaced_by_a_non_tree()
    {
        var tree = Cs.Tree(Cs.Instruction(InstructionKind.Return));
        Assert.Throws<ArgumentException>(() => tree.Remove(NodePath.Root));
        Assert.Throws<ArgumentException>(() => tree.Move(NodePath.Root, NodePath.Root, 0));
        Assert.Throws<ArgumentException>(() => tree.Move(new NodePath(0), new NodePath(0), 0));
        Assert.Equal("Root replacement must be a document.", Assert.Throws<ArgumentException>(() => tree.Replace(NodePath.Root, FieldDocument())).Message);
        Assert.Throws<ArgumentNullException>(() => tree.Replace(NodePath.Root, null!));
        Assert.Throws<ArgumentNullException>(() => tree.Insert(NodePath.Root, 0, null!));
        Assert.Throws<ArgumentNullException>(() => tree.At(null!));
        var two = Cs.Tree(Cs.Instruction(InstructionKind.Empty), Cs.Instruction(InstructionKind.Return));
        Assert.IsType<CsInstruction>(Assert.Single(two.Remove(new NodePath(0)).Items));
        Assert.Equal(InstructionKind.Return, ((CsInstruction)two.Remove(new NodePath(0)).Items[0]).Kind);
    }

    [Fact]
    public void Insert_rejects_child_indexes_outside_the_parent()
    {
        var document = FieldDocument();
        var type = new NodePath(0);
        var member = new CsField(Cs.Int, "G");
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Insert(type, -1, member));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Insert(type, 99, member));
        Assert.Equal(new[] { "F", "G" }, document.Insert(type, 1, member).Types[0].Members.Select(m => m.Name));
        var tree = Cs.Tree(Cs.Instruction(InstructionKind.Return));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.Insert(NodePath.Root, -1, Cs.Instruction(InstructionKind.Return)));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.Insert(NodePath.Root, 2, Cs.Instruction(InstructionKind.Return)));
        Assert.Equal(2, tree.Insert(NodePath.Root, 1, Cs.Symbol("x", new() { AsVar = true, Value = Cs.Value(1) })).Items.Count);
    }

    [Fact]
    public void Edits_that_produce_an_invalid_graph_are_rejected_with_the_validation_errors()
    {
        var document = FieldDocument();
        var error = Assert.Throws<GenValidationException>(() => document.Replace(new NodePath(0, 0), new CsField(Cs.Int, "bad name")));
        Assert.Contains(error.Diagnostics, e => e.Code == "GEN003");
        var tree = Cs.Tree(Cs.Instruction(InstructionKind.Return));
        Assert.NotEmpty(Assert.Throws<GenValidationException>(() => tree.Replace(new NodePath(0), Cs.Symbol("refOnly"))).Diagnostics);
    }

    [Fact]
    public void Moving_a_node_out_of_an_earlier_sibling_shifts_the_destination_index()
    {
        var document = new CsDocument
        {
            Items = new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "A"), new CsNamespace("N", Values<CsNode>.Empty) }
        };
        var moved = document.Move(new NodePath(0), new NodePath(1), 0);
        var space = Assert.IsType<CsNamespace>(Assert.Single(moved.Items));
        Assert.Equal("A", Assert.IsType<CsTypeDeclaration>(Assert.Single(space.Items)).Name);

        var reversed = new CsDocument
        {
            Items = new CsNode[] { new CsNamespace("N", Values<CsNode>.Empty), new CsTypeDeclaration(TypeKind.Class, "A") }
        };
        var back = reversed.Move(new NodePath(1), new NodePath(0), 0);
        Assert.Equal("A", Assert.IsType<CsTypeDeclaration>(Assert.Single(Assert.IsType<CsNamespace>(Assert.Single(back.Items)).Items)).Name);

        var twoTypes = new CsDocument
        {
            Types = new[]
            {
                new CsTypeDeclaration(TypeKind.Class, "A") { Members = new CsMember[] { new CsField(Cs.Int, "F") } },
                new CsTypeDeclaration(TypeKind.Class, "B")
            }
        };
        var across = twoTypes.Move(new NodePath(0, 0), new NodePath(1), 0);
        Assert.Empty(across.Types[0].Members);
        Assert.Equal("F", Assert.Single(across.Types[1].Members).Name);
    }

    [Fact]
    public void Tree_moves_shift_destination_and_same_parent_indexes()
    {
        var tree = Cs.Tree(
            Cs.Symbol("a", new() { AsVar = true, Value = Cs.Value(1) }),
            Cs.Symbol("b", new() { AsVar = true, Value = Cs.Value(2) }),
            Cs.Instruction(InstructionKind.Return));
        var reordered = tree.Move(new NodePath(0), NodePath.Root, 2);
        Assert.Equal(new[] { "b", "a" }, reordered.Items.OfType<CsSymbol>().Select(s => s.Name));
        Assert.IsType<CsInstruction>(reordered.Items[2]);
        var earlier = tree.Move(new NodePath(1), NodePath.Root, 0);
        Assert.Equal(new[] { "b", "a" }, earlier.Items.OfType<CsSymbol>().Select(s => s.Name));
    }

    [Fact]
    public void Tree_moves_into_a_later_sibling_subtree_shift_the_destination_index()
    {
        var target = Cs.Compound(Cs.Value(1));
        var tree = Cs.Tree(Cs.Value(9), Cs.Symbol("Call", new() { AsCall = true, Arguments = target }));
        var parts = tree.Descendants().Single(n => ReferenceEquals(n.Node, target.Options)).Path;
        Assert.Equal(1, parts.Segments[0]);
        var moved = tree.Move(new NodePath(0), parts, 1);
        Assert.Single(moved.Items);
        Assert.Equal("Call(1, 9);", moved.Render().Trim());
    }

    [Fact]
    public void Rewrites_reject_replacements_of_the_wrong_kind_in_required_optional_and_list_slots()
    {
        var document = new CsDocument
        {
            Types = new[]
            {
                new CsTypeDeclaration(TypeKind.Class, "C")
                {
                    Members = new CsMember[] { new CsField(Cs.Int, "F") { Initializer = Cs.Literal(1) }, new CsMethod(Cs.Void, "M") { Body = Cs.Body() } }
                }
            }
        };
        var fieldType = document.Descendants().Single(n => n.Node is CsType { Name: "int" }).Path;
        var body = document.Descendants().Single(n => n.Node is CsBody).Path;
        Assert.Equal("Replacement has an invalid required child kind.", Assert.Throws<ArgumentException>(() => document.Replace(fieldType, Cs.Id("x"))).Message);
        Assert.Equal("Replacement has an invalid optional child kind.", Assert.Throws<ArgumentException>(() => document.Replace(body, Cs.Id("x"))).Message);
        Assert.Equal("Replacement has an invalid list child kind.", Assert.Throws<ArgumentException>(() => document.Replace(new NodePath(0, 0), Cs.Id("x"))).Message);
        var initializer = document.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal }).Path;
        var removed = document.Remove(initializer);
        Assert.Null(Assert.IsType<CsField>(removed.Types[0].Members[0]).Initializer);
        Assert.Contains("int F;", removed.Render());
    }

    [Fact]
    public void Cref_parameter_lists_are_children_and_survive_detach()
    {
        var parameter = new CsFunctionPointerParameter(Cs.Int, "ref");
        var withParameters = new CsCref(CrefKind.Name) { Type = Cs.Type("M"), Parameters = new Values<CsFunctionPointerParameter>(parameter) };
        Assert.Equal(new CsNode[] { withParameters.Type!, parameter }, withParameters.Children);
        var detached = Assert.IsType<CsCref>(withParameters.Detach());
        Assert.Equal(withParameters, detached);
        Assert.Equal("ref", Assert.Single(detached.Parameters!).Modifier);
        var bare = new CsCref(CrefKind.Name) { Type = Cs.Type("M") };
        Assert.Single(bare.Children);
        Assert.Null(Assert.IsType<CsCref>(bare.Detach()).Parameters);
    }

    [Fact]
    public void ToBytes_without_options_uses_strict_utf8_without_a_preamble_for_builder_documents()
    {
        var document = Cs.Document().Class("C").End().Build();
        Assert.Equal(Encoding.UTF8.GetBytes(document.Render()), document.ToBytes());
        Assert.Equal(Encoding.UTF8.GetBytes(document.Render()), document.ToBytes(new EmitOptions()));
    }

    [Fact]
    public void ToBytes_keeps_the_source_preamble_only_when_no_encoding_override_is_given()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var parsed = Cs.Parse(bom.Concat(Encoding.UTF8.GetBytes("class C { }")).ToArray()).Root;
        Assert.Equal(bom, parsed.ToBytes(new EmitOptions()).Take(3));
        Assert.Equal(bom, parsed.ToBytes().Take(3));
        var utf16 = parsed.ToBytes(new EmitOptions { Encoding = Encoding.Unicode });
        Assert.Equal(new byte[] { 0xFF, 0xFE }, utf16.Take(2));
        Assert.Equal("class C\n{\n}\n", Encoding.Unicode.GetString(utf16, 2, utf16.Length - 2));
        var latin = parsed.ToBytes(new EmitOptions { Encoding = Encoding.Latin1 });
        Assert.Equal(Encoding.Latin1.GetBytes("class C\n{\n}\n"), latin);
    }

    [Theory]
    [InlineData(1201, new byte[] { 0xFE, 0xFF })]
    [InlineData(12000, new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData(12001, new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    public void Forced_preambles_match_each_unicode_encoding(int codePage, byte[] preamble)
    {
        Encoding encoding = codePage switch
        {
            1201 => Encoding.BigEndianUnicode,
            12000 => Encoding.UTF32,
            _ => new UTF32Encoding(true, false)
        };
        var document = Cs.Document().Class("C").End().Build();
        var bytes = document.ToBytes(new EmitOptions { Encoding = encoding, EmitBom = true });
        Assert.Equal(preamble, bytes.Take(preamble.Length));
        Assert.Equal(document.Render(), encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length));
    }

    [Fact]
    public void WriteTo_rejects_null_and_read_only_destinations()
    {
        var document = Cs.Document().Class("C").End().Build();
        Assert.Throws<ArgumentNullException>(() => document.WriteTo(null!));
        using var readOnly = new MemoryStream(new byte[16], writable: false);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => document.WriteTo(readOnly)).ParamName);
        using var output = new MemoryStream();
        document.WriteTo(output);
        Assert.Equal(document.ToBytes(), output.ToArray());
    }
}
