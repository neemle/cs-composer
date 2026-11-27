using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsExpression(ExpressionKind Kind, string Text, Values<CsExpression> Arguments) : CsNode
{
    public ArgumentModifier ArgumentModifier { get; init; }
    public Values<CsType> TypeArguments { get; init; } = Values<CsType>.Empty;
    public Values<CsQueryClause> QueryClauses { get; init; } = Values<CsQueryClause>.Empty;

    public string? Format { get; init; }
    public CsExpression? Alignment { get; init; }
    public CsStringStyle StringStyle { get; init; }
    public bool Utf8 { get; init; }

    public CsType? Type { get; init; }

    public CsPattern? Pattern { get; init; }

    public bool PointerAccess { get; init; }
    public bool AliasAccess { get; init; }

    public bool ConditionalAccess { get; init; }

    public int ArrayRank { get; init; } = 1;

    public CsExpression? Initializer { get; init; }

    public CsOperator Operator { get; init; }

    public Values<CsLambdaParameter> LambdaParameters { get; init; } = Values<CsLambdaParameter>.Empty;

    public CsBody? LambdaBody { get; init; }

    public Values<CsAttribute> LambdaAttributes { get; init; } = Values<CsAttribute>.Empty;
    public string? ReturnModifier { get; init; }
    public bool HasParameterList { get; init; } = true;

    public bool IsAsync { get; init; }

    public bool IsStatic { get; init; }

    public Values<CsSwitchArm> Arms { get; init; } = Values<CsSwitchArm>.Empty;

    public CsExpression Member(string name) => new CsExpression(ExpressionKind.Member, name, new[] { this });

    public CsExpression Call(params CsExpression[] arguments) =>
        new CsExpression(ExpressionKind.Invocation, "", new Values<CsExpression>(new[] { this }.Concat(arguments)));

    public CsExpression Await() => new CsExpression(ExpressionKind.Await, "", new[] { this });

    // Record equality, written out so that a chain is compared along Arguments[0] in a loop instead of one
    // nested call per link (GENE-19 D10). The members and their meaning are those the compiler would compare.
    public bool Equals(CsExpression? other)
    {
        var left = this;
        var right = other;
        while (true)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || !left.LinkEquals(right)) return false;
            if (left.Arguments is null || left.Arguments.Count == 0) return true;
            left = left.Arguments[0];
            right = right.Arguments[0];
        }
    }

    public override int GetHashCode()
    {
        int hash = 0;
        for (var current = this; current != null; current = current.Arguments is { Count: > 0 } arguments ? arguments[0] : null)
            hash = unchecked(hash * -1521134295 + current.LinkHash());
        return hash;
    }

    // Every member except Arguments[0]; the Arguments counts must match.
    internal bool LinkEquals(CsExpression other)
    {
        if (!NodeEquals(other) || Kind != other.Kind || !string.Equals(Text, other.Text, StringComparison.Ordinal)) return false;
        if (Arguments is null || other.Arguments is null)
        {
            if (!ReferenceEquals(Arguments, other.Arguments)) return false;
        }
        else
        {
            if (Arguments.Count != other.Arguments.Count) return false;
            for (int i = 1; i < Arguments.Count; i++)
                if (!EqualityComparer<CsExpression>.Default.Equals(Arguments[i], other.Arguments[i])) return false;
        }
        return ArgumentModifier == other.ArgumentModifier &&
            EqualityComparer<Values<CsType>>.Default.Equals(TypeArguments, other.TypeArguments) &&
            EqualityComparer<Values<CsQueryClause>>.Default.Equals(QueryClauses, other.QueryClauses) &&
            string.Equals(Format, other.Format, StringComparison.Ordinal) &&
            EqualityComparer<CsExpression?>.Default.Equals(Alignment, other.Alignment) &&
            StringStyle == other.StringStyle && Utf8 == other.Utf8 &&
            EqualityComparer<CsType?>.Default.Equals(Type, other.Type) &&
            EqualityComparer<CsPattern?>.Default.Equals(Pattern, other.Pattern) &&
            PointerAccess == other.PointerAccess && AliasAccess == other.AliasAccess && ConditionalAccess == other.ConditionalAccess &&
            ArrayRank == other.ArrayRank &&
            EqualityComparer<CsExpression?>.Default.Equals(Initializer, other.Initializer) &&
            Operator == other.Operator &&
            EqualityComparer<Values<CsLambdaParameter>>.Default.Equals(LambdaParameters, other.LambdaParameters) &&
            EqualityComparer<CsBody?>.Default.Equals(LambdaBody, other.LambdaBody) &&
            EqualityComparer<Values<CsAttribute>>.Default.Equals(LambdaAttributes, other.LambdaAttributes) &&
            string.Equals(ReturnModifier, other.ReturnModifier, StringComparison.Ordinal) &&
            HasParameterList == other.HasParameterList && IsAsync == other.IsAsync && IsStatic == other.IsStatic &&
            EqualityComparer<Values<CsSwitchArm>>.Default.Equals(Arms, other.Arms);
    }

    int LinkHash()
    {
        var hash = new HashCode();
        hash.Add(NodeHash());
        hash.Add(Kind);
        hash.Add(Text);
        hash.Add(Arguments?.Count ?? -1);
        if (Arguments != null) for (int i = 1; i < Arguments.Count; i++) hash.Add(Arguments[i]);
        hash.Add(ArgumentModifier);
        hash.Add(TypeArguments);
        hash.Add(QueryClauses);
        hash.Add(Format);
        hash.Add(Alignment);
        hash.Add(StringStyle);
        hash.Add(Utf8);
        hash.Add(Type);
        hash.Add(Pattern);
        hash.Add(PointerAccess);
        hash.Add(AliasAccess);
        hash.Add(ConditionalAccess);
        hash.Add(ArrayRank);
        hash.Add(Initializer);
        hash.Add(Operator);
        hash.Add(LambdaParameters);
        hash.Add(LambdaBody);
        hash.Add(LambdaAttributes);
        hash.Add(ReturnModifier);
        hash.Add(HasParameterList);
        hash.Add(IsAsync);
        hash.Add(IsStatic);
        hash.Add(Arms);
        return hash.ToHashCode();
    }
}
