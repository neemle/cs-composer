using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Statement(CsStatement statement)
    {
        switch (statement.Kind)
        {
            case StatementKind.Raw: Raw(statement.Text); break;
            case StatementKind.Local: Line(Local(statement) + ";"); break;
            case StatementKind.Block: Body(statement.Body); break;
            case StatementKind.If: If(statement); break;
            case StatementKind.Try: Try(statement); break;
            case StatementKind.Using: Using(statement); break;
            case StatementKind.Rethrow: Line("throw;"); break;
            default: SimpleStatement(statement); break;
        }
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
            Line("catch" + (handler.Type == null ? "" : " (" + Names.Type(handler.Type) +
                (handler.Name == null ? "" : " " + Names.Id(handler.Name)) + ")"));
            Body(handler.Body);
        }
        if (statement.Finally == null) return;
        Line("finally");
        Body(statement.Finally);
    }

    void Using(CsStatement statement)
    {
        string resource = statement.Resource == null ? Names.Expression(statement.Expression!) :
            Local(statement.Resource);
        Line("using (" + resource + ")");
        Body(statement.Body);
    }
}
