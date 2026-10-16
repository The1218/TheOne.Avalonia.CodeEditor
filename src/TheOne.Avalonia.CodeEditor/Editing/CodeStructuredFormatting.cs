using System.Text;

namespace TheOne.Avalonia.Controls;

internal static class CodeStructuredFormatting
{
    internal static string Format(CodeFormattingContext context, CancellationToken token)
    {
        return context.CodeType switch
        {
            CodeType.Python => Python(context, token),
            CodeType.Yaml => Yaml(context, token),
            _ => Html(context, token)
        };
    }

    private static string Unit(CodeFormattingContext c)
    {
        return c.ConvertTabsToSpaces ? new string(' ', c.TabSize) : "\t";
    }

    private static string Padding(string unit, int level)
    {
        return string.Concat(Enumerable.Repeat(unit, Math.Clamp(level, 0, 256)));
    }

    private static CodeDocument Lex(CodeFormattingContext c)
    {
        var d = new CodeDocument();
        d.Reset(c.Text);
        CodeSyntax.Update(d, c.CodeType, force: true);
        return d;
    }

    internal static bool[] Protected(CodeLine line)
    {
        var mask = new bool[line.Text.Length];
        foreach (var t in line.Tokens.Where(t => t.Kind is CodeTokenKind.String or CodeTokenKind.Comment))
            Array.Fill(mask, true, t.Start, t.Length);
        return mask;
    }

    private static string Python(CodeFormattingContext c, CancellationToken token)
    {
        var document = Lex(c);
        var result = new StringBuilder(c.Text.Length + 128);
        var levels = new List<int> { 0 };
        var continuation = 0;
        var explicitContinuation = false;
        var baseLevel = 0;
        foreach (var line in document.Lines)
        {
            token.ThrowIfCancellationRequested();
            // Triple-quoted bodies, including their closing line, carry literal data.
            if (line.StartState.End != null)
            {
                result.Append(line.Text).Append(line.Delimiter);
                continue;
            }

            var value = line.Text.Trim();
            if (value.Length == 0)
            {
                result.Append(line.Delimiter);
                continue;
            }

            var original = CodeAnalysis.Indent(line.Text);
            var continuing = continuation > 0 || explicitContinuation;
            if (!continuing && !value.StartsWith('#'))
            {
                while (levels.Count > 1 && original < levels[^1]) levels.RemoveAt(levels.Count - 1);
                if (original > levels[^1]) levels.Add(original);
                baseLevel = levels.Count - 1;
            }

            var closes = value[0] is ')' or ']' or '}';
            var level = baseLevel + (continuing && !closes ? 1 : 0);
            if (value.StartsWith('#')) level = Math.Max(0, levels.FindLastIndex(n => n <= original));
            var content = line.EndState.End != null ? line.Text.TrimStart() : PythonSpacing(value);
            result.Append(Padding(Unit(c), level)).Append(content).Append(line.Delimiter);
            var mask = Protected(line);
            for (var n = 0; n < line.Text.Length; n++)
                if (!mask[n])
                {
                    if (line.Text[n] is '(' or '[' or '{') continuation++;
                    else if (line.Text[n] is ')' or ']' or '}') continuation = Math.Max(0, continuation - 1);
                }

            explicitContinuation = value.EndsWith('\\');
        }

        return result.ToString();
    }

    private static string PythonSpacing(string text)
    {
        text = CodeFormatting.NormalizeSpacing(text, CodeType.Python);
        var state = default(CodeLexState);
        var tokens = CodeSyntax.Scan(text, CodeType.Python, ref state);
        var mask = new bool[text.Length];
        foreach (var t in tokens.Where(t => t.Kind is CodeTokenKind.String or CodeTokenKind.Comment))
            Array.Fill(mask, true, t.Start, t.Length);
        var output = new StringBuilder(text.Length + 16);
        var brackets = new Stack<char>();
        for (var n = 0; n < text.Length; n++)
        {
            var ch = text[n];
            if (mask[n])
            {
                if (ch == '#' && (n == 0 || !mask[n - 1]))
                {
                    while (output.Length > 0 && output[^1] == ' ') output.Length--;
                    if (output.Length > 0) output.Append("  ");
                }

                output.Append(ch);
                continue;
            }

            if (ch is '(' or '[' or '{') brackets.Push(ch);
            else if (ch is ')' or ']' or '}' && brackets.Count > 0) brackets.Pop();
            if (ch == ':')
            {
                while (output.Length > 0 && output[^1] == ' ') output.Length--;
                output.Append(':');
                while (n + 1 < text.Length && text[n + 1] == ' ') n++;
                // Slices retain their compact spelling; mappings and annotations use a space.
                if (n + 1 < text.Length && (brackets.Count == 0 || brackets.Peek() != '[')) output.Append(' ');
            }
            else
            {
                output.Append(ch);
            }
        }

        return output.ToString().TrimEnd();
    }

    private static string Yaml(CodeFormattingContext c, CancellationToken token)
    {
        var result = new StringBuilder(c.Text.Length + 64);
        var levels = new List<(int Source, int Target)> { (0, 0) };
        int? scalarIndent = null;
        var scalarOutputIndent = 0;
        var document = Lex(c);
        foreach (var line in document.Lines)
        {
            token.ThrowIfCancellationRequested();
            var text = line.Text;
            var value = text.Trim();
            var indent = CodeAnalysis.Indent(text);
            if (scalarIndent.HasValue && (value.Length == 0 || indent > scalarIndent.Value))
            {
                // Shift the whole block by the same delta; retain content indentation and trailing spaces.
                var delta = scalarOutputIndent - scalarIndent.Value;
                result.Append(new string(' ', Math.Max(0, indent + delta))).Append(text.TrimStart(' ', '\t'))
                    .Append(line.Delimiter);
                continue;
            }

            scalarIndent = null;
            if (value.Length == 0)
            {
                result.Append(line.Delimiter);
                continue;
            }

            while (levels.Count > 1 && indent < levels[^1].Source) levels.RemoveAt(levels.Count - 1);
            if (indent > levels[^1].Source) levels.Add((indent, levels[^1].Target + c.TabSize));
            // YAML indentation is always spaces, even when editor tabs are enabled.
            var outputIndent = levels[^1].Target;
            var content = YamlSpacing(value);
            result.Append(' ', outputIndent).Append(content).Append(line.Delimiter);
            // A compact mapping after '- ' starts two columns after the sequence
            // marker; its following keys must align there, independently of TabSize.
            var sequencePrefix = 0;
            while (value.AsSpan(sequencePrefix).StartsWith("- ", StringComparison.Ordinal))
            {
                sequencePrefix += 2;
                levels.Add((indent + sequencePrefix, outputIndent + sequencePrefix));
            }

            if (YamlBlockHeader(value))
            {
                scalarIndent = indent;
                scalarOutputIndent = outputIndent;
            }
        }

        return result.ToString();
    }

    internal static bool YamlBlockHeader(string text)
    {
        var value = text.Split('#')[0].TrimEnd();
        var last = value.LastIndexOfAny(['|', '>']);
        return last >= 0 && (last == 0 || char.IsWhiteSpace(value[last - 1])) &&
               value[(last + 1)..].All(c => c is '+' or '-' or >= '1' and <= '9');
    }

    internal static string YamlSpacing(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        var quote = '\0';
        var flow = 0;
        for (var n = 0; n < text.Length; n++)
        {
            var ch = text[n];
            if (quote != '\0')
            {
                result.Append(ch);
                if (ch == '\\' && quote == '"' && n + 1 < text.Length)
                {
                    result.Append(text[++n]);
                }
                else if (ch == quote)
                {
                    if (quote == '\'' && n + 1 < text.Length && text[n + 1] == '\'') result.Append(text[++n]);
                    else quote = '\0';
                }

                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                result.Append(ch);
                continue;
            }

            if (ch == '#' && (n == 0 || char.IsWhiteSpace(text[n - 1])))
            {
                while (result.Length > 0 && result[^1] == ' ') result.Length--;
                if (result.Length > 0) result.Append("  ");
                result.Append(text.AsSpan(n));
                break;
            }

            if (ch is '[' or '{') flow++;
            if (ch is ']' or '}') flow--;
            result.Append(ch);
            if ((ch == ',' && flow > 0) || (ch == ':' && (n + 1 == text.Length || char.IsWhiteSpace(text[n + 1]) ||
                                                          (flow > 0 && (text[n + 1] is '\'' or '"' or '[' or '{' ||
                                                                        (n > 0 && text[n - 1] is '\'' or '"'))))))
            {
                while (n + 1 < text.Length && text[n + 1] == ' ') n++;
                if (n + 1 < text.Length && text[n + 1] is not (']' or '}')) result.Append(' ');
            }
        }

        return result.ToString().TrimEnd();
    }

    private static string Html(CodeFormattingContext c, CancellationToken token)
    {
        var tree = CodeHtml.Parse(c.Text, token);
        var output = new StringBuilder(c.Text.Length + 128);

        void Write(CodeHtml.Node node, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (node.Name == "#text")
            {
                if (!string.IsNullOrWhiteSpace(node.Open))
                    output.Append(Padding(Unit(c), depth)).Append(node.Open).Append(c.NewLine);
                return;
            }

            var mixed = depth >= 256 || node.Raw || node.Children.Any(n =>
                (n.Name == "#text" && !string.IsNullOrWhiteSpace(n.Open)) ||
                (n.Name[0] != '#' && !CodeHtml.Block.Contains(n.Name)));
            output.Append(Padding(Unit(c), depth)).Append(node.Open.Trim());
            if (mixed)
            {
                output.Append(c.Text.AsSpan(node.OpenEnd, Math.Max(0, node.End - node.OpenEnd)));
            }
            else
            {
                if (node.Children.Any(n => n.Name != "#text" || !string.IsNullOrWhiteSpace(n.Open)))
                {
                    output.Append(c.NewLine);
                    foreach (var child in node.Children) Write(child, depth + 1);
                    output.Append(Padding(Unit(c), depth));
                }

                output.Append(node.Close);
            }

            output.Append(c.NewLine);
        }

        foreach (var child in tree.Root.Children) Write(child, 0);
        var formatted = output.ToString();
        return c.Text.EndsWith('\n') || c.Text.EndsWith('\r') ? formatted : formatted.TrimEnd('\r', '\n');
    }
}

internal static class CodeHtml
{
    internal static readonly HashSet<string> Void =
        new("area base br col embed hr img input link meta param source track wbr".Split(' '),
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Optional =
        new("html head body p li dt dd rt rp optgroup option colgroup thead tbody tfoot tr td th".Split(' '),
            StringComparer.OrdinalIgnoreCase);

    internal static readonly HashSet<string> Block = new(
        "html head body address article aside blockquote canvas dd div dl dt fieldset figcaption figure footer form h1 h2 h3 h4 h5 h6 header hr li main nav noscript ol p pre section table tbody td tfoot th thead tr ul video script style meta link title"
            .Split(' '), StringComparer.OrdinalIgnoreCase);

    internal static Result Parse(string text, CancellationToken token)
    {
        var root = new Node("#root", "", 0, 0) { End = text.Length };
        var stack = new Stack<Node>();
        stack.Push(root);
        var errors = new List<(int, int, string)>();
        var p = 0;
        while (p < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var start = p;
            if (text[p] != '<')
            {
                p = text.IndexOf('<', p);
                if (p < 0) p = text.Length;
                stack.Peek().Children.Add(new Node("#text", text[start..p], start, p));
                continue;
            }

            if (text.AsSpan(p).StartsWith("<!--"))
            {
                var end = text.IndexOf("-->", p + 4, StringComparison.Ordinal);
                p = end < 0 ? text.Length : end + 3;
                if (end < 0) errors.Add((start, 4, "Unclosed HTML comment."));
                stack.Peek().Children.Add(new Node("#comment", text[start..p], start, p));
                continue;
            }

            var quote = '\0';
            p++;
            for (; p < text.Length; p++)
            {
                var ch = text[p];
                if (quote != '\0')
                {
                    if (ch == quote) quote = '\0';
                }
                else if (ch is '\'' or '"')
                {
                    quote = ch;
                }
                else if (ch == '>')
                {
                    break;
                }
            }

            if (p >= text.Length)
            {
                errors.Add((start, Math.Max(1, text.Length - start), "Unclosed HTML tag or attribute quote."));
                stack.Peek().Children.Add(new Node("#text", text[start..], start, text.Length));
                break;
            }

            p++;
            var tag = text[start..p];
            var closing = tag.StartsWith("</", StringComparison.Ordinal);
            var nameStart = closing ? 2 : 1;
            var nameEnd = nameStart;
            while (nameEnd < tag.Length && (char.IsLetterOrDigit(tag[nameEnd]) || tag[nameEnd] is '-' or ':'))
                nameEnd++;
            var name = tag[nameStart..nameEnd].ToLowerInvariant();
            if (name.Length == 0)
            {
                stack.Peek().Children.Add(new Node("#declaration", tag, start, p));
                continue;
            }

            if (closing)
            {
                if (!stack.Any(n => n.Name == name))
                {
                    errors.Add((start, tag.Length, $"Unexpected closing tag </{name}>."));
                    stack.Peek().Children.Add(new Node("#declaration", tag, start, p));
                    continue;
                }

                while (stack.Count > 1 && stack.Peek().Name != name)
                {
                    var unclosed = stack.Pop();
                    unclosed.End = start;
                    if (!Optional.Contains(unclosed.Name))
                        errors.Add((unclosed.Start, unclosed.Open.Length, $"Missing closing tag </{unclosed.Name}>."));
                }

                var node = stack.Pop();
                node.Close = tag;
                node.End = p;
                continue;
            }

            if (stack.Count > 1 && ((Optional.Contains(name) && stack.Peek().Name == name) ||
                                    (stack.Peek().Name == "p" && Block.Contains(name))))
            {
                var omitted = stack.Pop();
                omitted.End = start;
            }

            var element = new Node(name, tag, start, p);
            stack.Peek().Children.Add(element);
            if (Void.Contains(name) || tag.EndsWith("/>", StringComparison.Ordinal)) continue;
            if (name is "script" or "style" or "pre" or "textarea")
            {
                var close = text.IndexOf("</" + name, p, StringComparison.OrdinalIgnoreCase);
                if (close >= 0)
                {
                    var end = text.IndexOf('>', close);
                    if (end >= 0)
                    {
                        element.Raw = true;
                        element.End = end + 1;
                        p = end + 1;
                        continue;
                    }
                }
            }

            stack.Push(element);
        }

        while (stack.Count > 1)
        {
            var node = stack.Pop();
            node.End = text.Length;
            if (!Optional.Contains(node.Name))
                errors.Add((node.Start, node.Open.Length, $"Missing closing tag </{node.Name}>."));
        }

        return new Result(root, errors);
    }

    internal sealed class Node(string name, string open, int start, int openEnd)
    {
        internal List<Node> Children = [];
        internal string Name = name, Open = open, Close = "";
        internal bool Raw;
        internal int Start = start, OpenEnd = openEnd, End = openEnd;
    }

    internal sealed record Result(Node Root, List<(int Start, int Length, string Message)> Errors);
}