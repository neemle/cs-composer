using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Statement(CsStatement statement)
    {
        Comments(statement, CommentPlacement.Leading);
        Comments(statement, CommentPlacement.Container);
        switch (statement.Kind)
        {
            case StatementKind.For:
            case StatementKind.ForEach:
            case StatementKind.While:
            case StatementKind.DoWhile:
                Loop(statement); break;
            case StatementKind.Continue: Line("continue;"); break;
            case StatementKind.Empty: Line(";"); break;
            case StatementKind.Switch: Switch(statement); break;
            case StatementKind.Break: Line("break;"); break;
            case StatementKind.GotoCase: Line("goto case " + Names.Expression(statement.Expression!) + ";"); break;
            case StatementKind.GotoDefault: Line("goto default;"); break;
            case StatementKind.Raw: Raw(statement.Text); break;
            case StatementKind.UsingDeclaration: Line((statement.IsAwait ? "await " : "") + "using " + Local(statement.Resource!) + ";"); break;
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

    void SimpleStatement(CsStatement statement)
    {
        string keyword = statement.Kind == StatementKind.Return ? "return" :
            statement.Kind == StatementKind.Throw ? "throw" : "";
        Line(keyword + (keyword.Length != 0 && statement.Expression != null ? " " : "") +
            (statement.Expression == null ? "" : Names.Expression(statement.Expression)) + ";");
    }

    static string Local(CsStatement statement) =>
        (statement.IsVar ? "var" : Names.Type(statement.Type!)) + " " + Names.Id(statement.Text) +
        Initializer(statement.Expression);

    void If(CsStatement statement)
    {
        Line("if (" + Names.Expression(statement.Expression!) + ")");
        Body(statement.Body);
        if (statement.Else == null) return;
        Line("else");
        if (statement.Else.Kind == StatementKind.Block) Body(statement.Else.Body);
        else { Open(); Statement(statement.Else); Close(); }
    }

    void Try(CsStatement statement)
    {
        Line("try");
        Body(statement.Body);
        foreach (var handler in statement.Catches)
        {
            Comments(handler, CommentPlacement.Leading);
            Line("catch" + (handler.Type == null ? "" : " (" + Names.Type(handler.Type) +
                (handler.Name == null ? "" : " " + Names.Id(handler.Name)) + ")"));
            Body(handler.Body);
            Comments(handler, CommentPlacement.Trailing);
        }
        if (statement.Finally == null) return;
        Line("finally");
        Body(statement.Finally);
    }

    void Using(CsStatement statement)
    {
        string resource = statement.Resource == null ? Names.Expression(statement.Expression!) :
            Local(statement.Resource);
        Line((statement.IsAwait ? "await " : "") + "using (" + resource + ")");
        Body(statement.Body);
    }
}
