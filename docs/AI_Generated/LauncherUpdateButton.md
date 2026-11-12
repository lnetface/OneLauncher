# 设置页更新启动器按钮

设置页“关于”板块在版本号下方新增“更新启动器”按钮，复用 `item-action` 样式和主题刷新图标。

按钮通过编译绑定调用 `SettingsPageViewModel.UpdateLauncherCommand`。命令由 CommunityToolkit.Mvvm 的 `[RelayCommand]` 生成，异步执行期间自动禁止重复点击；通过 `Task.Run(AutoUpdate.Update)` 调用现有后端更新入口，避免更新准备工作阻塞界面。操作开始和返回时发送提示，抛出的异常交由设置页现有的 `ShowSettingsException` 处理。

后端 `AutoUpdate.Update` 负责下载与启动更新脚本，目前内部捕获异常且不返回更新结果，因此结束提示仅表示调用返回，不表示更新安装成功。此次未修改后端更新逻辑；未来可在后端提供结果和进度后扩展界面反馈。

改动文件：`OneLauncher/Views/Pages/settings.axaml`、`OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs`；新增本文档。使用编译绑定和源生成命令，适配现有 AOT 约定。按照项目对小型改动的约定，未运行测试或实际更新流程。
