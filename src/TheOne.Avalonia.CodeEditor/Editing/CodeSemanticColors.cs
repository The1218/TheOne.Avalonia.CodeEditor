namespace TheOne.Avalonia.Controls;

internal enum CodeSemanticKind : byte
{
    Type,
    Method,
    Field,
    Property,
    Parameter,
    Variable
}

internal readonly record struct CodeSemanticSpan(int Start, int Length, CodeSemanticKind Kind);

// Immutable, sorted UTF-16 spans. The UI queries only the current visual row.
internal sealed class CodeSemanticColors(CodeSemanticSpan[] spans)
{
    internal static readonly CodeSemanticColors Empty = new([]);
    internal bool IsEmpty => spans.Length == 0;

    internal ReadOnlySpan<CodeSemanticSpan> ForRange(int start, int end)
    {
        var lo = 0;
        var hi = spans.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (spans[mid].Start + spans[mid].Length <= start) lo = mid + 1;
            else hi = mid;
        }

        var first = lo;
        while (lo < spans.Length && spans[lo].Start < end) lo++;
        return spans.AsSpan(first, lo - first);
    }
}

internal sealed partial class CodeAnalysis
{
    internal CodeSemanticColors SemanticColors { get; private set; } = CodeSemanticColors.Empty;

    private void IndexSemanticColors(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!HasSemanticScopes) return;
        var declarations = new Dictionary<int, Symbol>(Symbols.Count);
        var visited = 0;
        foreach (var symbol in Symbols)
        {
            if ((visited++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
            if (symbol.Signature != null || !_functionScopes.ContainsKey(symbol.Start))
                declarations[symbol.Start] = symbol;
        }

        var result = new List<CodeSemanticSpan>(Math.Min(Tokens.Count, Symbols.Count));
        var keywords = CodeLanguage.Get(Language).KeywordSet;
        for (var n = 0; n < Tokens.Count; n++)
        {
            if ((n & 255) == 0) cancellation.ThrowIfCancellationRequested();
            var t = Tokens[n];
            if (!t.Identifier || keywords.Contains(t.Text)) continue;
            if (Types.ContainsKey(t.Text) && (At(n - 1) is "class" or "struct" or "interface" or "record" or "enum"
                                                  or "type" or "new" or "as" or "is" ||
                                              Tokens.ElementAtOrDefault(n + 1)?.Identifier == true))
            {
                result.Add(new CodeSemanticSpan(t.Start, t.Text.Length, CodeSemanticKind.Type));
                continue;
            }

            Symbol? symbol = null;
            if (declarations.TryGetValue(t.Start, out var declaration) && declaration.Name == t.Text)
            {
                symbol = declaration;
            }
            else if (MemberReceiver(n - 1, out var receiver))
            {
                var type = RootType(ReceiverType(receiver, t.Start));
                if (_byOwner.TryGetValue(type, out var members))
                    foreach (var member in members)
                        if (member.Name == t.Text)
                        {
                            symbol = member;
                            break;
                        }

                // An unresolved member must not bind to an unrelated local variable.
                if (symbol == null) continue;
            }
            else
            {
                symbol = Binding(t.Text, t.Start + 1);
                if (symbol == null && At(n + 1) is "(" or "<" && _byName.TryGetValue(t.Text, out var methods))
                    foreach (var method in methods)
                        if (method.Signature != null && Visible(method, t.Start))
                        {
                            symbol = method;
                            break;
                        }
            }

            CodeSemanticKind kind;
            if (symbol != null)
                kind = symbol.Signature != null
                    ? CodeSemanticKind.Method
                    : symbol.Owner != null
                        ? At(Before(symbol.Start + 1) + 1) == "{" ? CodeSemanticKind.Property : CodeSemanticKind.Field
                        : _functionScopes.ContainsKey(symbol.Start)
                            ? CodeSemanticKind.Parameter
                            : CodeSemanticKind.Variable;
            else if (Types.ContainsKey(t.Text)) kind = CodeSemanticKind.Type;
            else continue;
            result.Add(new CodeSemanticSpan(t.Start, t.Text.Length, kind));
        }

        cancellation.ThrowIfCancellationRequested();
        SemanticColors = new CodeSemanticColors(result.ToArray());
    }
}