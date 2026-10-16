using System.Globalization;
using System.Text;

namespace TheOne.Avalonia.Controls;

internal static class CodeDataDiagnostics
{
    internal static IEnumerable<CodeIssue> Html(CodeAnalysis analysis, CodeHtml.Result parsed, CancellationToken token)
    {
        var queue = new Queue<CodeHtml.Node>();
        queue.Enqueue(parsed.Root);
        while (queue.TryDequeue(out var node))
        {
            token.ThrowIfCancellationRequested();
            foreach (var child in node.Children) queue.Enqueue(child);
            if (!node.Raw || node.Name is not ("script" or "style")) continue;
            var close = analysis.Document.Text.IndexOf("</" + node.Name, node.OpenEnd,
                StringComparison.OrdinalIgnoreCase);
            if (close < node.OpenEnd) continue;
            var type = node.Name == "style" ? CodeType.Css : CodeType.JavaScript;
            if (node.Name == "script" && node.Open.Contains("json", StringComparison.OrdinalIgnoreCase))
                type = CodeType.Json;
            else if (node.Name == "script" && node.Open.Contains("type=", StringComparison.OrdinalIgnoreCase) &&
                     !node.Open.Contains("javascript", StringComparison.OrdinalIgnoreCase) &&
                     !node.Open.Contains("module", StringComparison.OrdinalIgnoreCase) &&
                     !node.Open.Contains("ecmascript", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var issue in CodeAnalysis.Create(analysis.Document.Text[node.OpenEnd..close], type, token)
                         .Diagnostics)
                yield return new CodeIssue(node.OpenEnd + issue.Start, issue.Length, issue.Message,
                    "HTML." + issue.Code, issue.Severity);
        }
    }

    internal static IReadOnlyList<CodeIssue> Analyze(CodeAnalysis a, CancellationToken cancellation)
    {
        var errors = new List<CodeIssue>();
        var document = a.Document;
        var toml = a.Language == CodeType.Toml;

        void Add(int start, int length, string message, string code,
            CodeDiagnosticSeverity severity = CodeDiagnosticSeverity.Error)
        {
            if (errors.Count < 200) errors.Add(new CodeIssue(start, Math.Max(1, length), message, code, severity));
        }

        if (a.Language == CodeType.Markdown)
        {
            Markdown();
            return errors;
        }

        var prefix = toml ? "TOML" : "INI";
        var section = "";
        var keys = new HashSet<string>(toml ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, int>(StringComparer.Ordinal);
        var flow = new Stack<(char Open, int Start)>();
        foreach (var line in document.Lines)
        {
            cancellation.ThrowIfCancellationRequested();
            if (toml && line.StartState.End != null) continue;
            var value = (toml ? Uncomment(line.Text) : line.Text).Trim();
            var indent = line.Text.Length - line.Text.TrimStart().Length;
            if (value.Length == 0 || value[0] == '#' || (!toml && value[0] == ';')) continue;
            if (flow.Count == 0 && value.StartsWith('['))
            {
                var array = toml && value.StartsWith("[[", StringComparison.Ordinal);
                var closer = array ? "]]" : "]";
                var end = value.LastIndexOf(closer, StringComparison.Ordinal);
                if (end < (array ? 2 : 1))
                {
                    Add(line.Start + indent, value.Length,
                        $"Invalid section header; expected '{closer}' after a non-empty name.", prefix + "SECTION001");
                    continue;
                }

                var suffix = value[(end + closer.Length)..].Trim();
                if (suffix.Length > 0 && !(suffix.StartsWith(';') || suffix.StartsWith('#')))
                    Add(line.Start + indent + end + closer.Length, suffix.Length,
                        "Unexpected text after the section header.", prefix + "SECTION002");
                var name = value[(array ? 2 : 1)..end].Trim();
                if (name.Length == 0 || (toml && !ValidKey(name)))
                    Add(line.Start + indent, value.Length, "Invalid section name.", prefix + "KEY001");
                section = NormalizeKey(name);
                // Child tables belong to the most recently declared array element.
                foreach (var parent in arrays.OrderBy(p => p.Key.Length))
                    if (section.StartsWith(parent.Key + "\u001f", StringComparison.Ordinal))
                        section = parent.Key + "#" + parent.Value + section[parent.Key.Length..];
                if (toml && keys.Contains(section))
                    Add(line.Start + indent, value.Length, "A value cannot be redefined as a table.", "TOMLTABLE001");
                if (toml && (array ? tables.Contains(section) : arrays.ContainsKey(section)))
                    Add(line.Start + indent, value.Length, "A table and an array of tables cannot redefine each other.",
                        "TOMLTABLE003");
                if (array)
                {
                    var index = arrays.GetValueOrDefault(section) + 1;
                    arrays[section] = index;
                    section += "#" + index;
                }
                else if (toml && !tables.Add(section))
                {
                    Add(line.Start + indent, value.Length, "This table has already been declared.", "TOMLTABLE002");
                }

                continue;
            }

            var equal = EqualsAt(value);
            var content = value;
            if (flow.Count == 0)
            {
                if (equal <= 0)
                {
                    Add(line.Start + indent, value.Length, "Expected a key followed by '=' and a value.",
                        prefix + "ASSIGN001");
                    continue;
                }

                var key = value[..equal].Trim();
                var identity = (section.Length == 0 ? "" : section + "\u001f") + NormalizeKey(key);
                if (toml && !ValidKey(key))
                    Add(line.Start + indent, key.Length,
                        "TOML keys must be quoted or consist of ASCII letters, digits, '_' and '-', separated by dots.",
                        "TOMLKEY001");
                if (!keys.Add(identity))
                    Add(line.Start + indent, key.Length, $"Duplicate key '{key}' in this section.", prefix + "KEY002",
                        toml ? CodeDiagnosticSeverity.Error : CodeDiagnosticSeverity.Warning);
                content = value[(equal + 1)..].Trim();
                if (toml && content.Length == 0)
                    Add(line.Start + indent + equal, 1, "Expected a TOML value after '='.", "TOMLVALUE001");
                else if (toml && content.Length > 0 && content[0] is not ('\'' or '"' or '[' or '{') &&
                         !Scalar(content))
                    Add(line.Start + line.Text.IndexOf(content, StringComparison.Ordinal), content.Length,
                        "Invalid TOML bare value; use a boolean, number, date/time, array/table, or a quoted string.",
                        "TOMLVALUE002");
            }

            if (!toml) continue;
            foreach (var literal in line.Tokens.Where(t => t.Kind == CodeTokenKind.String))
            {
                var text = line.Text.Substring(literal.Start, literal.Length);
                if (line.EndState.End == null && text.Length > 0 && text[0] is '\'' or '"' &&
                    (text.Length < 2 || text[^1] != text[0]))
                    Add(line.Start + literal.Start, literal.Length, "Unterminated TOML string.", "TOMLSTR001");
            }

            var mask = CodeStructuredFormatting.Protected(line);
            var from = flow.Count == 0 ? Math.Max(0, line.Text.IndexOf('=') + 1) : 0;
            for (var n = from; n < line.Text.Length; n++)
            {
                if (mask[n]) continue;
                var ch = line.Text[n];
                if (ch is '[' or '{')
                {
                    flow.Push((ch, line.Start + n));
                }
                else if (ch is ']' or '}')
                {
                    if (flow.TryPeek(out var open) && CodeAnalysis.Matches(open.Open.ToString(), ch.ToString()))
                        flow.Pop();
                    else Add(line.Start + n, 1, $"Unexpected '{ch}' in TOML value.", "TOMLSYM001");
                }
            }
        }

        foreach (var open in flow) Add(open.Start, 1, $"Unclosed '{open.Open}' in TOML value.", "TOMLSYM002");
        if (toml && document.Lines[^1].EndState.End is { } terminator)
            Add(document.Text.Length, 1, $"Unterminated TOML multiline string; expected {terminator}.", "TOMLSTR002");
        return errors;

        void Markdown()
        {
            for (var row = 0; row < document.Lines.Count; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                var line = document.Lines[row];
                var text = line.Text.TrimStart();
                var indent = line.Text.Length - text.Length;
                if (indent > 3 || text.Length < 3 || text[0] is not ('`' or '~')) continue;
                var marker = text[0];
                var count = text.TakeWhile(c => c == marker).Count();
                if (count < 3) continue;
                var info = text[count..].Trim().Split([' ', '\t', '{'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "";
                var type = FenceLanguage(info);
                var body = new StringBuilder();
                var origins = new List<int>();
                for (row++; row < document.Lines.Count; row++)
                {
                    var current = document.Lines[row];
                    var trimmed = current.Text.TrimStart();
                    if (trimmed.TakeWhile(c => c == marker).Count() >= count &&
                        trimmed.TrimEnd(marker).Trim().Length == 0) break;
                    var remove = Math.Min(indent, current.Text.TakeWhile(c => c == ' ').Count());
                    origins.Add(current.Start + remove);
                    body.Append(current.Text[remove..]).Append(current.Delimiter);
                }

                if (type is CodeType.PlainText or CodeType.Markdown || origins.Count == 0) continue;
                foreach (var issue in CodeAnalysis.Create(body.ToString(), type, cancellation).Diagnostics)
                {
                    var source = origins[Math.Clamp(issue.Line - 1, 0, origins.Count - 1)] + issue.Column - 1;
                    Add(Math.Min(source, document.Text.Length), issue.Length, issue.Message, "MD." + issue.Code,
                        issue.Severity);
                }
            }
        }
    }

    private static CodeType FenceLanguage(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "cs" or "c#" or "csharp" => CodeType.CSharp, "js" or "javascript" => CodeType.JavaScript,
            "ts" or "typescript" => CodeType.TypeScript,
            "py" or "python" => CodeType.Python, "c++" or "cpp" => CodeType.Cpp,
            "sh" or "shell" or "bash" => CodeType.Bash,
            "ps1" or "pwsh" or "powershell" => CodeType.PowerShell, "yml" or "yaml" => CodeType.Yaml,
            _ => Enum.TryParse<CodeType>(name, true, out var type) && Enum.IsDefined(type) ? type : CodeType.PlainText
        };
    }

    private static int EqualsAt(string value)
    {
        var quote = '\0';
        for (var n = 0; n < value.Length; n++)
        {
            var ch = value[n];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"') n++;
                else if (ch == quote) quote = '\0';
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '=')
            {
                return n;
            }
        }

        return -1;
    }

    private static string Uncomment(string value)
    {
        var quote = '\0';
        for (var n = 0; n < value.Length; n++)
        {
            var ch = value[n];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"') n++;
                else if (ch == quote) quote = '\0';
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '#')
            {
                return value[..n];
            }
        }

        return value;
    }

    private static bool ValidKey(string key)
    {
        var n = 0;
        while (n < key.Length)
        {
            while (n < key.Length && char.IsWhiteSpace(key[n])) n++;
            if (n == key.Length) return false;
            if (key[n] is '\'' or '"')
            {
                var quote = key[n++];
                var closed = false;
                for (; n < key.Length; n++)
                    if (key[n] == '\\' && quote == '"')
                    {
                        n++;
                    }
                    else if (key[n] == quote)
                    {
                        n++;
                        closed = true;
                        break;
                    }

                if (!closed) return false;
            }
            else
            {
                var first = n;
                while (n < key.Length && (char.IsAsciiLetterOrDigit(key[n]) || key[n] is '_' or '-')) n++;
                if (n == first) return false;
            }

            while (n < key.Length && char.IsWhiteSpace(key[n])) n++;
            if (n == key.Length) return true;
            if (key[n++] != '.') return false;
        }

        return false;
    }

    private static string NormalizeKey(string key)
    {
        var result = new StringBuilder();
        var quote = '\0';
        foreach (var ch in key.Trim())
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else result.Append(ch);
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '.')
            {
                result.Append('\u001f');
            }
            else if (!char.IsWhiteSpace(ch))
            {
                result.Append(ch);
            }

        return result.ToString();
    }

    private static bool Scalar(string value)
    {
        if (value is "true" or "false" or "inf" or "+inf" or "-inf" or "nan" or "+nan" or "-nan") return true;
        if (value.StartsWith('_') || value.EndsWith('_') || value.Contains("__", StringComparison.Ordinal))
            return false;
        var number = value.Replace("_", "", StringComparison.Ordinal);
        if (number.StartsWith("0x", StringComparison.Ordinal))
            return number.Length > 2 && number[2..].All(char.IsAsciiHexDigit);
        if (number.StartsWith("0o", StringComparison.Ordinal))
            return number.Length > 2 && number[2..].All(c => c is >= '0' and <= '7');
        if (number.StartsWith("0b", StringComparison.Ordinal))
            return number.Length > 2 && number[2..].All(c => c is '0' or '1');
        var unsigned = number.TrimStart('+', '-');
        if (unsigned.Length > 1 && unsigned[0] == '0' && char.IsDigit(unsigned[1]) &&
            unsigned.All(c => char.IsAsciiDigit(c) || ".eE+-".Contains(c))) return false;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
               DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
               DateTimeOffset.TryParseExact(value.Replace('t', 'T').Replace('z', 'Z'),
               [
                   "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ssK",
                   "yyyy-MM-dd HH:mm:ss.FFFFFFFK"
               ], CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
               TimeOnly.TryParseExact(value, ["HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out _);
    }
}