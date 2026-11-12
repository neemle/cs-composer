using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    bool LambdaAhead()
    {
        int look = p;
        while (tokens[look].Text is "async" or "static" && tokens[Math.Min(look + 1, tokens.Count - 1)].Text != "=>") look++;
        string name = tokens[look].Text;
        if (look + 1 < tokens.Count && tokens[look + 1].Text == "=>" && name.Length != 0 &&
            name is not ("null" or "true" or "false") && (char.IsLetter(name[0]) || name[0] is '_' or '@')) return true;
        if (tokens[look].Text != "(") return false;
        int nesting = 0;
        for (; look < tokens.Count; look++)
        {
            if (tokens[look].Text == "(") nesting++;
            if (tokens[look].Text == ")" && --nesting == 0)
                return look + 1 < tokens.Count && tokens[look + 1].Text == "=>";
        }
        return false;
    }

    CsExpression LambdaExpression(int depth)
    {
        Guard(depth);
        int start = p;
        bool async = false, statik = false;
        while (Current.Text is "async" or "static" && Peek(1) != "=>")
        {
            if (Take("async"))
            {
                if (async) throw new ParseFailure("Repeated async lambda modifier.");
                async = true;
            }
            else
            {
                Expect("static");
                if (statik) throw new ParseFailure("Repeated static lambda modifier.");
                statik = true;
            }
        }
        var parameters = new List<CsLambdaParameter>();
        if (Take("("))
        {
            if (Current.Text != ")") do { parameters.Add(LambdaParameter(depth + 1)); } while (Take(","));
            Expect(")");
        }
        else
        {
            int parameterStart = p;
            var name = Current; Identifier();
            parameters.Add(Bind(Cs.LambdaParameter(name.Text), parameterStart, "Name", name.Start, name.Text.Length));
        }
        Expect("=>");
        int headerEnd = Current.Start;
        var body = Current.Text == "{" ? Body(depth + 1)! : CsBody.FromExpression(Expression(0, depth + 1));
        var expression = Cs.Lambda(body, parameters.ToArray()) with { IsAsync = async, IsStatic = statik };
        var comments = all.Where(t => t.IsComment && t.Start >= tokens[start].Start && t.End <= headerEnd)
            .Select(t => Comment(t, CommentPlacement.Container)).ToArray();
        expression = (CsExpression)NodeRewrite.Children(expression, child => StripOwnedComments(child, comments));
        return Bind(expression with { Comments = new(comments) }, start);
    }

    CsLambdaParameter LambdaParameter(int depth)
    {
        Guard(depth);
        int start = p;
        string? modifier = Current.Text is "ref" or "out" or "in" ? tokens[p++].Text : null;
        int typeStart = p;
        try
        {
            var type = TypeName(depth + 1);
            var name = Current; Identifier();
            if (Current.Text is not ("," or ")")) throw new ParseFailure("Unsupported lambda parameter form.");
            return Bind(Cs.LambdaParameter(name.Text, type, modifier), start, "Name", name.Start, name.Text.Length);
        }
        catch (ParseFailure) { p = typeStart; }
        if (modifier != null) throw new ParseFailure("Lambda parameter modifiers require an explicit type.");
        var simple = Current; Identifier();
        return Bind(Cs.LambdaParameter(simple.Text), start, "Name", simple.Start, simple.Text.Length);
    }
}
