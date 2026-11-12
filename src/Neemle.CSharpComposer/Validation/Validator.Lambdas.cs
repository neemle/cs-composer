using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void Lambda(CsExpression expression, string path, int depth)
    {
        if (expression.IsStatic) Feature("Static lambda", CsLanguageVersion.CSharp9, path + ".IsStatic");
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool? typed = null;
        bool discards = expression.LambdaParameters.Count(p => p?.Name == "_") > 1 &&
            !expression.LambdaParameters.Any(p => p?.Name == "@_");
        List(expression.LambdaParameters, path + ".LambdaParameters", (parameter, child) =>
        {
            Identifier(parameter.Name, child + ".Name");
            // C# 9 permits repeated underscore parameters as discards. In 7.3/8,
            // a single underscore is an ordinary named parameter.
            if (parameter.Name != null && !names.Add(parameter.Name.TrimStart('@')))
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
            if (parameter.Modifier != null && (parameter.Modifier is not ("ref" or "out" or "in") || !hasType || expression.IsAsync))
                Error("GEN089", child + ".Modifier", "Ref/out/in lambda parameters require explicit types and cannot be async.");
        });
        // A lambda is a function boundary: enclosing loop, switch and catch
        // contexts do not authorize transfers from inside its body.
        Body(expression.LambdaBody, path + ".LambdaBody", depth + 1, allowThrow: true);
    }
}
