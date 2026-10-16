namespace TheOne.Avalonia.Controls;

// Compact scope ids, mapped to brushes only by the renderer. The first seven
// entries deliberately match the coarse tokens used by diagnostics/completion.
internal enum CodeHighlightKind : byte
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Type,
    Punctuation,
    Escape,
    Regex,
    Tag,
    Attribute,
    Function,
    Preprocessor,
    Heading,
    Fence,
    Interpolation
}

internal readonly record struct CodeHighlightSpan(int Start, int Length, CodeHighlightKind Kind);

internal sealed record CodeHighlightFrame(
    string Quote,
    string Opener,
    bool Escapes,
    bool Doubled,
    bool Multiline,
    int Depth = -1,
    CodeLexState Lex = default,
    CodeHighlightFrame? Parent = null,
    int Level = 1);

internal sealed record CodeFence(string Marker, CodeType Language);

internal sealed record CodeHighlightContext(CodeHighlightFrame? Frame, CodeFence? Fence, CodeLexState Lex);

internal readonly record struct CodeHighlightState(CodeHighlightContext? Context = null)
{
    internal CodeHighlightState(CodeHighlightFrame? Frame = null, CodeFence? Fence = null, CodeLexState Lex = default)
        : this(new CodeHighlightContext(Frame, Fence, Lex))
    {
    }

    internal CodeHighlightFrame? Frame => Context?.Frame;
    internal CodeFence? Fence => Context?.Fence;
}

// TextMate's scopes, begin/end contexts and embedded-language idea, without a
// regex VM or external grammars. Visual scopes never change semantic tokens.
internal static class CodeHighlighting
{
    internal const int ColorCount = (int)CodeHighlightKind.Interpolation + 1;

    internal static string ScopeName(CodeHighlightKind kind)
    {
        return kind switch
        {
            CodeHighlightKind.Escape => "constant.character.escape",
            CodeHighlightKind.Regex => "string.regexp",
            CodeHighlightKind.Tag => "entity.name.tag",
            CodeHighlightKind.Attribute => "entity.other.attribute-name",
            CodeHighlightKind.Function => "entity.name.function",
            CodeHighlightKind.Preprocessor => "meta.preprocessor",
            CodeHighlightKind.Heading => "markup.heading",
            CodeHighlightKind.Fence => "punctuation.definition.fenced",
            CodeHighlightKind.Interpolation => "punctuation.section.interpolation",
            _ => kind.ToString().ToLowerInvariant()
        };
    }

    internal static CodeHighlightSpan[] Scan(string text, CodeType type, CodeToken[] tokens,
        CodeLexState lexicalStart, CodeLexState lexicalEnd, ref CodeHighlightState state)
    {
        if (type == CodeType.PlainText)
        {
            state = default;
            return [];
        }

        var result = new List<CodeHighlightSpan>();
        var frame = state.Frame;
        var lex = state.Context?.Lex ?? lexicalStart;
        if (type == CodeType.Markdown)
        {
            var trimmed = text.AsSpan().TrimStart();
            var indent = text.Length - trimmed.Length;
            if (state.Fence is { } fence)
            {
                if (indent <= 3 && FenceLength(trimmed, fence.Marker[0]) >= fence.Marker.Length &&
                    trimmed[FenceLength(trimmed, fence.Marker[0])..].Trim().IsEmpty)
                {
                    state = default;
                    return [new CodeHighlightSpan(0, text.Length, CodeHighlightKind.Fence)];
                }

                if (fence.Language == CodeType.PlainText)
                    return [new CodeHighlightSpan(0, text.Length, CodeHighlightKind.String)];
                Code(text, fence.Language, null, ref lex, ref frame, result, 0, true);
                state = new CodeHighlightState(frame, state.Fence, lex);
                return result.ToArray();
            }

            if (indent <= 3 && trimmed.Length >= 3 && trimmed[0] is '`' or '~' &&
                FenceLength(trimmed, trimmed[0]) is var count && count >= 3)
            {
                var info = trimmed[count..].Trim();
                if (trimmed[0] != '`' || !info.Contains('`'))
                {
                    var end = info.IndexOfAny(' ', '\t');
                    if (end >= 0) info = info[..end];
                    state = new CodeHighlightState(Fence: new CodeFence(new string(trimmed[0], count),
                        EmbeddedLanguage(info)));
                    return [new CodeHighlightSpan(0, text.Length, CodeHighlightKind.Fence)];
                }
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                return [new CodeHighlightSpan(0, text.Length, CodeHighlightKind.Heading)];
            state = default;
            return [];
        }

        Code(text, type, lex == lexicalStart ? tokens : null, ref lex, ref frame, result, 0, false, lexicalEnd);
        // Ordinary one-line literals recover at the next line. Template/verbatim
        // strings and an expression still inside interpolation retain their stack.
        while (frame is { Multiline: false, Depth: < 0 }) frame = frame.Parent;
        state = frame == null && lex == lexicalEnd ? default : new CodeHighlightState(frame, Lex: lex);
        return result.ToArray();
    }

    private static void Code(string text, CodeType type, CodeToken[]? initial, ref CodeLexState lex,
        ref CodeHighlightFrame? frame, List<CodeHighlightSpan> output, int offset, bool embedded,
        CodeLexState initialEnd = default)
    {
        var i = 0;

        void Add(int start, int length, CodeHighlightKind kind)
        {
            if (length > 0) output.Add(new CodeHighlightSpan(offset + start, length, kind));
        }

        while (i < text.Length)
        {
            if (frame is { Depth: < 0 } literal)
            {
                var start = i;
                while (i < text.Length)
                {
                    if (literal.Escapes && text[i] == '\\')
                    {
                        Add(start, i - start, CodeHighlightKind.String);
                        var length = EscapeLength(text, i, text.Length);
                        Add(i, length, CodeHighlightKind.Escape);
                        i += length;
                        start = i;
                        continue;
                    }

                    if (literal.Opener == "{" && i + 1 < text.Length && text[i] is '{' or '}' && text[i + 1] == text[i])
                    {
                        i += 2;
                        continue;
                    }

                    if (text.AsSpan(i).StartsWith(literal.Quote, StringComparison.Ordinal))
                    {
                        i += literal.Quote.Length;
                        if (literal.Doubled && text.AsSpan(i).StartsWith(literal.Quote, StringComparison.Ordinal))
                        {
                            i += literal.Quote.Length;
                            continue;
                        }

                        Add(start, i - start, CodeHighlightKind.String);
                        frame = literal.Parent;
                        break;
                    }

                    if (text.AsSpan(i).StartsWith(literal.Opener, StringComparison.Ordinal))
                    {
                        Add(start, i - start, CodeHighlightKind.String);
                        Add(i, literal.Opener.Length, CodeHighlightKind.Interpolation);
                        i += literal.Opener.Length;
                        frame = literal with { Depth = 0, Lex = default };
                        break;
                    }

                    i++;
                }

                if (i == text.Length && ReferenceEquals(frame, literal))
                    Add(start, i - start, CodeHighlightKind.String);
                continue;
            }

            var segment = i == 0 ? text : text[i..];
            var inputLex = frame?.Lex ?? lex;
            var activeLex = inputLex;
            var scanned = i == 0 && initial != null ? initial : CodeSyntax.Scan(segment, type, ref activeLex);
            if (i == 0 && initial != null) activeLex = initialEnd;
            var moved = false;
            var stringStart = -1;
            var previousStringEnd = -1;
            foreach (var token in scanned)
            {
                var start = i + token.Start;
                if (token.Kind != CodeTokenKind.Comment &&
                    TryLiteral(text, start, type, frame, out var opening, out var next))
                {
                    // A preceding comment/literal in this code segment is now
                    // closed. Do not restore its incoming state after the child.
                    if (frame != null)
                    {
                        frame = frame with { Lex = default };
                        next = next! with { Parent = frame };
                    }
                    else
                    {
                        lex = default;
                    }

                    Add(start, opening, CodeHighlightKind.String);
                    i = start + opening;
                    frame = next;
                    moved = true;
                    break;
                }

                if (frame is { Depth: >= 0 } expression && token.Kind == CodeTokenKind.Punctuation)
                {
                    if (text[start] == '{')
                    {
                        frame = expression with { Depth = expression.Depth + 1 };
                    }
                    else if (text[start] == '}')
                    {
                        if (expression.Depth == 0)
                        {
                            Add(start, 1, CodeHighlightKind.Interpolation);
                            i = start + 1;
                            frame = expression with { Depth = -1 };
                            moved = true;
                            break;
                        }

                        frame = expression with { Depth = expression.Depth - 1 };
                    }
                }

                if (embedded || frame != null) Add(start, token.Length, (CodeHighlightKind)token.Kind);
                if (token.Kind == CodeTokenKind.String)
                {
                    if (previousStringEnd != start) stringStart = start;
                    previousStringEnd = start + token.Length;
                }
                else
                {
                    previousStringEnd = -1;
                }

                var escapes = !(stringStart == i && inputLex.End != null && inputLex.Kind == CodeTokenKind.String &&
                                !inputLex.Escapes) &&
                              (stringStart < 0 || StringEscapes(text, stringStart, type));
                Decorate(text, start, token.Length, token.Kind, type, output, offset, escapes);
            }

            if (moved)
            {
                initial = null;
                continue;
            }

            if (frame != null) frame = frame with { Lex = activeLex };
            else lex = activeLex;
            break;
        }
    }

    private static bool TryLiteral(string text, int start, CodeType type, CodeHighlightFrame? parent, out int length,
        out CodeHighlightFrame? frame)
    {
        length = 0;
        frame = null;
        if (parent?.Level >= 16) return false;
        var tail = text.AsSpan(start);
        var verbatim = false;
        var opener = "{";
        var multiline = false;
        var prefix = 0;
        if (type is CodeType.JavaScript or CodeType.TypeScript && tail.StartsWith("`", StringComparison.Ordinal))
        {
            opener = "${";
            multiline = true;
        }
        else if (type == CodeType.CSharp)
        {
            if (tail.StartsWith("$@\"", StringComparison.Ordinal) || tail.StartsWith("@$\"", StringComparison.Ordinal))
            {
                prefix = 2;
                verbatim = true;
                multiline = true;
            }
            else if (tail.StartsWith("$\"", StringComparison.Ordinal))
            {
                prefix = 1;
            }
            else
            {
                return false;
            }
        }
        else if (type == CodeType.Python && tail.Length >= 2 && tail[0] is 'f' or 'F' && tail[1] is '\'' or '"')
        {
            prefix = 1;
        }
        else
        {
            return false;
        }

        var quote = text[start + prefix].ToString();
        if (quote != "`" && tail.Length >= prefix + 3 &&
            tail[prefix..].StartsWith(new string(quote[0], 3), StringComparison.Ordinal))
        {
            quote = new string(quote[0], 3);
            multiline = true;
        }

        length = prefix + quote.Length;
        frame = new CodeHighlightFrame(quote, opener, !verbatim, verbatim, multiline, Parent: parent,
            Level: (parent?.Level ?? 0) + 1);
        return true;
    }

    private static void Decorate(string text, int start, int length, CodeTokenKind kind, CodeType type,
        List<CodeHighlightSpan> output, int offset, bool escapes)
    {
        void Add(int first, int count, CodeHighlightKind scope)
        {
            if (count > 0) output.Add(new CodeHighlightSpan(offset + first, count, scope));
        }

        var end = start + length;
        if (kind == CodeTokenKind.String || (type == CodeType.Json && kind == CodeTokenKind.Type && text[start] == '"'))
        {
            if (type is CodeType.JavaScript or CodeType.TypeScript && text[start] == '/')
                Add(start, length, CodeHighlightKind.Regex);
            if (escapes && type is not (CodeType.Sql or CodeType.Ini or CodeType.Yaml))
                for (var i = start; i < end; i++)
                    if (text[i] == '\\')
                    {
                        var count = EscapeLength(text, i, end);
                        Add(i, count, CodeHighlightKind.Escape);
                        i += count - 1;
                    }

            return;
        }

        if (kind is CodeTokenKind.Comment or CodeTokenKind.Punctuation or CodeTokenKind.Number) return;
        var next = end;
        while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
        var previous = start - 1;
        while (previous >= 0 && char.IsWhiteSpace(text[previous])) previous--;
        if (CodeLanguage.Get(type).Markup)
        {
            if (next < text.Length && text[next] == '=') Add(start, length, CodeHighlightKind.Attribute);
            else if (previous >= 0 && (text[previous] == '<' ||
                                       (text[previous] == '/' && previous > 0 && text[previous - 1] == '<')))
                Add(start, length, CodeHighlightKind.Tag);
        }
        else if (kind != CodeTokenKind.Keyword &&
                 type is not (CodeType.Markdown or CodeType.Json or CodeType.Yaml or CodeType.Toml or CodeType.Ini) &&
                 next < text.Length && text[next] == '(')
        {
            Add(start, length, CodeHighlightKind.Function);
        }

        if (type is CodeType.CSharp or CodeType.C or CodeType.Cpp && previous >= 0 && text[previous] == '#')
            Add(start, length, CodeHighlightKind.Preprocessor);
    }

    private static int EscapeLength(string text, int start, int end)
    {
        if (start + 1 >= end) return 1;
        var count = text[start + 1] switch { 'u' => 4, 'U' => 8, 'x' => 4, _ => 0 };
        var i = start + 2;
        if (count > 0)
        {
            var braces = text[start + 1] == 'u' && i < end && text[i] == '{';
            if (braces)
            {
                i++;
                count = 6;
            }

            while (i < end && count-- > 0 && char.IsAsciiHexDigit(text[i])) i++;
            if (braces && i < end && text[i] == '}') i++;
        }

        return i - start;
    }

    private static bool StringEscapes(string text, int start, CodeType type)
    {
        var tail = text.AsSpan(start);
        return type switch
        {
            CodeType.CSharp => text[start] != '@' && !tail.StartsWith("\"\"\"", StringComparison.Ordinal),
            CodeType.Cpp => !(tail.StartsWith("R\"", StringComparison.Ordinal) ||
                              tail.StartsWith("u8R\"", StringComparison.Ordinal) ||
                              tail.StartsWith("uR\"", StringComparison.Ordinal) ||
                              tail.StartsWith("LR\"", StringComparison.Ordinal)),
            CodeType.Rust => text[start] != 'r', CodeType.Go => text[start] != '`',
            CodeType.Lua => text[start] != '[', CodeType.Bash => text[start] != '\'',
            CodeType.Python => start == 0 || text[start - 1] is not ('r' or 'R'),
            _ => true
        };
    }

    private static int FenceLength(ReadOnlySpan<char> text, char marker)
    {
        var n = 0;
        while (n < text.Length && text[n] == marker) n++;
        return n;
    }

    private static CodeType EmbeddedLanguage(ReadOnlySpan<char> name)
    {
        return name.ToString().ToLowerInvariant() switch
        {
            "cs" or "csharp" or "c#" => CodeType.CSharp, "js" or "javascript" => CodeType.JavaScript,
            "ts" or "typescript" => CodeType.TypeScript, "py" or "python" => CodeType.Python, "json" => CodeType.Json,
            "xml" => CodeType.Xml, "html" => CodeType.Html, "axaml" or "xaml" => CodeType.Axaml, "css" => CodeType.Css,
            "sql" => CodeType.Sql, "c" => CodeType.C, "cpp" or "c++" => CodeType.Cpp, "java" => CodeType.Java,
            "go" => CodeType.Go, "rust" or "rs" => CodeType.Rust, "kotlin" or "kt" => CodeType.Kotlin,
            "swift" => CodeType.Swift, "php" => CodeType.Php, "ruby" or "rb" => CodeType.Ruby,
            "sh" or "bash" or "shell" => CodeType.Bash, "ps1" or "powershell" => CodeType.PowerShell,
            "dart" => CodeType.Dart, "lua" => CodeType.Lua, "toml" => CodeType.Toml,
            "yaml" or "yml" => CodeType.Yaml, "ini" => CodeType.Ini, _ => CodeType.PlainText
        };
    }
}