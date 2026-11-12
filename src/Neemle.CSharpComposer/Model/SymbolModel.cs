using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;

namespace Neemle.CSharpComposer.Model;

internal static partial class SymbolModel
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

    internal static CsExpression ToExpression(CsNode node) => new Lowerer().Expression(node, "Expression");

    internal static bool Declaration(CsSymbol symbol) => (symbol.Options.AsVar || symbol.Options.Type != null && !SymbolModes.TypeExpression(symbol.Options)) && symbol.Options.Operation == SymbolOperation.None;

    sealed partial class Lowerer
    {
        readonly Dictionary<SourceOrigin, SourceOrigin> origins = new(ReferenceEqualityComparer.Instance);
        internal CsBody Body(CsTree tree, bool attach = true) => Keep(new CsBody(tree.Items
            .Select((node, i) => Statement(node, "Items[" + i + "]", attach)).ToArray()), tree, attach);

        T Keep<T>(T result, CsNode source, bool attach) where T : CsNode
        {
            var extra = source switch { CsSymbol s => s.Options.Comments, CsInstruction i => i.Options.Comments, _ => Values<CsComment>.Empty };
            var trivia = source switch { CsSymbol s => s.Options.SyntaxTrivia, CsInstruction i => i.Options.SyntaxTrivia, _ => Values<CsSyntaxTrivia>.Empty };
            result = (T)(result with { Comments = new(source.Comments.Concat(extra).Concat(result.Comments).Distinct<CsComment>(ReferenceEqualityComparer.Instance)), SyntaxTrivia = new(source.SyntaxTrivia.Concat(trivia).Concat(result.SyntaxTrivia).Distinct<CsSyntaxTrivia>(ReferenceEqualityComparer.Instance)), SyntaxPosition = source.SyntaxPosition, Formatting = source.Formatting });
            if (!attach || source.Origin is not { } origin) return result;
            if (!origins.TryGetValue(origin, out var mapped))
            {
                CsNode original = origin.Original switch
                {
                    CsTree t => Body(t),
                    CsSymbol s => s.Options.Operation == SymbolOperation.Parameter ? Parameter(s, "Original", true) : s.Options.Operation == SymbolOperation.QueryClause ? QueryClause(s, "Original", true) : result is CsStatement ? Statement(s, "Original") : Expression(s, "Original"),
                    CsInstruction i => Instruction(i, "Original", true),
                    _ => origin.Original
                };
                mapped = origin with { Original = original };
                origins.Add(origin, mapped);
            }
            return (T)(result with { Origin = mapped });
        }

        internal CsExpression Expression(CsNode node, string path, bool origins = true)
        {
            if (node is CsExpression existing) return origins ? existing : (CsExpression)existing.Detach();
            Reject(node is not CsSymbol, path, "An expression value must be a symbol or expression.");
            var symbol = (CsSymbol)node;
            var o = symbol.Options;
            Reject(Declaration(symbol) || o.AsCompound || o.Using, path, "A declaration or compound symbol cannot be used as a scalar expression.");
            CsExpression value;
            CsExpression? Initializer() => o.Initializer == null ? null : Expression(o.Initializer, path + ".Initializer", origins);
            if (o.Operation != SymbolOperation.None) value = Operation(symbol, path, origins);
            else if (o.AsIndex) value = new(ExpressionKind.Index, "", new(new[] { Expression(o.Target!, path + ".Target", origins) }.Concat(Arguments(o.Arguments, path, origins)))) { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
            else if (o.AsCast) value = new(ExpressionKind.Cast, "", new[] { Expression(o.Value!, path + ".Value", origins) }) { Type = o.Type };
            else if (o.AsTypeOf) value = new(ExpressionKind.TypeOf, "", Values<CsExpression>.Empty) { Type = o.Type };
            else if (o.AsDefault) value = new(ExpressionKind.Default, "", Values<CsExpression>.Empty) { Type = o.Type };
            else if (o.AsIs) value = new(ExpressionKind.Is, "", new[] { Expression(o.Value!, path + ".Value", origins) }) { Type = o.Type, Pattern = o.Pattern };
            else if (o.AsConditional) value = new(ExpressionKind.Conditional, "", new(o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]", origins))));
            else if (o.AsArray) value = new(ExpressionKind.Array, "", Arguments(o.Arguments, path, origins)) { Type = o.Type, ArrayRank = o.ArrayRank, Initializer = Initializer() };
            else if (o.AsInitializer) value = new(ExpressionKind.Initializer, "", new(o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]", origins))));
            else if (o.AsParenthesized) value = new(ExpressionKind.Parenthesized, "", new[] { Expression(o.Value!, path + ".Value", origins) });
            else if (o.AsNew) value = new(ExpressionKind.New, "", Arguments(o.Arguments, path, origins)) { Type = o.Type, Initializer = Initializer() };
            else if (o.AsCall)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Expression(o.Target, path + ".Target", origins);
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
                value = target.Call(Arguments(o.Arguments, path, origins).ToArray());
            }
            else if (o.AsAssignment)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Expression(o.Target, path + ".Target", origins);
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
                value = Cs.Assign(target, Expression(o.Value!, path + ".Value", origins)) with { Operator = o.Operator };
            }
            else if (o.Operator != CsOperator.None)
            {
                var operands = o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]", origins)).ToArray();
                value = Operators.Unary(o.Operator) ? Cs.Unary(o.Operator, operands[0]) : Cs.Binary(o.Operator, operands[0], operands[1]);
            }
            else if (o.Target != null) value = Expression(o.Target, path + ".Target", origins).Member(symbol.Name!) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
            else if (o.Value != null) value = Expression(o.Value, path + ".Value", origins);
            else value = Cs.Id(symbol.Name!);
            if (o.TypeArguments.Count != 0) value = value with { TypeArguments = o.TypeArguments };
            if (o.Await) value = Cs.Await(value);
            // A parsed expression-statement has separate expression and statement
            // envelopes. Keep both, so editing an argument does not reformat the line.
            var expressionSource = symbol.Origin?.Original is CsSymbol { Origin: not null } originalExpression
                ? symbol with { Origin = originalExpression.Origin } : symbol;
            return Keep(value, expressionSource, origins);
        }

        CsExpression Operation(CsSymbol symbol, string path, bool attach)
        {
            var o = symbol.Options;
            Reject(o.Operation is SymbolOperation.Parameter or SymbolOperation.QueryClause, path, "Parameter/clause needs a signature/query context.");
            var kind = Enum.Parse<ExpressionKind>(o.Operation.ToString());
            if (o.Operation is SymbolOperation.Lambda or SymbolOperation.AnonymousMethod)
                return new(kind, "", Values<CsExpression>.Empty)
                {
                    Type = o.Type,
                    LambdaParameters = o.ParameterSymbols == null ? o.Parameters : new(o.ParameterSymbols.Options.Parts.Select((p, i) => Parameter(p, path + ".Parameters[" + i + "]", attach))),
                    LambdaAttributes = o.Attributes,
                    ReturnModifier = o.ReturnModifier,
                    IsAsync = o.Async,
                    IsStatic = o.Static,
                    HasParameterList = o.HasParameterList,
                    LambdaBody = o.Body == null ? CsBody.FromExpression(Expression(o.Value!, path + ".Value", attach)) : Body(o.Body, attach)
                };
            IEnumerable<CsNode> operands = o.Operation == SymbolOperation.Range
                ? new[] { o.Value, o.Target }.Select(n => n ?? new CsExpression(ExpressionKind.Omitted, "", Values<CsExpression>.Empty))
                : o.Operation is SymbolOperation.This or SymbolOperation.Base or SymbolOperation.SizeOf or SymbolOperation.TypeReference or SymbolOperation.Field or SymbolOperation.Query or SymbolOperation.StringLiteral or SymbolOperation.StringText ? Array.Empty<CsNode>()
                : o.Operation is SymbolOperation.Tuple or SymbolOperation.Collection or SymbolOperation.AnonymousObject or SymbolOperation.ImplicitIndex or SymbolOperation.StackAlloc or SymbolOperation.CollectionArguments or SymbolOperation.Declaration or SymbolOperation.InterpolatedString
                    ? o.Parts : o.Value == null ? Array.Empty<CsNode>() : new[] { o.Value };
            return new(kind, o.Text ?? symbol.Name ?? "", new(operands.Select((n, i) => Expression(n, path + ".Parts[" + i + "]", attach))))
            {
                Type = o.Type,
                ArgumentModifier = o.ArgumentModifier,
                ArrayRank = o.ArrayRank,
                QueryClauses = o.Operation == SymbolOperation.Query && o.Parts.Count > 0 ? new(o.Parts.Select((p, i) => QueryClause(p, path + ".Parts[" + i + "]", attach))) : o.QueryClauses,
                Format = o.Format,
                Alignment = o.Alignment == null ? null : Expression(o.Alignment, path + ".Alignment", attach),
                StringStyle = o.StringStyle,
                Utf8 = o.Utf8,
                Initializer = o.Initializer == null ? null : Expression(o.Initializer, path + ".Initializer", attach)
            };
        }

        Values<CsExpression> Arguments(CsSymbol? compound, string path, bool origins) => compound == null
            ? Values<CsExpression>.Empty : new(compound.Options.Parts.Select((p, i) => Expression(p, path + ".Arguments[" + i + "]", origins)));

        CsStatement Statement(CsNode node, string path, bool origins = true)
        {
            if (node is CsDisabledText disabled) return Keep(new CsStatement(StatementKind.DisabledText) { Disabled = disabled with { Origin = null } }, disabled, origins);
            if (node is CsDirective directive) return Keep(new CsStatement(StatementKind.Directive) { Directive = directive with { Origin = null, Comments = Values<CsComment>.Empty } }, directive, origins);
            if (node is CsStatement existing) return origins ? existing : (CsStatement)existing.Detach();
            if (node is CsInstruction instruction) return Instruction(instruction, path, origins);
            if (node is CsSymbol { Options.AsCompound: true } compound)
            {
                var symbols = compound.Options.Parts.OfType<CsSymbol>().ToArray();
                Reject(symbols.Length == 0 || symbols.Length != compound.Options.Parts.Count || symbols.Any(s => !Declaration(s)), path, "A statement compound requires declaration symbols of the same type.");
                var first = symbols[0].Options;
                Reject(symbols.Any(s => s.Options.AsVar || s.Options.Type != first.Type || s.Options.VariableModifiers != first.VariableModifiers || s.Options.Using != first.Using || s.Options.Await != first.Await), path, "Declaration group requires one explicit type and consistent modifiers.");
                var grouped = new CsStatement(StatementKind.LocalGroup)
                {
                    ForInitializer = new()
                    {
                        Type = first.Type,
                        VariableModifiers = first.VariableModifiers,
                        Variables = new(symbols.Select(s => Cs.Variable(s.Name!, s.Options.Value == null ? null : Expression(s.Options.Value, path + ".Value", origins))))
                    }
                };
                return Keep(first.Using ? new CsStatement(StatementKind.UsingDeclaration) { Resource = grouped, IsAwait = first.Await } : grouped, compound, origins);
            }
            if (node is CsSymbol symbol && Declaration(symbol))
            {
                var o = symbol.Options;
                var value = o.Value == null ? null : Expression(o.Value, path + ".Value", origins);
                var local = new CsStatement(StatementKind.Local, value, symbol.Name!) { Type = o.Type, IsVar = o.AsVar, VariableModifiers = o.VariableModifiers };
                return Keep(o.Using ? new CsStatement(StatementKind.UsingDeclaration) { Resource = local, IsAwait = o.Await } : local, symbol, origins);
            }
            var expression = Expression(node, path, origins);
            Reject(!ExpressionKinds.StatementExpression(expression.Kind), path, "This symbol is a value; it needs an expression context, not a standalone statement.");
            var statementExpression = expression with { Comments = Values<CsComment>.Empty, SyntaxTrivia = Values<CsSyntaxTrivia>.Empty };
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
                InstructionKind.Switch => new CsStatement(StatementKind.Switch, Value(o.Value, "Value")) { Sections = o.Sections },
                InstructionKind.Try => new CsStatement(StatementKind.Try) { Body = Block(o.Body), Catches = o.Catches, Finally = o.Finally == null ? null : Block(o.Finally) },
                InstructionKind.Using => new CsStatement(StatementKind.Using, Value(o.Value, "Value")) { Resource = o.Initializer == null ? null : Statement(o.Initializer, path + ".Initializer", origins), Body = Block(o.Body), IsAwait = o.Await },
                InstructionKind.GotoCase => Cs.GotoCase(Value(o.Value, "Value")!),
                InstructionKind.GotoDefault => Cs.GotoDefault(),
                InstructionKind.LocalFunction => new CsStatement(StatementKind.LocalFunction) { Function = o.Function },
                InstructionKind.Lock or InstructionKind.Fixed or InstructionKind.Unsafe or InstructionKind.Checked or InstructionKind.Unchecked or InstructionKind.YieldReturn or InstructionKind.YieldBreak or InstructionKind.Goto or InstructionKind.Label =>
                    new CsStatement(Enum.Parse<StatementKind>(instruction.Kind.ToString()), Value(o.Value, "Value"), o.Name ?? "")
                    {
                        Body = o.Body == null ? null : Block(o.Body),
                        Resource = o.Initializer == null ? null : Statement(o.Initializer, path + ".Initializer", origins)
                    },
                InstructionKind.Block => new CsStatement(StatementKind.Block) { Body = Block(o.Body) },
                InstructionKind.Return => Cs.Return(Value(o.Value, "Value")),
                InstructionKind.Throw => o.Value == null ? Cs.Rethrow() : Cs.Throw(Value(o.Value, "Value")!),
                InstructionKind.Break => Cs.Break() with { Text = o.Name ?? "" },
                InstructionKind.Continue => Cs.Continue() with { Text = o.Name ?? "" },
                InstructionKind.Empty => Cs.Empty(),
                InstructionKind.If => Cs.If(Value(o.Condition, "Condition")!, Block(o.Body),
                    o.Else == null ? null : new CsStatement(StatementKind.Block) { Body = Block(o.Else) }),
                InstructionKind.While => Cs.While(Value(o.Condition, "Condition")!, Block(o.Body)),
                InstructionKind.DoWhile => Cs.DoWhile(Block(o.Body), Value(o.Condition, "Condition")!),
                InstructionKind.ForEach => new CsStatement(StatementKind.ForEach, Value(o.Value, "Value"), o.Symbol!.Name ?? "")
                {
                    Binding = Declaration(o.Symbol) ? null : Expression(o.Symbol, path + ".Symbol", origins),
                    Type = o.Symbol.Options.Type,
                    IsVar = o.Symbol.Options.AsVar,
                    VariableModifiers = o.Symbol.Options.VariableModifiers,
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
                VariableModifiers = symbol.Options.VariableModifiers,
                Variables = new[] { Cs.Variable(symbol.Name!, symbol.Options.Value == null ? null : Expression(symbol.Options.Value, path + ".Initializer.Value", origins)) }
            };
            return Cs.ForExpressions(node is CsSymbol { Options.AsCompound: true } compound
                ? Arguments(compound, path + ".Initializer", origins).ToArray() : new[] { Expression(node, path + ".Initializer", origins) });
        }
    }
}
