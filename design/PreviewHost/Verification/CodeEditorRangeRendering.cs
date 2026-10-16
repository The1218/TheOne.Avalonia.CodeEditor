using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Styling;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    private static void RangePerformance(Action<bool, string> check, string? output)
    {
        var text = string.Join('\n',
            Enumerable.Range(0, 10000).Select(i =>
                $"int value{i}=1; // abc אבג e\u0301 👩‍💻 mixed text and another אבג e\u0301 segment"));
        var editor = new CodeEditor { Text = text, CodeType = CodeType.CSharp, WordWrap = true };
        var window = Open(editor, 600, 700);
        try
        {
            Pump(editor.RefreshDiagnosticsAsync());
            var markers = Enumerable.Range(5000, 200).Select(n =>
            {
                var line = editor.Document.Lines[n];
                var start = line.Text.IndexOf("אבג", StringComparison.Ordinal) - 2;
                return new CodeDiagnostic(line.Start + start, 9, "Mixed text range", "RANGE",
                    CodeDiagnosticSeverity.Error, n + 1, start + 1);
            }).ToArray();
            editor.PublishDiagnostics(markers);
            editor.ScrollToLine(5000);
            Flush();
            var samples = new List<double>();
            var raw = new List<string> { "sample,scroll_ms,shape_ms,render_ms,pause_ms,ui_bytes" };
            for (var i = 0; i < 80; i++)
            {
                var pause = GC.GetTotalPauseDuration();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                editor.ScrollToLine(5000 + i % 20 * 7);
                Flush();
                watch.Stop();
                if (i >= 5) samples.Add(watch.Elapsed.TotalMilliseconds);
                raw.Add(FormattableString.Invariant(
                    $"{i},{watch.Elapsed.TotalMilliseconds:F3},{editor.View.LastShapeMilliseconds:F3},{editor.View.LastRenderMilliseconds:F3},{(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F3},{GC.GetAllocatedBytesForCurrentThread() - bytes}"));
            }

            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "mixed-range-samples.csv"), raw);
            }

            var p95 = Percentile(samples, .95);
            check(
                !editor.SemanticColors.IsEmpty && editor.View.Rows.Count > 10000 &&
                editor.View.CachedLayoutCount <= 192 && p95 < 16.7,
                $"CodeEditor/performance: wrapped 10000 mixed-text lines, semantic colors and 200 range markers scroll p95 {p95:F2}ms meets unchanged 16.7ms budget");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    internal static void RangeAndSemanticRendering(Action<bool, string> check, string? output)
    {
        SemanticRoles(check);
        foreach (var text in new[] { "\u1100\u1161", "\u06001" })
        {
            using var layout = new TextLayout(text, new Typeface("monospace"), 16, Brushes.Black);
            int[]? elements = null;
            var whole = CodeTextRangeGeometry.Bounds(layout, text, 0, text.Length, ref elements)
                .Select(b => b.Rectangle).ToArray();
            var partial = CodeTextRangeGeometry.Bounds(layout, text, 1, 2, ref elements).Select(b => b.Rectangle)
                .ToArray();
            check(whole.SequenceEqual(partial),
                $"CodeEditor/ranges: non-mark grapheme U+{(int)text[0]:X4} expands to its whole cluster");
        }

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var density in Enum.GetValues<CodeEditorDensity>())
        foreach (var width in new[] { 320, 820 })
        {
            var editor = new CodeEditor
            {
                Text = "abc אבג xyz\ne\u0301 \t👩‍💻 z\nab\t" + new string('x', 160) + "e\u0301👩‍💻 end",
                CodeType = CodeType.PlainText, WordWrap = true, Density = density, IsDiagnosticsEnabled = false,
                IsSemanticHighlightingEnabled = false, IsCurrentLineHighlighted = false
            };
            var window = Open(editor, width, 360, theme);
            try
            {
                var bidi = editor.View.RangeRectangles(2, 3).OrderBy(r => r.Left).ToArray();
                check(bidi.Length >= 2 && bidi[^1].Left - bidi[0].Right > 1,
                    $"CodeEditor/ranges: {theme}/{density}/{width} bidi span keeps the visual gap");
                var combining = editor.Text.IndexOf('e');
                check(
                    editor.View.RangeRectangles(combining, 2)
                        .SequenceEqual(editor.View.RangeRectangles(combining + 1, 1)),
                    $"CodeEditor/ranges: {theme}/{density}/{width} combining mark selects its whole grapheme");
                var emoji = editor.Text.IndexOf("👩", StringComparison.Ordinal);
                check(
                    editor.View.RangeRectangles(emoji, "👩‍💻".Length)
                        .SequenceEqual(editor.View.RangeRectangles(emoji + 1, 1)),
                    $"CodeEditor/ranges: {theme}/{density}/{width} partial surrogate selects the ZWJ grapheme");
                var tab = editor.Text.IndexOf('\t');
                var ordinaryTab = editor.Text.LastIndexOf('\t');
                check(
                    Math.Abs(editor.View.RangeRectangles(tab, 1).Sum(r => r.Width) -
                             editor.View.RangeRectangles(ordinaryTab, 1).Sum(r => r.Width)) < .01,
                    $"CodeEditor/ranges: {theme}/{density}/{width} Tab columns count graphemes once");
                var wrapped = editor.View.Rows.First(r => r.Line == 2 && r.Start > 0);
                var boundary = editor.Document.Lines[2].Start + wrapped.Start;
                var after = editor.View.RangeRectangles(boundary, 1);
                var before = editor.View.RangeRectangles(boundary - 1, 1);
                check(after.Count > 0 && before.Count > 0 && after.All(r => r.Top > before[0].Top),
                    $"CodeEditor/ranges: {theme}/{density}/{width} wrap boundary belongs to exactly one row");
                check(
                    editor.View.Rows.All(r =>
                        r.Start == 0 || (!char.IsLowSurrogate(editor.Document.Lines[r.Line].Text[r.Start]) &&
                                         editor.Document.Lines[r.Line].Text[r.Start] != '\u0301' &&
                                         editor.Document.Lines[r.Line].Text[r.Start] != '\u200d')),
                    $"CodeEditor/ranges: {theme}/{density}/{width} wrapping preserves grapheme boundaries");
                var eof = editor.View.RangeRectangles(editor.Text.Length, 1);
                check(eof.Count == 1, $"CodeEditor/ranges: {theme}/{density}/{width} EOF has one caret marker");
                var combinedBounds = editor.View.RangeRectangles(combining + 1, 1).ToArray();
                var emojiBounds = editor.View.RangeRectangles(emoji + 1, 1).ToArray();
                editor.PublishDiagnostics([
                    new CodeDiagnostic(2, 3, "Bidi range geometry", "RANGE", CodeDiagnosticSeverity.Error, 1, 3),
                    new CodeDiagnostic(combining + 1, 1, "Combining mark", "RANGE", CodeDiagnosticSeverity.Error, 2, 2),
                    new CodeDiagnostic(emoji + 1, 1, "ZWJ grapheme", "RANGE", CodeDiagnosticSeverity.Error, 2,
                        emoji - editor.Document.Lines[1].Start + 2),
                    new CodeDiagnostic(boundary, 1, "Wrap boundary", "RANGE", CodeDiagnosticSeverity.Error, 3,
                        wrapped.Start + 1)
                ]);
                Flush();
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-range-{theme}-{density}-{width}.png");
                CheckMarkerPixels(editor, window, frame, bidi, check, $"{theme}/{density}/{width}");
                CheckMarkerPixels(editor, window, frame, combinedBounds, check, $"combining/{theme}/{density}/{width}");
                CheckMarkerPixels(editor, window, frame, emojiBounds, check, $"emoji/{theme}/{density}/{width}");
                CheckMarkerPixels(editor, window, frame, after.ToArray(), check, $"wrap/{theme}/{density}/{width}");
                var hover = new Point(combinedBounds[0].Left + .5, combinedBounds[0].Center.Y);
                window.MouseMove(editor.View.TranslatePoint(hover, window)!.Value);
                Flush();
                check(
                    ToolTip.GetTip(editor.View) is TextBlock tip &&
                    tip.Text!.Contains("Combining mark", StringComparison.Ordinal),
                    $"CodeEditor/range hover: {theme}/{density}/{width} native pointer resolves the expanded grapheme marker");
                window.MouseMove(
                    editor.View.TranslatePoint(new Point((bidi[0].Right + bidi[^1].Left) / 2, bidi[0].Center.Y),
                        window)!.Value);
                Flush();
                check(ToolTip.GetTip(editor.View) == null,
                    $"CodeEditor/range hover: {theme}/{density}/{width} bidi gap has no diagnostic tooltip");
                window.MouseMove(
                    editor.View.TranslatePoint(new Point(before[^1].Right - .5, before[^1].Center.Y), window)!.Value);
                Flush();
                check(ToolTip.GetTip(editor.View) == null,
                    $"CodeEditor/range hover: {theme}/{density}/{width} previous wrap row cannot borrow the next row's marker");
            }
            finally
            {
                window.Close();
                Flush();
            }
        }

        SemanticPixels(check, output);
        ColoredGlyphGeometry(check);
    }

    private static void ColoredGlyphGeometry(Action<bool, string> check)
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var mode in new[] { 0, 1, 2 })
        {
            var source = mode == 0 ? "\"abc אבג e\u0301 👩‍💻 xyz\"" :
                mode == 1 ? "int שלום=1; // אבג e\u0301 👩‍💻" :
                "class widget { int שלום; int add(int value) { return שלום+value; } }";
            var editor = new CodeEditor
            {
                Text = source, CodeType = CodeType.CSharp, IsEnabled = false,
                IsSyntaxHighlightingEnabled = false, IsSemanticHighlightingEnabled = false, IsDiagnosticsEnabled = false
            };
            var window = Open(editor, 680, 220, theme);
            try
            {
                // Equal brushes isolate glyph placement from color differences.
                foreach (var kind in Enum.GetValues<CodeHighlightKind>())
                    editor.Resources["CodeEditor." + kind] = editor.Foreground;
                foreach (var kind in Enum.GetValues<CodeSemanticKind>())
                    editor.Resources["CodeEditor.Semantic" + kind] = editor.Foreground;
                Flush();
                using var native = window.CaptureRenderedFrame()!;
                if (mode < 2)
                {
                    editor.IsSyntaxHighlightingEnabled = true;
                }
                else
                {
                    editor.IsSemanticHighlightingEnabled = true;
                    Pump(editor.RefreshAnalysisAsync());
                }

                Flush();
                using var colored = window.CaptureRenderedFrame()!;
                var a = PixelBytes(native);
                var b = PixelBytes(colored);
                var max = 0;
                for (var n = 0; n < a.Length; n++) max = Math.Max(max, Math.Abs(a[n] - b[n]));
                check(max <= 3,
                    $"CodeEditor/glyph geometry: {theme}/{mode} colored bidi/graphemes match native layout positions (max channel delta {max})");
            }
            finally
            {
                window.Close();
                Flush();
            }
        }
    }

    private static void SemanticRoles(Action<bool, string> check)
    {
        const string source =
            "class widget { int field; public int Property { get; set; } int add(int argument) { int UpperLocal=argument; { string field=\"field\"; field.ToString(); } return field+UpperLocal+Property; } } widget item=new widget(); item.add(1); // argument";
        var analysis = CodeAnalysis.Create(source, CodeType.CSharp, default);

        void Role(string find, CodeSemanticKind kind, bool last = false)
        {
            var start = last
                ? source.LastIndexOf(find, StringComparison.Ordinal)
                : source.IndexOf(find, StringComparison.Ordinal);
            var spans = analysis.SemanticColors.ForRange(start, start + 1);
            check(spans.Length == 1 && spans[0].Kind == kind,
                $"CodeEditor/semantic color: {find} = {kind} (actual {string.Join(',', spans.ToArray().Select(s => s.Kind))})");
        }

        Role("widget", CodeSemanticKind.Type);
        Role("field;", CodeSemanticKind.Field);
        Role("Property {", CodeSemanticKind.Property);
        Role("add(", CodeSemanticKind.Method);
        Role("argument)", CodeSemanticKind.Parameter);
        Role("argument;", CodeSemanticKind.Parameter);
        Role("UpperLocal=", CodeSemanticKind.Variable);
        Role("field.ToString", CodeSemanticKind.Variable);
        Role("field+", CodeSemanticKind.Field);
        Role("add(", CodeSemanticKind.Method, true);
        Role("widget item", CodeSemanticKind.Type);
        var comment = source.LastIndexOf("argument", StringComparison.Ordinal);
        var literal = source.IndexOf("\"field\"", StringComparison.Ordinal);
        check(
            analysis.SemanticColors.ForRange(comment, source.Length).IsEmpty &&
            analysis.SemanticColors.ForRange(literal, literal + 7).IsEmpty,
            "CodeEditor/semantic color: comments and literals never acquire symbol colors");
        const string unknown = "int field=1; external.field;";
        check(
            CodeAnalysis.Create(unknown, CodeType.CSharp, default).SemanticColors
                .ForRange(unknown.LastIndexOf("field", StringComparison.Ordinal), unknown.Length).IsEmpty,
            "CodeEditor/semantic color: unresolved receivers do not borrow unrelated local bindings");
        foreach (var sample in new[]
                 {
                     (CodeType.TypeScript, "function f(value: number): number { let local=value; return local; }"),
                     (CodeType.Python, "def f(value: int) -> int:\n    local=value\n    return local")
                 })
        {
            var colors = CodeAnalysis.Create(sample.Item2, sample.Item1, default).SemanticColors;
            var position = sample.Item2.LastIndexOf("local", StringComparison.Ordinal);
            check(
                colors.ForRange(position, position + 5) is var spans && spans.Length == 1 &&
                spans[0].Kind == CodeSemanticKind.Variable,
                $"CodeEditor/semantic color: {sample.Item1} uses known lexical bindings");
        }
    }

    private static void SemanticPixels(Action<bool, string> check, string? output)
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var density in Enum.GetValues<CodeEditorDensity>())
        foreach (var width in new[] { 320, 680 })
        {
            var label = $"{theme}/{density}/{width}";
            var editor = new CodeEditor
            {
                CodeType = CodeType.CSharp,
                Text =
                    "class widget {\n int field;\n int add(int parameter) {\n   int UpperLocal=parameter;\n   return field+UpperLocal;\n }\n}",
                IsSemanticHighlightingEnabled = false, Density = density
            };
            var window = Open(editor, width, 360, theme);
            try
            {
                Flush();
                using var lexical = window.CaptureRenderedFrame()!;
                var shaped = editor.View.ShapedLayoutCount;
                editor.IsSemanticHighlightingEnabled = true;
                check(Pump(editor.RefreshAnalysisAsync()),
                    $"CodeEditor/semantic color: {label} publishes an independent map");
                Flush();
                using var semantic = window.CaptureRenderedFrame()!;
                check(
                    editor.View.ShapedLayoutCount == shaped && !PixelBytes(lexical).SequenceEqual(PixelBytes(semantic)),
                    $"CodeEditor/semantic color: {label} changes actual pixels without reshaping cached rows");
                Save(semantic, output, $"codeeditor-semantic-{theme}-{density}-{width}.png");
                editor.IsSyntaxHighlightingEnabled = false;
                Flush();
                check(!editor.SemanticColors.IsEmpty,
                    $"CodeEditor/semantic color: {label} lexical and semantic switches are independent");
                editor.IsDiagnosticsEnabled = false;
                check(
                    Pump(editor.RefreshAnalysisAsync()) && !editor.SemanticColors.IsEmpty &&
                    editor.Diagnostics.Count == 0,
                    $"CodeEditor/semantic color: {label} remains available with diagnostics disabled");
                editor.CaretIndex = editor.Text.Length;
                editor.FocusEditor();
                window.KeyTextInput(" ");
                Flush();
                check(editor.SemanticColors.IsEmpty,
                    $"CodeEditor/semantic color: {label} native edits immediately remove stale spans");
                check(Pump(editor.RefreshAnalysisAsync()) && !editor.SemanticColors.IsEmpty,
                    $"CodeEditor/semantic color: {label} recolors the new revision");
                editor.IsSemanticHighlightingEnabled = false;
                Flush();
                check(editor.SemanticColors.IsEmpty,
                    $"CodeEditor/semantic color: {label} disabling the layer removes all semantic spans");
                if (density == CodeEditorDensity.Normal && width == 680)
                {
                    editor.Text = string.Join('\n', Enumerable.Range(0, 10000).Select(i => $"int value{i}=1;"));
                    editor.IsSemanticHighlightingEnabled = true;
                    var pending = editor.RefreshAnalysisAsync();
                    editor.CaretIndex = 0;
                    editor.FocusEditor();
                    window.KeyTextInput(" ");
                    check(!Pump(pending) && editor.SemanticColors.IsEmpty,
                        $"CodeEditor/semantic color: {label} canceled large snapshots cannot publish stale colors");
                    editor.CodeType = CodeType.PlainText;
                    check(!Pump(editor.RefreshAnalysisAsync()) && editor.SemanticColors.IsEmpty,
                        $"CodeEditor/semantic color: {label} unsupported languages do not start color-only background work");
                }
            }
            finally
            {
                window.Close();
                Flush();
            }
        }
    }

    private static byte[] PixelBytes(Bitmap frame)
    {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            frame.CopyPixels(new PixelRect(frame.PixelSize), buffer, bytes.Length, frame.PixelSize.Width * 4);
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return bytes;
    }

    private static void CheckMarkerPixels(CodeEditor editor, Window window, Bitmap frame, Rect[] spans,
        Action<bool, string> check, string label)
    {
        var bytes = PixelBytes(frame);
        var origin = editor.View.TranslatePoint(default, window)!.Value;
        var scale = frame.PixelSize.Width / window.ClientSize.Width;
        var red = frame.Format == PixelFormat.Rgba8888 ? 0 : 2;
        var danger = ((ISolidColorBrush)editor.ResourceBrush("CodeEditor.Danger")).Color;

        bool Painted(double left, double right)
        {
            for (var y = (int)((origin.Y + spans[0].Bottom - 4) * scale); y < (origin.Y + spans[0].Bottom) * scale; y++)
            for (var x = (int)((origin.X + left) * scale); x < (origin.X + right) * scale; x++)
            {
                var i = (y * frame.PixelSize.Width + x) * 4;
                // One-pixel diagonals are antialiased. Detect the red chroma, not
                // an opaque pen pixel that fractional X positions need not produce.
                if (bytes[i + red] > bytes[i + 1] + 25 && bytes[i + red] > bytes[i + 2 - red] + 25) return true;
            }

            return false;
        }

        var ink = spans.Select(s => Painted(s.Left, s.Right)).ToArray();
        var gap = Painted(spans[0].Right + 1, spans[^1].Left - 1);
        check(ink.All(v => v) && !gap,
            $"CodeEditor/range pixels: {label} paints each bidi fragment and leaves the visual gap unmarked (ink={string.Join(',', ink)}, gap={gap}, bounds={string.Join(';', spans)}, origin={origin}, color={danger})");
    }
}