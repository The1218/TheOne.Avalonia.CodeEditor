using System.Text;

namespace TheOne.Avalonia.Controls;

internal sealed partial class CodeAnalysis
{
    private readonly Dictionary<string, string[]> _bases = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string[]> _methodTypeParameters = [];
    private readonly Dictionary<string, string[]> _typeParameters = new(StringComparer.Ordinal);
    private Dictionary<(string Name, int Start, bool Method), int> _symbolSlots = [];
    internal List<CodeIssue> DeclarationIssues { get; } = [];

    private int AngleEnd(int open)
    {
        var depth = 0;
        for (var n = open; n < Math.Min(Tokens.Count, open + 128); n++)
            if (At(n) == "<") depth++;
            else if (At(n) == ">" && --depth == 0) return n;
            else if (At(n) is ";" or "{" or "}") break;
        return -1;
    }

    private int CallableName(int index)
    {
        if (At(index) != ">") return index;
        var depth = 0;
        for (var n = index; n >= Math.Max(0, index - 128); n--)
            if (At(n) == ">") depth++;
            else if (At(n) == "<" && --depth == 0) return n - 1;
            else if (At(n) is ";" or "{" or "}") break;
        return index;
    }

    private string TypeBefore(int name)
    {
        if (At(name - 1) == ">" && At(name - 2) == "=") return "";
        var start = name - 1;
        var depth = 0;
        for (; start >= 0 && start >= name - 128; start--)
        {
            var t = At(start);
            // Attributes belong to the declaration, not to its return type.
            if (depth == 0 && t == "]" && Pairs.TryGetValue(start, out var attribute) &&
                Tokens.ElementAtOrDefault(attribute + 1)?.Identifier == true) break;
            if (t is ">" or "]")
            {
                depth++;
                continue;
            }

            if (t is "<" or "[")
            {
                if (depth == 0) break;
                depth--;
                continue;
            }

            if (depth > 0) continue;
            if (CodeCompletionRules.Modifier(t) || t is "return" or "var" or "const" or "let" ||
                (t is not ("." or "?" or ":") && !Tokens[start].Identifier)) break;
            if (start > 0 && Tokens[start - 1].Line < Tokens[start].Line && At(start - 1) is not ("." or ">" or "]"))
            {
                start--;
                break;
            }
        }

        var count = name - start - 1;
        return count == 0 ? "" :
            count == 1 ? At(start + 1) : string.Concat(Tokens.Skip(start + 1).Take(count).Select(t => t.Text));
    }

    private int CSharpCallableBody(int close)
    {
        var body = close + 1;
        if (At(body) == ":" && At(body + 1) is "base" or "this" && At(body + 2) == "(" &&
            Pairs.TryGetValue(body + 2, out var initializer)) body = initializer + 1;
        if (At(body) == "where")
            for (var limit = Math.Min(Tokens.Count, body + 128); body < limit; body++)
            {
                if (At(body) is "{" or ";" or "=") break;
                if (Pairs.TryGetValue(body, out var nested) && nested > body) body = nested;
            }

        return body;
    }

    private void IndexExtended(CancellationToken token)
    {
        if (Language == CodeType.CSharp)
        {
            var methods = Symbols.Where(s => s.Signature != null && !CodeCompletionRules.Modifier(s.Type))
                .Select(s => s.Start).ToHashSet();
            Symbols.RemoveAll(s => s.Signature == null && methods.Contains(s.Start));
        }

        for (var n = 0; n < Symbols.Count; n++)
            _symbolSlots[(Symbols[n].Name, Symbols[n].Start, Symbols[n].Signature != null)] = n;
        for (var n = 0; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) token.ThrowIfCancellationRequested();
            if (Language == CodeType.CSharp && At(n) is "class" or "struct" or "interface" or "record" &&
                Tokens.ElementAtOrDefault(n + 1)?.Identifier == true)
            {
                var name = At(n + 1);
                var after = n + 2;
                if (At(after) == "<" && AngleEnd(after) is var end && end > after)
                {
                    _typeParameters[name] = Tokens.Skip(after + 1).Take(end - after - 1).Where(t => t.Identifier)
                        .Select(t => t.Text).ToArray();
                    after = end + 1;
                }

                if (At(after) == ":")
                {
                    var first = after + 1;
                    var bases = new List<string>();
                    for (var p = first; p < Math.Min(Tokens.Count, first + 128); p++)
                    {
                        if (At(p) == "<" && AngleEnd(p) is var genericEnd && genericEnd > p)
                        {
                            p = genericEnd;
                            continue;
                        }

                        if (At(p) is "," or "{" or "where")
                        {
                            if (p > first)
                                bases.Add(string.Concat(Tokens.Skip(first).Take(p - first).Select(t => t.Text)));
                            first = p + 1;
                            if (At(p) != ",") break;
                        }
                    }

                    _bases[name] = bases.ToArray();
                }
            }

            if (!Tokens[n].Identifier) continue;
            if (Language == CodeType.CSharp)
            {
                var open = n + 1;
                if (At(open) == "<" && AngleEnd(open) is var generic && generic > open)
                {
                    _methodTypeParameters[Tokens[n].Start] = Tokens.Skip(open + 1).Take(generic - open - 1)
                        .Where(t => t.Identifier).Select(t => t.Text).ToArray();
                    open = generic + 1;
                }

                if (At(open) == "(" && Pairs.TryGetValue(open, out var close) && At(n - 1) is not ("." or "new") &&
                    At(n) is not ("foreach" or "using" or "lock" or "fixed" or "checked" or "unchecked") &&
                    CSharpCallableBody(close) is var body &&
                    (At(body) is "{" or ";" || (At(body) == "=" && At(body + 1) == ">")))
                {
                    var returns = TypeBefore(n);
                    var owner = Owner(Tokens[n].Start);
                    if (owner == At(n)) returns = At(n);
                    var missingReturn = returns.Length == 0 && owner != At(n) &&
                                        (n == 0 || CodeCompletionRules.Modifier(At(n - 1)) ||
                                         At(n - 1) is "{" or "}" or ";" or "]") &&
                                        (At(body) != ";" || (owner != null && MethodAt(Tokens[n].Start) == null));
                    if ((returns.Length > 0 || missingReturn) && returns is not ("new" or "await") &&
                        !CodeLanguage.Get(Language).KeywordSet.Contains(At(n)))
                    {
                        if (missingReturn && DeclarationIssues.Count < 200)
                            DeclarationIssues.Add(new CodeIssue(Tokens[n].Start, At(n).Length,
                                $"Method '{At(n)}' needs a return type before its name, such as 'void {At(n)}(...)' or 'int {At(n)}(...)'. Only a constructor named '{owner ?? "the containing type"}' may omit it.",
                                "CSDECL003"));
                        var parameters = new List<CodeParameterInfo>();
                        var first = open + 1;
                        for (var p = first; p <= close; p++)
                        {
                            if (p < close && At(p) == "<" && AngleEnd(p) is var angle && angle > p)
                            {
                                p = angle;
                                continue;
                            }

                            if (p < close && Pairs.TryGetValue(p, out var nested) && nested > p)
                            {
                                p = nested;
                                continue;
                            }

                            if (p == close || At(p) == ",")
                            {
                                if (p > first)
                                {
                                    var parameter = Parameter(first, p);
                                    parameters.Add(parameter);
                                    if (DeclarationIssues.Count < 200 && parameter.Name != "__arglist" &&
                                        (string.IsNullOrEmpty(parameter.Type) || CodeLanguage.Get(Language).KeywordSet
                                            .Contains(parameter.Name)))
                                        DeclarationIssues.Add(new CodeIssue(Tokens[first].Start,
                                            Tokens[p - 1].End - Tokens[first].Start,
                                            $"Parameter in '{At(n)}' needs both a type and a name, for example 'int value'.",
                                            "CSPARAM002"));
                                }

                                first = p + 1;
                            }
                        }

                        var end = BlockEnd(n);
                        if (DeclarationIssues.Count < 200 && PrimitiveCategory(returns) != 0 && At(body) == "{" &&
                            At(body + 1) == "}")
                            DeclarationIssues.Add(new CodeIssue(Tokens[n].Start, At(n).Length,
                                $"Method '{At(n)}' must return a value of type '{returns}'; its body is empty.",
                                "CSRETURN003"));
                        ReplaceSymbol(new Symbol(At(n), returns, owner, Tokens[n].Start, Tokens[n].Start, end,
                            new CodeSignatureInfo(At(n), parameters, returns.Length > 0 ? returns : null)));
                        foreach (var parameter in parameters)
                            ReplaceSymbol(new Symbol(parameter.Name, parameter.Type ?? "", null, Tokens[n].Start,
                                Tokens[n].Start, end));
                    }
                }
            }
            else if (Language is CodeType.JavaScript or CodeType.TypeScript && At(n + 1) == "=" &&
                     Tokens.ElementAtOrDefault(n + 2)?.Identifier == true && At(n + 3) == "=" && At(n + 4) == ">")
            {
                var end = BlockEnd(n);
                var signature = new CodeSignatureInfo(At(n), [new CodeParameterInfo(At(n + 2))]);
                ReplaceSymbol(new Symbol(At(n), "", Owner(Tokens[n].Start), Tokens[n].Start, Tokens[n].Start, end,
                    signature));
                Symbols.Add(new Symbol(At(n + 2), "", null, Tokens[n].Start, Tokens[n].Start, end));
            }
        }

        _methods = Symbols.Where(s => s.Signature != null).OrderBy(s => s.ScopeStart).ToArray();
        if (Language == CodeType.CSharp)
            for (var n = 1; n < Tokens.Count; n++)
            {
                if ((n & 255) == 0) token.ThrowIfCancellationRequested();
                if (!Tokens[n].Identifier || At(n + 1) is not ("=" or ";" or "{") ||
                    At(n - 1) is "." or "return") continue;
                var type = TypeBefore(n);
                if (At(n - 1) == "var" && At(n + 1) == "=") type = Infer(n + 2, Tokens[n].Start);
                if (type.Length == 0 || type is "get" or "set" or "init" ||
                    type.IndexOfAny(['=', '(', ')', '{', '}', ';']) >= 0 ||
                    CodeCompletionRules.Modifier(type)) continue;
                var scope = Scope(Tokens[n].Start);
                var owner = MethodAt(Tokens[n].Start) == null ? Owner(Tokens[n].Start) : null;
                ReplaceSymbol(new Symbol(At(n), type, owner, Tokens[n].Start, scope.Start, scope.End));
                _inferred[At(n)] = type;
            }

        _symbolSlots = [];
    }

    private void ReplaceSymbol(Symbol symbol)
    {
        var key = (symbol.Name, symbol.Start, symbol.Signature != null);
        if (_symbolSlots.TryGetValue(key, out var index))
        {
            Symbols[index] = symbol;
        }
        else
        {
            _symbolSlots[key] = Symbols.Count;
            Symbols.Add(symbol);
        }
    }

    private static string RootType(string type)
    {
        type = type.TrimEnd('?');
        var open = type.IndexOf('<');
        if (type.EndsWith("[]", StringComparison.Ordinal)) return "Array";
        if (open < 0) open = type.IndexOf('[');
        var root = open < 0 ? type : type[..open];
        return root[(root.LastIndexOf('.') + 1)..];
    }

    private static string[] TypeArguments(string type)
    {
        var open = type.IndexOf('<');
        var close = type.LastIndexOf('>');
        if (open < 0 || close <= open) return [];
        var args = new List<string>();
        var first = open + 1;
        var depth = 0;
        for (var n = first; n <= close; n++)
        {
            if (n < close && type[n] == '<') depth++;
            else if (n < close && type[n] == '>') depth--;
            if (n == close || (type[n] == ',' && depth == 0))
            {
                args.Add(type[first..n].Trim());
                first = n + 1;
            }
        }

        return args.ToArray();
    }

    private static string ElementType(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal)) return type[..^2];
        var args = TypeArguments(type);
        return args.Length == 0 ? "" : args[RootType(type) is "Dictionary" or "IDictionary" ? args.Length - 1 : 0];
    }

    private static string SubstituteType(string value, string[] parameters, string[] args)
    {
        // Replace identifier tokens together, so substituting T cannot corrupt TResult
        // or replace characters inside a generic argument inserted in this pass.
        var result = new StringBuilder();
        for (var n = 0; n < value.Length;)
        {
            if (!CodeDocument.IsIdentifier(value[n]))
            {
                result.Append(value[n++]);
                continue;
            }

            var first = n++;
            while (n < value.Length && CodeDocument.IsIdentifier(value[n])) n++;
            var word = value[first..n];
            var index = Array.IndexOf(parameters, word);
            result.Append(index >= 0 && index < args.Length ? args[index] : word);
        }

        return result.ToString();
    }

    private static CodeSignatureInfo SubstituteSignature(CodeSignatureInfo signature, string[] parameters,
        string[] args)
    {
        return signature with
        {
            ReturnType = signature.ReturnType == null
                ? null
                : SubstituteType(signature.ReturnType, parameters, args),
            Parameters = signature.Parameters.Select(p =>
                p with { Type = p.Type == null ? null : SubstituteType(p.Type, parameters, args) }).ToArray()
        };
    }

    private IEnumerable<Symbol> ResolvedMembers(string type)
    {
        var queue = new Queue<string>();
        queue.Enqueue(type);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0 && visited.Count < 16)
        {
            var current = queue.Dequeue();
            var root = RootType(current);
            if (!visited.Add(root)) continue;
            var args = current.EndsWith("[]", StringComparison.Ordinal) ? [current[..^2]] : TypeArguments(current);
            var parameters = _typeParameters.GetValueOrDefault(root) ??
                             (root is "Dictionary" or "IDictionary" ? ["TKey", "TValue"] : ["T"]);

            string Substitute(string value)
            {
                value = SubstituteType(value, parameters, args);
                if (args.Length == 1 && value is "IEnumerable" or "List" or "list") value += "<" + args[0] + ">";
                return value;
            }

            var local = _byOwner.Count == 0
                ? Symbols.Where(s => s.Owner == root)
                : _byOwner.GetValueOrDefault(root) ?? [];
            foreach (var symbol in local.Concat(CodeBuiltins.Members(Language, root)))
                yield return args.Length == 0
                    ? symbol
                    : symbol with
                    {
                        Type = Substitute(symbol.Type),
                        Signature = symbol.Signature is not { } signature
                            ? null
                            : signature with
                            {
                                ReturnType = signature.ReturnType == null ? null : Substitute(signature.ReturnType),
                                Parameters = signature.Parameters.Select(p =>
                                    p with { Type = p.Type == null ? null : Substitute(p.Type) }).ToArray()
                            }
                    };
            foreach (var parent in _bases.GetValueOrDefault(root) ?? []) queue.Enqueue(Substitute(parent));
        }
    }

    private IEnumerable<CodeCompletionItem> ParameterCompletions(int caret, string prefix)
    {
        if (Language is not (CodeType.CSharp or CodeType.Python) ||
            Signature(caret - prefix.Length) is not { } help) yield break;
        var separator = Language == CodeType.Python ? "=" : ": ";
        var before = Before(caret - prefix.Length);
        if (At(before) is not ("(" or ",")) yield break;
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var n = Before(help.ArgumentListStart + 1) + 1;
             n < Tokens.Count && Tokens[n].Start < caret - prefix.Length;
             n++)
            if (Tokens[n].Identifier && At(n + 1) == (Language == CodeType.Python ? "=" : ":"))
                used.Add(At(n));
        foreach (var parameter in help.Signatures[help.ActiveSignature].Parameters)
        {
            if (parameter.Name.StartsWith('*') || used.Contains(parameter.Name)) continue;
            yield return new CodeCompletionItem(parameter.Name + (Language == CodeType.Python ? "=" : ":"),
                parameter.Name + separator, parameter.ToString()) { Kind = CodeCompletionKind.Variable };
        }
    }
}