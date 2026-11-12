using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class LambdaWriter
{
    internal static string Parameter(CsLambdaParameter parameter) => InlineComments.Apply(parameter,
        string.Concat(parameter.Attributes.Select(a => Names.Attribute(a) + " ")) + (parameter.Modifier == null ? "" : parameter.Modifier + " ") +
        (parameter.Type == null ? "" : Names.Type(parameter.Type) + " ") + Names.Id(parameter.Name) + (parameter.Default == null ? "" : " = " + Names.Expression(parameter.Default)));

    internal static string Write(CsExpression expression)
    {
        var body = expression.LambdaBody!;
        if (expression.Kind == ExpressionKind.AnonymousMethod)
            return (expression.IsAsync ? "async " : "") + "delegate" + (expression.HasParameterList ? "(" + string.Join(", ", expression.LambdaParameters.Select(Parameter)) + ")" : "") + " " + Renderer.Node(body, body.Formatting ?? new(), CsLanguageVersion.CSharp15).TrimEnd();
        string text = string.Concat(expression.LambdaAttributes.Select(a => Names.Attribute(a) + " ")) + (expression.IsStatic ? "static " : "") + (expression.IsAsync ? "async " : "") +
            (expression.ReturnModifier == null ? "" : expression.ReturnModifier + " ") + (expression.Type == null ? "" : Names.Type(expression.Type) + " ") + "(" + string.Join(", ", expression.LambdaParameters.Select(Parameter)) + ") => " +
            (body.Expression != null ? InlineComments.Apply(body, Names.Expression(body.Expression)) :
                Renderer.Node(body, body.Formatting ?? new(), CsLanguageVersion.CSharp15).TrimEnd());
        return InlineComments.Apply(expression, text);
    }
}
