using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class LambdaWriter
{
    internal static string Parameter(CsLambdaParameter parameter) => InlineComments.Apply(parameter,
        (parameter.Modifier == null ? "" : parameter.Modifier + " ") +
        (parameter.Type == null ? "" : Names.Type(parameter.Type) + " ") + Names.Id(parameter.Name));

    internal static string Write(CsExpression expression)
    {
        var body = expression.LambdaBody!;
        string text = (expression.IsStatic ? "static " : "") + (expression.IsAsync ? "async " : "") +
            "(" + string.Join(", ", expression.LambdaParameters.Select(Parameter)) + ") => " +
            (body.Expression != null ? InlineComments.Apply(body, Names.Expression(body.Expression)) :
                Renderer.Node(body, body.Formatting ?? new(), CsLanguageVersion.CSharp15).TrimEnd());
        return InlineComments.Apply(expression, text);
    }
}
