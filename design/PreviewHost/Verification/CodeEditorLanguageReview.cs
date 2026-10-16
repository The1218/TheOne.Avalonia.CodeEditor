using Avalonia.Headless;
using Avalonia.Styling;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    private static void LanguageReview(Action<bool, string> check, string? output)
    {
        const string screenshot =
            "namespace Test {\n    public class User{\n        public voidAdd(int a,int b)\n        {\n            return a+ b;\n        }\n    }\n}";

        CodeAnalysis Analyze(string text)
        {
            return CodeAnalysis.Create(text, CodeType.CSharp, default);
        }

        var malformed = Analyze(screenshot);
        var name = screenshot.IndexOf("voidAdd", StringComparison.Ordinal);
        check(
            malformed.Diagnostics.Any(d =>
                d.Code == "CSDECL003" && d.Start == name && d.Length == 7 && d.Line == 3 && d.Column == 16),
            "CodeEditor/review: screenshot missing return type is marked on voidAdd at 3:16");
        check(
            malformed.Symbols.Where(s => s.Name == "voidAdd" && s.Signature != null)
                .All(s => s.Signature!.ReturnType == null),
            "CodeEditor/review: modifiers never become a signature return type");
        foreach (var text in new[]
                 {
                     "class C { public F(int x) { return x; } }", "class C { F() {} }",
                     "class C { public static F<T>(T x) => x; }", "class C { [Obsolete] public F() {} }",
                     "class C { abstract F(); }", "public F(int x) { return x; }",
                     "class C { public F<T>(T x) where T : class { return x; } }",
                     "class C { public Wrong() : base() {} }"
                 })
            check(Analyze(text).Diagnostics.Any(d => d.Code == "CSDECL003"),
                $"CodeEditor/review: missing return type: {text}");
        foreach (var text in new[]
                 {
                     "class C { void F(value) {} }", "class C { void F(int a, b) {} }",
                     "class C { void F(int) {} }", "class C { void F(ref value) {} }"
                 })
            check(Analyze(text).Diagnostics.Any(d => d.Code == "CSPARAM002"),
                $"CodeEditor/review: incomplete parameter: {text}");
        foreach (var text in new[]
                 {
                     screenshot.Replace("voidAdd", "void Add"),
                     "class C { string Add(int a, int b) { return a + b; } }",
                     "class C { bool Add(int a, int b) => a + b; }", "class C { void F() => 1; }",
                     "class C { public C() { return 1; } }"
                 })
            check(Analyze(text).Diagnostics.Any(d => d.Code is "CSRETURN001" or "CSRETURN002"),
                $"CodeEditor/review: return contract: {text.Replace('\n', ' ')}");
        foreach (var text in new[]
                 {
                     screenshot.Replace("voidAdd", "int Add"),
                     "class C { public C(int a) {} static C() {} ~C() {} }",
                     "class C { public C() : base() {} }", "record C(int Value);",
                     "class C { public int voidAdd(int a, int b) => a + b; }",
                     "class C { int F() => 1; void G() { F(); } }",
                     "class C { [Obsolete] public T F<T>(T value) { return value; } }",
                     "interface I { int F(int value); }",
                     "class C { void F(params string[] values) {} }",
                     "class C { void F([Obsolete] int value) {} }",
                     "class C { void F() { System.Func<int, int> f = x => x; } }",
                     "class C { public static C operator +(C a, C b) => a; }"
                 })
        {
            var errors = Analyze(text).Diagnostics;
            check(errors.Count == 0,
                $"CodeEditor/review: valid declaration [{string.Join(';', errors.Select(d => d.Code + ':' + d.Message))}]: {text.Replace('\n', ' ')}");
        }

        foreach (var text in new[]
                 {
                     "class C { public int[] F(int[] values) => values; }",
                     "class C { [Obsolete] public int F([Obsolete] int value) => value; }",
                     "class C { void F() { lock(this) {} using(var item = new C()) {} } }",
                     "class C { C F() { return new C(); } }",
                     "class C { int F(int a, int b) { return (a + b) * 2; } }",
                     "class C { int F() => 1; void G() => F(); }"
                 })
        {
            var diagnostics = Analyze(text).Diagnostics;
            check(diagnostics.Count == 0,
                $"CodeEditor/review: declaration boundaries [{string.Join(';', diagnostics.Select(d => d.Code + ':' + d.Message))}]: {text}");
        }

        foreach (var text in new[]
                 {
                     "class C { int F() { System.Func<string> f = () => { return \"text\"; }; return 1; } }",
                     "class C { int F() { System.Func<string> f = delegate() { return \"text\"; }; return 1; } }"
                 })
            check(!Analyze(text).Diagnostics.Any(d => d.Code.StartsWith("CSRETURN", StringComparison.Ordinal)),
                "CodeEditor/review: anonymous function returns do not inherit the enclosing return type");
        check(Analyze("class C { int F() {} }").Diagnostics.Any(d => d.Code == "CSRETURN003"),
            "CodeEditor/review: empty non-void bodies need a return value");
        check(
            Analyze("class C { [Obsolete] public string F(int value) => value; }").Diagnostics
                .Any(d => d.Code == "CSRETURN002"),
            "CodeEditor/review: attributes do not hide an incompatible return type");
        foreach (var sample in new (CodeType Language, string Text)[]
                 {
                     (CodeType.TypeScript, "function f(a: number, b: number): string { return a + b; }"),
                     (CodeType.TypeScript, "const f = (a: number): string => { return a; };"),
                     (CodeType.Python, "def f(a: int, b: int) -> str:\n    return a + b"),
                     (CodeType.C, "bool f(int a, int b) { return a + b; }"),
                     (CodeType.Java, "class C { String f(int a, int b) { return a + b; } }"),
                     (CodeType.Go, "func f(a int, b int) string { return a + b }"),
                     (CodeType.Rust, "fn f(a: i32, b: i32) -> bool { return a + b; }"),
                     (CodeType.Kotlin, "fun f(a: Int, b: Int): String { return a + b }"),
                     (CodeType.Swift, "func f(a: Int, b: Int) -> String { return a + b }"),
                     (CodeType.Dart, "String f(int a, int b) { return a + b; }")
                 })
        {
            // C permits scalar-to-bool conversions.
            var diagnostics = CodeAnalysis.Create(sample.Text, sample.Language, default).Diagnostics;
            check(
                sample.Language == CodeType.C
                    ? !diagnostics.Any(d => d.Code.EndsWith("RETURN002", StringComparison.Ordinal))
                    : diagnostics.Any(d => d.Code.EndsWith("RETURN002", StringComparison.Ordinal)),
                $"CodeEditor/review: conservative primitive arithmetic in {sample.Language}");
        }

        const string overloads = "void Pick(int value) {} void Pick(string value) {} Pick(\"text\"";
        var help = Analyze(overloads).Signature(overloads.Length);
        check(help?.Signatures[help.ActiveSignature].Parameters[0].Type == "string",
            "CodeEditor/review: signature selects the overload matching the argument type");
        const string completion =
            "void Pick(int x, string value) {} void Pick(string x, int value) {} string valueAlpha=\"a\"; int valueBeta=1; Pick(\"text\", value";
        var items = Analyze(completion).Complete(completion.Length, "value").ToList();
        check(
            items.FindIndex(i => i.Label == "valueBeta") >= 0 && items.FindIndex(i => i.Label == "valueBeta") <
            items.FindIndex(i => i.Label == "valueAlpha"),
            "CodeEditor/review: overload selection improves the next argument completion order");
        const string declaration = "class C { void F(int value) {} }";
        check(Analyze(declaration).Signature(declaration.IndexOf("value", StringComparison.Ordinal)) == null,
            "CodeEditor/review: parameter declarations do not show invocation hints");

        var editor = new CodeEditor { CodeType = CodeType.CSharp, Text = screenshot, Header = "settings.json" };
        var window = Open(editor, 840, 370);
        try
        {
            check(Pump(editor.RefreshDiagnosticsAsync()) && editor.ErrorCount > 0,
                "CodeEditor/review: screenshot publishes an error even when the display filename is settings.json");
            editor.GoToNextDiagnostic();
            Flush();
            check(
                editor.SelectionStart == name && editor.SelectionLength == 7 &&
                editor.DiagnosticDetail.Contains("return type", StringComparison.Ordinal),
                "CodeEditor/review: error navigation selects the name and explains the missing return type");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Flush();
                Thread.Sleep(200);
                Flush();
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-missing-return-{theme}.png");
            }

            editor.Select(name, 7);
            editor.FocusEditor();
            window.KeyTextInput("int Add");
            Flush();
            check(editor.Diagnostics.Count == 0,
                "CodeEditor/review: editing invalidates old error markers immediately");
            check(Pump(editor.RefreshDiagnosticsAsync()) && editor.Diagnostics.Count == 0,
                "CodeEditor/review: native correction clears the error");
            editor.Undo();
            Flush();
            check(Pump(editor.RefreshDiagnosticsAsync()) && editor.Diagnostics.Any(d => d.Code == "CSDECL003"),
                "CodeEditor/review: undo restores the declaration error");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }
}