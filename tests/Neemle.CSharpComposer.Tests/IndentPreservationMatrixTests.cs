using System.Text;
using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
public sealed class IndentPreservationMatrixTests
{
    static readonly (string Name, string L1, string L2, string L3)[] Styles =
    {
        ("tab", "\t", "\t\t", "\t\t\t"),
        ("2sp", "  ", "    ", "      "),
        ("3sp", "   ", "      ", "         "),
        ("4sp", "    ", "        ", "            "),
        ("8sp", "        ", "                ", "                        "),
        ("mixed", "\t", "\t  ", "\t      ")
    };

    static readonly string[] Operations =
    {
        "RemoveM", "InsertStart", "InsertEnd", "MoveSameDepth", "MoveDeep", "ReplaceK",
        "RemoveNested", "RemoveStatement", "ReformatR", "ReformatM", "Unbound"
    };

    public static IEnumerable<object[]> LfCells() =>
        from style in Styles from operation in Operations select new object[] { style.Name, operation };

    public static IEnumerable<object[]> NewlineCells() =>
        from style in Styles
        from operation in Operations
        from newline in new[] { "\r\n", "\r" }
        select new object[] { style.Name, operation, newline };

    [Theory]
    [MemberData(nameof(LfCells))]
    public void Lf_cell_uses_destination_indentation(string style, string operation) =>
        AssertCell(style, operation, "\n");

    [Theory]
    [MemberData(nameof(NewlineCells))]
    public void Crlf_and_cr_cells_use_destination_indentation_and_newline(string style, string operation,
        string newline) =>
        AssertCell(style, operation, newline);

    internal static void AssertCell(string styleName, string operation, string newline)
    {
        var style = Styles.Single(s => s.Name == styleName);
        var lines = SourceLines(style);
        string source = Join(lines, newline);
        var (expectedLines, first, last) = Expected(operation, style, lines);
        string output = Apply(Cs.Parse(source).Root, operation).Render();
        Assert.Equal(Join(expectedLines, newline), output);
        AssertOutsideEnvelope(source, output, Offset(lines, first, newline), Offset(lines, last, newline));
        AssertNewlines(output, newline);
        var reparsed = Cs.Parse(output);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(output, reparsed.Root.Render());
        if (operation.StartsWith("Reformat", StringComparison.Ordinal))
            Assert.Equal(output, Apply(reparsed.Root, operation).Render());
    }

    internal static List<string> SourceLines((string Name, string L1, string L2, string L3) s) => new()
    {
        "namespace N", "{",
        s.L1 + "class A", s.L1 + "{",
        s.L2 + "int M() => 1;", s.L2 + "int K() => 2;", s.L2 + "int R(){return 1;}",
        s.L2 + "int F()", s.L2 + "{", s.L3 + "R();", s.L3 + "return 1;", s.L2 + "}",
        s.L1 + "}",
        s.L1 + "class B", s.L1 + "{",
        s.L2 + "int Z() => 3;", s.L2 + "class C", s.L2 + "{",
        s.L3 + "int P() => 4;", s.L3 + "int Q() => 5;", s.L2 + "}",
        s.L1 + "}",
        "}"
    };

    static (List<string> Lines, int First, int Last) Expected(string operation,
        (string Name, string L1, string L2, string L3) s, List<string> source)
    {
        var lines = new List<string>(source);
        string unit = s.L2[s.L1.Length..];
        switch (operation)
        {
            case "RemoveM": lines.RemoveAt(4); return (lines, 4, 5);
            case "InsertStart": lines.Insert(4, s.L2 + "int G() => 9;"); return (lines, 4, 4);
            case "InsertEnd":
                lines.InsertRange(12, new[] { s.L2 + "int H()", s.L2 + "{", s.L2 + unit + "return 7;", s.L2 + "}" });
                return (lines, 12, 12);
            case "MoveSameDepth": lines.Insert(21, s.L2 + "int M() => 1;"); lines.RemoveAt(4); return (lines, 4, 21);
            case "MoveDeep": lines.RemoveAt(19); lines.Insert(12, s.L2 + "int Q() => 5;"); return (lines, 12, 20);
            case "ReplaceK": lines[5] = s.L2 + "int K2() => 22;"; return (lines, 5, 6);
            case "RemoveNested": lines.RemoveAt(18); return (lines, 18, 19);
            case "RemoveStatement": lines.RemoveAt(9); return (lines, 9, 10);
            case "ReformatR":
                lines.RemoveAt(6);
                lines.InsertRange(6, new[] { s.L2 + "int R()", s.L2 + "{", s.L2 + "    return 1;", s.L2 + "}" });
                return (lines, 6, 7);
            case "ReformatM": return (lines, 4, 5);
            default: lines[7] = s.L2 + "public int F()"; return (lines, 7, 12);
        }
    }

    internal static CsDocument Apply(CsDocument document, string operation)
    {
        var a = Find(document, "A");
        var b = Find(document, "B");
        return operation switch
        {
            "RemoveM" => document.Remove(Find(document, "M").Path),
            "InsertStart" => document.Insert(a.Path, IndexOf(a.Node, "M"), Cs.RawMember("int G() => 9;")),
            "InsertEnd" => document.Insert(a.Path, a.Node.Children.Count,
                new CsMethod(Cs.Type<int>(), "H") { Body = Cs.Body(Cs.Return(Cs.Literal(7))) }),
            "MoveSameDepth" => document.Move(Find(document, "M").Path, b.Path, b.Node.Children.Count),
            "MoveDeep" => document.Move(Find(document, "Q").Path, a.Path, a.Node.Children.Count),
            "ReplaceK" => document.Replace(Find(document, "K").Path, Cs.RawMember("int K2() => 22;")),
            "RemoveNested" => document.Remove(Find(document, "P").Path),
            "RemoveStatement" => document.Remove(document.Descendants()
                .First(x => x.Node is CsStatement { Kind: StatementKind.Expression }).Path),
            "ReformatR" => document.Reformat(Find(document, "R").Path),
            "ReformatM" => document.Reformat(Find(document, "M").Path),
            _ => document.Replace(Find(document, "F").Path,
                (CsMethod)Find(document, "F").Node with { Modifiers = new[] { "public" } })
        };
    }

    internal static (NodePath Path, CsNode Node) Find(CsDocument document, string name) =>
        document.Descendants().Single(x => x.Node is CsMember { Name: var n } && n == name ||
            x.Node is CsTypeDeclaration { Name: var t } && t == name);

    static int IndexOf(CsNode parent, string name) =>
        parent.Children.ToList().FindIndex(c => c is CsMember { Name: var n } && n == name);

    internal static string Join(IEnumerable<string> lines, string newline) =>
        string.Concat(lines.Select(line => line + newline));

    static int Offset(List<string> lines, int line, string newline) =>
        lines.Take(line).Sum(text => text.Length + newline.Length);

    internal static void AssertOutsideEnvelope(string source, string output, int start, int end)
    {
        byte[] before = Encoding.UTF8.GetBytes(source), after = Encoding.UTF8.GetBytes(output);
        int head = Encoding.UTF8.GetByteCount(source[..start]), tail = Encoding.UTF8.GetByteCount(source[end..]);
        Assert.True(after.Length >= head + tail, "output is shorter than the untouched source bytes");
        Assert.Equal(before[..head], after[..head]);
        Assert.Equal(before[^tail..], after[^tail..]);
    }

    internal static void AssertNewlines(string output, string newline)
    {
        string other = output.Replace(newline, "", StringComparison.Ordinal);
        Assert.False(other.Contains('\n') || other.Contains('\r'), "output mixes newline sequences");
    }

    // Integration: these tests build or run composed source with the real compiler host.
    [Trait("Category", "Integration")]
    public sealed class Compiler
    {
        [Theory]
        [InlineData("\n", "RemoveM,InsertEnd,MoveDeep,ReplaceK,ReformatR,Unbound")]
        [InlineData("\r\n", "RemoveM,InsertEnd,MoveDeep,ReplaceK,ReformatR,Unbound")]
        public async Task Compiler_accepts_representative_cells(string newline, string operations)
        {
            var program = new StringBuilder("class Program { static void Main() { } }" + newline);
            int index = 0;
            foreach (var style in Styles)
                foreach (string operation in operations.Split(','))
                {
                    string output = Apply(Cs.Parse(Join(SourceLines(style), newline)).Root, operation).Render();
                    program.Append("namespace N" + index++ + output["namespace N".Length..]);
                }
            Assert.Empty(await CompilerFixture.Build(program.ToString(), "14.0"));
        }
    }
}
