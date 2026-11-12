using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class StatementValidator
{
    void Loop(CsStatement statement, string path, int depth)
    {
        if (statement.Kind == StatementKind.For)
        {
            if (statement.ForInitializer != null) Initializer(statement.ForInitializer, path + ".ForInitializer", depth + 1);
            validation.List(statement.Iterators, path + ".Iterators", (iterator, child) => HeaderExpression(iterator, child, depth + 1));
        }
        if (statement.Kind == StatementKind.ForEach)
        {
            if (statement.Binding != null) validation.Expression(statement.Binding, path + ".Binding", depth + 1, parent: ExpressionKind.Assignment);
            else
            {
                validation.VariableModifiers(statement.VariableModifiers, path + ".VariableModifiers", statement.IsVar, allowConst: false);
                validation.Identifier(statement.Text, path + ".Text");
                if (!statement.IsVar) VariableType(statement.Type, path + ".Type", depth + 1);
            }
            validation.Expression(statement.Expression, path + ".Expression", depth + 1);
        }
        else if (statement.Kind != StatementKind.For || statement.Expression != null)
        {
            validation.Expression(statement.Expression, path + ".Expression", depth + 1);
            if (statement.Expression is { Kind: ExpressionKind.Literal, Text: not ("true" or "false") })
                validation.Error("GEN086", path + ".Expression", "A loop condition must be Boolean.");
        }
        new StatementValidator(validation, inCatch, switchContext, true, labels).Block(statement.Body, path + ".Body", depth + 1, inCatch);
    }

    void Initializer(CsForInitializer initializer, string path, int depth, bool allowConst = false)
    {
        if (!validation.Depth(depth, path)) return;
        validation.VariableModifiers(initializer.VariableModifiers, path + ".VariableModifiers", initializer.IsVar, allowConst);
        bool declaration = initializer.Type != null || initializer.IsVar || initializer.Variables?.Count > 0;
        if (declaration)
        {
            if (initializer.Expressions?.Count > 0)
                validation.Error("GEN084", path, "For initialization cannot mix a declaration and statement expressions.");
            if (!initializer.IsVar) VariableType(initializer.Type, path + ".Type", depth + 1);
            if (initializer.IsVar && initializer.Type != null)
                validation.Error("GEN065", path + ".Type", "An inferred variable cannot have an explicit type.");
            if (initializer.Variables?.Count == 0 || initializer.IsVar && initializer.Variables?.Count != 1)
                validation.Error("GEN084", path + ".Variables", "A declaration requires variables; var requires exactly one.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        validation.List(initializer.Variables, path + ".Variables", (variable, child) =>
        {
            if (variable.FixedSize != null) validation.Error("GEN065", child, "Fixed buffer size belongs to field declarations.");
            validation.Identifier(variable.Name, child + ".Name");
            if (variable.Name != null && !names.Add(Names.Key(variable.Name)))
                validation.Error("GEN084", child + ".Name", "Duplicate for variable name.");
            if (initializer.IsVar && variable.Initializer == null)
                validation.Error("GEN067", child + ".Initializer", "An inferred variable requires an initializer.");
            if (initializer.IsVar && variable.Initializer is { Kind: ExpressionKind.Lambda })
                validation.Feature("Natural lambda type inference", CsLanguageVersion.CSharp10, child + ".Initializer");
            if ((initializer.VariableModifiers & CsVariableModifiers.Const) != 0) validation.Constant(variable.Initializer, child + ".Initializer", depth + 1);
            else validation.VariableInitializer(variable.Initializer, initializer.Type, child + ".Initializer", depth + 1);
        });
        validation.List(initializer.Expressions, path + ".Expressions", (expression, child) => HeaderExpression(expression, child, depth + 1));
    }

    void VariableType(CsType? type, string path, int depth)
    {
        validation.Type(type, path, depth);
        if (type is { Form: TypeForm.Named, Name: "void" })
            validation.Error("GEN084", path, "A loop variable cannot have type void.");
    }

    void HeaderExpression(CsExpression expression, string path, int depth)
    {
        validation.Expression(expression, path, depth);
        if (!ExpressionKinds.StatementExpression(expression.Kind))
            validation.Error("GEN087", path, "For initializers and iterators require statement expressions.");
        if (expression.Kind == ExpressionKind.Assignment && expression.Arguments?.Count == 2 &&
            expression.Arguments[0] is { } left && !ExpressionKinds.Assignable(left))
            validation.Error("GEN088", path, "An assignment requires a writable target.");
    }

    void Continue(string path)
    {
        if (!inLoop) validation.Error("GEN085", path, "Continue requires an enclosing loop outside finally.");
    }
}
