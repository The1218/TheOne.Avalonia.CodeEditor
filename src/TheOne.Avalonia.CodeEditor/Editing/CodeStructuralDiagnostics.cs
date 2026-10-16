namespace TheOne.Avalonia.Controls;

internal static class CodeStructuralDiagnostics
{
    internal static IEnumerable<(int Start, int Length, string Message, string Code)> Analyze(CodeAnalysis a,
        CancellationToken cancellation)
    {
        foreach (var issue in a.DeclarationIssues)
        {
            cancellation.ThrowIfCancellationRequested();
            yield return (issue.Start, issue.Length, issue.Message, issue.Code);
        }

        var tokens = a.Tokens;
        for (var n = 0; n < tokens.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            var t = tokens[n];
            if (t.Kind == CodeTokenKind.String) continue;
            if (a.Language == CodeType.CSharp && CodeCompletionRules.Modifier(t.Text) && t.Text != "new" &&
                (n == 0 || a.At(n - 1) is ";" or "{" or "}" || tokens[n - 1].Line < t.Line))
            {
                var next = n;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var repeated = false;
                while (next < tokens.Count && CodeCompletionRules.Modifier(a.At(next)))
                {
                    if (!seen.Add(a.At(next)))
                    {
                        repeated = true;
                        break;
                    }

                    next++;
                }

                if (repeated || next == tokens.Count || a.At(next) is ";" or "}")
                    yield return (t.Start, t.Text.Length,
                        $"Incomplete declaration after '{t.Text}': expected a type and member name, or a class/struct/interface declaration.",
                        "CSDECL001");
                else if (tokens[next].Identifier && a.At(next + 1) is ";" or "}")
                    yield return (tokens[next].Start, tokens[next].Text.Length,
                        $"Expected a member name after type '{a.At(next)}'. For example: {t.Text} {a.At(next)} Name {{ get; set; }}",
                        "CSDECL002");
                n = Math.Max(n, next - 1);
                continue;
            }

            if (a.Language == CodeType.CSharp && t.Text == "(" && a.Pairs.TryGetValue(n, out var close) &&
                ((n > 0 && tokens[n - 1].Identifier) || a.At(n - 1) is ")" or "]"))
            {
                var previous = "(";
                for (var i = n + 1; i < close; i++)
                {
                    if (a.Pairs.TryGetValue(i, out var end) && end > i)
                    {
                        previous = a.At(end);
                        i = end;
                        continue;
                    }

                    if (a.At(i) == "," && (previous is "(" or "," || i + 1 == close))
                        yield return (tokens[i].Start, 1,
                            $"Missing argument in '{a.At(n - 1)}(...)': expected an expression {(i + 1 == close ? "after" : "before")} ','. Remove the comma or supply the argument.",
                            "CSCALL001");
                    previous = a.At(i);
                }
            }

            if (a.Language is CodeType.JavaScript or CodeType.TypeScript && t.Text is "const" or "let" or "var" &&
                a.At(n + 1) is "=" or ";")
                yield return (t.Start, t.Text.Length, $"Expected a variable name after '{t.Text}'.", "JSDECL001");
        }

        // Line groups retain token positions and ignore comments/literals; inspect only
        // definite statement boundaries, allowing multiline expressions and for headers.
        if (a.Language == CodeType.CSharp)
        {
            var parens = 0;
            var squares = 0;
            for (var g = 0; g < tokens.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                var start = g;
                var end = g + 1;
                while (end < tokens.Count && tokens[end].Line == tokens[start].Line) end++;
                g = end;
                var startDepth = parens + squares;
                for (var n = start; n < end; n++)
                {
                    var t = tokens[n];
                    if (t.Kind == CodeTokenKind.String) continue;
                    if (t.Text == "(") parens++;
                    else if (t.Text == ")") parens--;
                    else if (t.Text == "[") squares++;
                    else if (t.Text == "]") squares--;
                }

                if (startDepth != 0 || parens != 0 || squares != 0) continue;
                var last = tokens[end - 1];
                var first = tokens[start];
                var next = end < tokens.Count ? tokens[end] : null;
                var statement = first.Text is "return" or "throw" or "break" or "continue" ||
                                (end - start >= 4 && first.Identifier && tokens[start + 1].Identifier &&
                                 tokens[start + 2].Text == "=");
                if (statement && (last.Identifier || last.Kind is CodeTokenKind.String or CodeTokenKind.Number ||
                                  last.Text is ")" or "]") &&
                    (next == null || next.Text is "}" or "return" || (next.Line > first.Line && next.Identifier &&
                                                                      a.Document.Lines[next.Line].Text.TrimStart()
                                                                          .StartsWith("var ",
                                                                              StringComparison.Ordinal))))
                    yield return (last.Start, last.Text.Length, "Expected ';' at the end of this statement.",
                        "CSSTMT001");
            }
        }
    }
}