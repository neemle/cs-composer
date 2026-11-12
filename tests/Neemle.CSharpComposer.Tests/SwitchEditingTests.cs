using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SwitchEditingTests
{
    [Fact]
    public void Leaf_edits_keep_switch_spacing_comments_and_everything_outside_the_selection()
    {
        const string source = "// file\r\nclass C { int M(int x) { switch ( x ) { case 1: /* one */ return 10; default: return 0; } } }\r\n";
        var parsed = Cs.Parse(source).Root;
        var selected = parsed.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "10" });
        var edited = parsed.Replace(selected.Path, (CsExpression)selected.Node with { Text = "20" });
        Assert.Equal(source.Replace("return 10", "return 20"), edited.Render());
        var label = edited.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "1" });
        var relabeled = edited.Replace(label.Path, (CsExpression)label.Node with { Text = "2" });
        Assert.Equal(source.Replace("return 10", "return 20").Replace("case 1", "case 2"), relabeled.Render());
        Assert.Contains("/* one */", relabeled.Reformat().Render());
    }

    [Fact]
    public void Inserting_moving_removing_and_reformatting_sections_and_arms_preserves_typed_children()
    {
        var parsed = Cs.Parse("class C { int M(int x) { switch(x) { case 1: return 10; default: return 0; } } }").Root;
        var selected = parsed.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.Switch });
        var inserted = parsed.Insert(selected.Path, 2, Cs.Section(Cs.Body(Cs.Return(Cs.Literal(20))), Cs.Case(Cs.Literal(2))));
        Assert.Contains("case 2:", inserted.Render());
        var section = inserted.Descendants().Single(n => n.Node is CsSwitchSection s && s.Labels[0].Pattern?.Value?.Text == "2");
        var switchPathAfterInsert = inserted.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.Switch }).Path;
        var moved = inserted.Move(section.Path, switchPathAfterInsert, 1);
        Assert.True(moved.Render().IndexOf("case 2:", StringComparison.Ordinal) < moved.Render().IndexOf("case 1:", StringComparison.Ordinal));
        var remove = moved.Descendants().Single(n => n.Node is CsSwitchSection s && s.Labels[0].Pattern?.Value?.Text == "2");
        var removed = moved.Remove(remove.Path);
        Assert.DoesNotContain("case 2:", removed.Render());
        Assert.Empty(Cs.Parse(removed.Render()).Diagnostics);

        var expression = Cs.Parse("class C { int M(int x) => x switch { 1 => 10, _ => 0 }; }").Root;
        var switchPath = expression.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Switch }).Path;
        var added = expression.Insert(switchPath, 2, Cs.Arm(Cs.ConstantPattern(Cs.Literal(2)), Cs.Literal(20)));
        Assert.Contains("2 => 20", added.Render());
        var arm = added.Descendants().Single(n => n.Node is CsSwitchArm a && a.Pattern.Value?.Text == "2");
        var replaced = added.Replace(arm.Path, (CsSwitchArm)arm.Node with { Value = Cs.Literal(30) });
        Assert.Contains("2 => 30", replaced.Render());
        Assert.Empty(Cs.Parse(replaced.Render()).Diagnostics);
    }

    [Fact]
    public void New_arm_and_section_comments_survive_reformatting_and_edits()
    {
        const string source = "class C { int M(int x) => x switch {\n// positive\n1 => 10,\n// fallback\n_ => 0\n// end\n}; }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        Assert.Contains("// positive", formatted);
        Assert.Contains("// fallback", formatted);
        Assert.Contains("// end", formatted);
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
    }

    [Fact]
    public void Editing_a_section_statement_list_does_not_introduce_a_block_scope()
    {
        var parsed = Cs.Parse("class C { int M(int x) { switch(x) { case 1: int n = 10; return n; default: return 0; } } }").Root;
        var body = parsed.Descendants().Single(n => n.Node is CsSwitchSection s && s.Labels[0].Pattern != null);
        var section = (CsSwitchSection)body.Node;
        var bodyPath = body.Path.Child(section.Children.Count - 1);
        var inserted = parsed.Insert(bodyPath, 1, Cs.Local(Cs.Int, "another", Cs.Literal(5)));
        string source = inserted.Render();
        Assert.DoesNotContain("case 1: {", source);
        Assert.DoesNotContain("case 1:\n", source); // Existing label spacing is untouched.
        var reparsed = Cs.Parse(source);
        Assert.Empty(reparsed.Diagnostics);
        var result = (CsSwitchSection)reparsed.Root.Descendants().First(n => n.Node is CsSwitchSection).Node;
        Assert.Equal(StatementKind.Local, result.Body.Statements[0].Kind);
        Assert.Equal(3, result.Body.Statements.Count);
        Assert.Equal(StatementKind.Local, ((CsSwitchSection)Cs.Parse(parsed.Reformat(bodyPath).Render()).Root
            .Descendants().First(n => n.Node is CsSwitchSection).Node).Body.Statements[0].Kind);
    }
}
