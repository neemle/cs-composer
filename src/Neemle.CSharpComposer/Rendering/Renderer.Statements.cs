using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Statement(CsStatement statement) => WithSyntaxTrivia(statement, () => StatementCore(statement));

    void StatementCore(CsStatement statement)
    {
        Comments(statement, CommentPlacement.Leading);
        foreach (var comment in statement.Comments)
            if (comment.Placement == CommentPlacement.Container && !BeforeElse(statement, comment)) CommentNode(comment);
        switch (statement.Kind)
        {
            case StatementKind.DisabledText: Disabled(statement.Disabled!); break;
            case StatementKind.Directive: output.Append(SyntaxTriviaWriter.Protect(statement.Directive!, includeComments: false)).Append('\n'); break;
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

    void If(CsStatement statement)
    {
        Line("if (" + Names.Expression(statement.Expression!) + ")");
        Body(statement.Body);
        if (statement.Else == null) return;
        foreach (var comment in statement.Comments)
            if (comment.Placement == CommentPlacement.Container && BeforeElse(statement, comment)) CommentNode(comment);
        Line("else");
        if (statement.Else.Kind == StatementKind.Block) Body(statement.Else.Body);
        else { Open(); Statement(statement.Else); Close(); }
    }

    // GENE-27 L3: a parsed if's own comment that stood between its then-branch and `else` in source (the parser
    // records that gap as "Else"); the writer keeps it before `else` instead of before the whole if.
    static bool BeforeElse(CsStatement statement, CsComment comment) =>
        statement is { Else: not null, Origin: { } origin } && comment.Origin is { } at && ReferenceEquals(at.Snapshot, origin.Snapshot) &&
        origin.Bindings.FirstOrDefault(b => b.Property == "Else") is var gap && (uint)(at.CoreStart - gap.Start) < (uint)(gap.End - gap.Start);

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
