# Windows 安装与更新脚本

`build/install.ps1` 从 `lnetface/OneLauncher` 获取最近 30 条 Release，跳过草稿和预发布，使用第一个包含 `OneLauncher.Desktop.exe` 的 Release。

## 使用方式

```powershell
# 无参数：默认安装到用户目录，已有程序时选择打开或更新
powershell.exe -ExecutionPolicy Bypass -File .\build\install.ps1

# 直接更新默认用户目录中的程序
powershell.exe -ExecutionPolicy Bypass -File .\build\install.ps1 update

# 直接更新指定旧版 EXE，也支持传入已有安装目录
powershell.exe -ExecutionPolicy Bypass -File .\build\install.ps1 update "D:\OneLauncher\OneLauncher.Desktop.exe"
```

默认程序路径为 `%USERPROFILE%\OneLauncher\OneLauncher.Desktop.exe`。无参数且程序不存在时保留原来的三项安装菜单，可选择仅下载、安装 .NET 10 Desktop Runtime，或再安装 Temurin JDK 21。

`update` 模式不询问安装选项或是否启动，不安装依赖。交互菜单中的更新也只替换程序，完成后退出。

## 更新逻辑

通过目标 EXE 的文件名和完整路径匹配进程，强制结束并等待退出，避免终止其他目录中的同名程序。下载时用流读取并通过 `Write-Progress` 显示已下载大小和百分比。

下载先写入安装目录内唯一的 `.download` 临时文件，文件大小必须与 Release 资产大小一致且非空。替换前再次检查并结束目标进程，再覆盖旧 EXE。临时文件和网络资源在 finally 中清理，下载失败不会覆盖旧文件。大小校验不等同于签名或哈希验证。

将来变更发布仓库或资产名可修改脚本顶部的 `$repository`、`$assetName`。日志仍追加到 `%TEMP%\OneLauncherInstall.log`。

验证：已进行 PowerShell 语法解析检查。2026-11-12 在工作区内使用本地 HTTP 服务和模拟交互、Release API、进程操作完成 8 项行为测试：首次仅下载安装、已有程序打开、交互更新、默认路径直接更新与进程隔离、含空格的自定义 EXE 路径、安装目录参数、下载大小不符时保留旧文件并清理临时文件、非法菜单输入。全部通过，HTTP 流式读取和下载进度回调实际执行。未验证真实 GitHub 网络、依赖安装和实际进程终止。测试产物通过 `build/clean.ps1 -Task install-test-20261112` 清理。
