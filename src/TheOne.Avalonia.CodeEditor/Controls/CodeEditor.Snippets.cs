using System.Text;

namespace TheOne.Avalonia.Controls;

public partial class CodeEditor
{
    private bool _editingSnippet;
    private PendingSnippet? _pendingSnippet;
    private SnippetSession? _snippetSession;

    internal void CancelSnippet()
    {
        _pendingSnippet = null;
        _snippetSession = null;
    }

    private bool HandleSnippetTab(bool backwards)
    {
        if (!CanEdit || !IsCompletionEnabled || !IsTabCompletionEnabled) return false;
        if (_snippetSession is { } session)
        {
            if (!session.Contains(SelectionStart, SelectionLength))
            {
                _snippetSession = null;
                return false;
            }

            session.Index = Math.Clamp(session.Index + (backwards ? -1 : 1), 0, session.Groups.Length - 1);
            SelectSnippetField(session);
            return true;
        }

        if (backwards || SelectionLength > 0 || InsideLiteral(_caret)) return false;
        if (_pendingSnippet is { } pending)
        {
            _pendingSnippet = null;
            if (pending.Revision == Document.Revision && pending.Caret == _caret && pending.Language == CodeType)
                return InsertCompletion(pending.Item, pending.Start);
        }

        if (_completion.IsVisible)
        {
            if (_completionList.SelectedItem is CodeCompletionItem { Snippet: not null } selected)
            {
                ArmSnippet(selected, _completionStart);
                return true;
            }

            return false;
        }

        var start = _caret;
        while (start > 0 && CodeDocument.IsIdentifier(Document.Text[start - 1])) start--;
        if (start == _caret || (start > 0 && Document.Text[start - 1] is '.' or ':' or '>')) return false;
        var prefix = Document.Text[start.._caret];
        var owner = _analysisTask?.IsCompletedSuccessfully == true && _analysisRevision == Document.Revision
            ? _analysisTask.Result.ContainingType(_caret)
            : null;
        if (owner == null && CodeType == CodeType.CSharp && prefix == "ctor")
            owner = CodeSnippets.EnclosingType(Document, _caret);
        var item = CodeSnippets.Get(CodeType, owner).FirstOrDefault(i => i.Label == prefix);
        if (item == null) return false;
        ArmSnippet(item, start);
        return true;
    }

    private void ArmSnippet(CodeCompletionItem item, int start)
    {
        var shortcut = item.Label;
        if (!Document.Text.AsSpan(start, _caret - start).SequenceEqual(shortcut)) Edit(start, _caret - start, shortcut);
        CloseCompletion();
        _pendingSnippet = new PendingSnippet(item, start, _caret, Document.Revision, CodeType);
        View.InvalidateVisual();
    }

    private void BeginSnippet(CodeCompletionItem item, int start, Func<string, string> format)
    {
        if (item.Snippet is not { Fields.Count: > 0 } definition) return;
        var fields = definition.Fields.Select(f => new CodeSnippetField(f.Group,
                start + format(item.InsertText[..f.Start]).Length,
                format(item.InsertText[..(f.Start + f.Length)]).Length - format(item.InsertText[..f.Start]).Length))
            .ToArray();
        var session = new SnippetSession(fields);
        _snippetSession = session;
        SelectSnippetField(session);
    }

    private void SelectSnippetField(SnippetSession session)
    {
        var field = session.Current;
        if (field.Group == 0) _snippetSession = null;
        _editingSnippet = true;
        try
        {
            Select(field.Start, field.Length);
        }
        finally
        {
            _editingSnippet = false;
        }
    }

    private bool EditSnippet(int start, int length, string insertion)
    {
        if (_editingSnippet || _snippetSession is not { } session) return false;
        if (!session.Contains(start, length) || insertion.Contains('\n') || insertion.Contains('\r'))
        {
            _snippetSession = null;
            return false;
        }

        var active = session.Current;
        var offset = start - active.Start;
        var value = Document.Text.Substring(active.Start, active.Length).Remove(offset, length)
            .Insert(offset, insertion);
        var copies = session.Fields.Where(f => f.Group == active.Group).OrderBy(f => f.Start).ToArray();
        var first = copies[0].Start;
        var last = copies[^1].Start + copies[^1].Length;
        var text = new StringBuilder(Document.Text.Substring(first, last - first));
        for (var i = copies.Length - 1; i >= 0; i--)
        {
            text.Remove(copies[i].Start - first, copies[i].Length);
            text.Insert(copies[i].Start - first, value);
        }

        var caret = active.Start + offset + insertion.Length;
        session.Fields = session.Fields.Select(f =>
        {
            var shift = copies.Where(c => c.Start < f.Start).Sum(c => value.Length - c.Length);
            return f with { Start = f.Start + shift, Length = f.Group == active.Group ? value.Length : f.Length };
        }).ToArray();
        _editingSnippet = true;
        try
        {
            Edit(first, last - first, text.ToString(), caret);
        }
        finally
        {
            _editingSnippet = false;
        }

        return true;
    }

    private sealed record PendingSnippet(
        CodeCompletionItem Item,
        int Start,
        int Caret,
        int Revision,
        CodeType Language);

    private sealed class SnippetSession(CodeSnippetField[] fields)
    {
        internal readonly int[] Groups = fields.Select(f => f.Group).Distinct().OrderBy(g => g == 0 ? int.MaxValue : g)
            .ToArray();

        internal CodeSnippetField[] Fields = fields;
        internal int Index;
        internal CodeSnippetField Current => Fields.First(f => f.Group == Groups[Index]);

        internal bool Contains(int start, int length)
        {
            return start >= Current.Start && start + length <= Current.Start + Current.Length;
        }
    }
}