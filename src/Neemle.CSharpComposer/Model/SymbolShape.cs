using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Model;

internal static partial class SymbolShape
{
    internal static void Check(CsNode node, string path, CsSymbol? binding = null, int depth = 0)
    {
        if (depth > 128) throw new SymbolModel.InvalidModel("GEN001", path, "Tree nesting exceeds 128.");
        if (node is CsSymbol s) Symbol(s, path, ReferenceEquals(s, binding));
        if (node is CsInstruction i) Instruction(i, path);
        var nextBinding = node is CsInstruction { Kind: InstructionKind.ForEach } each ? each.Options.Symbol : binding;
        for (int index = 0; index < node.Children.Count; index++) Check(node.Children[index], path + ".Children[" + index + "]", nextBinding, depth + 1);
    }

    static void Bad(bool value, string path, string message) => SymbolModel.Reject(value, path, message);

    static void Symbol(CsSymbol s, string path, bool binding)
    {
        var o = s.Options;
        Bad(s.Name != null && !Names.IsIdentifier(s.Name), path + ".Name", "Invalid symbol name.");
        Bad(o.QueryClauses.Count > 0 && o.Operation != SymbolOperation.Query, path, "Query clauses belong to a query operation.");
        bool strings = o.Operation is SymbolOperation.StringLiteral or SymbolOperation.StringText or SymbolOperation.Interpolation or SymbolOperation.InterpolatedString;
        Bad(!strings && (o.Text != null || o.Format != null || o.Alignment != null || o.StringStyle != CsStringStyle.Regular || o.Utf8), path, "String options belong to string operations.");
        bool function = o.Operation is SymbolOperation.Lambda or SymbolOperation.AnonymousMethod;
        Bad(!function && (o.Parameters.Count > 0 || o.ParameterSymbols != null || o.Attributes.Count > 0 && o.Operation != SymbolOperation.Parameter || o.Body != null || o.ReturnModifier != null || o.Async || o.Static || !o.HasParameterList), path, "Function options belong to lambdas or anonymous methods.");
        Bad(o.ParameterModifier != null && o.Operation != SymbolOperation.Parameter, path, "Parameter modifier belongs to a parameter symbol.");
        Bad(o.Operation != SymbolOperation.QueryClause && (o.QueryKind != QueryClauseKind.From || o.Clauses != null || o.EqualityKey != null || o.Into != null || o.Descending), path, "Clause options belong to a query clause symbol.");
        if (o.Operation != SymbolOperation.None)
        {
            Bad(SymbolModes.Count(o) != 1, path, "Conflicting operation modes.");
            Operation(s, path);
            return;
        }
        Bad(o.ArgumentModifier != ArgumentModifier.None, path, "Argument modifier belongs to an argument operation.");
        Bad(o.TypeArguments.Count > 0 && (o.AsVar || SymbolModes.Count(o) != 0 || o.Value != null), path, "Type arguments belong to a reference symbol.");
        bool declaration = SymbolModel.Declaration(s);
        Bad(o.AsVar && o.Type != null, path, "AsVar and an explicit type are mutually exclusive.");
        Bad(o.AsVar && o.Value == null && !binding, path, "An inferred declaration requires a value.");
        Bad(o.Using && (!declaration || o.Value == null), path, "Using belongs to an initialized declaration symbol.");
        Bad(declaration && (s.Name == null || o.AsNew || o.AsCall || o.AsCompound || o.AsAssignment || o.Operator != CsOperator.None || o.Arguments != null || o.Target != null || o.Parts.Count != 0), path, "Conflicting declaration options.");
        Bad(declaration && o.Await && !o.Using, path, "Await on a declaration requires Using.");
        if (!declaration && o.VariableModifiers != CsVariableModifiers.None) Bad(true, path, "Variable modifiers belong to a declaration.");
        if (declaration)
        {
            Bad(SymbolModes.Count(o) != 0 || o.Pattern != null || o.Initializer != null || o.ConditionalAccess || o.ArrayRank != 1, path, "Expression options do not belong to a declaration.");
            return;
        }
        int modes = SymbolModes.Count(o);
        Bad(modes > 1, path, "Expression modes are mutually exclusive.");
        Bad((o.PointerAccess || o.AliasAccess) && (s.Name == null || o.Target == null || o.AsIndex || o.ConditionalAccess || o.PointerAccess && o.AliasAccess), path, "Pointer/alias access needs a named member target and one access mode.");
        Bad(o.ConditionalAccess && !(o.AsIndex || s.Name != null && o.Target != null), path, "Conditional access needs a member or index target.");
        Bad(o.Pattern != null && !o.AsIs, path, "Pattern belongs only to an is symbol.");
        Bad(o.Initializer != null && (!(o.AsNew || o.AsArray) || !o.Initializer.Options.AsInitializer), path, "Initializer belongs to construction/array and must be an initializer symbol.");
        Bad(o.ArrayRank != 1 && !o.AsArray, path, "Array rank belongs to array construction.");
        Bad((o.AsNew || o.AsCall || o.AsIndex || o.AsArray) && o.Arguments == null, path + ".Arguments", "An argument/dimension list must be a compound symbol, including an empty one.");
        Bad(o.Arguments != null && (!o.Arguments.Options.AsCompound || !(o.AsNew || o.AsCall || o.AsIndex || o.AsArray)), path + ".Arguments", "Arguments must be a compound symbol on a call, construction or access.");
        bool typeMode = SymbolModes.TypeExpression(o);
        Bad(o.Type != null && !typeMode, path, "Type payload does not belong to this expression mode.");
        if (o.AsIndex) Bad(o.Target == null || s.Name != null || o.Value != null || o.Operator != CsOperator.None || o.Parts.Count != 0 || o.Arguments!.Options.Parts.Count == 0, path, "Index access requires a target and at least one index.");
        else if (o.AsCast || o.AsTypeOf || o.AsDefault || o.AsIs)
        {
            Bad(s.Name != null || o.Target != null || o.Operator != CsOperator.None || o.Parts.Count != 0, path, "Conflicting typed-operation options.");
            Bad((o.AsCast || o.AsTypeOf) && o.Type == null, path, "Type is required.");
            Bad((o.AsCast || o.AsIs) != (o.Value != null), path, "This operation requires exactly one value operand.");
            Bad(o.AsIs && (o.Pattern == null) == (o.Type == null), path, "Is requires exactly one type or pattern.");
        }
        else if (o.AsConditional) Bad(s.Name != null || o.Value != null || o.Target != null || o.Operator != CsOperator.None || o.Parts.Count != 3, path, "Conditional expression needs condition, true and false parts.");
        else if (o.AsArray) Bad(s.Name != null || o.Value != null || o.Target != null || o.Operator != CsOperator.None || o.Parts.Count != 0, path, "Array construction has only type, dimensions, rank and initializer.");
        else if (o.AsInitializer) Bad(s.Name != null || o.Value != null || o.Target != null || o.Operator != CsOperator.None || o.Await, path, "An initializer contains only its ordered parts.");
        else if (o.AsParenthesized) Bad(s.Name != null || o.Value == null || o.Target != null || o.Operator != CsOperator.None || o.Parts.Count != 0, path, "A grouped symbol needs one value.");
        else if (o.AsNew) Bad(s.Name != null || o.Target != null || o.Value != null || o.Parts.Count != 0 || o.Operator != CsOperator.None, path, "A construction needs an optional type, argument list and initializer.");
        else if (o.AsCall) Bad(s.Name == null && o.Target == null || o.Value != null || o.Parts.Count != 0 || o.Operator != CsOperator.None, path, "A call needs a named or nested target and compound arguments.");
        else if (o.AsCompound) Bad(s.Name != null || o.Value != null || o.Target != null || o.Operator != CsOperator.None || o.Await, path, "A compound symbol contains only its ordered parts.");
        else if (o.AsAssignment) Bad(s.Name == null && o.Target == null || o.Value == null || !Operators.Assignment(o.Operator) || o.Parts.Count != 0, path, "An assignment needs a target, value and assignment operator.");
        else if (o.Operator != CsOperator.None) Bad(s.Name != null || o.Value != null || o.Target != null || o.Parts.Count != (Operators.Unary(o.Operator) ? 1 : 2) || !Operators.Unary(o.Operator) && !Operators.Binary(o.Operator), path, "An operator symbol needs the correct ordered operands.");
        else Bad(o.Parts.Count != 0 || s.Name == null && o.Value == null || o.Value != null && (s.Name != null || o.Target != null), path, "A symbol must be a reference, value, operation, declaration or compound.");
    }

    static void Instruction(CsInstruction i, string path)
    {
        Bad(!Enum.IsDefined(i.Kind), path, "Unknown instruction kind.");
        var o = i.Options;
        bool loop = i.Kind is InstructionKind.For or InstructionKind.ForEach or InstructionKind.While or InstructionKind.DoWhile;
        bool body = loop || i.Kind is InstructionKind.Block or InstructionKind.If or InstructionKind.Try or InstructionKind.Using or
            InstructionKind.Lock or InstructionKind.Fixed or InstructionKind.Unsafe or InstructionKind.Checked or InstructionKind.Unchecked or InstructionKind.Label;
        bool value = i.Kind is InstructionKind.Return or InstructionKind.Throw or InstructionKind.ForEach or InstructionKind.Switch or InstructionKind.GotoCase or InstructionKind.Using or InstructionKind.Lock or InstructionKind.YieldReturn;
        bool condition = i.Kind is InstructionKind.If or InstructionKind.For or InstructionKind.While or InstructionKind.DoWhile;
        Bad(body != (o.Body != null), path + ".Body", "Instruction body is missing or belongs to another instruction kind.");
        Bad(o.Value != null && !value || o.Condition != null && !condition || o.Else != null && i.Kind != InstructionKind.If ||
            (o.Iterators != null) && i.Kind != InstructionKind.For || o.Initializer != null && i.Kind is not (InstructionKind.For or InstructionKind.Using or InstructionKind.Fixed) || o.Symbol != null && i.Kind != InstructionKind.ForEach || o.Await && i.Kind is not (InstructionKind.ForEach or InstructionKind.Using),
            path, "Options do not belong to this instruction kind.");
        Bad(o.Name != null && i.Kind is not (InstructionKind.Goto or InstructionKind.Label or InstructionKind.Break or InstructionKind.Continue) ||
            o.Sections.Count > 0 && i.Kind != InstructionKind.Switch || (o.Catches.Count > 0 || o.Finally != null) && i.Kind != InstructionKind.Try ||
            o.Function != null && i.Kind != InstructionKind.LocalFunction, path, "Payload belongs to another instruction kind.");
        Bad(i.Kind is InstructionKind.Goto or InstructionKind.Label && !Names.IsIdentifier(o.Name), path, "Jump/label needs an identifier.");
        Bad(i.Kind is InstructionKind.Switch or InstructionKind.GotoCase or InstructionKind.Lock or InstructionKind.YieldReturn && o.Value == null, path, "Value is required.");
        Bad(i.Kind == InstructionKind.LocalFunction && o.Function == null || i.Kind == InstructionKind.Fixed && o.Initializer == null, path, "Instruction payload is required.");
        Bad(i.Kind == InstructionKind.Using && (o.Value == null) == (o.Initializer == null), path, "Using needs one resource expression or declaration.");
        Bad(condition && i.Kind != InstructionKind.For && o.Condition == null, path + ".Condition", "Condition is required.");
        Bad(o.Iterators != null && !o.Iterators.Options.AsCompound, path + ".Iterators", "Iterators must be a compound symbol.");
        if (i.Kind == InstructionKind.ForEach)
        {
            Bad(o.Symbol == null || o.Value == null, path, "Foreach needs a binding symbol and collection value.");
            Bad(!SymbolModel.Declaration(o.Symbol!) && o.Symbol!.Options.Operation is not (SymbolOperation.Tuple or SymbolOperation.Declaration) || o.Symbol!.Options.Value != null || o.Symbol.Options.Using || o.Symbol.Options.Await,
                path + ".Symbol", "Foreach binding must be an uninitialized typed or inferred symbol.");
        }
    }

}
