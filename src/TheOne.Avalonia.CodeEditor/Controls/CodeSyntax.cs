namespace TheOne.Avalonia.Controls;

internal enum CodeTokenKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Type,
    Punctuation
}

internal readonly record struct CodeToken(int Start, int Length, CodeTokenKind Kind);

internal readonly record struct CodeLexState(
    string? End = null,
    CodeTokenKind Kind = CodeTokenKind.Plain,
    bool Escapes = false,
    bool Doubled = false,
    int Depth = 0,
    string? NestedStart = null,
    byte LineTerminator = 0);

// Deterministic scanning, without regex backtracking or grammar assemblies.
internal static class CodeSyntax
{
    internal static string LanguageName(CodeType type)
    {
        return CodeLanguage.Get(type).Name;
    }

    internal static void Update(CodeDocument document, CodeType type, int first = 0, bool force = false,
        CancellationToken cancellation = default, bool visualScopes = true)
    {
        var state = first > 0 ? document.Lines[first - 1].EndState : default;
        var highlighting = first > 0 ? document.Lines[first - 1].EndHighlightState : default;
        for (var i = first; i < document.Lines.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = document.Lines[i];
            if (!force && line.Lexed && line.StartState == state &&
                (!visualScopes || line.StartHighlightState == highlighting)) break;
            line.StartState = state;
            line.Tokens = Scan(line.Text, type, ref state);
            line.StartHighlightState = highlighting;
            line.Highlights = visualScopes
                ? CodeHighlighting.Scan(line.Text, type, line.Tokens, line.StartState, state, ref highlighting)
                : [];
            line.EndHighlightState = highlighting;
            line.EndState = state;
            line.Lexed = true;
            line.LexVersion++;
            document.UpdateCodeWords(line);
        }
    }

    internal static CodeToken[] Scan(string text, CodeType type, ref CodeLexState state)
    {
        if (type == CodeType.PlainText || !Enum.IsDefined(type))
        {
            state = default;
            return [];
        }

        var language = CodeLanguage.Get(type);
        var tokens = new List<CodeToken>();
        var i = 0;
        CodeLexState? pendingState = null;

        bool At(string value)
        {
            return text.AsSpan(i).StartsWith(value, StringComparison.Ordinal);
        }

        void Add(int start, CodeTokenKind kind)
        {
            if (i > start) tokens.Add(new CodeToken(start, i - start, kind));
        }

        while (i < text.Length)
        {
            var start = i;
            if (state.End is { } terminator)
            {
                var kind = state.Kind;
                if (state.LineTerminator > 0)
                {
                    var candidate = state.LineTerminator == 2 ? text.TrimStart('\t') :
                        state.LineTerminator == 3 ? text.TrimStart() : text;
                    if (candidate == terminator) state = default;
                    i = text.Length;
                    Add(start, kind);
                    continue;
                }

                if (type == CodeType.Markdown)
                {
                    if (text.TrimStart().StartsWith(terminator, StringComparison.Ordinal)) state = default;
                    i = text.Length;
                    Add(start, kind);
                    continue;
                }

                while (i < text.Length)
                {
                    if (state.Escapes && text[i] == '\\')
                    {
                        i = Math.Min(text.Length, i + 2);
                        continue;
                    }

                    if (state.NestedStart is { } nested && At(nested))
                    {
                        state = state with { Depth = state.Depth + 1 };
                        i += nested.Length;
                        continue;
                    }

                    if (At(terminator))
                    {
                        i += terminator.Length;
                        if (state.Depth > 1)
                        {
                            state = state with { Depth = state.Depth - 1 };
                            continue;
                        }

                        if (state.Doubled && i < text.Length &&
                            text.AsSpan(i).StartsWith(terminator, StringComparison.Ordinal))
                        {
                            i += terminator.Length;
                            continue;
                        }

                        state = default;
                        break;
                    }

                    i++;
                }

                Add(start, kind);
                continue;
            }

            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (language.BlockStart is { } block && At(block))
            {
                i += block.Length;
                state = new CodeLexState(language.BlockEnd, CodeTokenKind.Comment, Depth: 1,
                    NestedStart: type == CodeType.Rust ? block : null);
                Add(start, CodeTokenKind.Comment);
                continue;
            }

            if (language.LineComment is { } comment && At(comment))
            {
                i = text.Length;
                Add(start, CodeTokenKind.Comment);
                break;
            }

            if (type == CodeType.Markdown)
            {
                if (At("```") || At("~~~"))
                {
                    var fence = text.Substring(i, 3);
                    i = text.Length;
                    state = new CodeLexState(fence, CodeTokenKind.String);
                    Add(start, CodeTokenKind.String);
                    break;
                }

                if (text[i] == '#')
                {
                    i = text.Length;
                    Add(start, CodeTokenKind.Keyword);
                    break;
                }
            }

            if (type is CodeType.Python or CodeType.Toml or CodeType.Java or CodeType.Kotlin or CodeType.Swift
                    or CodeType.Dart && (At("\"\"\"") || At("'''")))
            {
                var quote = text.Substring(i, 3);
                i += 3;
                state = new CodeLexState(quote, CodeTokenKind.String, true);
                Add(start, CodeTokenKind.String);
                continue;
            }

            if (type == CodeType.CSharp && At("@\""))
            {
                i += 2;
                state = new CodeLexState("\"", CodeTokenKind.String, false, true);
                Add(start, CodeTokenKind.String);
                continue;
            }

            if (type == CodeType.CSharp && At("\"\"\""))
            {
                while (i < text.Length && text[i] == '"') i++;
                state = new CodeLexState(text[start..i], CodeTokenKind.String);
                Add(start, CodeTokenKind.String);
                continue;
            }

            if (type == CodeType.Cpp && (At("R\"") || At("u8R\"") || At("uR\"") || At("LR\"")))
            {
                var quote = text.IndexOf('"', i);
                var opening = text.IndexOf('(', quote + 1);
                if (opening > quote && opening - quote <= 17)
                {
                    var delimiter = text[(quote + 1)..opening];
                    i = opening + 1;
                    state = new CodeLexState(")" + delimiter + "\"", CodeTokenKind.String);
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type == CodeType.Rust && text[i] == 'r')
            {
                var opening = i + 1;
                while (opening < text.Length && text[opening] == '#') opening++;
                if (opening < text.Length && text[opening] == '"')
                {
                    var hashes = text[(i + 1)..opening];
                    i = opening + 1;
                    state = new CodeLexState("\"" + hashes, CodeTokenKind.String);
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type is CodeType.Bash or CodeType.Ruby && At("<<") && !At("<<<"))
            {
                var opening = i + 2;
                byte mode = 1;
                if (opening < text.Length && text[opening] is '-' or '~')
                {
                    mode = type == CodeType.Ruby ? (byte)3 : (byte)2;
                    opening++;
                }

                while (opening < text.Length && char.IsWhiteSpace(text[opening])) opening++;
                var quote = opening < text.Length && text[opening] is '\'' or '"' or '`' ? text[opening++] : '\0';
                var end = opening;
                while (end < text.Length &&
                       (quote != '\0' ? text[end] != quote : CodeDocument.IsIdentifier(text[end]))) end++;
                if (end > opening)
                {
                    pendingState = new CodeLexState(text[opening..end], CodeTokenKind.String, LineTerminator: mode);
                    i = Math.Min(text.Length, end + (quote == '\0' ? 0 : 1));
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type == CodeType.Ruby && text[i] == '%' && i + 2 < text.Length && "qQwWiIxrs".Contains(text[i + 1]))
            {
                var opening = text[i + 2];
                var closing = opening switch { '(' => ')', '[' => ']', '{' => '}', '<' => '>', _ => opening };
                if (!char.IsLetterOrDigit(opening) && !char.IsWhiteSpace(opening))
                {
                    i += 3;
                    state = new CodeLexState(closing.ToString(), CodeTokenKind.String, true, Depth: 1,
                        NestedStart: closing != opening ? opening.ToString() : null);
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type == CodeType.Lua && text[i] == '[')
            {
                var opening = i + 1;
                while (opening < text.Length && text[opening] == '=') opening++;
                if (opening < text.Length && text[opening] == '[')
                {
                    var equals = text[(i + 1)..opening];
                    i = opening + 1;
                    state = new CodeLexState("]" + equals + "]", CodeTokenKind.String);
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type == CodeType.Sql && text[i] == '[')
            {
                i++;
                state = new CodeLexState("]", CodeTokenKind.String, Doubled: true);
                Add(start, CodeTokenKind.String);
                continue;
            }

            if (type == CodeType.PowerShell && (At("@\"") || At("@'")))
            {
                var quote = text[i + 1];
                i += 2;
                state = new CodeLexState(quote + "@", CodeTokenKind.String);
                Add(start, CodeTokenKind.String);
                continue;
            }

            if (type is CodeType.JavaScript or CodeType.TypeScript && text[i] == '/')
            {
                var previous = i - 1;
                while (previous >= 0 && char.IsWhiteSpace(text[previous])) previous--;
                var word = previous;
                while (word >= 0 && CodeDocument.IsIdentifier(text[word])) word--;
                var previousWord = text[(word + 1)..(previous + 1)];
                if (previous < 0 || "=([{,:;!&|?".Contains(text[previous]) ||
                    previousWord is "return" or "yield" or "case")
                {
                    i++;
                    var characterClass = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\')
                        {
                            i = Math.Min(text.Length, i + 2);
                            continue;
                        }

                        if (text[i] == '[') characterClass = true;
                        else if (text[i] == ']') characterClass = false;
                        if (text[i++] == '/' && !characterClass) break;
                    }

                    while (i < text.Length && char.IsLetter(text[i])) i++;
                    Add(start, CodeTokenKind.String);
                    continue;
                }
            }

            if (type == CodeType.Rust && text[i] == '\'' && i + 1 < text.Length && char.IsLetter(text[i + 1]) &&
                (i + 2 >= text.Length || text[i + 2] != '\''))
            {
                i++;
                while (i < text.Length && CodeDocument.IsIdentifier(text[i])) i++;
                Add(start, CodeTokenKind.Type);
                continue;
            }

            if (text[i] is '"' or '\'' or '`')
            {
                var quote = text[i++];
                var multiline = quote == '`' ||
                                type is CodeType.Ruby or CodeType.Bash or CodeType.PowerShell or CodeType.Sql;
                var doubled = type == CodeType.Sql;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && type != CodeType.Sql)
                    {
                        i = Math.Min(text.Length, i + 2);
                        continue;
                    }

                    if (text[i++] != quote) continue;
                    if (doubled && i < text.Length && text[i] == quote)
                    {
                        i++;
                        continue;
                    }

                    closed = true;
                    break;
                }

                var next = i;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                Add(start,
                    type == CodeType.Json && next < text.Length && text[next] == ':'
                        ? CodeTokenKind.Type
                        : CodeTokenKind.String);
                if (!closed && (multiline || text.EndsWith('\\')))
                    state = new CodeLexState(quote.ToString(), CodeTokenKind.String, type != CodeType.Sql, doubled);
                continue;
            }

            if (char.IsDigit(text[i]) ||
                (text[i] == '.' && (i == 0 || text[i - 1] != '.') && i + 1 < text.Length &&
                 char.IsDigit(text[i + 1])) || (text[i] == '#' && type == CodeType.Css))
            {
                i = NumberEnd(text, i, type);
                Add(start, CodeTokenKind.Number);
                continue;
            }

            if (char.IsLetter(text[i]) || text[i] is '_' or '$' or '@')
            {
                i++;
                while (i < text.Length &&
                       (CodeDocument.IsIdentifier(text[i]) || (language.Markup && text[i] is ':' or '-'))) i++;
                var word = text[start..i];
                var next = i;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                var attribute = language.Markup && next < text.Length && text[next] == '=';
                var key = type is CodeType.Yaml or CodeType.Css or CodeType.Toml or CodeType.Ini &&
                          next < text.Length && text[next] is ':' or '=';
                Add(start,
                    language.KeywordSet.Contains(word) ? CodeTokenKind.Keyword :
                    attribute || key || char.IsUpper(word[0]) ? CodeTokenKind.Type : CodeTokenKind.Plain);
                continue;
            }

            i++;
            Add(start,
                language.Markup && text[start] is '<' or '>' or '/'
                    ? CodeTokenKind.Keyword
                    : CodeTokenKind.Punctuation);
        }

        if (pendingState is { } pending) state = pending;
        return tokens.ToArray();
    }

    private static int NumberEnd(string text, int start, CodeType type)
    {
        var i = start;
        if (text[i] == '#')
        {
            i++;
            while (i < text.Length && char.IsAsciiHexDigit(text[i])) i++;
            return i;
        }

        // Keep malformed suffixes in the numeric token so diagnostics can reject
        // them. A dot joins only a decimal fraction, never member access/ranges.
        i++;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_' || (type == CodeType.Css && c == '%') ||
                (type == CodeType.Cpp && c == '\''))
            {
                i++;
                continue;
            }

            if (c == '.' && text[i - 1] != '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                continue;
            }

            if (c is '+' or '-' && text[i - 1] is 'e' or 'E' or 'p' or 'P' && i + 1 < text.Length &&
                char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }
}