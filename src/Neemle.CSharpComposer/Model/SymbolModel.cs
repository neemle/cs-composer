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
        // Options are checked first so an Opaque tree and a typed tree reject the same invalid options, and with
        // the same public exception CsDocument.Render throws for them (GENE-27 D9).
        if (options.Indent == null || options.Indent.Any(c => c is not (' ' or '\t')))
            throw new ArgumentException("Indent must contain only spaces or tabs.", nameof(options));
        var newline = Renderer.Newline(options) ?? (tree.Origin is { } source ? SourceNewline.Dominant(source.Snapshot.Text) : "\n");
        using var scope = Renderer.Enter(options);
        // A parsed tree whose only item is raw source (an Opaque parse) has no symbols to lower: it renders
        // that source as it is, so a tree the parser returned always renders.
        if (tree.Origin != null && tree.Items is [CsRaw raw]) return raw.Text;
        var errors = Validate(tree);
        if (errors.Count != 0) throw new GenValidationException(errors);
        var body = new Lowerer().Body(tree);
        string Once()
        {
            string text = Renderer.Statements(body, options, tree.LanguageVersion);
            return SourceNewline.Convert(text, newline);
        }
        // GENE-29 F3 (AD D1): a checked render reads its output back as a tree and compares the statements both trees
        // lower to.
        return !options.Validate ? Once() : MeaningGuard.Run(body, Once, text => new Lowerer().Body(
            SymbolParser.Parse(text, tree.LanguageVersion, tree.Origin?.Snapshot.Symbols).Root));
    }

    // An unchecked render (RenderOptions.Validate = false) reaches this with a symbol validation never saw: its
    // shape is checked first, so a malformed symbol is refused with SYM001 rather than failing in a writer (GENE-27 D10).
    internal static CsExpression ToExpression(CsNode node, string path = "Expression")
    {
        SymbolShape.Check(node, path);
        return new Lowerer().Expression(node, path);
    }

    internal static bool Declaration(CsSymbol symbol) => (symbol.Options.AsVar || symbol.Options.Type != null && !SymbolModes.TypeExpression(symbol.Options)) && symbol.Options.Operation == SymbolOperation.None;

    sealed partial class Lowerer
    {
        readonly Dictionary<SourceOrigin, SourceOrigin> origins = new(ReferenceEqualityComparer.Instance);
        internal CsBody Body(CsTree tree) => Keep(new CsBody(tree.Items
            .Select((node, i) => Statement(node, "Items[" + i + "]")).ToArray()), tree);

        T Keep<T>(T result, CsNode source) where T : CsNode
        {
            var extra = source switch { CsSymbol s => s.Options.Comments, CsInstruction i => i.Options.Comments, _ => Values<CsComment>.Empty };
            var trivia = source switch { CsSymbol s => s.Options.SyntaxTrivia, CsInstruction i => i.Options.SyntaxTrivia, _ => Values<CsSyntaxTrivia>.Empty };
            result = (T)(result with { Comments = new(source.Comments.Concat(extra).Concat(result.Comments).Distinct<CsComment>(ReferenceEqualityComparer.Instance)), SyntaxTrivia = new(source.SyntaxTrivia.Concat(trivia).Concat(result.SyntaxTrivia).Distinct<CsSyntaxTrivia>(ReferenceEqualityComparer.Instance)), SyntaxPosition = source.SyntaxPosition });
            if (source.Origin is not { } origin) return result;
            if (!origins.TryGetValue(origin, out var mapped))
            {
                CsNode original = origin.Original switch
                {
                    CsTree t => Body(t),
                    CsSymbol s => s.Options.Operation == SymbolOperation.Parameter ? Parameter(s, "Original") : s.Options.Operation == SymbolOperation.QueryClause ? QueryClause(s, "Original") : result is CsStatement ? Statement(s, "Original") : Expression(s, "Original"),
                    CsInstruction i => Instruction(i, "Original"),
                    _ => origin.Original
                };
                mapped = origin with { Original = original };
                origins.Add(origin, mapped);
            }
            return (T)(result with { Origin = mapped });
        }

        // Lowered symbols, so that a chain is lowered once even though each link's original is lowered for its
        // origin too (GENE-19 D10).
        readonly Dictionary<CsNode, CsExpression> lowered = new(ReferenceEqualityComparer.Instance);

        // A member-access, call, element-access or operator chain is lowered in a loop: the receivers are
        // collected down to the first one that is not a link, then each link is built on the one below it.
        internal CsExpression Expression(CsNode node, string path)
        {
            var links = new List<(CsSymbol Symbol, string Path)>();
            string runBase = path;
            while (node is CsSymbol { Options: { } options } symbol && !lowered.ContainsKey(node) && Spine.Next(options) is { } receiver)
            {
                links.Add((symbol, path));
                path = links.Count > Limits.MaxNestingDepth ? runBase + NodePathText.Run(links.Count) :
                    path + (ReferenceEquals(receiver, options.Target) ? ".Target" : ".Parts[0]");
                node = receiver;
            }
            var value = Scalar(node, path, null);
            for (int i = links.Count - 1; i >= 0; i--) value = Scalar(links[i].Symbol, links[i].Path, value);
            return value;
        }

        // One symbol lowered; `receiver` is its already lowered receiver when it is a chain link.
        CsExpression Scalar(CsNode node, string path, CsExpression? receiver)
        {
            if (node is CsExpression existing) return existing;
            if (lowered.TryGetValue(node, out var known)) return known;
            var result = Lower(node, path, receiver);
            lowered[node] = result;
            return result;
        }

        CsExpression Lower(CsNode node, string path, CsExpression? receiver)
        {
            Reject(node is not CsSymbol, path, "An expression value must be a symbol or expression.");
            var symbol = (CsSymbol)node;
            var o = symbol.Options;
            Reject(Declaration(symbol) || o.AsCompound || o.Using, path, "A declaration or compound symbol cannot be used as a scalar expression.");
            CsExpression value;
            CsExpression? Initializer() => o.Initializer == null ? null : Expression(o.Initializer, path + ".Initializer");
            CsExpression Receiver() => receiver ?? Expression(o.Target!, path + ".Target");
            if (o.Operation != SymbolOperation.None) value = Operation(symbol, path);
            else if (o.AsIndex) value = new(ExpressionKind.Index, "", new(new[] { Receiver() }.Concat(Arguments(o.Arguments, path)))) { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
            else if (o.AsCast) value = new(ExpressionKind.Cast, "", new[] { Expression(o.Value!, path + ".Value") }) { Type = o.Type };
            else if (o.AsTypeOf) value = new(ExpressionKind.TypeOf, "", Values<CsExpression>.Empty) { Type = o.Type };
            else if (o.AsDefault) value = new(ExpressionKind.Default, "", Values<CsExpression>.Empty) { Type = o.Type };
            else if (o.AsIs) value = new(ExpressionKind.Is, "", new[] { Expression(o.Value!, path + ".Value") }) { Type = o.Type, Pattern = o.Pattern };
            else if (o.AsConditional) value = new(ExpressionKind.Conditional, "", new(o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]"))));
            else if (o.AsArray) value = new(ExpressionKind.Array, "", Arguments(o.Arguments, path)) { Type = o.Type, ArrayRank = o.ArrayRank, Initializer = Initializer() };
            else if (o.AsInitializer) value = new(ExpressionKind.Initializer, "", new(o.Parts.Select((p, i) => Expression(p, path + ".Parts[" + i + "]"))));
            else if (o.AsParenthesized) value = new(ExpressionKind.Parenthesized, "", new[] { Expression(o.Value!, path + ".Value") });
            else if (o.AsNew) value = new(ExpressionKind.New, "", Arguments(o.Arguments, path)) { Type = o.Type, Initializer = Initializer() };
            else if (o.AsCall)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Receiver();
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
                value = target.Call(Arguments(o.Arguments, path).ToArray());
            }
            else if (o.AsAssignment)
            {
                var target = o.Target == null ? Cs.Id(symbol.Name!) : Expression(o.Target, path + ".Target");
                if (o.Target != null && symbol.Name != null) target = target.Member(symbol.Name) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
                value = Cs.Assign(target, Expression(o.Value!, path + ".Value")) with { Operator = o.Operator };
            }
            else if (o.Operator != CsOperator.None)
            {
                var operands = o.Parts.Select((p, i) => i == 0 && receiver != null ? receiver : Expression(p, path + ".Parts[" + i + "]")).ToArray();
                value = Operators.Unary(o.Operator) ? Cs.Unary(o.Operator, operands[0]) : Cs.Binary(o.Operator, operands[0], operands[1]);
            }
            else if (o.Target != null) value = Receiver().Member(symbol.Name!) with { ConditionalAccess = o.ConditionalAccess, PointerAccess = o.PointerAccess, AliasAccess = o.AliasAccess };
            else if (o.Value != null) value = Expression(o.Value, path + ".Value");
            else value = Cs.Id(symbol.Name!);
            if (o.TypeArguments.Count != 0) value = value with { TypeArguments = o.TypeArguments };
            if (o.Await) value = Cs.Await(value);
            // A parsed expression-statement has separate expression and statement
            // envelopes. Keep both, so editing an argument does not reformat the line.
            var expressionSource = symbol.Origin?.Original is CsSymbol { Origin: not null } originalExpression
                ? symbol with { Origin = originalExpression.Origin } : symbol;
            return Keep(value, expressionSource);
        }

        CsExpression Operation(CsSymbol symbol, string path)
        {
            var o = symbol.Options;
            Reject(o.Operation is SymbolOperation.Parameter or SymbolOperation.QueryClause, path, "Parameter/clause needs a signature/query context.");
            var kind = Enum.Parse<ExpressionKind>(o.Operation.ToString());
            if (o.Operation is SymbolOperation.Lambda or SymbolOperation.AnonymousMethod)
                return new(kind, "", Values<CsExpression>.Empty)
                {
                    Type = o.Type,
                    LambdaParameters = o.ParameterSymbols == null ? o.Parameters : new(o.ParameterSymbols.Options.Parts.Select((p, i) => Parameter(p, path + ".Parameters[" + i + "]"))),
                    LambdaAttributes = o.Attributes,
                    ReturnModifier = o.ReturnModifier,
                    IsAsync = o.Async,
                    IsStatic = o.Static,
                    HasParameterList = o.HasParameterList,
                    LambdaBody = o.Body == null ? CsBody.FromExpression(Expression(o.Value!, path + ".Value")) : Body(o.Body)
                };
            // Every lowering entry runs SymbolShape.Check first (GENE-27 D10), which requires a value for each
            // operation that reaches the last arm below.
            IEnumerable<CsNode> operands = o.Operation == SymbolOperation.Range
                ? new[] { o.Value, o.Target }.Select(n => n ?? new CsExpression(ExpressionKind.Omitted, "", Values<CsExpression>.Empty))
                : o.Operation is SymbolOperation.This or SymbolOperation.Base or SymbolOperation.SizeOf or SymbolOperation.TypeReference or SymbolOperation.Field or SymbolOperation.Query or SymbolOperation.StringLiteral or SymbolOperation.StringText ? Array.Empty<CsNode>()
                : o.Operation is SymbolOperation.Tuple or SymbolOperation.Collection or SymbolOperation.AnonymousObject or SymbolOperation.ImplicitIndex or SymbolOperation.StackAlloc or SymbolOperation.CollectionArguments or SymbolOperation.Declaration or SymbolOperation.InterpolatedString
                    ? o.Parts : new[] { o.Value! };
            return new(kind, o.Text ?? symbol.Name ?? "", new(operands.Select((n, i) => Expression(n, path + ".Parts[" + i + "]"))))
            {
                Type = o.Type,
                ArgumentModifier = o.ArgumentModifier,
                ArrayRank = o.ArrayRank,
                QueryClauses = o.Operation == SymbolOperation.Query && o.Parts.Count > 0 ? new(o.Parts.Select((p, i) => QueryClause(p, path + ".Parts[" + i + "]"))) : o.QueryClauses,
                Format = o.Format,
                Alignment = o.Alignment == null ? null : Expression(o.Alignment, path + ".Alignment"),
                StringStyle = o.StringStyle,
                Utf8 = o.Utf8,
                Initializer = o.Initializer == null ? null : Expression(o.Initializer, path + ".Initializer")
            };
        }

        Values<CsExpression> Arguments(CsSymbol? compound, string path) => compound == null
            ? Values<CsExpression>.Empty : new(compound.Options.Parts.Select((p, i) => Expression(p, path + ".Arguments[" + i + "]")));

        CsStatement Statement(CsNode node, string path)
        {
            if (node is CsDisabledText disabled) return Keep(new CsStatement(StatementKind.DisabledText) { Disabled = disabled with { Origin = null } }, disabled);
            if (node is CsDirective directive) return Keep(new CsStatement(StatementKind.Directive) { Directive = directive with { Origin = null, Comments = Values<CsComment>.Empty } }, directive);
            if (node is CsStatement existing) return existing;
            if (node is CsInstruction instruction) return Instruction(instruction, path);
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
                        Variables = new(symbols.Select(s => Cs.Variable(s.Name!, s.Options.Value == null ? null : Expression(s.Options.Value, path + ".Value"))))
                    }
                };
                return Keep(first.Using ? new CsStatement(StatementKind.UsingDeclaration) { Resource = grouped, IsAwait = first.Await } : grouped, compound);
            }
            if (node is CsSymbol symbol && Declaration(symbol))
            {
                var o = symbol.Options;
                var value = o.Value == null ? null : Expression(o.Value, path + ".Value");
                var local = new CsStatement(StatementKind.Local, value, symbol.Name!) { Type = o.Type, IsVar = o.AsVar, VariableModifiers = o.VariableModifiers };
                return Keep(o.Using ? new CsStatement(StatementKind.UsingDeclaration) { Resource = local, IsAwait = o.Await } : local, symbol);
            }
            var expression = Expression(node, path);
            Reject(!ExpressionKinds.StatementExpression(expression.Kind), path, "This symbol is a value; it needs an expression context, not a standalone statement.");
            var statementExpression = expression with { Comments = Values<CsComment>.Empty, SyntaxTrivia = Values<CsSyntaxTrivia>.Empty };
            // Constructed/projected nodes with only one envelope must not own it twice.
            if (node.Origin?.Original is CsSymbol { Origin: null })
                statementExpression = statementExpression with { Origin = null };
            return Keep(Cs.Statement(statementExpression), node);
        }

        // The block an else branch is written in, which writes no text of its own: a parsed else branch's body has an
        // origin (embedded when brace-less), and its block gets one over the same source, as the parser gives it (each
        // original lowered once, so an edit compares against it), so an edit in it keeps the rest of the if's source
        // and a brace-less branch stays brace-less (GENE-27 phase 4).
        readonly Dictionary<SourceOrigin, SourceOrigin> blocks = new(ReferenceEqualityComparer.Instance);

        CsStatement ElseBlock(CsBody body)
        {
            var block = new CsStatement(StatementKind.Block) { Body = body };
            if (body.Origin is not { } inner) return block;
            if (!blocks.TryGetValue(inner, out var wrapper))
                blocks.Add(inner, wrapper = SourceOrigin.Wrap(block with { Body = (CsBody)inner.Original with { Origin = inner } }, inner));
            return block with { Origin = wrapper };
        }

        CsStatement Instruction(CsInstruction instruction, string path)
        {
            var o = instruction.Options;
            CsExpression? Value(CsNode? node, string slot) => node == null ? null : Expression(node, path + "." + slot);
            CsBody Block(CsTree? body) => Body(body!);
            var result = instruction.Kind switch
            {
                InstructionKind.Switch => new CsStatement(StatementKind.Switch, Value(o.Value, "Value")) { Sections = o.Sections },
                InstructionKind.Try => new CsStatement(StatementKind.Try) { Body = Block(o.Body), Catches = o.Catches, Finally = o.Finally == null ? null : Block(o.Finally) },
                InstructionKind.Using => new CsStatement(StatementKind.Using, Value(o.Value, "Value")) { Resource = o.Initializer == null ? null : Statement(o.Initializer, path + ".Initializer"), Body = Block(o.Body), IsAwait = o.Await },
                InstructionKind.GotoCase => Cs.GotoCase(Value(o.Value, "Value")!),
                InstructionKind.GotoDefault => Cs.GotoDefault(),
                InstructionKind.LocalFunction => new CsStatement(StatementKind.LocalFunction) { Function = o.Function },
                InstructionKind.Lock or InstructionKind.Fixed or InstructionKind.Unsafe or InstructionKind.Checked or InstructionKind.Unchecked or InstructionKind.YieldReturn or InstructionKind.YieldBreak or InstructionKind.Goto or InstructionKind.Label =>
                    new CsStatement(Enum.Parse<StatementKind>(instruction.Kind.ToString()), Value(o.Value, "Value"), o.Name ?? "")
                    {
                        Body = o.Body == null ? null : Block(o.Body),
                        Resource = o.Initializer == null ? null : Statement(o.Initializer, path + ".Initializer")
                    },
                InstructionKind.Block => new CsStatement(StatementKind.Block) { Body = Block(o.Body) },
                InstructionKind.Return => Cs.Return(Value(o.Value, "Value")),
                InstructionKind.Throw => o.Value == null ? Cs.Rethrow() : Cs.Throw(Value(o.Value, "Value")!),
                InstructionKind.Break => Cs.Break() with { Text = o.Name ?? "" },
                InstructionKind.Continue => Cs.Continue() with { Text = o.Name ?? "" },
                InstructionKind.Empty => Cs.Empty(),
                InstructionKind.If => Cs.If(Value(o.Condition, "Condition")!, Block(o.Body), o.Else == null ? null : ElseBlock(Block(o.Else))),
                InstructionKind.While => Cs.While(Value(o.Condition, "Condition")!, Block(o.Body)),
                InstructionKind.DoWhile => Cs.DoWhile(Block(o.Body), Value(o.Condition, "Condition")!),
                InstructionKind.ForEach => new CsStatement(StatementKind.ForEach, Value(o.Value, "Value"), o.Symbol!.Name ?? "")
                {
                    Binding = Declaration(o.Symbol) ? null : Expression(o.Symbol, path + ".Symbol"),
                    Type = o.Symbol.Options.Type,
                    IsVar = o.Symbol.Options.AsVar,
                    VariableModifiers = o.Symbol.Options.VariableModifiers,
                    IsAwait = o.Await,
                    Body = Block(o.Body)
                },
                // InstructionKind.For. SymbolShape.Check, which runs before every lowering (GENE-27 D10), already
                // rejects an undefined instruction kind with SYM001, so no other value reaches this arm.
                _ => Cs.For(Initializer(o.Initializer, path), Value(o.Condition, "Condition"),
                    Arguments(o.Iterators, path + ".Iterators"), Block(o.Body))
            };
            return Keep(result, instruction);
        }

        CsForInitializer? Initializer(CsNode? node, string path)
        {
            if (node == null) return null;
            if (node is CsForInitializer existing) return existing;
            if (node is CsSymbol symbol && Declaration(symbol)) return new CsForInitializer
            {
                Type = symbol.Options.Type,
                IsVar = symbol.Options.AsVar,
                VariableModifiers = symbol.Options.VariableModifiers,
                Variables = new[] { Cs.Variable(symbol.Name!, symbol.Options.Value == null ? null : Expression(symbol.Options.Value, path + ".Initializer.Value")) }
            };
            return Cs.ForExpressions(node is CsSymbol { Options.AsCompound: true } compound
                ? Arguments(compound, path + ".Initializer").ToArray() : new[] { Expression(node, path + ".Initializer") });
        }
    }
}
