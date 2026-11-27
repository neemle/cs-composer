using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class LambdaWriter
{
    internal static string Parameter(CsLambdaParameter parameter) => InlineComments.Apply(parameter,
        string.Concat(parameter.Attributes.Select(a => Names.Attribute(a) + " ")) + (parameter.Modifier == null ? "" : parameter.Modifier + " ") +
        (parameter.Type == null ? "" : Names.Type(parameter.Type) + " ") + Names.Id(parameter.Name) + (parameter.Default == null ? "" : " = " + Names.Expression(parameter.Default)));

    // GENE-29 F2 (AD C3): a lambda's parameter list, breaking one parameter per line.
    static string Parameters(CsExpression expression) =>
        Layout.List("(", expression.LambdaParameters.Select(p => InlineComments.Entry(p, Parameter)), ")");

    internal static string Write(CsExpression expression)
    {
        var body = expression.LambdaBody!;
        if (expression.Kind == ExpressionKind.AnonymousMethod)
            return (expression.IsAsync ? "async " : "") + "delegate" + (expression.HasParameterList ? Parameters(expression) : "") + " " + Renderer.Nested(body);
        string parameters = Parameters(expression);
        // One parameter that is just a name needs no parentheses (`x => x`), unless the lambda has attributes or a
        // return type, which only a parenthesized list can follow.
        bool simple = expression.LambdaParameters.Count == 1 && expression.Type == null && expression.LambdaAttributes.Count == 0 &&
            Names.IsIdentifier(Parameter(expression.LambdaParameters[0]));
        string text = string.Concat(expression.LambdaAttributes.Select(a => Names.Attribute(a) + " ")) + (expression.IsStatic ? "static " : "") + (expression.IsAsync ? "async " : "") +
            (expression.ReturnModifier == null ? "" : expression.ReturnModifier + " ") + (expression.Type == null ? "" : Names.Type(expression.Type) + " ") + (simple ? Parameter(expression.LambdaParameters[0]) : parameters) + " => " +
            (body.Expression != null ? InlineComments.Apply(body, Names.Expression(body.Expression)) : Renderer.Nested(body));
        return InlineComments.Apply(expression, text);
    }
}
