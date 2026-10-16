namespace TheOne.Avalonia.Controls;

// Conservative checks on explicit declarations. Unknown/dynamic types are not guessed.
internal static class CodeSemanticDiagnostics
{
    internal static IReadOnlyList<CodeIssue> Analyze(CodeAnalysis a, CancellationToken cancellation)
    {
        var result = new List<CodeIssue>();
        var t = a.Tokens;
        var language = a.Language;
        var prefix = CodeLanguageDiagnostics.Prefix(language);
        var scopes = new Stack<Dictionary<string, int>>();
        scopes.Push(new Dictionary<string, int>(StringComparer.Ordinal));
        var parens = 0;

        void Add(CodeAnalysis.Token item, string message, string code,
            CodeDiagnosticSeverity severity = CodeDiagnosticSeverity.Error)
        {
            if (result.Count < 200)
                result.Add(new CodeIssue(item.Start, item.Text.Length, message, prefix + code, severity));
        }

        for (var n = 0; n < t.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            var item = t[n];
            if (item.Kind is CodeTokenKind.String or CodeTokenKind.Comment) continue;
            var word = item.Text;
            var prior = a.At(n - 1);
            var next = a.At(n + 1);
            if (word == "{") scopes.Push(new Dictionary<string, int>(StringComparer.Ordinal));
            else if (word == "}" && scopes.Count > 1) scopes.Pop();
            if (word == "(") parens++;
            else if (word == ")") parens = Math.Max(0, parens - 1);
            var binding = language switch
            {
                CodeType.CSharp => word == "var" || Primitive(word) != 0,
                CodeType.C or CodeType.Cpp or CodeType.Java or CodeType.Dart => Primitive(word) != 0,
                CodeType.JavaScript or CodeType.TypeScript => word is "let" or "const",
                CodeType.Kotlin => word is "val" or "var", CodeType.Swift => word is "let" or "var",
                CodeType.Go => word == "var", _ => false
            };
            if (binding && !(language == CodeType.C && scopes.Count == 1) && parens == 0 &&
                prior is not ("." or ":" or "new" or "extern") &&
                t.ElementAtOrDefault(n + 1) is { Identifier: true } name && a.At(n + 2) is "=" or ";" or ":")
            {
                if (scopes.Peek().TryGetValue(name.Text, out var line))
                    Add(name, $"'{name.Text}' is already declared in this scope on line {line + 1}.", "BIND001");
                else scopes.Peek()[name.Text] = name.Line;
            }

            var header = language switch
            {
                CodeType.CSharp => word is "class" or "struct" or "interface" or "enum" or "record" or "namespace",
                CodeType.C or CodeType.Cpp => word is "class" or "struct" or "union" or "enum" or "namespace",
                CodeType.Java or CodeType.TypeScript => word is "class" or "interface" or "enum",
                CodeType.JavaScript or CodeType.Php or CodeType.Dart => word == "class",
                CodeType.Swift => word is "class" or "struct" or "enum" or "protocol" or "extension",
                CodeType.Rust => word is "struct" or "enum" or "trait" or "impl" or "mod",
                CodeType.Go => word is "struct" or "interface",
                _ => false
            };
            if (language == CodeType.Swift && word == "class" && next is "func" or "var" or "subscript") header = false;
            if (header && prior is not ("." or "@" or ":" or "," or "<") && next != ":" &&
                !(word is "record" or "enum" && next is "class" or "struct"))
            {
                var body = false;
                var bounded = n + 1 == t.Count;
                var semicolon = language is CodeType.CSharp or CodeType.C or CodeType.Cpp ||
                                (language == CodeType.Rust && word is "struct" or "mod");
                for (var p = n + 1; p < Math.Min(t.Count, n + 256); p++)
                {
                    var value = a.At(p);
                    if (value == "{")
                    {
                        body = true;
                        break;
                    }

                    if (value == ";")
                    {
                        body = semicolon;
                        bounded = true;
                        break;
                    }

                    if (value == "}" || (p > n + 1 && a.At(p - 1) is not (":" or ",") && value is "public" or "private"
                            or "protected" or "class" or "struct" or "namespace" or "return"))
                    {
                        bounded = true;
                        break;
                    }

                    if (value is "(" or "[" && a.Pairs.TryGetValue(p, out var close) && close > p) p = close;
                    if (p == t.Count - 1) bounded = true;
                }

                if (!body && bounded)
                    Add(item,
                        $"Incomplete '{word}' declaration: expected a declaration body '{{ ... }}'" +
                        (semicolon ? " or a valid ';' declaration." : "."), "BODY001");
            }

            if (language is CodeType.JavaScript or CodeType.TypeScript && word == "const" && next != "enum" &&
                prior is not ("declare" or ".") && t.ElementAtOrDefault(n + 1)?.Identifier == true &&
                a.At(n + 2) != ":")
            {
                var after = a.At(n + 2);
                if (after is ";" or "}" or "" || (n + 2 < t.Count && t[n + 2].Line > item.Line && after != "="))
                    Add(t[n + 1], "A const binding requires an initializer ('const name = value').", "CONST001");
            }

            var type = "";
            var literal = -1;
            if (language is CodeType.CSharp or CodeType.C or CodeType.Cpp or CodeType.Java or CodeType.Dart &&
                t.ElementAtOrDefault(n + 1)?.Identifier == true && a.At(n + 2) == "=")
            {
                type = word;
                literal = n + 3;
            }
            else if (language is CodeType.TypeScript or CodeType.Kotlin or CodeType.Swift or CodeType.Rust
                         or CodeType.Python && word == ":" && a.At(n + 2) == "=")
            {
                type = next;
                literal = n + 3;
            }
            else if (language == CodeType.Go && word == "var" && a.At(n + 3) == "=")
            {
                type = a.At(n + 2);
                literal = n + 4;
            }

            var category = Primitive(type);
            if (literal < 0 || literal >= t.Count || category == 0) continue;
            var valueToken = t[literal];
            var end = a.At(literal + 1);
            var single = end is ";" or "}" or "" || (literal + 1 < t.Count && t[literal + 1].Line > valueToken.Line);
            if (!single) continue;
            var actual = valueToken.Kind == CodeTokenKind.String
                ?
                valueToken.Text.StartsWith('\'') && language is CodeType.CSharp or CodeType.C or CodeType.Cpp
                    or CodeType.Java or CodeType.Kotlin or CodeType.Rust
                    ? 4
                    : 1
                : valueToken.Kind == CodeTokenKind.Number
                    ? 2
                    : valueToken.Text is "true" or "false" or "True" or "False"
                        ? 3
                        : 0;
            var conversion =
                (language is CodeType.C or CodeType.Cpp && ((category is 2 or 3 or 4 && actual is 2 or 3 or 4) ||
                                                            (category == 3 && actual == 1))) ||
                (category == 2 && actual == 4 && language is CodeType.CSharp or CodeType.Java) ||
                (language == CodeType.Python && category == 2 && actual == 3);
            if (actual != 0 && actual != category && !conversion)
                Add(valueToken, $"Literal {valueToken.Text} is incompatible with the declared type '{type}'.",
                    "TYPE002",
                    language == CodeType.Python ? CodeDiagnosticSeverity.Warning : CodeDiagnosticSeverity.Error);
        }

        if (language is CodeType.CSharp or CodeType.TypeScript or CodeType.Python or CodeType.Java or CodeType.C
            or CodeType.Cpp or CodeType.Go or CodeType.Rust or CodeType.Kotlin or CodeType.Php or CodeType.Ruby
            or CodeType.Dart)
            foreach (var symbol in a.Symbols.Where(s => s.Signature != null))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var group in symbol.Signature!.Parameters.GroupBy(p => p.Name.TrimStart('*', '.'))
                             .Where(g => g.Key.Length > 0 && g.Count() > 1))
                    if (t.FirstOrDefault(p => p.Start == symbol.Start) is { } at)
                        Add(at,
                            $"Duplicate parameter '{group.Key}' in '{symbol.Name}'. Parameter names must be unique.",
                            "PARAM001");
            }

        return result;
    }

    private static int Primitive(string type)
    {
        return type switch
        {
            "string" or "String" or "str" => 1,
            "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "byte" or "sbyte" or "float" or "double"
                or "decimal" or "number" or "Int" or "Float" or "Double" or "i8" or "i16" or "i32" or "i64" or "u8"
                or "u16" or "u32" or "u64" or "usize" or "isize" or "f32" or "f64" => 2,
            "bool" or "boolean" or "Boolean" or "Bool" => 3, "char" or "Char" => 4, _ => 0
        };
    }
}