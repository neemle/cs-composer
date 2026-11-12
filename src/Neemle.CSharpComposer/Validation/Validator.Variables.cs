using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    internal void VariableInitializer(CsExpression? expression, CsType? type, string path, int depth)
    {
        if (expression != null) Expression(expression, path, depth, initializerContext: type?.Form == TypeForm.Array);
    }

    internal void VariableModifiers(CsVariableModifiers modifiers, string path, bool isVar, bool allowConst)
    {
        bool Has(CsVariableModifiers flag) => (modifiers & flag) != 0;
        if ((modifiers & ~(CsVariableModifiers.Const | CsVariableModifiers.Ref | CsVariableModifiers.ReadOnly | CsVariableModifiers.Scoped)) != 0 ||
            Has(CsVariableModifiers.ReadOnly) && !Has(CsVariableModifiers.Ref) || Has(CsVariableModifiers.Const) && (!allowConst || isVar || modifiers != CsVariableModifiers.Const))
            Error("GEN092", path, "Incompatible variable modifiers.");
        if (Has(CsVariableModifiers.Scoped)) Feature("Scoped variable", CsLanguageVersion.CSharp11, path);
    }
}
