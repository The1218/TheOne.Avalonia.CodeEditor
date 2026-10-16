using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Input;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    internal static void Semantics(Action<bool, string> check, string? output = null)
    {
        var lazy = new CodeDocument(false);
        lazy.Reset("alpha\r\ne\u0301\nlast");
        check(lazy.Lines.All(l => !l.IsTextMaterialized) && lazy.MaxColumns == 5 && lazy.LineAt(7) == 1,
            "CodeEditor/snapshot: line indexing does not copy lazy snapshot text");
        check(lazy.Lines[1].Text == "e\u0301" && lazy.Lines[0].Delimiter == "\r\n" && !lazy.Lines[0].IsTextMaterialized,
            "CodeEditor/snapshot: Unicode and delimiters materialize independently");
        LanguageReview(check, output);

        CodeAnalysis Analyze(string text, CodeType language = CodeType.CSharp)
        {
            return CodeAnalysis.Create(text, language, default);
        }

        void Has(CodeType language, string text, string code)
        {
            var diagnostics = Analyze(text, language).Diagnostics;
            check(diagnostics.Any(d => d.Code.EndsWith(code, StringComparison.Ordinal)),
                $"CodeEditor/semantics: {language} {code}: {text.Replace('\n', ' ')} [{string.Join(';', diagnostics.Select(d => d.Code + ':' + d.Message))}]");
        }

        foreach (var sample in new (CodeType Language, string Text)[]
                 {
                     (CodeType.CSharp, "int F() { return missing; }"),
                     (CodeType.JavaScript, "function f() { return missing; }"),
                     (CodeType.TypeScript, "function f(): number { return missing; }"),
                     (CodeType.Python, "def f() -> int:\n    return missing"),
                     (CodeType.C, "int f() { return missing; }"), (CodeType.Cpp, "int f() { return missing; }"),
                     (CodeType.Java, "class C { int f() { return missing; } }"),
                     (CodeType.Go, "func f() int { return missing }"),
                     (CodeType.Rust, "fn f() -> i32 { return missing; }"),
                     (CodeType.Kotlin, "fun f(): Int { return missing }"),
                     (CodeType.Swift, "func f() -> Int { return missing }"),
                     (CodeType.Php, "function f(): int { return $missing; }"),
                     (CodeType.Ruby, "def f()\n  return missing\nend"), (CodeType.Dart, "int f() { return missing; }"),
                     (CodeType.Lua, "function f() return missing end")
                 }) Has(sample.Language, sample.Text, "NAME001");
        foreach (var sample in new (CodeType Language, string Text)[]
                 {
                     (CodeType.CSharp, "string value = \"bad\"; int count = value;"),
                     (CodeType.TypeScript, "let value: string = 'bad'; let count: number = value;"),
                     (CodeType.Python, "value: str = 'bad'\ncount: int = value"),
                     (CodeType.C, "int count = 1; count = \"bad\";"), (CodeType.Cpp, "int count = 1; count = \"bad\";"),
                     (CodeType.Java, "class C { void f() { int count=1; count=\"bad\"; } }"),
                     (CodeType.Go, "var value string = \"bad\"\nvar count int = value"),
                     (CodeType.Rust, "let value: bool = true; let count: i32 = value;"),
                     (CodeType.Kotlin, "val value: String = \"bad\"; val count: Int = value;"),
                     (CodeType.Swift, "let value: String = \"bad\"; let count: Int = value;"),
                     (CodeType.Dart, "String value='bad'; int count=value;")
                 }) Has(sample.Language, sample.Text, "TYPE003");
        foreach (var sample in new (CodeType Language, string Text)[]
                 {
                     (CodeType.CSharp, "int F(int count) { return \"bad\"; } F(\"bad\");"),
                     (CodeType.TypeScript, "function f(count: number): number { return 'bad'; } f('bad');"),
                     (CodeType.Python, "def f(count: int) -> int:\n    return 'bad'\nf('bad')"),
                     (CodeType.C, "int f(int count) { return \"bad\"; } f(\"bad\");"),
                     (CodeType.Cpp, "int f(int count) { return \"bad\"; } f(\"bad\");"),
                     (CodeType.Java, "class C { int f(int count) { return \"bad\"; } void g() { f(\"bad\"); } }"),
                     (CodeType.Go, "func f(count int) int { return \"bad\" }\nf(\"bad\")"),
                     (CodeType.Rust, "fn f(count: i32) -> i32 { return \"bad\"; } f(\"bad\");"),
                     (CodeType.Kotlin, "fun f(count: Int): Int { return \"bad\" }\nf(\"bad\")"),
                     (CodeType.Swift, "func f(count: Int) -> Int { return \"bad\" }\nf(count: \"bad\")"),
                     (CodeType.Php, "function f(int $count): int { return 'bad'; } f('bad');"),
                     (CodeType.Dart, "int f(int count) { return 'bad'; } f('bad');")
                 })
        {
            Has(sample.Language, sample.Text, "RETURN002");
            Has(sample.Language, sample.Text, "CALL001");
        }

        Has(CodeType.CSharp, "void F() { return 1; }", "RETURN001");
        Has(CodeType.CSharp, "int F() { return; }", "RETURN001");
        Has(CodeType.CSharp, "int F() => \"bad\";", "RETURN002");
        Has(CodeType.Kotlin, "fun f(): Int = \"bad\"", "RETURN002");
        Has(CodeType.TypeScript, "const f = (value: number): number => 'bad';", "RETURN002");
        Has(CodeType.CSharp, "void F(int count, string name=\"\") {} F(name: \"a\");", "CALL001");
        Has(CodeType.CSharp, "void F(int count) {} F(unknown: 1);", "CALL001");
        Has(CodeType.Python, "def f(count: int):\n    pass\nf(count=1, count=2)", "CALL001");
        Has(CodeType.CSharp, "void F() { { int count=1; } int value=count; }", "NAME001");
        Has(CodeType.CSharp, "void F() { int value=missing; int missing=1; }", "NAME001");
        Has(CodeType.CSharp, "int F() { return MissingValue; }", "NAME001");
        foreach (var sample in new (CodeType Language, string Text)[]
                 {
                     (CodeType.CSharp, "void F(int count, string name=\"\") {} F(name: \"a\", count: 1);"),
                     (CodeType.CSharp, "void F(int value) {} void F(string value) {} string text=\"a\"; F(text);"),
                     (CodeType.CSharp, "int F(int value) { return value; } string G(string value) { return value; }"),
                     (CodeType.CSharp, "void F(params string[] values) {} F(\"a\", \"b\");"),
                     (CodeType.CSharp, "int F() { return\n1; }"),
                     (CodeType.Python,
                         "def f(count: int, name: str='a') -> int:\n    return count\nf(name='b', count=1)"),
                     (CodeType.Python, "value=1\nvalue='dynamic'"),
                     (CodeType.Python, "def f() -> int:\n    return value\nvalue=1"),
                     (CodeType.Python, "values = [x for x in range(3)]"),
                     (CodeType.CSharp, "var values = new List<int>(); var result=values.Select(x => x.ToString());"),
                     (CodeType.CSharp, "var value = new ExternalType { name=\"a\" };"),
                     (CodeType.CSharp, "class C : ExternalBase { int F() { return inheritedValue; } }"),
                     (CodeType.CSharp,
                         "var values = new List<int>(); foreach(var item in values) { Console.WriteLine(item); }"),
                     (CodeType.CSharp,
                         "void F() { try {} catch(Exception error) { Console.WriteLine(error.Message); } }"),
                     (CodeType.CSharp,
                         "void F() { if(int.TryParse(\"1\", out var value)) { Console.WriteLine(value); } }"),
                     (CodeType.Go, "import \"fmt\"\nfunc f() string { return fmt.Sprint(1) }"),
                     (CodeType.JavaScript, "function f(a) {} f(); f(1, 2);"),
                     (CodeType.Lua, "function f(a) return a end\nf()\nf(1, 2)"),
                     (CodeType.C, "void f(void) {} f();"),
                     (CodeType.Java, "class C { void f(String... values) {} void g() { f(\"a\", \"b\"); } }"),
                     (CodeType.Swift, "func f(label value: Int) -> Int { return value }\nf(label: 1)"),
                     (CodeType.JavaScript, "function f() { if(true) { var value=1; } return value; }"),
                     (CodeType.TypeScript, "let value: number | string = 'a'; value=1;"),
                     (CodeType.TypeScript, "function f(): number | string { return 'a'; }"),
                     (CodeType.TypeScript, "const f = (value: number): number => value; f(1);"),
                     (CodeType.TypeScript, "function f(value?: string, ...rest: number[]) {} f(); f('a', 1, 2);"),
                     (CodeType.Cpp, "bool value=\"yes\"; char letter=65;"),
                     (CodeType.Kotlin, "fun f(value: Int): Int { return value }\nf(1)"),
                     (CodeType.Kotlin, "fun f(value: Int): Int { return value }\nf(value=1)"),
                     (CodeType.Swift, "func f(value: Int) -> Int { return value }\nf(value: 1)")
                 })
        {
            var errors = Analyze(sample.Text, sample.Language).Diagnostics;
            check(errors.Count == 0,
                $"CodeEditor/semantic accuracy: {sample.Language} [{string.Join(';', errors.Select(d => d.Code + ':' + d.Message))}] {sample.Text.Replace('\n', ' ')}");
        }

        var scoped = "void F() { { string hiddenValue=\"x\"; }\n hid\n int futureValue=1; }";
        check(
            Analyze(scoped).Complete(scoped.IndexOf("hid\n", StringComparison.Ordinal) + 3, "hid")
                .All(i => i.Label != "hiddenValue"), "CodeEditor/completion: closed blocks do not leak variables");
        var future = "void F() { fut\n int futureValue=1; }";
        check(
            Analyze(future).Complete(future.IndexOf("fut\n", StringComparison.Ordinal) + 3, "fut")
                .All(i => i.Label != "futureValue"), "CodeEditor/completion: later declarations are excluded");
        var shadow = "string value=\"text\"; void F() { int value=1; value. }";
        var members = Analyze(shadow).Complete(shadow.IndexOf("value. }", StringComparison.Ordinal) + 6, "");
        check(!members.Any(i => i.Label == "Substring"),
            "CodeEditor/completion: nearest binding determines receiver type");
        var expected = "void F(int count) {} string valueAlpha=\"a\"; int valueBeta=1; F(value";
        var items = Analyze(expected).Complete(expected.Length, "value");
        check(
            items.ToList().FindIndex(i => i.Label == "valueBeta") <
            items.ToList().FindIndex(i => i.Label == "valueAlpha"),
            "CodeEditor/completion: compatible argument variables rank ahead of incompatible variables");
        var editor = new CodeEditor { CodeType = CodeType.CSharp, Text = expected[..^1] };
        var window = Open(editor, 640, 360);
        try
        {
            editor.CaretIndex = editor.Text.Length;
            editor.FocusEditor();
            window.KeyTextInput("e");
            Pump(editor.RequestCompletionAsync());
            Flush();
            check(editor.InlineCompletionText == "Beta",
                "CodeEditor/inline: native typing previews the compatible local variable");
            var originalItems = editor.CompletionItems;
            var completed = 0;
            CodeWorkProbe.Observer = sample =>
            {
                if (sample.Operation == "completion" && !sample.Starting) Interlocked.Increment(ref completed);
            };
            try
            {
                Pump(editor.RequestCompletionAsync());
                Flush();
            }
            finally
            {
                CodeWorkProbe.Observer = null;
            }

            check(ReferenceEquals(originalItems, editor.CompletionItems) && completed == 1,
                "CodeEditor/completion: identical requests recompute semantic results while retaining popup rows");
            Press(window, Key.Tab, PhysicalKey.Tab);
            Flush();
            check(editor.Text == expected + "Beta", "CodeEditor/inline: native Tab accepts the ranked suffix");
            editor.Undo();
            check(editor.Text == expected, "CodeEditor/inline: accepting the semantic suggestion is one undo step");
            Pump(editor.RequestCompletionAsync());
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            check(!Pump(editor.RequestCompletionAsync(stop.Token)) && !editor.IsCompletionOpen,
                "CodeEditor/completion: canceled repeat requests clear the retained popup");
        }
        finally
        {
            window.Close();
            Flush();
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var observed = false;
        try
        {
            CodeAnalysis.Create("int value=1;", CodeType.CSharp, canceled.Token);
        }
        catch (OperationCanceledException)
        {
            observed = true;
        }

        check(observed, "CodeEditor/semantics: canceled snapshots stop analysis");
        const string recursive = "var value = value.ToString();\nvalue.";
        check(Analyze(recursive).Complete(recursive.Length, "").Count == 0,
            "CodeEditor/semantics: recursive initializer inference stays bounded and unknown");
        var annotations = Analyze("def f(value: int) -> int:\n    return 'a'\nf('a')", CodeType.Python).Diagnostics;
        check(annotations.Count >= 2 && annotations.All(d => d.Severity == CodeDiagnosticSeverity.Warning),
            "CodeEditor/semantics: Python argument and return annotations produce warnings");
        var large = string.Join('\n', Enumerable.Range(0, 10000).Select(i => $"int value{i}=1;"));
        var watch = Stopwatch.StartNew();
        var snapshot = Analyze(large);
        watch.Stop();
        check(snapshot.Diagnostics.Count == 0 && watch.ElapsedMilliseconds < 3000,
            $"CodeEditor/semantics: 10000 declarations analyze in {watch.Elapsed.TotalMilliseconds:F2}ms without errors");
        AnalysisCancellation(check, large);
    }

    private static void AnalysisCancellation(Action<bool, string> check, string large)
    {
        var observer = CodeWorkProbe.Observer;
        var thread = Environment.CurrentManagedThreadId;
        try
        {
            foreach (var phase in new[]
                     {
                         "analysis", "snapshot", "lex", "tokens", "pairs", "symbols", "scopes", "lookups",
                         "semantic-colors", "diagnostics",
                         "diagnostics-calls", "diagnostics-structure", "diagnostics-members", "diagnostics-language",
                         "diagnostics-declarations", "diagnostics-references"
                     })
            {
                using var source = new CancellationTokenSource();
                var reached = false;
                var canceled = false;
                long bytes = 0;
                long start = 0;
                CodeWorkProbe.Observer = sample =>
                {
                    if (sample.Thread == thread && sample.Starting && sample.Operation == phase)
                    {
                        reached = true;
                        bytes = GC.GetAllocatedBytesForCurrentThread();
                        start = Stopwatch.GetTimestamp();
                        source.Cancel();
                    }
                };
                try
                {
                    CodeAnalysis.Create(large, CodeType.CSharp, source.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                var afterCancelBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                check(reached && canceled && afterCancelBytes < 65536,
                    $"CodeEditor/cancellation: {phase} stops after {afterCancelBytes} bytes / {milliseconds:F2}ms without another bulk phase");
            }

            CodeWorkProbe.Observer = observer;
            using var canceledSource = new CancellationTokenSource();
            canceledSource.Cancel();
            var analysis = CodeAnalysis.Create("int value=1;\nval", CodeType.CSharp, default);
            var stopped = false;
            try
            {
                analysis.Complete(analysis.Document.Text.Length, "val", canceledSource.Token);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }

            check(stopped,
                "CodeEditor/cancellation: canceled completion requests do not enumerate document candidates");
        }
        finally
        {
            CodeWorkProbe.Observer = observer;
        }
    }
}