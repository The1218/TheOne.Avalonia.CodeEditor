# CodeEditor

[简体中文](USAGE.zh-cn.md) · [Project](../README.md)

`CodeEditor` is an independent single-document editor in **TheOne.Avalonia.CodeEditor**
(.NET 10, Avalonia 12.1.4). It has no AvaloniaEdit, TextMate or Roslyn dependency.
The base library does not depend on the optional editor. Add its project/package and
register its standalone theme explicitly (no `OneTheme` required):

```xml
<Application.Styles>
  <one:CodeEditorTheme />
</Application.Styles>
```

Here `one` maps to `using:TheOne.Avalonia.Themes`. In views, map `controls` to
`using:TheOne.Avalonia.Controls`. Declare the real view model with `x:DataType`
and keep compiled bindings enabled:

```xml
<controls:CodeEditor Header="settings.json" Height="420"
    Text="{Binding Code, Mode=TwoWay}"
    CodeType="{Binding SelectedCodeType}"
    IsReadOnly="{Binding IsReadOnly}"
    ShowLineNumbers="{Binding ShowLineNumbers}"
    IsSyntaxHighlightingEnabled="{Binding EnableHighlighting}" />
```

`SelectedCodeType` is a `TheOne.Avalonia.Controls.CodeType` enum, not a string.
For a fixed language use `CodeType="Json"`.

| Property | Default | Contract |
| --- | --- | --- |
| `Text` | `""` | Nullable; null displays as empty. Two-way by default. External replacement resets document history; configuration changes do not. |
| `Density` | `Normal` | Small / Normal / Large via the package-owned CodeEditorDensity enum; no ControlSizing dependency. |
| `CodeType` | `PlainText` | One of the 28 profiles listed below. Existing enum values 0–12 remain stable. |
| `IsReadOnly` | `false` | Blocks user input, cut, paste, deletion, drag insertion and undo/redo. Selection, copying, search, scrolling and host Text updates remain available. |
| `ShowLineNumbers` | `true` | Toggles the fixed, directly drawn gutter. |
| `IsSyntaxHighlightingEnabled` | `true` | Toggles coloring without changing the selected language or document. PlainText is always uncolored. |
| `WordWrap` | `false` | Wraps visual lines; line numbers still identify logical document lines. |
| `TabSize` | `4` | Valid range 1–32. |
| `ConvertTabsToSpaces` | `true` | Affects newly inserted indentation, not existing text. |
| `Header` | `null` | Optional content. Empty header is collapsed unless the copy button is shown. |
| `ShowCopyButton` | `false` | Copies the whole document without changing selection. |
| `ShowFormatButton` | `false` | Displays the built-in format command; disabled in read-only mode and during formatting. |
| `ShowStatusBar` | `true` | Language and one-based document caret line/column. Columns count UTF-16 positions, not visual glyphs. |
| `IsCurrentLineHighlighted` | `true` | Subtle current-line background. |
| `IsAutoIndentEnabled` | `true` | Enter copies indentation and recognizes block openers; paired braces create an indented empty line. |
| `IsCompletionEnabled` | `true` | Enables delayed automatic suggestions and explicit completion requests. |
| `IsTabCompletionEnabled` | `true` | Tab requests and accepts a function, property, identifier or contextual completion even before the list opens. Blank lines/selected text still indent; Shift+Tab outdents. |
| `IsInlineCompletionEnabled` | `true` | Shows the selected completion's first-line suffix at 42% opacity after the caret. Uses the same candidates as the completion list. |
| `IsSignatureHelpEnabled` | `true` | Shows known method signatures and the active parameter while entering a call. |
| `IsDiagnosticsEnabled` | `true` | Runs built-in diagnostics after a 320 ms idle delay; displays source squiggles, tooltips and problem count. |
| `IsSemanticHighlightingEnabled` | `true` | Colors known single-file symbol bindings independently of lexical highlighting and diagnostic display. |
| `AutoInsertPairs` | `true` | Inserts paired brackets/quotes, skips tracked closing characters and removes empty pairs together. |
| `CompletionProvider` | `null` | Optional `ICodeCompletionProvider`; null uses built-in completion. |
| `Formatter` | `null` | Optional `ICodeFormatter`; null uses the safe built-in formatters. |

Profiles: PlainText, CSharp, JavaScript, TypeScript, Json, Xml, Axaml, Html, Css,
Sql, Python, Yaml, Markdown, C, Cpp, Java, Go, Rust, Kotlin, Swift, Php, Ruby,
Bash, PowerShell, Dart, Lua, Toml and Ini. These are lexical profiles, not complete
language parsers. Embedded languages and compiler-specific dialects are not fully parsed;
formatting and semantic completion can be extended through their service interfaces.

`CaretIndex`, `SelectionStart` and `SelectionLength` are writable UTF-16 document
offsets. `Select(start, length)`, `SelectedText`, `ReplaceSelection(text)`,
`IndentSelection(outdent)`, `ScrollToLine(oneBasedLine)` and `FindNext(text)` expose
editing without exposing the renderer. User-edit operations respect read-only mode.
Read-only observations include `LineCount`, `CaretLine`, `CaretColumn`, `LanguageName`,
`HighlightingError`, `IsFormatting`, `FormattingError` and `CompletionError`.
An undefined enum value falls back to plain text and sets
`HighlightingError`; it never rewrites the document.

Commands: `CopyCommand`, `CopyAllCommand`, `CutCommand`, `PasteCommand`,
`SelectAllCommand`, `UndoCommand`, `RedoCommand`, `FormatCommand`, `CompleteCommand`, `NextDiagnosticCommand` and `GoToDiagnosticCommand`.
Modification commands are unavailable in read-only mode.
The built-in copy button automatically uses Avalonia's `TopLevel.Clipboard` to
copy the entire document, including in read-only mode; no consumer `Click` handler
is needed. It preserves the selection and caret, and is disabled for empty text.
`FocusEditor()` focuses the editor's Avalonia input surface. `CopyAllAsync()` reports whether
the platform clipboard accepted the request. No clipboard service returns false.

Use platform copy/select/undo/redo shortcuts. Ctrl/Cmd+F opens find; Escape closes it.
Tab completes the current identifier or context, and can skip a tracked closing symbol;
on blank lines or selected text it indents. Shift+Tab unindents. Ctrl+Space opens completion; Enter accepts it,
Up/Down selects an item and Escape dismisses it. Alt+Shift+F formats the document.
Ctrl+Tab/Ctrl+Shift+Tab move focus outside the code area. Read-only is a UI editing
policy, not an authorization boundary.
Ctrl/Cmd+Shift+Space requests parameter hints; Ctrl/Cmd+Up/Down switches overloads
while hints are visible. F8/Shift+F8 selects the next/previous problem. Hover a
squiggle for its diagnostic code and message. The compact status link uses a 10 px
error icon followed by a source row number and message; clicking it selects that row.
The message is ellipsized to the available width. Its wrapping tooltip retains the full
diagnostic code, line, column and message. `DiagnosticMessage` and `DiagnosticDetail`
expose those complete strings without truncation.
The status bar has a 28 DIP minimum height and an 18 DIP text line height.
Line/column labels and values share one text layout and the vertical center of the error text.
`ErrorCount` retains the total error count and `DiagnosticSummary` retains its existing summary contract.
`DiagnosticLine` is the displayed row (0 when empty); `GoToDiagnosticCommand` selects it.

Diagnostic and selection ranges use the shaped text's visual bounds, keeping bidi fragments
separate and expanding partial UTF-16 spans to complete graphemes. Soft-wrap end offsets are
exclusive, and an EOF insertion marker belongs to the final visual row only.
This changes paint geometry only; document offsets and selected text remain UTF-16 based.
Diagnostic hover uses the same visual fragments, including expanded graphemes, rather than caret affinity at a range boundary.

Semantic colors are an independent, immutable UTF-16 span layer generated by the shared
background analysis after the same 320 ms idle delay. Known types, methods, fields, properties,
parameters and local variables use visible bindings, including shadowing; unknown external
symbols remain at their lexical colors. Theme brushes are `CodeEditor.SemanticType`,
`CodeEditor.SemanticMethod`, `CodeEditor.SemanticField`, `CodeEditor.SemanticProperty` and
`CodeEditor.SemanticParameter`; local variables follow `Foreground`.
`IsSyntaxHighlightingEnabled` controls lexical coloring; `IsSemanticHighlightingEnabled`
controls this layer. Disabling diagnostics leaves semantic colors available. Edits clear stale
spans, and only the matching revision/language can publish replacements. Color publication
reuses cached shaping; it does not rebuild document rows. This does not provide project-wide
compiler semantic tokens or infer unresolved members.
Languages without symbol-scope support do not schedule color-only background work.

Lexical highlighting also has compact, incremental visual scopes inspired by TextMate:
escapes, regular expressions, tag/attribute names, function names, preprocessor directives,
headings, code fences and interpolation delimiters. JavaScript/TypeScript templates, C#
interpolated strings and Python f-strings retain nested contexts; Markdown fenced blocks
can use the selected embedded language. Changing a delimiter or fence language updates
following lines until both lexical and visual states stabilize. Scope nesting is bounded
at 16 contexts. The visual scopes do not change diagnostic/completion tokens, and the
independent semantic layer can override symbol colors. This is a built-in scanner, not a
TextMate grammar/Oniguruma implementation; external `.tmLanguage` files and theme imports
are not supported.

Light/Dark follow Avalonia theme scopes. `CodeEditor.Density` supports
Small/Normal/Large (13/14/16 default font sizes). Explicit font overrides remain
supported. Package resources `CodeEditor.Surface`, `CodeEditor.Text`, `CodeEditor.InputLine`, `CodeEditor.Accent` and
`CodeEditor.TextSelection` control chrome and selection. Syntax resources are
`CodeEditor.Keyword`, `String`, `Comment`, `Number`, `Type`, `Punctuation`,
plus `GutterForeground` and `CurrentLineBackground` under the same prefix.
Additional scope brushes use the same prefix: `Escape`, `Regex`, `Tag`, `Attribute`,
`Function`, `Preprocessor`, `Heading`, `Fence` and `Interpolation`.
Consumer resource overrides update existing editors.

The library owns its line index, incremental lexical state, edit-delta undo,
viewport drawing and bounded shaped-glyph cache. Text shaping, font fallback,
clipboard and the preedit/surrounding-text IME contract use native Avalonia APIs.
No control is created per line or token. Soft wrapping uses monospaced display
columns; use a monospace font for predictable tab and wrap alignment.

Built-in completion combines language keywords, document identifiers/snippets and a
background document symbol index. It resolves declared types, fields/properties,
methods, parameters, common assignments and constructors; JavaScript object literals
and Python `self` attributes are included. Built-in standard-function/member catalogs
cover C#, JavaScript/TypeScript, Python, C/C++, Go, Rust, Java, Kotlin, Swift, Dart,
PHP, Ruby, Lua, Bash, PowerShell and SQL. HTML/XML/AXAML provide contextual elements
and attributes; CSS provides properties and values. JSON/YAML/TOML/INI retain literal,
document-identifier and paired-symbol completion. Markdown/plain text offer document identifiers.

Each editor runs at most one expensive semantic snapshot at a time. In documents over
100,000 UTF-16 characters, requests immediately after an edit share an 80 ms idle window;
obsolete queued work is canceled before parsing. The existing 320 ms diagnostics timer
already exceeds that window, and cached analysis and preliminary indexed suggestions remain immediate.
Semantic snapshots reuse their symbol index instead of rebuilding unused word indexes;
completion filters by prefix before retaining candidates and creates only the best 100 symbol items.
Cancellation is checked during snapshot splitting, lexical scanning, indexing and candidate filtering.
Edits cancel obsolete analysis and completion before allocating the replacement public Text snapshot.
The editor does not force garbage collection or change application-wide GC settings.
Declaration modifiers such as `public ` automatically request type/keyword suggestions.
Suggestions match the typed prefix, ignoring case; unrelated infix and initial-letter matches are excluded.
Member access limits the list to the receiver's known members, and declaration contexts show types and keywords.
Programming-language document suggestions exclude comment/string contents and known locals outside the active scope.
The same prefix filter applies to custom providers. C# includes
for/foreach/reverse-loop, property and method snippets. Tab confirms a built-in snippet
shortcut, and a second Tab expands it as one undo step; no double-click timer is required.
Enter, pointer acceptance and `AcceptCompletion` expand it immediately. Tab/Shift+Tab
then navigate editable fields, including empty parameter lists and the body. Linked
loop identifiers and backing fields update together; Escape, leaving the editor,
external text/language changes and read-only changes end the session.
Single-line rows show kind, highlighted match and a trimmed detail
with a full tooltip. Standard C# catalogs include collection/LINQ, StringBuilder,
DateTime, file/path and exception members; catalogs are finite and require no reflection.
Periods, `::`, `->` and Lua's `:` request the relevant receiver members;
method items show their signature, insert parentheses and place the caret inside.
Known return types support chained completion. Unknown external types have no invented members.
Single-file C# analysis retains generic parameter/return types, explicit generic method
arguments, array/collection indexing and local base members. Named argument completion
and active parameter hints support C# (`name:`) and Python (`name=`). Common C#
constructors provide parameter hints; a generic container's element type flows through
known methods and properties. Completion filtering and ranking run off the UI thread.

Useful snippet shortcuts include `method`/`func`, `async`, `ctor`, `prop`, `propg`,
`propfull`, `for`, `foreach`, `forr`, `if`, `while`, `try`, `switch` and `class` in C#.
Function snippets also cover JavaScript/TypeScript (`function`, `func`, `afn`), Python
(`def`, `func`), C/C++/Java/Dart/Go/Swift/Bash (`func`), Rust (`fn`), Kotlin (`fun`),
PHP/Lua/PowerShell (`function`) and Ruby (`def`). Generated fields respect indentation
settings and document line endings. PreviewHost's “函数片段 · Tab Tab” button loads a tryout.
Go receiver methods and struct fields, Rust `impl` methods, optional member access,
and shell/cmdlet insertion without invalid call parentheses are supported.
Large documents can show the incremental word list first, then enrich that same
list asynchronously; typing or accepting an item cancels obsolete enrichment.
`RequestCompletionAsync()` and `AcceptCompletion(item)` are also callable directly.
Implement `ICodeCompletionProvider.GetCompletionsAsync(context, cancellationToken)`
for project-specific or LSP suggestions. An item has Label, InsertText, Detail,
Kind and a relative CaretOffset (-1 means end). Acceptance is one undo transaction.
`RequestSignatureHelpAsync()` publishes `SignatureHelp` with overloads, named
parameters/defaults, active parameter and call-start offset. Nested calls and literal
commas are excluded from the outer argument count. Python hints omit `self`/`cls`.

`RefreshDiagnosticsAsync()` publishes `Diagnostics`, `ErrorCount` and
`DiagnosticSummary`; each `CodeDiagnostic` includes a UTF-16 span, one-based line/column,
severity, stable code and message. `GoToNextDiagnostic(backwards)` selects a problem.
Built-ins detect unmatched delimiters (including the expected closing symbol), unterminated literals/comments,
missing names/expressions/conditions and invalid common numeric literal forms for the selected language.
JSON/XML/AXAML use parser diagnostics; HTML checks tags plus JavaScript/JSON script and CSS style contents.
Python checks blocks, indentation and assignment in conditions; YAML checks indentation, dedents and duplicate keys.
Ruby/Lua/Bash check block terminators and required block keywords; PowerShell checks function/control headers.
CSS checks declaration names, colons and values; SQL checks definite missing SELECT/FROM/WHERE operands and CASE/END blocks.
TOML/INI check section headers, assignments and duplicate keys; TOML also checks bare values, table redefinition,
strings and flow delimiters. Markdown checks recognized fenced code and maps errors back to source rows;
plain text and prose have no invented syntax errors.
Additional C# checks cover incomplete modifier/member declarations, empty call arguments,
definite missing statement terminators, primitive literal argument mismatches and
unknown members of complete local types. Inheritance and external members are excluded
from missing-member checks. Known C#/TypeScript/Python calls check argument counts, optional and variadic parameters;
Python unpacked arguments are handled conservatively. JavaScript allows extra or omitted arguments.
Call errors include the known expected signatures.
C# also reports missing method return types (`CSDECL003`), parameters missing a type/name
(`CSPARAM002`), and empty primitive-returning bodies (`CSRETURN003`). Constructors retain
their own rules; modifiers and attributes are excluded from return types. Known primitive
arithmetic participates in assignment, argument and return checks. Anonymous-function returns
are not checked against the enclosing method. Invocation hints prefer compatible overloads
and stay hidden inside recognized parameter declarations. `Header` is display content;
`CodeType` selects the language even if the header contains a different file extension.
Analysis is shared by completion, signature hints and diagnostics, runs on immutable snapshots, and rejects stale results.
This lightweight single-file analysis does not replace a compiler: project imports, external inheritance,
external assemblies, full type checking and every language grammar are outside its scope.

`FormatDocumentAsync(cancellationToken)` runs built-in formatting off the UI thread
and applies it as one undo transaction. Newer edits, language/read-only changes
and cancellation invalidate pending results. Invalid JSON/XML preserves the input
and sets `FormattingError`. The built-in formatting levels are:

| Languages | Built-in formatting |
| --- | --- |
| JSON | Parse and pretty-print with configured indentation. |
| XML / AXAML | Element indentation while preserving mixed text and `xml:space` data. |
| C#, C, C++, Java, CSS, Dart, JavaScript, TypeScript | Structural block line breaks, indentation and safe assignment/comma spacing. Existing newlines/literals are retained; ambiguous JavaScript slash syntax uses conservative indentation. |
| Go, Rust, Kotlin, Swift, PHP, PowerShell | Token-aware indentation/spacing; existing newlines are retained to protect semicolon insertion. |
| SQL | Keyword capitalization and clause line breaks; quoted text is retained. |
| Python | Normalizes block/continuation indentation, assignment/comma/annotation/mapping spacing; protects multiline string payloads and slices. |
| YAML | Normalizes mapping/sequence indentation and flow collection spacing; protects quotes, URLs, comments and block scalar content. YAML indentation always uses spaces. |
| HTML | Indents element structure, handles void tags and quoted attributes, preserves mixed text and raw script/style/pre/textarea content. |
| Ruby, Lua, TOML, INI | Conservative spacing/whitespace cleanup; significant indentation and multiline strings are retained. |
| Bash | Conservative trailing-whitespace cleanup. |
| Plain text, Markdown | Preserved verbatim; whitespace can be content. |

These rules are not replacements for every compiler's canonical formatter.
Implement `ICodeFormatter.FormatAsync(context, cancellationToken)` for project-specific
canonical formatting. Python, YAML and HTML already have built-in implementations. Providers receive
an immutable text snapshot and must honor cancellation. The editor rejects stale results.

The 10,000-line demo and benchmark exercise typing, full selection, large scroll
jumps, completion and viewport-cache bounds. `Text` binding still publishes a
complete string on each edit; expensive consumer handlers can dominate latency.
Frame timings depend on hardware, line lengths, fonts and rendering backend.

PreviewHost navigation includes CodeEditor. `--codeeditor` opens its standalone
desktop demo. `--verify-codeeditor [output]` runs only this control's binding, input,
language, formatting, completion, 10,000-line performance, layout and pixel checks. Native consumers use `--require-aot
--verify-codeeditor [output]`. Generated artifacts belong in `.preview/codeeditor/`.
`--verify-codeeditor-functional [output]` runs functional/visual checks separately;
`--verify-codeeditor-performance [output]` runs the same 10,000-line benchmark in
a fresh process. Run benchmarks serially, after builds and outside heavy IDE indexing.
Their timing thresholds are unchanged by these separate entries.
`--verify-codeeditor-highlighting [output]` runs the incremental scope and highlighting pixel checks.


Additional single-file checks diagnose unfinished type/namespace bodies, duplicate function parameters,
duplicate lexical declarations in supported static/lexical scopes, missing const initializers and
incompatible literals assigned to explicit primitive types. Python annotation mismatches are warnings;
dynamic-language reassignment is allowed. C/C++ forward declarations, C# semicolon bodies,
Rust unit/tuple structs and external modules, Kotlin bodyless classes and TypeScript ambient declarations are retained.

The shared scope index covers C#, JavaScript/TypeScript, Python, C/C++, Java, Go, Rust,
Kotlin, Swift, PHP, Ruby, Dart and Lua. Simple expression reads report `*NAME001` when
no visible single-file declaration is known. Static/annotated primitive assignments also
check known variables and call results (`*TYPE003`). Known calls check required/default/rest
parameters, named arguments and primitive argument types (`CALL001`); JavaScript and Lua retain
its permissive argument counts. Explicit returns check missing/unexpected values
(`*RETURN001`) and known incompatible types (`*RETURN002`), including supported C#/Kotlin
expression bodies and typed TypeScript arrows. Python annotation mismatches are warnings;
unresolved references in dynamic languages are warnings because globals can be supplied by a host.

Completion excludes later local declarations and closed blocks, resolves the nearest binding,
and ranks candidates with a compatible known assignment/argument/return type ahead of incompatible
candidates with the same prefix score. Inline previews use this same ordered list.
JavaScript/TypeScript `var` retains function scope; Python function reads allow later module bindings.
Unknown external types, union types, complex operators, lambda/comprehension scopes, control-flow
return coverage and full language-specific overload/conversion rules are not inferred.
Shell and declarative languages retain their language-specific checks; this is not a compiler or project-wide analysis.

With auto-indent enabled, Enter inside a one-line brace block moves the matching closer onto its own line
and indents the remaining body. C-family statement fragments also use the existing formatter.
Matching XML/AXAML/HTML end tags and Ruby/Lua/Bash block keywords are handled similarly;
Python/YAML headers increase indentation, while HTML void elements do not. Typed closing brackets on an
otherwise blank indented line align with a known opener. The operation is one undo step and stays local
to the current row (backward closer alignment is bounded); strings/comments are protected.

At a line end, normal typing also displays a translucent inline suffix from the selected candidate;
Tab accepts a method/property/identifier directly. Arrow-key candidate selection updates the suffix.
The preview never changes document text, layout extent, clipboard contents or undo history.
Selection, read-only mode, IME composition, stale requests and Escape dismiss it; existing text to the right is not overpainted.
Built-in snippets keep their Tab-Tab workflow: the first Tab arms the shortcut, and the second expands the fields. A first-line preview is shown when the expansion continues the shortcut text.
