namespace Neemle.CSharpComposer.Parsing;

internal sealed class Preprocessor(IEnumerable<string>? symbols)
{
    readonly HashSet<string> defined = new(symbols ?? Array.Empty<string>(), StringComparer.Ordinal);
    readonly Stack<(bool Parent, bool Taken)> stack = new();
    internal bool Active { get; private set; } = true;

    internal void Directive(string text)
    {
        string source = text[1..].Trim();
        int separator = source.IndexOfAny(new[] { ' ', '\t' });
        string command = separator < 0 ? source : source[..separator];
        string argument = separator < 0 ? "" : source[(separator + 1)..].Trim();
        if (command is "define" or "undef") argument = Lexer.Scan(argument).First().Text;
        switch (command)
        {
            case "define": if (Active) defined.Add(argument); break;
            case "undef": if (Active) defined.Remove(argument); break;
            case "if":
                bool taken = Evaluate(argument); stack.Push((Active, taken)); Active &= taken; break;
            case "elif" when stack.Count > 0:
                var branch = stack.Pop(); bool match = !branch.Taken && Evaluate(argument);
                Active = branch.Parent && match; stack.Push((branch.Parent, branch.Taken || match)); break;
            case "else" when stack.Count > 0:
                var alternative = stack.Pop(); Active = alternative.Parent && !alternative.Taken; stack.Push((alternative.Parent, true)); break;
            case "endif" when stack.Count > 0: Active = stack.Pop().Parent; break;
        }
    }

    bool Evaluate(string text)
    {
        var tokens = Lexer.Scan(text); int position = 0;
        string Current() => tokens[Math.Min(position, tokens.Count - 1)].Text;
        bool Primary(int depth)
        {
            if (depth > 128) return false;
            string token = Current(); position++;
            if (token == "!") return !Primary(depth + 1);
            if (token == "(") { bool value = Binary(0, depth + 1); if (Current() == ")") position++; return value; }
            return token == "true" || token != "false" && defined.Contains(token);
        }
        bool Binary(int minimum, int depth)
        {
            bool value = Primary(depth + 1);
            while (true)
            {
                string op = Current(); int priority = op is "==" or "!=" ? 3 : op == "&&" ? 2 : op == "||" ? 1 : -1;
                if (priority < minimum || priority < 0) return value;
                position++; bool right = Binary(priority + 1, depth + 1);
                value = op switch { "==" => value == right, "!=" => value != right, "&&" => value && right, _ => value || right };
            }
        }
        return Binary(0, 0);
    }
}
