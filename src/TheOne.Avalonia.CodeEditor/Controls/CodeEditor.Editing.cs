using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace TheOne.Avalonia.Controls;

public partial class CodeEditor
{
    private readonly Dictionary<int, char> _autoPairs = [];
    private readonly List<CodeEdit> _undo = [], _redo = [];
    private long _lastTyping;

    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, Document.Text.Length);
        SetSelection(start, (int)Math.Clamp((long)start + Math.Max(0, length), start, Document.Text.Length));
    }

    private int Boundary(int index)
    {
        index = Math.Clamp(index, 0, Document.Text.Length);
        if (index > 0 && index < Document.Text.Length && char.IsHighSurrogate(Document.Text[index - 1]) &&
            char.IsLowSurrogate(Document.Text[index])) index--;
        if (index > 0 && index < Document.Text.Length && Document.Text[index - 1] == '\r' &&
            Document.Text[index] == '\n') index--;
        return index;
    }

    internal void SetSelection(int anchor, int caret, bool ensureVisible = true, bool typing = false)
    {
        _pendingSnippet = null;
        if (!_editingSnippet && _snippetSession is { } session &&
            !session.Contains(Math.Min(anchor, caret), Math.Abs(caret - anchor))) _snippetSession = null;
        var oldStart = SelectionStart;
        var oldLength = SelectionLength;
        var oldCaret = _caret;
        Anchor = Boundary(anchor);
        _caret = Boundary(caret);
        RaisePropertyChanged(CaretIndexProperty, oldCaret, _caret);
        RaisePropertyChanged(SelectionStartProperty, oldStart, SelectionStart);
        RaisePropertyChanged(SelectionLengthProperty, oldLength, SelectionLength);
        var line = Document.LineAt(_caret);
        SetAndRaise(CaretLineProperty, ref _caretLine, line + 1);
        SetAndRaise(CaretColumnProperty, ref _caretColumn, _caret - Document.Lines[line].Start + 1);
        UpdateDiagnosticLine();
        if (!typing) _lastTyping = 0;
        CloseCompletion();
        View.InputChanged();
        ScheduleSignature();
        ToolTip.SetIsOpen(View, false);
        if (ensureVisible) View.EnsureCaretVisible();
        UpdateCommands();
    }

    /// <summary>Replaces the current selection as one undoable edit; respects read-only mode.</summary>
    public void ReplaceSelection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (CanEdit) Edit(SelectionStart, SelectionLength, text);
    }

    private void Edit(int start, int length, string insertion, int? newCaret = null, bool typing = false,
        int? newAnchor = null)
    {
        if (!CanEdit) return;
        if (EditSnippet(start, length, insertion)) return;
        start = Boundary(start);
        length = Math.Clamp(length, 0, Document.Text.Length - start);
        var end = Boundary(start + length);
        length = end - start;
        if (length == 0 && insertion.Length == 0) return;
        var after = newCaret ?? start + insertion.Length;
        var anchorAfter = newAnchor ?? after;
        var edit = new CodeEdit(start, Document.Text.Substring(start, length), insertion, Anchor, _caret, anchorAfter,
            after);
        var now = Environment.TickCount64;
        if (typing && length == 0 && insertion.Length == 1 && _undo.Count > 0 && now - _lastTyping < 700 &&
            _undo[^1] is var previous && previous.Removed.Length == 0 && previous.Inserted.Length < 256 &&
            previous.Start + previous.Inserted.Length == start && previous.CaretAfter == _caret)
            _undo[^1] = previous with
            {
                Inserted = previous.Inserted + insertion, AnchorAfter = after, CaretAfter = after
            };
        else _undo.Add(edit);
        while (_undo.Count > 1 &&
               (_undo.Count > 1000 || _undo.Sum(e => (long)e.Removed.Length + e.Inserted.Length) > 8_000_000))
            _undo.RemoveAt(0);
        _redo.Clear();
        ApplyEdit(start, length, insertion, anchorAfter, after, typing);
        _lastTyping = typing ? now : 0;
    }

    private void ApplyEdit(int start, int length, string insertion, int anchor, int caret, bool typing = false)
    {
        CancelIntelligenceBeforeEdit();
        var oldCount = LineCount;
        foreach (var pair in _autoPairs.ToArray())
        {
            _autoPairs.Remove(pair.Key);
            if (pair.Key < start) _autoPairs[pair.Key] = pair.Value;
            else if (pair.Key >= start + length) _autoPairs[pair.Key + insertion.Length - length] = pair.Value;
        }

        var first = Document.Replace(start, length, insertion);
        InvalidateAnalysis();
        CodeSyntax.Update(Document, CodeType, first);
        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, Document.Text);
        }
        finally
        {
            _syncing = false;
        }

        RaisePropertyChanged(LineCountProperty, oldCount, LineCount);
        View.DocumentChanged();
        SetSelection(anchor, caret, typing: typing);
    }

    public void Undo()
    {
        CancelSnippet();
        if (!CanEdit || _undo.Count == 0) return;
        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(edit);
        ApplyEdit(edit.Start, edit.Inserted.Length, edit.Removed, edit.AnchorBefore, edit.CaretBefore);
    }

    public void Redo()
    {
        CancelSnippet();
        if (!CanEdit || _redo.Count == 0) return;
        var edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(edit);
        ApplyEdit(edit.Start, edit.Removed.Length, edit.Inserted, edit.AnchorAfter, edit.CaretAfter);
    }

    internal int NextElement(int index)
    {
        if (index >= Document.Text.Length) return Document.Text.Length;
        if (Document.Text[index] == '\r' && index + 1 < Document.Text.Length && Document.Text[index + 1] == '\n')
            return index + 2;
        return index + StringInfo.GetNextTextElementLength(Document.Text.AsSpan(index));
    }

    internal int PreviousElement(int index)
    {
        if (index <= 0) return 0;
        var line = Document.Lines[Document.LineAt(index)];
        if (index == line.Start)
            return Document.Lines[Math.Max(0, Document.LineAt(index) - 1)] is var previous
                ? previous.Start + previous.Text.Length
                : 0;
        var start = line.Start;
        while (start < index)
        {
            var next = NextElement(start);
            if (next >= index) break;
            start = next;
        }

        return start;
    }

    internal void InputText(string text)
    {
        if (!CanEdit || text.Length == 0) return;
        View.ClearPreedit();
        if (_snippetSession != null)
        {
            Edit(SelectionStart, SelectionLength, text);
            return;
        }

        if (text.Length == 1 && !AutoInsertPairs && TryAlignCloser(text[0])) return;
        if (text.Length == 1 && AutoInsertPairs)
        {
            var c = text[0];
            if (SelectionLength == 0 && _autoPairs.TryGetValue(_caret, out var expected) && expected == c)
            {
                _autoPairs.Remove(_caret);
                CaretIndex = _caret + 1;
                return;
            }

            if (TryAlignCloser(c)) return;
            var closer = c switch
            {
                '(' => ')', '[' => ']', '{' => '}', '"' => '"', '\'' => '\'', '`' => '`', _ => '\0'
            };
            if (closer != '\0' && !InsideLiteral(_caret) && (SelectionLength > 0 || _caret == Document.Text.Length ||
                                                             char.IsWhiteSpace(Document.Text[_caret]) ||
                                                             ")]}:,;".Contains(Document.Text[_caret])))
            {
                var start = SelectionStart;
                var selected = SelectedText;
                Edit(start, SelectionLength, c + selected + closer, start + selected.Length + 1, newAnchor: start + 1);
                _autoPairs[start + selected.Length + 1] = closer;
                return;
            }
        }

        Edit(SelectionStart, SelectionLength, text, typing: text.Length == 1);
        if (IsCompletionEnabled && text.Length == 1 && (CodeDocument.IsIdentifier(text[0]) || text[0] == '.' ||
                                                        (text[0] == ':' && (CodeType == CodeType.Lua ||
                                                                            (_caret > 1 && Document.Text[_caret - 2] ==
                                                                                ':'))) ||
                                                        (text[0] == '>' && _caret > 1 &&
                                                         Document.Text[_caret - 2] == '-') ||
                                                        (text[0] == ' ' &&
                                                         CodeCompletionRules.TypeContext(Document, CodeType, _caret,
                                                             "")) ||
                                                        (CodeLanguage.Get(CodeType).Markup && text[0] is '<' or ' ')))
        {
            _completionTimer.Stop();
            _completionTimer.Start();
        }
    }

    private bool InsideLiteral(int offset)
    {
        var line = Document.Lines[Document.LineAt(offset)];
        var column = offset - line.Start;
        if (column == 0 && line.StartState.End != null) return true;
        return line.Tokens.Any(t =>
            t.Kind is CodeTokenKind.Comment or CodeTokenKind.String && column > t.Start &&
            column <= t.Start + t.Length);
    }

    internal void NewLine()
    {
        if (!CanEdit) return;
        var line = Document.Lines[Document.LineAt(SelectionStart)];
        var before = line.Text[..Math.Min(line.Text.Length, SelectionStart - line.Start)];
        var indent = new string(before.TakeWhile(c => c is ' ' or '\t').ToArray());
        if (IsAutoIndentEnabled && ExpandInlineBlock(line, SelectionStart - line.Start, indent)) return;
        var trimmed = before.TrimEnd();
        var extra = false;
        if (IsAutoIndentEnabled && !InsideLiteral(SelectionStart))
        {
            extra = trimmed.Length > 0 && "{[(".Contains(trimmed[^1]);
            if (CodeType == CodeType.Python && trimmed.EndsWith(':')) extra = true;
            if (CodeType == CodeType.Yaml && trimmed.EndsWith(':')) extra = true;
            if (CodeLanguage.Get(CodeType).Markup) extra = OpensMarkupBlock(trimmed);
            if (CodeType is CodeType.Ruby or CodeType.Lua or CodeType.Bash &&
                trimmed.Split(' ').Any(w => w is "do" or "then" or "def" or "function")) extra = true;
        }

        if (!IsAutoIndentEnabled) indent = "";
        var first = Document.NewLine + indent + (extra ? IndentUnit : "");
        var position = SelectionStart + first.Length;
        var remaining = Document.Text.AsSpan(SelectionStart + SelectionLength);
        if (extra && remaining.Length > 0 && ")]}".Contains(remaining[0])) first += Document.NewLine + indent;
        Edit(SelectionStart, SelectionLength, first, position);
    }

    /// <summary>Indents or outdents selected logical lines as one undo transaction.</summary>
    public void IndentSelection(bool outdent = false)
    {
        if (!CanEdit) return;
        if (SelectionLength == 0 && !outdent)
        {
            var line = Document.Lines[Document.LineAt(_caret)];
            var column = 0;
            foreach (var c in line.Text.AsSpan(0, Math.Min(line.Text.Length, _caret - line.Start)))
                column += c == '\t' ? TabSize - column % TabSize : 1;
            Edit(_caret, 0, ConvertTabsToSpaces ? new string(' ', TabSize - column % TabSize) : "\t");
            return;
        }

        var first = Document.LineAt(SelectionStart);
        var last = Document.LineAt(SelectionStart + SelectionLength);
        if (last > first && SelectionStart + SelectionLength == Document.Lines[last].Start) last--;
        var start = Document.Lines[first].Start;
        var end = Document.Lines[last].Start + Document.Lines[last].Text.Length;
        var builder = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            var line = Document.Lines[i];
            var remove = 0;
            if (outdent)
            {
                if (line.Text.StartsWith('\t')) remove = 1;
                else
                    while (remove < Math.Min(TabSize, line.Text.Length) && line.Text[remove] == ' ')
                        remove++;
                builder.Append(line.Text.AsSpan(remove));
            }
            else
            {
                builder.Append(IndentUnit).Append(line.Text);
            }

            if (i < last) builder.Append(line.Delimiter);
        }

        var value = builder.ToString();
        var hadSelection = SelectionLength > 0;
        var original = _caret;
        Edit(start, end - start, value,
            hadSelection
                ? start + value.Length
                : Math.Clamp(original + value.Length - (end - start), start, start + value.Length),
            newAnchor: hadSelection ? start : null);
    }

    public Task<bool> CopyAllAsync()
    {
        return WriteClipboardAsync(Document.Text);
    }

    public Task<bool> CopySelectionAsync()
    {
        return SelectionLength > 0 ? WriteClipboardAsync(SelectedText) : Task.FromResult(false);
    }

    private async Task<bool> WriteClipboardAsync(string value)
    {
        if (!IsEffectivelyEnabled || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
        try
        {
            await clipboard.SetTextAsync(value);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    public async Task CutAsync()
    {
        var revision = Document.Revision;
        var start = SelectionStart;
        var length = SelectionLength;
        if (CanEdit && length > 0 && await CopySelectionAsync() && CanEdit && revision == Document.Revision &&
            start == SelectionStart && length == SelectionLength) ReplaceSelection("");
    }

    public async Task PasteAsync()
    {
        if (!CanEdit || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var revision = Document.Revision;
        var start = SelectionStart;
        var length = SelectionLength;
        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (text != null && CanEdit && revision == Document.Revision && start == SelectionStart &&
                length == SelectionLength) ReplaceSelection(text);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException)
        {
        }
    }

    public bool FindNext(string text, bool matchCase = false, bool backwards = false, bool fromStart = false)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = fromStart ? 0 : backwards ? Math.Max(0, SelectionStart - 1) : SelectionStart + SelectionLength;
        var found = backwards
            ? Document.Text.LastIndexOf(text, Math.Min(start, Math.Max(0, Document.Text.Length - 1)), comparison)
            : Document.Text.IndexOf(text, Math.Min(start, Document.Text.Length), comparison);
        if (found < 0)
            found = backwards ? Document.Text.LastIndexOf(text, comparison) : Document.Text.IndexOf(text, comparison);
        if (found < 0) return false;
        Select(found, text.Length);
        return true;
    }

    private void OpenFind()
    {
        _find.IsVisible = true;
        if (SelectionLength is > 0 and < 100) _findBox.Text = SelectedText;
        _findBox.Focus();
        _findBox.SelectAll();
        CloseCompletion();
    }

    private void CloseFind()
    {
        _find.IsVisible = false;
        FocusEditor();
    }

    internal void HandleKey(KeyEventArgs e)
    {
        if (e.Handled) return;
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 &&
                      !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!command && e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift &&
            HandleSnippetTab(shift))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && (_pendingSnippet != null || _snippetSession != null))
        {
            CancelSnippet();
            CloseCompletion();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab) _pendingSnippet = null;
        if (command)
        {
            switch (e.Key)
            {
                case Key.A: Select(0, Document.Text.Length); break;
                case Key.C: CopyCommand.Execute(null); break;
                case Key.X: CutCommand.Execute(null); break;
                case Key.V: PasteCommand.Execute(null); break;
                case Key.Z:
                    if (shift) Redo();
                    else Undo();
                    break;
                case Key.Y: Redo(); break;
                case Key.F: OpenFind(); break;
                case Key.H: break;
                case Key.Space:
                    if (shift) _ = RequestSignatureHelpAsync();
                    else CompleteCommand.Execute(null);
                    break;
                case Key.Up when _signatureHelp is { Signatures.Count: > 1 } help:
                    SetAndRaise(SignatureHelpProperty, ref _signatureHelp,
                        help with
                        {
                            ActiveSignature = (help.ActiveSignature + help.Signatures.Count - 1) %
                                              help.Signatures.Count
                        });
                    RenderSignature();
                    break;
                case Key.Down when _signatureHelp is { Signatures.Count: > 1 } help:
                    SetAndRaise(SignatureHelpProperty, ref _signatureHelp,
                        help with { ActiveSignature = (help.ActiveSignature + 1) % help.Signatures.Count });
                    RenderSignature();
                    break;
                default: goto Navigation;
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift))
        {
            FormatCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8)
        {
            GoToNextDiagnostic(shift);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _signature.IsVisible)
        {
            ClearSignature();
            CloseCompletion();
            e.Handled = true;
            return;
        }

        if (_completion.IsVisible)
        {
            if (e.Key is Key.Up or Key.Down)
            {
                _completionList.SelectedIndex = Math.Clamp(_completionList.SelectedIndex + (e.Key == Key.Down ? 1 : -1),
                    0, CompletionItems.Count - 1);
                _completionList.ScrollIntoView(_completionList.SelectedItem!);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || (e.Key == Key.Tab && !shift && IsTabCompletionEnabled))
            {
                if (_completionList.SelectedItem is CodeCompletionItem item) AcceptCompletion(item);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab) CloseCompletion();
            if (e.Key == Key.Escape)
            {
                CloseCompletion();
                e.Handled = true;
                return;
            }
        }

        Navigation:
        var target = _caret;
        var moved = true;
        switch (e.Key)
        {
            case Key.Left:
                target = !shift && SelectionLength > 0
                    ? SelectionStart
                    :
                    OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                        ?
                        Document.Lines[Document.LineAt(_caret)].Start
                        : command || e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                            ? WordBoundary(-1)
                            : PreviousElement(_caret); break;
            case Key.Right:
                var currentLine = Document.Lines[Document.LineAt(_caret)];
                target = !shift && SelectionLength > 0
                    ? SelectionStart + SelectionLength
                    :
                    OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                        ?
                        currentLine.Start + currentLine.Text.Length
                        : command || e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                            ? WordBoundary(1)
                            : NextElement(_caret);
                break;
            case Key.Up: target = e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? 0 : View.MoveVertically(-1); break;
            case Key.Down:
                target = e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                    ? Document.Text.Length
                    : View.MoveVertically(1); break;
            case Key.PageUp:
                target = View.MoveVertically(-(int)Math.Max(1, View.Bounds.Height / LineHeight - 1)); break;
            case Key.PageDown:
                target = View.MoveVertically((int)Math.Max(1, View.Bounds.Height / LineHeight - 1)); break;
            case Key.Home: target = command ? 0 : Document.Lines[Document.LineAt(_caret)].Start; break;
            case Key.End:
                var line = Document.Lines[Document.LineAt(_caret)];
                target = command ? Document.Text.Length : line.Start + line.Text.Length;
                break;
            case Key.Enter:
                NewLine();
                moved = false;
                break;
            case Key.Tab:
                if (!shift && CanEdit && IsCompletionEnabled && IsTabCompletionEnabled && SelectionLength == 0)
                {
                    if (_autoPairs.ContainsKey(_caret))
                    {
                        CaretIndex = NextElement(_caret);
                        moved = false;
                        break;
                    }

                    if (!InsideLiteral(_caret) && _caret > 0 && (CodeDocument.IsIdentifier(Document.Text[_caret - 1]) ||
                                                                 Document.Text[_caret - 1] == '.'))
                    {
                        _ = CompleteOnTabAsync();
                        moved = false;
                        break;
                    }
                }

                IndentSelection(shift);
                moved = false;
                break;
            case Key.Back:
                if (CanEdit)
                {
                    if (SelectionLength > 0)
                    {
                        ReplaceSelection("");
                    }
                    else if (_caret > 0)
                    {
                        var previous = command ? WordBoundary(-1) : PreviousElement(_caret);
                        var pair = 0;
                        if (AutoInsertPairs && _caret - previous == 1 &&
                            _autoPairs.TryGetValue(_caret, out var closing))
                        {
                            var opening = closing switch { ')' => '(', ']' => '[', '}' => '{', _ => closing };
                            if (Document.Text[previous] == opening) pair = 1;
                        }

                        var indentation = Document.Lines[Document.LineAt(_caret)];
                        if (!command && _caret > indentation.Start && Document.Text
                                .AsSpan(indentation.Start, _caret - indentation.Start).IndexOfAnyExcept(' ') < 0)
                            previous = Math.Max(indentation.Start,
                                _caret - ((_caret - indentation.Start - 1) % TabSize + 1));
                        Edit(previous, _caret - previous + pair, "");
                    }
                }

                moved = false;
                break;
            case Key.Delete:
                if (CanEdit)
                {
                    if (SelectionLength > 0) ReplaceSelection("");
                    else if (_caret < Document.Text.Length)
                        Edit(_caret, (command ? WordBoundary(1) : NextElement(_caret)) - _caret, "");
                }

                moved = false;
                break;
            case Key.Escape:
                if (_find.IsVisible) CloseFind();
                CloseCompletion();
                moved = false;
                break;
            default: return;
        }

        if (moved) SetSelection(shift ? Anchor : target, target);
        e.Handled = true;
    }

    private int WordBoundary(int direction)
    {
        var offset = _caret;
        if (direction < 0)
        {
            while (offset > 0 && char.IsWhiteSpace(Document.Text[offset - 1])) offset = PreviousElement(offset);
            while (offset > 0 && CodeDocument.IsIdentifier(Document.Text[offset - 1])) offset = PreviousElement(offset);
            if (offset == _caret) offset = PreviousElement(offset);
        }
        else
        {
            while (offset < Document.Text.Length && CodeDocument.IsIdentifier(Document.Text[offset]))
                offset = NextElement(offset);
            while (offset < Document.Text.Length && char.IsWhiteSpace(Document.Text[offset]))
                offset = NextElement(offset);
            if (offset == _caret) offset = NextElement(offset);
        }

        return offset;
    }

    private void PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Source is not Visual source ||
            (source != View && !source.GetVisualAncestors().Contains(View))) return;
        var manager = TopLevel.GetTopLevel(this)?.FocusManager;
        var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? NavigationDirection.Previous
            : NavigationDirection.Next;
        IInputElement current = this;
        var visited = new HashSet<IInputElement> { current };
        while (manager?.FindNextElement(direction, new FindNextElementOptions { FocusedElement = current }) is
                   { } next && visited.Add(next))
        {
            if (!ReferenceEquals(next, this) &&
                (next is not Visual visual || !visual.GetVisualAncestors().Contains(this)) &&
                next.Focus(NavigationMethod.Tab)) break;
            current = next;
        }

        e.Handled = true;
    }
}