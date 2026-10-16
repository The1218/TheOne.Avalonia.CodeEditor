namespace TheOne.Avalonia.Controls;

internal sealed record CodeIssue(
    int Start,
    int Length,
    string Message,
    string Code,
    CodeDiagnosticSeverity Severity = CodeDiagnosticSeverity.Error);

// Definite syntax checks on the same immutable token snapshot as completion.
// Dialect-specific constructs are left alone when there is insufficient evidence.
internal static class CodeLanguageDiagnostics
{
    internal static IReadOnlyList<CodeIssue> Analyze(CodeAnalysis a, CancellationToken cancellation)
    {
        var errors = new List<CodeIssue>();
        var t = a.Tokens;
        var language = a.Language;
        var code = Prefix(language);

        void Add(CodeAnalysis.Token token, string message, string suffix)
        {
            if (errors.Count < 200)
                errors.Add(new CodeIssue(token.Start, Math.Max(1, token.Text.Length), message, code + suffix));
        }

        var blocks = new Stack<(CodeAnalysis.Token Open, string Close, string Marker, bool Marked)>();
        var previousLine = -1;
        var commandStart = true;
        for (var n = 0; n < t.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            var item = t[n];
            var value = language is CodeType.PowerShell or CodeType.Php ? item.Text.ToLowerInvariant() : item.Text;
            var next = a.At(n + 1);
            var prior = a.At(n - 1);
            var first = previousLine != item.Line || commandStart;
            previousLine = item.Line;
            commandStart = value is ";" or "|" or "&" ||
                           (language == CodeType.Bash && value is "then" or "do" or "else");
            if (item.Kind == CodeTokenKind.String) continue;
            if (CodeNumericDiagnostics.Invalid(a, n, out var literal) && errors.Count < 200)
                errors.Add(new CodeIssue(item.Start, literal.Length,
                    $"Invalid numeric literal '{literal}' for {language}.", code + "NUM001"));
            if (language is not (CodeType.Css or CodeType.Sql or CodeType.Toml or CodeType.Ini or CodeType.Yaml
                or CodeType.Bash or CodeType.PowerShell))
            {
                var bad = value.AsSpan().IndexOfAny("（）｛｝［］；，“”");
                if (bad >= 0)
                    Add(item, $"Unexpected full-width symbol '{value[bad]}'; use the corresponding ASCII code symbol.",
                        "SYM001");
            }

            var declaration = value switch
            {
                "class" or "interface" or "enum" => language is CodeType.CSharp or CodeType.Cpp or CodeType.Java
                    or CodeType.JavaScript or CodeType.TypeScript or CodeType.Kotlin or CodeType.Swift or CodeType.Php
                    or CodeType.Dart,
                "struct" => language is CodeType.CSharp or CodeType.C or CodeType.Cpp or CodeType.Go or CodeType.Rust
                    or CodeType.Swift,
                "let" or "var" or "const" => language is CodeType.CSharp or CodeType.JavaScript or CodeType.TypeScript
                    or CodeType.Go or CodeType.Rust or CodeType.Kotlin or CodeType.Swift or CodeType.Dart,
                "val" => language == CodeType.Kotlin,
                "local" => language == CodeType.Lua,
                _ => false
            };
            if (prior is "." or "@" or ":" or "," or "<" || item.Kind != CodeTokenKind.Keyword) declaration = false;
            var anonymousType = language is CodeType.C or CodeType.Cpp ||
                                (language == CodeType.Go && value is "struct" or "interface") ||
                                (language is CodeType.JavaScript or CodeType.TypeScript or CodeType.Php &&
                                 value == "class" && prior is "=" or "return" or "new" or "(");
            if (declaration && (next is "=" or ";" or "}" || next.Length == 0 || (next == "{" && !anonymousType &&
                    value is "class" or "interface" or "enum" or "struct")))
                Add(item, $"Expected a name after '{value}'.", "DECL001");
            var function = value switch
            {
                "def" => language is CodeType.Python or CodeType.Ruby,
                "function" => language is CodeType.JavaScript or CodeType.TypeScript or CodeType.Php or CodeType.Lua
                    or CodeType.PowerShell or CodeType.Bash,
                "func" => language is CodeType.Go or CodeType.Swift,
                "fn" => language == CodeType.Rust,
                "fun" => language == CodeType.Kotlin,
                _ => false
            };
            if ((language is CodeType.Bash or CodeType.PowerShell && !first) || prior == ".") function = false;
            if (language is CodeType.JavaScript or CodeType.TypeScript && next == ":") function = false;
            if (function && (next is "{" or ";" or "=" or ":" || next.Length == 0 ||
                             (next == "(" && language is CodeType.Python or CodeType.Ruby)))
                Add(item, $"Expected a function name after '{value}'.", "DECL002");
            if (function && language == CodeType.Python && t.ElementAtOrDefault(n + 1)?.Identifier == true &&
                a.At(n + 2) != "(")
                Add(t[n + 1], "Expected '(' and a parameter list after the function name.", "DECL003");
            if (language == CodeType.CSharp && value == "var" && t.ElementAtOrDefault(n + 1)?.Identifier == true &&
                a.At(n + 2) == ";")
                Add(item, "An implicitly typed variable requires an initializer ('var name = value;').", "DECL004");
            if (language is CodeType.TypeScript or CodeType.Kotlin or CodeType.Swift && value == ":" &&
                next is "=" or ";" or "}")
                Add(item, "Expected a type or value after ':'.", "TYPE001");
            if (CodeLanguage.Get(language).Braces && language != CodeType.Css &&
                value is "if" or "while" or "switch" or "foreach" && next == "(" && a.At(n + 2) == ")")
                Add(item, $"Expected a condition/expression inside '{value} (...)'.", "COND002");
            if (value == "=" && prior is not ("=" or "!" or "<" or ">") && next is "" or ")" or "," or ";" or "}" &&
                language is not (CodeType.Bash or CodeType.Css))
                if (!(CodeLanguage.Get(language).Braces || language == CodeType.Python) || next is "" or ")" or ",")
                    Add(item, "Expected an expression after '='; provide a value or remove the assignment.", "EXPR001");
            if (language is CodeType.CSharp or CodeType.Java or CodeType.C or CodeType.Cpp or CodeType.Go
                or CodeType.Rust or CodeType.Kotlin or CodeType.Swift or CodeType.Dart or CodeType.Php or CodeType.Lua
                or CodeType.Python)
                if (value == "(" && a.Pairs.TryGetValue(n, out var close) && n > 0 && t[n - 1].Identifier &&
                    prior is not ("for" or "if" or "while" or "switch" or "catch"))
                {
                    var last = n;
                    for (var p = n + 1; p < close; p++)
                    {
                        if (a.Pairs.TryGetValue(p, out var end) && end > p)
                        {
                            last = end;
                            p = end;
                            continue;
                        }

                        if (a.At(p) == "," && (last == n || a.At(last) == ","))
                            Add(t[p], "Missing expression before ',' in the parameter/argument list.", "ARG001");
                        last = p;
                    }
                }

            if (language == CodeType.Python && value == "(" && prior == "def") continue;
            if (language == CodeType.Python && value == "=" && prior is not (":" or "=" or "!" or "<" or ">") &&
                next != "=")
            {
                var line = a.Document.Lines[item.Line];
                var head = line.Text.TrimStart();
                if ((head.StartsWith("if ", StringComparison.Ordinal) ||
                     head.StartsWith("elif ", StringComparison.Ordinal) ||
                     head.StartsWith("while ", StringComparison.Ordinal)) && !PythonNamedArgument(n))
                    Add(item,
                        "Assignment '=' is not a comparison in this condition; use '==' or an assignment expression ':='.",
                        "COND001");
            }

            if (language == CodeType.PowerShell && value is "if" or "elseif" or "while" or "foreach" or "switch" &&
                next != "(" && first)
                Add(item, $"Expected '(' after PowerShell '{value}'.", "HEADER001");
            if (language == CodeType.Sql)
            {
                var upper = value.ToUpperInvariant();
                var after = next.ToUpperInvariant();
                if (upper == "SELECT" && after is "FROM" or ";" or "")
                    Add(item, "SELECT requires a projection before FROM or the statement end.", "SELECT001");
                if (upper is "FROM" or "JOIN" && after is "WHERE" or "ON" or "ORDER" or "GROUP" or ";" or "")
                    Add(item, $"Expected a table or subquery after {upper}.", "TABLE001");
                if (upper is "WHERE" or "HAVING" && after is "ORDER" or "GROUP" or ";" or "")
                    Add(item, $"Expected a condition after {upper}.", "COND001");
                if (upper == "CASE") blocks.Push((item, "END", "", true));
                if (upper == "END" && blocks.Count > 0) Close("END", item);
            }

            if (language == CodeType.Ruby)
            {
                if (prior != "." &&
                    (value is "def" or "class" or "module" || (first &&
                                                               value is "if" or "unless" or "case" or "begin" or "while"
                                                                   or "until" or "for")) &&
                    !(value == "def" && EndlessRuby(n)))
                    blocks.Push((item, "end", "", true));
                else if (value == "do" && !(blocks.TryPeek(out var loop) && loop.Open.Line == item.Line &&
                                            loop.Open.Text is "while" or "until" or "for"))
                    blocks.Push((item, "end", "", true));
                else if (value == "end") Close("end", item);
                else if (first && value is "else" or "elsif" or "rescue" or "ensure" && blocks.Count == 0)
                    Add(item, $"Unexpected '{value}' without an enclosing block.", "BLOCK002");
            }

            if (language == CodeType.Lua)
            {
                if (value is "function" or "if" or "for" or "while")
                {
                    blocks.Push((item, "end", value == "if" ? "then" : value is "for" or "while" ? "do" : "",
                        value == "function"));
                }
                else if (value == "then" && blocks.TryPeek(out var conditional) && conditional.Marker == "then")
                {
                    blocks.Pop();
                    blocks.Push(conditional with { Marked = true });
                }
                else if (value == "repeat")
                {
                    blocks.Push((item, "until", "", true));
                }
                else if (value == "do")
                {
                    if (blocks.TryPeek(out var loop) && loop.Marker == "do" && !loop.Marked)
                    {
                        blocks.Pop();
                        blocks.Push(loop with { Marked = true });
                    }
                    else
                    {
                        blocks.Push((item, "end", "", true));
                    }
                }
                else if (value is "end" or "until")
                {
                    Close(value, item);
                }
            }

            if (language == CodeType.Bash)
            {
                if (first && value == "if")
                {
                    blocks.Push((item, "fi", "then", false));
                }
                else if (first && value is "for" or "while" or "until" or "select")
                {
                    blocks.Push((item, "done", "do", false));
                }
                else if (first && value == "case")
                {
                    blocks.Push((item, "esac", "in", false));
                }
                else if (value is "then" or "do" or "in" && blocks.TryPeek(out var block) && block.Marker == value)
                {
                    blocks.Pop();
                    blocks.Push(block with { Marked = true });
                }
                else if (first && value is "fi" or "done" or "esac")
                {
                    Close(value, item);
                }
            }
        }

        foreach (var block in blocks.Take(40))
            Add(block.Open, $"Unclosed '{block.Open.Text}' block; expected '{block.Close}'.", "BLOCK001");
        if (language == CodeType.Css) Css();
        if (language == CodeType.CSharp) CSharpStatements();
        if (language is CodeType.C or CodeType.Cpp or CodeType.Java or CodeType.Dart or CodeType.Php) Terminators();
        return errors;

        void CSharpStatements()
        {
            var curly = new Stack<int>();
            for (var row = 0; row < t.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                var end = row + 1;
                while (end < t.Count && t[end].Line == t[row].Line) end++;
                var count = end - row;
                var only = t[row];
                if (count <= 2 && only.Identifier && !CodeLanguage.Get(language).KeywordSet.Contains(only.Text))
                {
                    var next = end < t.Count ? t[end].Text : "";
                    var standalone = count == 2 && t[row + 1].Text == ";";
                    var body = curly.TryPeek(out var brace) && CSharpBody(brace);
                    if (standalone || (count == 1 && body && next is "}" or ""))
                        Add(only,
                            $"Incomplete or invalid statement '{only.Text}'; expected an assignment, invocation, increment/decrement, or a complete declaration.",
                            "STMT002");
                }

                for (var n = row; n < end; n++)
                {
                    if (t[n].Kind == CodeTokenKind.String) continue;
                    if (t[n].Text == "{") curly.Push(n);
                    else if (t[n].Text == "}" && curly.Count > 0) curly.Pop();
                }

                row = end;
            }
        }

        bool CSharpBody(int brace)
        {
            if (a.At(brace - 1) != ")" || !a.Pairs.TryGetValue(brace - 1, out var open)) return false;
            for (var p = open - 1; p >= Math.Max(0, open - 64) && a.At(p) is not (";" or "{" or "}"); p--)
                if (a.At(p) == "new")
                    return false;
            return true;
        }

        bool PythonNamedArgument(int at)
        {
            for (var p = at - 1; p >= Math.Max(0, at - 512) && t[p].Line == t[at].Line; p--)
            {
                if (a.At(p) == "lambda") return true;
                if (a.At(p) == "(" && a.Pairs.TryGetValue(p, out var end) && end > at)
                    return p > 0 && t[p - 1].Identifier &&
                           a.At(p - 1) is not ("if" or "elif" or "while" or "not" or "and" or "or");
            }

            return false;
        }

        bool EndlessRuby(int start)
        {
            for (var n = start + 1; n < t.Count && t[n].Line == t[start].Line; n++)
            {
                if (a.Pairs.TryGetValue(n, out var end) && end > n)
                {
                    n = end;
                    continue;
                }

                if (a.At(n) == "=" && a.At(n - 1) != "=" && a.At(n + 1) != "=") return true;
            }

            return false;
        }

        void Close(string closer, CodeAnalysis.Token at)
        {
            if (!blocks.TryPeek(out var block))
            {
                Add(at, $"Unexpected '{closer}' without a matching opening block.", "BLOCK002");
                return;
            }

            if (block.Close != closer)
            {
                Add(at, $"Expected '{block.Close}' to close '{block.Open.Text}', found '{closer}'.", "BLOCK002");
                return;
            }

            blocks.Pop();
            if (!block.Marked)
                Add(block.Open, $"Expected '{block.Marker}' in the '{block.Open.Text}' header.", "HEADER002");
        }

        void Terminators()
        {
            var rows = t.GroupBy(x => x.Line).Select(g => g.ToArray()).ToArray();
            var depth = 0;
            for (var n = 0; n < rows.Length; n++)
            {
                var row = rows[n];
                var before = depth;
                foreach (var item in row.Where(x => x.Kind != CodeTokenKind.String))
                    if (item.Text is "(" or "[") depth++;
                    else if (item.Text is ")" or "]") depth--;
                if (before != 0 || depth != 0 ||
                    row[0].Text is not ("return" or "throw" or "break" or "continue")) continue;
                var last = row[^1];
                var next = n + 1 == rows.Length ? "" : rows[n + 1][0].Text;
                if (last.Text != ";" && next is "}" or "" && (last.Identifier ||
                                                              last.Kind is CodeTokenKind.String
                                                                  or CodeTokenKind.Number || last.Text is ")" or "]"))
                    Add(last, "Expected ';' to terminate this statement.", "STMT001");
            }
        }

        void Css()
        {
            var frames = new Stack<bool>();
            var start = 0;
            var nested = 0;
            for (var n = 0; n < t.Count; n++)
            {
                var item = t[n];
                if (item.Kind == CodeTokenKind.String) continue;
                if (item.Text is "(" or "[") nested++;
                if (item.Text is ")" or "]") nested--;
                if (nested > 0) continue;
                if (item.Text == "{")
                {
                    var header = string.Concat(t.Skip(start).Take(n - start).Select(x => x.Text));
                    frames.Push(
                        !new[] { "@media", "@supports", "@keyframes", "@layer", "@container", "@scope" }.Any(p =>
                            header.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
                    start = n + 1;
                }
                else if (item.Text is ";" or "}")
                {
                    if (frames.TryPeek(out var declaration) && declaration && n > start)
                    {
                        var segment = t.Skip(start).Take(n - start).ToArray();
                        var colon = Array.FindIndex(segment, x => x.Text == ":");
                        if (!segment[0].Text.StartsWith('@'))
                        {
                            if (colon <= 0)
                            {
                                Add(segment[0], "CSS declarations require a property name followed by ':'.", "PROP001");
                            }
                            else if (colon == segment.Length - 1)
                            {
                                Add(segment[colon], "Expected a CSS value after ':'.", "VALUE001");
                            }
                            else if (!string.Concat(segment.Take(colon).Select(x => x.Text))
                                         .StartsWith("--", StringComparison.Ordinal))
                            {
                                var depth = 0;
                                for (var p = colon + 1; p < segment.Length; p++)
                                {
                                    if (segment[p].Kind == CodeTokenKind.String) continue;
                                    if (segment[p].Text is "(" or "[")
                                    {
                                        depth++;
                                    }
                                    else if (segment[p].Text is ")" or "]")
                                    {
                                        depth--;
                                    }
                                    else if (depth == 0 && segment[p].Text == ":")
                                    {
                                        Add(segment[p], "Expected ';' before the next CSS declaration.", "SEMI001");
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (item.Text == "}" && frames.Count > 0) frames.Pop();
                    start = n + 1;
                }
            }
        }
    }

    internal static string Prefix(CodeType type)
    {
        return type switch
        {
            CodeType.CSharp => "CS", CodeType.JavaScript => "JS", CodeType.TypeScript => "TS", CodeType.Cpp => "CPP",
            CodeType.PowerShell => "PS", CodeType.Python => "PY", _ => type.ToString().ToUpperInvariant()
        };
    }
}