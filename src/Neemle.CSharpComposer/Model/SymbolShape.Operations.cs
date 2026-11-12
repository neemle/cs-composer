using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Model;

internal static partial class SymbolShape
{
    static void Operation(CsSymbol symbol, string path)
    {
        var o = symbol.Options;
        Bad(o.ArgumentModifier != ArgumentModifier.None && o.Operation != SymbolOperation.Argument, path, "Argument modifier belongs to an argument operation.");
        Bad(o.VariableModifiers != CsVariableModifiers.None, path, "Variable modifiers belong to declaration symbols.");
        Bad(o.Text != null && o.Operation is not (SymbolOperation.StringLiteral or SymbolOperation.StringText), path, "Text belongs to string text/literal operations.");
        if (o.Operation == SymbolOperation.Parameter)
        {
            Bad(symbol.Name == null || o.Target != null || o.Arguments != null || o.Parts.Count > 0 || o.Operator != CsOperator.None || o.AsVar || o.Using || o.Await || o.Initializer != null || o.Pattern != null || o.TypeArguments.Count > 0 || o.ArrayRank != 1 || o.PointerAccess || o.AliasAccess || o.ConditionalAccess, path, "Parameter accepts a name, optional type/default, attributes and modifier.");
            return;
        }
        if (o.Operation == SymbolOperation.QueryClause)
        {
            Bad(!Enum.IsDefined(o.QueryKind) || o.Arguments != null || o.Parts.Count > 0 || o.Operator != CsOperator.None || o.AsVar || o.Using || o.Await || o.Initializer != null || o.Pattern != null || o.TypeArguments.Count > 0 || o.ArrayRank != 1 || o.PointerAccess || o.AliasAccess || o.ConditionalAccess, path, "Invalid query clause options.");
            Bad(o.Clauses != null && o.QueryKind is not (QueryClauseKind.OrderBy or QueryClauseKind.Into), path, "Nested clauses belong to orderby or continuation.");
            Bad(o.Clauses != null && !o.Clauses.Options.AsCompound, path, "Nested query clauses require a compound.");
            return;
        }
        if (o.Operation is SymbolOperation.Lambda or SymbolOperation.AnonymousMethod)
        {
            Bad(symbol.Name != null || o.Target != null || o.Arguments != null || o.Parts.Count > 0 || o.Operator != CsOperator.None ||
                o.TypeArguments.Count > 0 || o.Initializer != null || o.Pattern != null || o.Using || o.AsVar || o.ArrayRank != 1 || o.ConditionalAccess || o.PointerAccess || o.AliasAccess || o.QueryClauses.Count > 0,
                path, "Conflicting function options.");
            Bad(o.ParameterSymbols != null && (!o.ParameterSymbols.Options.AsCompound || o.Parameters.Count > 0), path, "Parameter symbols require a compound and cannot coexist with legacy parameters.");
            Bad((o.Body == null) == (o.Value == null), path, "Function requires one block or expression body.");
            Bad(o.Operation == SymbolOperation.AnonymousMethod && (o.Body == null || o.Type != null || o.ReturnModifier != null || o.Static || o.Attributes.Count > 0), path, "Anonymous method requires a block and permits only async as a modifier.");
            return;
        }
        bool typed = o.Operation is SymbolOperation.As or SymbolOperation.SizeOf or SymbolOperation.StackAlloc or SymbolOperation.Declaration or SymbolOperation.TypeReference or SymbolOperation.RefValue;
        bool value = o.Operation is SymbolOperation.As or SymbolOperation.NameOf or SymbolOperation.Checked or SymbolOperation.Unchecked or
            SymbolOperation.Unsafe or SymbolOperation.Interpolation or SymbolOperation.MakeRef or SymbolOperation.RefType or SymbolOperation.RefValue or SymbolOperation.Ref or SymbolOperation.NullForgiving or SymbolOperation.PreIncrement or SymbolOperation.PreDecrement or
            SymbolOperation.PostIncrement or SymbolOperation.PostDecrement or SymbolOperation.Argument or SymbolOperation.Spread or SymbolOperation.With;
        bool parts = o.Operation is SymbolOperation.Tuple or SymbolOperation.Collection or SymbolOperation.AnonymousObject or SymbolOperation.ImplicitIndex or SymbolOperation.StackAlloc or SymbolOperation.CollectionArguments or SymbolOperation.Declaration or SymbolOperation.InterpolatedString or SymbolOperation.Query;
        bool name = o.Operation is SymbolOperation.Argument or SymbolOperation.Declaration;
        bool initializer = o.Operation is SymbolOperation.StackAlloc or SymbolOperation.With;
        Bad(!Enum.IsDefined(o.Operation), path, "Unknown symbol operation.");
        Bad(o.Type != null && !typed || o.Value != null && !value && o.Operation != SymbolOperation.Range ||
            o.Target != null && o.Operation != SymbolOperation.Range || o.Parts.Count > 0 && !parts || symbol.Name != null && !name ||
            o.Arguments != null || o.Operator != CsOperator.None || o.Pattern != null || o.ConditionalAccess || o.PointerAccess || o.AliasAccess ||
            o.Using || o.AsVar && o.Operation != SymbolOperation.Declaration || o.Initializer != null && !initializer ||
            o.TypeArguments.Count > 0 || o.ArrayRank != 1,
            path, "Options do not belong to this symbol operation.");
        Bad(o.Operation is SymbolOperation.StringLiteral or SymbolOperation.StringText && o.Text == null, path, "String text is required.");
        Bad(o.Operation != SymbolOperation.Interpolation && (o.Alignment != null || o.Format != null) ||
            o.Operation != SymbolOperation.StringLiteral && o.Utf8 || o.Operation is not (SymbolOperation.StringLiteral or SymbolOperation.InterpolatedString) && o.StringStyle != CsStringStyle.Regular,
            path, "String payload belongs to another operation.");
        Bad(o.Operation == SymbolOperation.Query && o.QueryClauses.Count > 0 && o.Parts.Count > 0, path, "Choose one query clause representation.");
        Bad(value && o.Value == null, path + ".Value", "Operation requires a value operand.");
        Bad(o.Operation is SymbolOperation.As or SymbolOperation.SizeOf or SymbolOperation.TypeReference or SymbolOperation.RefValue && o.Type == null, path + ".Type", "Operation requires a type.");
        Bad(o.ArgumentModifier != ArgumentModifier.None && o.Operation != SymbolOperation.Argument, path, "Argument modifier belongs to an argument.");
        Bad(o.Operation == SymbolOperation.Declaration && (symbol.Name != null && ((o.Type == null) == !o.AsVar || o.Parts.Count > 0) || symbol.Name == null && (!o.AsVar || o.Type != null || o.Parts.Count < 2)), path, "Declaration operand requires a name and either var or an explicit type.");
        Bad(o.Operation == SymbolOperation.Tuple && o.Parts.Count < 2 || o.Operation == SymbolOperation.ImplicitIndex && o.Parts.Count == 0,
            path, "Operation has too few parts.");
        Bad(o.Operation == SymbolOperation.With && o.Initializer == null, path, "With requires an initializer.");
        Bad(o.Initializer != null && !o.Initializer.Options.AsInitializer, path, "Initializer must contain structured initializer parts.");
    }
}
