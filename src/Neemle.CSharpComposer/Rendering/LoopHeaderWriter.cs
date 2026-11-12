using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class LoopHeaderWriter
{
    internal static string Initializer(CsForInitializer initializer)
    {
        string text = initializer.IsVar || initializer.Type != null || initializer.Variables.Count != 0
            ? Modifiers(initializer.VariableModifiers) + (initializer.IsVar ? "var" : Names.Type(initializer.Type!)) + " " +
                string.Join(", ", initializer.Variables.Select(Variable))
            : string.Join(", ", initializer.Expressions.Select(Names.Expression));
        return InlineComments.Apply(initializer, text);
    }

    internal static string Modifiers(CsVariableModifiers modifiers) =>
        ((modifiers & CsVariableModifiers.Const) != 0 ? "const " : "") + ((modifiers & CsVariableModifiers.Scoped) != 0 ? "scoped " : "") +
        ((modifiers & CsVariableModifiers.Ref) != 0 ? "ref " : "") + ((modifiers & CsVariableModifiers.ReadOnly) != 0 ? "readonly " : "");

    internal static string Variable(CsVariableDeclarator variable) => InlineComments.Apply(variable,
        Names.Id(variable.Name) + (variable.FixedSize == null ? "" : "[" + Names.Expression(variable.FixedSize) + "]") + (variable.Initializer == null ? "" : " = " + Names.Expression(variable.Initializer)));
}
