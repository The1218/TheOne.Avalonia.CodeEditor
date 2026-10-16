namespace TheOne.Avalonia.Controls;

public enum CodeCompletionKind
{
    Keyword,
    Variable,
    Type,
    Property,
    Method,
    Snippet
}

public enum CodeDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>UTF-16 document span. Line and column are one-based.</summary>
public sealed record CodeDiagnostic(
    int Start,
    int Length,
    string Message,
    string Code,
    CodeDiagnosticSeverity Severity,
    int Line,
    int Column);

public sealed record CodeParameterInfo(string Name, string? Type = null, string? DefaultValue = null)
{
    public override string ToString()
    {
        return (Type is { Length: > 0 } ? Type + " " : "") + Name + (DefaultValue != null ? " = " + DefaultValue : "");
    }
}

public sealed record CodeSignatureInfo(
    string Name,
    IReadOnlyList<CodeParameterInfo> Parameters,
    string? ReturnType = null,
    string? Documentation = null)
{
    public override string ToString()
    {
        return Name + "(" + string.Join(", ", Parameters) + ")" + (ReturnType != null ? " → " + ReturnType : "");
    }
}

public sealed record CodeSignatureHelp(
    IReadOnlyList<CodeSignatureInfo> Signatures,
    int ActiveSignature,
    int ActiveParameter,
    int ArgumentListStart);

// An immutable, cancellable snapshot; no UI objects or reflection in the language engine.
internal sealed partial class CodeAnalysis
{
    private static readonly string[] TokenCharacters =
        Enumerable.Range(0, 128).Select(n => ((char)n).ToString()).ToArray();

    internal readonly CodeDocument Document;
    internal readonly CodeType Language;
    internal readonly Dictionary<int, int> Pairs = [];
    internal readonly List<Symbol> Symbols = [];
    internal readonly List<Token> Tokens = [];
    internal readonly Dictionary<string, (int Start, int End)> Types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Symbol[]> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Symbol[]> _byOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _inferred = new(StringComparer.Ordinal);
    internal IReadOnlyList<CodeDiagnostic> Diagnostics = [];
    private Symbol[] _methods = [];
    private (string Name, int Start, int End, int MaxEnd)[] _typeRanges = [];

    private CodeAnalysis(string text, CodeType language, CancellationToken token)
    {
        var phase = CodeWorkProbe.Start("snapshot", token);
        Document = new CodeDocument(!SupportsSemanticScopes(language));
        Document.Reset(text, token);
        Language = language;
        phase.Dispose();
        phase = CodeWorkProbe.Start("lex", token);
        CodeSyntax.Update(Document, language, force: true, cancellation: token, visualScopes: false);
        phase.Dispose();
        phase = CodeWorkProbe.Start("tokens", token);
        for (var lineNumber = 0; lineNumber < Document.Lines.Count; lineNumber++)
        {
            token.ThrowIfCancellationRequested();
            var line = Document.Lines[lineNumber];
            foreach (var span in line.Tokens)
            {
                if (span.Kind == CodeTokenKind.Comment) continue;
                if (span.Kind == CodeTokenKind.String)
                {
                    Tokens.Add(new Token(line.Text.Substring(span.Start, span.Length), line.Start + span.Start,
                        lineNumber, span.Kind));
                    continue;
                }

                var end = span.Start + span.Length;
                for (var p = span.Start; p < end;)
                {
                    if (char.IsWhiteSpace(line.Text[p]))
                    {
                        p++;
                        continue;
                    }

                    var first = p++;
                    if (CodeDocument.IsIdentifier(line.Text[first]))
                        while (p < end && CodeDocument.IsIdentifier(line.Text[p]))
                            p++;
                    var value = p == first + 1 && line.Text[first] < TokenCharacters.Length
                        ? TokenCharacters[line.Text[first]]
                        : line.Text[first..p];
                    Tokens.Add(new Token(value, line.Start + first, lineNumber,
                        span.Kind));
                }
            }
        }

        phase.Dispose();
        phase = CodeWorkProbe.Start("pairs", token);
        var stack = new Stack<int>();
        for (var i = 0; i < Tokens.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            if (Tokens[i].Kind == CodeTokenKind.String) continue;
            var value = At(i);
            if (value is "(" or "[" or "{")
            {
                stack.Push(i);
            }
            else if (value is ")" or "]" or "}" && stack.TryPeek(out var open) && Matches(At(open), value))
            {
                stack.Pop();
                Pairs[open] = i;
                Pairs[i] = open;
            }
        }

        phase.Dispose();
        phase = CodeWorkProbe.Start("symbols", token);
        Index(token);
        phase.Dispose();
        phase = CodeWorkProbe.Start("scopes", token);
        IndexSemanticScopes(token);
        phase.Dispose();
        phase = CodeWorkProbe.Start("lookups", token);
        token.ThrowIfCancellationRequested();
        _byName = Symbols.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        token.ThrowIfCancellationRequested();
        _byOwner = Symbols.Where(s => s.Owner != null).GroupBy(s => s.Owner!)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        phase.Dispose();
        phase = CodeWorkProbe.Start("semantic-colors", token);
        IndexSemanticColors(token);
        phase.Dispose();
        phase = CodeWorkProbe.Start("diagnostics", token);
        Diagnostics = CodeDiagnostics.Analyze(this, token);
        phase.Dispose();
    }

    internal static CodeAnalysis Create(string text, CodeType language, CancellationToken token)
    {
        using var work = CodeWorkProbe.Start("analysis", token);
        token.ThrowIfCancellationRequested();
        return new CodeAnalysis(text, language, token);
    }

    internal string At(int i)
    {
        return i >= 0 && i < Tokens.Count ? Tokens[i].Text : "";
    }

    internal string? ContainingType(int caret)
    {
        return Owner(caret);
    }

    internal static bool Matches(string a, string b)
    {
        return (a, b) is ("(", ")") or ("[", "]") or ("{", "}");
    }

    private int BlockEnd(int index)
    {
        if (Language is CodeType.Ruby or CodeType.Lua)
        {
            var depth = 1;
            for (var n = index + 1; n < Tokens.Count; n++)
            {
                if (Tokens[n].Kind == CodeTokenKind.String) continue;
                if (At(n) is "def" or "function" or "if" or "for" or "while") depth++;
                if (At(n) == "end" && --depth == 0) return Tokens[n].End;
            }

            return Document.Text.Length;
        }

        if (Language == CodeType.Python)
        {
            var line = Tokens[index].Line;
            var indent = Indent(Document.Lines[line].Text);
            for (var n = line + 1; n < Document.Lines.Count; n++)
                if (!string.IsNullOrWhiteSpace(Document.Lines[n].Text) &&
                    !Document.Lines[n].Text.TrimStart().StartsWith('#') && Indent(Document.Lines[n].Text) <= indent)
                    return Document.Lines[n].Start;
            return Document.Text.Length;
        }

        var stop = Math.Min(Tokens.Count, index + 160);
        var expressionBody = false;
        for (var n = index + 1; n < stop; n++)
        {
            if (At(n) == "=" && At(n + 1) == ">")
            {
                expressionBody = true;
                n++;
                continue;
            }

            if ((At(n) is "(" or "[" || (expressionBody && At(n) == "{")) && Pairs.TryGetValue(n, out var nested) &&
                nested > n)
            {
                n = nested;
                continue;
            }

            if (At(n) == ";") return Tokens[n].End;
            if (At(n) == "}") return Tokens[n].Start;
            if (At(n) == "{" && Pairs.TryGetValue(n, out var end)) return Tokens[end].End;
        }

        return Document.Text.Length;
    }

    internal static int Indent(string text)
    {
        var n = 0;
        foreach (var c in text)
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        return n;
    }

    private string? Owner(int offset)
    {
        var lo = 0;
        var hi = _typeRanges.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_typeRanges[mid].Start <= offset) lo = mid + 1;
            else hi = mid;
        }

        for (var n = lo - 1; n >= 0 && _typeRanges[n].MaxEnd > offset; n--)
            if (_typeRanges[n].End > offset)
                return _typeRanges[n].Name;
        return null;
    }

    private (int Start, int End) Scope(int offset)
    {
        var method = MethodAt(offset);
        if (method != null) return (method.ScopeStart, method.ScopeEnd);
        if (Owner(offset) is { } owner) return Types[owner];
        return (0, Document.Text.Length);
    }

    private Symbol? MethodAt(int offset)
    {
        var lo = 0;
        var hi = _methods.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_methods[mid].ScopeStart < offset) lo = mid + 1;
            else hi = mid;
        }

        return lo > 0 && _methods[lo - 1].ScopeEnd > offset ? _methods[lo - 1] : null;
    }

    private void Index(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        for (var i = 0; i < Tokens.Count - 1; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            if ((At(i) is "class" or "struct" or "interface" or "record" or "enum" || (Language == CodeType.Go &&
                    At(i) == "type" && At(i + 2) is "struct" or "interface")) && Tokens[i + 1].Identifier)
            {
                var name = At(i + 1);
                Types.TryAdd(name, (Tokens[i].Start, BlockEnd(i)));
            }
        }

        for (var i = 0; i < Tokens.Count - 2; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            if (Tokens[i].Identifier && At(i + 1) == "=" &&
                (At(i + 2) == "{" || (At(i + 2) == "new" && At(i + 3) == "{")))
            {
                var open = At(i + 2) == "new" ? i + 3 : i + 2;
                if (Pairs.TryGetValue(open, out var end))
                    Types.TryAdd("@" + Tokens[open].Start, (Tokens[open].Start, Tokens[end].End));
            }
        }

        var ranges = Types.Select(p => (Name: p.Key, p.Value.Start, p.Value.End)).ToList();
        if (Language == CodeType.Rust)
            for (var i = 0; i < Tokens.Count - 1; i++)
                if (At(i) == "impl" && Tokens[i + 1].Identifier)
                    ranges.Add((At(i + 1), Tokens[i].Start, BlockEnd(i)));
        var maxEnd = 0;
        _typeRanges = ranges.OrderBy(p => p.Start).Select(p =>
        {
            maxEnd = Math.Max(maxEnd, p.End);
            return (p.Name, p.Start, p.End, maxEnd);
        }).ToArray();
        for (var i = 0; i < Tokens.Count - 2; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            if (!Tokens[i].Identifier || At(i + 1) != "(" || !Pairs.TryGetValue(i + 1, out var close)) continue;
            var before = At(i - 1);
            var after = At(close + 1);
            var goReceiver = Language == CodeType.Go && before == ")" &&
                             Pairs.TryGetValue(i - 1, out var receiverOpen) && At(receiverOpen - 1) == "func";
            var declared = before is "def" or "function" or "fn" or "func" or "fun" || goReceiver ||
                           (before != "." && before != "new" &&
                            !CodeLanguage.Get(Language).KeywordSet.Contains(At(i)) &&
                            (after == "{" || (after == ":" && Language == CodeType.Python) ||
                             (after == ":" && Language == CodeType.TypeScript) ||
                             (after == "=" && At(close + 2) == ">")) &&
                            (Tokens.ElementAtOrDefault(i - 1)?.Identifier == true || Owner(Tokens[i].Start) != null));
            if (!declared || At(i) is "if" or "while" or "for" or "switch" or "catch" or "with") continue;
            var parameters = new List<CodeParameterInfo>();
            var segment = i + 2;
            for (var n = segment; n <= close; n++)
            {
                if (n < close && Pairs.TryGetValue(n, out var nested) && nested > n)
                {
                    n = nested;
                    continue;
                }

                if (n != close && At(n) != ",") continue;
                if (segment < n) parameters.Add(Parameter(segment, n));
                segment = n + 1;
            }

            var returns = before is "def" or "function" or "fn" or "func" or "fun" || goReceiver ? "" : before;
            if (after == ":" && Language != CodeType.Python) returns = At(close + 2);
            if (after == "-" && At(close + 2) == ">") returns = At(close + 3);
            if (Language == CodeType.Go && Tokens.ElementAtOrDefault(close + 1)?.Identifier == true) returns = after;
            var returnStart = after == ":" && Language != CodeType.Python ? close + 2 :
                after == "-" && At(close + 2) == ">" ? close + 3 : -1;
            if (returnStart >= 0)
            {
                var end = returnStart;
                while (end < Math.Min(Tokens.Count, returnStart + 64) && Tokens[end].Line == Tokens[returnStart].Line &&
                       At(end) is not ("{" or ";" or "=" or ":")) end++;
                returns = string.Concat(Tokens.Skip(returnStart).Take(end - returnStart).Select(t => t.Text));
            }

            var signature = new CodeSignatureInfo(At(i),
                parameters.Where(p =>
                        p.Name is not ("self" or "cls" or "/" or "*") && !(Language is CodeType.C or CodeType.Cpp &&
                                                                           parameters.Count == 1 && p.Name == "void"))
                    .ToArray(), returns.Length > 0 ? returns : null);
            var scopeEnd = BlockEnd(i);
            var owner = goReceiver ? At(i - 2) : Owner(Tokens[i].Start);
            if (goReceiver && Pairs.TryGetValue(i - 1, out var receiverStart))
                Symbols.Add(new Symbol(At(receiverStart + 1), owner ?? "", null, Tokens[i].Start, Tokens[i].Start,
                    scopeEnd));
            Symbols.Add(new Symbol(At(i), returns, owner, Tokens[i].Start, Tokens[i].Start, scopeEnd, signature));
            foreach (var p in parameters.Where(p => p.Name is not ("self" or "cls" or "/" or "*")))
                Symbols.Add(new Symbol(p.Name.TrimStart('*', '.'), p.Type ?? "", null, Tokens[i].Start, Tokens[i].Start,
                    scopeEnd));
        }

        if (Language is CodeType.JavaScript or CodeType.TypeScript)
            for (var i = 1; i < Tokens.Count - 3; i++)
                if (Tokens[i - 1].Identifier && At(i) == "=" && At(i + 1) == "(" &&
                    Pairs.TryGetValue(i + 1, out var close))
                {
                    var arrow = close + 1;
                    string? returns = null;
                    if (Language == CodeType.TypeScript && At(arrow) == ":")
                    {
                        var firstType = ++arrow;
                        while (arrow < Math.Min(Tokens.Count, firstType + 64) &&
                               At(arrow) is not ("=" or "{" or ";")) arrow++;
                        returns = string.Concat(Tokens.Skip(firstType).Take(arrow - firstType).Select(t => t.Text));
                    }

                    if (At(arrow) != "=" || At(arrow + 1) != ">") continue;
                    var parameters = new List<CodeParameterInfo>();
                    var first = i + 2;
                    for (var n = first; n <= close; n++)
                        if (n == close || At(n) == ",")
                        {
                            if (n > first) parameters.Add(Parameter(first, n));
                            first = n + 1;
                        }

                    Symbols.Add(new Symbol(At(i - 1), returns ?? "", Owner(Tokens[i].Start), Tokens[i - 1].Start,
                        Tokens[i - 1].Start, BlockEnd(i), new CodeSignatureInfo(At(i - 1), parameters, returns)));
                    foreach (var p in parameters)
                        Symbols.Add(new Symbol(p.Name.TrimStart('*'), p.Type ?? "", null, Tokens[i - 1].Start,
                            Tokens[i - 1].Start, BlockEnd(i)));
                }

        _methods = Symbols.Where(s => s.Signature != null).OrderBy(s => s.ScopeStart).ToArray();
        for (var i = 0; i < Tokens.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            if (!Tokens[i].Identifier || At(i - 1) == "." || At(i + 1) == "(") continue;
            var next = At(i + 1);
            var prior = At(i - 1);
            var type = "";
            var goField = Language == CodeType.Go && Tokens.ElementAtOrDefault(i + 1)?.Identifier == true &&
                          Owner(Tokens[i].Start) != null &&
                          (prior == "{" || Tokens.ElementAtOrDefault(i - 1)?.Line < Tokens[i].Line);
            if (goField || next == "=" || (next == ":" && At(i + 2) != ":" && At(i - 1) != ":") ||
                (next == ";" && Tokens.ElementAtOrDefault(i - 1)?.Identifier == true) ||
                (next == "{" && Tokens.ElementAtOrDefault(i - 1)?.Identifier == true))
            {
                if (goField) type = next;
                else if (Language == CodeType.Go && next == ":" && At(i + 2) == "=")
                    type = Infer(i + 3, Tokens[i].Start);
                else if (next == ":") type = At(i + 2);
                else if (Language != CodeType.Python &&
                         prior is not ("var" or "let" or "const" or "return" or "val" or "final" or "mut" or "auto"
                             or "local") && Tokens.ElementAtOrDefault(i - 1) is { Identifier: true } previous &&
                         previous.Line == Tokens[i].Line) type = prior;
                if (type.Length == 0 && next == "=") type = Infer(i + 2, Tokens[i].Start);
                var scope = Scope(Tokens[i].Start);
                var inMethod = MethodAt(Tokens[i].Start) != null;
                Symbols.Add(new Symbol(At(i), type, inMethod ? null : Owner(Tokens[i].Start), Tokens[i].Start,
                    scope.Start, scope.End));
                _inferred[At(i)] = type;
            }

            // Python instance attributes are often introduced in __init__.
            if (At(i) == "self" && At(i + 1) == "." && At(i + 3) is "=" or ":" && Owner(Tokens[i].Start) is { } owner)
                Symbols.Add(new Symbol(At(i + 2), At(i + 3) == ":" ? At(i + 4) : Infer(i + 4, Tokens[i].Start), owner,
                    Tokens[i].Start, Types[owner].Start, Types[owner].End));
        }

        IndexExtended(token);
    }

    private CodeParameterInfo Parameter(int first, int end)
    {
        if (Language == CodeType.CSharp)
            while (At(first) == "[" && Pairs.TryGetValue(first, out var attribute) && attribute < end)
                first = attribute + 1;
        var eq = end;
        var colon = -1;
        for (var n = first; n < end; n++)
        {
            if (At(n) == "=" && eq == end) eq = n;
            if (At(n) == ":" && colon < 0) colon = n;
        }

        var ids = Tokens.Skip(first).Take(eq - first)
            .Where(t => t.Identifier && t.Text is not ("ref" or "out" or "in" or "params")).ToArray();
        var name = colon > first ? At(colon - 1) :
            Language is CodeType.Python or CodeType.JavaScript or CodeType.Go ? ids.FirstOrDefault()?.Text :
            ids.LastOrDefault()?.Text;
        var type = colon >= 0 && colon + 1 < eq
            ? string.Concat(Tokens.Skip(colon + 1).Take(eq - colon - 1).Select(t => t.Text))
            : ids.Length > 1
                ? Language == CodeType.Go ? ids[^1].Text : ids[0].Text
                : null;
        if (Language == CodeType.CSharp && ids.Length > 1)
        {
            var nameAt = eq - 1;
            while (nameAt > first && !Tokens[nameAt].Identifier) nameAt--;
            var typeStart = first;
            while (At(typeStart) is "ref" or "out" or "in" or "params" or "this" or "scoped" or "readonly") typeStart++;
            name = At(nameAt);
            type = string.Concat(Tokens.Skip(typeStart).Take(nameAt - typeStart).Select(t => t.Text));
        }

        var optional = Language == CodeType.TypeScript && colon > first && At(colon - 1) == "?";
        if (optional) name = At(colon - 2);
        if (Language == CodeType.Python && At(first) == "*" && first + 1 < end)
            name = (At(first + 1) == "*" ? "**" : "*") + name;
        if (Language is CodeType.JavaScript or CodeType.TypeScript && At(first) == "." && At(first + 1) == "." &&
            At(first + 2) == ".") name = "..." + name;
        if (Language == CodeType.CSharp && At(first) == "params") type = "params " + type;
        if (Language is CodeType.Java or CodeType.Go or CodeType.C or CodeType.Cpp)
            for (var n = first; n < eq - 2; n++)
                if (At(n) == "." && At(n + 1) == "." && At(n + 2) == ".")
                {
                    name = "..." + name;
                    break;
                }

        if (Language == CodeType.Kotlin && At(first) == "vararg") name = "*" + name;
        return new CodeParameterInfo(name ?? At(first), type,
            eq < end - 1 ? Document.Text[Tokens[eq + 1].Start..Tokens[end - 1].End] : optional ? "undefined" : null);
    }

    private string Infer(int index, int offset)
    {
        var value = At(index);
        if (value == "new")
        {
            if (At(index + 1) == "{") return "@" + Tokens[index + 1].Start;
            var end = index + 2;
            while (At(end) == "." && Tokens.ElementAtOrDefault(end + 1)?.Identifier == true) end += 2;
            if (At(end) == "<" && AngleEnd(end) is var angle && angle > end) end = angle + 1;
            var array = At(end) == "[";
            return string.Concat(Tokens.Skip(index + 1).Take(end - index - 1).Select(t => t.Text)) +
                   (array ? "[]" : "");
        }

        if (Tokens.ElementAtOrDefault(index)?.Kind == CodeTokenKind.String)
            return value.StartsWith('\'') && Language is CodeType.CSharp or CodeType.C or CodeType.Cpp or CodeType.Java
                or CodeType.Kotlin or CodeType.Rust
                ? "char"
                : "string";
        if (value == "[") return "list";
        if (Language == CodeType.Rust && value == "vec" && At(index + 1) == "!") return "Vec";
        if (value == "{") return Types.ContainsKey("@" + Tokens[index].Start) ? "@" + Tokens[index].Start : "object";
        if (value is "true" or "false" or "True" or "False") return "bool";
        if (value.Length > 0 && char.IsDigit(value[0])) return "number";
        if (Types.ContainsKey(value)) return value;
        if (At(index + 1) == "." || (At(index + 1) == ":" && At(index + 2) == ":"))
        {
            var receiver = _inferred.GetValueOrDefault(value, value);
            var member = At(index + (At(index + 1) == ":" ? 3 : 2));
            return Symbols.FirstOrDefault(s => s.Owner == receiver && s.Name == member)?.Type ??
                   CodeBuiltins.Members(Language, receiver).FirstOrDefault(s => s.Name == member)?.Type ?? "";
        }

        if (At(index + 1) == "(")
            return Symbols.FirstOrDefault(s => s.Name == value && s.Signature != null)?.Type ??
                   CodeBuiltins.Globals(Language).FirstOrDefault(s => s.Name == value)?.Type ?? "";
        if (At(index + 1) == "<" && AngleEnd(index + 1) is var generic && generic > index && At(generic + 1) == "(")
            return Callables(generic, offset).FirstOrDefault()?.ReturnType ?? "";
        return _inferred.GetValueOrDefault(value, "");
    }

    private string ReceiverType(int index, int caret)
    {
        if (index < 0) return "";
        if (At(index) is ")" or "]" && Pairs.TryGetValue(index, out var open))
        {
            if (At(index) == "]") return ElementType(ReceiverType(open - 1, caret));
            var name = At(open - 1);
            if (Types.ContainsKey(name)) return name;
            var callable = Callables(open - 1, caret).FirstOrDefault();
            return callable?.ReturnType ?? "";
        }

        var nameAt = At(index);
        if (nameAt is "self" or "this" or "cls") return Owner(caret) ?? "";
        if (Tokens[index].Kind == CodeTokenKind.String) return "string";
        if (MemberReceiver(index - 1, out var parentIndex))
        {
            var parent = ReceiverType(parentIndex, caret);
            return Members(parent).FirstOrDefault(s => s.Name == nameAt)?.Type ?? "";
        }

        return Named(nameAt)
            .Where(s => s.Signature == null && Visible(s, caret))
            .OrderBy(s => s.ScopeEnd - s.ScopeStart).ThenByDescending(s => s.Start).FirstOrDefault() is { } binding
            ? BindingType(binding)
            : nameAt;
    }

    private IEnumerable<Symbol> Named(string name)
    {
        return _byName.Count == 0 ? Symbols.Where(s => s.Name == name) : _byName.GetValueOrDefault(name) ?? [];
    }

    private bool MemberReceiver(int index, out int receiver)
    {
        receiver = index - 1;
        if (At(index) == ".")
        {
            if (At(receiver) == "?") receiver--;
            return true;
        }

        if ((At(index) == ":" && At(index - 1) == ":") || (At(index) == ">" && At(index - 1) == "-"))
        {
            receiver--;
            return true;
        }

        return Language == CodeType.Lua && At(index) == ":";
    }

    private IEnumerable<Symbol> Members(string type)
    {
        return ResolvedMembers(type);
    }

    private IEnumerable<CodeSignatureInfo> Callables(int index, int caret)
    {
        var original = index;
        index = CallableName(index);
        var name = Language == CodeType.Rust && At(index) == "!" ? At(--index) + "!" : At(index);
        var candidates = MemberReceiver(index - 1, out var receiver)
            ? Members(ReceiverType(receiver, caret))
            : Named(name).Where(s => Visible(s, caret)).Concat(CodeBuiltins.Globals(Language))
                .Concat(CodeBuiltins.Constructors(Language, name));
        var arguments = original == index
            ? []
            : TypeArguments(string.Concat(Tokens.Skip(index).Take(original - index + 1).Select(t => t.Text)));
        return candidates.Where(s => s.Name == name && s.Signature != null).Select(s =>
        {
            var parameters = _methodTypeParameters.GetValueOrDefault(s.Start) ??
                             _typeParameters.GetValueOrDefault(name) ??
                             (name == "Dictionary" ? ["TKey", "TValue"] : ["T"]);
            return arguments.Length == 0 ? s.Signature! : SubstituteSignature(s.Signature!, parameters, arguments);
        });
    }

    private int Before(int offset)
    {
        var lo = 0;
        var hi = Tokens.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Tokens[mid].Start < offset) lo = mid + 1;
            else hi = mid;
        }

        return lo - 1;
    }

    internal IReadOnlyList<CodeCompletionItem> Complete(int caret, string prefix,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var contextual = CodeContextCompletions.Get(Document, Language, caret, prefix);
        if (contextual != null) return contextual;
        var before = Before(caret - prefix.Length);
        var member = MemberReceiver(before, out var receiver);
        var owner = Owner(caret);
        var typeContext = !member && CodeCompletionRules.TypeContext(Document, Language, caret, prefix);
        var parameters = member || typeContext ? [] : ParameterCompletions(caret, prefix).ToArray();
        var parameterNames = parameters.Select(p => p.Label).ToHashSet(StringComparer.Ordinal);

        int Match(CodeCompletionItem item)
        {
            return CodeCompletionRules.Score(
                parameterNames.Contains(item.Label) ? item.Label.TrimEnd(':', '=') : item.Label, prefix);
        }

        var source = member
            ? Members(ReceiverType(receiver, caret))
            : Symbols.Where(s =>
                    s.Owner == null && Visible(s, caret))
                .Concat(owner == null ? [] : Members(owner)).Concat(CodeBuiltins.Globals(Language));
        // Filter before retaining/sorting symbols. A narrow prefix must not allocate
        // arrays and name sets for every unrelated binding in a large document.
        var symbols = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var visited = 0;
        if (!typeContext)
            foreach (var symbol in source)
            {
                if ((visited++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
                if (CodeCompletionRules.Score(symbol.Name, prefix) == int.MaxValue) continue;
                if (!symbols.TryGetValue(symbol.Name, out var previous) ||
                    symbol.ScopeEnd - symbol.ScopeStart < previous.ScopeEnd - previous.ScopeStart ||
                    (symbol.ScopeEnd - symbol.ScopeStart == previous.ScopeEnd - previous.ScopeStart &&
                     symbol.Start > previous.Start))
                    symbols[symbol.Name] = symbol;
            }

        var expected = member || typeContext ? "" : ExpectedCompletionType(caret, prefix);

        int SymbolTypeRank(Symbol symbol)
        {
            if (PrimitiveCategory(expected) == 0) return 1;
            var actual = symbol.Signature?.ReturnType ?? BindingType(symbol);
            return PrimitiveCategory(actual) == 0 ? 1 : Incompatible(expected, actual) ? 2 : 0;
        }

        var items = symbols.Values.OrderBy(s => CodeCompletionRules.Score(s.Name, prefix))
            .ThenBy(SymbolTypeRank).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Take(100)
            .Select(s =>
                new CodeCompletionItem(s.Name,
                    s.Signature == null
                        ? s.Name
                        : s.Name + (Language is CodeType.Bash or CodeType.PowerShell ? " " : "()"),
                    s.Signature?.ToString() ?? BindingType(s),
                    s.Signature == null || Language is CodeType.Bash or CodeType.PowerShell
                        ? -1
                        : s.Name.Length + 1)
                {
                    Kind = s.Signature != null ? CodeCompletionKind.Method :
                        member ? CodeCompletionKind.Property : CodeCompletionKind.Variable
                });
        if (!member)
            items = items.Concat(Types.Keys.Where(s => !s.StartsWith('@') && !symbols.ContainsKey(s)).Select(s =>
                    new CodeCompletionItem(s, s, "Document type") { Kind = CodeCompletionKind.Type }))
                .Concat(CodeCompletionRules.Types(Language))
                .Concat(typeContext ? [] : CodeSnippets.Get(Language, owner))
                .Concat(parameters)
                .Concat(typeContext
                    ? CodeLanguage.Get(Language).KeywordSet.Where(CodeCompletionRules.DeclarationKeyword).Select(s =>
                        new CodeCompletionItem(s, s, "Declaration keyword") { Kind = CodeCompletionKind.Keyword })
                    : CodeCompletions.Get(Document, Language, prefix).Where(i =>
                        i.Detail != "Document" || symbols.ContainsKey(i.Label) ||
                        (!HasSemanticScopes && !Named(i.Label).Any())));
        var rankedSymbols = PrimitiveCategory(expected) == 0 ? null : symbols;
        var result = items.Where(s =>
            {
                if ((visited++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
                return Match(s) < int.MaxValue;
            })
            .DistinctBy(s => s.Label).OrderBy(Match).ThenBy(s => parameterNames.Contains(s.Label) ? 0 : 1)
            .ThenBy(s => CompletionTypeRank(s, rankedSymbols, expected))
            .ThenBy(s =>
                typeContext && s.Kind == CodeCompletionKind.Type ? 0 : s.Kind == CodeCompletionKind.Snippet ? 1 : 2)
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    internal CodeSignatureHelp? Signature(int caret)
    {
        var active = 0;
        var closed = new Stack<string>();
        for (var i = Before(caret); i >= 0; i--)
        {
            if (Tokens[i].Kind == CodeTokenKind.String) continue;
            var t = At(i);
            if (Language == CodeType.CSharp && t == ">" && At(i + 1) == "(" && CallableName(i) is var generic &&
                generic < i)
            {
                i = generic;
                continue;
            }

            if (t is ")" or "]" or "}")
            {
                closed.Push(t);
            }
            else if (t is "(" or "[" or "{")
            {
                if (closed.TryPeek(out var closing))
                {
                    if (Matches(t, closing)) closed.Pop();
                    continue;
                }

                if (t != "(")
                {
                    active = 0;
                    continue;
                }

                if (_headers.Contains(i)) return null;
                var signatures = Callables(i - 1, caret).Distinct().Take(12).ToArray();
                if (signatures.Length == 0)
                {
                    active = 0;
                    continue;
                }

                var selected = SelectSignature(signatures, i + 1, caret, active);
                selected = selected < 0 ? signatures.Length - 1 : selected;
                if (Language is CodeType.CSharp or CodeType.Python)
                {
                    var segment = i + 1;
                    for (var p = segment; p < Tokens.Count && Tokens[p].Start < caret; p++)
                    {
                        if (Pairs.TryGetValue(p, out var end) && end > p && Tokens[end].Start < caret)
                        {
                            p = end;
                            continue;
                        }

                        if (At(p) == ",") segment = p + 1;
                    }

                    if (At(segment + 1) == (Language == CodeType.Python ? "=" : ":"))
                    {
                        var named = signatures[selected].Parameters.Select((p, index) => (p, index))
                            .FirstOrDefault(p => p.p.Name == At(segment));
                        if (named.p != null) active = named.index;
                    }
                }

                return new CodeSignatureHelp(signatures, selected, active, Tokens[i].Start);
            }
            else if (t == "," && closed.Count == 0)
            {
                active++;
            }
            else if (t == ";" && closed.Count == 0)
            {
                return null;
            }
        }

        return null;
    }

    internal IEnumerable<(int Start, int Length, string Message, CodeDiagnosticSeverity Severity)> ArgumentErrors(
        CancellationToken token)
    {
        var declarations = Symbols.Where(s => s.Signature != null).Select(s => s.Start).ToHashSet();
        for (var n = 1; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) token.ThrowIfCancellationRequested();
            if (At(n) != "(" || !Tokens[n - 1].Identifier || declarations.Contains(Tokens[n - 1].Start) ||
                !Pairs.TryGetValue(n, out var close)) continue;
            if (ArgumentError(n, close) is { } error) yield return error;
        }
    }

    // Keep overload-check closures out of the per-token traversal above.
    private (int Start, int Length, string Message, CodeDiagnosticSeverity Severity)? ArgumentError(int n, int close)
    {
        var signatures = Callables(n - 1, Tokens[n].Start).ToArray();
        if (signatures.Length == 0) return null;
        var count = n + 1 == close ? 0 : 1;
        var unpacked = false;
        for (var i = n + 1; i < close; i++)
        {
            if (Pairs.TryGetValue(i, out var end) && end > i)
            {
                i = end;
                continue;
            }

            if (Language == CodeType.Python && At(i) == "*" && (i == n + 1 || At(i - 1) == ",")) unpacked = true;
            if (Language == CodeType.CSharp && At(i) == "<" && AngleEnd(i) is var generic && generic > i &&
                At(generic + 1) == "(")
            {
                i = generic;
                continue;
            }

            if (At(i) == "," && i + 1 < close) count++;
        }

        bool Accepts(CodeSignatureInfo s)
        {
            bool Variadic(CodeParameterInfo p)
            {
                return p.Name.StartsWith('*') || p.Name.StartsWith("...", StringComparison.Ordinal) ||
                       p.Type?.StartsWith("params ", StringComparison.Ordinal) == true;
            }

            if (unpacked) return true;
            return count >= s.Parameters.Count(p => p.DefaultValue == null && !Variadic(p)) &&
                   (s.Parameters.Any(Variadic) || count <= s.Parameters.Count);
        }

        if (!signatures.Any(Accepts))
            return (Tokens[n - 1].Start, Tokens[n - 1].Text.Length,
                $"No known overload of '{At(n - 1)}' accepts {count} argument(s). Expected: {string.Join("; ", signatures.Select(s => s.ToString()))}",
                CodeDiagnosticSeverity.Error);

        var arguments = new List<(int First, int End, string? Name)>();
        var segment = n + 1;
        for (var i = n + 1; i <= close; i++)
        {
            if (i < close && Pairs.TryGetValue(i, out var end) && end > i)
            {
                i = end;
                continue;
            }

            if (i < close && At(i) == "<" && AngleEnd(i) is var angle && angle > i && At(angle + 1) == "(")
            {
                i = angle;
                continue;
            }

            if (i == close || At(i) == ",")
            {
                if (i > segment)
                {
                    var named = At(segment + 1) == (Language is CodeType.Python or CodeType.Kotlin ? "=" : ":") &&
                                Language is CodeType.CSharp or CodeType.Python or CodeType.Swift or CodeType.Kotlin
                                    or CodeType.Php;
                    arguments.Add((named ? segment + 2 : segment, i, named ? At(segment) : null));
                }

                segment = i + 1;
            }
        }

        bool Compatible(CodeSignatureInfo signature, bool types)
        {
            var used = new HashSet<int>();
            var position = 0;
            foreach (var argument in arguments)
            {
                var p = argument.Name == null
                    ? position++
                    : signature.Parameters.ToList()
                        .FindIndex(p => p.Name.TrimStart('$') == argument.Name.TrimStart('$'));
                if (p < 0 && Language == CodeType.Swift) p = position++;
                var variadic = signature.Parameters.Select((v, i) => (v, i))
                    .FirstOrDefault(p => VariadicParameter(p.v));
                if (p < 0)
                {
                    if (signature.Parameters.Any(p => p.Name.StartsWith("**", StringComparison.Ordinal))) continue;
                    return false;
                }

                if (p >= signature.Parameters.Count)
                {
                    if (variadic.v != null) continue;
                    return false;
                }

                var parameter = signature.Parameters[p];
                if (!used.Add(p) && !VariadicParameter(parameter)) return false;
                if (VariadicParameter(parameter)) continue;
                if (types && Incompatible(parameter.Type, ExpressionType(argument.First, argument.End))) return false;
            }

            return signature.Parameters.Select((p, i) => (p, i)).All(p =>
                used.Contains(p.i) || p.p.DefaultValue != null || VariadicParameter(p.p));
        }

        if (!unpacked && !signatures.Where(Accepts).Any(s => Compatible(s, true)))
            return (Tokens[n - 1].Start, Tokens[n - 1].Text.Length,
                $"Argument names or types do not match '{At(n - 1)}'. Expected: {string.Join("; ", signatures.Select(s => s.ToString()))}",
                Language == CodeType.Python && signatures.Where(Accepts).Any(s => Compatible(s, false))
                    ? CodeDiagnosticSeverity.Warning
                    : CodeDiagnosticSeverity.Error);
        return null;
    }

    private static bool VariadicParameter(CodeParameterInfo p)
    {
        return p.Name.StartsWith('*') || p.Name.StartsWith("...", StringComparison.Ordinal) ||
               p.Type?.StartsWith("params ", StringComparison.Ordinal) == true;
    }

    internal IEnumerable<(int Start, int Length, string Message)> MemberErrors(CancellationToken token)
    {
        var extensible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, range) in Types)
        {
            var start = Before(range.Start + 1);
            for (var i = start + 1; i < Math.Min(Tokens.Count, start + 64) && At(i) != "{"; i++)
                if (At(i) is ":" or "extends" or "implements")
                {
                    extensible.Add(name);
                    break;
                }
        }

        for (var n = 2; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) token.ThrowIfCancellationRequested();
            if (!Tokens[n].Identifier || !MemberReceiver(n - 1, out var receiver)) continue;
            var type = ReceiverType(receiver, Tokens[n].Start);
            // Only complete, locally declared types: imported/dynamic types and
            // framework catalogs may have additional members we cannot know about.
            if (!Types.ContainsKey(type) || extensible.Contains(type) || type.StartsWith('@') ||
                At(n + 1) is not ("(" or ";" or ")" or "=")) continue;
            if (Members(type).Any(s => s.Name == At(n))) continue;
            var available = Members(type).Select(s => s.Name).Distinct().Take(8).ToArray();
            yield return (Tokens[n].Start, Tokens[n].Text.Length,
                $"Type '{type}' has no declared member '{At(n)}'." + (available.Length == 0
                    ? ""
                    : $" Available members: {string.Join(", ", available)}."));
        }
    }

    internal sealed record Token(string Text, int Start, int Line, CodeTokenKind Kind)
    {
        internal int End => Start + Text.Length;
        internal bool Identifier => Text.Length > 0 && (char.IsLetter(Text[0]) || Text[0] is '_' or '$');
    }

    internal sealed record Symbol(
        string Name,
        string Type,
        string? Owner,
        int Start,
        int ScopeStart,
        int ScopeEnd,
        CodeSignatureInfo? Signature = null);
}