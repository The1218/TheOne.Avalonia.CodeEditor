using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TheOne.Avalonia.Controls;

public partial class CodeEditor
{
    public static readonly StyledProperty<bool> IsDiagnosticsEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsDiagnosticsEnabled), true);

    public static readonly StyledProperty<bool> IsSignatureHelpEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsSignatureHelpEnabled), true);

    public static readonly StyledProperty<bool> IsSemanticHighlightingEnabledProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsSemanticHighlightingEnabled), true);

    public static readonly DirectProperty<CodeEditor, IReadOnlyList<CodeDiagnostic>> DiagnosticsProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, IReadOnlyList<CodeDiagnostic>>(nameof(Diagnostics),
            c => c.Diagnostics);

    public static readonly DirectProperty<CodeEditor, int> ErrorCountProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(ErrorCount), c => c.ErrorCount);

    public static readonly DirectProperty<CodeEditor, string> DiagnosticSummaryProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string>(nameof(DiagnosticSummary), c => c.DiagnosticSummary);

    public static readonly DirectProperty<CodeEditor, int> DiagnosticLineProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, int>(nameof(DiagnosticLine), c => c.DiagnosticLine);

    public static readonly DirectProperty<CodeEditor, string> DiagnosticMessageProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string>(nameof(DiagnosticMessage), c => c.DiagnosticMessage);

    public static readonly DirectProperty<CodeEditor, string> DiagnosticDetailProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, string>(nameof(DiagnosticDetail), c => c.DiagnosticDetail);

    public static readonly DirectProperty<CodeEditor, CodeSignatureHelp?> SignatureHelpProperty =
        AvaloniaProperty.RegisterDirect<CodeEditor, CodeSignatureHelp?>(nameof(SignatureHelp), c => c.SignatureHelp);

    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private readonly DispatcherTimer _diagnosticTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly Dictionary<int, List<CodeDiagnostic>> _diagnosticsByLine = [];

    private readonly Border _signature = new()
    {
        Name = "PART_SignaturePopup", IsVisible = false, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
        MaxHeight = 160, ClipToBounds = true
    };

    private readonly TextBlock _signatureText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _signatureTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private CancellationTokenSource? _analysisCancellation;
    private long _analysisInvalidatedAt;
    private int _analysisRevision = -1, _signatureRequest;
    private Task<CodeAnalysis>? _analysisTask;
    private CodeType _analysisType;
    private Button? _diagnosticLink;
    private string _diagnosticMessage = "", _diagnosticDetail = "";
    private string _diagnosticSummary = "";
    private IReadOnlyList<CodeDiagnostic> _diagnostics = [];
    private int _errorCount, _diagnosticLine;
    private CodeSignatureHelp? _signatureHelp;
    private TextBlock? _statusDiagnosticTip;
    internal CodeSemanticColors SemanticColors { get; private set; } = CodeSemanticColors.Empty;
    internal int SemanticColorVersion { get; private set; }

    private bool NeedsBackgroundAnalysis => IsDiagnosticsEnabled ||
                                            (IsSemanticHighlightingEnabled &&
                                             CodeAnalysis.SupportsSemanticScopes(CodeType));

    public bool IsDiagnosticsEnabled
    {
        get => GetValue(IsDiagnosticsEnabledProperty);
        set => SetValue(IsDiagnosticsEnabledProperty, value);
    }

    public bool IsSignatureHelpEnabled
    {
        get => GetValue(IsSignatureHelpEnabledProperty);
        set => SetValue(IsSignatureHelpEnabledProperty, value);
    }

    /// <summary>Colors known single-file symbols independently of lexical highlighting.</summary>
    public bool IsSemanticHighlightingEnabled
    {
        get => GetValue(IsSemanticHighlightingEnabledProperty);
        set => SetValue(IsSemanticHighlightingEnabledProperty, value);
    }

    public IReadOnlyList<CodeDiagnostic> Diagnostics => _diagnostics;
    public int ErrorCount => _errorCount;
    public string DiagnosticSummary => _diagnosticSummary;
    public int DiagnosticLine => _diagnosticLine;

    /// <summary>The full message of the problem displayed in the status bar; the template trims it visually.</summary>
    public string DiagnosticMessage => _diagnosticMessage;

    /// <summary>Full diagnostic code, source location and message for tooltips and accessibility.</summary>
    public string DiagnosticDetail => _diagnosticDetail;

    public CodeSignatureHelp? SignatureHelp => _signatureHelp;
    public ICommand NextDiagnosticCommand { get; private set; } = null!;
    public ICommand GoToDiagnosticCommand { get; private set; } = null!;

    private void InitializeIntelligence()
    {
        _signature.Child = _signatureText;
        _completionLayer.Children.Add(_signature);
        ToolTip.SetCustomPopupPlacementCallback(View, placement =>
        {
            var caret = View.CaretRectangle;
            placement.AnchorRectangle = new Rect(caret.X, caret.Y, caret.Width, caret.Height + 4);
            placement.Anchor = PopupAnchor.BottomLeft;
            placement.Gravity = PopupGravity.BottomRight;
            placement.ConstraintAdjustment =
                PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        });
        _diagnosticTimer.Tick += async (_, _) =>
        {
            _diagnosticTimer.Stop();
            await RefreshAnalysisAsync();
        };
        _signatureTimer.Tick += async (_, _) =>
        {
            _signatureTimer.Stop();
            await RequestSignatureHelpAsync();
        };
        NextDiagnosticCommand = new EditorCommand(() => GoToNextDiagnostic(), () => Diagnostics.Count > 0);
        GoToDiagnosticCommand = new EditorCommand(() =>
        {
            var d = DisplayedDiagnostic();
            if (d != null) NavigateToDiagnostic(d);
        }, () => Diagnostics.Count > 0);
    }

    private Task<CodeAnalysis> AnalysisAsync()
    {
        if (_analysisTask != null && _analysisRevision == Document.Revision && _analysisType == CodeType)
            return _analysisTask;
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        var token = _analysisCancellation.Token;
        var text = Document.Text;
        var type = CodeType;
        _analysisRevision = Document.Revision;
        _analysisType = type;
        return _analysisTask = AnalyzeSnapshotAsync(text, type, token);
    }

    private async Task<CodeAnalysis> AnalyzeSnapshotAsync(string text, CodeType type, CancellationToken token)
    {
        // Explicit requests during fast typing share the same short idle window.
        // The 320ms diagnostics timer has already passed it; cached snapshots and
        // the large-document preliminary completion path remain immediate.
        if (text.Length > 100_000 && _analysisInvalidatedAt != 0)
        {
            var remaining = 80 - Stopwatch.GetElapsedTime(_analysisInvalidatedAt).TotalMilliseconds;
            if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), token).ConfigureAwait(false);
        }

        // A canceled predecessor may still be unwinding. Never overlap expensive
        // snapshots for this editor, and cancel queued revisions before they allocate.
        await _analysisGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => CodeAnalysis.Create(text, type, token), token).ConfigureAwait(false);
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    private void InvalidateAnalysis()
    {
        using var work = CodeWorkProbe.Start("invalidate");
        _analysisInvalidatedAt = Stopwatch.GetTimestamp();
        _analysisCancellation?.Cancel();
        _analysisTask = null;
        ToolTip.SetIsOpen(View, false);
        ToolTip.SetTip(View, null);
        if (_diagnosticLink != null) ToolTip.SetIsOpen(_diagnosticLink, false);
        ClearSignature();
        PublishDiagnostics([]);
        PublishSemanticColors(CodeSemanticColors.Empty);
        _diagnosticTimer.Stop();
        if (NeedsBackgroundAnalysis && _host?.IsAttachedToVisualTree() == true) _diagnosticTimer.Start();
    }

    internal void DeferDiagnostics()
    {
        if (!NeedsBackgroundAnalysis || _analysisTask?.IsCompletedSuccessfully == true) return;
        _diagnosticTimer.Stop();
        if (_host?.IsAttachedToVisualTree() == true) _diagnosticTimer.Start();
    }

    private void CancelIntelligenceBeforeEdit()
    {
        using var work = CodeWorkProbe.Start("cancel-before-edit");
        // Cancel allocation-heavy workers before replacing the immutable public
        // Text snapshot, rather than after that large foreground allocation.
        _analysisCancellation?.Cancel();
        _completionCancellation?.Cancel();
    }

    /// <summary>Runs built-in lexical, structural and language diagnostics on a snapshot in the background.</summary>
    public async Task<bool> RefreshDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDiagnosticsEnabled) return false;
        return await RefreshAnalysisAsync(cancellationToken);
    }

    internal async Task<bool> RefreshAnalysisAsync(CancellationToken cancellationToken = default)
    {
        if (!NeedsBackgroundAnalysis) return false;
        if (cancellationToken.IsCancellationRequested) return false;
        var revision = Document.Revision;
        var type = CodeType;
        try
        {
            var analysis = await AnalysisAsync().WaitAsync(cancellationToken);
            if (revision != Document.Revision || type != CodeType || !NeedsBackgroundAnalysis) return false;
            PublishSemanticColors(IsSemanticHighlightingEnabled ? analysis.SemanticColors : CodeSemanticColors.Empty);
            if (IsDiagnosticsEnabled) PublishDiagnostics(analysis.Diagnostics);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void PublishSemanticColors(CodeSemanticColors colors)
    {
        if (ReferenceEquals(SemanticColors, colors) || (SemanticColors.IsEmpty && colors.IsEmpty)) return;
        SemanticColors = colors;
        SemanticColorVersion++;
        View.InvalidateVisual(); // Row geometry stays valid; only colored glyph groups change.
    }

    internal void PublishDiagnostics(IReadOnlyList<CodeDiagnostic> diagnostics)
    {
        if (_diagnostics.Count == 0 && diagnostics.Count == 0) return;
        SetAndRaise(DiagnosticsProperty, ref _diagnostics, diagnostics);
        PseudoClasses.Set(":has-diagnostics", diagnostics.Count > 0);
        SetAndRaise(ErrorCountProperty, ref _errorCount,
            diagnostics.Count(d => d.Severity == CodeDiagnosticSeverity.Error));
        SetAndRaise(DiagnosticSummaryProperty, ref _diagnosticSummary,
            diagnostics.Count == 0 ? "" : $"⚠ {diagnostics.Count}");
        UpdateDiagnosticLine();
        _diagnosticsByLine.Clear();
        foreach (var d in diagnostics)
        {
            var last = Document.LineAt(Math.Min(Document.Text.Length, d.Start + d.Length));
            for (var line = Math.Max(0, d.Line - 1); line <= last; line++)
            {
                if (!_diagnosticsByLine.TryGetValue(line, out var items)) _diagnosticsByLine[line] = items = [];
                items.Add(d);
            }
        }

        (NextDiagnosticCommand as EditorCommand)?.Raise();
        (GoToDiagnosticCommand as EditorCommand)?.Raise();
        View.InvalidateVisual();
    }

    internal IReadOnlyList<CodeDiagnostic> DiagnosticsForLine(int line)
    {
        return _diagnosticsByLine.TryGetValue(line, out var items) ? items : [];
    }

    /// <summary>Selects the next reported problem, wraps at the end, and brings it into view.</summary>
    public bool GoToNextDiagnostic(bool backwards = false)
    {
        if (Diagnostics.Count == 0) return false;
        var d = backwards
            ? Diagnostics.LastOrDefault(d => d.Start < SelectionStart) ?? Diagnostics[^1]
            : Diagnostics.FirstOrDefault(d => d.Start > SelectionStart) ?? Diagnostics[0];
        NavigateToDiagnostic(d);
        return true;
    }

    private CodeDiagnostic? DisplayedDiagnostic()
    {
        return Diagnostics.FirstOrDefault(d => d.Start >= SelectionStart) ?? Diagnostics.FirstOrDefault();
    }

    private void NavigateToDiagnostic(CodeDiagnostic d)
    {
        Select(d.Start, Math.Min(d.Length, Document.Text.Length - d.Start));
        FocusEditor();
        ToolTip.SetPlacement(View, PlacementMode.Custom);
        ToolTip.SetTip(View, DiagnosticTip(d));
        ToolTip.SetIsOpen(View, true);
    }

    private void UpdateDiagnosticLine()
    {
        var problem = DisplayedDiagnostic();
        SetAndRaise(DiagnosticLineProperty, ref _diagnosticLine, problem?.Line ?? 0);
        SetAndRaise(DiagnosticMessageProperty, ref _diagnosticMessage, problem?.Message ?? "");
        SetAndRaise(DiagnosticDetailProperty, ref _diagnosticDetail, problem == null ? "" : DiagnosticText(problem));
        if (_statusDiagnosticTip != null) _statusDiagnosticTip.Text = _diagnosticDetail;
    }

    internal static string DiagnosticText(CodeDiagnostic problem)
    {
        return $"{problem.Code} · {problem.Line}:{problem.Column}\n{problem.Message}";
    }

    internal static TextBlock DiagnosticTip(CodeDiagnostic problem)
    {
        return new TextBlock { Text = DiagnosticText(problem), MaxWidth = 560, TextWrapping = TextWrapping.Wrap };
    }

    /// <summary>Finds the innermost known call; nested expressions and literal commas do not change its active parameter.</summary>
    public async Task<bool> RequestSignatureHelpAsync(CancellationToken cancellationToken = default)
    {
        var request = ++_signatureRequest;
        var revision = Document.Revision;
        var caret = _caret;
        var type = CodeType;
        if (!IsSignatureHelpEnabled || SelectionLength > 0)
        {
            ClearSignature();
            return false;
        }

        try
        {
            var analysis = await AnalysisAsync().WaitAsync(cancellationToken);
            if (request != _signatureRequest || revision != Document.Revision || caret != _caret || type != CodeType ||
                !IsSignatureHelpEnabled) return false;
            var help = analysis.Signature(caret);
            SetAndRaise(SignatureHelpProperty, ref _signatureHelp, help);
            _signature.IsVisible = help != null;
            if (help == null) return false;
            RenderSignature();
            PositionCompletion();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void RenderSignature()
    {
        _signatureText.Inlines!.Clear();
        if (_signatureHelp is not { } help) return;
        var signature = help.Signatures[help.ActiveSignature];
        _signatureText.Inlines.Add(new Run(signature.Name + "("));
        for (var i = 0; i < signature.Parameters.Count; i++)
        {
            if (i > 0) _signatureText.Inlines.Add(new Run(", "));
            _signatureText.Inlines.Add(new Run(signature.Parameters[i].ToString())
            {
                FontWeight = i == help.ActiveParameter ? FontWeight.Bold : FontWeight.Normal,
                Foreground = i == help.ActiveParameter ? ResourceBrush("CodeEditor.Accent") : Foreground
            });
        }

        _signatureText.Inlines.Add(new Run(")" + (signature.ReturnType != null ? " → " + signature.ReturnType : "")));
        if (help.Signatures.Count > 1)
            _signatureText.Inlines.Add(new Run($"  [{help.ActiveSignature + 1}/{help.Signatures.Count}] Ctrl+↑↓"));
    }

    private void ClearSignature()
    {
        _signatureRequest++;
        _signatureTimer.Stop();
        _signature.IsVisible = false;
        SetAndRaise(SignatureHelpProperty, ref _signatureHelp, null);
    }

    private void ScheduleSignature()
    {
        ClearSignature();
        if (!IsSignatureHelpEnabled || SelectionLength > 0) return;
        // A bounded context check keeps ordinary caret movement out of the language engine.
        var from = Math.Max(0, _caret - 2048);
        if (Document.Text.AsSpan(from, _caret - from).Contains('(')) _signatureTimer.Start();
    }

    private void PositionSignature()
    {
        if (!_signature.IsVisible) return;
        PlaceOverlay(_signature, 520, true);
    }
}