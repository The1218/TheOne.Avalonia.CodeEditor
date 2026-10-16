using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    public static void Verify(Action<bool, string> check, string? output = null)
    {
        VerifyFunctional(check, output);
        VerifyPerformance(check, output);
    }

    public static void VerifyPerformance(Action<bool, string> check, string? output = null)
    {
        Performance(check, output);
        RangePerformance(check, output);
        HighlightingPerformance(check, output);
    }

    public static void VerifyFunctional(Action<bool, string> check, string? output = null)
    {
        DocumentEdits(check);
        Input(check);
        LanguagesAndFormatting(check);
        Services(check);
        Intelligence(check, output);
        DiagnosticDetails(check, output);
        SnippetsAndMembers(check, output);
        LanguageDiagnostics(check);
        IdeEditing(check, output);
        CompactAndTab(check, output);
        Semantics(check, output);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var size in Enum.GetValues<CodeEditorDensity>())
            Visuals(theme, size, check, output);
        Demo(check, output);
        RangeAndSemanticRendering(check, output);
        Highlighting(check, output);
    }

    private static void DocumentEdits(Action<bool, string> check)
    {
        var random = new Random(3127);
        var doc = new CodeDocument();
        var reference = "alpha\r\nbeta\n中文😀\rlast";
        doc.Reset(reference);
        var inserts = new[] { "", "x", "\n", "\r\n", "hello\nworld", "中文", "😀", "\t", "\r" };
        var valid = true;
        for (var step = 0; step < 1500; step++)
        {
            var start = random.Next(reference.Length + 1);
            var length = random.Next(Math.Min(12, reference.Length - start) + 1);
            var value = inserts[random.Next(inserts.Length)];
            reference = reference[..start] + value + reference[(start + length)..];
            doc.Replace(start, length, value);
            valid &= doc.Text == reference &&
                     string.Concat(doc.Lines.Select(line => line.Text + line.Delimiter)) == reference;
            var offset = 0;
            foreach (var line in doc.Lines)
            {
                valid &= line.Start == offset;
                offset += line.Text.Length + line.Delimiter.Length;
            }

            var probe = random.Next(reference.Length + 1);
            var expected = doc.Lines.FindLastIndex(line => line.Start <= probe);
            valid &= doc.LineAt(probe) == expected;
        }

        check(valid,
            "CodeEditor/document: 1500 seeded incremental edits match a string oracle, mixed line endings and binary offset lookup");
        var words = new SortedSet<string>(doc.Lines.SelectMany(line => CodeDocument.Identifiers(line.Text)),
            StringComparer.OrdinalIgnoreCase);
        check(doc.Words.SetEquals(words),
            "CodeEditor/document: incremental completion word index releases removed identifiers");
        doc.Reset("a\nb\nc");
        var last = doc.Lines[2];
        doc.Replace(0, 1, "alpha");
        check(ReferenceEquals(last, doc.Lines[2]) && last.Start == 8,
            "CodeEditor/document: edits preserve untouched line identities and update their offsets");
        doc.Reset("/*\ncommentIdentifier\n*/\nvar actualIdentifier = \"literalIdentifier\";");
        CodeSyntax.Update(doc, CodeType.CSharp);
        check(
            doc.CodeWords.Contains("actualIdentifier") && !doc.CodeWords.Contains("commentIdentifier") &&
            !doc.CodeWords.Contains("literalIdentifier"),
            "CodeEditor/document: lexical identifier index excludes comments and literals");
        var changed = doc.Replace(0, 2, "//");
        CodeSyntax.Update(doc, CodeType.CSharp, changed);
        check(doc.CodeWords.Contains("commentIdentifier"),
            "CodeEditor/document: changed multiline lexical state updates following identifier rows");
        changed = doc.Replace(0, doc.Text.Length, "var remainingIdentifier = 1;");
        CodeSyntax.Update(doc, CodeType.CSharp, changed);
        check(doc.CodeWords.SetEquals(["remainingIdentifier"]),
            "CodeEditor/document: removed rows release lexical identifier counts");
        CodeSyntax.Update(doc, CodeType.PlainText, force: true);
        check(doc.CodeWords.Count == 0, "CodeEditor/document: language changes release prior lexical identifiers");
    }

    private static void Input(Action<bool, string> check)
    {
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var editor = new CodeEditor
        {
            Text = "start", Height = 260, Header = "sample.cs", ShowCopyButton = true, ShowFormatButton = true,
            CodeType = CodeType.CSharp
        };
        var window = Open(new StackPanel { Children = { before, editor, after } }, 800, 450);
        try
        {
            var modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            check(
                !editor.IsReadOnly && editor.ShowLineNumbers && editor.TabSize == 4 && editor.IsAutoIndentEnabled &&
                editor.IsCompletionEnabled,
                "CodeEditor/defaults: native editing, line numbers, auto-indent and completion enabled");
            editor.FocusEditor();
            editor.CaretIndex = 5;
            window.KeyTextInput("中文😀");
            Flush();
            check(editor.Text == "start中文😀" && editor.CaretColumn == 10,
                "CodeEditor/input: Unicode commits update Text and UTF-16 caret columns");
            Press(window, Key.Back, PhysicalKey.Backspace);
            check(editor.Text == "start中文", "CodeEditor/input: backspace removes a complete supplementary character");
            editor.Undo();
            check(editor.Text == "start中文😀", "CodeEditor/history: undo restores removed grapheme");
            editor.Redo();
            check(editor.Text == "start中文", "CodeEditor/history: redo reapplies the delta");
            editor.Text = "a\u0301😀\r\nend";
            editor.CaretIndex = 2;
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            check(editor.CaretIndex == 0, "CodeEditor/input: left navigation respects combining sequences");
            editor.CaretIndex = 6;
            Press(window, Key.Back, PhysicalKey.Backspace);
            check(editor.Text == "a\u0301😀end", "CodeEditor/input: deleting CRLF treats the line break as a unit");
            editor.Text = "long value\nx\nlong value";
            editor.CaretIndex = 8;
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            check(editor.CaretLine == 3 && editor.CaretColumn == 9,
                "CodeEditor/input: vertical navigation retains the preferred column across short lines");
            editor.Text = "{";
            editor.CaretIndex = 1;
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "{\n    " && editor.CaretColumn == 5,
                "CodeEditor/indent: Enter increases indentation after an opening brace");
            editor.Text = "";
            editor.CaretIndex = 0;
            window.KeyTextInput("{");
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "{\n    \n}" && editor.CaretLine == 2 && editor.CaretColumn == 5,
                "CodeEditor/pairs: opening and closing braces surround an indented empty line");
            editor.Undo();
            check(editor.Text == "{}", "CodeEditor/history: smart newline is one undo transaction");
            editor.Text = "";
            editor.CaretIndex = 0;
            window.KeyTextInput("(");
            window.KeyTextInput(")");
            check(editor.Text == "()" && editor.CaretIndex == 2,
                "CodeEditor/pairs: typing an auto-inserted closer skips it");
            editor.Text = "";
            editor.CaretIndex = 0;
            window.KeyTextInput("(");
            Press(window, Key.Back, PhysicalKey.Backspace);
            check(editor.Text == "", "CodeEditor/pairs: Backspace removes an empty auto-inserted pair");
            window.KeyTextInput("(");
            window.KeyTextInput("x");
            Press(window, Key.Back, PhysicalKey.Backspace);
            check(editor.Text == "()", "CodeEditor/pairs: deleting pair contents preserves the tracked closer");
            editor.Text = "    ";
            editor.CaretIndex = 4;
            Press(window, Key.Back, PhysicalKey.Backspace);
            check(editor.Text == "", "CodeEditor/indent: Backspace removes one leading indentation stop");
            editor.Text = "alpha\nbeta";
            editor.Select(0, editor.Text.Length);
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(editor.Text == "    alpha\n    beta",
                "CodeEditor/indent: selection indentation updates all selected logical lines");
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            check(editor.Text == "alpha\nbeta", "CodeEditor/indent: Shift+Tab outdents selected lines");
            editor.CodeType = CodeType.Python;
            editor.Text = "def greet():";
            editor.CaretIndex = editor.Text.Length;
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "def greet():\n    ", "CodeEditor/indent: Python colon starts an indented block");
            editor.CodeType = CodeType.CSharp;
            editor.Text = "read only sample";
            editor.Select(5, 4);
            var selected = editor.SelectedText;
            var caret = editor.CaretIndex;
            editor.IsReadOnly = true;
            window.KeyTextInput("blocked");
            Press(window, Key.Delete, PhysicalKey.Delete);
            Press(window, Key.Z, PhysicalKey.Z, modifier);
            editor.ReplaceSelection("blocked");
            editor.IndentSelection();
            check(
                editor.Text == "read only sample" && !editor.UndoCommand.CanExecute(null) &&
                !Pump(editor.FormatDocumentAsync()),
                "CodeEditor/readonly: text, deletion, history, indentation and formatting are blocked");
            var clipboard = window.Clipboard!;
            clipboard.SetTextAsync("sentinel").GetAwaiter().GetResult();
            var copy = Part<Button>(editor, "PART_CopyButton");
            Click(window, copy);
            check(
                clipboard.TryGetTextAsync().GetAwaiter().GetResult() == editor.Text &&
                editor.SelectedText == selected && editor.CaretIndex == caret,
                "CodeEditor/clipboard: toolbar click copies the complete readonly document and preserves partial selection and caret");
            editor.FocusEditor();
            Press(window, Key.C, PhysicalKey.C, modifier);
            check(clipboard.TryGetTextAsync().GetAwaiter().GetResult() == selected,
                "CodeEditor/clipboard: platform shortcut copies the selection");
            clipboard.SetTextAsync("paste").GetAwaiter().GetResult();
            Press(window, Key.V, PhysicalKey.V, modifier);
            Press(window, Key.X, PhysicalKey.X, modifier);
            check(editor.Text == "read only sample", "CodeEditor/readonly: paste and cut cannot modify content");
            editor.IsReadOnly = false;
            editor.Select(5, 4);
            Pump(editor.PasteAsync());
            check(editor.Text == "read paste sample",
                "CodeEditor/clipboard: editable paste replaces the current selection");
            editor.IsEnabled = false;
            Flush();
            check(!copy.IsEffectivelyEnabled && !editor.CopyAllCommand.CanExecute(null),
                "CodeEditor/commands: disabled editor disables toolbar copy");
            editor.IsEnabled = true;
            editor.Text = null;
            Flush();
            check(editor.LineCount == 1 && !copy.IsEffectivelyEnabled,
                "CodeEditor/text: null is an empty single-line document with copy disabled");
            editor.Text = "first\nsecond";
            editor.FocusEditor();
            editor.CaretIndex = 8;
            var client = editor.View.InputClientForVerification;
            check(
                client.SupportsPreedit && client.SupportsSurroundingText && client.SurroundingText == "second" &&
                client.Selection.Start == 2,
                "CodeEditor/IME: client exposes current-line surrounding text and zero-based selection");
            client.SetPreeditText("拼音", 1);
            check(editor.Text == "first\nsecond" && client.CursorRectangle.Height > 0,
                "CodeEditor/IME: composition is drawn separately without mutating document history");
            window.KeyTextInput("中文");
            Flush();
            check(editor.Text == "first\nse中文cond",
                "CodeEditor/IME: committed Unicode text replaces the composition once");
            editor.FocusEditor();
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
            check(after.IsFocused, "CodeEditor/focus: Ctrl+Tab exits the editor");
            editor.FocusEditor();
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
            check(before.IsFocused, "CodeEditor/focus: Ctrl+Shift+Tab exits in reverse order");
            check(editor.FindNext("中文") && editor.SelectedText == "中文",
                "CodeEditor/find: matching text is selected and scrolled into view");
            var document = editor.Document;
            var text = editor.Text;
            var template = editor.Template;
            editor.Template = null;
            editor.ApplyTemplate();
            editor.Template = template;
            editor.ApplyTemplate();
            Flush();
            check(
                ReferenceEquals(document, editor.Document) && editor.Text == text &&
                editor.UndoCommand.CanExecute(null) &&
                editor.GetVisualDescendants().OfType<CodeTextSurface>().Count() == 1,
                "CodeEditor/lifecycle: retemplating retains one surface, selection and undo history");
            editor.ShowCopyButton = false;
            editor.ShowFormatButton = false;
            editor.Header = null;
            Flush();
            check(!Part<Border>(editor, "PART_Header").IsVisible,
                "CodeEditor/chrome: header collapses when all header content is hidden");
            editor.CodeType = (CodeType)999;
            check(editor.HighlightingError != null && editor.LanguageName == "Plain text",
                "CodeEditor/fallback: undefined language is diagnostic plain text");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void LanguagesAndFormatting(Action<bool, string> check)
    {
        CompletionAndLiteralAccuracy(check);
        var model = new CodeEditorDemoViewModel();
        foreach (var type in Enum.GetValues<CodeType>())
        {
            model.SelectedCodeType = type;
            model.LoadSample();
            var document = new CodeDocument();
            document.Reset(model.Code!);
            CodeSyntax.Update(document, type, force: true);
            check(
                type == CodeType.PlainText
                    ? document.Lines.All(l => l.Tokens.Length == 0)
                    : document.Lines.Any(l => l.Tokens.Any(t => t.Kind != CodeTokenKind.Plain)),
                $"CodeEditor/language: {type} has explicit managed lexical rules");
            var formatted = CodeFormatting.Format(new CodeFormattingContext(model.Code!, type, 4, true, "\n"), default);
            var again = CodeFormatting.Format(new CodeFormattingContext(formatted, type, 4, true, "\n"), default);
            check(formatted == again, $"CodeEditor/format: {type} formatting is idempotent on its sample");
        }

        var state = default(CodeLexState);
        var tokens = CodeSyntax.Scan("var text = \"a\\\" // literal\"; // comment", CodeType.CSharp, ref state);
        check(
            tokens.Count(t => t.Kind == CodeTokenKind.Comment) == 1 &&
            tokens.Count(t => t.Kind == CodeTokenKind.String) == 1,
            "CodeEditor/lexer: escaped quotes keep comment markers inside strings");
        var doc = new CodeDocument();
        doc.Reset("/* start\ncontinued */ var value = 1;\nreturn value;");
        CodeSyntax.Update(doc, CodeType.CSharp, force: true);
        check(
            doc.Lines[1].Tokens.Any(t => t.Kind == CodeTokenKind.Comment) &&
            doc.Lines[1].Tokens.Any(t => t.Kind == CodeTokenKind.Keyword),
            "CodeEditor/lexer: multiline comments resume code after the terminator");
        doc.Replace(0, 2, "//");
        CodeSyntax.Update(doc, CodeType.CSharp);
        check(doc.Lines[1].StartState.End == null,
            "CodeEditor/lexer: edits invalidate downstream multiline lexical state");
        const string significant =
            "def greet():\n    value = '''first\n  exact whitespace  \nlast'''\n    return value\n";
        check(
            CodeFormatting.Format(new CodeFormattingContext(significant, CodeType.Python, 4, true, "\n"), default) ==
            significant, "CodeEditor/format: Python indentation and multiline string payload remain unchanged");
        const string yaml = "payload: |\n    keep  \n      whitespace\n";
        check(CodeFormatting.Format(new CodeFormattingContext(yaml, CodeType.Yaml, 4, true, "\n"), default) == yaml,
            "CodeEditor/format: YAML block-scalar whitespace is data");
        const string heredoc = "cat <<'EOF'\n  exact payload  \nEOF\n";
        check(
            CodeFormatting.Format(new CodeFormattingContext(heredoc, CodeType.Bash, 4, true, "\n"), default) == heredoc,
            "CodeEditor/format: shell heredoc payload whitespace remains unchanged");
        var python =
            CodeFormatting.Format(new CodeFormattingContext("value=1\nitems=[1,2]\n", CodeType.Python, 4, true, "\n"),
                default);
        check(python == "value = 1\nitems = [1, 2]\n",
            "CodeEditor/format: Python assignment and comma spacing preserves indentation");
        var javascript =
            CodeFormatting.Format(
                new CodeFormattingContext("function run(){const rule=/[{}]/g;return {value:1};}", CodeType.JavaScript,
                    4, true, "\n"), default);
        check(
            javascript.Contains("\n    const rule = /[{}]/g;", StringComparison.Ordinal) &&
            javascript.Contains("return {", StringComparison.Ordinal),
            "CodeEditor/format: JavaScript block formatting protects regex and return-expression placement");
        var json = CodeFormatting.Format(
            new CodeFormattingContext("{\"a\":1,\"b\":[true,false]}", CodeType.Json, 2, true, "\n"), default);
        check(json.Contains("\n  \"a\": 1", StringComparison.Ordinal),
            "CodeEditor/format: JSON parsing expands compact objects using the configured indent");
        var code = CodeFormatting.Format(
            new CodeFormattingContext("class Sample{void Run(){var value=\"{ ; }\";}}", CodeType.CSharp, 4, true, "\n"),
            default);
        check(
            code.Contains("\n    void Run()", StringComparison.Ordinal) &&
            code.Contains("\"{ ; }\"", StringComparison.Ordinal),
            "CodeEditor/format: structural C# formatting expands blocks without touching string payloads");
    }

    private static void Services(Action<bool, string> check)
    {
        var editor = new CodeEditor { Text = "ret", CodeType = CodeType.CSharp };
        var window = Open(editor, 700, 400);
        try
        {
            editor.FocusEditor();
            editor.CaretIndex = 3;
            check(Pump(editor.RequestCompletionAsync()) && editor.CompletionItems.Any(i => i.Label == "return"),
                "CodeEditor/completion: language prefix opens keyword completion");
            var item = editor.CompletionItems.First(i => i.Label == "return");
            editor.AcceptCompletion(item);
            check(editor.Text == "return" && editor.CaretIndex == 6 && !editor.IsCompletionOpen,
                "CodeEditor/completion: accepting an item replaces only the typed prefix");
            editor.Undo();
            check(editor.Text == "ret", "CodeEditor/completion: acceptance is a single undoable edit");
            Pump(editor.RequestCompletionAsync());
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "return",
                "CodeEditor/completion: native Enter accepts the selected completion without inserting a newline");
            editor.Text = "var documentIdentifier = 1;\ndocumentI";
            editor.CaretIndex = editor.Text.Length;
            check(
                Pump(editor.RequestCompletionAsync()) &&
                editor.CompletionItems.Any(i => i.Label == "documentIdentifier"),
                "CodeEditor/completion: document identifier index supplies local symbols");
            editor.Text = "// ret";
            editor.CaretIndex = editor.Text.Length;
            check(!Pump(editor.RequestCompletionAsync()),
                "CodeEditor/completion: comments suppress default suggestions");
            editor.Text = "i";
            editor.CaretIndex = 1;
            Pump(editor.RequestCompletionAsync());
            var snippet = editor.CompletionItems.First(i => i.Label == "if…");
            editor.AcceptCompletion(snippet);
            check(editor.Text!.StartsWith("if ()", StringComparison.Ordinal) && editor.CaretIndex == 4,
                "CodeEditor/completion: snippets insert blocks with an explicit caret stop");
            var provider = new DeferredCompletion();
            editor.CompletionProvider = provider;
            editor.Text = "ab";
            editor.CaretIndex = 2;
            var pending = editor.RequestCompletionAsync();
            editor.ReplaceSelection("c");
            provider.Result.SetResult([new CodeCompletionItem("obsolete", "obsolete")]);
            check(!Pump(pending) && !editor.IsCompletionOpen && editor.Text == "abc",
                "CodeEditor/completion: asynchronous stale provider results cannot reopen or replace newer text");
            provider = new DeferredCompletion();
            editor.CompletionProvider = provider;
            editor.Text = "mat";
            editor.CaretIndex = 3;
            pending = editor.RequestCompletionAsync();
            provider.Result.SetResult([
                new CodeCompletionItem("matching", "matching"), new CodeCompletionItem("unrelated", "unrelated")
            ]);
            check(Pump(pending) && editor.CompletionItems.Select(i => i.Label).SequenceEqual(["matching"]),
                "CodeEditor/relevance: custom providers cannot display unrelated labels");
            editor.CompletionProvider = null;
            editor.CodeType = CodeType.Json;
            editor.Text = "{\"value\":42}";
            check(Pump(editor.FormatDocumentAsync()) && editor.Text!.Contains('\n'),
                "CodeEditor/format: asynchronous formatting applies a fresh snapshot");
            editor.Undo();
            check(editor.Text == "{\"value\":42}", "CodeEditor/format: formatting has one undo transaction");
            editor.Text = "{broken";
            var invalid = editor.Text;
            check(!Pump(editor.FormatDocumentAsync()) && editor.Text == invalid && editor.FormattingError != null,
                "CodeEditor/format: invalid JSON reports an error and preserves the document");
            var formatter = new DeferredFormatter();
            editor.Formatter = formatter;
            editor.Text = "before";
            var format = editor.FormatDocumentAsync();
            editor.CaretIndex = 6;
            editor.ReplaceSelection(" newer");
            formatter.Result.SetResult("obsolete");
            check(!Pump(format) && editor.Text == "before newer",
                "CodeEditor/format: asynchronous stale formatting never overwrites subsequent edits");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void Intelligence(Action<bool, string> check, string? output)
    {
        CodeAnalysis Analyze(string text, CodeType type)
        {
            return CodeAnalysis.Create(text, type, default);
        }

        IReadOnlyList<CodeCompletionItem> Members(string text, CodeType type, string prefix = "")
        {
            return Analyze(text, type).Complete(text.Length, prefix);
        }

        var csharp =
            "class Person { public string Name { get; set; } public string Greet(string name, int count = 1) { return name; } }\nvar p = new Person();\np.";
        var members = Members(csharp, CodeType.CSharp);
        check(
            members.Any(i => i.Label == "Name" && i.Kind == CodeCompletionKind.Property) &&
            members.Any(i => i.Label == "Greet" && i.Kind == CodeCompletionKind.Method),
            "CodeEditor/intelligence: C# document types expose instance properties and methods");
        check(!members.Any(i => i.Label == "return"),
            "CodeEditor/intelligence: member completion excludes unrelated document words and keywords");
        check(Members("var message = \"hello\";\nmessage.Sub", CodeType.CSharp, "Sub").Any(i => i.Label == "Substring"),
            "CodeEditor/intelligence: inferred string variables expose filtered standard members");
        check(Members("var p = new { Name = \"Ada\", Age = 20 };\np.", CodeType.CSharp).Any(i => i.Label == "Name"),
            "CodeEditor/intelligence: anonymous object properties are indexed");
        const string py =
            "class Person:\n    def __init__(self):\n        self.name = 'Ada'\n    def greet(self, name: str, count: int = 1) -> str:\n        return name\np = Person()\np.";
        check(
            Members(py, CodeType.Python).Any(i => i.Label == "greet") &&
            Members(py, CodeType.Python).Any(i => i.Label == "name"),
            "CodeEditor/intelligence: Python classes expose methods and self attributes");
        check(
            Members("message = 'hello'\nmessage.", CodeType.Python).Any(i => i.Label == "upper") &&
            Members("items = []\nitems.", CodeType.Python).Any(i => i.Label == "append"),
            "CodeEditor/intelligence: Python inferred strings and lists expose built-in members");
        check(
            Members("const user = { name: 'Ada', greet(name) { return name; } };\nuser.", CodeType.JavaScript)
                .Any(i => i.Label == "greet"),
            "CodeEditor/intelligence: JavaScript object methods are available without an external provider");
        check(Members("document.querySelector('main').", CodeType.TypeScript).Any(i => i.Label == "setAttribute"),
            "CodeEditor/intelligence: known return types support chained member completion");
        const string arrow = "const add = (a: number, b: number) => a + b;\nadd(1, ";
        var arrowHelp = Analyze(arrow, CodeType.TypeScript).Signature(arrow.Length);
        check(arrowHelp?.ActiveParameter == 1 && arrowHelp.Signatures[0].Parameters[1].Name == "b",
            "CodeEditor/signature: TypeScript arrow functions retain named typed parameters");
        var call = csharp[..^2] + "p.Greet(\"a,b\", Math.Max(1, 2), ";
        var help = Analyze(call, CodeType.CSharp).Signature(call.Length);
        check(
            help?.ActiveParameter == 2 && help.Signatures[0].Name == "Greet" &&
            help.Signatures[0].Parameters[1].DefaultValue == "1",
            "CodeEditor/signature: document methods retain defaults and ignore commas in literals and nested calls");
        var pythonCall = py[..^2] + "p.greet('Ada', ";
        var pyHelp = Analyze(pythonCall, CodeType.Python).Signature(pythonCall.Length);
        check(
            pyHelp?.ActiveParameter == 1 && pyHelp.Signatures[0].Parameters.Count == 2 &&
            pyHelp.Signatures[0].ReturnType == "str",
            "CodeEditor/signature: Python hints exclude self and retain annotations and return types");
        check(Members("unknownThing.", CodeType.CSharp).Count == 0,
            "CodeEditor/intelligence: unknown external objects do not receive invented members");

        string Format(string text, CodeType type, int size = 4)
        {
            return CodeFormatting.Format(new CodeFormattingContext(text, type, size, true, "\n"), default);
        }

        var python = Format("def greet(name:str):\n  if name:\n    return {'name':name,'items':[1,2]}\n  return None\n",
            CodeType.Python);
        check(
            python.Contains("\n    if name:\n        return {'name': name, 'items': [1, 2]}\n    return None",
                StringComparison.Ordinal) && Format(python, CodeType.Python) == python,
            "CodeEditor/format: Python normalizes block indentation, annotations and collections idempotently");
        const string yaml =
            "server:\n  ports: [80,443]\n  url: https://example.com\n  text: |\n    literal:  unchanged  \n      nested\n";
        var formattedYaml = Format(yaml, CodeType.Yaml);
        check(
            formattedYaml.Contains("\n    ports: [80, 443]", StringComparison.Ordinal) &&
            formattedYaml.Contains("https://example.com", StringComparison.Ordinal) &&
            formattedYaml.Contains("literal:  unchanged  ", StringComparison.Ordinal) &&
            Format(formattedYaml, CodeType.Yaml) == formattedYaml,
            "CodeEditor/format: YAML normalizes maps and flow collections while protecting URLs and block scalar data");
        check(
            Format("data: {url: https://host, time: 12:30}\n", CodeType.Yaml) ==
            "data: {url: https://host, time: 12:30}\n",
            "CodeEditor/format: YAML flow values retain URL and time colons");
        var sequence = Format("items:\n  - name: Ada\n    age: 20\n    tags:\n      - one\n      - two\n",
            CodeType.Yaml);
        check(
            sequence.Contains("\n    - name: Ada\n      age: 20\n      tags:", StringComparison.Ordinal) &&
            Format(sequence, CodeType.Yaml) == sequence,
            "CodeEditor/format: YAML compact sequence mappings keep sibling keys aligned after changing indentation");
        const string html =
            "<!doctype html><html><body><div><img src=\"a>b.png\"><span>Hello <b>world</b> !</span></div><script>if (a < b) { x=1; }</script><pre>  exact\n    spaces  </pre></body></html>";
        var formattedHtml = Format(html, CodeType.Html, 2);
        check(
            formattedHtml.Contains("\n    <div><img", StringComparison.Ordinal) &&
            formattedHtml.Contains("<span>Hello <b>world</b> !</span>", StringComparison.Ordinal) &&
            formattedHtml.Contains("<pre>  exact\n    spaces  </pre>", StringComparison.Ordinal) &&
            Format(formattedHtml, CodeType.Html, 2) == formattedHtml,
            "CodeEditor/format: HTML expands structure while preserving void tags, quoted attributes, mixed text and raw blocks");
        check(
            Format("<div><span>a</span><span>b</span></div>", CodeType.Html) ==
            "<div><span>a</span><span>b</span></div>",
            "CodeEditor/format: HTML inline siblings retain significant absence of whitespace");
        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "var x = ;\nif (true] {", "SYN004"),
                     (CodeType.Python, "def broken()\nreturn 1\n", "PY004"),
                     (CodeType.Python, "if True:\nprint('x')", "PY001"), (CodeType.Yaml, "key: 1\nkey: 2\n", "YAML002"),
                     (CodeType.Html, "<div><span>x</div>", "HTML001"),
                     (CodeType.Json, "{\"中文\": 1, broken}", "JSON001"), (CodeType.Xml, "<root><x></root>", "XML001"),
                     (CodeType.CSharp, "var text = \"unfinished", "SYN005")
                 })
        {
            var errors = Analyze(sample.Item2, sample.Item1).Diagnostics;
            check(
                errors.Any(d => d.Code == sample.Item3) && errors.All(d =>
                    d.Start >= 0 && d.Start <= sample.Item2.Length && d.Line > 0 && d.Column > 0),
                $"CodeEditor/diagnostics: {sample.Item1} reports {sample.Item3} with source spans");
        }

        check(
            Analyze("def good(name):\n    return {'value': name}\n", CodeType.Python).Diagnostics.Count == 0 &&
            !Analyze(html, CodeType.Html).Diagnostics.Any(d => d.Severity == CodeDiagnosticSeverity.Error),
            "CodeEditor/diagnostics: valid Python and tolerant HTML have no errors (ambient JavaScript names may warn)");
        check(Analyze("payload: |\n  [literal is data\nname: Joe's text\n", CodeType.Yaml).Diagnostics.Count == 0,
            "CodeEditor/diagnostics: YAML block scalars and plain apostrophes do not create false lexical errors");
        check(
            Analyze("items: [1, 2\n", CodeType.Yaml).Diagnostics.Any(d => d.Code == "YAML003") &&
            Analyze("def f(a):\n    return a\nf()", CodeType.Python).Diagnostics.Any(d => d.Code == "CALL001"),
            "CodeEditor/diagnostics: unclosed YAML flow collections and known-call argument counts are checked");
        check(Analyze("def good(\n    name: str,\n):\n    return name\n", CodeType.Python).Diagnostics.Count == 0,
            "CodeEditor/diagnostics: multiline Python signatures retain block context");
        var editor = new CodeEditor { Text = csharp + "Gre", CodeType = CodeType.CSharp, Header = "intelligence.cs" };
        var window = Open(editor, 760, 420);
        try
        {
            editor.FocusEditor();
            editor.CaretIndex = editor.Text.Length;
            Pump(editor.RequestCompletionAsync());
            var method = editor.CompletionItems.Single(i => i.Label == "Greet");
            check(editor.AcceptCompletion(method) && editor.Text!.EndsWith("p.Greet()", StringComparison.Ordinal),
                "CodeEditor/intelligence UI: method acceptance inserts one parenthesis pair");
            check(Pump(editor.RequestSignatureHelpAsync()) && editor.SignatureHelp?.ActiveParameter == 0,
                "CodeEditor/intelligence UI: accepted methods display active parameter hints");
            window.KeyTextInput("\"Ada\", ");
            Pump(editor.RequestSignatureHelpAsync());
            check(editor.SignatureHelp?.ActiveParameter == 1,
                "CodeEditor/intelligence UI: typing arguments updates the active parameter");
            using (var frame = window.CaptureRenderedFrame()!)
            {
                Save(frame, output, "codeeditor-signature.png");
            }

            editor.Text = "var broken = ;\nif (true] {";
            Pump(editor.RefreshDiagnosticsAsync());
            check(editor.ErrorCount >= 2 && Part<Button>(editor, "PART_Diagnostics").IsVisible,
                "CodeEditor/diagnostics UI: built-in errors reach the status button and public collection");
            editor.CaretIndex = 0;
            Press(window, Key.F8, PhysicalKey.F8);
            check(editor.SelectionLength > 0 && editor.SelectionStart == editor.Diagnostics[0].Start,
                "CodeEditor/diagnostics UI: F8 selects and reveals the next problem");
            editor.CaretIndex = 0;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in Enum.GetValues<CodeEditorDensity>())
            {
                window.RequestedThemeVariant = theme;
                editor.Density = size;
                foreach (var width in new[] { 720d, 320d })
                {
                    window.Width = width;
                    Flush();
                    using var frame = window.CaptureRenderedFrame()!;
                    Save(frame, output, $"codeeditor-diagnostics-{theme}-{size}-{width}.png");
                    check(editor.View.LastRenderedRows > 0,
                        $"CodeEditor/diagnostics drawing: {theme}/{size}/{width} paints visible rows");
                }
            }

            editor.Text = "var fine = 1;";
            check(
                editor.Diagnostics.Count == 0 && ToolTip.GetTip(editor.View) == null &&
                Pump(editor.RefreshDiagnosticsAsync()) && editor.ErrorCount == 0,
                "CodeEditor/diagnostics: edits clear obsolete spans, tooltips and accessibility help, and valid analysis removes errors");
            editor.IsDiagnosticsEnabled = false;
            editor.Text = "{";
            check(!Pump(editor.RefreshDiagnosticsAsync()) && editor.Diagnostics.Count == 0,
                "CodeEditor/diagnostics: disabling diagnostics cancels publication");
            editor.IsDiagnosticsEnabled = true;
            var obsolete = editor.RefreshDiagnosticsAsync();
            editor.Text = "var latest = 1;";
            check(!Pump(obsolete) && editor.Diagnostics.Count == 0,
                "CodeEditor/diagnostics: cancelled snapshot results cannot overwrite newer edits");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void DiagnosticDetails(Action<bool, string> check, string? output)
    {
        CodeAnalysis Analyze(string text, CodeType type = CodeType.CSharp)
        {
            return CodeAnalysis.Create(text, type, default);
        }

        const string screenshot =
            "class User\n{\n    public string Name { get; set; }\n    public \n    public string Greet(string name, int count = 1)\n    {\n        for (int i=0;i<10;i++){\n        }\n        return name;\n    }\n}\nvar user = new User();\nvar broken = ;\nuser.Greet(\"Ada\", )";
        var analysis = Analyze(screenshot);
        check(
            analysis.Diagnostics.Any(d => d.Code == "CSDECL001" && d.Line == 4) &&
            analysis.Diagnostics.Any(d => d.Code == "SYN004" && d.Line == 13) &&
            analysis.Diagnostics.Any(d => d.Code == "CSCALL001" && d.Line == 14),
            "CodeEditor/diagnostic details: screenshot reports incomplete public declaration, missing initializer and missing call argument at their source rows");
        foreach (var sample in new[]
                 {
                     ("class C { public }", "CSDECL001"), ("class C { public string; }", "CSDECL002"),
                     ("int F() {\nreturn 1\n}", "CSSTMT001"), ("void F(int x) {}\nF(\"wrong\");", "CALL001"),
                     ("class C { public int Count; }\nvar c = new C();\nc.Cout;", "MEMBER001"),
                     ("void F(int x, int y) {}\nF(, 1);", "CSCALL001")
                 })
            check(Analyze(sample.Item1).Diagnostics.Any(d => d.Code == sample.Item2),
                $"CodeEditor/diagnostic details: {sample.Item2} checks {sample.Item1.Replace('\n', ' ')}");
        foreach (var valid in new[]
                 {
                     "class C { public\n string Name { get; set; } }",
                     "class C { public int F(int n) { for(int i=0;i<n;i++) { } return n; } }",
                     "var list = new List<int> { 1, 2, };", "int F() {\nreturn\n 1;\n}", "void F(int x) {}\nF(1);",
                     "unknown.External(1, 2);", "class C { public int Count; }\nvar c = new C();\nc.Count;"
                 })
            check(Analyze(valid).Diagnostics.Count == 0,
                $"CodeEditor/diagnostic details: valid multiline/loop/initializer/external/member sample stays clean: {valid.Replace('\n', ' ')}");
        var completions = analysis.Complete(screenshot.IndexOf("public \n", StringComparison.Ordinal) + 7, "");
        check(
            completions.Any(i => i.Label == "string") &&
            completions.Any(i => i.Label == "DateTime" && i.Kind == CodeCompletionKind.Type) &&
            !completions.Any(i => i.Label == "user"),
            "CodeEditor/context: public-space suggests declaration types rather than local expression variables");
        const string forText = "class CodeFormatting {}\nfor";
        var forItems = Analyze(forText).Complete(forText.Length, "for");
        check(
            forItems[0].Label == "for" && forItems[0].Kind == CodeCompletionKind.Snippet &&
            forItems.Any(i => i.Label == "foreach") && forItems.Any(i => i.Label == "FormatException") &&
            !forItems.Any(i => i.Label == "CodeFormatting"),
            "CodeEditor/context: only input prefixes match; unrelated infix types are excluded");
        check(!Analyze("SB").Complete(2, "SB").Any(i => i.Label == "StringBuilder"),
            "CodeEditor/context: initials do not introduce unrelated suggestions");
        foreach (var sample in new[]
                 {
                     ("DateTime.No", "Now"), ("var map = new Dictionary();\nmap.TryG", "TryGetValue"),
                     ("var sb = new StringBuilder();\nsb.Append(\"value\").ToS", "ToString"),
                     ("File.ReadAllT", "ReadAllText"), ("var values = new List();\nvalues.Whe", "Where")
                 })
            check(
                Analyze(sample.Item1).Complete(sample.Item1.Length, sample.Item1[(sample.Item1.LastIndexOf('.') + 1)..])
                    .Any(i => i.Label == sample.Item2),
                $"CodeEditor/context: standard members and known return chains resolve {sample.Item2}");
        check(Analyze("class C : ExternalBase {}\nvar c = new C();\nc.Inherited();").Diagnostics.Count == 0,
            "CodeEditor/diagnostic details: incomplete external base-type information does not create missing-member errors");
        var editor = new CodeEditor
            { CodeType = CodeType.CSharp, Header = "diagnostics.cs", Text = "class C {\n public" };
        var window = Open(editor, 720, 350);
        try
        {
            editor.FocusEditor();
            editor.CaretIndex = editor.Text.Length;
            window.KeyTextInput(" ");
            var timer = Stopwatch.StartNew();
            var dispatcherFrame = new DispatcherFrame();
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            poll.Tick += (_, _) =>
            {
                if (editor.IsCompletionOpen || timer.ElapsedMilliseconds >= 3000) dispatcherFrame.Continue = false;
            };
            poll.Start();
            try
            {
                Dispatcher.UIThread.PushFrame(dispatcherFrame);
            }
            finally
            {
                poll.Stop();
            }

            check(editor.IsCompletionOpen && editor.CompletionItems.Any(i => i.Label == "DateTime"),
                $"CodeEditor/context UI: native space after public opens types (text={editor.Text}, caret={editor.CaretIndex}, open={editor.IsCompletionOpen}, error={editor.CompletionError})");
            editor.Text = "for";
            editor.CaretIndex = 3;
            Press(window, Key.Tab, PhysicalKey.Tab);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Flush();
            timer.Restart();
            while (editor.Text == "for" && timer.ElapsedMilliseconds < 3000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            Flush();
            check(
                editor.Text!.StartsWith("for (int i", StringComparison.Ordinal) &&
                editor.Text.Contains("length", StringComparison.Ordinal),
                "CodeEditor/context UI: Tab Tab expands the selected for snippet as one edit");
            editor.Undo();
            check(editor.Text == "for", "CodeEditor/context UI: snippet expansion is one undo step");
            editor.Text = screenshot;
            editor.CaretIndex = 0;
            Pump(editor.RefreshDiagnosticsAsync());
            Flush();
            check(
                editor.DiagnosticLine == 4 &&
                editor.DiagnosticMessage.Contains("Incomplete declaration", StringComparison.Ordinal) &&
                editor.DiagnosticDetail.Contains("CSDECL001 · 4:", StringComparison.Ordinal),
                "CodeEditor/status details: public properties expose full error text, code and source location");
            var message = Part<TextBlock>(editor, "PART_ErrorMessage");
            var link = Part<Button>(editor, "PART_Diagnostics");
            check(
                message.Text == editor.DiagnosticMessage && ToolTip.GetTip(link) is TextBlock tip &&
                tip.Text == editor.DiagnosticDetail && tip.TextWrapping == TextWrapping.Wrap,
                "CodeEditor/status details: visible text and untrimmed wrapping tooltip bind to the same diagnostic");
            Click(window, link);
            check(ToolTip.GetIsOpen(link) && editor.CaretLine == 4,
                "CodeEditor/status details: clicking the error link opens full details after navigating to its source row");
            ToolTip.SetIsOpen(link, false);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in Enum.GetValues<CodeEditorDensity>())
            foreach (var width in new[] { 720d, 320d })
            {
                window.RequestedThemeVariant = theme;
                editor.Density = size;
                window.Width = width;
                Flush();
                var number = Part<TextBlock>(editor, "PART_ErrorLine");
                var position = Part<StackPanel>(editor, "PART_Position");
                var positionText = Part<TextBlock>(editor, "PART_PositionText");
                var status = Part<Border>(editor, "PART_StatusBar");
                var messageY = message.TranslatePoint(new Point(0, message.Bounds.Height / 2), status)!.Value.Y;
                var positionY = positionText.TranslatePoint(new Point(0, positionText.Bounds.Height / 2), status)!.Value
                    .Y;
                var messageRight = message.TranslatePoint(new Point(message.Bounds.Width, 0), editor)!.Value.X;
                var positionLeft = position.TranslatePoint(default, editor)!.Value.X;
                check(
                    message.Bounds.Width > 0 && messageRight <= positionLeft &&
                    number.Bounds.Width >= number.DesiredSize.Width &&
                    message.TextTrimming == TextTrimming.CharacterEllipsis,
                    $"CodeEditor/status details: {theme}/{size}/{width} retains whole row number and ellipsizes the message before caret coordinates");
                check(
                    status.Bounds.Height >= 28 && Math.Abs(messageY - positionY) <= 1 && message.Bounds.Height >= 18 &&
                    number.Bounds.Height >= 18 && positionText.Text == $"行 {editor.CaretLine}，列 {editor.CaretColumn}",
                    $"CodeEditor/status alignment: {theme}/{size}/{width} height={status.Bounds.Height}, centers={messageY}/{positionY}, text heights={message.Bounds.Height}/{number.Bounds.Height}, position={positionText.Text}");
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-details-{theme}-{size}-{width}.png");
            }
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void SnippetsAndMembers(Action<bool, string> check, string? output)
    {
        CodeAnalysis Analyze(string text, CodeType type = CodeType.CSharp)
        {
            return CodeAnalysis.Create(text, type, default);
        }

        const string types =
            "class User { public string Name => \"Ada\"; public string Greet(string who, int count = 1) => who; }\nclass Base { public int Id { get; set; } public string Describe() => \"base\"; }\nclass Derived : Base { public string Title { get; set; } }\nclass Repository<T> { public List<T> Items { get; set; } public T Find(string name) => default; }\n";
        foreach (var sample in new[]
                 {
                     ("List<User> users;\nusers[0].Na", "Name"),
                     ("var users = new List<User>();\nusers[0].Gr", "Greet"),
                     ("var users = new User[10];\nusers[0].Na", "Name"),
                     ("var repo = new Repository<User>();\nrepo.Find(\"Ada\").Na", "Name"),
                     ("var repo = new Repository<User>();\nrepo.Items[0].Na", "Name"),
                     ("var users = new List<User>();\nusers.Where(u => true).FirstOrDefault().Na", "Name"),
                     ("Dictionary<string,List<User>> groups;\ngroups[\"all\"][0].Na", "Name"),
                     ("var user = new Derived();\nuser.I", "Id"),
                     ("var user = new Derived();\nuser.Des", "Describe"),
                     ("User Echo<T>(T value) => new User();\nvar user = Echo<int>(1);\nuser.Na", "Name")
                 })
        {
            var text = types + sample.Item1;
            var prefix = text[(text.LastIndexOf('.') + 1)..];
            var inspected = Analyze(text);
            check(inspected.Complete(text.Length, prefix).Any(i => i.Label == sample.Item2),
                $"CodeEditor/members: resolves {sample.Item1.Replace('\n', ' ')}; symbols={string.Join("; ", inspected.Symbols.Where(s => s.Owner == "Repository" || s.Name == "repo").Select(s => s.Name + ":" + s.Type))}");
        }

        var signatureText = types + "var repo = new Repository<User>();\nrepo.Find(";
        check(
            Analyze(signatureText).Signature(signatureText.Length)?.Signatures.Any(s => s.ReturnType == "User") == true,
            "CodeEditor/members: class generic arguments substitute method return types in parameter hints");
        const string generic = "T Echo<T>(Dictionary<string, T> values, T fallback) => fallback;\nEcho<User>(";
        var genericHelp = Analyze(types + generic).Signature((types + generic).Length);
        check(
            genericHelp?.Signatures.Any(s =>
                s.ReturnType == "User" && s.Parameters.Count == 2 &&
                s.Parameters[0].Type == "Dictionary<string,User>") == true,
            "CodeEditor/members: generic methods retain nested generic parameter types and substitute explicit type arguments");
        var named = types + "var user = new User();\nuser.Greet(co";
        check(Analyze(named).Complete(named.Length, "co").Any(i => i.Label == "count:" && i.InsertText == "count: "),
            "CodeEditor/parameters: C# completion suggests named arguments");
        named = types + "var user = new User();\nuser.Greet(count: 2, name";
        check(Analyze(named).Signature(named.LastIndexOf(',') - 1)?.ActiveParameter == 1,
            "CodeEditor/parameters: named C# arguments select the declared parameter rather than positional order");
        var python = "def greet(name: str, count: int = 1):\n    return name\ngreet(co";
        check(Analyze(python, CodeType.Python).Complete(python.Length, "co").Any(i => i.Label == "count="),
            "CodeEditor/parameters: Python completion suggests keyword arguments");
        var arrow = "const greet = name => name;\ngreet(";
        check(
            Analyze(arrow, CodeType.JavaScript).Signature(arrow.Length)?.Signatures
                .Any(s => s.Parameters.Count == 1 && s.Parameters[0].Name == "name") == true,
            "CodeEditor/members: single-parameter arrow functions provide signatures");
        named = types + "var user = new User();\nuser.Greet(count";
        check(Analyze(named).Complete(named.Length, "count")[0].Label == "count:",
            "CodeEditor/parameters: a matching named argument ranks ahead of document-word fallback");
        const string genericCall = "void Use(object value, int count) {}\nUse(new Dictionary<string,int>(), ";
        check(Analyze(genericCall).Signature(genericCall.Length)?.ActiveParameter == 1,
            "CodeEditor/parameters: generic type commas do not advance the enclosing call parameter");
        foreach (var sample in new[]
                 {
                     ("Math.Rou", "Round"), ("Math.P", "PI"), ("Convert.ToI", "ToInt32"),
                     ("Directory.GetF", "GetFiles"), ("Guid.New", "NewGuid"),
                     ("var text = \"x\";\ntext.TrimE", "TrimEnd"), ("Task<User> work;\nwork.Result.Na", "Name"),
                     ("var users = new List<User>();\nusers.Find(u => true).Na", "Name")
                 })
        {
            var text = types + sample.Item1;
            var prefix = text[(text.LastIndexOf('.') + 1)..];
            check(Analyze(text).Complete(text.Length, prefix).Any(i => i.Label == sample.Item2),
                $"CodeEditor/library: methods, properties and generic returns include {sample.Item2}");
        }

        const string constructor = "new FormatException(";
        check(
            Analyze(constructor).Signature(constructor.Length)?.Signatures
                .Any(s => s.Parameters.Any(p => p.Name == "message")) == true,
            "CodeEditor/parameters: built-in constructor signatures include expected parameter names");
        const string listCtor = "new List<User>(";
        check(
            Analyze(listCtor).Signature(listCtor.Length)?.Signatures
                .Any(s => s.Parameters.Any(p => p.Type == "IEnumerable<User>")) == true,
            "CodeEditor/parameters: generic constructor hints substitute their collection element type");
        foreach (var sample in new[]
                 {
                     ("IEnumerable<User> users;\nusers.", "Where", "Add"),
                     ("IList<User> users;\nusers.", "Insert", "Sort"),
                     ("IReadOnlyList<User> users;\nusers.", "Count", "Add"), ("User[] users;\nusers.", "Length", "Name")
                 })
        {
            var text = types + sample.Item1;
            var items = Analyze(text).Complete(text.Length, "");
            check(items.Any(i => i.Label == sample.Item2) && !items.Any(i => i.Label == sample.Item3),
                $"CodeEditor/member accuracy: {sample.Item1.Replace('\n', ' ')} includes {sample.Item2} and excludes invalid {sample.Item3}");
        }

        var editor = new CodeEditor { CodeType = CodeType.CSharp, Header = "snippets.cs" };
        var window = Open(editor, 740, 420);

        void Reset(string text, CodeType language = CodeType.CSharp)
        {
            editor.CodeType = language;
            editor.Text = text;
            editor.CaretIndex = text.Length;
            editor.FocusEditor();
        }

        void Tab(bool shift = false)
        {
            Press(window, Key.Tab, PhysicalKey.Tab, shift ? RawInputModifiers.Shift : RawInputModifiers.None);
        }

        void Expand(string shortcut, CodeType language = CodeType.CSharp)
        {
            Reset(shortcut, language);
            Tab();
            Tab();
        }

        try
        {
            Reset("method");
            Tab();
            check(editor.Text == "method",
                "CodeEditor/snippets: first Tab confirms the shortcut without inserting or changing undo history");
            Tab();
            check(
                editor.Text!.Contains("public void Method()", StringComparison.Ordinal) &&
                editor.SelectedText == "void",
                "CodeEditor/snippets: immediate second Tab expands a method and selects its return type");
            window.KeyTextInput("string");
            Tab();
            check(editor.SelectedText == "Method",
                "CodeEditor/snippets: replacing a field preserves navigation to the method name");
            window.KeyTextInput("Greet");
            Tab();
            window.KeyTextInput("string name");
            Tab();
            check(
                editor.Text!.Contains("public string Greet(string name)", StringComparison.Ordinal) &&
                editor.CaretLine == 3 && editor.SelectionLength == 0,
                "CodeEditor/snippets: Tab visits return type, function name, parameters and body");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Flush();
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-method-snippet-{theme}.png");
            }

            Expand("method");
            Tab();
            check(editor.SelectedText == "Method", "CodeEditor/snippets: Tab moves to the next editable field");
            Tab(true);
            check(editor.SelectedText == "void", "CodeEditor/snippets: Shift+Tab returns to the previous field");
            Press(window, Key.Escape, PhysicalKey.Escape);
            editor.ReplaceSelection("int");
            check(editor.Text!.Contains("public int Method", StringComparison.Ordinal),
                "CodeEditor/snippets: Escape ends field navigation while retaining generated code");
            Expand("for");
            window.KeyTextInput("index");
            check(editor.Text!.Contains("int index = 0; index < length; index++", StringComparison.Ordinal),
                "CodeEditor/snippets: editing a loop variable mirrors all linked occurrences in one edit");
            editor.Undo();
            check(editor.Text!.Contains("int i = 0; i < length; i++", StringComparison.Ordinal),
                "CodeEditor/snippets: one undo reverses a linked-field edit");
            editor.Undo();
            check(editor.Text == "for", "CodeEditor/snippets: one additional undo reverses the complete expansion");
            editor.Redo();
            check(editor.Text!.Contains("for (int i", StringComparison.Ordinal),
                "CodeEditor/snippets: redo restores generated code without stale field state");
            Expand("propfull");
            window.KeyTextInput("int");
            Tab();
            window.KeyTextInput("count");
            Tab();
            window.KeyTextInput("Count");
            check(
                editor.Text!.Contains("private int _count;", StringComparison.Ordinal) &&
                editor.Text.Contains("public int Count", StringComparison.Ordinal) &&
                editor.Text.Contains("get => _count;", StringComparison.Ordinal) &&
                editor.Text.Contains("set => _count = value;", StringComparison.Ordinal),
                "CodeEditor/snippets: full properties link type and backing-field references while keeping the public name independent");
            const string ctor = "class Person\n{\n    ctor\n}";
            Reset(ctor);
            editor.CaretIndex = ctor.IndexOf("ctor", StringComparison.Ordinal) + 4;
            Pump(editor.RequestCompletionAsync());
            Tab();
            Tab();
            check(editor.Text!.Contains("public Person()", StringComparison.Ordinal),
                "CodeEditor/snippets: constructor expansion uses the enclosing document type");
            Reset(ctor);
            editor.CaretIndex = ctor.IndexOf("ctor", StringComparison.Ordinal) + 4;
            Tab();
            Tab();
            check(editor.Text!.Contains("public Person()", StringComparison.Ordinal),
                "CodeEditor/snippets: rapid constructor Tab Tab works before a background analysis is available");
            foreach (var language in new[]
                     {
                         CodeType.CSharp, CodeType.JavaScript, CodeType.TypeScript, CodeType.Python, CodeType.C,
                         CodeType.Cpp, CodeType.Java, CodeType.Dart, CodeType.Go, CodeType.Rust, CodeType.Kotlin,
                         CodeType.Swift, CodeType.Php, CodeType.Ruby, CodeType.Lua, CodeType.Bash, CodeType.PowerShell
                     })
            {
                var item = CodeSnippets.Get(language).First(i =>
                    i.Snippet != null && i.Label is "method" or "func" or "function" or "def" or "fn" or "fun");
                Expand(item.Label, language);
                check(
                    editor.Text != item.Label && editor.SelectionLength > 0 &&
                    !editor.Text!.Contains("${", StringComparison.Ordinal) && editor.Text == item.InsertText,
                    $"CodeEditor/snippets: {language} Tab Tab generates its language-specific function syntax and selects a field");
            }

            Reset("class C\r\n{\r\n  method\r\n}");
            editor.TabSize = 2;
            editor.CaretIndex = editor.Text!.IndexOf("method", StringComparison.Ordinal) + 6;
            Tab();
            Tab();
            Tab();
            Tab();
            Tab();
            check(
                editor.CaretLine == 5 && editor.CaretColumn == 5 &&
                editor.Text.Contains("\r\n  {\r\n    \r\n  }", StringComparison.Ordinal),
                "CodeEditor/snippets: CRLF and two-space indentation preserve all field offsets and the body stop");
            editor.TabSize = 4;
            Reset("for");
            Tab();
            Press(window, Key.Escape, PhysicalKey.Escape);
            Tab();
            check(editor.Text == "for", "CodeEditor/snippets: Escape cancels an armed expansion");
            Reset("method");
            Tab();
            editor.Text = "for";
            editor.CaretIndex = 3;
            Tab();
            check(editor.Text == "for",
                "CodeEditor/snippets: external text replacement invalidates the previous armed shortcut");
            Reset("for");
            editor.IsTabCompletionEnabled = false;
            Tab();
            Tab();
            check(editor.Text == "for     ",
                "CodeEditor/snippets: disabled Tab completion retains indentation rather than expanding snippets");
            editor.IsTabCompletionEnabled = true;
            Reset("// method");
            Tab();
            Tab();
            check(!editor.Text!.Contains("public void", StringComparison.Ordinal),
                "CodeEditor/snippets: comments do not expand function shortcuts");
            Reset("method");
            Tab();
            editor.IsReadOnly = true;
            Tab();
            check(editor.Text == "method", "CodeEditor/snippets: read-only transitions invalidate armed expansions");
            editor.IsReadOnly = false;
            Reset("prop");
            Pump(editor.RequestCompletionAsync());
            var property = editor.CompletionItems.First(i => i.Label == "prop");
            editor.AcceptCompletion(property);
            check(
                editor.Text!.Contains("public string Name", StringComparison.Ordinal) &&
                editor.SelectedText == "string",
                "CodeEditor/snippets: Enter, pointer and explicit acceptance expand immediately without requiring two Tabs");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void LanguageDiagnostics(Action<bool, string> check)
    {
        CodeAnalysis Analyze(string text, CodeType type)
        {
            return CodeAnalysis.Create(text, type, default);
        }

        var model = new CodeEditorDemoViewModel();
        foreach (var type in Enum.GetValues<CodeType>())
        {
            model.SelectedCodeType = type;
            model.LoadSample();
            var errors = Analyze(model.Code!, type).Diagnostics;
            check(!errors.Any(d => d.Severity == CodeDiagnosticSeverity.Error),
                $"CodeEditor/language diagnostics: valid {type} demo has no errors ({string.Join("; ", errors.Select(e => e.Code + ":" + e.Message))})");
        }

        foreach (var sample in new (CodeType Type, string Text, string Code)[]
                 {
                     (CodeType.CSharp, "class { }", "CSDECL001"), (CodeType.JavaScript, "const = 1;", "JSDECL001"),
                     (CodeType.TypeScript, "interface { }", "TSDECL001"), (CodeType.Json, "{\"a\": }", "JSON001"),
                     (CodeType.Xml, "<root><item></root>", "XML001"),
                     (CodeType.Axaml, "<Grid><Button></Grid>", "XML001"),
                     (CodeType.Html, "<div><span></div>", "HTML001"), (CodeType.Css, "a { color red; }", "CSSPROP001"),
                     (CodeType.Sql, "SELECT FROM users;", "SQLSELECT001"),
                     (CodeType.Python, "def f:\n    pass", "PYDECL003"),
                     (CodeType.Yaml, "a: 1\na: 2", "YAML002"),
                     (CodeType.Markdown, "```python\ndef broken()\n    pass\n```", "MD.PY004"),
                     (CodeType.C, "struct ;", "CDECL001"), (CodeType.Cpp, "class ;", "CPPDECL001"),
                     (CodeType.Java, "class {}", "JAVADECL001"),
                     (CodeType.Go, "var = 1", "GODECL001"), (CodeType.Rust, "fn {}", "RUSTDECL002"),
                     (CodeType.Kotlin, "fun {}", "KOTLINDECL002"),
                     (CodeType.Swift, "func {}", "SWIFTDECL002"), (CodeType.Php, "function {}", "PHPDECL002"),
                     (CodeType.Ruby, "def greet\n  'hi'\n", "RUBYBLOCK001"),
                     (CodeType.Bash, "if true; then echo yes", "BASHBLOCK001"),
                     (CodeType.PowerShell, "function {}", "PSDECL002"), (CodeType.Dart, "class {}", "DARTDECL001"),
                     (CodeType.Lua, "function greet() return 'hi'", "LUABLOCK001"),
                     (CodeType.Toml, "flag = True", "TOMLVALUE002"),
                     (CodeType.Ini, "[missing", "INISECTION001")
                 })
        {
            var errors = Analyze(sample.Text, sample.Type).Diagnostics;
            check(
                errors.Any(d => d.Code == sample.Code) && errors.All(d =>
                    d.Start >= 0 && d.Start <= sample.Text.Length && d.Line > 0 && d.Column > 0),
                $"CodeEditor/language diagnostics: {sample.Type} identifies {sample.Code} with valid source spans ({string.Join(",", errors.Select(e => e.Code))})");
        }

        foreach (var sample in new (CodeType Type, string Text)[]
                 {
                     (CodeType.PlainText, "anything [ unfinished \" text"),
                     (CodeType.Markdown, "This is [free prose\n```unknown\nnot { code\n"),
                     (CodeType.C, "struct { int x; } value;"), (CodeType.Cpp, "enum { Red, Blue };"),
                     (CodeType.Go, "var value struct { Name string }"),
                     (CodeType.JavaScript, "const values = [1,,3];\nfunction f() {}\nf(1,);"),
                     (CodeType.Python, "def f(a, /, b=1, *, c=2):\n    return (a, b, c)\nf(1, c=3,)"),
                     (CodeType.Ruby, "def answer = 42\nputs answer if true\n[1,2].each do |x|\n  puts x\nend"),
                     (CodeType.Lua,
                         "local x = 1\nfor i=1,3 do\n  if i > 0 then x=x+1 end\nend\nrepeat x=x-1 until x == 0"),
                     (CodeType.Bash,
                         "value=\ncase \"$value\" in\n  yes) echo yes ;;\n  *) echo no ;;\nesac\nif true; then if false; then :; fi; fi\necho {"),
                     (CodeType.Sql,
                         "SELECT CASE WHEN x=1 THEN 'yes' ELSE 'no' END FROM t;\nBEGIN TRANSACTION; COMMIT; END;"),
                     (CodeType.Css,
                         "@media (min-width: 600px) { .a:hover { color: red; background: url('data:a;b'); --data: a:b; } }"),
                     (CodeType.Toml,
                         "date = 1979-05-27\ntime = 07:32:00\nstamp = 1979-05-27T07:32:00Z\nvalues = [1,\n 2,]\n[server]\nhost = 'localhost'\n[[items]]\nname='a'\n[[items]]\nname='b'"),
                     (CodeType.Toml, "\"a.b\" = 1\na.b = 2\ntext = '''\n[not a section]\n'''"),
                     (CodeType.Ini, "[server]\nkey=value\nempty=\n; comment\n[other]\nkey=other"),
                     (CodeType.PowerShell,
                         "IF ($true) { Write-Host 'yes' }\nfunction Get-Thing { param($name) return $name }")
                 })
        {
            var errors = Analyze(sample.Text, sample.Type).Diagnostics;
            check(!errors.Any(e => e.Severity == CodeDiagnosticSeverity.Error),
                $"CodeEditor/language accuracy: valid {sample.Type} syntax remains clean ({string.Join("; ", errors.Select(e => e.Code + ":" + e.Message))})");
        }

        foreach (var sample in new[]
                 {
                     (CodeType.Css, "a{color:;}", "CSSVALUE001"), (CodeType.Sql, "SELECT * FROM ;", "SQLTABLE001"),
                     (CodeType.Lua, "if true print(1) end", "LUAHEADER002"),
                     (CodeType.Bash, "if true; echo yes; fi", "BASHHEADER002"), (CodeType.Ruby, "end", "RUBYBLOCK002"),
                     (CodeType.PowerShell, "if $true {}", "PSHEADER001"),
                     (CodeType.Python, "if x = 1:\n    pass", "PYCOND001"), (CodeType.Toml, "a=1\na=2", "TOMLKEY002"),
                     (CodeType.Toml, "key=", "TOMLVALUE001"), (CodeType.Toml, "items=[1,2", "TOMLSYM002"),
                     (CodeType.CSharp, "void F() {\n  fase\n}", "CSSTMT002")
                 })
            check(Analyze(sample.Item2, sample.Item1).Diagnostics.Any(d => d.Code == sample.Item3),
                $"CodeEditor/language diagnostics: {sample.Item1} detects {sample.Item3}");
        const string html = "<script>const = 1;</script><style>a {color:;}</style>";
        check(
            Analyze(html, CodeType.Html).Diagnostics.Any(d => d.Code == "HTML.JSDECL001") &&
            Analyze(html, CodeType.Html).Diagnostics.Any(d => d.Code == "HTML.CSSVALUE001"),
            "CodeEditor/embedded diagnostics: HTML script/style errors retain original document locations");
        var markdown = "Intro\n\n```python\ndef broken()\n    pass\n```";
        check(Analyze(markdown, CodeType.Markdown).Diagnostics.Any(d => d.Line == 4 && d.Code == "MD.PY004"),
            "CodeEditor/embedded diagnostics: fenced errors map to the original Markdown row");
        foreach (var type in new[]
                 {
                     CodeType.CSharp, CodeType.JavaScript, CodeType.TypeScript, CodeType.C, CodeType.Cpp, CodeType.Java,
                     CodeType.Go, CodeType.Rust, CodeType.Kotlin, CodeType.Swift, CodeType.Php, CodeType.Ruby,
                     CodeType.Dart, CodeType.Lua, CodeType.Python
                 })
        {
            var errors = Analyze("x；", type).Diagnostics;
            check(errors.Any(d => d.Code.EndsWith("SYM001", StringComparison.Ordinal)),
                $"CodeEditor/symbols: {type} reports full-width code punctuation");
            check(!Analyze("\"；｛｝\"", type).Diagnostics.Any(d => d.Code.EndsWith("SYM001", StringComparison.Ordinal)),
                $"CodeEditor/symbols: {type} protects literal content");
        }

        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "class FormatThing {}\nvar uniform = 1;\nfor", "for"),
                     (CodeType.Python, "def format_data():\n    pass\nfor", "for"),
                     (CodeType.Rust, "fn format_data() {}\nfo", "fo")
                 })
        {
            var items = Analyze(sample.Item2, sample.Item1).Complete(sample.Item2.Length, sample.Item3);
            check(
                items.Count > 0 && items.All(i => i.Label.StartsWith(sample.Item3, StringComparison.OrdinalIgnoreCase)),
                $"CodeEditor/relevance: every {sample.Item1} completion matches the input prefix");
        }
    }

    private static void IdeEditing(Action<bool, string> check, string? output)
    {
        foreach (var type in new[]
                 {
                     CodeType.CSharp, CodeType.Cpp, CodeType.Java, CodeType.JavaScript, CodeType.TypeScript,
                     CodeType.Swift, CodeType.Php, CodeType.Dart
                 })
        {
            var text = type == CodeType.CSharp ? "namespace Test{\n public class User\n}" : "class User\n";
            check(
                CodeAnalysis.Create(text, type, default).Diagnostics
                    .Any(d => d.Code.EndsWith("BODY001", StringComparison.Ordinal)),
                $"CodeEditor/semantic: {type} incomplete type declaration reports a missing body");
        }

        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "int count = \"bad\";"), (CodeType.Java, "boolean ok = 12;"),
                     (CodeType.TypeScript, "let count: number = 'bad';"), (CodeType.Kotlin, "val count: Int = \"bad\""),
                     (CodeType.Swift, "let count: Int = \"bad\""), (CodeType.Rust, "let count: i32 = \"bad\";"),
                     (CodeType.Go, "var count int = \"bad\""), (CodeType.Dart, "int count = 'bad';"),
                     (CodeType.Cpp, "int count = \"bad\";"), (CodeType.C, "int count = \"bad\";")
                 })
            check(
                CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics
                    .Any(d => d.Code.EndsWith("TYPE002", StringComparison.Ordinal)),
                $"CodeEditor/semantic: {sample.Item1} explicit primitive declaration rejects an incompatible literal");
        check(
            CodeAnalysis.Create("count: int = 'bad'", CodeType.Python, default).Diagnostics.Any(d =>
                d.Code == "PYTYPE002" && d.Severity == CodeDiagnosticSeverity.Warning),
            "CodeEditor/semantic: Python annotation mismatch is a warning, not a syntax error");
        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "void F(int value, int value) {}"),
                     (CodeType.Python, "def f(value, value):\n    pass"),
                     (CodeType.TypeScript, "function f(value: number, value: number) {}"),
                     (CodeType.Java, "class T { void f(int value, int value) {} }"),
                     (CodeType.Go, "func f(value int, value int) {}")
                 })
            check(
                CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics
                    .Any(d => d.Code.EndsWith("PARAM001", StringComparison.Ordinal)),
                $"CodeEditor/semantic: {sample.Item1} duplicate function parameters are diagnosed");
        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "class Empty;\nrecord User(string Name);\nclass Generic<T> where T: class {}"),
                     (CodeType.Cpp, "class Forward;\nstruct Data;\nint count=1;"),
                     (CodeType.Rust, "struct Unit;\nstruct Pair(i32, i32);\nmod child;"),
                     (CodeType.Kotlin, "class Person(val name: String)"),
                     (CodeType.JavaScript, "const A = class {};\nfor (const x of values) {}\nvar value='a';value=1;"),
                     (CodeType.TypeScript, "declare const value: string;\nfunction f(value: string): void;"),
                     (CodeType.Python, "value=1\nvalue='dynamic'"), (CodeType.Ruby, "value=1\nvalue='dynamic'"),
                     (CodeType.Lua, "local value=1\nvalue='dynamic'")
                 })
        {
            var errors = CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics;
            check(!errors.Any(d => d.Severity == CodeDiagnosticSeverity.Error),
                $"CodeEditor/semantic accuracy: valid {sample.Item1} forward/ambient/dynamic declaration ({string.Join(';', errors.Select(e => e.Code + ':' + e.Message))})");
        }

        foreach (var sample in new[]
                 {
                     (CodeType.Cpp, "bool ok=\"yes\";char c=65;"), (CodeType.Kotlin, "val c: Char='a'"),
                     (CodeType.Python, "count: int=True"), (CodeType.C, "int value;int value;")
                 })
            check(
                !CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics.Any(d =>
                    d.Code.EndsWith("TYPE002", StringComparison.Ordinal) ||
                    d.Code.EndsWith("BIND001", StringComparison.Ordinal)),
                $"CodeEditor/semantic accuracy: {sample.Item1} legal conversions and redeclarations are retained");
        foreach (var type in new[] { CodeType.JavaScript, CodeType.TypeScript })
            check(
                CodeAnalysis.Create("const missing;", type, default).Diagnostics
                    .Any(d => d.Code.EndsWith("CONST001", StringComparison.Ordinal)),
                $"CodeEditor/semantic: {type} const needs an initializer");
        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "void F(){int value=1; int value=2;}"),
                     (CodeType.JavaScript, "let value=1;let value=2;"),
                     (CodeType.TypeScript, "let value: number=1;let value: number=2;"),
                     (CodeType.Cpp, "int value=1;int value=2;"), (CodeType.Kotlin, "val value=1;val value=2;"),
                     (CodeType.Swift, "let value=1;let value=2;")
                 })
            check(
                CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics
                    .Any(d => d.Code.EndsWith("BIND001", StringComparison.Ordinal)),
                $"CodeEditor/semantic: {sample.Item1} duplicate lexical binding is diagnosed");
        var editor = new CodeEditor { IsCompletionEnabled = false };
        var window = Open(editor, 720, 420);
        try
        {
            foreach (var sample in new[]
                     {
                         (CodeType.Cpp, "    void hello() { std::cout << \"Hello, TheOne\"; }", "std::cout",
                             "    void hello() {\n        std::cout << \"Hello, TheOne\";\n    }"),
                         (CodeType.CSharp, "void F() { Call(); }", "Call", "void F() {\n    Call();\n}"),
                         (CodeType.C, "void f() { run(); }", "run", "void f() {\n    run();\n}"),
                         (CodeType.Java, "void f() { run(); }", "run", "void f() {\n    run();\n}"),
                         (CodeType.Dart, "void f() { run(); }", "run", "void f() {\n    run();\n}"),
                         (CodeType.Kotlin, "fun f() { run() }", "run", "fun f() {\n    run()\n}"),
                         (CodeType.Swift, "func f() { run() }", "run", "func f() {\n    run()\n}"),
                         (CodeType.JavaScript, "function f() { run(); }", "run", "function f() {\n    run();\n}"),
                         (CodeType.TypeScript, "function f() { run(); }", "run", "function f() {\n    run();\n}"),
                         (CodeType.Go, "func f() { println(1) }", "println", "func f() {\n    println(1)\n}"),
                         (CodeType.Rust, "fn f() { run(); }", "run", "fn f() {\n    run();\n}"),
                         (CodeType.Bash, "f() { echo yes; }", "echo", "f() {\n    echo yes;\n}"),
                         (CodeType.PowerShell, "function F { Write-Host 'yes' }", "Write-Host",
                             "function F {\n    Write-Host 'yes'\n}"),
                         (CodeType.Html, "<div><span>yes</span></div>", "<span>",
                             "<div>\n    <span>yes</span>\n</div>"),
                         (CodeType.Xml, "<root><item/></root>", "<item", "<root>\n    <item/>\n</root>"),
                         (CodeType.Axaml, "<Grid><Button/></Grid>", "<Button", "<Grid>\n    <Button/>\n</Grid>"),
                         (CodeType.Html, "<div><div>nested</div></div>", "<div>nested",
                             "<div>\n    <div>nested</div>\n</div>"),
                         (CodeType.Python, "value = { 'key': 1 }", "'key'", "value = {\n    'key': 1\n}"),
                         (CodeType.Ruby, "def f; puts 'yes'; end", "puts", "def f;\n    puts 'yes';\nend"),
                         (CodeType.Lua, "function f() print('yes') end", "print",
                             "function f()\n    print('yes')\nend"),
                         (CodeType.Bash, "if true; then echo yes; fi", "echo", "if true; then\n    echo yes;\nfi")
                     })
            {
                editor.CodeType = sample.Item1;
                editor.Text = sample.Item2;
                editor.CaretIndex = sample.Item2.IndexOf(sample.Item3, StringComparison.Ordinal);
                editor.FocusEditor();
                Press(window, Key.Enter, PhysicalKey.Enter);
                check(editor.Text == sample.Item4,
                    $"CodeEditor/Enter: {sample.Item1} moves the matching closer and indents the inline body ({editor.Text!.Replace("\n", " / ", StringComparison.Ordinal)})");
                editor.Undo();
                check(editor.Text == sample.Item2,
                    $"CodeEditor/Enter: {sample.Item1} block expansion is a single undo transaction");
            }

            editor.CodeType = CodeType.Cpp;
            editor.Text = "void f() { }";
            editor.CaretIndex = 11;
            editor.ConvertTabsToSpaces = false;
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "void f() {\n\t\n}", "CodeEditor/Enter: empty inline blocks respect tab indentation");
            editor.ConvertTabsToSpaces = true;
            editor.CodeType = CodeType.CSharp;
            editor.Text = "// void f() { text }";
            editor.CaretIndex = 14;
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(!editor.Text!.Contains("\n    ", StringComparison.Ordinal),
                "CodeEditor/Enter: comment braces are not formatted as a code block");
            foreach (var type in new[]
                         { CodeType.CSharp, CodeType.Cpp, CodeType.JavaScript, CodeType.Rust, CodeType.Python })
            {
                editor.CodeType = type;
                editor.Text = "value = {\n        ";
                editor.CaretIndex = editor.Text.Length;
                window.KeyTextInput("}");
                check(editor.Text == "value = {\n}",
                    $"CodeEditor/closer: {type} typed closing brace aligns with its opener");
            }

            editor.IsCompletionEnabled = true;
            foreach (var sample in new[]
                     {
                         (CodeType.Html, "<br>", "<br>\n"), (CodeType.Yaml, "root:", "root:\n    "),
                         (CodeType.Python, "def f():", "def f():\n    ")
                     })
            {
                editor.CodeType = sample.Item1;
                editor.Text = sample.Item2;
                editor.CaretIndex = editor.Text.Length;
                Press(window, Key.Enter, PhysicalKey.Enter);
                check(editor.Text == sample.Item3,
                    $"CodeEditor/Enter: {sample.Item1} respects its own block header rules");
            }

            editor.IsAutoIndentEnabled = false;
            editor.CodeType = CodeType.Cpp;
            editor.Text = "void f() { run(); }";
            editor.CaretIndex = 11;
            Press(window, Key.Enter, PhysicalKey.Enter);
            check(editor.Text == "void f() { \nrun(); }",
                "CodeEditor/Enter: disabling auto-indent also disables block expansion");
            editor.IsAutoIndentEnabled = true;
            foreach (var sample in new[]
                     {
                         (CodeType.CSharp, "Console.Wri"), (CodeType.Python, "str.up"),
                         (CodeType.JavaScript, "console.lo"), (CodeType.Cpp, "std::st"), (CodeType.Go, "fmt.Pr"),
                         (CodeType.Rust, "String::fr"), (CodeType.Java, "System.out.pr"), (CodeType.Lua, "string.gs"),
                         (CodeType.PowerShell, "Write-H"), (CodeType.Sql, "COU")
                     })
            {
                editor.CodeType = sample.Item1;
                editor.Text = sample.Item2;
                editor.CaretIndex = editor.Text.Length;
                editor.FocusEditor();
                var opened = Pump(editor.RequestCompletionAsync());
                var item = editor.CompletionItems.FirstOrDefault();
                check(opened && editor.InlineCompletionText.Length > 0 && editor.Text == sample.Item2,
                    $"CodeEditor/inline: {sample.Item1} previews a matching suffix without changing the document");
                Press(window, Key.Tab, PhysicalKey.Tab);
                check(
                    editor.Text!.EndsWith(item!.InsertText, StringComparison.Ordinal) &&
                    editor.InlineCompletionText.Length == 0,
                    $"CodeEditor/inline: {sample.Item1} one Tab accepts the method/property/symbol");
                editor.Undo();
                check(editor.Text == sample.Item2, $"CodeEditor/inline: {sample.Item1} acceptance is undoable");
            }

            editor.CodeType = CodeType.CSharp;
            editor.Text = "Console.Wri";
            editor.CaretIndex = editor.Text.Length;
            Pump(editor.RequestCompletionAsync());
            var first = editor.InlineCompletionText;
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            check(first != editor.InlineCompletionText && editor.InlineCompletionText.Length > 0,
                "CodeEditor/inline: list navigation updates the preview suffix");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in Enum.GetValues<CodeEditorDensity>())
            foreach (var width in new[] { 320, 720 })
            {
                window.RequestedThemeVariant = theme;
                editor.Density = size;
                window.Width = width;
                Flush();
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-inline-{theme}-{size}-{width}.png");
                check(
                    editor.InlineCompletionText.Length > 0 &&
                    editor.View.CaretRectangle.Right < editor.View.Bounds.Width,
                    $"CodeEditor/inline drawing: {theme}/{size}/{width} keeps suffix anchored to the shaped caret");
            }

            editor.IsInlineCompletionEnabled = false;
            check(editor.InlineCompletionText.Length == 0 && editor.IsCompletionOpen,
                "CodeEditor/inline: preview can be disabled independently of the completion list");
            editor.IsInlineCompletionEnabled = true;
            editor.View.InputClientForVerification.SetPreeditText("中");
            check(!editor.IsCompletionOpen && editor.InlineCompletionText.Length == 0,
                "CodeEditor/inline: IME composition dismisses suggestions");
            editor.View.ClearPreedit();
            editor.Text = "Console.Wr";
            editor.CaretIndex = editor.Text.Length;
            editor.FocusEditor();
            window.KeyTextInput("i");
            using (var loop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
            {
                Dispatcher.UIThread.MainLoop(loop.Token);
            }

            var wait = Stopwatch.StartNew();
            while (!editor.IsCompletionOpen && wait.ElapsedMilliseconds < 2000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            check(editor.IsCompletionOpen && editor.InlineCompletionText.Length > 0,
                "CodeEditor/inline: normal typing automatically opens the suffix preview");
            editor.IsReadOnly = true;
            check(editor.InlineCompletionText.Length == 0, "CodeEditor/inline: read-only mode clears the preview");
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void CompletionAndLiteralAccuracy(Action<bool, string> check)
    {
        foreach (var sample in new (CodeType Type, string Text)[]
                 {
                     (CodeType.CSharp, "var x = 0xFFul; var y = 1e-3; var z = 1.25m; var mod = 10%3;"),
                     (CodeType.Cpp, "auto x = 0xFFULL; auto y = 12_km;"),
                     (CodeType.Rust, "let x = 0xf32; let y = 1u64; let z = 0..10;"),
                     (CodeType.Python, "x = 0b101\ny = 1e-3\nz = 2j\n"),
                     (CodeType.JavaScript, "const x = 10n; const y = 1e-3;"),
                     (CodeType.CSharp,
                         "class Demo { public int @class; public void F(params string[] values) {} }\nvar d = new Demo(); d.F(\"a\", \"b\");"),
                     (CodeType.TypeScript,
                         "function greet(name?: string, ...values: number[]) {}\ngreet(); greet('a', 1, 2);"),
                     (CodeType.Python, "def f(a, /, *args, **kwargs):\n    pass\nf(1, 2, 3, key=4)\nf(*values)"),
                     (CodeType.JavaScript, "function f(a) {}\nf(); f(1, 2, 3);"),
                     (CodeType.JavaScript, "const obj = { function: true, class: 'a' };"),
                     (CodeType.Toml,
                         "[[items]]\nname='first'\n[items.details]\nx=1\n[[items]]\nname='second'\n[items.details]\nx=2"),
                     (CodeType.Python, "if check(value=1):\n    pass\nx = 10%3"),
                     (CodeType.CSharp, "var item = 1;\nvar values = new List<int>()\n{\n    item\n};"),
                     (CodeType.Bash, "echo function class ｛text｝"),
                     (CodeType.PowerShell, "Write-Host function class ｛text｝")
                 })
        {
            var errors = CodeAnalysis.Create(sample.Text, sample.Type, default).Diagnostics;
            check(!errors.Any(e => e.Severity == CodeDiagnosticSeverity.Error),
                $"CodeEditor/accuracy: valid {sample.Type} literals and variadic/dialect syntax ({string.Join(';', errors.Select(e => e.Code + ':' + e.Message))})");
        }

        foreach (var sample in new[]
                 {
                     (CodeType.CSharp, "var x = 0xGG;"), (CodeType.JavaScript, "const x = 1.5n;"),
                     (CodeType.Python, "x = 012"), (CodeType.Rust, "let x = 0b102;"), (CodeType.Java, "int x = 12abc;")
                 })
            check(
                CodeAnalysis.Create(sample.Item2, sample.Item1, default).Diagnostics
                    .Any(e => e.Code.EndsWith("NUM001", StringComparison.Ordinal)),
                $"CodeEditor/literals: {sample.Item1} identifies invalid numeric tokens");
        const string code =
            "// noiseComment\nvar text = \"noiseLiteral\";\nvoid First() { int noiseLocal = 1; }\nvoid Second() {\n noi\n}";
        var caret = code.LastIndexOf("noi", StringComparison.Ordinal) + 3;
        var items = CodeAnalysis.Create(code, CodeType.CSharp, default).Complete(caret, "noi");
        check(!items.Any(i => i.Label is "noiseComment" or "noiseLiteral" or "noiseLocal"),
            "CodeEditor/relevance: comments, strings and other function locals do not leak into completions");
        var doc = new CodeDocument();
        doc.Reset("// noiseComment\nvar value = \"noiseLiteral\";\nvar noiseCode = 1;\nnoi");
        CodeSyntax.Update(doc, CodeType.CSharp);
        items = CodeCompletions.Get(doc, CodeType.CSharp, "noi");
        check(items.Any(i => i.Label == "noiseCode") && !items.Any(i => i.Label is "noiseComment" or "noiseLiteral"),
            "CodeEditor/relevance: fast preliminary completion also excludes comments and strings");
    }

    private static void CompactAndTab(Action<bool, string> check, string? output)
    {
        var languages = new[]
        {
            (CodeType.C, "pri", "pri", "printf"), (CodeType.Cpp, "std::st", "st", "string"),
            (CodeType.Go, "fmt.Pr", "Pr", "Println"), (CodeType.Rust, "String::fr", "fr", "from"),
            (CodeType.Java, "System.out.pr", "pr", "println"),
            (CodeType.Kotlin, "val text = \"a\";\ntext.up", "up", "uppercase"),
            (CodeType.Swift, "let text = \"a\";\ntext.low", "low", "lowercased"),
            (CodeType.Php, "$date = new DateTime();\n$date->for", "for", "format"),
            (CodeType.Ruby, "text = 'a'\ntext.up", "up", "upcase"), (CodeType.Lua, "string.gs", "gs", "gsub"),
            (CodeType.Dart, "final text = 'a';\ntext.len", "len", "length"), (CodeType.Bash, "pri", "pri", "printf"),
            (CodeType.PowerShell, "Write-H", "Write-H", "Write-Host"), (CodeType.Sql, "COU", "COU", "COUNT"),
            (CodeType.Html, "<di", "di", "div"), (CodeType.Axaml, "<Grid Wid", "Wid", "Width"),
            (CodeType.Xml, "<root><child/></root>\n<chi", "chi", "child"),
            (CodeType.Css, "font-s", "font-s", "font-size"),
            (CodeType.Json, "tr", "tr", "true"), (CodeType.Yaml, "fa", "fa", "false"),
            (CodeType.Toml, "tr", "tr", "true"), (CodeType.Ini, "ye", "ye", "yes"),
            (CodeType.Markdown, "headingIdentifier\nheadingI", "headingI", "headingIdentifier"),
            (CodeType.PlainText, "documentSymbol\ndocumentS", "documentS", "documentSymbol")
        };
        foreach (var sample in languages)
        {
            var analysis = CodeAnalysis.Create(sample.Item2, sample.Item1, default);
            check(analysis.Complete(sample.Item2.Length, sample.Item3).Any(i => i.Label == sample.Item4),
                $"CodeEditor/common languages: {sample.Item1} offers {sample.Item4}");
        }

        const string go =
            "type User struct {\n Name string\n}\nfunc (u User) Greet(name string, count int) string { return name }\nvar u = User{}\nu.";
        var goMembers = CodeAnalysis.Create(go, CodeType.Go, default).Complete(go.Length, "");
        check(goMembers.Any(i => i.Label == "Name") && goMembers.Any(i => i.Label == "Greet"),
            "CodeEditor/common languages: Go structs and receiver methods share a member index");
        const string rust =
            "struct User { name: String }\nimpl User { fn greet(&self, name: &str) -> String { name.to_string() } }\nlet user = User { name: String::new() };\nuser.";
        check(CodeAnalysis.Create(rust, CodeType.Rust, default).Complete(rust.Length, "").Any(i => i.Label == "greet"),
            "CodeEditor/common languages: Rust impl methods attach to their declared type");
        var editor = new CodeEditor
            { CodeType = CodeType.CSharp, Header = "compact.cs", ShowCopyButton = true, ShowFormatButton = true };
        var window = Open(editor, 760, 430);

        void TabText(string text, string expected, CodeType type = CodeType.CSharp)
        {
            editor.CodeType = type;
            editor.Text = text;
            editor.CaretIndex = text.Length;
            editor.FocusEditor();
            Press(window, Key.Tab, PhysicalKey.Tab);
            var timeout = Stopwatch.StartNew();
            while (editor.Text == text && timeout.ElapsedMilliseconds < 2000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            Flush();
            check(editor.Text == expected,
                $"CodeEditor/Tab: {type} completes {text.Split('\n')[^1]} without an already open popup");
        }

        try
        {
            TabText("Console.WriteL", "Console.WriteLine()");
            TabText("var text = \"a\";\ntext.Len", "var text = \"a\";\ntext.Length");
            TabText("var documentSymbol = 1;\ndocumentSy", "var documentSymbol = 1;\ndocumentSymbol");
            TabText("<di", "<div></div>", CodeType.Html);
            TabText("<div cl", "<div class=\"\"", CodeType.Html);
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(
                editor.CaretIndex == editor.Text!.Length &&
                editor.Text.EndsWith("class=\"\"", StringComparison.Ordinal),
                "CodeEditor/Tab: attribute completion quotes provide a tracked closing-symbol stop");
            TabText("font-s", "font-size: ;", CodeType.Css);
            TabText("pri", "printf ", CodeType.Bash);
            editor.CodeType = CodeType.CSharp;
            editor.Text = "";
            editor.CaretIndex = 0;
            window.KeyTextInput("(");
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(editor.Text == "()" && editor.CaretIndex == 2,
                "CodeEditor/Tab: skips an auto-inserted closing symbol");
            editor.Text = "";
            editor.CaretIndex = 0;
            window.KeyTextInput("\"");
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(editor.Text == "\"\"" && editor.CaretIndex == 2,
                "CodeEditor/Tab: tracked quotes can be exited without adding spaces to a literal");
            editor.Text = "";
            editor.CaretIndex = 0;
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(editor.Text == "    ", "CodeEditor/Tab: an empty line still indents");
            editor.IsTabCompletionEnabled = false;
            editor.Text = "Con";
            editor.CaretIndex = 3;
            Press(window, Key.Tab, PhysicalKey.Tab);
            check(editor.Text == "Con ", "CodeEditor/Tab: disabling tab completion preserves native indentation stops");
            editor.IsTabCompletionEnabled = true;
            var deferred = new DeferredCompletion();
            editor.CompletionProvider = deferred;
            editor.Text = "ab";
            editor.CaretIndex = 2;
            Press(window, Key.Tab, PhysicalKey.Tab);
            editor.ReplaceSelection("c");
            deferred.Result.SetResult([new CodeCompletionItem("obsolete", "obsolete")]);
            Flush();
            check(editor.Text == "abc" && !editor.IsCompletionOpen,
                "CodeEditor/Tab: stale asynchronous completion does not overwrite subsequent input");
            editor.CompletionProvider = null;

            editor.Text = string.Join('\n', Enumerable.Repeat("// spacer", 11)) + "\nConsole.WriteLine(\"value\", ";
            editor.CaretIndex = editor.Text.Length;
            Pump(editor.RequestSignatureHelpAsync());
            var signature = Part<Border>(editor, "PART_SignaturePopup");
            Flush();
            var stable = editor.SignatureBounds;
            for (var i = 0; i < 16; i++)
            {
                editor.PositionCompletion();
                Flush();
            }

            var caret = editor.View.CaretRectangle;
            var origin = editor.View.TranslatePoint(default, (Visual)signature.Parent!)!.Value;
            caret = caret.Translate(new Vector(origin.X, origin.Y));
            check(
                Math.Abs(editor.SignatureBounds.Y - stable.Y) < .1 &&
                (Math.Abs(editor.SignatureBounds.Bottom - caret.Top + 4) < 1 ||
                 Math.Abs(editor.SignatureBounds.Top - caret.Bottom - 4) < 1),
                "CodeEditor/overlay: repeated positioning stays adjacent to the actual caret without accumulated margin drift");
            editor.Text = "Console.WriteLine(";
            editor.CaretIndex = editor.Text.Length;
            Pump(editor.RequestSignatureHelpAsync());
            Pump(editor.RequestCompletionAsync());
            Flush();
            check(!editor.SignatureBounds.Intersects(editor.CompletionBounds),
                "CodeEditor/overlay: signature and completion do not overlap when the caret is on the first row");
            editor.CloseCompletion();
            editor.Text = "var broken = ;\nvar another = ;";
            Pump(editor.RefreshDiagnosticsAsync());
            editor.CaretIndex = 0;
            check(editor.DiagnosticLine == 1 && editor.ErrorCount == 2,
                "CodeEditor/status: diagnostic link displays a source row number rather than the error count");
            Click(window, Part<Button>(editor, "PART_Diagnostics"));
            check(editor.CaretLine == 1 && editor.SelectionLength > 0,
                "CodeEditor/status: clicking the compact error link selects the advertised source row");
            check(
                ToolTip.GetPlacement(editor.View) == PlacementMode.Custom &&
                ToolTip.GetCustomPopupPlacementCallback(editor.View) != null,
                "CodeEditor/status: navigation diagnostics anchor to the code caret instead of the footer pointer");
            ToolTip.SetIsOpen(editor.View, false);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in Enum.GetValues<CodeEditorDensity>())
            {
                window.RequestedThemeVariant = theme;
                editor.Density = size;
                foreach (var width in new[] { 720d, 320d })
                {
                    window.Width = width;
                    Flush();
                    var header = Part<Border>(editor, "PART_Header");
                    var status = Part<Border>(editor, "PART_StatusBar");
                    var language = Part<TextBlock>(editor, "PART_Language");
                    var link = Part<Button>(editor, "PART_Diagnostics");
                    var icon = Part<PathIcon>(editor, "PART_ErrorIcon");
                    var langY = language.TranslatePoint(new Point(0, language.Bounds.Height / 2), status)!.Value.Y;
                    var linkY = link.TranslatePoint(new Point(0, link.Bounds.Height / 2), status)!.Value.Y;
                    check(
                        header.Bounds.Height <= 32 && status.Bounds.Height >= 28 && status.Bounds.Height <= 30 &&
                        Part<Button>(editor, "PART_CopyButton").Bounds.Height <= 22.1 && icon.Bounds.Width <= 10.1 &&
                        Math.Abs(langY - linkY) <= 1,
                        $"CodeEditor/compact chrome: {theme}/{size}/{width} header/footer/buttons stay compact and vertically centered ({header.Bounds.Height:F0}/{status.Bounds.Height:F0})");
                    using var frame = window.CaptureRenderedFrame()!;
                    Save(frame, output, $"codeeditor-compact-{theme}-{size}-{width}.png");
                }
            }
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static void Visuals(ThemeVariant theme, CodeEditorDensity size, Action<bool, string> check, string? output)
    {
        foreach (var width in new[] { 720, 320 })
        {
            var editor = new CodeEditor
            {
                Text = CodeEditorDemoViewModel.JsonSample, CodeType = CodeType.Json, Header = "settings.json",
                ShowCopyButton = true, ShowFormatButton = true, Margin = new Thickness(16)
            };
            editor.Density = size;
            var window = Open(editor, width, 520, theme);
            try
            {
                editor.FocusEditor();
                editor.Select(0, editor.Text!.Length);
                editor.WordWrap = true;
                Flush();
                check(
                    Math.Abs(editor.LineHeight -
                             (size == CodeEditorDensity.Small ? 20 : size == CodeEditorDensity.Large ? 24 : 22)) <
                    .01 && editor.View.Bounds.Width <= width,
                    $"CodeEditor/layout: {theme}/{size}/{width} uses library density and viewport bounds");
                check(editor.LineCount == 18 && editor.View.LastRenderedRows > 0 && editor.View.LastRenderedRows < 100,
                    $"CodeEditor/virtualization: {theme}/{size}/{width} renders only visible rows and retains logical line numbers");
                using var frame = window.CaptureRenderedFrame()!;
                var pixels = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
                var pointer = Marshal.AllocHGlobal(pixels.Length);
                try
                {
                    frame.CopyPixels(new PixelRect(frame.PixelSize), pointer, pixels.Length, frame.PixelSize.Width * 4);
                    Marshal.Copy(pointer, pixels, 0, pixels.Length);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }

                VerifyOutline(editor, window, frame, pixels, theme, size, width, check);
                check(
                    Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct()
                        .Take(51).Count() > 50,
                    $"CodeEditor/pixels: {theme}/{size}/{width} draws actual shaped glyphs, highlighting and selection");
                Save(frame, output, $"codeeditor-{theme}-{size}-{width}.png");
            }
            finally
            {
                window.Close();
                Flush();
            }
        }
    }

    private static void VerifyOutline(CodeEditor editor, Window window, Bitmap frame, byte[] pixels, ThemeVariant theme,
        CodeEditorDensity size, int width, Action<bool, string> check)
    {
        editor.TryFindResource("CodeEditor.Accent", editor.ActualThemeVariant, out var resource);
        var accent = ((ISolidColorBrush)resource!).Color;
        var origin = editor.TranslatePoint(default, window)!.Value;
        var scale = frame.PixelSize.Width / window.ClientSize.Width;
        var left = (int)Math.Round(origin.X * scale);
        var top = (int)Math.Round(origin.Y * scale);
        var right = left + (int)Math.Round(editor.Bounds.Width * scale) - 1;
        var bottom = top + (int)Math.Round(editor.Bounds.Height * scale) - 1;
        var red = frame.Format == PixelFormat.Rgba8888 ? 0 : 2;

        bool Accent(int x, int y)
        {
            var i = (y * frame.PixelSize.Width + x) * 4;
            return Math.Abs(pixels[i + 2 - red] - accent.B) + Math.Abs(pixels[i + 1] - accent.G) +
                Math.Abs(pixels[i + red] - accent.R) < 90;
        }

        bool Corner(int x, int y, int dx, int dy)
        {
            return Enumerable.Range(1, 5)
                .Any(a => Enumerable.Range(1, 5).Any(b => a + b <= 7 && Accent(x + a * dx, y + b * dy)));
        }

        check(
            Corner(left, top, 1, 1) && Corner(right, top, -1, 1) && Corner(left, bottom, 1, -1) &&
            Corner(right, bottom, -1, -1),
            $"CodeEditor/outline: {theme}/{size}/{width} all four selected rounded corners remain visible");
    }

    private static void Demo(Action<bool, string> check, string? output)
    {
        foreach (var width in new[] { 1440, 600 })
        {
            var demo = new CodeEditorDemoView { Margin = new Thickness(32) };
            var window = Open(new ScrollViewer { Content = demo }, width, 1100);
            try
            {
                var editor = demo.FindControl<CodeEditor>("Editor")!;
                editor.FocusEditor();
                editor.CaretIndex = editor.Text!.Length;
                window.KeyTextInput("\n// consumer edit");
                Flush();
                check(
                    demo.Model.Code == editor.Text &&
                    editor.Text.EndsWith("// consumer edit", StringComparison.Ordinal),
                    $"CodeEditor/binding: {width} native editing updates compiled two-way binding");
                var toggle = demo.FindControl<ToggleSwitch>("ReadOnlyToggle")!;
                toggle.Focus();
                Press(window, Key.Space, PhysicalKey.Space);
                check(demo.Model.IsReadOnly && editor.IsReadOnly,
                    $"CodeEditor/demo: {width} readonly binding updates the independent engine");
                check(Grid.GetRow(demo.FindControl<Border>("PropertiesSection")!) == (width < 850 ? 1 : 0),
                    $"CodeEditor/demo: {width} responsive property layout");
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, $"codeeditor-demo-{width}.png");
            }
            finally
            {
                window.Close();
                Flush();
            }
        }
    }

    private static void Performance(Action<bool, string> check, string? output)
    {
        var text = string.Join('\n',
            Enumerable.Range(1, 10000).Select(i =>
                $"var item{i:D5} = new {{ Name = \"代码行 {i}\", Value = {i} }}; // 10000-line benchmark"));
        var timer = Stopwatch.StartNew();
        var editor = new CodeEditor
        {
            Text = text, CodeType = CodeType.CSharp, Header = "10000-lines.cs", ShowCopyButton = true,
            ShowFormatButton = true
        };
        var window = Open(editor, 1000, 700);
        timer.Stop();
        var load = timer.Elapsed.TotalMilliseconds;
        try
        {
            editor.FocusEditor();
            editor.CaretIndex = editor.Document.Lines[5000].Start + 4;
            Flush();
            var edits = new List<double>();
            var scrolls = new List<double>();
            var measurements = new List<string> { "sample,scroll_ms,shape_ms,render_ms" };
            var inputMeasurements = new List<string> { "sample,input_ms,shape_ms,render_ms,pause_ms,ui_bytes" };
            for (var i = 0; i < 80; i++)
            {
                var pause = GC.GetTotalPauseDuration();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                timer.Restart();
                window.KeyTextInput("x");
                Flush();
                timer.Stop();
                if (i >= 5) edits.Add(timer.Elapsed.TotalMilliseconds);
                inputMeasurements.Add(FormattableString.Invariant(
                    $"{i},{timer.Elapsed.TotalMilliseconds:F3},{editor.View.LastShapeMilliseconds:F3},{editor.View.LastRenderMilliseconds:F3},{(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F3},{GC.GetAllocatedBytesForCurrentThread() - bytes}"));
            }

            editor.Select(0, editor.Text!.Length);
            Flush();
            for (var i = 0; i < 80; i++)
            {
                timer.Restart();
                editor.ScrollToLine(1 + i * 123);
                Flush();
                timer.Stop();
                if (i >= 5) scrolls.Add(timer.Elapsed.TotalMilliseconds);
                measurements.Add(FormattableString.Invariant(
                    $"{i},{timer.Elapsed.TotalMilliseconds:F3},{editor.View.LastShapeMilliseconds:F3},{editor.View.LastRenderMilliseconds:F3}"));
            }

            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "scroll-samples.csv"), measurements);
                File.WriteAllLines(Path.Combine(output, "input-samples.csv"), inputMeasurements);
            }

            check(
                editor.LineCount == 10000 && editor.View.LastRenderedRows < 80 && editor.View.CachedLayoutCount <= 192,
                "CodeEditor/performance: 10000 lines, full selection and document-wide scrolling keep rows and shaping cache bounded");
            check(editor.GetVisualDescendants().Count() < 150,
                "CodeEditor/performance: visual-tree size is independent of line count");
            var editP95 = Percentile(edits, .95);
            var scrollP95 = Percentile(scrolls, .95);
            check(editP95 < 16.7,
                $"CodeEditor/performance: 10000-line edit+render p95 {editP95:F2}ms meets 16.7ms frame budget");
            check(scrollP95 < 16.7,
                $"CodeEditor/performance: 10000-line scroll+render p95 {scrollP95:F2}ms meets 16.7ms frame budget");
            editor.Text = text + "\nitem09";
            editor.CaretIndex = editor.Text.Length;
            timer.Restart();
            var completed = Pump(editor.RequestCompletionAsync());
            Flush();
            timer.Stop();
            var completion = timer.Elapsed.TotalMilliseconds;
            check(completed && editor.CompletionItems.Count > 0 && completion < 100,
                $"CodeEditor/performance: indexed 10000-line completion including first popup render {completion:F2}ms");
            editor.CloseCompletion();
            editor.Text = text;
            window.Width = 600;
            Flush();
            timer.Restart();
            editor.WordWrap = true;
            Flush();
            timer.Stop();
            var wrap = timer.Elapsed.TotalMilliseconds;
            editor.CaretIndex = editor.Document.Lines[5000].Start + 4;
            Flush();
            var wrappedEdits = new List<double>();
            var wrappedScrolls = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                timer.Restart();
                window.KeyTextInput("x");
                Flush();
                timer.Stop();
                if (i >= 5) wrappedEdits.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart();
                editor.ScrollToLine(1 + i * 247);
                Flush();
                timer.Stop();
                if (i >= 5) wrappedScrolls.Add(timer.Elapsed.TotalMilliseconds);
            }

            var wrappedEditP95 = Percentile(wrappedEdits, .95);
            var wrappedScrollP95 = Percentile(wrappedScrolls, .95);
            check(
                editor.LineCount == 10000 && editor.View.Rows.Count > 10000 && wrappedEditP95 < 16.7 &&
                wrappedScrollP95 < 16.7,
                $"CodeEditor/performance: wrapped 10000-line edit p95 {wrappedEditP95:F2}ms / scroll p95 {wrappedScrollP95:F2}ms meets frame budget");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                var summary =
                    $"metric,milliseconds\nload_and_first_frame,{load.ToString("F3", CultureInfo.InvariantCulture)}\nedit_render_p95,{editP95.ToString("F3", CultureInfo.InvariantCulture)}\nscroll_render_p95,{scrollP95.ToString("F3", CultureInfo.InvariantCulture)}\ncompletion_first_popup,{completion.ToString("F3", CultureInfo.InvariantCulture)}\nwrap_10000_lines,{wrap.ToString("F3", CultureInfo.InvariantCulture)}\nwrapped_edit_render_p95,{wrappedEditP95.ToString("F3", CultureInfo.InvariantCulture)}\nwrapped_scroll_render_p95,{wrappedScrollP95.ToString("F3", CultureInfo.InvariantCulture)}\n";
                File.WriteAllText(Path.Combine(output, "performance.csv"), summary);
                File.WriteAllText(Path.Combine(output, "performance-host.txt"),
                    $"{RuntimeInformation.OSDescription}\n{RuntimeInformation.ProcessArchitecture}\n{RuntimeInformation.FrameworkDescription}\nUTF16 length: {text.Length}\nLogical lines: 10000\n");
                editor.CloseCompletion();
                editor.Select(0, 0);
                editor.ScrollToLine(5000);
                Flush();
                using var frame = window.CaptureRenderedFrame()!;
                Save(frame, output, "codeeditor-10000-lines.png");
            }
        }
        finally
        {
            window.Close();
            Flush();
        }
    }

    private static double Percentile(List<double> values, double p)
    {
        values.Sort();
        return values[Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * p) - 1)];
    }

    private static T Pump<T>(Task<T> task)
    {
        Pump((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        if (!task.IsCompleted) throw new TimeoutException("CodeEditor service did not complete.");
        task.GetAwaiter().GetResult();
        Flush();
    }

    private static T Part<T>(CodeEditor editor, string name) where T : Control
    {
        return editor.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    }

    private static Window Open(Control content, double width, double height, ThemeVariant? theme = null)
    {
        var window = new Window
            { Content = content, Width = width, Height = height, RequestedThemeVariant = theme ?? ThemeVariant.Light };
        window.Show();
        Flush();
        return window;
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }

    private static void Press(Window window, Key key, PhysicalKey physical,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Flush();
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Bitmap frame, string? output, string name)
    {
        if (output == null) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }

    private sealed class DeferredCompletion : ICodeCompletionProvider
    {
        internal readonly TaskCompletionSource<IReadOnlyList<CodeCompletionItem>> Result = new();

        public Task<IReadOnlyList<CodeCompletionItem>> GetCompletionsAsync(CodeCompletionContext context,
            CancellationToken cancellationToken)
        {
            return Result.Task;
        }
    }

    private sealed class DeferredFormatter : ICodeFormatter
    {
        internal readonly TaskCompletionSource<string> Result = new();

        public Task<string> FormatAsync(CodeFormattingContext context, CancellationToken cancellationToken)
        {
            return Result.Task;
        }
    }
}