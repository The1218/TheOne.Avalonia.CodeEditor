using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TheOne.Avalonia.Controls;

/// <summary>A virtualized, single-document code editor built on Avalonia text shaping.</summary>
public partial class CodeEditor : TemplatedControl
{
    public static readonly StyledProperty<CodeEditorDensity> DensityProperty =
        AvaloniaProperty.Register<CodeEditor, CodeEditorDensity>(nameof(Density), CodeEditorDensity.Normal);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<CodeType> CodeTypeProperty =
        AvaloniaProperty.Register<CodeEditor, CodeType>(nameof(CodeType));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowLineNumbers), true);

    public static readonly StyledProperty<bool> IsSyntaxHighlightingEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsSyntaxHighlightingEnabled), true);

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(WordWrap));

    public static readonly StyledProperty<int> TabSizeProperty =
        AvaloniaProperty.Register<CodeEditor, int>(nameof(TabSize), 4, validate: value => value is > 0 and <= 32);

    public static readonly StyledProperty<bool> ConvertTabsToSpacesProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ConvertTabsToSpaces), true);

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<CodeEditor, object?>(nameof(Header));

    public static readonly StyledProperty<bool> ShowStatusBarProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowStatusBar), true);

    public static readonly StyledProperty<bool> ShowCopyButtonProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowCopyButton));

    public static readonly StyledProperty<bool> ShowFormatButtonProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowFormatButton));

    public static readonly StyledProperty<bool> IsCurrentLineHighlightedProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsCurrentLineHighlighted), true);

    public static readonly StyledProperty<bool> IsAutoIndentEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsAutoIndentEnabled), true);

    public static readonly StyledProperty<bool> IsCompletionEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsCompletionEnabled), true);

    public static readonly StyledProperty<bool> IsTabCompletionEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsTabCompletionEnabled), true);

    public static readonly StyledProperty<bool> IsInlineCompletionEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsInlineCompletionEnabled), true);

    public static readonly StyledProperty<bool> AutoInsertPairsProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(AutoInsertPairs), true);

    public static readonly StyledProperty<ICodeCompletionProvider?> CompletionProviderProperty =
        AvaloniaProperty.Register<CodeEditor, ICodeCompletionProvider?>(nameof(CompletionProvider));

    public static readonly StyledProperty<ICodeFormatter?> FormatterProperty =
        AvaloniaProperty.Register<CodeEditor, ICodeFormatter?>(nameof(Formatter));

    public static readonly DirectProperty<CodeEditor, int> CaretLineProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(CaretLine), c => c.CaretLine);

    public static readonly DirectProperty<CodeEditor, int> CaretColumnProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(CaretColumn), c => c.CaretColumn);

    public static readonly DirectProperty<CodeEditor, int> CaretIndexProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(CaretIndex), c => c.CaretIndex,
            (c, v) => c.CaretIndex = v);

    public static readonly DirectProperty<CodeEditor, int> SelectionStartProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(SelectionStart), c => c.SelectionStart,
            (c, v) => c.SelectionStart = v);

    public static readonly DirectProperty<CodeEditor, int> SelectionLengthProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(SelectionLength), c => c.SelectionLength,
            (c, v) => c.SelectionLength = v);

    public static readonly DirectProperty<CodeEditor, int> LineCountProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(LineCount), c => c.LineCount);

    public static readonly DirectProperty<CodeEditor, string> LanguageNameProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string>(nameof(LanguageName), c => c.LanguageName);

    public static readonly DirectProperty<CodeEditor, string?> HighlightingErrorProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string?>(nameof(HighlightingError), c => c.HighlightingError);

    public static readonly DirectProperty<CodeEditor, string?> FormattingErrorProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string?>(nameof(FormattingError), c => c.FormattingError);

    public static readonly DirectProperty<CodeEditor, string?> CompletionErrorProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string?>(nameof(CompletionError), c => c.CompletionError);

    public static readonly DirectProperty<CodeEditor, bool> IsFormattingProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, bool>(nameof(IsFormatting), c => c.IsFormatting);

    internal readonly CodeDocument Document = new();
    internal readonly CodeTextSurface View;
    private readonly Grid _body = new();

    private readonly Border _completion = new()
    {
        Name = "PART_CompletionPopup", IsVisible = false, Width = 280, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(3), CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1)
    };

    private readonly Canvas _completionLayer = new() { ClipToBounds = true };
    private readonly ListBox _completionList = new() { MaxHeight = 230, Focusable = false };
    private readonly DispatcherTimer _completionTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };

    private readonly Border _find = new()
    {
        IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(8), Padding = new Thickness(6), CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(1)
    };

    private readonly TextBox _findBox = new() { PlaceholderText = "查找 / Find", Width = 190 };
    private readonly List<IDisposable> _resources = [];
    private readonly ScrollViewer _scroll;
    private int _caret, _caretLine = 1, _caretColumn = 1;
    private CancellationTokenSource? _completionCancellation, _formatCancellation;
    private string? _highlightingError, _formattingError, _completionError;
    private ContentControl? _host;
    private string _languageName = "Plain text";
    private bool _syncing, _isFormatting;

    public CodeEditor()
    {
        View = new CodeTextSurface(this);
        _scroll = new ScrollViewer
        {
            Content = View, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _body.Children.Add(_scroll);
        _body.Children.Add(_completionLayer);
        _body.Children.Add(_find);
        _completionLayer.Children.Add(_completion);
        _body.SizeChanged += (_, _) => PositionCompletion();
        _completion.Child = _completionList;
        _completionList.ItemTemplate = new FuncDataTemplate<CodeCompletionItem>((item, _) => CompletionRow(item));
        _completionList.SelectionChanged += (_, _) => View.InvalidateVisual();
        InitializeIntelligence();
        _completionList.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_completionList.SelectedItem is CodeCompletionItem item)
            {
                AcceptCompletion(item);
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);
        var next = new Button { Content = "↓", Padding = new Thickness(7), MinWidth = 0 };
        var close = new Button { Content = "×", Padding = new Thickness(7), MinWidth = 0 };
        next.Click += (_, _) => FindNext(_findBox.Text ?? "");
        close.Click += (_, _) => CloseFind();
        _find.Child = new StackPanel
            { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _findBox, next, close } };
        _findBox.TextChanged += (_, _) => FindNext(_findBox.Text ?? "", fromStart: true);
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                FindNext(_findBox.Text ?? "", backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseFind();
                e.Handled = true;
            }
        };
        _completionTimer.Tick += async (_, _) =>
        {
            _completionTimer.Stop();
            await RequestCompletionAsync();
        };
        CopyCommand =
            new AsyncEditorCommand(() => CopySelectionAsync(), () => IsEffectivelyEnabled && SelectionLength > 0);
        CopyAllCommand =
            new AsyncEditorCommand(() => CopyAllAsync(), () => IsEffectivelyEnabled && Document.Text.Length > 0);
        CutCommand = new AsyncEditorCommand(CutAsync, () => CanEdit && SelectionLength > 0);
        PasteCommand = new AsyncEditorCommand(PasteAsync, () => CanEdit);
        SelectAllCommand = new EditorCommand(() => Select(0, Document.Text.Length),
            () => IsEffectivelyEnabled && Document.Text.Length > 0);
        UndoCommand = new EditorCommand(Undo, () => CanEdit && _undo.Count > 0);
        RedoCommand = new EditorCommand(Redo, () => CanEdit && _redo.Count > 0);
        FormatCommand = new AsyncEditorCommand(() => FormatDocumentAsync(),
            () => CanEdit && !IsFormatting && Document.Text.Length > 0);
        CompleteCommand = new AsyncEditorCommand(() => RequestCompletionAsync(), () => CanEdit && IsCompletionEnabled);
        View.ContextFlyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem { Header = "撤销 / Undo", Command = UndoCommand },
                new MenuItem { Header = "重做 / Redo", Command = RedoCommand },
                new Separator(),
                new MenuItem { Header = "剪切 / Cut", Command = CutCommand },
                new MenuItem { Header = "复制 / Copy", Command = CopyCommand },
                new MenuItem { Header = "粘贴 / Paste", Command = PasteCommand },
                new MenuItem { Header = "全选 / Select all", Command = SelectAllCommand },
                new Separator(),
                new MenuItem { Header = "格式化 / Format", Command = FormatCommand }
            }
        };
        AddHandler(KeyDownEvent, PreviewKeyDown, RoutingStrategies.Tunnel);
        ApplyOptions();
    }

    public CodeEditorDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public CodeType CodeType
    {
        get => GetValue(CodeTypeProperty);
        set => SetValue(CodeTypeProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public bool IsSyntaxHighlightingEnabled
    {
        get => GetValue(IsSyntaxHighlightingEnabledProperty);
        set => SetValue(IsSyntaxHighlightingEnabledProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public int TabSize
    {
        get => GetValue(TabSizeProperty);
        set => SetValue(TabSizeProperty, value);
    }

    public bool ConvertTabsToSpaces
    {
        get => GetValue(ConvertTabsToSpacesProperty);
        set => SetValue(ConvertTabsToSpacesProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool ShowStatusBar
    {
        get => GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    public bool ShowCopyButton
    {
        get => GetValue(ShowCopyButtonProperty);
        set => SetValue(ShowCopyButtonProperty, value);
    }

    public bool ShowFormatButton
    {
        get => GetValue(ShowFormatButtonProperty);
        set => SetValue(ShowFormatButtonProperty, value);
    }

    public bool IsCurrentLineHighlighted
    {
        get => GetValue(IsCurrentLineHighlightedProperty);
        set => SetValue(IsCurrentLineHighlightedProperty, value);
    }

    public bool IsAutoIndentEnabled
    {
        get => GetValue(IsAutoIndentEnabledProperty);
        set => SetValue(IsAutoIndentEnabledProperty, value);
    }

    public bool IsCompletionEnabled
    {
        get => GetValue(IsCompletionEnabledProperty);
        set => SetValue(IsCompletionEnabledProperty, value);
    }

    public bool IsTabCompletionEnabled
    {
        get => GetValue(IsTabCompletionEnabledProperty);
        set => SetValue(IsTabCompletionEnabledProperty, value);
    }

    public bool IsInlineCompletionEnabled
    {
        get => GetValue(IsInlineCompletionEnabledProperty);
        set => SetValue(IsInlineCompletionEnabledProperty, value);
    }

    public bool AutoInsertPairs
    {
        get => GetValue(AutoInsertPairsProperty);
        set => SetValue(AutoInsertPairsProperty, value);
    }

    public ICodeCompletionProvider? CompletionProvider
    {
        get => GetValue(CompletionProviderProperty);
        set => SetValue(CompletionProviderProperty, value);
    }

    public ICodeFormatter? Formatter
    {
        get => GetValue(FormatterProperty);
        set => SetValue(FormatterProperty, value);
    }

    public int CaretLine => _caretLine;
    public int CaretColumn => _caretColumn;

    public int CaretIndex
    {
        get => _caret;
        set => SetSelection(value, value);
    }

    public int SelectionStart
    {
        get => Math.Min(Anchor, _caret);
        set => Select(value, SelectionLength);
    }

    public int SelectionLength
    {
        get => Math.Abs(_caret - Anchor);
        set => Select(SelectionStart, value);
    }

    public string SelectedText => Document.Text.Substring(SelectionStart, SelectionLength);
    public int LineCount => Document.Lines.Count;
    public string LanguageName => _languageName;
    public string? HighlightingError => _highlightingError;
    public string? FormattingError => _formattingError;
    public string? CompletionError => _completionError;
    public bool IsFormatting => _isFormatting;
    public ICommand CopyCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand FormatCommand { get; }
    public ICommand CompleteCommand { get; }
    internal bool CanEdit => IsEffectivelyEnabled && !IsReadOnly;
    internal int Anchor { get; private set; }

    internal double LineHeight =>
        Density switch { CodeEditorDensity.Small => 20d / 13, CodeEditorDensity.Large => 24d / 16, _ => 22d / 14 } *
        FontSize;

    private string IndentUnit => ConvertTabsToSpaces ? new string(' ', TabSize) : "\t";

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_host != null) _host.Content = null;
        base.OnApplyTemplate(e);
        _host = e.NameScope.Find<ContentControl>("PART_EditorHost");
        if (_host != null) _host.Content = _body;
        _statusDiagnosticTip = new TextBlock
            { Text = DiagnosticDetail, MaxWidth = 560, TextWrapping = TextWrapping.Wrap };
        if (_diagnosticLink != null) ToolTip.SetIsOpen(_diagnosticLink, false);
        _diagnosticLink = e.NameScope.Find<Button>("PART_Diagnostics");
        if (_diagnosticLink is { } diagnostics)
        {
            ToolTip.SetTip(diagnostics, _statusDiagnosticTip);
            diagnostics.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (diagnostics == _diagnosticLink && Diagnostics.Count > 0 && diagnostics.IsAttachedToVisualTree())
                    ToolTip.SetIsOpen(diagnostics, true);
            });
        }

        ApplyOptions();
        ApplyPalette();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var key in new[]
                 {
                     "CodeEditor.Surface", "CodeEditor.InputLine", "CodeEditor.Muted", "CodeEditor.Text",
                     "CodeEditor.Accent", "CodeEditor.TextSelection", "CodeEditor.GutterForeground",
                     "CodeEditor.CurrentLineBackground", "CodeEditor.Keyword", "CodeEditor.String",
                     "CodeEditor.Comment", "CodeEditor.Number", "CodeEditor.Type", "CodeEditor.Punctuation",
                     "CodeEditor.SemanticType", "CodeEditor.SemanticMethod", "CodeEditor.SemanticField",
                     "CodeEditor.SemanticProperty", "CodeEditor.SemanticParameter"
                 }.Concat(Enum.GetValues<CodeHighlightKind>().Where(k => k > CodeHighlightKind.Punctuation)
                     .Select(k => "CodeEditor." + k)))
            _resources.Add(this.GetResourceObservable(key).Subscribe(new ResourceObserver(_ => ApplyPalette())));
        if (NeedsBackgroundAnalysis) _diagnosticTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelSnippet();
        _completionTimer.Stop();
        CloseCompletion();
        _formatCancellation?.Cancel();
        _diagnosticTimer.Stop();
        ClearSignature();
        _analysisCancellation?.Cancel();
        _analysisTask = null;
        foreach (var item in _resources) item.Dispose();
        _resources.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (View == null) return;
        if (change.Property == TextProperty && !_syncing && Document.Text != (Text ?? ""))
        {
            CancelIntelligenceBeforeEdit();
            CancelSnippet();
            var count = LineCount;
            Document.Reset(Text ?? "");
            _undo.Clear();
            _redo.Clear();
            _autoPairs.Clear();
            InvalidateAnalysis();
            CodeSyntax.Update(Document, CodeType, force: true);
            RaisePropertyChanged(LineCountProperty, count, LineCount);
            View.DocumentChanged();
            SetSelection(Math.Min(Anchor, Document.Text.Length), Math.Min(_caret, Document.Text.Length));
            CloseCompletion();
            UpdateCommands();
        }

        if (change.Property == CodeTypeProperty)
        {
            CancelIntelligenceBeforeEdit();
            CancelSnippet();
            _formatCancellation?.Cancel();
            SetAndRaise(LanguageNameProperty, ref _languageName, CodeSyntax.LanguageName(CodeType));
            SetAndRaise(HighlightingErrorProperty, ref _highlightingError,
                Enum.IsDefined(CodeType) ? null : $"Unknown CodeType {(int)CodeType}; using plain text.");
            CodeSyntax.Update(Document, CodeType, force: true);
            CloseCompletion();
            View.ResetLayouts();
            InvalidateAnalysis();
            ScheduleSignature();
        }

        if (change.Property == HeaderProperty || change.Property == ShowCopyButtonProperty ||
            change.Property == ShowFormatButtonProperty || change.Property == IsReadOnlyProperty ||
            change.Property == WordWrapProperty || change.Property == ShowLineNumbersProperty ||
            change.Property == TabSizeProperty || change.Property == FontSizeProperty ||
            change.Property == FontFamilyProperty || change.Property == PaddingProperty ||
            change.Property == IsSyntaxHighlightingEnabledProperty ||
            change.Property == DensityProperty) ApplyOptions();
        if (change.Property == ForegroundProperty) View.ResetLayouts();
        if (change.Property == IsCurrentLineHighlightedProperty) View.InvalidateVisual();
        if (change.Property == IsInlineCompletionEnabledProperty || change.Property == IsTabCompletionEnabledProperty)
            View.InvalidateVisual();
        if (change.Property == IsEnabledProperty || change.Property == IsReadOnlyProperty ||
            change.Property == IsCompletionEnabledProperty || change.Property == CompletionProviderProperty)
        {
            CancelSnippet();
            CloseCompletion();
            UpdateCommands();
            View.InputChanged();
        }

        if (change.Property == IsTabCompletionEnabledProperty && !IsTabCompletionEnabled) CancelSnippet();
        if (change.Property == IsReadOnlyProperty || change.Property == FormatterProperty)
            _formatCancellation?.Cancel();
        if (change.Property == IsDiagnosticsEnabledProperty || change.Property == IsSemanticHighlightingEnabledProperty)
            InvalidateAnalysis();
        if (change.Property == IsSignatureHelpEnabledProperty) ScheduleSignature();
    }

    private void ApplyOptions()
    {
        PseudoClasses.Set(":small", Density == CodeEditorDensity.Small);
        PseudoClasses.Set(":large", Density == CodeEditorDensity.Large);
        PseudoClasses.Set(":readonly", IsReadOnly);
        PseudoClasses.Set(":has-header", Header != null || ShowCopyButton || ShowFormatButton);
        _scroll.HorizontalScrollBarVisibility = WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        View.ResetLayouts();
        UpdateCommands();
    }

    private void ApplyPalette()
    {
        _completion.Background = _find.Background = ResourceBrush("CodeEditor.Surface");
        _completion.BorderBrush = _find.BorderBrush = ResourceBrush("CodeEditor.InputLine");
        _signature.Background = ResourceBrush("CodeEditor.Surface");
        _signature.BorderBrush = ResourceBrush("CodeEditor.InputLine");
        _signatureText.Foreground = Foreground;
        RenderSignature();
        View.ResetLayouts();
    }

    internal IBrush ResourceBrush(string key)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Foreground ?? Brushes.Gray;
    }

    public bool FocusEditor()
    {
        return View.Focus();
    }

    public void ScrollToLine(int lineNumber)
    {
        View.ScrollToLine(Math.Clamp(lineNumber - 1, 0, LineCount - 1));
    }

    private void UpdateCommands()
    {
        foreach (var command in new[]
                 {
                     CopyCommand, CopyAllCommand, CutCommand, PasteCommand, SelectAllCommand, UndoCommand, RedoCommand,
                     FormatCommand, CompleteCommand
                 })
            (command as EditorCommand)?.Raise();
    }

    private class EditorCommand(Action execute, Func<bool> enabled) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return enabled();
        }

        public virtual void Execute(object? parameter)
        {
            if (CanExecute(parameter)) execute();
        }

        internal void Raise()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class AsyncEditorCommand(Func<Task> execute, Func<bool> enabled) : EditorCommand(() => { }, enabled)
    {
        public override async void Execute(object? parameter)
        {
            if (CanExecute(parameter)) await execute();
        }
    }

    private sealed class ResourceObserver(Action<object?> action) : IObserver<object?>
    {
        public void OnNext(object? value)
        {
            action(value);
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}