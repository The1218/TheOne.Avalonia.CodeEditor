# 独立开发与验证

所有命令在本仓库根目录执行，不需要 TheOne.Avalonia 主库。

```sh
dotnet build design/PreviewHost/PreviewHost.csproj --artifacts-path .preview/work/artifacts
dotnet .preview/work/artifacts/bin/PreviewHost/debug/PreviewHost.dll --verify-codeeditor-functional .preview/work/frames
dotnet .preview/work/artifacts/bin/PreviewHost/debug/PreviewHost.dll --verify-codeeditor-semantics .preview/work/semantic-frames
dotnet .preview/work/artifacts/bin/PreviewHost/debug/PreviewHost.dll --verify-codeeditor-highlighting .preview/work/highlighting
dotnet pack src/TheOne.Avalonia.CodeEditor/TheOne.Avalonia.CodeEditor.csproj -c Release --artifacts-path .preview/work/pack-artifacts -o .preview/work/packages
dotnet build design/PreviewHost/PreviewHost.csproj -p:UsePackageReferences=true --source .preview/work/packages --source https://api.nuget.org/v3/index.json --artifacts-path .preview/work/consumer-artifacts
```

包消费验证使用全新 NUGET_PACKAGES 目录，防止命中同版本旧缓存；运行 consumer-artifacts 中的 PreviewHost 再执行同一专项。未带参数时打开桌面预览。

语义专项包含跨语言错误/合法代码对照、作用域与类型排序、真实文本/Tab 事件及万行后台分析。性能测量使用 Release，单独运行 `--verify-codeeditor-performance`，避免并行构建干扰帧时间。
高亮专项覆盖 TextMate 风格的作用域、嵌套/跨行插值、围栏语言切换、增量编辑与完整重算对照，以及 Light/Dark、三种密度、窄/宽视口的真实像素。已移除重复的旧兼容性入口；原生 Unicode 输入仍由功能专项覆盖。
后台语义快照不生成视觉作用域；功能专项检查该边界。普通性能专项额外检查 10000 行插值/双向文字/emoji 在软换行下的输入与滚动，保持 p95 < 16.7 ms，完整样本写入 `highlighting-samples.csv`。
历史 `.preview` 中的构建目录、NuGet 缓存和发布副本可以删除后重建；报告、失败采样和有效回归不因清理产物而移除。
声明回归包含 `public voidAdd(int a,int b)` 原例、修正与撤销、构造函数/特性/泛型/数组参数反例、重载选择及基础类型算术返回；传入输出目录时保存原例的 Light / Dark 诊断像素。

万行后台任务/GC 争用专项（仍只验证 CodeEditor）：

```sh
dotnet build design/PreviewHost/PreviewHost.csproj -c Release --artifacts-path .preview/work/artifacts
dotnet .preview/work/artifacts/bin/PreviewHost/release/PreviewHost.dll --verify-codeeditor-contention .preview/work/contention
```

场景包含服务关闭对照、冷分析期间滚动、快速编辑取消、分析运行后恢复输入、热语义补全及开启换行。输入/滚动 p95 保持 16.7 ms 阈值，普通性能专项的首次补全仍为 100 ms。
`frames.csv` 保留每帧耗时、前台/进程分配差值、各代 GC 次数、GC 暂停增量和绘制/塑形耗时；`work.csv` 保留分析各阶段及补全的起止时钟和线程分配量，可按时间区间关联。
进程分配计数是运行时近似值；GC 暂停表示测量窗口中的进程暂停，并非某个任务独占的成本。活动数量列为帧开始采样，完整重叠关系使用起止时钟判断。
详细采样仅由专项临时启用；默认没有采样监听器。不强制 GC、不修改运行时 GC 模式、不剔除超过阈值的正式样本；首次 5 个预热样本沿用既有统计约定，原始 CSV 仍完整保留。
语义专项还在 16 个阶段入口验证取消（含语义着色），保证取消后不再分配下一批数据，并验证取消补全不会遍历候选。
独立渲染专项检查双向范围、组合字符、ZWJ、软换行/EOF、真实标记像素、符号绑定及复用塑形：
还使用真实指针事件检查悬停命中，并将同色着色字形与原生 TextLayout 作逐像素位置对照。

```sh
dotnet .preview/work/artifacts/bin/PreviewHost/release/PreviewHost.dll --verify-codeeditor-rendering .preview/work/rendering
```

关闭服务的争用对照同时关闭 `IsSemanticHighlightingEnabled`；其他争用场景保留语义层，原有阈值不变。
普通性能专项另测 10000 行混合双向文字、组合字符、emoji，在实际软换行、语义着色及 200 个范围标记同时启用时的滚动；仍要求 p95 < 16.7 ms。
`input-samples.csv` 保存普通输入的逐样本耗时、塑形/绘制和 GC 暂停/分配，`mixed-range-samples.csv` 保存新增负载的全部样本。

重复内置补全请求仍执行后台语义计算；上下文与结果相同时复用弹层列表，避免反复创建相同的可视行。功能专项检查结果列表复用、实际计算次数及取消后关闭弹层；自定义补全提供者仍按请求调用。

真实 macOS arm64 NativeAOT：使用同一 Xcode 的 xcrun clang 和 SDK，publish PreviewHost，运行产物并附加 --require-aot。Windows / Linux 需在相应平台运行；不把跨 RID 产物检查写成平台运行通过。

远程仓库为 https://github.com/The1218/TheOne.Avalonia.CodeEditor ，包版本由 PackageVersions.props 独立管理。提交、推送和 NuGet 发布需要用户授权；本地验证成功不等于已发布。发布流程见 PUBLISHING.md。
