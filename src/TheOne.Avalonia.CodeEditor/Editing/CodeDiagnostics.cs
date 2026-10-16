using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TheOne.Avalonia.Controls;

internal static class CodeDiagnostics
{
    internal static IReadOnlyList<CodeDiagnostic> Analyze(CodeAnalysis analysis, CancellationToken token)
    {
        var document = analysis.Document;
        var language = analysis.Language;
        var errors = new List<CodeDiagnostic>();

        void Add(int start, int length, string message, string code,
            CodeDiagnosticSeverity severity = CodeDiagnosticSeverity.Error)
        {
            if (errors.Count >= 200) return;
            start = Math.Clamp(start, 0, document.Text.Length);
            var line = document.LineAt(start);
            errors.Add(new CodeDiagnostic(start,
                Math.Min(Math.Max(1, length), Math.Max(1, document.Text.Length - start)), message, code, severity,
                line + 1, start - document.Lines[line].Start + 1));
        }

        int Position(int line, int column)
        {
            return document.Lines[Math.Clamp(line, 0, document.Lines.Count - 1)].Start + Math.Max(0, column);
        }

        if (language == CodeType.Json)
        {
            try
            {
                using var json = JsonDocument.Parse(document.Text);
            }
            catch (JsonException ex)
            {
                var line = document.Lines[Math.Clamp((int)(ex.LineNumber ?? 0), 0, document.Lines.Count - 1)];
                var bytes = Encoding.UTF8.GetBytes(line.Text);
                var count = Math.Clamp((int)(ex.BytePositionInLine ?? 0), 0, bytes.Length);
                Add(line.Start + Encoding.UTF8.GetCharCount(bytes.AsSpan(0, count)), 1, ex.Message, "JSON001");
            }

            return errors;
        }

        if (language is CodeType.Xml or CodeType.Axaml)
        {
            try
            {
                XDocument.Parse(document.Text);
            }
            catch (XmlException ex)
            {
                Add(Position(ex.LineNumber - 1, ex.LinePosition - 1), 1, ex.Message, "XML001");
            }

            return errors;
        }

        if (language == CodeType.Html)
        {
            var parsed = CodeHtml.Parse(document.Text, token);
            foreach (var error in parsed.Errors)
                Add(error.Start, error.Length, error.Message, "HTML001");
            foreach (var issue in CodeDataDiagnostics.Html(analysis, parsed, token))
                Add(issue.Start, issue.Length, issue.Message, issue.Code, issue.Severity);
            return errors;
        }

        if (language == CodeType.PlainText) return errors;
        if (language is CodeType.Markdown or CodeType.Ini or CodeType.Toml)
        {
            foreach (var issue in CodeDataDiagnostics.Analyze(analysis, token))
                Add(issue.Start, issue.Length, issue.Message, issue.Code, issue.Severity);
            return errors.OrderBy(d => d.Start).ToArray();
        }

        if (language == CodeType.Yaml)
        {
            Yaml();
            return errors.OrderBy(d => d.Start).ToArray();
        }

        var brackets = new Stack<CodeAnalysis.Token>();
        var shellCases = 0;
        for (var index = 0; index < analysis.Tokens.Count; index++)
        {
            var item = analysis.Tokens[index];
            token.ThrowIfCancellationRequested();
            if (item.Kind == CodeTokenKind.String) continue;
            if (language == CodeType.Bash)
            {
                if (item.Text == "case") shellCases++;
                if (item.Text == "esac") shellCases = Math.Max(0, shellCases - 1);
                if (item.Text == ")" && shellCases > 0 &&
                    (!brackets.TryPeek(out var pattern) || pattern.Text != "(")) continue;
                var prior = analysis.Tokens.ElementAtOrDefault(index - 1);
                if (item.Text == "{" && !(prior == null || prior.Line < item.Line ||
                                          prior.Text is ")" or ";" or "then" or "do" or "else" ||
                                          (prior.Text == "$" && prior.End == item.Start))) continue;
                if (item.Text == "}" && (!brackets.TryPeek(out var brace) || brace.Text != "{") && prior != null &&
                    prior.Line == item.Line && prior.Text != ";") continue;
            }

            if (item.Text is "(" or "[" or "{")
            {
                brackets.Push(item);
            }
            else if (item.Text is ")" or "]" or "}")
            {
                if (brackets.TryPeek(out var open) && CodeAnalysis.Matches(open.Text, item.Text)) brackets.Pop();
                else
                    Add(item.Start, 1,
                        brackets.TryPeek(out var expected)
                            ? $"Unexpected '{item.Text}'; expected '{(expected.Text == "(" ? ")" : expected.Text == "[" ? "]" : "}")}' to close '{expected.Text}' from line {expected.Line + 1}."
                            : $"Unexpected '{item.Text}' without an opening delimiter.", "SYN001");
            }
        }

        foreach (var item in brackets.Take(30))
            Add(item.Start, 1,
                $"Unclosed '{item.Text}'; expected '{(item.Text == "(" ? ")" : item.Text == "[" ? "]" : "}")}' before the end of the document.",
                "SYN002");
        if (document.Lines[^1].EndState.End is { } terminator)
        {
            var first = document.Lines.Count - 1;
            while (first > 0 && document.Lines[first].StartState.End != null) first--;
            var line = document.Lines[first];
            var t = line.Tokens.LastOrDefault(t => t.Kind is CodeTokenKind.String or CodeTokenKind.Comment);
            Add(line.Start + t.Start, Math.Max(1, t.Length), $"Unclosed literal or comment; expected {terminator}.",
                "SYN003");
        }

        foreach (var line in document.Lines.Where(l => l.StartState.End == null && l.EndState.End == null))
        foreach (var literal in line.Tokens.Where(t => t.Kind == CodeTokenKind.String && t.Length > 0))
        {
            var value = line.Text.Substring(literal.Start, literal.Length);
            if (value[0] is '\'' or '"' && (value.Length == 1 || value[^1] != value[0]))
                Add(line.Start + literal.Start, literal.Length, "Unterminated string literal.", "SYN005");
        }

        if (language == CodeType.Python) Python();
        // Definite missing expression, without guessing types from external libraries.
        if (language != CodeType.Css && (CodeLanguage.Get(language).Braces || language == CodeType.Python))
            for (var n = 0; n < analysis.Tokens.Count - 1; n++)
                if (analysis.At(n) == "=" && analysis.At(n - 1) is not ("=" or "!" or "<" or ">") &&
                    analysis.At(n + 1) is ";" or "}")
                    Add(analysis.Tokens[n].Start, 1, "Expected an expression after '='.", "SYN004");
        var phase = CodeWorkProbe.Start("diagnostics-calls", token);
        if (language is CodeType.CSharp or CodeType.TypeScript or CodeType.Python or CodeType.C or CodeType.Cpp
            or CodeType.Java or CodeType.Go or CodeType.Rust or CodeType.Kotlin or CodeType.Swift or CodeType.Php
            or CodeType.Dart or CodeType.Ruby)
            foreach (var error in analysis.ArgumentErrors(token))
                Add(error.Start, error.Length, error.Message, "CALL001", error.Severity);
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics-structure", token);
        foreach (var error in CodeStructuralDiagnostics.Analyze(analysis, token))
            Add(error.Start, error.Length, error.Message, error.Code);
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics-members", token);
        foreach (var error in analysis.MemberErrors(token)) Add(error.Start, error.Length, error.Message, "MEMBER001");
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics-language", token);
        foreach (var issue in CodeLanguageDiagnostics.Analyze(analysis, token))
            Add(issue.Start, issue.Length, issue.Message, issue.Code, issue.Severity);
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics-declarations", token);
        foreach (var issue in CodeSemanticDiagnostics.Analyze(analysis, token))
            Add(issue.Start, issue.Length, issue.Message, issue.Code, issue.Severity);
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics-references", token);
        foreach (var issue in analysis.ReferenceErrors(token))
        {
            if (issue.Code.EndsWith("TYPE003", StringComparison.Ordinal) && errors.Any(d =>
                    d.Start == issue.Start && d.Code.EndsWith("TYPE002", StringComparison.Ordinal))) continue;
            Add(issue.Start, issue.Length, issue.Message, issue.Code, issue.Severity);
        }

        phase.Dispose();
        return errors.DistinctBy(d => (d.Start, d.Code)).OrderBy(d => d.Start).ThenBy(d => d.Code).ToArray();

        void Python()
        {
            var levels = new List<int> { 0 };
            var suite = false;
            var depth = 0;
            var continuation = false;
            var pendingHeader = false;
            foreach (var line in document.Lines)
            {
                token.ThrowIfCancellationRequested();
                var text = line.Text;
                var value = text.Trim();
                if (line.StartState.End != null || value.Length == 0 || value.StartsWith('#')) continue;
                var mask = CodeStructuredFormatting.Protected(line);
                var code = new StringBuilder();
                for (var n = 0; n < text.Length; n++)
                    if (!mask[n])
                        code.Append(text[n]);
                var content = code.ToString().Trim();
                var indent = CodeAnalysis.Indent(text);
                if (depth == 0 && !continuation)
                {
                    if (suite && indent <= levels[^1])
                        Add(line.Start, Math.Max(1, text.Length), "Expected an indented block.", "PY001");
                    if (!suite && indent > levels[^1])
                        Add(line.Start, Math.Max(1, indent), "Unexpected indentation.", "PY002");
                    while (levels.Count > 1 && indent < levels[^1]) levels.RemoveAt(levels.Count - 1);
                    if (indent > levels[^1])
                    {
                        if (suite) levels.Add(indent);
                        else if (levels.Count > 1)
                            Add(line.Start, Math.Max(1, indent), "Dedent does not match an outer indentation level.",
                                "PY003");
                    }
                }

                var topColon = false;
                foreach (var ch in content)
                    if (ch is '(' or '[' or '{') depth++;
                    else if (ch is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
                    else if (ch == ':' && depth == 0) topColon = true;
                var first =
                    value.Split([' ', '\t', '(', ':'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                var header = pendingHeader ||
                             first is "def" or "class" or "if" or "elif" or "else" or "for" or "while" or "try"
                                 or "except" or "finally" or "with" or "match" or "case" ||
                             value.StartsWith("async def ", StringComparison.Ordinal) ||
                             value.StartsWith("async for ", StringComparison.Ordinal) ||
                             value.StartsWith("async with ", StringComparison.Ordinal);
                if (header && depth == 0 && !topColon && !value.EndsWith('\\'))
                    Add(line.Start + text.TrimEnd().Length, 1, "Expected ':' after the block header.", "PY004");
                suite = header && depth == 0 && content.EndsWith(':');
                continuation = value.EndsWith('\\');
                pendingHeader = header && (depth > 0 || continuation);
                var lastCode = line.Tokens.LastOrDefault(t => t.Kind != CodeTokenKind.Comment);
                if (lastCode.Length == 1 && text[lastCode.Start] == '=' &&
                    !content.EndsWith("==", StringComparison.Ordinal))
                    Add(line.Start + lastCode.Start, 1, "Expected an expression after '='.", "PY005");
            }

            if (suite) Add(document.Text.Length, 1, "Expected an indented block.", "PY001");
        }

        void Yaml()
        {
            var scopes = new List<(int Indent, HashSet<string> Keys)>
                { (0, new HashSet<string>(StringComparer.Ordinal)) };
            int? scalarIndent = null;
            var flow = new Stack<(char Value, int Start)>();
            var quote = '\0';
            var quoteStart = 0;
            foreach (var line in document.Lines)
            {
                token.ThrowIfCancellationRequested();
                var text = line.Text;
                var value = text.Trim();
                var indent = CodeAnalysis.Indent(text);
                if (scalarIndent.HasValue && (value.Length == 0 || indent > scalarIndent.Value)) continue;
                scalarIndent = null;
                if (value.Length == 0 || value.StartsWith('#')) continue;
                if (text.TakeWhile(char.IsWhiteSpace).Contains('\t'))
                    Add(line.Start, Math.Max(1, indent), "YAML indentation must use spaces.", "YAML001");
                if (value is "---" or "...")
                {
                    scopes.Clear();
                    scopes.Add((0, new HashSet<string>(StringComparer.Ordinal)));
                    continue;
                }

                if (indent < scopes[^1].Indent && !scopes.Any(s => s.Indent == indent))
                    Add(line.Start, Math.Max(1, indent),
                        "Dedent must match an existing YAML mapping indentation level.", "YAML005");
                while (scopes.Count > 1 && indent < scopes[^1].Indent) scopes.RemoveAt(scopes.Count - 1);
                if (indent > scopes[^1].Indent) scopes.Add((indent, new HashSet<string>(StringComparer.Ordinal)));
                if (value.StartsWith("- ", StringComparison.Ordinal))
                {
                    scopes[^1].Keys.Clear();
                    value = value[2..];
                }

                var colon = MappingColon(value);
                if (colon > 0)
                {
                    var key = value[..colon].Trim().Trim('\'', '"');
                    if (!scopes[^1].Keys.Add(key))
                        Add(line.Start + indent, colon, $"Duplicate mapping key '{key}'.", "YAML002");
                }

                if (CodeStructuredFormatting.YamlBlockHeader(value)) scalarIndent = indent;
                var content = colon >= 0 ? value[(colon + 1)..].TrimStart() : value;
                var contentStart = text.IndexOf(content, StringComparison.Ordinal);
                if (flow.Count > 0 || quote != '\0' || content.StartsWith('[') || content.StartsWith('{') ||
                    content.StartsWith('"') || content.StartsWith('\''))
                    for (var n = Math.Max(0, contentStart); n < text.Length; n++)
                    {
                        var ch = text[n];
                        if (quote != '\0')
                        {
                            if (ch == '\\' && quote == '"')
                            {
                                n++;
                            }
                            else if (ch == quote)
                            {
                                if (quote == '\'' && n + 1 < text.Length && text[n + 1] == '\'') n++;
                                else quote = '\0';
                            }

                            continue;
                        }

                        if (ch == '#' && (n == 0 || char.IsWhiteSpace(text[n - 1]))) break;
                        if (ch is '\'' or '"' &&
                            (n == 0 || char.IsWhiteSpace(text[n - 1]) || "[:,{".Contains(text[n - 1])))
                        {
                            quote = ch;
                            quoteStart = line.Start + n;
                        }
                        else if (ch is '[' or '{')
                        {
                            flow.Push((ch, line.Start + n));
                        }
                        else if (ch is ']' or '}')
                        {
                            if (flow.TryPeek(out var open) &&
                                CodeAnalysis.Matches(open.Value.ToString(), ch.ToString())) flow.Pop();
                            else Add(line.Start + n, 1, $"Unexpected '{ch}' in flow collection.", "YAML003");
                        }
                    }
            }

            foreach (var open in flow) Add(open.Start, 1, "Unclosed YAML flow collection.", "YAML003");
            if (quote != '\0') Add(quoteStart, 1, "Unclosed YAML quoted scalar.", "YAML004");
        }
    }

    private static int MappingColon(string text)
    {
        var quote = '\0';
        for (var n = 0; n < text.Length; n++)
        {
            var ch = text[n];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"') n++;
                else if (ch == quote) quote = '\0';
                continue;
            }

            if (ch is '\'' or '"') quote = ch;
            else if (ch == ':' && (n + 1 == text.Length || char.IsWhiteSpace(text[n + 1]))) return n;
            else if (ch == '#') break;
        }

        return -1;
    }
}