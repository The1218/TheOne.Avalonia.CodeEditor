# 独立发布准备

本仓库只管理 `TheOne.Avalonia.CodeEditor`，版本不与兄弟项目联动。远程仓库地址、作者资料和实际发布版本由用户指定。

1. 检查工作树，只纳入本包源码、资源、预览项目、必要构建配置、公共文档与许可证；Agent / Skill / 提示词和本地验证记录不纳入 Git，也不要提交 .preview、bin、obj 或凭证。
2. 在 PackageVersions.props 设置用户指定版本，或 pack 时显式传入 -p:PackageVersion=<版本>。
3. 按 DEVELOPMENT.md 构建、运行受影响专项，并在全新缓存中验证本地 nupkg 消费。涉及绑定、元数据或原生资产时运行真实 NativeAOT。
4. 检查 nuspec、包内 README / LICENSE / THIRD-PARTY-NOTICES 和平台资源。仓库 URL 确定后再设置相应 RepositoryUrl，避免占位地址进入发布包。
5. 用户授权后配置 Git remote、提交并推送；NuGet API Key 通过安全凭证或 CI secret 提供，不写入文件、日志或提示词。
6. 只有明确授权发布时才执行 dotnet nuget push；不把 pack 成功或本地消费成功写成已发布。

本次迁移仅 git init 和本地 pack，未提交、设置远程、推送或发布。既有版本号保留为 1.0.0-preview.2，发布前由用户决定是否使用新的独立版本号。
