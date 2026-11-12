using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    internal static CsCref? DocumentationReference(string text, CsLanguageVersion version)
    {
        try
        {
            var snapshot = SourceSnapshot.Decode(Encoding.UTF8.GetBytes(text.Replace('{', '<').Replace('}', '>')), null);
            var parser = new DocumentParser(snapshot, version); var reference = parser.Cref(0);
            return parser.Current.Text == "" ? (CsCref)reference.Detach() : null;
        }
        catch (ParseFailure) { return null; }
    }

    CsCref Cref(int depth)
    {
        Guard(depth);
        var type = TypeName(depth + 1, stopBeforeArray: true);
        var arguments = type.Form == TypeForm.Generic ? type.Arguments : Values<CsType>.Empty;
        var simple = type.Form == TypeForm.Generic ? type.Element! : type;
        string last = simple.Name.Split('.').Last(); CsType? qualifier = null;
        if (simple.Form == TypeForm.Nested) qualifier = simple.Element;
        else if (simple.Name.LastIndexOf('.') is int dot && dot >= 0) qualifier = new(simple.Name[..dot]);
        if (last == "extension")
        {
            var parameters = CrefParameters("(", ")", depth + 1); Expect(".");
            return new(CrefKind.Extension) { Qualifier = qualifier, TypeArguments = arguments, Parameters = parameters, Member = Cref(depth + 1) };
        }
        if (last is "implicit" or "explicit")
        {
            Expect("operator"); bool check = Take("checked"); var target = TypeName(depth + 1);
            return new(CrefKind.Conversion)
            {
                Qualifier = qualifier,
                Operator = last == "implicit" ? OverloadableOperator.Implicit : OverloadableOperator.Explicit,
                IsChecked = check,
                Type = target,
                Parameters = Current.Text == "(" ? CrefParameters("(", ")", depth + 1) : null
            };
        }
        if (last == "operator")
        {
            bool check = Take("checked"); string token = OperatorToken(out int width);
            var found = Enum.GetValues<OverloadableOperator>().Where(o => OperatorWriter.Token(o) == token).ToArray();
            if (found.Length == 0) throw new ParseFailure("Operator cref expected."); p += width;
            return new(CrefKind.Operator)
            {
                Qualifier = qualifier,
                Operator = found[0],
                IsChecked = check,
                Parameters = Current.Text == "(" ? CrefParameters("(", ")", depth + 1) : null
            };
        }
        if (last == "this") return new(CrefKind.Indexer) { Qualifier = qualifier, Parameters = Current.Text == "[" ? CrefParameters("[", "]", depth + 1) : null };
        while (Current.Text == "[" && Peek(1) is "]" or ",")
        { Expect("["); int rank = 1; while (Take(",")) rank++; Expect("]"); type = type.Array(rank); }
        return new(CrefKind.Name) { Type = type, Parameters = Current.Text == "(" ? CrefParameters("(", ")", depth + 1) : null };
    }

    Values<CsFunctionPointerParameter> CrefParameters(string open, string close, int depth)
    {
        Guard(depth); Expect(open); var parameters = new List<CsFunctionPointerParameter>();
        if (Current.Text != close) do { string? modifier = ParameterModifier(); parameters.Add(new(TypeName(depth + 1), modifier)); } while (Take(","));
        Expect(close); return new(parameters);
    }
}
