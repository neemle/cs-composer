using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Model;

internal static class SymbolShape
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
        bool declaration = SymbolModel.Declaration(s);
        Bad(o.AsVar && o.Type != null, path, "AsVar and an explicit type are mutually exclusive.");
        Bad(o.AsVar && o.Value == null && !binding, path, "An inferred declaration requires a value.");
        Bad(o.Using && (!declaration || o.Value == null), path, "Using belongs to an initialized declaration symbol.");
        Bad(declaration && (s.Name == null || o.AsNew || o.AsCall || o.AsCompound || o.AsAssignment || o.Operator != CsOperator.None || o.Arguments != null || o.Target != null || o.Parts.Count != 0), path, "Conflicting declaration options.");
        Bad(declaration && o.Await && !o.Using, path, "Await on a declaration requires Using.");
        if (declaration) return;
        int modes = (o.AsNew ? 1 : 0) + (o.AsCall ? 1 : 0) + (o.AsCompound ? 1 : 0) + (o.AsAssignment ? 1 : 0);
        Bad(modes > 1, path, "New, call, compound and assignment modes are mutually exclusive.");
        Bad((o.AsNew || o.AsCall) && o.Arguments == null, path + ".Arguments", "Calls and construction require a compound argument symbol, including an empty one.");
        Bad(o.Arguments != null && (!o.Arguments.Options.AsCompound || !(o.AsNew || o.AsCall)), path + ".Arguments", "Arguments must be a compound symbol on a call or construction.");
        if (o.AsNew) Bad(o.Type == null || s.Name != null || o.Target != null || o.Value != null || o.Parts.Count != 0 || o.Operator != CsOperator.None, path, "A construction needs a type and optional compound arguments.");
        else if (o.AsCall) Bad(s.Name == null && o.Target == null || o.Type != null || o.Value != null || o.Parts.Count != 0 || o.Operator != CsOperator.None, path, "A call needs a named or nested target and compound arguments.");
        else if (o.AsCompound) Bad(s.Name != null || o.Value != null || o.Target != null || o.Type != null || o.Operator != CsOperator.None || o.Await, path, "A compound symbol contains only its ordered parts.");
        else if (o.AsAssignment) Bad(s.Name == null && o.Target == null || o.Value == null || !Operators.Assignment(o.Operator) || o.Parts.Count != 0, path, "An assignment needs a target, value and assignment operator.");
        else if (o.Operator != CsOperator.None) Bad(s.Name != null || o.Value != null || o.Target != null || o.Parts.Count != (Operators.Unary(o.Operator) ? 1 : 2) || !Operators.Unary(o.Operator) && !Operators.Binary(o.Operator), path, "An operator symbol needs the correct ordered operands.");
        else Bad(o.Parts.Count != 0 || s.Name == null && o.Value == null || o.Value != null && (s.Name != null || o.Target != null), path, "A symbol must be a reference, value, operation, declaration or compound.");
    }

    static void Instruction(CsInstruction i, string path)
    {
        Bad(!Enum.IsDefined(i.Kind), path, "Unknown instruction kind.");
        var o = i.Options;
        bool loop = i.Kind is InstructionKind.For or InstructionKind.ForEach or InstructionKind.While or InstructionKind.DoWhile;
        bool body = loop || i.Kind is InstructionKind.Block or InstructionKind.If;
        bool value = i.Kind is InstructionKind.Return or InstructionKind.Throw or InstructionKind.ForEach;
        bool condition = i.Kind is InstructionKind.If or InstructionKind.For or InstructionKind.While or InstructionKind.DoWhile;
        Bad(body != (o.Body != null), path + ".Body", "Instruction body is missing or belongs to another instruction kind.");
        Bad(o.Value != null && !value || o.Condition != null && !condition || o.Else != null && i.Kind != InstructionKind.If ||
            (o.Initializer != null || o.Iterators != null) && i.Kind != InstructionKind.For || o.Symbol != null && i.Kind != InstructionKind.ForEach || o.Await && i.Kind != InstructionKind.ForEach,
            path, "Options do not belong to this instruction kind.");
        Bad(condition && i.Kind != InstructionKind.For && o.Condition == null, path + ".Condition", "Condition is required.");
        Bad(o.Iterators != null && !o.Iterators.Options.AsCompound, path + ".Iterators", "Iterators must be a compound symbol.");
        if (i.Kind == InstructionKind.ForEach)
        {
            Bad(o.Symbol == null || o.Value == null, path, "Foreach needs a binding symbol and collection value.");
            Bad(!SymbolModel.Declaration(o.Symbol!) || o.Symbol!.Options.Value != null || o.Symbol.Options.Using || o.Symbol.Options.Await,
                path + ".Symbol", "Foreach binding must be an uninitialized typed or inferred symbol.");
        }
    }

}
