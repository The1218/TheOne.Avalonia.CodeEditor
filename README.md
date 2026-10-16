# TheOne.Avalonia.CodeEditor

独立的 .NET 10 / Avalonia 12.1.4 代码编辑器，不依赖 TheOne.Avalonia 主控件库。

## 安装与主题

```sh
dotnet add package TheOne.Avalonia.CodeEditor
```

在 App.axaml 中显式注册主题：

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:one="using:TheOne.Avalonia.Themes">
  <Application.Styles>
    <one:CodeEditorTheme />
  </Application.Styles>
</Application>
```

主题包含本包色板、控件样式和 Fluent 原生控件基础样式，无需 OneTheme。与其他主题组合时，先注册本包主题，再注册宿主主题。

## 功能与使用

- 自有虚拟化编辑内核，支持原生文本输入、IME、剪贴板和撤销重做。
- 语法着色、查找、补全、行内建议、代码片段、参数提示、格式化和错误诊断。
- 行内建议可通过 IsInlineCompletionEnabled 关闭；普通补全使用 Tab，代码片段保留 Tab-Tab 展开。
- 单文件语义检查覆盖已知作用域内的未定义引用、基础类型赋值、函数参数及返回值；补全按可见绑定、遮蔽和已知预期类型排序，无需语言服务器。
- C# 检查方法返回类型与参数声明缺失，参数提示优先匹配兼容重载；标题不改变 CodeType 指定的诊断语言。
- 文本范围按真实布局片段绘制，支持双向文字、字素和软换行边界；独立语义着色层可通过 IsSemanticHighlightingEnabled 关闭。
- 自有增量高亮借鉴 TextMate 作用域，支持嵌套插值、Markdown 围栏内语言及细分颜色；无需 TextMate 依赖，不导入外部语法文件。
- 密度由 CodeEditor.Density 设置，使用 CodeEditorDensity.Small / Normal / Large；主题资源前缀为 CodeEditor.*。
- 公共控件命名空间为 TheOne.Avalonia.Controls，仅保留 API 命名，不代表引用主控件库。

详细用法见 [中文文档](docs/USAGE.zh-cn.md) / [English guide](docs/USAGE.en.md)。

## 预览与构建

在仓库根目录执行：

```sh
dotnet run --project design/PreviewHost/PreviewHost.csproj
dotnet build design/PreviewHost/PreviewHost.csproj --artifacts-path .preview/work/artifacts
```

专项验证和本地 NuGet 消费见 [开发说明](docs/DEVELOPMENT.md)，发布流程见 [发布准备](docs/PUBLISHING.md)。

## 仓库结构

- src/：控件源码、主题、资源和 NuGet 项目。
- design/PreviewHost/：独立桌面预览及控件专项验证。
- docs/：使用、开发和发布文档。
- PackageVersions.props：本包独立版本配置。

版本控制仅保留源码、预览项目、必要构建配置、公共文档与许可证。本地 Agent / Skill / 提示词、验证记录、缓存和构建产物由 .gitignore 排除，保留在本机使用。
