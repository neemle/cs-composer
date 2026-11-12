using System.Text;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal static class SymbolParser
{
    internal static ParseResult<CsTree> Parse(string source, CsLanguageVersion version)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = SourceSnapshot.Decode(new UTF8Encoding(false, true).GetBytes(source), null);
        var parsed = DocumentParser.Fragment(snapshot, new CsRaw(source), new(ParseKind.Statements, version));
        if (parsed.Root is not CsBody body || parsed.Coverage != ParseCoverage.Typed)
            return new(new CsTree(new CsNode[] { new CsRaw(source) }) { LanguageVersion = version }, parsed.Diagnostics, parsed.Coverage);
        var tree = Tree(body) with { LanguageVersion = version };
        tree = tree with { Origin = new(snapshot, tree, 0, source.Length) { CoreStart = 0, CoreEnd = source.Length, ListStart = 0, ListEnd = source.Length } };
        var diagnostics = tree.Validate().Select(e => new ParseDiagnostic(e.Code, DiagnosticSeverity.Error, 0, 0, 1, 1, e.Message)).ToArray();
        return new(tree, new(diagnostics), diagnostics.Length == 0 ? ParseCoverage.Typed : ParseCoverage.Partial);
    }

    static T Source<T>(T result, CsNode old) where T : CsNode
    {
        result = (T)(result with { Comments = old.Comments, Formatting = old.Formatting });
        return old.Origin is not { } origin ? result : (T)(result with { Origin = origin with { Original = result } });
    }

    static CsTree Tree(CsBody body) => Source(new CsTree(new(body.Statements.Select(Statement))), body);

    static CsNode Statement(CsStatement statement)
    {
        var s = statement;
        CsNode? Expr(CsExpression? e) => e == null ? null : Expression(e);
        CsTree? Body(CsBody? b) => b == null ? null : Tree(b);
        CsSymbol Variable(CsStatement local, bool use = false, bool await = false) => new(local.Text, new()
        {
            Type = local.Type,
            AsVar = local.IsVar,
            Using = use,
            Await = await,
            Value = Expr(local.Expression)
        });
        if (s.Kind == StatementKind.Local) return Source(Variable(s), s);
        if (s.Kind == StatementKind.UsingDeclaration) return Source(Variable(s.Resource!, true, s.IsAwait), s);
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
            _ => (InstructionKind?)null
        };
        if (kind == null) return s; // Existing typed switch/try/etc remain interoperable nodes.
        var options = new CsInstructionOptions
        {
            Value = s.Kind is StatementKind.Return or StatementKind.Throw or StatementKind.ForEach ? Expr(s.Expression) : null,
            Condition = s.Kind is StatementKind.If or StatementKind.For or StatementKind.While or StatementKind.DoWhile ? Expr(s.Expression) : null,
            Body = Body(s.Body),
            Await = s.IsAwait,
            Else = s.Else == null ? null : s.Else.Kind == StatementKind.Block ? Body(s.Else.Body) : Cs.Tree(Statement(s.Else)),
            Symbol = s.Kind == StatementKind.ForEach ? new CsSymbol(s.Text, new() { Type = s.Type, AsVar = s.IsVar }) : null,
            Initializer = Initializer(s.ForInitializer),
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
            case ExpressionKind.Identifier: name = e.Text; options = new(); break;
            case ExpressionKind.Member: name = e.Text; options = new() { Target = Expression(arguments[0]) }; break;
            case ExpressionKind.Invocation:
                options = new() { AsCall = true, Target = Expression(arguments[0]), Arguments = Cs.Compound(arguments.Skip(1).Select(Expression).Cast<CsNode>().ToArray()) }; break;
            case ExpressionKind.New:
                options = new() { Type = e.Type, AsNew = true, Arguments = Cs.Compound(arguments.Select(Expression).Cast<CsNode>().ToArray()) }; break;
            case ExpressionKind.Assignment:
                options = new() { AsAssignment = true, Operator = e.Operator, Target = Expression(arguments[0]), Value = Expression(arguments[1]) }; break;
            case ExpressionKind.Binary:
            case ExpressionKind.Unary:
                options = new() { Operator = e.Operator, Parts = new(arguments.Select(Expression).Cast<CsNode>()) }; break;
            case ExpressionKind.Await:
                options = new() { Await = true, Value = Expression(arguments[0]) }; break;
            default: options = new() { Value = e }; break;
        }
        return Source(new CsSymbol(name, options), e);
    }
}
