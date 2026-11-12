using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;

namespace Neemle.CSharpComposer.Model;

internal static class SymbolModel
{
    internal sealed class InvalidModel(string code, string path, string message) : Exception(message)
    {
        internal GenDiagnostic Diagnostic => new(code, path, Message);
    }

    internal static void Reject(bool invalid, string path, string message)
    {
        if (invalid) throw new InvalidModel("SYM001", path, message);
    }

    internal static IReadOnlyList<GenDiagnostic> Validate(CsTree tree)
    {
        var graph = GraphValidator.Validate(tree);
        if (graph.Count != 0) return graph;
        try
        {
            SymbolShape.Check(tree, "Tree");
            return Document(tree).Validate();
        }
        catch (InvalidModel error) { return new[] { error.Diagnostic }; }
    }

    internal static CsBody ToBody(CsTree tree)
    {
        var errors = GraphValidator.Validate(tree);
        if (errors.Count != 0) throw new InvalidModel(errors[0].Code, errors[0].Path, errors[0].Message);
        SymbolShape.Check(tree, "Tree");
        return new Lowerer().Body(tree);
    }

    static CsDocument Document(CsTree tree) => new(tree.LanguageVersion)
    {
        Types = new[] {
        new CsTypeDeclaration(TypeKind.Class, "Context") { Members = new CsMember[] {
            new CsMethod(Cs.Void, "Body") { Body = new Lowerer().Body(tree) } } } }
    };

    internal static string Render(CsTree tree, RenderOptions options)
    {
        var errors = Validate(tree);
        if (errors.Count != 0) throw new GenValidationException(errors);
        Reject(options.Indent == null || options.Indent.Any(c => c is not (' ' or '\t')), "RenderOptions.Indent", "Indent must contain only spaces or tabs.");
        return PreservedRenderer.RenderStatements(new Lowerer().Body(tree), tree.Formatting ?? options, tree.LanguageVersion);
    }

    internal static bool Declaration(CsSymbol symbol) => symbol.Options.AsVar || symbol.Options.Type != null && !symbol.Options.AsNew;

    sealed class Lowerer
    {
        readonly Dictionary<SourceOrigin, SourceOrigin> origins = new(ReferenceEqualityComparer.Instance);
        internal CsBody Body(CsTree tree, bool attach = true) => Keep(new CsBody(tree.Items
            .Select((node, i) => Statement(node, "Items[" + i + "]", attach)).ToArray()), tree, attach);

        T Keep<T>(T result, CsNode source, bool attach) where T : CsNode
        {
            var extra = source switch { CsSymbol s => s.Options.Comments, CsInstruction i => i.Options.Comments, _ => Values<CsComment>.Empty };
            result = (T)(result with { Comments = new(source.Comments.Concat(extra)), Formatting = source.Formatting });
            if (!attach || source.Origin is not { } origin) return result;
            if (!origins.TryGetValue(origin, out var mapped))
            {
                CsNode original = origin.Original switch
                {
                    CsTree t => Body(t),
                    CsSymbol s => result is CsStatement ? Statement(s, "Original") : Expression(s, "Original"),
                    CsInstruction i => Instruction(i, "Original", true),
                    _ => origin.Original
                };
                mapped = origin with { Original = original };
                origins.Add(origin, mapped);
            }
            return (T)(result with { Origin = mapped });
        }

        CsExpression Expression(CsNode node, string path, bool origins = true)
        {
            if (node is CsExpression existing) return origins ? existing : (CsExpression)existing.Detach();
            Reject(node is not CsSymbol, path, "An expression value must be a symbol or expression.");
            var symbol = (CsSymbol)node;
            var o = symbol.Options;
            Reject(Declaration(symbol) || o.AsCompound || o.Using, path, "A declaration or compound symbol cannot be used as a scalar expression.");
            CsExpression value;
            if (o.AsNew) value = new(ExpressionKind.New, "", Arguments(o.Arguments, path, origins)) { Type = o.Type };
            else if (o.AsCall)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Expression(o.Target, path + ".Target", origins);
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name);
                value = target.Call(Arguments(o.Arguments, path, origins).ToArray());
            }
            else if (o.AsAssignment)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Expression(o.Target, path + ".Target", origins);
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name);
                value = Cs.Assign(target, Expression(o.Value!, path + ".Value", origins)) with { Operator = o.Operator };
            }
            else if (o.Operator != CsOperator.None)
            {
                var operands = o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]", origins)).ToArray();
                value = Operators.Unary(o.Operator) ? Cs.Unary(o.Operator, operands[0]) : Cs.Binary(o.Operator, operands[0], operands[1]);
            }
            else if (o.Target != null) value = Expression(o.Target, path + ".Target", origins).Member(symbol.Name!);
            else if (o.Value != null) value = Expression(o.Value, path + ".Value", origins);
            else value = Cs.Id(symbol.Name!);
            if (o.Await) value = Cs.Await(value);
            // A parsed expression-statement has separate expression and statement
            // envelopes. Keep both, so editing an argument does not reformat the line.
            var expressionSource = symbol.Origin?.Original is CsSymbol { Origin: not null } originalExpression
                ? symbol with { Origin = originalExpression.Origin } : symbol;
            return Keep(value, expressionSource, origins);
        }

        Values<CsExpression> Arguments(CsSymbol? compound, string path, bool origins) => compound == null
            ? Values<CsExpression>.Empty : new(compound.Options.Parts.Select((p, i) => Expression(p, path + ".Arguments[" + i + "]", origins)));

        CsStatement Statement(CsNode node, string path, bool origins = true)
        {
            if (node is CsStatement existing) return origins ? existing : (CsStatement)existing.Detach();
            if (node is CsInstruction instruction) return Instruction(instruction, path, origins);
            if (node is CsSymbol symbol && Declaration(symbol))
            {
                var o = symbol.Options;
                var value = o.Value == null ? null : Expression(o.Value, path + ".Value", origins);
                var local = new CsStatement(StatementKind.Local, value, symbol.Name!) { Type = o.Type, IsVar = o.AsVar };
                return Keep(o.Using ? new CsStatement(StatementKind.UsingDeclaration) { Resource = local, IsAwait = o.Await } : local, symbol, origins);
            }
            var expression = Expression(node, path, origins);
            Reject(!ExpressionKinds.StatementExpression(expression.Kind), path, "This symbol is a value; it needs an expression context, not a standalone statement.");
            var statementExpression = expression with { Comments = Values<CsComment>.Empty };
            // Constructed/projected nodes with only one envelope must not own it twice.
            if (node.Origin?.Original is CsSymbol { Origin: null })
                statementExpression = statementExpression with { Origin = null };
            return Keep(Cs.Statement(statementExpression), node, origins);
        }

        CsStatement Instruction(CsInstruction instruction, string path, bool origins)
        {
            var o = instruction.Options;
            CsExpression? Value(CsNode? node, string slot) => node == null ? null : Expression(node, path + "." + slot, origins);
            CsBody Block(CsTree? body) => Body(body!, origins);
            var result = instruction.Kind switch
            {
                InstructionKind.Block => new CsStatement(StatementKind.Block) { Body = Block(o.Body) },
                InstructionKind.Return => Cs.Return(Value(o.Value, "Value")),
                InstructionKind.Throw => o.Value == null ? Cs.Rethrow() : Cs.Throw(Value(o.Value, "Value")!),
                InstructionKind.Break => Cs.Break(),
                InstructionKind.Continue => Cs.Continue(),
                InstructionKind.Empty => Cs.Empty(),
                InstructionKind.If => Cs.If(Value(o.Condition, "Condition")!, Block(o.Body),
                    o.Else == null ? null : new CsStatement(StatementKind.Block) { Body = Block(o.Else) }),
                InstructionKind.While => Cs.While(Value(o.Condition, "Condition")!, Block(o.Body)),
                InstructionKind.DoWhile => Cs.DoWhile(Block(o.Body), Value(o.Condition, "Condition")!),
                InstructionKind.ForEach => new CsStatement(StatementKind.ForEach, Value(o.Value, "Value"), o.Symbol!.Name!)
                {
                    Type = o.Symbol.Options.Type,
                    IsVar = o.Symbol.Options.AsVar,
                    IsAwait = o.Await,
                    Body = Block(o.Body)
                },
                InstructionKind.For => Cs.For(Initializer(o.Initializer, path, origins), Value(o.Condition, "Condition"),
                    Arguments(o.Iterators, path + ".Iterators", origins), Block(o.Body)),
                _ => throw new InvalidModel("SYM001", path, "Unknown instruction kind.")
            };
            return Keep(result, instruction, origins);
        }

        CsForInitializer? Initializer(CsNode? node, string path, bool origins)
        {
            if (node == null) return null;
            if (node is CsForInitializer existing) return origins ? existing : (CsForInitializer)existing.Detach();
            if (node is CsSymbol symbol && Declaration(symbol)) return new CsForInitializer
            {
                Type = symbol.Options.Type,
                IsVar = symbol.Options.AsVar,
                Variables = new[] { Cs.Variable(symbol.Name!, symbol.Options.Value == null ? null : Expression(symbol.Options.Value, path + ".Initializer.Value", origins)) }
            };
            return Cs.ForExpressions(node is CsSymbol { Options.AsCompound: true } compound
                ? Arguments(compound, path + ".Initializer", origins).ToArray() : new[] { Expression(node, path + ".Initializer", origins) });
        }
    }
}
