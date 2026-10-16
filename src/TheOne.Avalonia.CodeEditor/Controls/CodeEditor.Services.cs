using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace TheOne.Avalonia.Controls;

public partial class CodeEditor
{
    private int _completionStart, _completionRevision, _completionCaret;
    private CodeType _completionType;
    public bool IsCompletionOpen => _completion.IsVisible;
    public IReadOnlyList<CodeCompletionItem> CompletionItems { get; private set; } = [];

    // A preview only: it never enters the document, selection, clipboard or undo history.
    internal string InlineCompletionText
    {
        get
        {
            if (!IsInlineCompletionEnabled || !IsCompletionEnabled || !IsTabCompletionEnabled || !CanEdit ||
                SelectionLength != 0) return "";
            var line = Document.Lines[Document.LineAt(_caret)];
            if (!string.IsNullOrWhiteSpace(line.Text[(_caret - line.Start)..])) return "";
            var pending = _pendingSnippet;
            var item = pending?.Item ??
                       (_completion.IsVisible ? _completionList.SelectedItem as CodeCompletionItem : null);
            var start = pending?.Start ?? _completionStart;
            if (item == null || (pending == null && item.Snippet != null) || start > _caret ||
                start < line.Start) return "";
            var prefix = Document.Text[start.._caret];
            if (prefix.Length == 0 ||
                !item.InsertText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
            var tail = item.InsertText[prefix.Length..];
            var end = tail.IndexOfAny(['\r', '\n']);
            if (end >= 0) tail = tail[..end];
            return tail.Length > 160 ? tail[..160] : tail;
        }
    }

    internal Rect SignatureBounds => new(Canvas.GetLeft(_signature), Canvas.GetTop(_signature), _signature.Bounds.Width,
        _signature.Bounds.Height);

    internal Rect CompletionBounds => new(Canvas.GetLeft(_completion), Canvas.GetTop(_completion),
        _completion.Bounds.Width, _completion.Bounds.Height);

    private Control CompletionRow(CodeCompletionItem? item)
    {
        var row = new Grid
            { ColumnDefinitions = new ColumnDefinitions("16,*,Auto"), ColumnSpacing = 5, MinHeight = 22 };
        if (item == null) return row;
        var icon = new TextBlock
        {
            Text = item.Kind switch
            {
                CodeCompletionKind.Method => "ƒ", CodeCompletionKind.Property => "◇",
                CodeCompletionKind.Type => "□", CodeCompletionKind.Snippet => "{}",
                CodeCompletionKind.Keyword => "K", _ => "·"
            },
            FontSize = 12, Foreground = ResourceBrush("CodeEditor.Accent"), VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
            { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var prefix = Document.Text[Math.Clamp(_completionStart, 0, _caret).._caret];
        var match = prefix.Length == 0 ? -1 : item.Label.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (match < 0)
        {
            name.Text = item.Label;
        }
        else
        {
            name.Inlines!.Add(new Run(item.Label[..match]));
            name.Inlines.Add(new Run(item.Label.Substring(match, prefix.Length))
                { Foreground = ResourceBrush("CodeEditor.Accent"), FontWeight = FontWeight.Bold });
            name.Inlines.Add(new Run(item.Label[(match + prefix.Length)..]));
        }

        var detail = new TextBlock
        {
            Text = item.Detail, FontSize = 11, Opacity = .65, MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(detail, 2);
        row.Children.Add(icon);
        row.Children.Add(name);
        row.Children.Add(detail);
        ToolTip.SetTip(row,
            new TextBlock { Text = item.Detail ?? item.Label, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        return row;
    }

    /// <summary>Formats an immutable snapshot off the UI thread. Stale results never overwrite newer edits.</summary>
    public async Task<bool> FormatDocumentAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit || Document.Text.Length == 0 || IsFormatting) return false;
        _formatCancellation?.Dispose();
        _formatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _formatCancellation.Token;
        var revision = Document.Revision;
        var type = CodeType;
        var context = new CodeFormattingContext(Document.Text, type, TabSize, ConvertTabsToSpaces, Document.NewLine);
        SetAndRaise(FormattingErrorProperty, ref _formattingError, null);
        PseudoClasses.Set(":format-error", false);
        SetAndRaise(IsFormattingProperty, ref _isFormatting, true);
        UpdateCommands();
        try
        {
            var result = Formatter is { } formatter
                ? await formatter.FormatAsync(context, token)
                : await Task.Run(() => CodeFormatting.Format(context, token), token);
            if (token.IsCancellationRequested || revision != Document.Revision || type != CodeType || !CanEdit)
                return false;
            if (result == null) throw new InvalidOperationException("Formatter returned null.");
            if (result != Document.Text) Edit(0, Document.Text.Length, result, Math.Min(_caret, result.Length));
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception error)
        {
            SetAndRaise(FormattingErrorProperty, ref _formattingError, error.Message);
            PseudoClasses.Set(":format-error", true);
            return false;
        }
        finally
        {
            SetAndRaise(IsFormattingProperty, ref _isFormatting, false);
            UpdateCommands();
        }
    }

    /// <summary>Requests document symbols, members, methods, keywords and snippets, or a custom provider.</summary>
    public async Task<bool> RequestCompletionAsync(CancellationToken cancellationToken = default)
    {
        // Recompute every request, but retain an identical built-in popup's rows.
        var retain = CompletionProvider == null && _completion.IsVisible && _completionRevision == Document.Revision &&
                     _completionCaret == _caret && _completionType == CodeType &&
                     !cancellationToken.IsCancellationRequested;
        if (retain)
        {
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
            _completionCancellation?.Dispose();
            _completionCancellation = null;
        }
        else
        {
            CloseCompletion();
        }

        if (!CanEdit || !IsCompletionEnabled || SelectionLength > 0 || InsideLiteral(_caret))
        {
            CloseCompletion();
            return false;
        }

        var start = _caret;
        while (start > 0 && (CodeDocument.IsIdentifier(Document.Text[start - 1]) ||
                             (CodeType is CodeType.Css or CodeType.PowerShell && Document.Text[start - 1] == '-') ||
                             (CodeLanguage.Get(CodeType).Markup && Document.Text[start - 1] is '-' or ':' or '.')))
            start--;
        var prefix = Document.Text[start.._caret];
        _completionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _completionCancellation.Token;
        var revision = Document.Revision;
        var caret = _caret;
        var type = CodeType;
        SetAndRaise(CompletionErrorProperty, ref _completionError, null);
        try
        {
            // Large documents show their incrementally maintained word/keyword index
            // immediately. Semantic enrichment then replaces the same popup only if
            // the request, selection and snapshot are still current.
            if (CompletionProvider == null && Document.Text.Length > 100_000 &&
                !CodeCompletionRules.TypeContext(Document, type, caret, prefix) &&
                (start == 0 || Document.Text[start - 1] is not ('.' or ':' or '>')) && !CodeLanguage.Get(type).Markup &&
                type != CodeType.Css && _analysisTask?.IsCompletedSuccessfully != true)
            {
                var preliminary = CodeCompletions.Get(Document, type, prefix);
                if (preliminary.Count > 0)
                {
                    ShowCompletions(preliminary, start, revision);
                    _ = EnrichCompletionsAsync(start, revision, caret, type, prefix, token);
                    return true;
                }
            }

            var items = CompletionProvider is { } provider
                ? await provider.GetCompletionsAsync(new CodeCompletionContext(Document.Text, caret, type, prefix),
                    token)
                : await BuiltinCompletionsAsync(caret, prefix, token);
            if (token.IsCancellationRequested || revision != Document.Revision || _caret != caret || type != CodeType ||
                !CanEdit || !IsCompletionEnabled) return false;
            return ShowCompletions(items ?? [], start, revision);
        }
        catch (OperationCanceledException)
        {
            if (_completionCancellation is { } current && current.Token == token) CloseCompletion();
            return false;
        }
        catch (Exception error)
        {
            if (_completionCancellation is { } current && current.Token == token)
            {
                CloseCompletion();
                SetAndRaise(CompletionErrorProperty, ref _completionError, error.Message);
            }

            return false;
        }
    }

    private bool ShowCompletions(IReadOnlyList<CodeCompletionItem> items, int start, int revision)
    {
        var selected = (_completionList.SelectedItem as CodeCompletionItem)?.Label;
        var prefix = Document.Text[start.._caret];
        var filtered = items.Where(item =>
            item != null && !string.IsNullOrEmpty(item.Label) && item.InsertText != null &&
            CodeCompletionRules.Score(item.Label, prefix) < int.MaxValue).Take(100).ToArray();
        if (filtered.Length == 0)
        {
            CloseCompletion();
            return false;
        }

        if (_completion.IsVisible && _completionStart == start && _completionRevision == revision &&
            _completionCaret == _caret && _completionType == CodeType && CompletionItems.SequenceEqual(filtered))
        {
            PositionCompletion();
            return true;
        }

        CompletionItems = filtered;
        _completionStart = start;
        _completionRevision = revision;
        _completionCaret = _caret;
        _completionType = CodeType;
        _completionList.ItemsSource = CompletionItems;
        _completionList.SelectedIndex =
            Math.Max(0, Array.FindIndex(CompletionItems.ToArray(), i => i.Label == selected));
        _completion.IsVisible = true;
        PositionCompletion();
        View.InvalidateVisual();
        return true;
    }

    private async Task EnrichCompletionsAsync(int start, int revision, int caret, CodeType type, string prefix,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(30, token);
            var items = await BuiltinCompletionsAsync(caret, prefix, token);
            if (!token.IsCancellationRequested && revision == Document.Revision && caret == _caret &&
                type == CodeType && _completion.IsVisible && CanEdit && IsCompletionEnabled)
                ShowCompletions(items, start, revision);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                SetAndRaise(CompletionErrorProperty, ref _completionError, error.Message);
        }
    }

    private async Task<IReadOnlyList<CodeCompletionItem>> BuiltinCompletionsAsync(int caret, string prefix,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var analysis = await AnalysisAsync().WaitAsync(token);
        return await Task.Run(() =>
        {
            using var work = CodeWorkProbe.Start("completion", token);
            return analysis.Complete(caret, prefix, token);
        }, token);
    }

    /// <summary>Commits a displayed item as one undoable edit.</summary>
    public bool AcceptCompletion(CodeCompletionItem item)
    {
        if (!CanEdit || !_completion.IsVisible || _completionRevision != Document.Revision ||
            !CompletionItems.Contains(item)) return false;
        return InsertCompletion(item, _completionStart);
    }

    private bool InsertCompletion(CodeCompletionItem item, int start)
    {
        var length = _caret - start;
        var insertion = item.InsertText;
        var caretOffset = item.CaretOffset < 0 ? insertion.Length : Math.Clamp(item.CaretOffset, 0, insertion.Length);
        if (item.Kind == CodeCompletionKind.Method && insertion.EndsWith("()", StringComparison.Ordinal) &&
            _caret < Document.Text.Length && Document.Text[_caret] == '(')
        {
            insertion = insertion[..^2];
            caretOffset = insertion.Length + 1;
        }

        var line = Document.Lines[Document.LineAt(start)];
        var indent = new string(line.Text.TakeWhile(c => c is ' ' or '\t').ToArray());

        string Format(string value)
        {
            if (CompletionProvider != null) return value;
            var lines = value.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                var spaces = lines[i].TakeWhile(c => c == ' ').Count();
                lines[i] = indent + string.Concat(Enumerable.Repeat(IndentUnit, spaces / 4)) +
                           lines[i][(spaces / 4 * 4)..];
            }

            return string.Join(Document.NewLine, lines);
        }

        caretOffset = Format(insertion[..Math.Min(caretOffset, insertion.Length)]).Length +
                      Math.Max(0, caretOffset - insertion.Length);
        insertion = Format(insertion);
        Edit(start, length, insertion, start + caretOffset);
        if (item.Kind == CodeCompletionKind.Method && insertion.EndsWith("()", StringComparison.Ordinal))
            _autoPairs[start + insertion.Length - 1] = ')';
        if (caretOffset > 0 && caretOffset < insertion.Length &&
            (insertion[caretOffset - 1], insertion[caretOffset]) is ('(', ')') or ('[', ']') or ('{', '}') or ('"', '"')
            or ('\'', '\''))
            _autoPairs[start + caretOffset] = insertion[caretOffset];
        BeginSnippet(item, start, Format);
        FocusEditor();
        return true;
    }

    internal void CloseCompletion()
    {
        _completionTimer.Stop();
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        _completionCancellation = null;
        _completion.IsVisible = false;
        CompletionItems = [];
        _completionList.ItemsSource = null;
        View.InvalidateVisual();
    }

    private async Task CompleteOnTabAsync()
    {
        var revision = Document.Revision;
        var caret = _caret;
        var opened = await RequestCompletionAsync();
        if (revision != Document.Revision || caret != _caret || !CanEdit) return;
        if (opened && _completionList.SelectedItem is CodeCompletionItem item &&
            item.InsertText != Document.Text[_completionStart.._caret])
        {
            if (item.Snippet != null) ArmSnippet(item, _completionStart);
            else AcceptCompletion(item);
            return;
        }

        CloseCompletion();
        IndentSelection();
    }

    internal void PositionCompletion()
    {
        PositionSignature();
        if (_completion.IsVisible) PlaceOverlay(_completion, 300, false);
    }

    // Canvas coordinates never participate in child measurement. In particular,
    // the previous Y offset must not be counted as the next popup's height.
    private void PlaceOverlay(Border overlay, double preferredWidth, bool preferAbove)
    {
        var viewport = new Rect(_completionLayer.Bounds.Size);
        var origin = View.TranslatePoint(default, _completionLayer) ?? default;
        var caret = View.CaretRectangle.Translate(new Vector(origin.X, origin.Y));
        if (viewport.Width <= 8 || viewport.Height <= 8) return;
        var visible = caret.Bottom >= 0 && caret.Top < viewport.Height;
        overlay.Opacity = visible ? 1 : 0;
        overlay.IsHitTestVisible = visible && overlay != _signature;
        if (!visible) return;
        var width = Math.Min(preferredWidth, viewport.Width - 8);
        if (overlay == _signature)
        {
            _signatureText.Measure(new Size(Math.Max(1, width - 18), 150));
            width = Math.Min(width, Math.Max(80, _signatureText.DesiredSize.Width + 18));
        }

        overlay.Width = width;
        overlay.Margin = default;
        overlay.MaxHeight = Math.Max(20, Math.Min(overlay == _signature ? 160 : 238, viewport.Height - 8));
        overlay.Measure(new Size(width, overlay.MaxHeight));
        var height = Math.Min(overlay.DesiredSize.Height, viewport.Height - 8);
        var above = caret.Top - 4;
        var below = viewport.Height - caret.Bottom - 4;
        var useAbove = preferAbove ? above >= height || above >= below : below < height && above > below;
        var edge = useAbove ? caret.Top - 4 : caret.Bottom + 4;
        if (overlay == _completion && _signature.IsVisible && _signature.Opacity > 0)
        {
            var otherTop = Canvas.GetTop(_signature);
            var otherBottom = otherTop + _signature.DesiredSize.Height;
            if (useAbove && otherBottom <= caret.Top) edge = Math.Min(edge, otherTop - 4);
            if (!useAbove && otherTop >= caret.Bottom) edge = Math.Max(edge, otherBottom + 4);
        }

        var available = useAbove ? edge - 4 : viewport.Height - edge - 4;
        if (available < 20)
        {
            overlay.Opacity = 0;
            overlay.IsHitTestVisible = false;
            return;
        }

        overlay.MaxHeight = Math.Min(overlay.MaxHeight, available);
        overlay.Measure(new Size(width, overlay.MaxHeight));
        height = overlay.DesiredSize.Height;
        var y = useAbove ? edge - height : edge;
        Canvas.SetLeft(overlay, Math.Clamp(caret.X, 4, Math.Max(4, viewport.Width - width - 4)));
        Canvas.SetTop(overlay, Math.Clamp(y, 4, Math.Max(4, viewport.Height - height - 4)));
    }
}