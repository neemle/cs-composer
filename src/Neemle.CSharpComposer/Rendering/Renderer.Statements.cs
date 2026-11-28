using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Statement(CsStatement statement) => WithSyntaxTrivia(statement, () => StatementCore(statement));

    void StatementCore(CsStatement statement)
    {
        Comments(statement, CommentPlacement.Leading);
        int start = output.Length;
        switch (statement.Kind)
        {
            case StatementKind.DisabledText: Disabled(statement.Disabled!); break;
            case StatementKind.Directive: Directive(statement.Directive!); break;
            case StatementKind.LocalFunction: Member(statement.Function!); break;
            case StatementKind.Goto: Line("goto " + Names.Id(statement.Text) + ";"); break;
            case StatementKind.Label: Line(Names.Id(statement.Text) + ":"); SwitchBody(statement.Body!); break;
            case StatementKind.YieldReturn: Line("yield return " + Names.Expression(statement.Expression!) + ";"); break;
            case StatementKind.YieldBreak: Line("yield break;"); break;
            case StatementKind.Lock: Line("lock (" + Names.Expression(statement.Expression!) + ")"); Body(statement.Body); break;
            case StatementKind.Fixed: Line("fixed (" + Local(statement.Resource!) + ")"); Body(statement.Body); break;
            case StatementKind.Unsafe:
            case StatementKind.Checked:
            case StatementKind.Unchecked:
                Line(statement.Kind.ToString().ToLowerInvariant()); Body(statement.Body); break;
            case StatementKind.For:
            case StatementKind.ForEach:
            case StatementKind.While:
            case StatementKind.DoWhile:
                Loop(statement); break;
            case StatementKind.Continue: Line("continue" + (statement.Text == "" ? "" : " " + Names.Id(statement.Text)) + ";"); break;
            case StatementKind.Empty: Line(";"); break;
            case StatementKind.Switch: Switch(statement); break;
            case StatementKind.Break: Line("break" + (statement.Text == "" ? "" : " " + Names.Id(statement.Text)) + ";"); break;
            case StatementKind.GotoCase: Line("goto case " + Names.Expression(statement.Expression!) + ";"); break;
            case StatementKind.GotoDefault: Line("goto default;"); break;
            case StatementKind.Raw: Raw(statement.Text); break;
            case StatementKind.UsingDeclaration: Line((statement.IsAwait ? "await " : "") + "using " + Local(statement.Resource!) + ";"); break;
            case StatementKind.LocalGroup: Line(Local(statement) + ";"); break;
            case StatementKind.Local: Line(Local(statement) + ";"); break;
            case StatementKind.Block: Body(statement.Body); break;
            case StatementKind.If: If(statement); break;
            case StatementKind.Try: Try(statement); break;
            case StatementKind.Using: Using(statement); break;
            case StatementKind.Rethrow: Line("throw;"); break;
            default: SimpleStatement(statement); break;
        }
        Dangling(statement, start);
        Comments(statement, CommentPlacement.Trailing);
    }

    void Disabled(CsDisabledText text)
    {
        output.Append(SyntaxTriviaWriter.Protect(text));
        if (text.Text.Length > 0 && text.Text[^1] is not ('\r' or '\n')) output.Append('\n');
    }

    void SimpleStatement(CsStatement statement)
    {
        string keyword = statement.Kind == StatementKind.Return ? "return" :
            statement.Kind == StatementKind.Throw ? "throw" : "";
        Line(keyword + (keyword.Length != 0 && statement.Expression != null ? " " : "") +
            (statement.Expression == null ? "" : Names.Expression(statement.Expression)) + ";");
    }

    static string Local(CsStatement statement) => statement.Kind == StatementKind.LocalGroup ? LoopHeaderWriter.Initializer(statement.ForInitializer!) :
        LoopHeaderWriter.Modifiers(statement.VariableModifiers) + (statement.IsVar ? "var" : Names.Type(statement.Type!)) + " " + Names.Id(statement.Text) +
        Initializer(statement.Expression);

    // AD C3: an else branch that is an if statement with no comments or directives of its own continues the chain
    // (`else if`); any other embedded statement is braced.
    void If(CsStatement statement, string keyword = "if")
    {
        Line(keyword + " (" + Names.Expression(statement.Expression!) + ")");
        Body(statement.Body);
        if (statement.Else == null) return;
        // GENE-29 F5: an else branch's leading comments come before `else`, where its source span starts.
        Comments(statement.Else, CommentPlacement.Leading);
        var alternative = statement.Else with { Comments = new(statement.Else.Comments.Where(c => c.Placement != CommentPlacement.Leading)) };
        if (alternative is { Kind: StatementKind.If, Comments.Count: 0, SyntaxTrivia.Count: 0 }) { If(alternative, "else if"); return; }
        Line("else");
        if (alternative.Kind == StatementKind.Block) Body(alternative.Body);
        else { Open(); Statement(alternative); Close(); }
    }

    void Try(CsStatement statement)
    {
        Line("try");
        Body(statement.Body);
        foreach (var handler in statement.Catches) Catch(handler);
        if (statement.Finally == null) return;
        Line("finally");
        Body(statement.Finally);
    }

    // One catch clause with its type, variable and filter; a catch rendered on its own uses the same text.
    void Catch(CsCatch handler) => WithSyntaxTrivia(handler, () =>
    {
        Comments(handler, CommentPlacement.Leading);
        Line("catch" + (handler.Type == null ? "" : " (" + Names.Type(handler.Type) +
            (handler.Name == null ? "" : " " + Names.Id(handler.Name)) + ")") +
            (handler.Filter == null ? "" : " when (" + Names.Expression(handler.Filter) + ")"));
        Body(handler.Body);
        Comments(handler, CommentPlacement.Trailing);
    });

    void Using(CsStatement statement)
    {
        string resource = statement.Resource == null ? Names.Expression(statement.Expression!) :
            Local(statement.Resource);
        Line((statement.IsAwait ? "await " : "") + "using (" + resource + ")");
        Body(statement.Body);
    }
}
