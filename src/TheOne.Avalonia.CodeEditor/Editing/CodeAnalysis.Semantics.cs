using System.Collections.Concurrent;

namespace TheOne.Avalonia.Controls;

// Single-file facts only. Scopes are indexed once per immutable snapshot; expression
// inference is bounded and never loads assemblies, projects or external schemas.
internal sealed partial class CodeAnalysis
{
    [ThreadStatic] private static int _inferenceDepth;
    private readonly HashSet<int> _anonymousBodies = [];
    private readonly ConcurrentDictionary<int, string> _bindingTypes = new();
    private readonly HashSet<int> _declarations = [];
    private readonly HashSet<(int Method, int Body)> _declaredFunctionBodies = [];
    private readonly List<(Symbol Method, int First, int End)> _expressionBodies = [];
    private readonly Dictionary<int, int> _functionScopes = [];
    private readonly HashSet<int> _headers = [];
    private readonly HashSet<int> _hoisted = [];
    private readonly HashSet<string> _imports = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (int First, int End)> _initializers = [];
    private readonly List<LexicalScope> _lexicalScopes = [];
    private readonly HashSet<int> _objectScopes = [];
    private readonly HashSet<string> _openOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _specialBindings = [];
    private readonly HashSet<int> _typeTokens = [];
    private int[] _tokenScopes = [];

    private bool HasSemanticScopes => SupportsSemanticScopes(Language);

    internal static bool SupportsSemanticScopes(CodeType language)
    {
        return language is CodeType.CSharp or CodeType.JavaScript or CodeType.TypeScript
            or CodeType.Python or CodeType.C or CodeType.Cpp or CodeType.Java or CodeType.Go or CodeType.Rust
            or CodeType.Kotlin or CodeType.Swift or CodeType.Php or CodeType.Ruby or CodeType.Dart or CodeType.Lua;
    }

    private void IndexSemanticScopes(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!HasSemanticScopes) return;
        var ranges = new List<(int Start, int End, int Function)>();
        if (Language == CodeType.CSharp)
            for (var n = 0; n < Tokens.Count; n++)
                if (At(n) is "for" or "foreach" or "catch" && At(n + 1) == "(" &&
                    Pairs.TryGetValue(n + 1, out var close))
                {
                    var end = At(close + 1) == "{" && Pairs.TryGetValue(close + 1, out var body) ? Tokens[body].Start :
                        ExpressionEnd(close + 1) is var finish && finish < Tokens.Count ? Tokens[finish].End :
                        Document.Text.Length;
                    ranges.Add((Tokens[n].Start, end, -1));
                    if (At(n) == "catch" && close > n + 3 && Tokens[close - 1].Identifier)
                    {
                        _specialBindings[close - 1] = At(n + 2);
                        for (var p = n + 2; p < close - 1; p++) _typeTokens.Add(p);
                    }
                }

        if (Language is not (CodeType.Python or CodeType.Ruby or CodeType.Lua))
            foreach (var (open, close) in Pairs)
                if (At(open) == "{" && close > open)
                {
                    ranges.Add((Tokens[open].End, Tokens[close].Start, -1));
                    if ((At(open - 1) == ">" && At(open - 2) == "=") || At(open - 1) == "delegate" ||
                        (At(open - 1) == ")" && Pairs.TryGetValue(open - 1, out var parameters) &&
                         At(parameters - 1) is "delegate" or "function"))
                        _anonymousBodies.Add(Tokens[open].End);
                    if (At(open - 1) == "new" || At(open - 2) == "new" || At(open - 1) == "=" ||
                        (At(open - 1) == ")" && Pairs.TryGetValue(open - 1, out var call) && At(call - 2) == "new"))
                        _objectScopes.Add(Tokens[open].End);
                }

        foreach (var (name, range) in Types)
        {
            var first = Before(range.Start + 1);
            if (At(first - 1) == "partial") _openOwners.Add(name);
            for (var n = first; n < Math.Min(Tokens.Count, first + 64) && At(n) != "{"; n++)
                if (At(n) is ":" or "extends" or "implements")
                {
                    _openOwners.Add(name);
                    break;
                }
        }

        foreach (var method in _methods)
        {
            ranges.Add((method.Start, method.ScopeEnd, method.Start));
            var start = Before(method.Start + 1);
            var open = start + 1;
            if (At(open) == "=" && At(open + 1) == "(") open++;
            if (At(open) == "<" && AngleEnd(open) is var generic && generic > open) open = generic + 1;
            if (At(open) != "(" || !Pairs.TryGetValue(open, out var close)) continue;
            var end = close + 1;
            while (Language is not (CodeType.Lua or CodeType.Ruby) && end < Math.Min(Tokens.Count, close + 32) &&
                   At(end) is not ("{" or ";" or "=") &&
                   Tokens[end].Line == Tokens[close].Line)
            {
                if (Language == CodeType.Python && At(end) == ":")
                {
                    end++;
                    break;
                }

                end++;
            }

            for (var n = start; n < end; n++) _headers.Add(n);
            var body = At(end) == "=" && At(end + 1) == ">" ? end + 2 : end;
            if (At(body) == "{") _declaredFunctionBodies.Add((method.Start, Tokens[body].End));
            if (At(end) == "=")
            {
                var first = At(end + 1) == ">" ? end + 2 : Language == CodeType.Kotlin ? end + 1 : -1;
                if (first >= 0 && At(first) != "{") _expressionBodies.Add((method, first, ExpressionEnd(first)));
            }
        }

        _lexicalScopes.Add(new LexicalScope(0, Document.Text.Length + 1, -1));
        var stack = new Stack<int>();
        stack.Push(0);
        foreach (var range in ranges.OrderBy(r => r.Start).ThenByDescending(r => r.End))
        {
            while (stack.Count > 1 && range.Start >= _lexicalScopes[stack.Peek()].End) stack.Pop();
            var id = _lexicalScopes.Count;
            _lexicalScopes.Add(new LexicalScope(range.Start, range.End, stack.Peek()));
            if (range.Function >= 0) _functionScopes[range.Function] = id;
            stack.Push(id);
        }

        _tokenScopes = new int[Tokens.Count];
        var scopeIndex = 1;
        stack.Clear();
        stack.Push(0);
        for (var n = 0; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            while (scopeIndex < _lexicalScopes.Count && _lexicalScopes[scopeIndex].Start <= Tokens[n].Start)
            {
                while (stack.Count > 1 && _lexicalScopes[stack.Peek()].End <= _lexicalScopes[scopeIndex].Start)
                    stack.Pop();
                stack.Push(scopeIndex++);
            }

            while (stack.Count > 1 && _lexicalScopes[stack.Peek()].End <= Tokens[n].Start) stack.Pop();
            _tokenScopes[n] = stack.Peek();
            if (At(n) is "import" or "using" or "from" or "use" or "package" or "extern")
                for (var p = n + 1; p < Tokens.Count && Tokens[p].Line == Tokens[n].Line && At(p) != ";"; p++)
                    if (Tokens[p].Identifier) _imports.Add(At(p));
                    else if (Language == CodeType.Go && Tokens[p].Kind == CodeTokenKind.String)
                        _imports.Add(At(p).Trim('"', '`').Split('/')[^1]);
        }

        var methods = _methods.GroupBy(s => s.Start).ToDictionary(g => g.Key, g => g.First());
        var normalized = new List<Symbol>();
        foreach (var symbol in Symbols)
        {
            if (symbol.Signature != null)
            {
                normalized.Add(symbol);
                _declarations.Add(symbol.Start);
                continue;
            }

            if (methods.TryGetValue(symbol.Start, out var method) && symbol.Name != method.Name)
            {
                normalized.Add(symbol);
                continue; // Parameters live throughout their function.
            }

            var n = Before(symbol.Start + 1);
            if (n < 0 || _headers.Contains(n)) continue;
            if (!VariableDeclaration(n) && !(symbol.Owner != null &&
                                             (At(n) != symbol.Name || symbol.Owner.StartsWith('@') ||
                                              (Language == CodeType.Go &&
                                               Tokens.ElementAtOrDefault(n + 1)?.Identifier == true)))) continue;
            var scope = _lexicalScopes[_tokenScopes[n]];
            if (Language is CodeType.JavaScript or CodeType.TypeScript && At(n - 1) == "var")
            {
                var methodScope = MethodAt(symbol.Start);
                scope = methodScope != null && _functionScopes.TryGetValue(methodScope.Start, out var function)
                    ? _lexicalScopes[function]
                    : _lexicalScopes[0];
                _hoisted.Add(symbol.Start);
            }

            var type = symbol.Type;
            if (At(n + 1) == ":" && At(n + 2) != "=")
            {
                var end = n + 2;
                while (end < Math.Min(Tokens.Count, n + 64) && Tokens[end].Line == Tokens[n].Line &&
                       At(end) is not ("=" or ";" or "," or "}")) end++;
                type = string.Concat(Tokens.Skip(n + 2).Take(end - n - 2).Select(t => t.Text));
            }

            normalized.Add(symbol with { ScopeStart = scope.Start, ScopeEnd = scope.End, Type = type });
            _declarations.Add(symbol.Start);
        }

        var slots = normalized.Select(s => (s.Name, s.Start)).ToHashSet();
        for (var n = 0; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            if (_headers.Contains(n) || !Tokens[n].Identifier || !VariableDeclaration(n)) continue;
            _declarations.Add(Tokens[n].Start);
            var scope = _lexicalScopes[_tokenScopes[n]];
            if (slots.Add((At(n), Tokens[n].Start)))
            {
                var type = _specialBindings.GetValueOrDefault(n) ?? (At(n + 1) == ":" ? At(n + 2) :
                    Language == CodeType.Go && At(n - 1) == "var" ? At(n + 1) : "");
                normalized.Add(new Symbol(At(n), type,
                    MethodAt(Tokens[n].Start) == null ? Owner(Tokens[n].Start) : null,
                    Tokens[n].Start, scope.Start, scope.End));
            }

            var eq = n + 1;
            if (At(eq) == ":" && At(eq + 1) == "=") eq++;
            else if (At(eq) == ":") eq += 2;
            else if (Language == CodeType.Go && At(n - 1) is "var" or "const" &&
                     Tokens.ElementAtOrDefault(eq)?.Identifier == true) eq++;
            var inferred = At(n - 1) is "var" or "let" or "const" or "val" or "mut" or "auto" or "final" or "local" ||
                           Language is CodeType.Python or CodeType.Php or CodeType.Ruby or CodeType.Lua
                               or CodeType.JavaScript ||
                           (Language == CodeType.Go && At(n + 1) == ":");
            if (At(eq) == "=" && At(eq + 1) != "=" && inferred && !(At(n + 1) == ":" && At(n + 2) != "=") &&
                !(Language == CodeType.Go && Tokens.ElementAtOrDefault(n + 1)?.Identifier == true))
                _initializers[Tokens[n].Start] = (eq + 1, ExpressionEnd(eq + 1));
        }

        Symbols.Clear();
        Symbols.AddRange(normalized);
    }

    private bool VariableDeclaration(int n)
    {
        if (!Tokens[n].Identifier || MemberReceiver(n - 1, out _) || At(n + 1) == "(" ||
            CodeLanguage.Get(Language).KeywordSet.Contains(At(n))) return false;
        var prior = At(n - 1);
        var next = At(n + 1);
        if (_specialBindings.ContainsKey(n) || (Language == CodeType.CSharp &&
                                                ((prior == "var" && At(n - 2) == "out") || At(n - 2) == "is")))
            return true;
        if (prior is "let" or "val" or "var" or "const" or "final" or "local" || (prior == "mut" && At(n - 2) == "let"))
            return next is "=" or ":" or ";" or "in" or "of" ||
                   (Language == CodeType.Go && Tokens.ElementAtOrDefault(n + 1)?.Identifier == true);
        if (Language is CodeType.Python or CodeType.Php or CodeType.Ruby or CodeType.Lua)
            return ((next == "=" && At(n + 2) != "=") || (next == ":" && Language == CodeType.Python)) &&
                   (n == 0 || Tokens[n - 1].Line < Tokens[n].Line || prior is ";" or "{" or "}");
        if (Language == CodeType.Go) return next == ":" && At(n + 2) == "=";
        if (Language is CodeType.TypeScript or CodeType.JavaScript or CodeType.Rust or CodeType.Kotlin
            or CodeType.Swift) return false;
        return next is "=" or ";" or "{" && n > 0 &&
               ((Tokens[n - 1].Identifier && prior is not ("return" or "throw" or "yield" or "new" or "else")) ||
                prior is ">" or "]" or "?");
    }

    private bool Visible(Symbol symbol, int caret)
    {
        if (!HasSemanticScopes || _tokenScopes.Length == 0)
            return symbol.Signature != null || (symbol.ScopeStart <= caret && caret <= symbol.ScopeEnd);
        if (symbol.Owner != null) return symbol.Owner == Owner(caret);
        if (symbol.Signature != null)
        {
            var n = Before(symbol.Start + 1);
            var id = n >= 0 ? _tokenScopes[n] : 0;
            if (_functionScopes.TryGetValue(symbol.Start, out var function)) id = _lexicalScopes[function].Parent;
            return InScope(id, caret);
        }

        return symbol.ScopeStart <= caret && caret < symbol.ScopeEnd &&
               (symbol.Start <= caret || _hoisted.Contains(symbol.Start) || (MethodAt(symbol.Start) == null &&
                                                                             (Owner(symbol.Start) != null ||
                                                                              (Language == CodeType.Python &&
                                                                               MethodAt(caret) != null))));
    }

    private bool InScope(int id, int caret)
    {
        return _lexicalScopes[id].Start <= caret && caret < _lexicalScopes[id].End;
    }

    private Symbol? Binding(string name, int caret)
    {
        if (!_byName.TryGetValue(name, out var candidates)) return null;
        Symbol? nearest = null;
        foreach (var symbol in candidates)
            if (symbol.Signature == null && Visible(symbol, caret) && (nearest == null ||
                                                                       symbol.ScopeEnd - symbol.ScopeStart <
                                                                       nearest.ScopeEnd - nearest.ScopeStart ||
                                                                       (symbol.ScopeEnd - symbol.ScopeStart ==
                                                                        nearest.ScopeEnd - nearest.ScopeStart &&
                                                                        symbol.Start > nearest.Start)))
                nearest = symbol;
        return nearest;
    }

    private bool HasVisibleName(string name, int caret)
    {
        if (_byName.TryGetValue(name, out var candidates))
            foreach (var symbol in candidates)
                if (Visible(symbol, caret))
                    return true;
        return false;
    }

    private string BindingType(Symbol symbol, int depth = 0)
    {
        if (depth > 8 || _inferenceDepth >= 8) return "";
        if (!_initializers.TryGetValue(symbol.Start, out var expression)) return symbol.Type;
        if (_bindingTypes.TryGetValue(symbol.Start, out var cached)) return cached;
        _inferenceDepth++;
        try
        {
            var type = ExpressionType(expression.First, expression.End, depth + 1);
            _bindingTypes[symbol.Start] = type;
            return type;
        }
        finally
        {
            _inferenceDepth--;
        }
    }

    private int ExpressionEnd(int first)
    {
        if (first >= Tokens.Count) return first;
        for (var n = first; n < Math.Min(Tokens.Count, first + 128); n++)
        {
            if (At(n) is ";" or "}" or "," || (n > first && Tokens[n].Line > Tokens[n - 1].Line)) return n;
            if (Pairs.TryGetValue(n, out var close) && close > n) n = close;
        }

        return Math.Min(Tokens.Count, first + 128);
    }

    private string ExpressionType(int first, int end, int depth = 0)
    {
        if (depth > 8 || first >= end || end - first > 128) return "";
        if (At(first) == "(" && Pairs.GetValueOrDefault(first, -1) == end - 1)
            return ExpressionType(first + 1, end - 1, depth + 1);
        if (At(first) is "+" or "-") return ExpressionType(first + 1, end, depth + 1);
        if (first + 1 == end)
        {
            if (Tokens[first].Kind == CodeTokenKind.String) return Infer(first, Tokens[first].Start);
            if (Tokens[first].Kind == CodeTokenKind.Number) return "number";
            if (At(first) is "true" or "false" or "True" or "False") return "bool";
            return Binding(At(first), Tokens[first].Start - 1) is { } binding ? BindingType(binding, depth + 1) : "";
        }

        if (At(first) == "new") return Infer(first, Tokens[first].Start);
        if (At(first) == "[" && Pairs.GetValueOrDefault(first, -1) == end - 1) return "list";
        if (At(first) == "{" && Pairs.GetValueOrDefault(first, -1) == end - 1) return Infer(first, Tokens[first].Start);
        if (Language == CodeType.Rust && At(first) == "vec" && At(first + 1) == "!") return "Vec";
        if (Types.ContainsKey(At(first)) && At(first + 1) is "(" or "{") return At(first);
        // Only built-in primitive arithmetic is definite without a compiler. Split
        // at low precedence first; paired subexpressions remain single operands.
        for (var precedence = 0; precedence < 2; precedence++)
        for (var n = first; n < end - 1; n++)
        {
            if (Pairs.TryGetValue(n, out var nested) && nested > n)
            {
                n = nested;
                continue;
            }

            if (n == first) continue;
            if (!(precedence == 0 ? At(n) is "+" or "-" : At(n) is "*" or "/" or "%")) continue;
            var left = ExpressionType(first, n, depth + 1);
            var right = ExpressionType(n + 1, end, depth + 1);
            if (PrimitiveCategory(left) == 2 && PrimitiveCategory(right) == 2) return "number";
            if (At(n) == "+" && PrimitiveCategory(left) == 1 && PrimitiveCategory(right) == 1 &&
                Language is CodeType.CSharp or CodeType.JavaScript or CodeType.TypeScript or CodeType.Java
                    or CodeType.Python or CodeType.Kotlin or CodeType.Swift or CodeType.Ruby
                    or CodeType.Dart) return "string";
            return "";
        }

        if (At(end - 1) == ")" && Pairs.TryGetValue(end - 1, out var open) && open > first)
        {
            var signatures = Callables(open - 1, Tokens[first].Start).Select(s => s.ReturnType ?? "").Distinct().Take(2)
                .ToArray();
            return signatures.Length == 1 ? signatures[0] : "";
        }

        // Do not infer a whole expression from its first token (casts, ternaries,
        // operators and conversions may change its type).
        return "";
    }

    private int SelectSignature(CodeSignatureInfo[] signatures, int first, int caret, int active)
    {
        var arguments = new List<(string Type, string? Name)>();
        var segment = first;
        var end = Math.Min(Tokens.Count, first + 128);
        for (var n = first; n <= end; n++)
        {
            var finished = n == end || Tokens[n].Start >= caret || At(n) == ")";
            if (!finished && Pairs.TryGetValue(n, out var nested) && nested > n && Tokens[nested].Start < caret)
            {
                n = nested;
                continue;
            }

            if (!finished && At(n) != ",") continue;
            if (n > segment)
            {
                var named = At(segment + 1) == (Language is CodeType.Python or CodeType.Kotlin ? "=" : ":") &&
                            Language is CodeType.CSharp or CodeType.Python or CodeType.Kotlin or CodeType.Swift
                                or CodeType.Php;
                arguments.Add((ExpressionType(named ? segment + 2 : segment, n), named ? At(segment) : null));
            }

            segment = n + 1;
            if (finished) break;
        }

        var selected = -1;
        var best = int.MaxValue;
        for (var s = 0; s < signatures.Length; s++)
        {
            var parameters = signatures[s].Parameters;
            var variadic = parameters.Count > 0 && VariadicParameter(parameters[^1]);
            var score = parameters.Count > active || variadic ? 0 : 16;
            for (var n = 0; n < arguments.Count; n++)
            {
                var argument = arguments[n];
                var index = n;
                if (argument.Name != null)
                {
                    index = -1;
                    for (var p = 0; p < parameters.Count; p++)
                        if (parameters[p].Name == argument.Name)
                        {
                            index = p;
                            break;
                        }
                }

                if (index >= parameters.Count && variadic) index = parameters.Count - 1;
                if (index < 0 || index >= parameters.Count)
                {
                    score += 8;
                    continue;
                }

                var type = parameters[index].Type;
                if (variadic && index == parameters.Count - 1 &&
                    type?.StartsWith("params ", StringComparison.Ordinal) == true) type = type[7..].TrimEnd('[', ']');
                if (Incompatible(type, argument.Type)) score += 4;
            }

            if (score < best)
            {
                best = score;
                selected = s;
            }
        }

        return selected;
    }

    private static int PrimitiveCategory(string? type)
    {
        return type?.TrimEnd('?') switch
        {
            "string" or "String" or "str" => 1,
            "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "byte" or "sbyte" or "float" or "double"
                or "decimal" or "number" or "Int" or "Float" or "Double" or "i8" or "i16" or "i32" or "i64" or "u8"
                or "u16" or "u32" or "u64" or "usize" or "isize" or "f32" or "f64" => 2,
            "bool" or "boolean" or "Boolean" or "Bool" => 3, "char" or "Char" => 4, _ => 0
        };
    }

    private bool Incompatible(string? expected, string actual)
    {
        var e = PrimitiveCategory(expected);
        var a = PrimitiveCategory(actual);
        if (e == 0 || a == 0 || e == a) return false;
        if (Language is CodeType.C or CodeType.Cpp &&
            ((e is 2 or 3 or 4 && a is 2 or 3 or 4) || (e == 3 && a == 1))) return false;
        if ((e == 2 && a == 4 && Language is CodeType.CSharp or CodeType.Java) ||
            (Language == CodeType.Python && e == 2 && a == 3)) return false;
        return true;
    }

    private string ExpectedCompletionType(int caret, string prefix)
    {
        if (!HasSemanticScopes) return "";
        var n = Before(caret - prefix.Length);
        if (At(n) == "return") return MethodAt(caret)?.Signature?.ReturnType ?? "";
        if (At(n) == "=" && Binding(At(n - 1), caret) is { } binding) return BindingType(binding);
        var help = Signature(caret);
        return help?.Signatures[help.ActiveSignature].Parameters.ElementAtOrDefault(help.ActiveParameter)?.Type ?? "";
    }

    private int CompletionTypeRank(CodeCompletionItem item, Dictionary<string, Symbol>? symbols, string expected)
    {
        if (symbols == null) return 1;
        var symbol = symbols.GetValueOrDefault(item.Label);
        var actual = symbol == null ? "" : symbol.Signature?.ReturnType ?? BindingType(symbol);
        return PrimitiveCategory(actual) == 0 ? 1 : Incompatible(expected, actual) ? 2 : 0;
    }

    internal IReadOnlyList<CodeIssue> ReferenceErrors(CancellationToken cancellation)
    {
        var issues = new List<CodeIssue>();
        if (!HasSemanticScopes) return issues;
        var seen = new HashSet<(int, string)>();
        var reads = new List<(int First, int End)>();
        var globals = CodeBuiltins.Globals(Language).Select(s => s.Name).Concat(_imports)
            .ToHashSet(StringComparer.Ordinal);
        var severity =
            Language is CodeType.Python or CodeType.JavaScript or CodeType.Php or CodeType.Ruby or CodeType.Lua
                ? CodeDiagnosticSeverity.Warning
                : CodeDiagnosticSeverity.Error;

        void Add(int n, string code, string message)
        {
            if (issues.Count < 200 && seen.Add((n, code)))
                issues.Add(new CodeIssue(Tokens[n].Start, Tokens[n].Text.Length,
                    message, CodeLanguageDiagnostics.Prefix(Language) + code, severity));
        }

        void References(int first, int end)
        {
            // Lambda/comprehension bindings need their own grammar. Suppress reads
            // in that expression instead of diagnosing a valid local as undefined.
            for (var n = first; n < Math.Min(end, first + 128); n++)
                if ((At(n) == "=" && At(n + 1) == ">") || (Language == CodeType.Python && At(n) is "for" or "lambda"))
                    return;
            if (first < end) reads.Add((first, end));
        }

        void InspectReferences(int first, int end)
        {
            for (var n = first; n < end && issues.Count < 200; n++)
            {
                if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
                var t = Tokens[n];
                if (!t.Identifier || t.Kind is CodeTokenKind.String or CodeTokenKind.Comment || _headers.Contains(n) ||
                    _typeTokens.Contains(n) ||
                    _declarations.Contains(t.Start) || CodeLanguage.Get(Language).KeywordSet.Contains(t.Text) ||
                    PrimitiveCategory(t.Text) != 0 || globals.Contains(t.Text) || Types.ContainsKey(t.Text) ||
                    t.Text is "self" or "cls" or "this" or "super" or "base" || MemberReceiver(n - 1, out _) ||
                    At(n - 1) is "new" or "typeof" or "as" or "is" || At(n + 1) is "(" or ":" ||
                    t.Text.TrimStart('$') is not { Length: > 0 } bare ||
                    (char.IsUpper(bare[0]) && At(n + 1) is "." or "<")) continue;
                if (HasVisibleName(t.Text, t.Start)) continue;
                if (Owner(t.Start) is { } owner && _openOwners.Contains(owner)) continue;
                if (At(n + 1) == "=" && _objectScopes.Contains(_lexicalScopes[_tokenScopes[n]].Start)) continue;
                // Named arguments and object keys declare a label, not a read.
                if (At(n + 1) == "=" && Language is CodeType.Python or CodeType.Kotlin) continue;
                Add(n, "NAME001", $"No visible declaration for variable '{t.Text}' in this file.");
            }
        }

        for (var n = 0; n < Tokens.Count && issues.Count < 200; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            if (_headers.Contains(n) || Tokens[n].Kind == CodeTokenKind.String) continue;
            if (At(n) == "(" && Pairs.TryGetValue(n, out var callEnd) &&
                Tokens.ElementAtOrDefault(n - 1)?.Identifier == true)
                References(n + 1, callEnd);
            if (At(n) == "=" && At(n - 1) is not ("=" or "!" or "<" or ">" or "-" or "+" or ":") &&
                At(n + 1) is not ("=" or ">"))
            {
                if (Language is CodeType.Python or CodeType.Kotlin && At(n - 2) is "(" or ",") continue;
                var first = n + 1;
                var end = ExpressionEnd(first);
                References(first, end);
                var target = n - 1;
                if (At(target - 1) == ":") target -= 2;
                else if (Language == CodeType.Go && At(target - 2) is "var" or "const") target--;
                if (Tokens.ElementAtOrDefault(target)?.Identifier != true) continue;
                if (_objectScopes.Contains(_lexicalScopes[_tokenScopes[target]].Start)) continue;
                if (CodeLanguage.Get(Language).KeywordSet.Contains(At(target)) ||
                    PrimitiveCategory(At(target)) != 0) continue;
                var binding = Binding(At(target), Tokens[target].End);
                if (binding == null)
                {
                    if (!MemberReceiver(target - 1, out _))
                        Add(target, "NAME001", $"No visible declaration for variable '{At(target)}' in this file.");
                    continue;
                }

                if (Language is CodeType.JavaScript or CodeType.Php or CodeType.Ruby or CodeType.Lua ||
                    (Language == CodeType.Python && At(target + 1) != ":")) continue;
                var actual = ExpressionType(first, end);
                if (Incompatible(BindingType(binding), actual))
                    Add(first, "TYPE003",
                        $"Value of type '{actual}' is incompatible with variable '{binding.Name}' of type '{BindingType(binding)}'.");
            }

            if (At(n) != "return") continue;
            var method = MethodAt(Tokens[n].Start);
            if (method?.Signature == null) continue;
            var anonymous = false;
            for (var scope = _tokenScopes[n]; scope > 0; scope = _lexicalScopes[scope].Parent)
                if (_anonymousBodies.Contains(_lexicalScopes[scope].Start) &&
                    !_declaredFunctionBodies.Contains((method.Start, _lexicalScopes[scope].Start)))
                {
                    anonymous = true;
                    break;
                }

            if (anonymous) continue;
            var expression = n + 1;
            var hasValue = expression < Tokens.Count && At(expression) is not (";" or "}" or "end") &&
                           (Tokens[expression].Line == Tokens[n].Line || Language is CodeType.CSharp or CodeType.C
                               or CodeType.Cpp or CodeType.Java or CodeType.Rust or CodeType.Dart);
            var finish = hasValue ? ExpressionEnd(expression) : expression;
            References(expression, finish);
            var expected = method.Signature.ReturnType;
            if (expected is "void" or "Void" || (Language == CodeType.Python && expected == "None") ||
                (Language == CodeType.CSharp && method.Name == method.Owner))
            {
                if (hasValue) Add(n, "RETURN001", $"'{method.Name}' does not return a value.");
            }
            else if (PrimitiveCategory(expected) != 0)
            {
                if (!hasValue) Add(n, "RETURN001", $"'{method.Name}' must return a value of type '{expected}'.");
                else if (Incompatible(expected, ExpressionType(expression, finish)))
                    Add(expression, "RETURN002",
                        $"Return value is incompatible with '{method.Name}' return type '{expected}'.");
            }
        }

        foreach (var body in _expressionBodies)
        {
            cancellation.ThrowIfCancellationRequested();
            References(body.First, body.End);
            if (body.Method.Signature?.ReturnType == "void" &&
                PrimitiveCategory(ExpressionType(body.First, body.End)) != 0 &&
                At(body.End - 1) != ")" && At(body.First) != "new")
                Add(body.First, "RETURN001",
                    $"'{body.Method.Name}' does not return a value. Its expression body must be a statement expression, such as a call.");
            if (Incompatible(body.Method.Signature?.ReturnType, ExpressionType(body.First, body.End)))
                Add(body.First, "RETURN002",
                    $"Expression body is incompatible with '{body.Method.Name}' return type '{body.Method.Signature?.ReturnType}'.");
        }

        var readEnd = 0;
        foreach (var read in reads.OrderBy(r => r.First))
        {
            InspectReferences(Math.Max(readEnd, read.First), read.End);
            readEnd = Math.Max(readEnd, read.End);
        }

        return issues;
    }

    private sealed record LexicalScope(int Start, int End, int Parent);
}