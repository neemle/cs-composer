using System.Text;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal static class SymbolParser
{
    internal static ParseResult<CsTree> Parse(string source, CsLanguageVersion version, Values<string>? symbols = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = SourceSnapshot.Decode(new UTF8Encoding(false, true).GetBytes(source), null) with { Symbols = symbols ?? Values<string>.Empty };
        var parsed = DocumentParser.Fragment(snapshot, new CsRaw(source), new(ParseKind.Statements, version) { PreprocessorSymbols = symbols ?? Values<string>.Empty }, cancellation);
        if (parsed.Root is not CsBody body || parsed.Coverage != ParseCoverage.Typed)
            return new(Opaque(snapshot, version), parsed.Diagnostics, parsed.Coverage);
        var tree = Tree(body) with { LanguageVersion = version };
        // The symbol model nests deeper than the statement model it is built from, so it is measured again.
        if (DocumentParser.Height(tree, Limits.MaxNestingDepth) > Limits.MaxNestingDepth)
            return new(Opaque(snapshot, version),
                new(new ParseDiagnostic("PARSE002", DiagnosticSeverity.Warning, 0, source.Length, 1, 1, DocumentParser.NestingMessage)), ParseCoverage.Opaque);
        tree = tree with { Origin = new(snapshot, tree, 0, source.Length) { CoreStart = 0, CoreEnd = source.Length, ListStart = 0, ListEnd = source.Length } };
        var diagnostics = tree.Validate().Select(e => new ParseDiagnostic(e.Code, DiagnosticSeverity.Error, 0, 0, 1, 1, e.Message)).ToArray();
        // A tree that validation rejects is never returned typed, because rendering it would throw: the source is
        // kept whole as raw instead, with the errors as diagnostics (the document parser's rule, GENE-19).
        return diagnostics.Length == 0 ? new(tree, Values<ParseDiagnostic>.Empty, ParseCoverage.Typed) :
            new(Opaque(snapshot, version), new(diagnostics), ParseCoverage.Opaque);
    }

    // The whole source kept as one raw item. Both the item and the tree carry the source span, so the untouched
    // tree renders that source byte for byte (the raw item lowers to a raw statement).
    static CsTree Opaque(SourceSnapshot snapshot, CsLanguageVersion version)
    {
        int length = snapshot.Text.Length;
        var raw = new CsRaw(snapshot.Text);
        raw = raw with { Origin = new(snapshot, raw, 0, length) { CoreStart = 0, CoreEnd = length } };
        var tree = new CsTree(new CsNode[] { raw }) { LanguageVersion = version };
        return tree with { Origin = new(snapshot, tree, 0, length) { CoreStart = 0, CoreEnd = length, ListStart = 0, ListEnd = length } };
    }

    static T Source<T>(T result, CsNode old) where T : CsNode
    {
        result = (T)(result with { Comments = new(old.Comments.Concat(result.Comments).Distinct<CsComment>(ReferenceEqualityComparer.Instance)), SyntaxPosition = old.SyntaxPosition, SyntaxTrivia = new(old.SyntaxTrivia.Concat(result.SyntaxTrivia).Distinct<CsSyntaxTrivia>(ReferenceEqualityComparer.Instance)), Formatting = old.Formatting });
        // A brace-less embedded body (GENE-27 phase 3) keeps its embedded origin on the tree it becomes, and lowers back
        // to a body with it, so an edit below it keeps the statement brace-less, as in a member body (phase 4).
        return old.Origin is not { } origin ? result : (T)(result with { Origin = origin with { Original = result } });
    }

    // An else-if's `if` is held in a tree of its own, which writes no text: it gets an embedded origin over the `if`, so
    // an edit inside the `if` keeps the chain's source (GENE-27 phase 4). A converted statement keeps its origin.
    static CsTree ElseIf(CsNode statement)
    {
        var tree = Cs.Tree(statement);
        return tree with { Origin = SourceOrigin.Wrap(tree, statement.Origin!) };
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
                // A parsed local group always has a type: `var` with several declarators stays the type named var.
                Type = i == 0 ? s.ForInitializer.Type : (CsType)s.ForInitializer.Type!.Detach(),
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
            Else = s.Else == null ? null : s.Else.Kind == StatementKind.Block ? Body(s.Else.Body) : ElseIf(Statement(s.Else)),
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

    // A member-access, call, element-access or operator chain is converted in a loop: the receivers are
    // collected down to the first one that is not a link, then each link is built on the one below it
    // (GENE-19 D10).
    static CsSymbol Expression(CsExpression e)
    {
        var links = new List<CsExpression>();
        for (var link = e; Spine.Next(link) is { } receiver; link = receiver) { links.Add(link); e = receiver; }
        var symbol = Convert(e, null);
        for (int i = links.Count - 1; i >= 0; i--) symbol = Convert(links[i], symbol);
        return symbol;
    }

    // One expression converted; `receiver` is its already converted receiver when it is a chain link.
    static CsSymbol Convert(CsExpression e, CsSymbol? receiver)
    {
        var arguments = e.Arguments;
        CsSymbol Receiver() => receiver ?? Expression(arguments[0]);
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
                    // A parsed lambda or anonymous method always has a body.
                    Value = e.LambdaBody!.Expression == null ? null : Expression(e.LambdaBody.Expression),
                    Body = e.LambdaBody.Expression != null ? null : Tree(e.LambdaBody)
                }; break;
            case ExpressionKind.Identifier: name = e.Text; options = new(); break;
            case ExpressionKind.Member: name = e.Text; options = new() { Target = Receiver(), ConditionalAccess = e.ConditionalAccess, PointerAccess = e.PointerAccess, AliasAccess = e.AliasAccess }; break;
            case ExpressionKind.Invocation:
                options = new() { AsCall = true, Target = Receiver(), Arguments = Cs.Compound(arguments.Skip(1).Select(Expression).Cast<CsNode>().ToArray()) }; break;
            case ExpressionKind.New:
                options = new() { Type = e.Type, AsNew = true, Arguments = Cs.Compound(arguments.Select(Expression).Cast<CsNode>().ToArray()), Initializer = e.Initializer == null ? null : Expression(e.Initializer) }; break;
            case ExpressionKind.Index:
                options = new()
                {
                    AsIndex = true,
                    Target = Receiver(),
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
                options = new() { Operator = e.Operator, Parts = new(arguments.Select((a, i) => i == 0 && receiver != null ? receiver : Expression(a)).Cast<CsNode>()) }; break;
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
