# 构建与实验目录

`Generate-ThemeIcons.ps1` 生成主题图标；`Publish-WindowsAot.ps1` 执行 Windows 发布。发布入口仍位于 Desktop 的 PublishProfiles，构建中间产物归入 `build/output`，本地工具链归入 `build/toolchains`，原有发布包归入 `build/releases`。发布配置指定的最终 PublishDir 保持原样。

Agent 测试源码位于 `ConsoleTest/agent_test`，新实验输出使用 `build/<任务名称>-<yyyyMMdd>`，这些日期目录由 Git 忽略。清理前先停止测试、游戏和构建进程。

```powershell
pwsh -File build/clean.ps1 -WhatIf
pwsh -File build/clean.ps1 -Task migration-check-20261112
pwsh -File build/clean.ps1
```

默认清理代码目录中的 bin、obj、.avalonia-build-tasks，以及日期任务目录和已知旧实验输出目录。`-Task` 仅清理指定的 build 直属任务目录；不接受路径或通配符。build 根目录内的脚本、props、targets 和文档保留。发布包和工具链需显式使用 `-IncludeReleases`、`-IncludeToolchains`。遇到符号链接或 junction 会中止，避免跨出工作区。

增加不带日期的输出目录时，需要同步维护 clean.ps1 的目录列表与 .gitignore；推荐直接遵循任务名加日期的约定。
