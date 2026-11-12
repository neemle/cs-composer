using System.Text;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal static class SymbolParser
{
    internal static ParseResult<CsTree> Parse(string source, CsLanguageVersion version, Values<string>? symbols = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = SourceSnapshot.Decode(new UTF8Encoding(false, true).GetBytes(source), null);
        var parsed = DocumentParser.Fragment(snapshot, new CsRaw(source), new(ParseKind.Statements, version) { PreprocessorSymbols = symbols ?? Values<string>.Empty });
        if (parsed.Root is not CsBody body || parsed.Coverage != ParseCoverage.Typed)
            return new(new CsTree(new CsNode[] { new CsRaw(source) }) { LanguageVersion = version }, parsed.Diagnostics, parsed.Coverage);
        var tree = Tree(body) with { LanguageVersion = version };
        tree = tree with { Origin = new(snapshot, tree, 0, source.Length) { CoreStart = 0, CoreEnd = source.Length, ListStart = 0, ListEnd = source.Length } };
        var diagnostics = tree.Validate().Select(e => new ParseDiagnostic(e.Code, DiagnosticSeverity.Error, 0, 0, 1, 1, e.Message)).ToArray();
        return new(tree, new(diagnostics), diagnostics.Length == 0 ? ParseCoverage.Typed : ParseCoverage.Partial);
    }

    static T Source<T>(T result, CsNode old) where T : CsNode
    {
        result = (T)(result with { Comments = new(old.Comments.Concat(result.Comments).Distinct<CsComment>(ReferenceEqualityComparer.Instance)), SyntaxPosition = old.SyntaxPosition, SyntaxTrivia = new(old.SyntaxTrivia.Concat(result.SyntaxTrivia).Distinct<CsSyntaxTrivia>(ReferenceEqualityComparer.Instance)), Formatting = old.Formatting });
        return old.Origin is not { } origin ? result : (T)(result with { Origin = origin with { Original = result } });
    }

    static CsTree Tree(CsBody body) => Source(new CsTree(new(body.Statements.Select(Statement))), body);

    internal static CsNode Statement(CsStatement statement)
    {
        var s = statement;
        CsNode? Expr(CsExpression? e) => e == null ? null : Expression(e);
        CsTree? Body(CsBody? b) => b == null ? null : Tree(b);
        CsSymbol Variable(CsStatement local, bool use = false, bool await = false) => new(local.Text, new()
        {
            VariableModifiers = local.VariableModifiers,
            Type = local.Type,
            AsVar = local.IsVar,
            Using = use,
            Await = await,
            Value = Expr(local.Expression)
        });
        if (s.Kind == StatementKind.DisabledText) return Source(s.Disabled!, s);
        if (s.Kind == StatementKind.Directive) return Source(s.Directive!, s);
        if (s.Kind == StatementKind.LocalGroup)
            return Source(Cs.Compound(s.ForInitializer!.Variables.Select((v, i) => (CsNode)new CsSymbol(v.Name, new()
            {
                Type = i == 0 ? s.ForInitializer.Type : (CsType?)s.ForInitializer.Type?.Detach(),
                VariableModifiers = s.ForInitializer.VariableModifiers,
                Value = v.Initializer == null ? null : Expression(v.Initializer)
            })).ToArray()), s);
        if (s.Kind == StatementKind.Local) return Source(Variable(s), s);
        if (s.Kind == StatementKind.UsingDeclaration)
        {
            if (s.Resource!.Kind == StatementKind.Local) return Source(Variable(s.Resource, true, s.IsAwait), s);
            var group = (CsSymbol)Statement(s.Resource);
            group = group with { Options = group.Options with { Parts = new(group.Options.Parts.Cast<CsSymbol>().Select(v => (CsNode)(v with { Options = v.Options with { Using = true, Await = s.IsAwait } }))) } };
            return Source(group, s);
        }
        if (s.Kind == StatementKind.Expression) return Source(Expression(s.Expression!), s);
        var kind = s.Kind switch
        {
            StatementKind.Block => InstructionKind.Block,
            StatementKind.Return => InstructionKind.Return,
            StatementKind.Throw or StatementKind.Rethrow => InstructionKind.Throw,
            StatementKind.Break => InstructionKind.Break,
            StatementKind.Continue => InstructionKind.Continue,
            StatementKind.Empty => InstructionKind.Empty,
            StatementKind.If => InstructionKind.If,
            StatementKind.For => InstructionKind.For,
            StatementKind.ForEach => InstructionKind.ForEach,
            StatementKind.While => InstructionKind.While,
            StatementKind.DoWhile => InstructionKind.DoWhile,
            _ => Enum.TryParse<InstructionKind>(s.Kind.ToString(), out var same) ? same : (InstructionKind?)null
        };
        if (kind == null) return s; // Existing typed switch/try/etc remain interoperable nodes.
        var options = new CsInstructionOptions
        {
            Value = s.Kind is StatementKind.Return or StatementKind.Throw or StatementKind.ForEach or StatementKind.Switch or StatementKind.Using or StatementKind.Lock or StatementKind.YieldReturn or StatementKind.GotoCase ? Expr(s.Expression) : null,
            Condition = s.Kind is StatementKind.If or StatementKind.For or StatementKind.While or StatementKind.DoWhile ? Expr(s.Expression) : null,
            Body = Body(s.Body),
            Await = s.IsAwait,
            Else = s.Else == null ? null : s.Else.Kind == StatementKind.Block ? Body(s.Else.Body) : Cs.Tree(Statement(s.Else)),
            Symbol = s.Kind == StatementKind.ForEach ? s.Binding != null ? Expression(s.Binding) : new CsSymbol(s.Text, new() { Type = s.Type, AsVar = s.IsVar, VariableModifiers = s.VariableModifiers }) : null,
            Name = s.Kind is StatementKind.Goto or StatementKind.Label or StatementKind.Break or StatementKind.Continue && s.Text != "" ? s.Text : null,
            Sections = new(s.Sections.Select(section => section with { Body = new CsBody { Tree = Tree(section.Body) } })),
            Catches = new(s.Catches.Select(handler => handler with { Body = new CsBody { Tree = Tree(handler.Body) } })),
            Finally = Body(s.Finally),
            Function = s.Function,
            Initializer = s.Resource != null ? Statement(s.Resource) : Initializer(s.ForInitializer),
            Iterators = s.Kind == StatementKind.For ? Cs.Compound(s.Iterators.Select(Expression).Cast<CsNode>().ToArray()) : null
        };
        return Source(new CsInstruction(kind.Value, options), s);
    }

    static CsNode? Initializer(CsForInitializer? initializer)
    {
        if (initializer == null) return null;
        if (initializer.Variables.Count == 1)
        {
            var v = initializer.Variables[0];
            return new CsSymbol(v.Name, new()
            {
                Type = initializer.Type,
                VariableModifiers = initializer.VariableModifiers,
                AsVar = initializer.IsVar,
                Value = v.Initializer == null ? null : Expression(v.Initializer)
            });
        }
        return initializer.Variables.Count == 0 ? Cs.Compound(initializer.Expressions.Select(Expression).Cast<CsNode>().ToArray()) : initializer;
    }

    static CsSymbol Expression(CsExpression e)
    {
        var arguments = e.Arguments;
        CsSymbolOptions options;
        string? name = null;
        switch (e.Kind)
        {
            case ExpressionKind.Query:
                options = new() { Operation = SymbolOperation.Query, Parts = new(e.QueryClauses.Select(QueryClause).Cast<CsNode>()) }; break;
            case ExpressionKind.Lambda:
            case ExpressionKind.AnonymousMethod:
                options = new()
                {
                    Operation = e.Kind == ExpressionKind.Lambda ? SymbolOperation.Lambda : SymbolOperation.AnonymousMethod,
                    Type = e.Type,
                    ParameterSymbols = Cs.Compound(e.LambdaParameters.Select(Parameter).Cast<CsNode>().ToArray()),
                    Attributes = e.LambdaAttributes,
                    ReturnModifier = e.ReturnModifier,
                    Async = e.IsAsync,
                    Static = e.IsStatic,
                    HasParameterList = e.HasParameterList,
                    Value = e.LambdaBody?.Expression == null ? null : Expression(e.LambdaBody.Expression),
                    Body = e.LambdaBody?.Expression != null ? null : Tree(e.LambdaBody!)
                }; break;
            case ExpressionKind.Identifier: name = e.Text; options = new(); break;
            case ExpressionKind.Member: name = e.Text; options = new() { Target = Expression(arguments[0]), ConditionalAccess = e.ConditionalAccess, PointerAccess = e.PointerAccess, AliasAccess = e.AliasAccess }; break;
            case ExpressionKind.Invocation:
                options = new() { AsCall = true, Target = Expression(arguments[0]), Arguments = Cs.Compound(arguments.Skip(1).Select(Expression).Cast<CsNode>().ToArray()) }; break;
            case ExpressionKind.New:
                options = new() { Type = e.Type, AsNew = true, Arguments = Cs.Compound(arguments.Select(Expression).Cast<CsNode>().ToArray()), Initializer = e.Initializer == null ? null : Expression(e.Initializer) }; break;
            case ExpressionKind.Index:
                options = new()
                {
                    AsIndex = true,
                    Target = Expression(arguments[0]),
                    ConditionalAccess = e.ConditionalAccess,
                    Arguments = Cs.Compound(arguments.Skip(1).Select(Expression).Cast<CsNode>().ToArray())
                }; break;
            case ExpressionKind.Cast:
                options = new() { AsCast = true, Type = e.Type, Value = Expression(arguments[0]) }; break;
            case ExpressionKind.TypeOf:
                options = new() { AsTypeOf = true, Type = e.Type }; break;
            case ExpressionKind.Default:
                options = new() { AsDefault = true, Type = e.Type }; break;
            case ExpressionKind.Is:
                options = new() { AsIs = true, Type = e.Type, Pattern = e.Pattern, Value = Expression(arguments[0]) }; break;
            case ExpressionKind.Conditional:
                options = new() { AsConditional = true, Parts = new(arguments.Select(Expression).Cast<CsNode>()) }; break;
            case ExpressionKind.Array:
                options = new()
                {
                    AsArray = true,
                    Type = e.Type,
                    ArrayRank = e.ArrayRank,
                    Arguments = Cs.Compound(arguments.Select(Expression).Cast<CsNode>().ToArray()),
                    Initializer = e.Initializer == null ? null : Expression(e.Initializer)
                }; break;
            case ExpressionKind.Initializer:
                options = new() { AsInitializer = true, Parts = new(arguments.Select(Expression).Cast<CsNode>()) }; break;
            case ExpressionKind.Parenthesized:
                options = new() { AsParenthesized = true, Value = Expression(arguments[0]) }; break;
            case ExpressionKind.Assignment:
                options = new() { AsAssignment = true, Operator = e.Operator, Target = Expression(arguments[0]), Value = Expression(arguments[1]) }; break;
            case ExpressionKind.Binary:
            case ExpressionKind.Unary:
                options = new() { Operator = e.Operator, Parts = new(arguments.Select(Expression).Cast<CsNode>()) }; break;
            case ExpressionKind.Await:
                options = new() { Await = true, Value = Expression(arguments[0]) }; break;
            default:
                if (Enum.TryParse<SymbolOperation>(e.Kind.ToString(), out var operation) && operation != SymbolOperation.None)
                {
                    bool parts = operation is SymbolOperation.Tuple or SymbolOperation.Collection or SymbolOperation.AnonymousObject or SymbolOperation.StackAlloc or SymbolOperation.ImplicitIndex or SymbolOperation.CollectionArguments or SymbolOperation.Declaration or SymbolOperation.InterpolatedString;
                    bool range = operation == SymbolOperation.Range;
                    bool stringText = operation is SymbolOperation.StringLiteral or SymbolOperation.StringText;
                    name = stringText || e.Text == "" ? null : e.Text;
                    options = new()
                    {
                        Operation = operation,
                        Type = e.Type,
                        Text = stringText ? e.Text : null,
                        StringStyle = e.StringStyle,
                        Utf8 = e.Utf8,
                        Format = e.Format,
                        Alignment = e.Alignment == null ? null : Expression(e.Alignment),
                        ArgumentModifier = e.ArgumentModifier,
                        QueryClauses = e.QueryClauses,
                        AsVar = operation == SymbolOperation.Declaration && e.Type == null,
                        Initializer = e.Initializer == null ? null : Expression(e.Initializer),
                        Parts = parts ? new(arguments.Select(Expression).Cast<CsNode>()) : Values<CsNode>.Empty,
                        Value = parts || arguments.Count == 0 || arguments[0].Kind == ExpressionKind.Omitted ? null : Expression(arguments[0]),
                        Target = range && arguments[1].Kind != ExpressionKind.Omitted ? Expression(arguments[1]) : null
                    };
                }
                else options = new() { Value = e with { Comments = Values<CsComment>.Empty, SyntaxTrivia = Values<CsSyntaxTrivia>.Empty } };
                break;
        }
        return Source(new CsSymbol(name, options with { TypeArguments = e.TypeArguments }), e);
    }
    static CsSymbol Parameter(CsLambdaParameter p) => Source(new CsSymbol(p.Name, new()
    {
        Operation = SymbolOperation.Parameter,
        Type = p.Type,
        ParameterModifier = p.Modifier,
        Attributes = p.Attributes,
        Value = p.Default == null ? null : Expression(p.Default)
    }), p);

    static CsSymbol QueryClause(CsQueryClause q) => Source(new CsSymbol(q.Name, new()
    {
        Operation = SymbolOperation.QueryClause,
        QueryKind = q.Kind,
        Type = q.Type,
        Into = q.Into,
        Descending = q.Descending,
        Value = q.Value == null ? null : Expression(q.Value),
        Target = q.Key == null ? null : Expression(q.Key),
        EqualityKey = q.EqualsKey == null ? null : Expression(q.EqualsKey),
        Clauses = q.Clauses.Count == 0 ? null : Cs.Compound(q.Clauses.Select(QueryClause).Cast<CsNode>().ToArray())
    }), q);
}
