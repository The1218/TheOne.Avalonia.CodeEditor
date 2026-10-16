using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    private static void HighlightingPerformance(Action<bool, string> check, string? output)
    {
        var source = string.Join('\n',
            Enumerable.Range(0, 10000).Select(n =>
                $"const value{n} = `אבג ${{format(3)}} e\u0301 👩‍💻`; // nested syntax scopes and wrapped text"));
        var editor = new CodeEditor
        {
            Text = source, CodeType = CodeType.JavaScript, WordWrap = true, IsDiagnosticsEnabled = false,
            IsSemanticHighlightingEnabled = false
        };
        var window = Open(editor, 600, 700);
        try
        {
            var scroll = new List<double>();
            var input = new List<double>();
            var rows = new List<string> { "operation,index,ms,pause_ms,ui_bytes" };

            void Sample(string operation, int index, Action action, List<double> samples)
            {
                var pause = GC.GetTotalPauseDuration();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                action();
                Flush();
                watch.Stop();
                if (index >= 5) samples.Add(watch.Elapsed.TotalMilliseconds);
                rows.Add(FormattableString.Invariant(
                    $"{operation},{index},{watch.Elapsed.TotalMilliseconds:F3},{(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F3},{GC.GetAllocatedBytesForCurrentThread() - bytes}"));
            }

            for (var n = 0; n < 80; n++)
            {
                var line = 1 + n * 109 % 9900;
                Sample("scroll", n, () => editor.ScrollToLine(line), scroll);
            }

            editor.CaretIndex = editor.Document.Lines[5000].Start + 7;
            editor.FocusEditor();
            Flush();
            var tailVersion = editor.Document.Lines[9999].LexVersion;
            for (var n = 0; n < 40; n++) Sample("input", n, () => window.KeyTextInput("x"), input);
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "highlighting-samples.csv"), rows);
            }

            var s95 = Percentile(scroll, .95);
            var i95 = Percentile(input, .95);
            check(
                editor.View.CachedLayoutCount <= 192 && editor.Document.Lines[9999].LexVersion == tailVersion &&
                s95 < 16.7 && i95 < 16.7,
                $"CodeEditor/performance: 10000 wrapped interpolation lines scroll p95 {s95:F2}ms / input p95 {i95:F2}ms meet unchanged 16.7ms budget and bounded propagation");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    internal static void Highlighting(Action<bool, string> check, string? output)
    {
        static CodeDocument Lex(string text, CodeType type)
        {
            var doc = new CodeDocument();
            doc.Reset(text);
            CodeSyntax.Update(doc, type);
            return doc;
        }

        static CodeHighlightKind Color(CodeLine line, int at)
        {
            var kind = (CodeHighlightKind)line.Tokens.FirstOrDefault(t => at >= t.Start && at < t.Start + t.Length)
                .Kind;
            foreach (var span in line.Highlights)
                if (at >= span.Start && at < span.Start + span.Length)
                    kind = span.Kind;
            return kind;
        }

        void Scope(CodeType type, string text, string find, CodeHighlightKind expected, int line = 0)
        {
            var doc = Lex(text, type);
            var at = doc.Lines[line].Text.IndexOf(find, StringComparison.Ordinal);
            var actual = at < 0 ? CodeHighlightKind.Plain : Color(doc.Lines[line], at);
            check(at >= 0 && actual == expected, $"CodeEditor/scopes: {type}/{find} = {expected} (actual {actual})");
        }

        Scope(CodeType.CSharp, "Print(\"a\\n\\u0041\");", "Print", CodeHighlightKind.Function);
        Scope(CodeType.CSharp, "Print(\"a\\n\\u0041\");", "\\u0041", CodeHighlightKind.Escape);
        Scope(CodeType.CSharp, "var path = @\"C:\\temp\";", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.CSharp, "var path = \"\"\"C:\\temp\"\"\";", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.Cpp, "auto path = R\"(C:\\temp)\";", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.Rust, "let path = r#\"C:\\temp\"#;", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.Go, "path := `C:\\temp`", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.Python, "path = r'C:\\temp'", "\\temp", CodeHighlightKind.String);
        Scope(CodeType.JavaScript, "const p = /a[\\/]+/gi;", "/a", CodeHighlightKind.Regex);
        Scope(CodeType.Json, "{\"key\\u0041\": \"text\"}", "\\u0041", CodeHighlightKind.Escape);
        Scope(CodeType.JavaScript, "const n = value / amount;", "/", CodeHighlightKind.Punctuation);
        Scope(CodeType.CSharp, "var n = 1.ToString();", "ToString", CodeHighlightKind.Function);
        Scope(CodeType.CSharp, "var n = 1.25e-3;", "e-3", CodeHighlightKind.Number);
        Scope(CodeType.Html, "<div class=\"hi\">text</div>", "div", CodeHighlightKind.Tag);
        Scope(CodeType.Cpp, "#include <vector>", "include", CodeHighlightKind.Preprocessor);
        check(CodeAnalysis.Create("auto value = 1'000;", CodeType.Cpp, default).Diagnostics.Count == 0,
            "CodeEditor/scopes: C++ digit separators preserve valid numeric diagnostics");
        Scope(CodeType.Xml, "<Widget title=\"hi\" />", "Widget", CodeHighlightKind.Tag);
        Scope(CodeType.Xml, "<Widget title=\"hi\" />", "title", CodeHighlightKind.Attribute);
        Scope(CodeType.Xml, "<!-- <Widget title=\"hi\" /> -->", "Widget", CodeHighlightKind.Comment);
        Scope(CodeType.CSharp, "#region Code", "region", CodeHighlightKind.Preprocessor);
        Scope(CodeType.CSharp, "var s = $\"hello {GetValue(1)}\";", "GetValue", CodeHighlightKind.Function);
        Scope(CodeType.CSharp, "var s = $\"hello {GetValue(1)}\";", "{", CodeHighlightKind.Interpolation);
        Scope(CodeType.CSharp, "var s = $\"{{GetValue(1)}}\";", "GetValue", CodeHighlightKind.String);
        Scope(CodeType.CSharp, "var s = $@\"quote \"\" {GetValue(1)}\";", "GetValue", CodeHighlightKind.Function);
        Scope(CodeType.Python, "value = f'hello {format(3)}'", "format", CodeHighlightKind.Function);
        Scope(CodeType.JavaScript, "const s = `outer ${ { name: `inner ${call(1)}` }.name } end`;", "call",
            CodeHighlightKind.Function);
        Scope(CodeType.JavaScript, "const s = `outer ${ { name: `inner ${call(1)}` }.name } end`;", " end",
            CodeHighlightKind.String);
        Scope(CodeType.JavaScript, "const s = `outer ${\ncall(1)\n} end`;", "call", CodeHighlightKind.Function, 1);
        Scope(CodeType.JavaScript, "const s = `outer ${\n/* } fake() */\ncall(1)\n} end`;", "fake",
            CodeHighlightKind.Comment, 1);
        Scope(CodeType.JavaScript, "const s = `outer ${\n/* } fake() */\ncall(1)\n} end`;", "call",
            CodeHighlightKind.Function, 2);
        Scope(CodeType.CSharp, "/* comment\n*/ var s = $\"hi {call()}\"; finish();", "finish",
            CodeHighlightKind.Function, 1);
        Scope(CodeType.JavaScript, "const s = `outer ${ /* comment\n*/ `inner ${call()}` + finish() } end`;", "finish",
            CodeHighlightKind.Function, 1);
        Scope(CodeType.Markdown, "````csharp\nvar n = Calculate(3);\n````\n# Title", "Calculate",
            CodeHighlightKind.Function, 1);
        Scope(CodeType.Markdown, "````csharp\nvar n = 3;\n```\n````\n# Title", "Title", CodeHighlightKind.Heading, 4);
        Scope(CodeType.Markdown, "```unknown\ncall(3)\n```", "call", CodeHighlightKind.String, 1);
        Scope(CodeType.Markdown, "```js\nconst s = `hello ${call(3)}`;\n```", "call", CodeHighlightKind.Function, 1);
        Scope(CodeType.JavaScript, "// const s = `hello ${call(3)}`;", "call", CodeHighlightKind.Comment);
        check(CodeHighlighting.ScopeName(CodeHighlightKind.Escape) == "constant.character.escape",
            "CodeEditor/scopes: scope metadata follows TextMate naming");
        var fence = Lex("```unknown\nconst n = compute(3);\n```\nafter", CodeType.Markdown);
        var change = fence.Replace(3, 7, "js");
        CodeSyntax.Update(fence, CodeType.Markdown, change);
        check(
            Color(fence.Lines[1], fence.Lines[1].Text.IndexOf("compute", StringComparison.Ordinal)) ==
            CodeHighlightKind.Function,
            "CodeEditor/scopes: changing only the fence language invalidates following scope states");
        var random = new Random(7314);
        var doc = Lex("const s = `one ${call({ a: `two ${n}` })} end`;\n/* comment */\nfinish(2);",
            CodeType.JavaScript);
        var valid = true;
        var inserts = new[] { "x", "`", "${", "}", "\n", "/*", "*/", "\\", "\"", "" };
        for (var n = 0; n < 160; n++)
        {
            var start = random.Next(doc.Text.Length + 1);
            var length = random.Next(Math.Min(4, doc.Text.Length - start) + 1);
            var first = doc.Replace(start, length, inserts[random.Next(inserts.Length)]);
            CodeSyntax.Update(doc, CodeType.JavaScript, first);
            var fresh = Lex(doc.Text, CodeType.JavaScript);
            valid &= doc.Lines.Zip(fresh.Lines).All(p => p.First.Tokens.SequenceEqual(p.Second.Tokens) &&
                                                         p.First.Highlights.SequenceEqual(p.Second.Highlights) &&
                                                         p.First.EndHighlightState == p.Second.EndHighlightState);
        }

        check(valid, "CodeEditor/scopes: 160 seeded delimiter edits match complete re-tokenization");
        var stable = Lex("call(1);\nunchanged(2);\nfinal(3);", CodeType.CSharp);
        var version = stable.Lines[2].LexVersion;
        CodeSyntax.Update(stable, CodeType.CSharp, stable.Replace(0, 4, "other"));
        check(stable.Lines[2].LexVersion == version,
            "CodeEditor/scopes: stable outgoing states stop incremental propagation");
        var coarse = default(CodeLexState);
        var source = "const s = `value ${call(1)}`;";
        var original = CodeSyntax.Scan(source, CodeType.JavaScript, ref coarse);
        var highlighted = Lex(source, CodeType.JavaScript);
        check(original.SequenceEqual(highlighted.Lines[0].Tokens),
            "CodeEditor/scopes: visual interpolation scopes preserve semantic tokenization");
        check(
            CodeAnalysis.Create(source, CodeType.JavaScript, default).Document.Lines
                .All(l => l.Highlights.Length == 0 && l.EndHighlightState == default),
            "CodeEditor/scopes: semantic snapshots do not allocate unused visual scope data");
        var deep = "const s = " + string.Concat(Enumerable.Repeat("`a ${", 40)) + "value";
        check(Lex(deep, CodeType.JavaScript).Lines[0].Highlights.Length < deep.Length * 2,
            "CodeEditor/scopes: malformed deep interpolation has a bounded context stack");
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var density in Enum.GetValues<CodeEditorDensity>())
        foreach (var width in new[] { 320, 820 })
        {
            var editor = new CodeEditor
            {
                CodeType = CodeType.JavaScript,
                Text = "// Syntax scopes\nconst text = `hello ${format(3)} \\n`;\nconst regex = /[a-z]+/gi;",
                Density = density,
                WordWrap = true, IsSemanticHighlightingEnabled = false, IsDiagnosticsEnabled = false, IsEnabled = false
            };
            var window = Open(editor, width, 300, theme);
            try
            {
                using var before = window.CaptureRenderedFrame()!;
                editor.Resources["CodeEditor.Function"] = new SolidColorBrush(Colors.Magenta);
                Flush();
                using var after = window.CaptureRenderedFrame()!;
                check(!PixelBytes(before).SequenceEqual(PixelBytes(after)),
                    $"CodeEditor/scopes: {theme}/{density}/{width} scope brush changes actual pixels");
                Save(after, output, $"codeeditor-scopes-{theme}-{density}-{width}.png");
            }
            finally
            {
                window.Close();
                Flush();
            }
        }
    }
}