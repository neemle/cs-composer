using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Loop(CsStatement statement)
    {
        switch (statement.Kind)
        {
            case StatementKind.For:
                Line("for (" + (statement.ForInitializer == null ? "" : LoopHeaderWriter.Initializer(statement.ForInitializer)) +
                    "; " + (statement.Expression == null ? "" : Names.Expression(statement.Expression)) + "; " +
                    string.Join(", ", statement.Iterators.Select(Names.Expression)) + ")");
                break;
            case StatementKind.ForEach:
                Line((statement.IsAwait ? "await " : "") + "foreach (" + (statement.IsVar ? "var" : Names.Type(statement.Type!)) + " " + Names.Id(statement.Text) +
                    " in " + Names.Expression(statement.Expression!) + ")");
                break;
            case StatementKind.While:
                Line("while (" + Names.Expression(statement.Expression!) + ")");
                break;
            case StatementKind.DoWhile: Line("do"); break;
        }
        Body(statement.Body);
        if (statement.Kind == StatementKind.DoWhile) Line("while (" + Names.Expression(statement.Expression!) + ");");
    }
}
