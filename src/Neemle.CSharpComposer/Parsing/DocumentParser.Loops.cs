using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsStatement ForStatement(int start, int depth)
    {
        Guard(depth);
        int headerStart = Current.Start;
        Expect("(");
        var initializer = Current.Text == ";" ? null : ForInitializer(depth + 1);
        Expect(";");
        var condition = Current.Text == ";" ? null : Expression(0, depth + 1);
        Expect(";");
        var iterators = ExpressionList(")", depth + 1);
        Expect(")");
        int headerEnd = Last.End;
        return BindLoop(Cs.For(initializer, condition, new(iterators), Embedded(depth + 1)), start, headerStart, headerEnd);
    }

    CsForInitializer ForInitializer(int depth)
    {
        Guard(depth);
        int start = p;
        try
        {
            bool isVar = Take("var");
            var type = isVar ? null : TypeName(depth + 1);
            var variables = new List<CsVariableDeclarator>();
            do
            {
                int variableStart = p;
                var name = Current; Identifier();
                var value = Take("=") ? Expression(0, depth + 1) : null;
                variables.Add(Bind(Cs.Variable(name.Text, value), variableStart, "Name", name.Start, name.Text.Length));
            } while (Take(","));
            if (Current.Text != ";") throw new ParseFailure("For declaration expected.");
            return Bind(new CsForInitializer { Type = type, IsVar = isVar, Variables = new(variables) }, start);
        }
        catch (ParseFailure) { p = start; }
        return Bind(Cs.ForExpressions(ExpressionList(";", depth + 1).ToArray()), start);
    }

    List<CsExpression> ExpressionList(string close, int depth)
    {
        var result = new List<CsExpression>();
        if (Current.Text != close)
            do { result.Add(Expression(0, depth)); } while (Take(","));
        return result;
    }

    CsStatement ForEachStatement(int start, int depth)
    {
        Guard(depth);
        int headerStart = Current.Start;
        Expect("(");
        bool isVar = Take("var");
        var type = isVar ? null : TypeName(depth + 1);
        var name = Current; Identifier();
        Expect("in");
        var collection = Expression(0, depth + 1); Expect(")");
        int headerEnd = Last.End;
        var body = Embedded(depth + 1);
        return BindLoop(isVar ? Cs.ForEach(name.Text, collection, body) : Cs.ForEach(type!, name.Text, collection, body),
            start, headerStart, headerEnd, name);
    }

    CsStatement WhileStatement(int start, int depth)
    {
        Guard(depth);
        int headerStart = Current.Start;
        Expect("("); var condition = Expression(0, depth + 1); Expect(")");
        int headerEnd = Last.End;
        return BindLoop(Cs.While(condition, Embedded(depth + 1)), start, headerStart, headerEnd);
    }

    CsStatement BindLoop(CsStatement statement, int start, int headerStart, int headerEnd, Token? name = null)
    {
        // Header trivia belongs to the loop, including comments between separators.
        // Canonical formatting emits it as container trivia; scalar edits retain
        // the original byte positions through the source bindings.
        var comments = all.Where(t => t.IsComment && t.Start >= headerStart && t.End <= headerEnd)
            .Select(t => Comment(t, CommentPlacement.Container)).ToArray();
        statement = (CsStatement)NodeRewrite.Children(statement, child => StripOwnedComments(child, comments));
        statement = statement with { Comments = new(statement.Comments.Concat(comments)) };
        return name is { } token ? Bind(statement, start, "Text", token.Start, token.Text.Length) : Bind(statement, start);
    }

    CsStatement DoStatement(int start, int depth)
    {
        Guard(depth);
        var body = Embedded(depth + 1);
        int headerStart = Last.End;
        Expect("while"); Expect("("); var condition = Expression(0, depth + 1); Expect(")"); Expect(";");
        return BindLoop(Cs.DoWhile(body, condition), start, headerStart, Last.End);
    }
}
