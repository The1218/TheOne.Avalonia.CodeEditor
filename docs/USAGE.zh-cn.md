# CodeEditor 代码编辑器

[English](USAGE.en.md) · [项目](../README.md)

`CodeEditor` 位于可选程序集 **TheOne.Avalonia.CodeEditor**，面向 .NET 10、
Avalonia 12.1.4，使用自有编辑内核，不依赖 AvaloniaEdit、TextMate 或 Roslyn。主库不依赖这个可选程序集。
消费者引用独立项目或包，在 App.axaml 显式注册编辑器主题，无需 `OneTheme`：

```xml
<Application.Styles>
  <one:CodeEditorTheme />
</Application.Styles>
```

上述 `one` 对应 `using:TheOne.Avalonia.Themes`。视图中的 `controls` 对应
`using:TheOne.Avalonia.Controls`；视图必须以实际 ViewModel 声明 `x:DataType`，
保持编译绑定：

```xml
<controls:CodeEditor Header="settings.json" Height="420"
    Text="{Binding Code, Mode=TwoWay}"
    CodeType="{Binding SelectedCodeType}"
    IsReadOnly="{Binding IsReadOnly}"
    ShowLineNumbers="{Binding ShowLineNumbers}"
    IsSyntaxHighlightingEnabled="{Binding EnableHighlighting}" />
```

`SelectedCodeType` 直接使用 `TheOne.Avalonia.Controls.CodeType` 枚举；固定语言可写
`CodeType="Json"`，不需要字符串转换器。

| 属性 | 默认值 | 契约 |
| --- | --- | --- |
| `Text` | 空文本 | 默认双向绑定，null 显示为空。宿主替换文本会重置历史；配置开关不会。 |
| `Density` | `Normal` | 本包 CodeEditorDensity 枚举的 Small / Normal / Large，不依赖主库 ControlSizing。 |
| `CodeType` | `PlainText` | 下方列出的 28 种语言配置。原枚举值 0–12 保持不变。 |
| `IsReadOnly` | `false` | 阻止用户输入、剪切、粘贴、删除、拖入文本和撤销重做；保留选择、复制、查找、滚动及宿主更新 Text。 |
| `ShowLineNumbers` | `true` | 直接绘制固定行号区，隐藏时释放宽度。 |
| `IsSyntaxHighlightingEnabled` | `true` | 独立控制着色，不改变 CodeType 与文档；PlainText 始终不着色。 |
| `WordWrap` | `false` | 按视觉宽度换行，行号仍对应逻辑行。 |
| `TabSize` | `4` | 有效范围 1–32。 |
| `ConvertTabsToSpaces` | `true` | 仅影响新输入缩进，不改写已有文本。 |
| `Header` | `null` | 可选标题内容；为空且未显示复制按钮时折叠页头。 |
| `ShowCopyButton` | `false` | 复制全部文本，不改变当前选择。 |
| `ShowFormatButton` | `false` | 显示内置格式化按钮；只读及格式化进行中不可执行。 |
| `ShowStatusBar` | `true` | 显示语言及从 1 开始的文档行列；列按 UTF-16 文档位置计数，不是视觉字形列数。 |
| `IsCurrentLineHighlighted` | `true` | 当前行弱背景提示。 |
| `IsAutoIndentEnabled` | `true` | Enter 继承缩进，识别块起始符；成对括号间换行会创建缩进空行。 |
| `IsCompletionEnabled` | `true` | 启用输入后的延迟提示和显式补全请求。 |
| `IsTabCompletionEnabled` | `true` | 列表未打开时，Tab 也可请求并接受函数、属性、标识符和上下文补全；空行/选区仍缩进，Shift+Tab 反缩进。 |
| `IsInlineCompletionEnabled` | `true` | 在光标后以 42% 不透明度显示当前候选的首行后缀，与补全列表复用同一结果。 |
| `IsSignatureHelpEnabled` | `true` | 输入调用时显示已知方法签名及当前参数。 |
| `IsDiagnosticsEnabled` | `true` | 输入空闲 320 ms 后运行内置诊断，显示波浪线、悬停提示和问题数量。 |
| `IsSemanticHighlightingEnabled` | `true` | 根据当前文件中的已知符号绑定着色，独立于词法高亮和诊断显示开关。 |
| `AutoInsertPairs` | `true` | 自动配对括号、引号；跳过已自动插入的闭合符，空配对可一起删除。 |
| `CompletionProvider` | `null` | 可选 `ICodeCompletionProvider`；为空时使用内置补全。 |
| `Formatter` | `null` | 可选 `ICodeFormatter`；为空时使用内置保守格式化器。 |

语言配置：PlainText、CSharp、JavaScript、TypeScript、Json、Xml、Axaml、Html、
Css、Sql、Python、Yaml、Markdown、C、Cpp、Java、Go、Rust、Kotlin、Swift、Php、
Ruby、Bash、PowerShell、Dart、Lua、Toml、Ini。这里提供词法规则，不等同每种语言的完整语法解析器；
嵌入语言和编译器专用方言并未完整解析；格式化和语义补全可通过服务接口扩展。

`CaretIndex`、`SelectionStart`、`SelectionLength` 可写，均为 UTF-16 文档偏移。
提供 `Select(start, length)`、`SelectedText`、`ReplaceSelection(text)`、
`IndentSelection(outdent)`、`ScrollToLine(从 1 开始的行号)`、`FindNext(text)`，不暴露内部绘制器。
编辑操作遵守只读设置。

只读观测属性包括 `LineCount`、`CaretLine`、`CaretColumn`、`LanguageName`、
`HighlightingError`、`IsFormatting`、`FormattingError`、`CompletionError`。
遇到未定义枚举值时回退为纯文本并给出 `HighlightingError`，不改写原文。

提供 `CopyCommand`、`CopyAllCommand`、`CutCommand`、`PasteCommand`、
`SelectAllCommand`、`UndoCommand`、`RedoCommand`、`FormatCommand`、`CompleteCommand`、`NextDiagnosticCommand`、`GoToDiagnosticCommand`，
只读时修改命令不可执行。内置复制按钮自动通过 Avalonia 的
`TopLevel.Clipboard` 复制全文，只读时也可使用，无需额外处理 `Click`；
复制保留选区和光标，空文本时按钮禁用。`FocusEditor()` 聚焦编辑器的 Avalonia 输入表面；
`CopyAllAsync()` 返回平台是否接受复制请求，无剪贴板服务时返回 false。

保留平台惯用的复制、全选、撤销重做快捷键。Ctrl/Cmd+F 查找，Esc 关闭；
Tab 补全当前标识符或上下文，也可跳过跟踪的自动闭合符；空行/选区仍用于缩进，Shift+Tab 反缩进。
Ctrl+Space 请求补全，↑/↓ 选择，Enter 接受，普通方法/属性仍由单次 Tab 接受，Esc 取消；
Alt+Shift+F 格式化；Ctrl+Tab/Ctrl+Shift+Tab 向前/向后移出代码区。
只读是用户编辑策略，不能作为业务授权边界。
Ctrl/Cmd+Shift+Space 请求参数提示；提示显示时 Ctrl/Cmd+↑/↓ 切换重载。
F8/Shift+F8 定位下一个/上一个问题；状态栏使用 10px 错误图标和错误行号的小链接按钮，点击定位显示的行。
`ErrorCount` 保留错误总数，`DiagnosticSummary` 保留原摘要契约；新增 `DiagnosticLine` 表示显示的行号（无问题时为 0），`GoToDiagnosticCommand` 定位此行。悬停波浪线显示诊断编号和说明。
错误链接在图标和行号后显示说明，空间不足时仅在视觉上省略正文；Tooltip 换行显示完整编号、行列和说明。
`DiagnosticMessage`、`DiagnosticDetail` 分别提供未截断的正文与完整详情，便于消费者绑定和无障碍读取。
底栏最小高度 28 DIP、文本行高 18 DIP；行列标签与数字使用同一文本布局，并与错误正文竖向居中对齐。

Light / Dark 由 Avalonia 主题作用域控制，`CodeEditor.Density` 提供 Small / Normal /
Large，默认字号 13 / 14 / 16，仍允许显式覆盖字体。边框、文本、焦点和选择复用
本包的 `CodeEditor.Surface`、`CodeEditor.Text`、`CodeEditor.InputLine`、`CodeEditor.Accent`、`CodeEditor.TextSelection`。
新增 `CodeEditor.*` 资源包括 Keyword、String、Comment、Number、Type、
Punctuation、GutterForeground 和 CurrentLineBackground；消费者覆盖资源后已有编辑器同步更新。

错误标记和选区根据塑形后的范围矩形绘制，双向文字的多个片段分别处理，部分 UTF-16 范围扩展到完整字素。
软换行使用排他的结束偏移；EOF 插入标记只归最后一个可视行，避免边界重复标记。
调整只作用于绘制几何，不改变 UTF-16 文档偏移、选中文字、复制内容或撤销事务。
悬停命中复用同一组可视片段，覆盖扩展后的字素，不再依赖边界处的光标偏移推断。

语义着色是共享后台分析输出的独立、不可变 UTF-16 区间层，沿用 320 ms 空闲延迟。
依据可见绑定和遮蔽关系识别类型、方法、字段、属性、参数和局部变量；外部未知符号保持词法颜色。
可覆盖 `CodeEditor.SemanticType`、`CodeEditor.SemanticMethod`、`CodeEditor.SemanticField`、
`CodeEditor.SemanticProperty`、`CodeEditor.SemanticParameter`，局部变量沿用 `Foreground`。
`IsSyntaxHighlightingEnabled` 管词法颜色，`IsSemanticHighlightingEnabled` 管语义层；关闭诊断仍可保留语义颜色。
编辑立即清除过期区间，仅对应修订和语言的快照可发布；颜色更新复用已有塑形，不重建文档行。
该层仍限于当前文件的保守绑定，不提供工程级编译器语义 token，也不猜测未知成员。
没有符号作用域支持的语言，不会单纯为了语义颜色启动后台分析。

词法高亮借鉴 TextMate 的作用域、嵌套上下文和嵌入语言设计，增加转义、正则、标签、属性、函数、
预处理指令、标题、代码围栏与插值边界着色。支持 JavaScript/TypeScript 模板字符串、C# 插值字符串、
Python f-string 的嵌套上下文，以及 Markdown 围栏内的语言着色。修改分隔符或围栏语言时，向后更新
直到词法与视觉状态均稳定；上下文最多嵌套 16 层。视觉作用域不改变诊断/补全 token，符号颜色仍由
独立语义层覆盖。这里是内置扫描器，不是完整的 TextMate/Oniguruma 实现，不导入外部 `.tmLanguage`
语法或 TextMate 主题。

新增画刷均使用 `CodeEditor.` 前缀：`Escape`、`Regex`、`Tag`、`Attribute`、`Function`、
`Preprocessor`、`Heading`、`Fence`、`Interpolation`；消费者覆盖后已有编辑器同步更新。

编辑器自行维护行索引、增量词法状态、差量撤销记录、可视行绘制及有上限的字形缓存。
每行、每个 token 都不创建 Control。字体整形、字体回退、剪贴板、输入法组合文本和周边文本协议使用 Avalonia API。
软换行按等宽显示列计算；建议使用等宽字体，保证 Tab 与软换行对齐。

内置补全包含语言关键字、文档标识符、片段及后台建立的当前文档符号索引。
`public ` 等声明修饰符后自动提示类型和声明关键字；精确匹配排在其他前缀匹配之前。
C# 内置 for/foreach/反向循环、属性和方法片段。第一次 Tab 确认内置片段快捷词，第二次 Tab 展开为一次撤销事务，无双击计时限制。
Enter、指针或 `AcceptCompletion` 直接展开。展开后 Tab/Shift+Tab 切换返回类型、名称、参数和函数体等字段；循环变量和后备字段的多处引用同步修改。
Esc、离开编辑器、外部替换文本、切换语言或只读状态结束片段会话。
列表单行显示种类、匹配高亮和简述，完整详情放在 Tooltip 中。
C# 常用目录补充集合/LINQ、StringBuilder、DateTime、文件/路径和异常成员，使用静态目录，无程序集反射加载。
支持声明类型、字段/属性、方法、参数、常见赋值和构造器的类型推断，包含 JavaScript 对象字面量和 Python `self` 属性。
C#、JavaScript/TypeScript、Python、C/C++、Go、Rust、Java、Kotlin、Swift、Dart、PHP、Ruby、Lua、Bash、PowerShell、SQL 的常用标准函数和成员由内置目录提供。
HTML/XML/AXAML 提供上下文标签与属性，CSS 提供属性和值；JSON/YAML/TOML/INI 提供字面量、文档标识符和配对符号，Markdown/纯文本提供文档标识符。
输入 `.`、`::`、`->` 或 Lua 的 `:` 请求对应成员；方法条目显示签名，接受后插入括号并将光标放入参数区，已知返回类型支持链式成员补全。
支持 Go 结构字段/接收者方法、Rust impl 方法、可选成员访问；Shell 命令和 PowerShell cmdlet 不插入无效的调用括号。
无法识别的外部类型不会填入猜测成员。
补全仅显示与输入前缀匹配的条目（忽略大小写），不再混入子串或首字母匹配结果；成员访问只显示对应接收者的已知成员，声明位置限制为类型和关键字。
编程语言的文档词汇排除注释、字符串和已知的其他作用域局部变量；自定义补全提供器也遵循相同的前缀筛选。
C# 保留泛型参数/返回类型、显式泛型方法实参、数组/集合索引结果和同文件基类成员；C#（`name:`）、Python（`name=`）支持命名参数补全与对应参数高亮。
常用 C# 构造函数有内置签名，泛型元素类型可通过已知方法/属性返回值传递。补全筛选和排序在后台运行。

C# 片段快捷词包括 `method`/`func`、`async`、`ctor`、`prop`、`propg`、`propfull`、`for`、`foreach`、`forr`、`if`、`while`、`try`、`switch`、`class`。
函数片段还覆盖 JavaScript/TypeScript（`function`、`func`、`afn`）、Python（`def`、`func`）、C/C++/Java/Dart/Go/Swift/Bash（`func`）、Rust（`fn`）、Kotlin（`fun`）、PHP/Lua/PowerShell（`function`）、Ruby（`def`）。
字段偏移随缩进设置及文档换行符调整；PreviewHost 的“函数片段 · Tab Tab”按钮可直接试用。
大文档会先显示增量词汇索引的结果，再异步补充同一列表的语义条目；继续输入或接受条目会取消过期更新。
每个编辑器最多同时运行一个完整语义快照。超过 100,000 个 UTF-16 码元的文档，编辑后立即发起的请求共享 80 ms 空闲窗口，过期的排队任务在解析前取消。
原有 320 ms 诊断定时器已超过该窗口，不额外等待；缓存命中及增量词汇首批补全仍即时返回。
语义快照复用符号索引，不重建用不到的词汇索引；补全先按前缀筛选，再保留候选，只创建排名前 100 的符号条目。
快照分行、词法扫描、符号索引与补全筛选均检查取消，不强制垃圾回收或修改应用全局 GC 设置。
编辑操作在分配新的公开 Text 快照之前取消过期分析和补全，避免后台分配与前台整篇文本复制同时争用 GC。
可调用 `RequestCompletionAsync()`、`AcceptCompletion(item)`；接受补全为一次可撤销编辑。
项目专用或 LSP 补全可实现 `ICodeCompletionProvider.GetCompletionsAsync(context, cancellationToken)`。
条目包含 Label、InsertText、Detail、Kind、相对插入文本的 CaretOffset（-1 表示末尾）。
`RequestSignatureHelpAsync()` 将重载、命名参数、默认值、当前参数和调用起始偏移发布至 `SignatureHelp`。
嵌套表达式、字符串中的逗号不影响外层参数计数；Python 提示省略 `self`/`cls`。

`RefreshDiagnosticsAsync()` 更新 `Diagnostics`、`ErrorCount`、`DiagnosticSummary`。
每条 `CodeDiagnostic` 提供 UTF-16 范围、从 1 开始的行列、严重级别、稳定编号和说明。
`GoToNextDiagnostic(backwards)` 选中并滚动到问题。内置检查按语言处理括号不匹配（提示预期闭合符）、未闭合字面量/注释、缺少名称/表达式/条件，以及常见数值字面量错误。
JSON/XML/AXAML 使用解析器诊断；HTML 检查标签及 script 中的 JavaScript/JSON、style 中的 CSS。
Python 检查块、缩进和条件误用赋值；YAML 检查缩进、无效退格层级和重复键。
Ruby/Lua/Bash 检查块结束符和必要关键字，PowerShell 检查函数及控制头部。
CSS 检查声明名称、冒号和值；SQL 检查明确缺失的 SELECT/FROM/WHERE 操作数和 CASE/END 配对。
TOML/INI 检查节头、赋值和重复键，TOML 另检查裸值、表重定义、字符串和流式容器配对。
Markdown 对已识别语言的代码围栏执行诊断并映射回原行号；纯文本和普通文章内容不臆造语法错误。
额外检查 C# 不完整修饰符/成员声明、空调用参数、明确缺少语句结束符、基础字面量参数类型不匹配，以及完整本地类型中的未知成员。
继承及外部成员不参与未知成员检查；已知调用检查参数数量、可选及可变参数，Python 解包参数保守处理。
JavaScript/Lua 允许省略或多传参数，不据此误报。调用错误给出已知的预期签名。
C# 另检查方法缺少返回类型（`CSDECL003`）、参数缺少类型或名称（`CSPARAM002`）、基础类型返回方法的空函数体（`CSRETURN003`）。
构造函数保留独立规则，修饰符和特性不再混入返回类型；已知基础类型的算术表达式参与赋值、参数和返回值检查。
匿名函数的 return 不套用外层方法的返回类型。调用提示优先选择参数类型兼容的重载，已识别的参数声明内部不显示调用提示。
`Header` 只负责显示，诊断语言由 `CodeType` 决定，即使标题包含不同的文件扩展名。
补全、参数和诊断共用不可变快照的后台分析，编辑后旧结果不再发布。
这是轻量的单文件分析；工程导入、外部继承、外部程序集、完整类型检查及各语言完整语法不在覆盖范围内，不能替代编译器。

共享作用域索引覆盖 C#、JavaScript/TypeScript、Python、C/C++、Java、Go、Rust、Kotlin、Swift、PHP、Ruby、Dart 和 Lua。
简单表达式中的变量读取在当前文件找不到可见声明时报告 `*NAME001`；动态语言使用警告，因为宿主可能提供全局变量。
静态或带注解的基础类型赋值支持已知变量和调用结果（`*TYPE003`）；已知调用增加命名参数与基础类型实参匹配检查（`CALL001`）。
显式 return 检查缺少或多余返回值（`*RETURN001`）、已知返回类型不兼容（`*RETURN002`），并检查已支持的 C#/Kotlin 表达式函数体和带返回注解的 TypeScript 箭头函数。
Python 参数与返回值注解不匹配使用警告，JavaScript/Lua 保留可少传或多传参数的规则。
补全排除后声明的局部变量和已结束代码块，优先解析较近的同名绑定；相同前缀匹配级别下，已知赋值、实参或返回值类型兼容的候选靠前，行内预览复用该排序。
JavaScript/TypeScript 的 var 保留函数作用域，Python 函数可引用稍后声明的模块变量。
外部未知类型、联合类型、复杂运算、lambda/推导式局部作用域、全路径返回覆盖和各语言完整重载/转换规则不作推断。
Shell 和声明式语言沿用各自检查，不套用静态变量规则。

`FormatDocumentAsync(cancellationToken)` 在后台处理内置格式化，成功后作为一次撤销事务应用。
新编辑、语言/只读变化及取消会使旧结果失效。JSON/XML 输入无效时保留原文，并设置 `FormattingError`。
内置格式化分级如下：

| 语言 | 内置行为 |
| --- | --- |
| JSON | 解析后按指定缩进展开对象、数组。 |
| XML / AXAML | 元素缩进，保留混合文本及 `xml:space` 数据。 |
| C#、C、C++、Java、CSS、Dart、JavaScript、TypeScript | 块结构换行、缩进、赋值及逗号的安全空格整理；保留原有换行和字面量，JavaScript 斜杠语法有歧义时仅保守缩进。 |
| Go、Rust、Kotlin、Swift、PHP、PowerShell | 按 token 缩进和整理空格，保留已有换行以保护分号插入语义。 |
| SQL | 关键字大写、子句换行，保留引号内文本。 |
| Python | 统一块/续行缩进，整理赋值、逗号、注解和字典间距，保护多行字符串内容及切片。 |
| YAML | 统一映射/序列缩进、流式集合间距，保护引号、URL、注释和块标量内容；缩进始终使用空格。 |
| HTML | 按元素结构缩进，识别 void 标签及带引号属性，保留混合文本和 script/style/pre/textarea 内容。 |
| Ruby、Lua、TOML、INI | 保守整理空格/尾空白，保留有语义的缩进和多行字符串。 |
| Bash | 保守整理尾空白。 |
| 纯文本、Markdown | 原样保留，避免改变作为内容的空白。 |

这些内置规则不替代各编译器的完整规范格式化器。
Python/YAML/HTML 已有内置实现。项目专用规范可通过 `ICodeFormatter.FormatAsync(context, cancellationToken)` 扩展。
服务接收不可变文档快照并应响应取消；编辑器不会将过期结果覆盖到新文档。

演示页包含 10000 行样例。专项测量输入、全选、大跨度滚动、补全和缓存上限。
`Text` 绑定仍在每次编辑时发布完整字符串；消费者绑定处理过重会增加延迟。
具体耗时取决于硬件、行长度、字体及绘制后端，不能仅凭行数保证所有文档和机器的帧率。

PreviewHost 导航包含 CodeEditor，`--codeeditor` 可直接打开桌面演示。
`--verify-codeeditor [output]` 只测试该控件的绑定、输入、语言、格式化、补全、10000 行性能、布局和像素结果。
原生消费者使用 `--require-aot --verify-codeeditor [output]`。
产物使用 `.preview/codeeditor/` 隔离目录。
`--verify-codeeditor-functional [output]` 可单独运行功能/视觉检查；
`--verify-codeeditor-performance [output]` 在新进程运行同一套 10000 行基准。
性能测量应在构建完成后串行进行，并记录 IDE 后台索引等外部负载；独立入口保持原有耗时阈值。
`--verify-codeeditor-highlighting [output]` 单独检查增量作用域与高亮像素。


新增检查未完成的类型/命名空间体、重复函数参数、已支持静态/词法作用域中的重复声明、缺少 const 初始值，以及显式基础类型与字面量的不兼容。
Python 注解不匹配按警告处理，动态语言允许重新赋入其他类型；保留 C/C++ 前向声明、C# 分号类体、Rust 单元/元组结构和外部模块、Kotlin 无类体声明、TypeScript 环境声明等合法写法。

启用自动缩进时，在单行大括号块中按 Enter，会把对应闭合括号移到独立行并缩进后续内容；C 系语言的语句片段同时复用已有格式化。
XML/AXAML/HTML 的对应结束标签及 Ruby/Lua/Bash 结束关键字同样处理；Python/YAML 块头增加缩进，HTML 空元素不会增加缩进。
空白缩进行输入闭合括号时会对齐已知开符号。以上均为一次撤销操作，仅处理当前行（闭合符向前查找有上限），保护字符串和注释。

正常输入会在行尾显示半透明后续代码，Tab 直接接受函数、属性或标识符；上下键切换候选时同步预览。
预览不进入文档、撤销记录或剪贴板，不增加布局范围；选区、只读、IME 组合输入、过期请求及 Esc 会取消预览，右侧已有代码时不覆盖绘制。
内置片段保留双 Tab：第一次确认快捷词，第二次展开可编辑字段；展开文本延续快捷词前缀时，可显示首行预览。
