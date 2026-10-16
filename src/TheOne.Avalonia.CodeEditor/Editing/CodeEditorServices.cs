using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TheOne.Avalonia.Controls;

/// <summary>A completion insertion. CaretOffset is relative to InsertText; -1 places it at the end.</summary>
public sealed record CodeCompletionItem(string Label, string InsertText, string? Detail = null, int CaretOffset = -1)
{
    public CodeCompletionKind Kind { get; init; }
    internal CodeSnippetDefinition? Snippet { get; init; }

    public override string ToString()
    {
        return Label;
    }
}

public sealed record CodeCompletionContext(string Text, int CaretIndex, CodeType CodeType, string Prefix);

public interface ICodeCompletionProvider
{
    Task<IReadOnlyList<CodeCompletionItem>> GetCompletionsAsync(CodeCompletionContext context,
        CancellationToken cancellationToken);
}

public sealed record CodeFormattingContext(
    string Text,
    CodeType CodeType,
    int TabSize,
    bool ConvertTabsToSpaces,
    string NewLine);

public interface ICodeFormatter
{
    Task<string> FormatAsync(CodeFormattingContext context, CancellationToken cancellationToken);
}

internal static class CodeCompletions
{
    internal static IReadOnlyList<CodeCompletionItem> Get(CodeDocument document, CodeType type, string prefix)
    {
        var items = new Dictionary<string, CodeCompletionItem>(StringComparer.OrdinalIgnoreCase);

        void Add(CodeCompletionItem item)
        {
            if (item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) items.TryAdd(item.Label, item);
        }

        foreach (var word in CodeLanguage.Get(type).KeywordSet)
            Add(new CodeCompletionItem(word, word, "Keyword") { Kind = CodeCompletionKind.Keyword });
        var source =
            type is CodeType.PlainText or CodeType.Markdown or CodeType.Json or CodeType.Yaml or CodeType.Toml
                or CodeType.Ini or CodeType.Xml or CodeType.Axaml or CodeType.Html
                ? document.Words
                : document.CodeWords;
        var words = prefix.Length == 0 ? source : source.GetViewBetween(prefix, prefix + '\uffff');
        foreach (var word in words)
        {
            if (!word.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                Add(new CodeCompletionItem(word, word, "Document") { Kind = CodeCompletionKind.Variable });
            if (items.Count >= 100) break;
        }

        var snippet = type switch
        {
            CodeType.Python => new CodeCompletionItem("def…", "def function_name():\n    pass", "Function snippet", 4),
            CodeType.Rust => new CodeCompletionItem("fn…", "fn function_name() {\n    \n}", "Function snippet", 3),
            CodeType.Go => new CodeCompletionItem("func…", "func functionName() {\n    \n}", "Function snippet", 5),
            CodeType.JavaScript or CodeType.TypeScript => new CodeCompletionItem("function…",
                "function name() {\n    \n}", "Function snippet", 9),
            CodeType.Sql => new CodeCompletionItem("select…", "SELECT *\nFROM table_name\nWHERE condition;",
                "Query snippet", 14),
            CodeType.Xml or CodeType.Axaml or CodeType.Html => new CodeCompletionItem("element…",
                "<element>\n    \n</element>", "Element snippet", 1),
            _ when CodeLanguage.Get(type).Braces => new CodeCompletionItem("if…", "if ()\n{\n    \n}",
                "Conditional snippet", 4),
            _ => null
        };
        if (snippet != null) Add(snippet with { Kind = CodeCompletionKind.Snippet });
        if (prefix.Length == 0)
            foreach (var pair in new[] { ("()", 1), ("[]", 1), ("{}", 1), ("\"\"", 1), ("''", 1) })
                Add(new CodeCompletionItem(pair.Item1, pair.Item1, "Paired symbol", pair.Item2)
                    { Kind = CodeCompletionKind.Snippet });
        return items.Values.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).Take(60).ToArray();
    }
}

internal static class CodeFormatting
{
    internal static string Format(CodeFormattingContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.CodeType is CodeType.Python or CodeType.Yaml or CodeType.Html)
            return CodeStructuredFormatting.Format(context, cancellationToken);
        if (context.CodeType == CodeType.Sql) return FormatSql(context, cancellationToken);
        if (context.CodeType == CodeType.Json)
        {
            using var json = JsonDocument.Parse(context.Text);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream,
                       new JsonWriterOptions { Indented = true, IndentSize = context.TabSize }))
            {
                json.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray()).Replace("\n", context.NewLine, StringComparison.Ordinal);
        }

        if (context.CodeType is CodeType.Xml or CodeType.Axaml)
        {
            var document = XDocument.Parse(context.Text, LoadOptions.PreserveWhitespace);
            // Mixed content and xml:space are data. Never normalize their whitespace.
            foreach (var element in document.Descendants())
                if (element.Attribute(XNamespace.Xml + "space")?.Value != "preserve" && !element.Ancestors()
                        .Any(a => a.Attribute(XNamespace.Xml + "space")?.Value == "preserve") &&
                    !element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                    foreach (var whitespace in element.Nodes().OfType<XText>().ToArray())
                        whitespace.Remove();
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings
                   {
                       Indent = true,
                       IndentChars = context.ConvertTabsToSpaces ? new string(' ', context.TabSize) : "\t",
                       NewLineChars = context.NewLine, OmitXmlDeclaration = document.Declaration == null
                   }))
            {
                document.Save(writer);
            }

            // XmlWriter over StringBuilder reports UTF-16; preserve the source declaration.
            var result = output.ToString();
            if (document.Declaration is { } declaration && result.StartsWith("<?xml", StringComparison.Ordinal))
                result = declaration + result[(result.IndexOf("?>", StringComparison.Ordinal) + 2)..];
            return result;
        }

        var language = CodeLanguage.Get(context.CodeType);
        if (!language.Braces) return FormatConservative(context, cancellationToken);
        var source = context.Text;
        // Retain existing newlines and literal payloads. New structural breaks
        // never separate return/throw from their expression; ambiguous JS slashes
        // fall back to indentation only.
        if (context.CodeType is CodeType.CSharp or CodeType.C or CodeType.Cpp or CodeType.Java or CodeType.Css
            or CodeType.Dart or CodeType.JavaScript or CodeType.TypeScript)
            source = ExpandBlocks(context, cancellationToken);
        var lines = new CodeDocument();
        lines.Reset(source);
        CodeSyntax.Update(lines, context.CodeType, force: true);
        var depth = 0;
        var resultBuilder = new StringBuilder(source.Length + 64);
        var unit = context.ConvertTabsToSpaces ? new string(' ', context.TabSize) : "\t";
        foreach (var line in lines.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.StartState.End != null)
            {
                resultBuilder.Append(line.Text).Append(line.Delimiter);
                continue;
            }

            var content = line.EndState.End == null ? line.Text.Trim() : line.Text.TrimStart();
            content = NormalizeSpacing(content, context.CodeType);
            var prefixClose = content.Length > 0 && content[0] is '}' or ']' ? 1 : 0;
            if (content.Length > 0)
            {
                for (var i = 0; i < Math.Max(0, depth - prefixClose); i++) resultBuilder.Append(unit);
                resultBuilder.Append(content);
            }

            foreach (var token in line.Tokens)
            {
                if (token.Kind is CodeTokenKind.String or CodeTokenKind.Comment or CodeTokenKind.Type) continue;
                foreach (var c in line.Text.AsSpan(token.Start, token.Length))
                    if (c is '{' or '[') depth++;
                    else if (c is '}' or ']') depth = Math.Max(0, depth - 1);
            }

            resultBuilder.Append(line.Delimiter);
        }

        return resultBuilder.ToString();
    }

    private static string FormatConservative(CodeFormattingContext context, CancellationToken cancellationToken)
    {
        if (context.CodeType is CodeType.PlainText or CodeType.Markdown or CodeType.Yaml or CodeType.Html)
            return context.Text;
        var document = new CodeDocument();
        document.Reset(context.Text);
        CodeSyntax.Update(document, context.CodeType, force: true);
        var result = new StringBuilder(context.Text.Length);
        foreach (var line in document.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Significant indentation is retained (Python, shell, Ruby, YAML).
            var protectedTail = line.EndState.End != null || line.Text.EndsWith('\\');
            var value = protectedTail ? line.Text : line.Text.TrimEnd();
            if (line.StartState.End == null &&
                context.CodeType is CodeType.Python or CodeType.Ruby or CodeType.Lua or CodeType.Toml or CodeType.Ini)
                value = NormalizeSpacing(value, context.CodeType);
            result.Append(value);
            result.Append(line.Delimiter);
        }

        return result.ToString();
    }

    internal static string NormalizeSpacing(string text, CodeType type)
    {
        var state = default(CodeLexState);
        var tokens = CodeSyntax.Scan(text, type, ref state);
        var protectedPositions = new bool[text.Length];
        foreach (var token in tokens.Where(t => t.Kind is CodeTokenKind.String or CodeTokenKind.Comment))
            Array.Fill(protectedPositions, true, token.Start, token.Length);
        var result = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            if (protectedPositions[i])
            {
                result.Append(text[i]);
                continue;
            }

            var c = text[i];
            var assignment = c == '=' && (i == 0 || !"=!<>+-*/%&|^?:".Contains(text[i - 1])) &&
                             (i + 1 == text.Length || text[i + 1] is not ('=' or '>'));
            if (assignment)
            {
                while (result.Length > 0 && result[^1] is ' ' or '\t') result.Length--;
                if (result.Length > 0) result.Append(' ');
                result.Append("= ");
                while (i + 1 < text.Length && text[i + 1] is ' ' or '\t') i++;
            }
            else
            {
                result.Append(c);
                if (c == ',')
                {
                    while (i + 1 < text.Length && text[i + 1] is ' ' or '\t') i++;
                    if (i + 1 < text.Length && text[i + 1] is not (')' or ']' or '}')) result.Append(' ');
                }
            }
        }

        return result.ToString();
    }

    private static string FormatSql(CodeFormattingContext context, CancellationToken cancellationToken)
    {
        var document = new CodeDocument();
        document.Reset(context.Text);
        CodeSyntax.Update(document, CodeType.Sql, force: true);
        var result = new StringBuilder(context.Text.Length + 32);
        foreach (var line in document.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = 0;
            foreach (var token in line.Tokens)
            {
                var word = line.Text.Substring(token.Start, token.Length);
                var upper = token.Kind == CodeTokenKind.Keyword ? word.ToUpperInvariant() : word;
                var clause = token.Kind == CodeTokenKind.Keyword && upper is "SELECT" or "FROM" or "WHERE" or "JOIN"
                    or "GROUP" or "ORDER" or "HAVING" or "LIMIT" or "UNION" or "VALUES";
                var gap = line.Text[position..token.Start];
                var lastLine = result.Length - 1;
                while (lastLine >= 0 && result[lastLine] is not ('\n' or '\r') &&
                       char.IsWhiteSpace(result[lastLine])) lastLine--;
                if (clause && lastLine >= 0 && result[lastLine] is not ('\n' or '\r'))
                {
                    while (result.Length > 0 && result[^1] is ' ' or '\t') result.Length--;
                    result.Append(context.NewLine);
                }
                else
                {
                    result.Append(gap);
                }

                result.Append(upper);
                position = token.Start + token.Length;
            }

            result.Append(line.Text.AsSpan(position));
            result.Append(line.Delimiter);
        }

        return result.ToString();
    }

    private static string ExpandBlocks(CodeFormattingContext context, CancellationToken cancellationToken)
    {
        var document = new CodeDocument();
        document.Reset(context.Text);
        CodeSyntax.Update(document, context.CodeType, force: true);
        var result = new StringBuilder(context.Text.Length + 64);
        var parentheses = 0;
        foreach (var line in document.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.StartState.End != null || line.Text.TrimStart().StartsWith('#') || line.Text.EndsWith('\\'))
            {
                result.Append(line.Text).Append(line.Delimiter);
                continue;
            }

            var protectedPositions = new bool[line.Text.Length];
            foreach (var token in line.Tokens.Where(t => t.Kind is CodeTokenKind.String or CodeTokenKind.Comment))
                Array.Fill(protectedPositions, true, token.Start, token.Length);
            if (context.CodeType is CodeType.JavaScript or CodeType.TypeScript && Enumerable.Range(0, line.Text.Length)
                    .Any(i => line.Text[i] == '/' && !protectedPositions[i]))
            {
                result.Append(line.Text).Append(line.Delimiter);
                continue;
            }

            for (var i = 0; i < line.Text.Length; i++)
            {
                var c = line.Text[i];
                if (protectedPositions[i])
                {
                    result.Append(c);
                    continue;
                }

                if (c == '(') parentheses++;
                if (c == ')') parentheses = Math.Max(0, parentheses - 1);
                if (c == '}')
                {
                    var hasContent = false;
                    for (var cursor = result.Length - 1; cursor >= 0 && result[cursor] is not ('\n' or '\r'); cursor--)
                        if (!char.IsWhiteSpace(result[cursor]))
                        {
                            hasContent = true;
                            break;
                        }

                    if (hasContent) result.Append(context.NewLine);
                }

                result.Append(c);
                var next = i + 1;
                while (next < line.Text.Length && char.IsWhiteSpace(line.Text[next])) next++;
                if ((c == '{' || (c == ';' && parentheses == 0)) && next < line.Text.Length)
                    result.Append(context.NewLine);
                if (c == '}' && next < line.Text.Length && line.Text[next] is not (';' or ',' or ')' or ']'))
                    result.Append(context.NewLine);
            }

            result.Append(line.Delimiter);
        }

        return result.ToString();
    }
}