using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace TheOne.Avalonia.Controls;

internal readonly record struct CodeVisualRow(int Line, int Start, int Length);

// Only viewport rows are shaped. There are no controls per line/token and the
// shaped-layout cache is bounded independently of document size.
internal sealed class CodeTextSurface : Control, ILogicalScrollable
{
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly DispatcherTimer _dragScroll = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly InputClient _inputClient;
    private readonly Queue<LayoutKey> _layoutOrder = [];
    private readonly Dictionary<LayoutKey, RowLayout> _layouts = [];
    private readonly CodeEditor _owner;
    private readonly List<CodeVisualRow> _rows = [];
    internal double LastShapeMilliseconds, LastRenderMilliseconds;
    private bool _caretVisible = true, _dragging;
    private double _charWidth = 8, _gutterWidth, _extentWidth;
    private TextLayout[] _digits = [];
    private Point _dragPosition;
    private TextLayout? _inlineLayout;
    private string _inlineText = "";
    private int[] _lineRows = [];
    private Vector _offset;
    private string _preedit = "";
    private int _preeditCursor;
    private double? _preferredX;
    private IBrush[] _syntaxBrushes = [];
    private bool _verticalMovement;
    private Size _viewport;
    private int _wrapColumns;

    internal CodeTextSurface(CodeEditor owner)
    {
        _owner = owner;
        _inputClient = new InputClient(this);
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        AutomationProperties.SetName(this, "Code editor");
        TextInputMethodClientRequested += (_, e) => e.Client = _owner.CanEdit ? _inputClient : null;
        _blink.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
        _dragScroll.Tick += (_, _) =>
        {
            if (!_dragging)
            {
                _dragScroll.Stop();
                return;
            }

            if (_dragPosition.Y < 0 || _dragPosition.Y > Bounds.Height)
            {
                Offset += new Vector(0, _dragPosition.Y < 0 ? -_owner.LineHeight : _owner.LineHeight);
                _owner.SetSelection(_owner.Anchor, HitPosition(_dragPosition), false);
            }
        };
    }

    internal int LastRenderedRows { get; private set; }
    internal int CachedLayoutCount => _layouts.Count;
    internal int ShapedLayoutCount { get; private set; }
    internal IReadOnlyList<CodeVisualRow> Rows => _rows;
    internal TextInputMethodClient InputClientForVerification => _inputClient;

    private double TextLeft => _owner.Padding.Left + _gutterWidth;

    internal Rect CaretRectangle
    {
        get
        {
            var rowIndex = RowAtCaret();
            var row = _rows[rowIndex];
            var line = _owner.Document.Lines[row.Line];
            var layout = GetLayout(row);
            return new Rect(
                TextLeft + layout.XAt(Math.Clamp(_owner.CaretIndex - line.Start - row.Start, 0, row.Length)) -
                _offset.X, RowY(rowIndex), 1.5, _owner.LineHeight);
        }
    }

    public bool CanHorizontallyScroll { get; set; } = true;
    public bool CanVerticallyScroll { get; set; } = true;
    public bool IsLogicalScrollEnabled => true;
    public Size ScrollSize => new(_charWidth * 3, _owner.LineHeight * 3);

    public Size PageScrollSize => new(Math.Max(_charWidth, _viewport.Width - TextLeft),
        Math.Max(_owner.LineHeight, _viewport.Height - _owner.LineHeight));

    public Size Extent => new(Math.Max(_viewport.Width, _extentWidth),
        _rows.Count * _owner.LineHeight + _owner.Padding.Top + _owner.Padding.Bottom);

    public Size Viewport => _viewport;

    public Vector Offset
    {
        get => _offset;
        set
        {
            var previous = _offset;
            _offset = value;
            CoerceOffset();
            if (_offset == previous) return;
            _owner.DeferDiagnostics();
            InvalidateVisual();
            RaiseScrollInvalidated(EventArgs.Empty);
            _inputClient.CursorMoved();
            _owner.PositionCompletion();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public void RaiseScrollInvalidated(EventArgs e)
    {
        ScrollInvalidated?.Invoke(this, e);
    }

    public bool BringIntoView(Control target, Rect targetRect)
    {
        EnsureCaretVisible();
        return true;
    }

    public Control? GetControlInDirection(NavigationDirection direction, Control? from)
    {
        return null;
    }

    internal void ResetLayouts()
    {
        _inlineLayout?.Dispose();
        _inlineLayout = null;
        _inlineText = "";
        foreach (var layout in _layouts.Values) layout.Dispose();
        _layouts.Clear();
        _layoutOrder.Clear();
        using var metric = new TextLayout("M", new Typeface(_owner.FontFamily), _owner.FontSize, _owner.Foreground);
        _charWidth = Math.Max(1, metric.WidthIncludingTrailingWhitespace);
        foreach (var digit in _digits) digit.Dispose();
        _digits = Enumerable.Range(0, 10).Select(digit => new TextLayout(digit.ToString(CultureInfo.InvariantCulture),
            new Typeface(_owner.FontFamily), _owner.FontSize, _owner.ResourceBrush("CodeEditor.GutterForeground"),
            lineHeight: _owner.LineHeight)).ToArray();
        _syntaxBrushes = Enum.GetValues<CodeHighlightKind>().Select(kind =>
            kind == CodeHighlightKind.Plain
                ? _owner.Foreground ?? Brushes.Gray
                : _owner.ResourceBrush("CodeEditor." + kind)).Concat(Enum.GetValues<CodeSemanticKind>()
            .Where(kind => kind != CodeSemanticKind.Variable)
            .Select(kind => _owner.ResourceBrush("CodeEditor.Semantic" + kind))).ToArray();
        _wrapColumns = 0;
        RebuildRows();
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal void DocumentChanged()
    {
        RebuildRows();
        InvalidateVisual();
    }

    private void RebuildRows()
    {
        _gutterWidth = _owner.ShowLineNumbers
            ? (_owner.LineCount.ToString(CultureInfo.InvariantCulture).Length + 2) * _charWidth + 12
            : 0;
        var columns = _owner.WordWrap
            ? Math.Max(1, (int)Math.Floor(Math.Max(1, _viewport.Width - TextLeft - _owner.Padding.Right) / _charWidth))
            : int.MaxValue;
        _wrapColumns = columns;
        _rows.Clear();
        _lineRows = new int[_owner.LineCount];
        _extentWidth = 0;
        for (var i = 0; i < _owner.LineCount; i++)
        {
            var line = _owner.Document.Lines[i];
            _lineRows[i] = _rows.Count;
            if (line.WrapWidth != columns || line.WrapTabSize != _owner.TabSize)
            {
                var starts = new List<int> { 0 };
                var current = 0;
                var maximum = 0;
                for (var index = 0; index < line.Text.Length;)
                {
                    var length = ElementLength(line.Text, index);
                    var cells = line.Text[index] == '\t'
                        ? _owner.TabSize - current % _owner.TabSize
                        : CellWidth(line.Text, index);
                    if (current + cells > columns && index > starts[^1])
                    {
                        maximum = Math.Max(maximum, current);
                        starts.Add(index);
                        current = 0;
                        cells = line.Text[index] == '\t' ? _owner.TabSize : cells;
                    }

                    current += cells;
                    index += length;
                }

                line.WrapStarts = starts.ToArray();
                line.DisplayColumns = Math.Max(maximum, current);
                line.WrapWidth = columns;
                line.WrapTabSize = _owner.TabSize;
            }

            for (var j = 0; j < line.WrapStarts.Length; j++)
            {
                var start = line.WrapStarts[j];
                var end = j + 1 < line.WrapStarts.Length ? line.WrapStarts[j + 1] : line.Text.Length;
                _rows.Add(new CodeVisualRow(i, start, end - start));
            }

            _extentWidth = Math.Max(_extentWidth,
                line.DisplayColumns * _charWidth + TextLeft + _owner.Padding.Right + 4);
        }

        if (_owner.WordWrap) _extentWidth = _viewport.Width;
        CoerceOffset();
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    private static int ElementLength(string text, int index)
    {
        return text[index] < 0x300 && (index + 1 == text.Length || text[index + 1] < 0x300)
            ? 1
            : StringInfo.GetNextTextElementLength(text.AsSpan(index));
    }

    private static int CellWidth(string text, int index)
    {
        var code = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];
        return code >= 0x1100 && (code <= 0x115f || code is >= 0x2e80 and <= 0xa4cf ||
                                  code is >= 0xac00 and <= 0xd7a3 || code is >= 0xf900 and <= 0xfaff ||
                                  code is >= 0xfe10 and <= 0xfe6f || code is >= 0xff00 and <= 0xff60 ||
                                  code is >= 0x1f000)
            ? 2
            : 1;
    }

    private double RowY(int row)
    {
        return _owner.Padding.Top + row * _owner.LineHeight - _offset.Y;
    }

    private int RowAtCaret()
    {
        if (_lineRows.Length != _owner.LineCount || _rows.Count == 0) RebuildRows();
        var line = _owner.Document.LineAt(_owner.CaretIndex);
        var value = _owner.Document.Lines[line];
        var column = Math.Min(value.Text.Length, _owner.CaretIndex - value.Start);
        var index = Array.BinarySearch(value.WrapStarts, column);
        if (index < 0) index = Math.Max(0, ~index - 1);
        return Math.Clamp(_lineRows[line] + index, 0, _rows.Count - 1);
    }

    internal void EnsureCaretVisible()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0) return;
        var caret = CaretRectangle;
        var x = _offset.X;
        var y = _offset.Y;
        if (caret.Top < _owner.Padding.Top) y += caret.Top - _owner.Padding.Top;
        else if (caret.Bottom > _viewport.Height - _owner.Padding.Bottom)
            y += caret.Bottom - _viewport.Height + _owner.Padding.Bottom;
        if (!_owner.WordWrap)
        {
            if (caret.Left < TextLeft) x += caret.Left - TextLeft;
            else if (caret.Right > _viewport.Width - 8) x += caret.Right - _viewport.Width + 8;
        }

        Offset = new Vector(x, y);
    }

    internal void ScrollToLine(int line)
    {
        Offset = new Vector(_offset.X, _lineRows.Length > line ? _lineRows[line] * _owner.LineHeight : 0);
    }

    internal int MoveVertically(int delta)
    {
        var rect = CaretRectangle;
        _preferredX ??= rect.X + _offset.X;
        _verticalMovement = true;
        return HitPosition(new Point(_preferredX.Value - _offset.X,
            rect.Y + _owner.LineHeight / 2 + delta * _owner.LineHeight));
    }

    internal int HitPosition(Point point)
    {
        if (_rows.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Floor((point.Y + _offset.Y - _owner.Padding.Top) / _owner.LineHeight), 0,
            _rows.Count - 1);
        var row = _rows[index];
        var layout = GetLayout(row);
        return _owner.Document.Lines[row.Line].Start + row.Start + layout.RawAt(point.X + _offset.X - TextLeft);
    }

    public override void Render(DrawingContext context)
    {
        var renderStart = Stopwatch.GetTimestamp();
        LastShapeMilliseconds = 0;
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_rows.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var start = Math.Max(0, (int)Math.Floor((_offset.Y - _owner.Padding.Top) / _owner.LineHeight));
        var end = Math.Min(_rows.Count, start + (int)Math.Ceiling(Bounds.Height / _owner.LineHeight) + 2);
        LastRenderedRows = end - start;
        var selectionBrush = _owner.ResourceBrush("CodeEditor.TextSelection");
        using (context.PushClip(new Rect(new Point(TextLeft, 0),
                   new Size(Math.Max(0, Bounds.Width - TextLeft), Bounds.Height))))
        {
            for (var i = start; i < end; i++)
            {
                var row = _rows[i];
                var line = _owner.Document.Lines[row.Line];
                var y = RowY(i);
                var layout = GetLayout(row);
                var origin = new Point(TextLeft - _offset.X, y);
                if (_owner.IsCurrentLineHighlighted && row.Line == _owner.CaretLine - 1)
                    context.FillRectangle(_owner.ResourceBrush("CodeEditor.CurrentLineBackground"),
                        new Rect(TextLeft, y, Math.Max(0, Bounds.Width - TextLeft), _owner.LineHeight));
                var selectionStart = Math.Max(_owner.SelectionStart, line.Start + row.Start);
                var selectionEnd = Math.Min(_owner.SelectionStart + _owner.SelectionLength,
                    line.Start + row.Start + row.Length);
                if (selectionEnd > selectionStart)
                {
                    if (selectionStart == line.Start + row.Start && selectionEnd == line.Start + row.Start + row.Length)
                        context.FillRectangle(selectionBrush,
                            new Rect(origin.X, y, Math.Max(1, layout.Layout.WidthIncludingTrailingWhitespace),
                                _owner.LineHeight));
                    else
                        foreach (var bounds in layout.RangeBounds(selectionStart - line.Start - row.Start,
                                     selectionEnd - line.Start - row.Start))
                            context.FillRectangle(selectionBrush,
                                new Rect(origin.X + bounds.Rectangle.X, y, Math.Max(1, bounds.Rectangle.Width),
                                    _owner.LineHeight));
                }

                if (_owner.SelectionLength > 0 && _owner.SelectionStart <= line.Start + row.Start + row.Length &&
                    _owner.SelectionStart + _owner.SelectionLength > line.Start + row.Start + row.Length &&
                    row.Start + row.Length == line.Text.Length)
                    context.FillRectangle(selectionBrush,
                        new Rect(origin.X + layout.Layout.WidthIncludingTrailingWhitespace, y, _charWidth,
                            _owner.LineHeight));
                layout.Draw(context, origin, _owner.SemanticColors, _owner.SemanticColorVersion, _syntaxBrushes);
                foreach (var diagnostic in _owner.DiagnosticsForLine(row.Line))
                {
                    if (!RangeInRow(row, line, diagnostic.Start, diagnostic.Length, out var from, out var to,
                            out var insertion)) continue;
                    var pen = new Pen(_owner.ResourceBrush(diagnostic.Severity == CodeDiagnosticSeverity.Error
                        ? "CodeEditor.Danger"
                        : "CodeEditor.Accent"));
                    if (insertion)
                    {
                        var x = layout.XAt(row.Length) + origin.X;
                        DrawWave(context, pen, x, x + _charWidth, y);
                    }
                    else
                    {
                        foreach (var bounds in layout.RangeBounds(from - row.Start, to - row.Start))
                            DrawWave(context, pen, origin.X + bounds.Rectangle.Left, origin.X + bounds.Rectangle.Right,
                                y);
                    }
                }
            }

            if (IsFocused && _owner.IsEffectivelyEnabled)
            {
                var caret = CaretRectangle;
                var inline = _preedit.Length == 0 ? _owner.InlineCompletionText : "";
                if (_inlineText != inline)
                {
                    _inlineLayout?.Dispose();
                    _inlineText = inline;
                    _inlineLayout = inline.Length == 0
                        ? null
                        : new TextLayout(inline, new Typeface(_owner.FontFamily), _owner.FontSize, _owner.Foreground,
                            lineHeight: _owner.LineHeight);
                }

                if (_inlineLayout != null)
                    using (context.PushOpacity(.42))
                    {
                        _inlineLayout.Draw(context, caret.Position);
                    }

                if (_preedit.Length > 0)
                {
                    using var preedit = new TextLayout(_preedit, new Typeface(_owner.FontFamily), _owner.FontSize,
                        _owner.Foreground, lineHeight: _owner.LineHeight);
                    context.FillRectangle(_owner.ResourceBrush("CodeEditor.Surface"),
                        new Rect(caret.Position, new Size(Math.Max(2, preedit.Width), _owner.LineHeight)));
                    preedit.Draw(context, caret.Position);
                    context.DrawLine(new Pen(_owner.ResourceBrush("CodeEditor.Accent")),
                        new Point(caret.X, caret.Bottom - 2),
                        new Point(caret.X + preedit.Width, caret.Bottom - 2));
                }
                else if (_caretVisible && _owner.SelectionLength == 0)
                {
                    context.FillRectangle(_owner.ResourceBrush("CodeEditor.Accent"), caret);
                }
            }
        }

        if (_owner.ShowLineNumbers)
        {
            context.DrawLine(new Pen(_owner.ResourceBrush("CodeEditor.InputLine")), new Point(TextLeft - 8, 0),
                new Point(TextLeft - 8, Bounds.Height));
            Span<char> number = stackalloc char[12];
            for (var i = start; i < end; i++)
            {
                if (_rows[i].Start != 0) continue;
                (_rows[i].Line + 1).TryFormat(number, out var count, provider: CultureInfo.InvariantCulture);
                var x = TextLeft - 16;
                for (var j = count - 1; j >= 0; j--)
                {
                    var digit = _digits[number[j] - '0'];
                    x -= digit.Width;
                    digit.Draw(context, new Point(x, RowY(i)));
                }
            }
        }

        LastRenderMilliseconds = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
    }

    private RowLayout GetLayout(CodeVisualRow row)
    {
        var line = _owner.Document.Lines[row.Line];
        var key = new LayoutKey(line, row.Start, row.Length, line.LexVersion);
        if (_layouts.TryGetValue(key, out var cached)) return cached;
        var shapeStart = Stopwatch.GetTimestamp();
        var layout = new RowLayout(_owner, line, row.Start, row.Length, _syntaxBrushes);
        ShapedLayoutCount++;
        LastShapeMilliseconds += Stopwatch.GetElapsedTime(shapeStart).TotalMilliseconds;
        while (_layouts.Count >= 192 && _layoutOrder.TryDequeue(out var oldest))
            if (_layouts.Remove(oldest, out var removed))
                removed.Dispose();
        _layouts.Add(key, layout);
        _layoutOrder.Enqueue(key);
        return layout;
    }

    private bool RangeInRow(CodeVisualRow row, CodeLine line, int start, int length, out int first, out int end,
        out bool insertion)
    {
        first = Math.Max(row.Start, start - line.Start);
        end = Math.Min(row.Start + row.Length, start + length - line.Start);
        // End offsets are exclusive. Only the final row owns the EOF caret.
        insertion = start == _owner.Document.Text.Length && row.Line == _owner.LineCount - 1 &&
                    row.Start + row.Length == line.Text.Length;
        return end > first || insertion;
    }

    private void DrawWave(DrawingContext context, IPen pen, double left, double right, double y)
    {
        left = Math.Max(TextLeft, left);
        right = Math.Min(Bounds.Width, Math.Max(left + 3, right));
        for (var x = left; x < right; x += 4)
        {
            context.DrawLine(pen, new Point(x, y + _owner.LineHeight - 3),
                new Point(Math.Min(x + 2, right), y + _owner.LineHeight - 1));
            context.DrawLine(pen, new Point(Math.Min(x + 2, right), y + _owner.LineHeight - 1),
                new Point(Math.Min(x + 4, right), y + _owner.LineHeight - 3));
        }
    }

    // Uses the same range mapping as diagnostic rendering; also useful to verify
    // marker geometry without reverse-engineering antialiased pixel boundaries.
    internal IReadOnlyList<Rect> RangeRectangles(int start, int length)
    {
        var result = new List<Rect>();
        foreach (var row in _rows)
        {
            var line = _owner.Document.Lines[row.Line];
            if (line.Start + row.Start > start + length) break;
            if (!RangeInRow(row, line, start, length, out var first, out var end, out var insertion)) continue;
            var layout = GetLayout(row);
            var y = RowY(_lineRows[row.Line] + Array.IndexOf(line.WrapStarts, row.Start));
            var origin = TextLeft - _offset.X;
            if (insertion) result.Add(new Rect(origin + layout.XAt(row.Length), y, _charWidth, _owner.LineHeight));
            else
                foreach (var bounds in layout.RangeBounds(first - row.Start, end - row.Start))
                    result.Add(new Rect(origin + bounds.Rectangle.Left, y, bounds.Rectangle.Width, _owner.LineHeight));
        }

        return result;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 500,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 300);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var changed = _viewport != finalSize;
        _viewport = finalSize;
        if (changed)
        {
            RebuildRows();
            _inputClient.CursorMoved();
        }

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResetLayouts();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blink.Stop();
        _dragScroll.Stop();
        ClearPreedit();
        foreach (var layout in _layouts.Values) layout.Dispose();
        _inlineLayout?.Dispose();
        _inlineLayout = null;
        _inlineText = "";
        _layouts.Clear();
        _layoutOrder.Clear();
        foreach (var digit in _digits) digit.Dispose();
        _digits = [];
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _caretVisible = true;
        _blink.Start();
        InputChanged();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _blink.Stop();
        ClearPreedit();
        InvalidateVisual();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_owner.IsKeyboardFocusWithin)
            {
                _owner.CloseCompletion();
                _owner.CancelSnippet();
            }
        });
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            _owner.InputText(e.Text);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _owner.HandleKey(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        ClearPreedit();
        _owner.CloseCompletion();
        var point = e.GetPosition(this);
        var offset = HitPosition(point);
        if (e.ClickCount >= 3 || point.X < TextLeft - 8)
        {
            var line = _owner.Document.Lines[_owner.Document.LineAt(offset)];
            _owner.Select(line.Start, line.Text.Length + line.Delimiter.Length);
        }
        else if (e.ClickCount == 2)
        {
            var first = offset;
            var last = offset;
            var text = _owner.Document.Text;
            while (first > 0 && CodeDocument.IsIdentifier(text[first - 1])) first--;
            while (last < text.Length && CodeDocument.IsIdentifier(text[last])) last++;
            _owner.Select(first, last - first);
        }
        else
        {
            _owner.SetSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _owner.Anchor : offset, offset);
        }

        _dragging = true;
        _dragPosition = point;
        _dragScroll.Start();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            var problem = DiagnosticAt(e.GetPosition(this));
            ToolTip.SetPlacement(this, PlacementMode.Pointer);
            ToolTip.SetTip(this, problem == null ? null : CodeEditor.DiagnosticTip(problem));
            return;
        }

        _dragPosition = e.GetPosition(this);
        _owner.SetSelection(_owner.Anchor, HitPosition(_dragPosition), false);
        e.Handled = true;
    }

    private CodeDiagnostic? DiagnosticAt(Point point)
    {
        if (point.X < TextLeft || point.X >= Bounds.Width || point.Y < 0 || point.Y >= Bounds.Height) return null;
        var index = (int)Math.Floor((point.Y + _offset.Y - _owner.Padding.Top) / _owner.LineHeight);
        if (index < 0 || index >= _rows.Count) return null;
        var row = _rows[index];
        var line = _owner.Document.Lines[row.Line];
        var diagnostics = _owner.DiagnosticsForLine(row.Line);
        if (diagnostics.Count == 0) return null;
        var layout = GetLayout(row);
        var x = point.X + _offset.X - TextLeft;
        foreach (var problem in diagnostics)
        {
            if (!RangeInRow(row, line, problem.Start, problem.Length, out var first, out var end,
                    out var insertion)) continue;
            if (insertion)
            {
                var left = layout.XAt(row.Length);
                if (x >= left && x < left + _charWidth) return problem;
            }
            else
            {
                foreach (var bounds in layout.RangeBounds(first - row.Start, end - row.Start))
                    if (x >= bounds.Rectangle.Left && x < Math.Max(bounds.Rectangle.Left + 3, bounds.Rectangle.Right))
                        return problem;
            }
        }

        return null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        _dragScroll.Stop();
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        _dragScroll.Stop();
    }

    internal void InputChanged()
    {
        if (!_verticalMovement) _preferredX = null;
        _verticalMovement = false;
        _caretVisible = true;
        InvalidateVisual();
        InputMethod.SetIsInputMethodEnabled(this, _owner.CanEdit);
        _inputClient.Notify();
    }

    internal void ClearPreedit()
    {
        if (_preedit.Length == 0) return;
        _preedit = "";
        _preeditCursor = 0;
        InvalidateVisual();
        _inputClient.CursorMoved();
    }

    private void CoerceOffset()
    {
        _offset = new Vector(
            _owner.WordWrap
                ? 0
                : Math.Clamp(double.IsFinite(_offset.X) ? _offset.X : 0, 0,
                    Math.Max(0, Extent.Width - _viewport.Width)),
            Math.Clamp(double.IsFinite(_offset.Y) ? _offset.Y : 0, 0, Math.Max(0, Extent.Height - _viewport.Height)));
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new SurfaceAutomationPeer(this);
    }

    private sealed class SurfaceAutomationPeer(CodeTextSurface surface) : ControlAutomationPeer(surface), IValueProvider
    {
        public bool IsReadOnly => !surface._owner.CanEdit;
        public string Value => surface._owner.Document.Text;

        public void SetValue(string? value)
        {
            if (IsReadOnly) return;
            surface._owner.Select(0, surface._owner.Document.Text.Length);
            surface._owner.ReplaceSelection(value ?? "");
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Edit;
        }

        protected override string GetClassNameCore()
        {
            return nameof(CodeEditor);
        }
    }

    private sealed class InputClient(CodeTextSurface surface) : TextInputMethodClient
    {
        public override Visual TextViewVisual => surface;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;
        private CodeLine Line => surface._owner.Document.Lines[surface._owner.CaretLine - 1];
        public override string SurroundingText => Line.Text;

        public override Rect CursorRectangle
        {
            get
            {
                var rect = surface.CaretRectangle;
                if (surface._preedit.Length == 0) return rect;
                using var layout = new TextLayout(surface._preedit, new Typeface(surface._owner.FontFamily),
                    surface._owner.FontSize, surface._owner.Foreground);
                return rect.WithX(rect.X + layout
                    .HitTestTextPosition(Math.Clamp(surface._preeditCursor, 0, surface._preedit.Length)).X);
            }
        }

        public override TextSelection Selection
        {
            get => new(Math.Clamp(surface._owner.Anchor - Line.Start, 0, Line.Text.Length),
                Math.Clamp(surface._owner.CaretIndex - Line.Start, 0, Line.Text.Length));
            set
            {
                if (surface._owner.CanEdit)
                    surface._owner.SetSelection(Line.Start + Math.Clamp(value.Start, 0, Line.Text.Length),
                        Line.Start + Math.Clamp(value.End, 0, Line.Text.Length));
            }
        }

        public override void SetPreeditText(string? preeditText)
        {
            SetPreeditText(preeditText, null);
        }

        public override void SetPreeditText(string? preeditText, int? cursorPos)
        {
            if (!surface._owner.CanEdit) return;
            surface._preedit = preeditText ?? "";
            surface._preeditCursor = cursorPos ?? surface._preedit.Length;
            surface._owner.CloseCompletion();
            surface.InvalidateVisual();
            RaiseCursorRectangleChanged();
        }

        public override void ExecuteContextMenuAction(ContextMenuAction action)
        {
            var owner = surface._owner;
            (action switch
            {
                ContextMenuAction.Copy => owner.CopyCommand, ContextMenuAction.Cut => owner.CutCommand,
                ContextMenuAction.Paste => owner.PasteCommand, _ => owner.SelectAllCommand
            }).Execute(null);
        }

        internal void Notify()
        {
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
            RaiseCursorRectangleChanged();
        }

        internal void CursorMoved()
        {
            RaiseCursorRectangleChanged();
        }
    }

    private readonly record struct LayoutKey(CodeLine Line, int Start, int Length, int LexVersion);

    private sealed class RowLayout : IDisposable
    {
        private readonly int[]? _expandedOffsets;
        private readonly List<(GlyphRun Run, IBrush Brush, Point Origin)> _glyphs = [];
        private readonly int _length;
        private readonly bool _lexical;
        private readonly CodeLine _line;
        private readonly int _start;
        private readonly string _text;
        private int[]? _elements;
        private bool _hasSemantic;
        private int _paintVersion = -1;

        internal RowLayout(CodeEditor owner, CodeLine line, int start, int length, IBrush[] brushes)
        {
            _length = length;
            _line = line;
            _start = start;
            _lexical = owner.IsSyntaxHighlightingEnabled;
            string text;
            if (!line.Text.AsSpan(start, length).Contains('\t'))
            {
                text = start == 0 && length == line.Text.Length ? line.Text : line.Text.Substring(start, length);
            }
            else
            {
                var builder = new StringBuilder(length);
                _expandedOffsets = new int[length + 1];
                var columns = 0;
                for (var i = 0; i < length;)
                {
                    _expandedOffsets[i] = builder.Length;
                    var c = line.Text[start + i];
                    if (c == '\t')
                    {
                        var count = owner.TabSize - columns % owner.TabSize;
                        builder.Append(' ', count);
                        columns += count;
                        i++;
                    }
                    else
                    {
                        var elementLength = ElementLength(line.Text, start + i);
                        for (var k = 1; k < elementLength; k++) _expandedOffsets[i + k] = builder.Length + k;
                        builder.Append(line.Text, start + i, elementLength);
                        columns += CellWidth(line.Text, start + i);
                        i += elementLength;
                    }
                }

                _expandedOffsets[length] = builder.Length;
                text = builder.ToString();
            }

            _text = text;
            // Shape the row once. Coloring must not split a paragraph into dozens
            // of independently shaped keyword/punctuation runs on every scroll.
            Layout = new TextLayout(text, new Typeface(owner.FontFamily), owner.FontSize, owner.Foreground,
                lineHeight: owner.LineHeight, textRunCache: null);
            UpdateGlyphs(owner.SemanticColors, owner.SemanticColorVersion, brushes);
        }

        internal TextLayout Layout { get; }

        public void Dispose()
        {
            foreach (var glyph in _glyphs) glyph.Run.Dispose();
            Layout.Dispose();
        }

        private bool FillLexicalColors(Span<byte> colors)
        {
            var hasColors = false;
            if (_lexical)
            {
                foreach (var token in _line.Tokens)
                {
                    if (token.Kind == CodeTokenKind.Plain) continue;
                    var from = Math.Max(_start, token.Start);
                    var to = Math.Min(_start + _length, token.Start + token.Length);
                    if (to <= from) continue;
                    var offset = ExpandedIndex(from - _start);
                    var end = ExpandedIndex(to - _start);
                    if (end > offset)
                    {
                        colors.Slice(offset, end - offset).Fill((byte)token.Kind);
                        hasColors = true;
                    }
                }

                foreach (var span in _line.Highlights)
                {
                    var from = Math.Max(_start, span.Start);
                    var to = Math.Min(_start + _length, span.Start + span.Length);
                    if (to <= from) continue;
                    var offset = ExpandedIndex(from - _start);
                    var end = ExpandedIndex(to - _start);
                    if (end > offset)
                    {
                        colors.Slice(offset, end - offset).Fill((byte)span.Kind);
                        hasColors = true;
                    }
                }
            }

            return hasColors;
        }

        private void BuildGlyphs(ReadOnlySpan<byte> colors, IBrush[] brushes,
            List<(GlyphRun Run, IBrush Brush, Point Origin)> target)
        {
            var textLine = Layout.TextLines[0];
            var runX = textLine.Start;
            Span<int> counts = stackalloc int[brushes.Length];
            Span<int> positions = stackalloc int[brushes.Length];
            Span<double> firstX = stackalloc double[brushes.Length];
            Span<double> lastX = stackalloc double[brushes.Length];
            foreach (var run in textLine.TextRuns)
            {
                if (run is not ShapedTextRun shaped) continue;
                var original = shaped.GlyphRun;
                MemoryMarshal.TryGetString(shaped.Text, out _, out var memoryStart, out _);
                var sourceOffset = Math.Max(0, memoryStart - original.Metrics.FirstCluster);
                var infos = original.GlyphInfos;
                var advance = 0d;
                // Keep native shaping, but batch disjoint glyphs of the same color.
                // Their advances include the original gaps. This bounds draw calls
                // by palette size instead of syntax-token count on long code rows.
                // Count first and allocate only the retained glyph arrays. The
                // scratch lists, growing tuple buffers and color arrays used to
                // account for much of a cold scroll's managed allocation.
                counts.Clear();
                positions.Clear();
                for (var index = 0; index < infos.Count; index++)
                    counts[colors[Math.Clamp(sourceOffset + infos[index].GlyphCluster, 0, colors.Length - 1)]]++;
                var groups = new GlyphInfo[brushes.Length][];
                for (var kind = 0; kind < groups.Length; kind++)
                    if (counts[kind] > 0)
                        groups[kind] = new GlyphInfo[counts[kind]];
                for (var index = 0; index < infos.Count; index++)
                {
                    var info = infos[index];
                    var kind = (int)colors[Math.Clamp(sourceOffset + info.GlyphCluster, 0, colors.Length - 1)];
                    var group = groups[kind];
                    var position = positions[kind]++;
                    if (position == 0)
                    {
                        firstX[kind] = advance;
                    }
                    else
                    {
                        var prior = group[position - 1];
                        group[position - 1] = new GlyphInfo(prior.GlyphIndex, prior.GlyphCluster, advance - lastX[kind],
                            prior.GlyphOffset);
                    }

                    group[position] = info;
                    lastX[kind] = advance;
                    advance += info.GlyphAdvance;
                }

                for (var kind = 0; kind < groups.Length; kind++)
                {
                    if (groups[kind] is not { Length: > 0 } group) continue;

                    var colored = new GlyphRun(original.GlyphTypeface, original.FontRenderingEmSize,
                        original.Characters, group, original.BaselineOrigin, shaped.BidiLevel);
                    target.Add((colored, brushes[kind],
                        new Point(runX + firstX[kind], textLine.Baseline - shaped.Baseline)));
                }

                runX += shaped.Size.Width;
            }
        }

        internal IReadOnlyList<TextBounds> RangeBounds(int first, int end)
        {
            return CodeTextRangeGeometry.Bounds(Layout, _text, ExpandedIndex(first), ExpandedIndex(end), ref _elements);
        }

        private void UpdateGlyphs(CodeSemanticColors semantic, int version, IBrush[] brushes)
        {
            if (_paintVersion == version) return;
            var start = _line.Start + _start;
            var spans = semantic.ForRange(start, start + _length);
            // Keep exactly one set of colored runs per cached row. Building both
            // lexical and semantic sets doubled cold-scroll glyph allocation.
            if (_paintVersion >= 0 && !_hasSemantic && spans.IsEmpty)
            {
                _paintVersion = version;
                return;
            }

            _paintVersion = version;
            _hasSemantic = !spans.IsEmpty;
            foreach (var glyph in _glyphs) glyph.Run.Dispose();
            _glyphs.Clear();
            var colors = _text.Length <= 512 ? stackalloc byte[_text.Length] : new byte[_text.Length];
            colors.Clear();
            var colored = FillLexicalColors(colors);
            foreach (var span in spans)
            {
                var first = ExpandedIndex(Math.Max(0, span.Start - start));
                var end = ExpandedIndex(Math.Min(_length, span.Start + span.Length - start));
                var kind = span.Kind == CodeSemanticKind.Variable ? 0 : CodeHighlighting.ColorCount + (int)span.Kind;
                colors.Slice(first, end - first).Fill((byte)kind);
                colored |= kind != 0;
            }

            if (colored) BuildGlyphs(colors, brushes, _glyphs);
        }

        internal void Draw(DrawingContext context, Point origin, CodeSemanticColors semantic, int version,
            IBrush[] brushes)
        {
            UpdateGlyphs(semantic, version, brushes);
            if (_glyphs.Count == 0)
            {
                Layout.Draw(context, origin);
                return;
            }

            foreach (var glyph in _glyphs)
                using (context.PushTransform(Matrix.CreateTranslation(origin + glyph.Origin)))
                {
                    context.DrawGlyphRun(glyph.Brush, glyph.Run);
                }
        }

        internal int ExpandedIndex(int raw)
        {
            return _expandedOffsets is { } offsets
                ? offsets[Math.Clamp(raw, 0, offsets.Length - 1)]
                : Math.Clamp(raw, 0, _length);
        }

        internal double XAt(int raw)
        {
            return Layout.HitTestTextPosition(ExpandedIndex(raw)).X;
        }

        internal int RawAt(double x)
        {
            var hit = Layout.TextLines[0].GetCharacterHitFromDistance(x);
            var offset = hit.FirstCharacterIndex + hit.TrailingLength;
            if (_expandedOffsets == null) return Math.Clamp(offset, 0, _length);
            var index = Array.BinarySearch(_expandedOffsets, offset);
            return index >= 0 ? index : Math.Clamp(~index - 1, 0, _expandedOffsets.Length - 1);
        }
    }
}