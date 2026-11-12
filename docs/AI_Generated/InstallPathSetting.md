# 游戏安装路径设置

## 用意与涉及文件

在设置页的“运行环境”板块中展示已有的 `OlanSettings.InstallPath` 配置，提供基本路径、运行路径和自定义三种选择。界面依次显示说明、当前生效路径、下拉框，以及自定义输入框和“选择路径”“应用”按钮。

- `OneLauncher.Core/Global/ModelDataMangers/DBManager.cs`：安装路径默认值与空值兼容。
- `OneLauncher.Core/Global/Init.cs`：通过 `InstalledPath` 属性解析启动时的路径。
- `OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs`：加载设置、切换模式、选择目录、校验与保存。
- `OneLauncher/Views/Pages/settings.axaml`：设置页布局和编译绑定。
- `OneLauncher/Converters/UiOptionConverter.cs`：三种模式的图标、名称和说明。

## 配置与路径解析

`AppSettings.InstallPath` 使用非空字符串属性，默认值为 `${OLAN_BASE_PATH}/installed`。属性的 setter 将 null、空字符串和纯空白统一补成默认值，`[AllowNull]` 表示允许接收旧配置中的 null，getter 始终返回非空路径。启动时保存配置，将旧值的补全结果写回 JSON。

占位符与 Java 路径一样复用 `Helper.Tools.ReplacePlaceholders`，没有新增占位符常量或独立安装路径转换方法。

| 下拉选项 | 保存到 JSON 的模板 | 解析结果 |
| --- | --- | --- |
| 基本路径 | `${OLAN_BASE_PATH}/installed` | `Init.BasePath` 下的 `installed` 目录 |
| 运行路径 | `${OLAN_APP_RUNNING_PATH}/installed` | `AppContext.BaseDirectory` 下的 `installed` 目录 |
| 自定义 | 用户输入的路径或带占位符的模板 | 展开占位符后规范化的绝对路径 |

`OLAN_BASE_PATH` 对应用户目录下的 `OneLauncher`，`OLAN_APP_RUNNING_PATH` 对应启动器所在目录，不使用进程当前工作目录。普通自定义路径保存为绝对路径；包含上述占位符时保留模板。

`InstalledPath` 的私有 setter 在启动时展开模板、转换目录分隔符并调用 `Path.GetFullPath`，随后固定本次运行的路径。`GameRootPath` 仍是其下的 `minecraft` 子目录。设置页保存配置时不重新赋值这些运行时路径，避免已有游戏数据管理器与其他服务在同一进程中使用不同目录。

## 交互与保存流程

1. 加载已有设置时使用加载保护，避免下拉框初始化触发保存。
2. 选择基本路径或运行路径立即保存对应模板；选择自定义只启用输入区域，不保存。
3. 非自定义模式下，输入框和两个按钮一同禁用。保存期间也禁止继续编辑和切换模式。
4. “选择路径”调用 Avalonia `StorageProvider.OpenFolderPickerAsync`，仅允许选择一个文件夹。取消选择不产生配置变更；选择本地文件夹后直接保存。平台不支持文件夹选择器时提示用户手动输入。
5. 手动编辑文本不写入配置，点击“应用”后才校验并保存；空输入通过现有错误对话框提示。
6. 保存前展开路径并调用 `Directory.CreateDirectory`，目标不存在时自动创建。没有实际配置变化时不重复显示致命错误。
7. 创建目录或保存失败时恢复原配置和模式，并交给现有 `OlanExceptionWorker` 处理。
8. 保存成功且配置发生变化后，提交 `OlanExceptionAction.FatalError`，标题为“安装路径已更改”，内容为“更改游戏安装路径不会复制原有的游戏文件与数据，请自行迁移；新路径将在重启启动器后生效。”现有致命错误处理会显示对话框，并在对话框关闭后退出启动器。

此功能只更改安装目录配置，不复制、移动或删除原目录的数据。

## 扩展方式

添加预设模式时，在 ViewModel 的模式列表、加载识别和选择保存分支中增加映射，并在 `UiOptionConverter` 中补充展示信息。增加占位符时，同步更新 `InstalledPath` setter 与保存前校验使用的映射，继续复用 `Tools.ReplacePlaceholders`。

视图使用 `x:CompileBindings="True"`，属性和命令由 CommunityToolkit.Mvvm 源生成器生成；配置沿用现有 System.Text.Json 源生成上下文，生产代码没有新增反射或动态类型发现。

## 验证

- 桌面项目 `dotnet build OneLauncher.Desktop/OneLauncher.Desktop.csproj --no-restore --nologo -v minimal -clp:ErrorsOnly` 编译成功，0 个错误。
- 工作区内的临时离线检查验证了旧 null 配置补全及持久化、两个占位符解析、三项下拉绑定、自定义模式启用、手动编辑不立即保存。
- 使用 Avalonia Headless + Skia 渲染真实设置页，检查浅色、深色和 698 像素宽窗口，路径控件没有溢出。临时程序、数据和截图通过 `build/clean.ps1 -Task install-path-ui-20261112` 清理。
- 未实际打开系统原生文件夹对话框，也未执行保存成功后退出整个应用的流程；这两部分沿用已编译通过的 Avalonia API 与项目既有致命错误处理。
