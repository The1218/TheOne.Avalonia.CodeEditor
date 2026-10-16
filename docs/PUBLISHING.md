# 独立发布

仓库：https://github.com/The1218/TheOne.Avalonia.CodeEditor

本仓库只管理 `TheOne.Avalonia.CodeEditor`。作者为许可证中的 `The1218`；版本由 `PackageVersions.props` 独立管理，当前为 `1.0.0-preview.2`。

1. 检查工作树，只提交本包源码、资源、预览项目、必要构建配置、公共文档与许可证。Agent / Skill / 提示词、本地验证记录、`.preview`、bin、obj、缓存和凭证不纳入 Git。
2. 删除已被替代的构建、NuGet 缓存、发布副本和一次性探针；保留有效回归、报告与定位长尾所需的失败采样。构建使用 `.preview/<发布任务>/` 和 `--artifacts-path` 隔离。
3. 核对远程分支后正常合并、提交和推送，不用强推覆盖远程历史。版本不可覆盖；发布前检查 NuGet 上是否已存在同版本。
4. 按 DEVELOPMENT.md 运行受影响专项。涉及依赖、绑定、元数据或原生资产时，验证本地 nupkg 消费和真实 NativeAOT；检查 nuspec、仓库信息、作者、README / LICENSE / THIRD-PARTY-NOTICES 与主题资源。
5. 发布包从已验证的 Release 产物使用 `--no-build` 打包，核对 DLL 哈希。包验证使用新的自有包缓存，避免同版本旧包干扰。
6. 获得用户发布授权后，可使用 NuGet.org 浏览器 Upload 页面选择本地 `.nupkg`，核对 ID、版本、作者、依赖及发布说明后提交。不要把 API Key 写入源码、日志或文档。
7. 上传不等于成功发布；以 NuGet 上传结果和包页面的验证状态为准，区分已接收、处理中和可安装状态。仅在确认发布结果后报告完成。

当前预览的已知限制：后台语义诊断仅限当前文件，不能替代编译器；输入/滚动的 GC 与调度长尾仍在排查。性能专项继续保持原阈值，不通过删除有效断言或放宽门槛宣称达标。
