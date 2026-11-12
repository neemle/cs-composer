namespace Neemle.CSharpComposer.Model;

internal static class NodeRewrite
{
    internal static CsNode Children(CsNode node, Func<CsNode, CsNode?> change)
    {
        var leading = Map(new Values<CsComment>(node.Comments.Where(c => c.Placement != CommentPlacement.Trailing)), change);
        CsType? explicitInterface = node is CsMember member ? Optional(member.ExplicitInterface, change) : null;
        var result = Payload(node, change);
        if (result is CsMember rewritten) result = rewritten with { ExplicitInterface = explicitInterface };
        var trailing = Map(new Values<CsComment>(node.Comments.Where(c => c.Placement == CommentPlacement.Trailing)), change);
        return result with { Comments = new(leading.Concat(trailing)), SyntaxTrivia = Map(node.SyntaxTrivia, change) };
    }

    static T Required<T>(T value, Func<CsNode, CsNode?> change) where T : CsNode =>
        change(value) is T replacement ? replacement : throw new ArgumentException("Replacement has an invalid required child kind.");

    static T? Optional<T>(T? value, Func<CsNode, CsNode?> change) where T : CsNode =>
        value == null ? null : change(value) switch
        {
            null => null,
            T result => result,
            _ => throw new ArgumentException("Replacement has an invalid optional child kind.")
        };

    static Values<T> Map<T>(Values<T> values, Func<CsNode, CsNode?> change) where T : CsNode
    {
        var result = new List<T>();
        foreach (var value in values)
        {
            var replacement = change(value);
            if (replacement is T typed) result.Add(typed);
            else if (replacement != null) throw new ArgumentException("Replacement has an invalid list child kind.");
        }
        return new(result);
    }

    static CsNode Payload(CsNode node, Func<CsNode, CsNode?> f) => node switch
    {
        CsSyntaxTrivia t => t with { Content = Required(t.Content, f) },
        CsComment c => c with { Documentation = Optional(c.Documentation, f) },
        CsXmlNode x => x with { Attributes = Map(x.Attributes, f), Content = Map(x.Content, f) },
        CsXmlAttribute a => a with { Reference = Optional(a.Reference, f) },
        CsCref r => r with { Qualifier = Optional(r.Qualifier, f), Type = Optional(r.Type, f), TypeArguments = Map(r.TypeArguments, f), Parameters = r.Parameters == null ? null : Map(r.Parameters, f), Member = Optional(r.Member, f) },
        CsDirective d => d with { Condition = Optional(d.Condition, f), Start = Optional(d.Start, f), End = Optional(d.End, f) },
        CsUsing u => u with { Target = Optional(u.Target, f) },
        CsGlobalStatement s => s with { Value = Required(s.Value, f) },
        CsTree t => t with { Items = Map(t.Items, f) },
        CsSymbol s => s with { Options = Required(s.Options, f) },
        CsSymbolOptions o => o with
        {
            Parameters = Map(o.Parameters, f),
            ParameterSymbols = Optional(o.ParameterSymbols, f),
            Clauses = Optional(o.Clauses, f),
            EqualityKey = Optional(o.EqualityKey, f),
            Attributes = Map(o.Attributes, f),
            Body = Optional(o.Body, f),
            Alignment = Optional(o.Alignment, f),
            QueryClauses = Map(o.QueryClauses, f),
            TypeArguments = Map(o.TypeArguments, f),
            Type = Optional(o.Type, f),
            Pattern = Optional(o.Pattern, f),
            Initializer = Optional(o.Initializer, f),
            Value = Optional(o.Value, f),
            Target = Optional(o.Target, f),
            Arguments = Optional(o.Arguments, f),
            Parts = Map(o.Parts, f)
        },
        CsInstruction i => i with { Options = Required(i.Options, f) },
        CsInstructionOptions o => o with
        {
            Initializer = Optional(o.Initializer, f),
            Symbol = Optional(o.Symbol, f),
            Condition = Optional(o.Condition, f),
            Iterators = Optional(o.Iterators, f),
            Value = Optional(o.Value, f),
            Body = Optional(o.Body, f),
            Else = Optional(o.Else, f),
            Sections = Map(o.Sections, f),
            Catches = Map(o.Catches, f),
            Finally = Optional(o.Finally, f),
            Function = Optional(o.Function, f)
        },
        CsDocument d => d with { Items = Map(d.Items, f) },
        CsNamespace n => n with { Items = Map(n.Items, f) },
        CsTypeDeclaration t => t with
        {
            Attributes = Map(t.Attributes, f),
            TypeParameters = Map(t.TypeParameters, f),
            PrimaryParameters = Map(t.PrimaryParameters, f),
            BaseTypes = Map(t.BaseTypes, f),
            UnionCases = Map(t.UnionCases, f),
            BaseArguments = t.BaseArguments == null ? null : Map(t.BaseArguments, f),
            Members = Map(t.Members, f)
        },
        CsDelegate d => d with { Attributes = Map(d.Attributes, f), ReturnType = Required(d.ReturnType, f), TypeParameters = Map(d.TypeParameters, f), Parameters = Map(d.Parameters, f) },
        CsDestructor d => d with { Attributes = Map(d.Attributes, f), Body = Optional(d.Body, f) },
        CsOperatorDeclaration o => o with { Attributes = Map(o.Attributes, f), ReturnType = Required(o.ReturnType, f), Parameters = Map(o.Parameters, f), Body = Optional(o.Body, f) },
        CsExtension e => e with { Attributes = Map(e.Attributes, f), ReceiverAttributes = Map(e.ReceiverAttributes, f), ReceiverType = Required(e.ReceiverType, f), TypeParameters = Map(e.TypeParameters, f), Members = Map(e.Members, f) },
        CsMethod m => m with
        {
            Attributes = Map(m.Attributes, f),
            ReturnType = Required(m.ReturnType, f),
            TypeParameters = Map(m.TypeParameters, f),
            Parameters = Map(m.Parameters, f),
            Body = Optional(m.Body, f)
        },
        CsBody b => b with { Tree = Optional(b.Tree, f), Expression = Optional(b.Expression, f), Statements = Map(b.Statements, f) },
        CsExpression e => e with
        {
            Alignment = Optional(e.Alignment, f),
            LambdaAttributes = Map(e.LambdaAttributes, f),
            QueryClauses = Map(e.QueryClauses, f),
            TypeArguments = Map(e.TypeArguments, f),
            Type = Optional(e.Type, f),
            Pattern = Optional(e.Pattern, f),
            Initializer = Optional(e.Initializer, f),

            Arguments = Map(e.Arguments, f),
            Arms = Map(e.Arms, f),
            LambdaParameters = Map(e.LambdaParameters, f),
            LambdaBody = Optional(e.LambdaBody, f)
        },
        CsQueryClause q => q with { Type = Optional(q.Type, f), Value = Optional(q.Value, f), Key = Optional(q.Key, f), EqualsKey = Optional(q.EqualsKey, f), Clauses = Map(q.Clauses, f) },
        CsLambdaParameter p => p with { Type = Optional(p.Type, f), Default = Optional(p.Default, f), Attributes = Map(p.Attributes, f) },
        CsPattern p => p with { Type = Optional(p.Type, f), Value = Optional(p.Value, f), Designation = Optional(p.Designation, f), Patterns = Map(p.Patterns, f), Properties = Map(p.Properties, f) },
        CsSwitchLabel l => l with { Pattern = Optional(l.Pattern, f), When = Optional(l.When, f) },
        CsSwitchSection s => s with { Labels = Map(s.Labels, f), Body = Required(s.Body, f) },
        CsSwitchArm a => a with { Pattern = Required(a.Pattern, f), When = Optional(a.When, f), Value = Required(a.Value, f) },
        CsField x => x with { Attributes = Map(x.Attributes, f), Type = Required(x.Type, f), FixedSize = Optional(x.FixedSize, f), Initializer = Optional(x.Initializer, f), AdditionalVariables = Map(x.AdditionalVariables, f) },
        CsType t => t with { Element = Optional(t.Element, f), Arguments = Map(t.Arguments, f), Elements = Map(t.Elements, f), FunctionParameters = Map(t.FunctionParameters, f) },
        CsFunctionPointerParameter p => p with { Type = Required(p.Type, f) },
        CsParameter x => x with { Attributes = Map(x.Attributes, f), Type = x.IsArgList ? Optional(x.Type, f) : Required(x.Type!, f), Default = Optional(x.Default, f) },
        CsStatement s => Statement(s, f),
        CsForInitializer x => x with { Type = Optional(x.Type, f), Variables = Map(x.Variables, f), Expressions = Map(x.Expressions, f) },
        CsVariableDeclarator v => v with { FixedSize = Optional(v.FixedSize, f), Initializer = Optional(v.Initializer, f) },
        CsCatch c => c with { Type = Optional(c.Type, f), Filter = Optional(c.Filter, f), Body = Required(c.Body, f) },
        TupleElement t => t with { Type = Required(t.Type, f) },
        AttributeArgument a => a with { Value = Required(a.Value, f) },
        CsAttribute a => a with { Type = Required(a.Type, f), Arguments = Map(a.Arguments, f) },
        TypeParameter t => t with { Attributes = Map(t.Attributes, f), Constraints = Map(t.Constraints, f) },
        CsConstraint c => c with { Type = Optional(c.Type, f) },
        CsAccessor a => a with { Attributes = Map(a.Attributes, f), Body = Optional(a.Body, f) },
        CsProperty p => p with
        {
            Attributes = Map(p.Attributes, f),
            Type = Required(p.Type, f),
            Parameters = Map(p.Parameters, f),
            Accessors = Map(p.Accessors, f),
            Expression = Optional(p.Expression, f),
            Initializer = Optional(p.Initializer, f)
        },
        CsConstructor c => c with
        {
            Attributes = Map(c.Attributes, f),
            Parameters = Map(c.Parameters, f),
            InitializerArguments = Map(c.InitializerArguments, f),
            Body = Optional(c.Body, f)
        },
        CsEvent e => e with
        {
            Attributes = Map(e.Attributes, f),
            Type = Required(e.Type, f),
            Accessors = Map(e.Accessors, f),
            Initializer = Optional(e.Initializer, f),
            AdditionalVariables = Map(e.AdditionalVariables, f)
        },
        CsEnumMember e => e with { Attributes = Map(e.Attributes, f), Value = Optional(e.Value, f) },
        _ => node
    };

    static CsStatement Statement(CsStatement s, Func<CsNode, CsNode?> f) => s.Kind == StatementKind.DoWhile
        ? s with
        {

            Body = Optional(s.Body, f),
            Expression = Optional(s.Expression, f),
            Type = Optional(s.Type, f),
            ForInitializer = Optional(s.ForInitializer, f),
            Binding = Optional(s.Binding, f),
            Iterators = Map(s.Iterators, f),
            Resource = Optional(s.Resource, f),
            Else = Optional(s.Else, f),
            Catches = Map(s.Catches, f),
            Finally = Optional(s.Finally, f),
            Sections = Map(s.Sections, f),
            Function = Optional(s.Function, f),
            Directive = Optional(s.Directive, f),
            Disabled = Optional(s.Disabled, f)
        }
        : s with
        {

            Type = Optional(s.Type, f),
            ForInitializer = Optional(s.ForInitializer, f),
            Binding = Optional(s.Binding, f),
            Expression = Optional(s.Expression, f),
            Iterators = Map(s.Iterators, f),
            Resource = Optional(s.Resource, f),
            Body = Optional(s.Body, f),
            Else = Optional(s.Else, f),
            Catches = Map(s.Catches, f),
            Finally = Optional(s.Finally, f),
            Sections = Map(s.Sections, f),
            Function = Optional(s.Function, f),
            Directive = Optional(s.Directive, f),
            Disabled = Optional(s.Disabled, f)
        };

    internal static CsNode Detach(CsNode node, int depth = 0)
    {
        if (depth > 128) throw new ArgumentException("Node graph exceeds128 or contains a cycle.");
        return Children(node, child => Detach(child, depth + 1)) with { Origin = null };
    }
}
