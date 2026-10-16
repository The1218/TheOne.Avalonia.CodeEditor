namespace TheOne.Avalonia.Controls;

// Keep untouched line records, layouts and lexical states alive across edits.
// Undo stores edit deltas; Text snapshots preserve the public two-way binding.
internal sealed class CodeDocument
{
    internal readonly SortedSet<string> CodeWords = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<CodeLine> Lines = [];
    internal readonly SortedSet<string> Words = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _codeWordCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _wordCounts = new(StringComparer.OrdinalIgnoreCase);

    internal CodeDocument(bool tracksWords = true)
    {
        TracksWords = tracksWords;
        Reset("");
    }

    internal bool TracksWords { get; }

    internal string Text { get; private set; } = "";
    internal int Revision { get; private set; }
    internal int MaxColumns { get; private set; }
    internal string NewLine => Lines.FirstOrDefault(l => l.Delimiter.Length > 0)?.Delimiter ?? "\n";

    internal void Reset(string text, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Text = text;
        Revision++;
        Lines.Clear();
        _wordCounts.Clear();
        Words.Clear();
        CodeWords.Clear();
        _codeWordCounts.Clear();
        Lines.AddRange(Split(text, cancellation, !TracksWords));
        if (TracksWords)
            foreach (var line in Lines)
            {
                cancellation.ThrowIfCancellationRequested();
                AddWords(line);
            }

        Index(0);
    }

    internal int LineAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        var low = 0;
        var high = Lines.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (Lines[mid].Start > offset) high = mid - 1;
            else low = mid + 1;
        }

        return Math.Max(0, high);
    }

    internal int Replace(int offset, int length, string insertion)
    {
        var first = LineAt(offset);
        var last = LineAt(offset + length);
        var start = Lines[first].Start;
        var end = Lines[last].Start + Lines[last].Text.Length + Lines[last].Delimiter.Length;
        var fragment = string.Concat(Text.AsSpan(start, offset - start), insertion.AsSpan(),
            Text.AsSpan(offset + length, end - offset - length));
        var replacement = Split(fragment);
        if (last + 1 < Lines.Count && replacement[^1].Text.Length == 0 && replacement[^1].Delimiter.Length == 0)
            replacement.RemoveAt(replacement.Count - 1);
        for (var i = first; i <= last; i++) RemoveWords(Lines[i]);
        Lines.RemoveRange(first, last - first + 1);
        Lines.InsertRange(first, replacement);
        foreach (var line in replacement) AddWords(line);
        Text = string.Concat(Text.AsSpan(0, offset), insertion.AsSpan(), Text.AsSpan(offset + length));
        Revision++;
        Index(first);
        return first;
    }

    private void Index(int first)
    {
        var offset = first == 0
            ? 0
            : Lines[first - 1].Start + Lines[first - 1].Length + Lines[first - 1].Delimiter.Length;
        MaxColumns = 0;
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (i >= first)
            {
                line.Start = offset;
                offset += line.Length + line.Delimiter.Length;
            }

            MaxColumns = Math.Max(MaxColumns, line.Length);
        }
    }

    private void AddWords(CodeLine line)
    {
        line.Words = Identifiers(line.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var word in line.Words)
        {
            _wordCounts.TryGetValue(word, out var count);
            _wordCounts[word] = count + 1;
            Words.Add(word);
        }
    }

    private void RemoveWords(CodeLine line)
    {
        RemoveCodeWords(line);
        foreach (var word in line.Words)
            if (_wordCounts[word] > 1)
            {
                _wordCounts[word]--;
            }
            else
            {
                _wordCounts.Remove(word);
                Words.Remove(word);
            }
    }

    internal void UpdateCodeWords(CodeLine line)
    {
        if (!TracksWords) return;
        RemoveCodeWords(line);
        line.CodeWords = line.Tokens.Where(t => t.Kind is CodeTokenKind.Plain or CodeTokenKind.Type)
            .SelectMany(t => Identifiers(line.Text.Substring(t.Start, t.Length)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var word in line.CodeWords)
        {
            _codeWordCounts[word] = _codeWordCounts.GetValueOrDefault(word) + 1;
            CodeWords.Add(word);
        }
    }

    private void RemoveCodeWords(CodeLine line)
    {
        foreach (var word in line.CodeWords)
            if (_codeWordCounts.GetValueOrDefault(word) > 1)
            {
                _codeWordCounts[word]--;
            }
            else
            {
                _codeWordCounts.Remove(word);
                CodeWords.Remove(word);
            }

        line.CodeWords = [];
    }

    internal static bool IsIdentifier(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or '$';
    }

    internal static IEnumerable<string> Identifiers(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            if (!char.IsLetter(text[i]) && text[i] is not ('_' or '$'))
            {
                i++;
                continue;
            }

            var start = i++;
            while (i < text.Length && IsIdentifier(text[i])) i++;
            if (i - start > 1) yield return text[start..i];
        }
    }

    private static List<CodeLine> Split(string text, CancellationToken cancellation = default, bool lazyText = false)
    {
        var result = new List<CodeLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            if (text[i] is not ('\r' or '\n')) continue;
            var end = i;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            var delimiter = text[end] == '\n' ? "\n" : i > end ? "\r\n" : "\r";
            result.Add(lazyText
                ? new CodeLine(text.AsMemory(start, end - start), delimiter)
                : new CodeLine(text[start..end], delimiter));
            start = i + 1;
        }

        result.Add(lazyText ? new CodeLine(text.AsMemory(start), "") : new CodeLine(text[start..], ""));
        return result;
    }
}

internal sealed class CodeLine
{
    private static readonly int[] SingleWrapStart = [0];
    private readonly ReadOnlyMemory<char> _source;
    internal string[] CodeWords = [];
    internal CodeHighlightSpan[] Highlights = [];
    internal int LexVersion;
    internal bool Lexed;
    internal int Start;
    internal CodeHighlightState StartHighlightState, EndHighlightState;
    internal CodeLexState StartState, EndState;
    internal CodeToken[] Tokens = [];
    internal string[] Words = [];
    internal int[] WrapStarts = SingleWrapStart;
    internal int WrapWidth, WrapTabSize, DisplayColumns;
    private string? _text;

    internal CodeLine(string text, string delimiter)
    {
        _text = text;
        Delimiter = delimiter;
    }

    internal CodeLine(ReadOnlyMemory<char> source, string delimiter)
    {
        _source = source;
        Delimiter = delimiter;
    }

    internal int Length => _text?.Length ?? _source.Length;

    internal bool IsTextMaterialized => _text != null;

    // UI lines are eager. Immutable analysis snapshots materialize only when
    // lexing reaches the row, so early cancellation does not copy the whole file.
    internal string Text => _text ??= _source.ToString();
    internal string Delimiter { get; }
}

internal readonly record struct CodeEdit(
    int Start,
    string Removed,
    string Inserted,
    int AnchorBefore,
    int CaretBefore,
    int AnchorAfter,
    int CaretAfter);