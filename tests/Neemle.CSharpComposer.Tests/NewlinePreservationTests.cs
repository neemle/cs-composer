using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

public sealed class NewlinePreservationTests
{
    static readonly string[] Shape =
    {
        "class C", "{", "    int  A( ){return 1;}", "    int B() => 2;", "    int D()", "    {", "        return 3;",
        "    }", "    class N", "    {", "        int E() => 5;", "    }", "}"
    };

    static readonly string[] Rows =
    {
        "InsertEnd", "InsertStart", "InsertNested", "InsertStatement", "InsertAfterB", "ReplaceMultiline", "ReplaceRaw",
        "MoveBeforeA", "MoveIntoN", "RemoveB", "ReformatA", "ReformatD", "ReformatN", "UnboundA", "UnboundD", "Rename"
    };

    static readonly (string Name, string Default, string B)[] Variants =
    {
        ("LF", "\n", "\n"), ("CRLF", "\r\n", "\r\n"), ("CR", "\r", "\r"), ("MIX1", "\r\n", "\n"), ("MIX2", "\n", "\r\n")
    };

    const string Reformatted = "class C\n{\n    int A()\n    {\n        return 1;\n    }\n\n    int B() => 2;\n\n" +
        "    int D()\n    {\n        return 3;\n    }\n\n    class N\n    {\n        int E() => 5;\n    }\n}\n";

    public static IEnumerable<object[]> Cells() =>
        from variant in Variants from row in Rows select new object[] { variant.Name, row };

    [Theory]
    [MemberData(nameof(Cells))]
    public void Synthesized_breaks_follow_the_destination_newline(string variantName, string row)
    {
        var variant = Variants.Single(v => v.Name == variantName);
        var lines = Shape.Select((text, i) => (Text: text, Break: i == 3 ? variant.B : variant.Default)).ToList();
        string source = Concat(lines);
        var (expected, first, last) = Expected(row, lines, variant.Default, variant.B);
        string output = Apply(Cs.Parse(source).Root, row).Render();
        Assert.Equal(Concat(expected), output);
        IndentPreservationMatrixTests.AssertOutsideEnvelope(source, output, Offset(lines, first), Offset(lines, last));
        if (variant.Default == variant.B) IndentPreservationMatrixTests.AssertNewlines(output, variant.Default);
        var reparsed = Cs.Parse(output);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(output, reparsed.Root.Render());
        if (row.StartsWith("Reformat", StringComparison.Ordinal))
            Assert.Equal(output, Apply(reparsed.Root, row).Render());
    }

    static (List<(string Text, string Break)> Lines, int First, int Last) Expected(string row,
        List<(string Text, string Break)> source, string d, string b)
    {
        var lines = new List<(string Text, string Break)>(source);
        void Put(int at, string newline, params string[] texts) =>
            lines.InsertRange(at, texts.Select(t => (t, newline)));
        switch (row)
        {
            case "InsertEnd": Put(12, d, "    int F() => 9;"); return (lines, 12, 12);
            case "InsertStart": Put(2, d, Method("    ", "int G()", 7)); return (lines, 2, 2);
            case "InsertNested": Put(11, d, Method("        ", "int H()", 8)); return (lines, 11, 11);
            case "InsertStatement": Put(6, d, "        x();"); return (lines, 6, 6);
            case "InsertAfterB": Put(4, b, "    int F() => 9;"); return (lines, 4, 4);
            case "ReplaceMultiline": lines.RemoveAt(3); Put(3, b, Method("    ", "int B2()", 22)); return (lines, 3, 4);
            case "ReplaceRaw": lines[3] = ("    int B3() => 33;", b); return (lines, 3, 4);
            case "MoveBeforeA":
                lines.InsertRange(2, source.Skip(4).Take(4));
                lines.RemoveRange(8, 4);
                return (lines, 2, 8);
            case "MoveIntoN":
                lines.InsertRange(11, source.Skip(4).Take(4).Select(l => ("    " + l.Text, l.Break)));
                lines.RemoveRange(4, 4);
                return (lines, 4, 11);
            case "RemoveB": lines.RemoveAt(3); return (lines, 3, 4);
            case "ReformatA": lines.RemoveAt(2); Put(2, d, Method("    ", "int A()", 1)); return (lines, 2, 3);
            case "ReformatD": return (lines, 4, 8);
            case "ReformatN": return (lines, 8, 12);
            case "UnboundA": lines.RemoveAt(2); Put(2, d, Method("    ", "long A()", 1)); return (lines, 2, 3);
            case "UnboundD": lines[4] = ("    long D()", d); return (lines, 4, 8);
            default: lines[2] = ("    int  Renamed( ){return 1;}", d); return (lines, 2, 3);
        }
    }

    static string[] Method(string indent, string header, int value) =>
        new[] { indent + header, indent + "{", indent + "    return " + value + ";", indent + "}" };

    static CsDocument Apply(CsDocument document, string row)
    {
        var c = Find(document, "C");
        var n = Find(document, "N");
        CsMethod Method(string name, int value) =>
            new(Cs.Type<int>(), name) { Body = Cs.Body(Cs.Return(Cs.Literal(value))) };
        CsMethod Member(string name) => (CsMethod)Find(document, name).Node;
        return row switch
        {
            "InsertEnd" => document.Insert(c.Path, c.Node.Children.Count, Cs.RawMember("int F() => 9;")),
            "InsertStart" => document.Insert(c.Path, IndexOf(c.Node, "A"), Method("G", 7)),
            "InsertNested" => document.Insert(n.Path, n.Node.Children.Count, Method("H", 8)),
            "InsertStatement" => document.Insert(BodyOf(document, "D"), 0, Cs.RawStatement("x();")),
            "InsertAfterB" => document.Insert(c.Path, IndexOf(c.Node, "D"), Cs.RawMember("int F() => 9;")),
            "ReplaceMultiline" => document.Replace(Find(document, "B").Path, Method("B2", 22)),
            "ReplaceRaw" => document.Replace(Find(document, "B").Path, Cs.RawMember("int B3() => 33;")),
            "MoveBeforeA" => document.Move(Find(document, "D").Path, c.Path, IndexOf(c.Node, "A")),
            "MoveIntoN" => document.Move(Find(document, "D").Path, n.Path, n.Node.Children.Count),
            "RemoveB" => document.Remove(Find(document, "B").Path),
            "ReformatA" or "ReformatD" or "ReformatN" => document.Reformat(Find(document, row[^1..]).Path),
            "UnboundA" or "UnboundD" => document.Replace(Find(document, row[^1..]).Path,
                Member(row[^1..]) with { ReturnType = Cs.Type<long>() }),
            _ => document.Replace(Find(document, "A").Path, Member("A") with { Name = "Renamed" })
        };
    }

    [Fact]
    public void A1_scoped_reformat_in_crlf_source_emits_crlf()
    {
        var document = Cs.Parse("class C\r\n{\r\n    int  A( ){return 1;}\r\n    int B() => 2;\r\n}\r\n").Root;
        Assert.Equal("class C\r\n{\r\n    int A()\r\n    {\r\n        return 1;\r\n    }\r\n    int B() => 2;\r\n}\r\n",
            document.Reformat(Find(document, "A").Path).Render());
    }

    [Theory]
    [InlineData("\r\n", "\r\n")]
    [InlineData("\r", "\r")]
    [InlineData("MIX1", "\r\n")]
    [InlineData("MIX2", "\n")]
    public void A4_whole_document_reformat_uses_the_dominant_newline_and_is_idempotent(string variant, string dominant)
    {
        var (d, b) = variant == "MIX1" ? ("\r\n", "\n") : variant == "MIX2" ? ("\n", "\r\n") : (variant, variant);
        string source = string.Concat(Shape.Select((text, i) => text + (i == 3 ? b : d)));
        string output = Cs.Parse(source).Root.Reformat().Render();
        Assert.Equal(Reformatted.Replace("\n", dominant, StringComparison.Ordinal), output);
        Assert.Equal(output, Cs.Parse(output).Root.Reformat().Render());
        Assert.Equal("class C\n{\n    int A;\n}\n",
            Cs.Parse("class C\n{\r\n    int A;\n}\r\n").Root.Reformat().Render());
        Assert.DoesNotContain('\r', Cs.Document().Class("C").End().Build().Render());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    public void A5_literal_and_comment_payloads_are_never_converted(string payload)
    {
        string source = ("class C\r\n{\r\n    /* c" + payload + "d */\r\n    string P()\r\n    {\r\n" +
            "        var v = @\"a" + payload + "b\";\r\n" +
            "        var r = \"\"\"" + payload + "  e" + payload + "  \"\"\";\r\n" +
            "        return v + r;\r\n    }\r\n    class N\r\n    {\r\n    }\r\n}\r\n");
        var document = Cs.Parse(source).Root;
        var p = Find(document, "P");
        var n = Find(document, "N");
        var raw = Cs.RawMember("/* c" + payload + "d */ string P() { var v = @\"a" + payload + "b\"; var r = \"\"\"" +
            payload + "  e" + payload + "  \"\"\"; return v + r; }");
        var outputs = new[]
        {
            document.Reformat(p.Path).Render(),
            document.Replace(p.Path, (CsMethod)p.Node with { ReturnType = Cs.Type<object>() }).Render(),
            document.Insert(n.Path, 0, raw).Render(),
            document.Replace(p.Path, raw).Render(),
            document.Move(p.Path, n.Path, 0).Render(),
            document.Reformat().Render()
        };
        string[] payloads =
        {
            "/* c" + payload + "d */", "@\"a" + payload + "b\"", "\"\"\"" + payload + "  e" + payload + "  \"\"\""
        };
        foreach (string output in outputs)
        {
            string layout = payloads.Aggregate(output, (text, kept) => text.Contains(kept, StringComparison.Ordinal)
                ? text.Replace(kept, "", StringComparison.Ordinal)
                : throw new Xunit.Sdk.XunitException(kept + " lost in " + output));
            IndentPreservationMatrixTests.AssertNewlines(layout, "\r\n");
        }
    }

    static readonly (string Name, string Value, string Payload)[] FirstLinePayloads =
    {
        ("verbatim", "@\"x\ny\"", "@\"x\ny\""),
        ("raw", "\"\"\"\n  x\n  \"\"\"", "\"\"\"\n  x\n  \"\"\""),
        ("interpolated", "$@\"x\ny\"", "$@\"x\ny\""),
        ("escape", "\"x\\ny\"", "\"x\\ny\""),
        ("char", "'\\n'", "'\\n'"),
        ("block", "/* a\nb */ \"\"", "/* a\nb */"),
        ("line", "\"\" // c{nl}        ", "// c")
    };

    public static IEnumerable<object[]> FirstLinePayloadCells() =>
        from payload in FirstLinePayloads
        from newline in new[] { "\r\n", "\r" }
        from operation in new[] { "Reformat", "Unbound", "Statement" }
        select new object[] { payload.Name, newline, operation };

    [Theory]
    [MemberData(nameof(FirstLinePayloadCells))]
    public void A5_payload_on_the_first_line_does_not_choose_the_newline(string kind, string newline, string operation)
    {
        var payload = FirstLinePayloads.Single(p => p.Name == kind);
        string value = payload.Value.Replace("{nl}", newline, StringComparison.Ordinal);
        string source = operation == "Statement"
            ? "class A" + newline + "{" + newline + "    string S()" + newline + "    {" + newline +
                "        var v = " + value + ";" + newline + "        return v;" + newline + "    }" + newline +
                "}" + newline
            : "class A" + newline + "{" + newline + "    string S(){return " + value + ";}" + newline + "}" + newline;
        var document = Cs.Parse(source).Root;
        var s = Find(document, "S");
        var edited = operation switch
        {
            "Reformat" => document.Reformat(s.Path),
            "Unbound" => document.Replace(s.Path, (CsMethod)s.Node with { Modifiers = new[] { "public" } }),
            _ => document.Reformat(document.Descendants().First(x => x.Node is CsStatement).Path)
        };
        string output = edited.Render();
        Assert.Contains(payload.Payload, output, StringComparison.Ordinal);
        Assert.Empty(Cs.Parse(output).Diagnostics);
        IndentPreservationMatrixTests.AssertNewlines(output.Replace(payload.Payload, "", StringComparison.Ordinal),
            newline);
    }

    [Fact]
    public void A5_dominant_newline_ignores_line_breaks_inside_literals()
    {
        const string source = "class C\r\n{\r\n    string V = @\"a\nb\nc\nd\ne\nf\";\r\n}\r\n";
        Assert.Equal(source, Cs.Parse(source).Root.Reformat().Render());
    }

    [Fact]
    public void A6_cr_move_reindents_every_line_and_keeps_cr()
    {
        var document = Cs.Parse(string.Concat(Shape.Select(line => line + "\r"))).Root;
        var n = Find(document, "N");
        Assert.Equal("class C\r{\r    int  A( ){return 1;}\r    int B() => 2;\r    class N\r    {\r" +
            "        int E() => 5;\r" +
            "        int D()\r        {\r            return 3;\r        }\r    }\r}\r",
            document.Move(Find(document, "D").Path, n.Path, n.Node.Children.Count).Render());
    }

    [Fact]
    public void Retained_unicode_line_separator_is_kept_and_never_emitted()
    {
        const string source = "class C\r\n{\r\n    string V = @\"a\u2028b\";\r\n    int  A( ){return 1;}\r\n" +
            "    int B() => 2;\r\n}\r\n";
        var document = Cs.Parse(source).Root;
        var reformatted = document.Reformat(Find(document, "A").Path);
        string output = reformatted.Remove(Find(reformatted, "B").Path).Render();
        Assert.Equal("class C\r\n{\r\n    string V = @\"a\u2028b\";\r\n    int A()\r\n    {\r\n        return 1;\r\n" +
            "    }\r\n}\r\n", output);
    }

    static (NodePath Path, CsNode Node) Find(CsDocument document, string name) =>
        IndentPreservationMatrixTests.Find(document, name);

    static NodePath BodyOf(CsDocument document, string name)
    {
        var member = Find(document, name);
        return member.Path.Child(member.Node.Children.ToList().FindIndex(child => child is CsBody));
    }

    static int IndexOf(CsNode parent, string name) =>
        parent.Children.ToList().FindIndex(child => child is CsMember { Name: var n } && n == name);

    static string Concat(IEnumerable<(string Text, string Break)> lines) =>
        string.Concat(lines.Select(l => l.Text + l.Break));

    static int Offset(List<(string Text, string Break)> lines, int line) =>
        lines.Take(line).Sum(l => l.Text.Length + l.Break.Length);
}
