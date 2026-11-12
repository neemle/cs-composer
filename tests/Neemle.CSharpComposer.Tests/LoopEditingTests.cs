using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LoopEditingTests
{
    [Theory]
    [InlineData("for (int i = 1; i < 9; i++) { Run(i); }", "i < 9", "i < 8")]
    [InlineData("foreach (int i in source) { Run(9); }", "Run(9)", "Run(8)")]
    [InlineData("while (i < 9) Run(i);", "i < 9", "i < 8")]
    [InlineData("do { Run(i); } while (i < 9);", "i < 9", "i < 8")]
    public void Bound_leaf_edits_preserve_external_text_spacing_comments_and_crlf(string loop, string before, string after)
    {
        string source = "// file\r\nclass C { void M() { /* before */ " + loop + " /* after */ Tail(); } }\r\n";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        var selected = parsed.Root.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "9" });
        var edited = parsed.Root.Replace(selected.Path, (CsExpression)selected.Node with { Text = "8" });
        Assert.Equal(source.Replace(before, after), edited.Render());
    }

    [Fact]
    public void Declarations_and_foreach_names_have_scalar_bindings_and_do_children_keep_lexical_order()
    {
        const string source = "class C { void M() { for (int item = 0; ready; Tick()) {} foreach (var other in source) {} do {} while (ready); } }";
        var parsed = Cs.Parse(source).Root;
        var variable = parsed.Descendants().Single(n => n.Node is CsVariableDeclarator);
        var renamed = parsed.Replace(variable.Path, (CsVariableDeclarator)variable.Node with { Name = "entry" });
        Assert.Equal(source.Replace("int item", "int entry"), renamed.Render());
        var each = renamed.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.ForEach });
        var named = renamed.Replace(each.Path, (CsStatement)each.Node with { Text = "next" });
        Assert.Equal(source.Replace("int item", "int entry").Replace("var other", "var next"), named.Render());
        var condition = named.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.DoWhile });
        var @do = (CsStatement)condition.Node;
        Assert.Same(@do.Body, @do.Children[0]);
        Assert.Same(@do.Expression, @do.Children[1]);
        var changed = named.Replace(condition.Path.Child(1), @do.Expression! with { Text = "done" });
        Assert.Contains("while (done);", changed.Render());
        Assert.Contains("do {}", changed.Render());
    }

    [Fact]
    public void Header_lists_and_bodies_can_be_inserted_removed_moved_and_reformatted()
    {
        var parsed = Cs.Parse("class C { void M() { for(int i = 0; ready; i++) { Run(); } } }").Root;
        var initializer = parsed.Descendants().Single(n => n.Node is CsForInitializer);
        var added = parsed.Insert(initializer.Path, initializer.Node.Children.Count, Cs.Variable("j", Cs.Literal(1)));
        Assert.Contains("int i = 0, j = 1", added.Render());
        var variable = added.Descendants().Single(n => n.Node is CsVariableDeclarator { Name: "j" });
        var removed = added.Remove(variable.Path);
        Assert.DoesNotContain("j = 1", removed.Render());
        var loop = removed.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.For });
        int bodyIndex = loop.Node.Children.Count - 1;
        var withIterator = removed.Insert(loop.Path, bodyIndex, Cs.Id("Tick").Call());
        Assert.Contains("i++, Tick()", withIterator.Render());
        var @for = withIterator.Descendants().Single(n => n.Node is CsStatement { Kind: StatementKind.For });
        var body = @for.Path.Child(@for.Node.Children.Count - 1);
        var withStatement = withIterator.Insert(body, 0, Cs.If(Cs.Id("skip"), Cs.Body(Cs.Continue())));
        Assert.Contains("continue;", withStatement.Render());
        Assert.Empty(Cs.Parse(withStatement.Reformat().Render()).Diagnostics);

        var expressions = Cs.Parse("class C { void M() { for(Reset(), Set(); ready; Tick()) {} } }").Root;
        var init = expressions.Descendants().Single(n => n.Node is CsForInitializer);
        var first = expressions.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments[0].Text == "Reset");
        var moved = expressions.Move(first.Path, init.Path, 2);
        Assert.Contains("Set(), Reset()", moved.Render());
    }

    [Fact]
    public void Header_and_body_comments_survive_reformatting()
    {
        const string source = "class C { void M() {\n// loop\nfor (/* type */ int i = /* value */ 0; /* condition */ i < 3; /* step */ i++) {\n// body\ncontinue;\n}\n// tail\ndo { Tick(); } /* test */ while (ready);\n} }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        foreach (string comment in new[] { "// loop", "/* type */", "/* value */", "/* condition */", "/* step */", "// body", "// tail", "/* test */" })
        {
            Assert.Contains(comment, formatted);
            Assert.Equal(1, formatted.Split(comment, StringSplitOptions.None).Length - 1);
        }
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
    }
}
