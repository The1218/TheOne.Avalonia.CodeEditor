using System.Text;

namespace TheOne.Avalonia.Controls;

public partial class CodeEditor
{
    private bool TryAlignCloser(char closer)
    {
        if (!IsAutoIndentEnabled || SelectionLength != 0 || closer is not ('}' or ']' or ')') ||
            InsideLiteral(_caret)) return false;
        var row = Document.LineAt(_caret);
        var line = Document.Lines[row];
        var column = _caret - line.Start;
        if (column == 0 || !string.IsNullOrWhiteSpace(line.Text[..column])) return false;
        var opener = closer == '}' ? '{' : closer == ']' ? '[' : '(';
        var depth = 1;
        var scanned = 0;
        for (var n = row - 1; n >= Math.Max(0, row - 512) && scanned < 20000; n--)
        {
            var previous = Document.Lines[n];
            scanned += previous.Text.Length;
            foreach (var token in previous.Tokens.Reverse().Where(t => t.Kind == CodeTokenKind.Punctuation))
                for (var p = token.Start + token.Length - 1; p >= token.Start; p--)
                {
                    var ch = previous.Text[p];
                    if (ch == closer)
                    {
                        depth++;
                    }
                    else if (ch == opener && --depth == 0)
                    {
                        var indent = new string(previous.Text.TakeWhile(c => c is ' ' or '\t').ToArray());
                        Edit(line.Start, column, indent + closer);
                        return true;
                    }
                }
        }

        return false;
    }

    // Work on the current lexical row only. Enter never reformats the whole document.
    private bool ExpandInlineBlock(CodeLine line, int column, string indent)
    {
        if (SelectionLength != 0 || InsideLiteral(_caret)) return false;
        var punctuation = line.Tokens.Where(t => t.Kind == CodeTokenKind.Punctuation).ToArray();
        var opens = new Stack<int>();
        foreach (var token in punctuation.Where(t => t.Start < column))
            for (var p = token.Start; p < Math.Min(column, token.Start + token.Length); p++)
                if (line.Text[p] == '{') opens.Push(p);
                else if (line.Text[p] == '}' && opens.Count > 0) opens.Pop();
        if ((CodeLanguage.Get(CodeType).Braces ||
             CodeType is CodeType.Python or CodeType.Ruby or CodeType.Lua or CodeType.Bash) &&
            opens.TryPeek(out var open))
        {
            var depth = 1;
            var close = -1;
            foreach (var token in punctuation.Where(t => t.Start >= column))
            {
                for (var p = token.Start; p < token.Start + token.Length; p++)
                    if (line.Text[p] == '{')
                    {
                        depth++;
                    }
                    else if (line.Text[p] == '}' && --depth == 0)
                    {
                        close = p;
                        break;
                    }

                if (close >= 0) break;
            }

            if (close < 0 || close - column > 16000) return false;
            var start = column;
            while (start > open + 1 && char.IsWhiteSpace(line.Text[start - 1])) start--;
            var body = line.Text[column..close].Trim();
            if (body.Length > 0 && CodeType is CodeType.CSharp or CodeType.C or CodeType.Cpp or CodeType.Java
                    or CodeType.Dart or CodeType.JavaScript or CodeType.TypeScript or CodeType.Css)
                body = CodeFormatting
                    .Format(new CodeFormattingContext(body, CodeType, TabSize, ConvertTabsToSpaces, Document.NewLine),
                        default).Trim();
            var insertion = IndentedBody(body, indent) + "}";
            Edit(line.Start + start, close + 1 - start, insertion,
                line.Start + start + Document.NewLine.Length + indent.Length + IndentUnit.Length);
            return true;
        }

        if (CodeLanguage.Get(CodeType).Markup)
        {
            var before = line.Text[..column].TrimEnd();
            var after = line.Text[column..];
            var tagStart = before.LastIndexOf('<');
            if (tagStart < 0 || !before.EndsWith('>') || before.EndsWith("/>", StringComparison.Ordinal) ||
                before[tagStart..].StartsWith("</", StringComparison.Ordinal)) return false;
            var name = new string(before[(tagStart + 1)..]
                .TakeWhile(c => char.IsLetterOrDigit(c) || c is ':' or '-' or '_').ToArray());
            if (name.Length == 0 || (CodeType == CodeType.Html && CodeHtml.Void.Contains(name))) return false;
            var closing = MarkupCloser(after, name);
            if (closing < 0) return false;
            var body = after[..closing].Trim();
            var insertion = IndentedBody(body, indent);
            Edit(_caret, closing, insertion, _caret + Document.NewLine.Length + indent.Length + IndentUnit.Length);
            return true;
        }

        if (CodeType is CodeType.Ruby or CodeType.Lua or CodeType.Bash)
        {
            var words = line.Tokens.Where(t => t.Kind is not (CodeTokenKind.Comment or CodeTokenKind.String))
                .Select(t => (Value: line.Text.Substring(t.Start, t.Length), t.Start)).ToArray();
            var blocks = new Stack<(string Open, string Close)>();
            var commandStart = true;
            var targetDepth = -1;
            foreach (var word in words)
            {
                if (word.Start >= column && targetDepth < 0) targetDepth = blocks.Count;
                var close = CodeType switch
                {
                    CodeType.Ruby when word.Value is "def" or "class" or "module" or "do" || (commandStart &&
                        word.Value is "if" or "unless" or "case" or "begin" or "while" or "until" or "for") => "end",
                    CodeType.Lua when word.Value is "function" or "if" or "for" or "while" or "do" => "end",
                    CodeType.Lua when word.Value == "repeat" => "until",
                    CodeType.Bash when commandStart && word.Value == "if" => "fi",
                    CodeType.Bash when commandStart && word.Value is "for" or "while" or "until" or "select" => "done",
                    _ => ""
                };
                if (close.Length > 0 && !(word.Value == "do" && blocks.TryPeek(out var loop) &&
                                          loop.Open is "for" or "while" or "until"))
                {
                    blocks.Push((word.Value, close));
                }
                else if (blocks.TryPeek(out var block) && word.Value == block.Close)
                {
                    if (word.Start >= column && blocks.Count == targetDepth)
                    {
                        var body = line.Text[column..word.Start].Trim();
                        var start = column;
                        while (start > 0 && char.IsWhiteSpace(line.Text[start - 1])) start--;
                        Edit(line.Start + start, word.Start - start, IndentedBody(body, indent),
                            line.Start + start + Document.NewLine.Length + indent.Length + IndentUnit.Length);
                        return true;
                    }

                    blocks.Pop();
                }

                commandStart = word.Value is ";" or "then" or "do" or "else";
            }
        }

        return false;
    }

    private bool OpensMarkupBlock(string before)
    {
        var start = before.LastIndexOf('<');
        if (start < 0 || !before.EndsWith('>') || before.EndsWith("/>", StringComparison.Ordinal) ||
            before[start..].StartsWith("</", StringComparison.Ordinal)) return false;
        var name = new string(before[(start + 1)..].TakeWhile(c => char.IsLetterOrDigit(c) || c is ':' or '-' or '_')
            .ToArray());
        return name.Length > 0 && (CodeType != CodeType.Html || !CodeHtml.Void.Contains(name));
    }

    private int MarkupCloser(string after, string name)
    {
        var depth = 1;
        var comparison = CodeType == CodeType.Html ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var p = 0; p < Math.Min(after.Length, 16000); p++)
        {
            if (after[p] != '<') continue;
            var start = p;
            var closing = p + 1 < after.Length && after[p + 1] == '/';
            var from = p + (closing ? 2 : 1);
            var tag = new string(after[from..].TakeWhile(c => char.IsLetterOrDigit(c) || c is ':' or '-' or '_')
                .ToArray());
            var quote = '\0';
            for (p = from; p < after.Length; p++)
            {
                var ch = after[p];
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

            if (!tag.Equals(name, comparison)) continue;
            if (closing && --depth == 0) return start;
            if (!closing && p > 0 && after[p - 1] != '/') depth++;
        }

        return -1;
    }

    private string IndentedBody(string body, string indent)
    {
        var builder = new StringBuilder(Document.NewLine);
        var rows = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var n = 0; n < rows.Length; n++)
        {
            if (n > 0) builder.Append(Document.NewLine);
            builder.Append(indent).Append(IndentUnit).Append(rows[n]);
        }

        return builder.Append(Document.NewLine).Append(indent).ToString();
    }
}