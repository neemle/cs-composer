using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    T WithAsync<T>(bool async, Func<T> parse)
    {
        bool previous = inAsync; inAsync = async;
        try { return parse(); }
        finally { inAsync = previous; }
    }

    bool LambdaAhead()
    {
        int before = p;
        try
        {
            Attributes();
            while (Current.Text is "async" or "static" && Peek(1) != "=>") p++;
            if (Peek(1) == "=>" && Rendering.Names.IsIdentifier(Current.Text) && Current.Text is not ("null" or "true" or "false")) return true;
            if (Take("ref")) Take("readonly");
            if (Current.Text == "(")
            {
                int tupleStart = p;
                try { TypeName(); if (Current.Text != "(") p = tupleStart; }
                catch (ParseFailure) { p = tupleStart; }
            }
            else TypeName();
            if (!Take("(")) return false;
            int nesting = 1;
            for (; p < tokens.Count - 1; p++)
            {
                if (Current.Text == "(") nesting++;
                if (Current.Text == ")" && --nesting == 0) return Peek(1) == "=>";
            }
            return false;
        }
        catch (ParseFailure) { return false; }
        finally { p = before; }
    }

    CsExpression AnonymousMethod(int depth)
    {
        Guard(depth);
        int start = p; bool async = Take("async"); Expect("delegate");
        bool hasParameters = Current.Text == "(";
        var parameters = hasParameters ? Parameters() : Values<CsParameter>.Empty;
        var body = WithAsync(async, () => Body(depth + 1)) ?? throw new ParseFailure("Anonymous method requires a block.");
        if (body.Expression != null) throw new ParseFailure("Anonymous method requires a block.");
        return Bind(new CsExpression(ExpressionKind.AnonymousMethod, "", Values<CsExpression>.Empty)
        {
            IsAsync = async,
            HasParameterList = hasParameters,
            LambdaBody = body,
            LambdaParameters = new(parameters.Select(p => new CsLambdaParameter(p.Name, p.Type, p.Modifier) { Attributes = p.Attributes, Default = p.Default }))
        }, start);
    }

    CsExpression LambdaExpression(int depth)
    {
        Guard(depth);
        int start = p;
        var attributes = Attributes();
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
        string? returnModifier = null;
        if (Take("ref")) returnModifier = Take("readonly") ? "ref readonly" : "ref";
        CsType? returnType = null;
        if (returnModifier != null || Current.Text != "(" && Peek(1) != "=>") returnType = TypeName(depth + 1);
        else if (Current.Text == "(")
        {
            int tupleStart = p;
            try { var candidate = TypeName(depth + 1); if (Current.Text == "(") returnType = candidate; else p = tupleStart; }
            catch (ParseFailure) { p = tupleStart; }
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
        var body = WithAsync(async, () => Current.Text == "{" ? Body(depth + 1)! : CsBody.FromExpression(Expression(0, depth + 1)));
        var expression = Cs.Lambda(body, parameters.ToArray()) with { IsAsync = async, IsStatic = statik, Type = returnType, ReturnModifier = returnModifier, LambdaAttributes = attributes };
        var comments = all.Where(t => t.IsComment && t.Start >= tokens[start].Start && t.End <= headerEnd)
            .Select(t => Comment(t, CommentPlacement.Container)).ToArray();
        expression = (CsExpression)NodeRewrite.Children(expression, child => StripOwnedComments(child, comments));
        return Bind(expression with { Comments = new(comments) }, start);
    }

    CsLambdaParameter LambdaParameter(int depth)
    {
        Guard(depth);
        int start = p;
        var attributes = Attributes();
        string? modifier = ParameterModifier();
        int typeStart = p;
        try
        {
            var type = TypeName(depth + 1);
            var name = Current; Identifier();
            if (Current.Text is not ("," or ")" or "=")) throw new ParseFailure("Unsupported lambda parameter form.");
            var value = Take("=") ? Expression(0, depth + 1) : null;
            return Bind(Cs.LambdaParameter(name.Text, type, modifier) with { Attributes = attributes, Default = value }, start, "Name", name.Start, name.Text.Length);
        }
        catch (ParseFailure) { p = typeStart; }
        var simple = Current; Identifier();
        var fallbackValue = Take("=") ? Expression(0, depth + 1) : null;
        return Bind(Cs.LambdaParameter(simple.Text, modifier: modifier) with { Attributes = attributes, Default = fallbackValue }, start, "Name", simple.Start, simple.Text.Length);
    }
}
