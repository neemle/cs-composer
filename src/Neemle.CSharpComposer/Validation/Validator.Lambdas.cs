using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void Lambda(CsExpression expression, string path, int depth)
    {
        if (expression.IsStatic) Feature("Static lambda", CsLanguageVersion.CSharp9, path + ".IsStatic");
        if (expression.Type != null || expression.LambdaAttributes?.Count > 0) Feature("Explicit lambda return types and attributes", CsLanguageVersion.CSharp10, path);
        Attributes(expression.LambdaAttributes, path + ".LambdaAttributes", depth + 1);
        if (expression.ReturnModifier != null && (expression.Type == null || expression.ReturnModifier is not ("ref" or "ref readonly") || expression.IsAsync)) Error("GEN089", path, "Invalid lambda return modifier.");
        if (expression.Kind == ExpressionKind.AnonymousMethod && (expression.IsStatic || expression.Type != null || expression.ReturnModifier != null || expression.LambdaAttributes?.Count > 0 || expression.LambdaBody?.Expression != null)) Error("GEN089", path, "Invalid anonymous method signature.");
        if (!expression.HasParameterList && (expression.Kind != ExpressionKind.AnonymousMethod || expression.LambdaParameters.Count > 0)) Error("GEN089", path, "Only an anonymous method can omit its parameter list.");
        bool optionalSeen = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool? typed = null;
        bool discards = expression.LambdaParameters.Count(p => p?.Name == "_") > 1 &&
            !expression.LambdaParameters.Any(p => p?.Name == "@_");
        List(expression.LambdaParameters, path + ".LambdaParameters", (parameter, child) =>
        {
            Identifier(parameter.Name, child + ".Name");
            // C# 9 permits repeated underscore parameters as discards. In 7.3/8,
            // a single underscore is an ordinary named parameter.
            if (parameter.Name != null && !names.Add(Rendering.Names.Key(parameter.Name)))
            {
                if (parameter.Name == "_" && discards) Feature("Lambda discard parameters", CsLanguageVersion.CSharp9, child);
                else Error("GEN089", child + ".Name", "Duplicate lambda parameter.");
            }
            bool hasType = parameter.Type != null;
            if (typed != null && typed != hasType) Error("GEN089", child, "Lambda parameters must be all explicit or all inferred.");
            typed = hasType;
            if (hasType)
            {
                Type(parameter.Type, child + ".Type", depth + 1);
                if (parameter.Type is { Form: TypeForm.Named, Name: "void" })
                    Error("GEN089", child + ".Type", "Lambda parameters cannot have type void.");
            }
            Attributes(parameter.Attributes, child + ".Attributes", depth + 1);
            if (parameter.Attributes?.Count > 0) Feature("Lambda parameter attributes", CsLanguageVersion.CSharp10, child);
            if (parameter.Modifier != null)
            {
                if (parameter.Modifier is not ("ref" or "out" or "in" or "ref readonly" or "params" or "scoped ref" or "scoped in") || expression.IsAsync && parameter.Modifier != "params") Error("GEN089", child, "Invalid lambda parameter modifier.");
                if (!hasType) Feature("Implicitly typed lambda parameter modifiers", CsLanguageVersion.CSharp14, child);
                if (parameter.Modifier == "params") Feature("Lambda parameter collection", CsLanguageVersion.CSharp12, child);
                if (parameter.Modifier == "ref readonly") Feature("Ref readonly parameters", CsLanguageVersion.CSharp12, child);
            }
            if (parameter.Default != null)
            {
                Feature("Lambda default parameters", CsLanguageVersion.CSharp12, child);
                Expression(parameter.Default, child + ".Default", depth + 1);
                if (parameter.Modifier != null) Error("GEN089", child, "Default value cannot accompany a parameter modifier.");
                optionalSeen = true;
            }
            else if (optionalSeen && parameter.Modifier != "params") Error("GEN089", child, "Required parameter follows an optional parameter.");
        });
        // A lambda is a function boundary: enclosing loop, switch and catch
        // contexts do not authorize transfers from inside its body.
        Body(expression.LambdaBody, path + ".LambdaBody", depth + 1, allowThrow: true);
    }
}
