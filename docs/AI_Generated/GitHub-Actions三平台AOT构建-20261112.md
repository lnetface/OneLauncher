# GitHub Actions 三平台 AOT 构建（2026-11-12）

## 目的与文件

修改 `.github/workflows/dotnet-desktop.yml`，替换旧桌面模板，复用当前三个平台的 AOT 发布配置。旧配置虽然列出 Linux，但 runs-on 固定为 Windows，也未使用平台发布配置。

本次新增此说明文档；不修改应用代码、发布配置或现有发布脚本。

## 触发与运行过程

推送到 master、提交或更新目标为 master 的 PR、在 Actions 页面手动运行时触发。GitHub 在独立的托管运行器上检出代码，根据 global.json 安装 .NET SDK，再进行发布、打包和上传。矩阵产生三个独立任务，平台失败不会取消其他平台；每个任务最多运行 45 分钟。

| 平台 | 运行器 | 发布配置 | RID | 包格式 |
| --- | --- | --- | --- | --- |
| Windows | windows-latest | WindowsAot | win-x64 | zip |
| Linux | ubuntu-22.04 | LinuxAot | linux-x64 | tar.gz |
| macOS | macos-15（ARM64） | MacOSAot | osx-arm64 | tar.gz |

Windows 调用 build/Publish-WindowsAot.ps1，复用其 LLVM 下载、校验和 PATH 设置。Linux 安装 NativeAOT 所需 clang、zlib1g-dev。Linux/macOS 直接使用 dotnet publish 加对应 PublishProfile，不依赖当前工作区新增但尚未提交的 Publish.sh。

发布过程中允许 dotnet publish 按同一发布配置自动还原依赖，避免先普通 restore 再使用 --no-restore 导致 AOT 工具链缺失。中间输出放在 build/output；发布目录沿用 pubxml；压缩包放在 build/releases。上传完整发布目录，Linux/macOS 使用 tar 保留可执行权限和原生库。

## 下载与权限

进入仓库 Actions，打开本次运行，在 Artifacts 下载对应平台产物。GitHub 的 artifact 下载可能在发布压缩包外再包一层 ZIP；解开外层后，Linux/macOS 继续解开 tar.gz。产物保留 14 天，可按需求调整 retention-days，但不能超过仓库策略上限。

工作流仅需要 contents: read，不需要 Secrets；不向 GitHub Releases 发布，也不进行程序签名、公证或生成 macOS app/dmg。macOS 包面向 Apple Silicon；Intel Mac 需另加匹配 x64 运行器和 osx-x64 RID 的矩阵项。

## 验证与扩展

本地静态核对矩阵、触发分支、发布配置、SDK 安装方式和上传路径。遵循项目小改动无需测试的约定，不在 Windows 本地模拟 Linux/macOS 原生发布；远端真实编译及运行结果需推送后查看 Actions。本次不提交或推送代码。

以后增加平台时扩展矩阵并匹配运行器架构、发布配置及 RID；更换目标框架时同步修改压缩步骤中的 net10.0 路径。JIT 可通过扩展模式字段及对应发布配置加入，包名称和输出路径也需包含模式以保持区分。
