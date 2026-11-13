# 资源清单同步 CheckManifestUpdate

日期：2026-11-13。接续 [资源地址集合与非微软登录开关](资源地址集合与非微软登录开关-20261113.md)。

## 文件

- `OneLauncher.Core/Global/Updater/JsonModel.cs`：按仓库根目录 `resource_manifest.json` 建立的模型，包括 `ResourceManifest`、`ManifestResources`、`ManifestFunction`，以及源生成上下文 `ResourceManifestJsonContext`。全部为 `internal`。
- `OneLauncher.Core/Global/Updater/AutoUpdate.cs`：实现 `CheckManifestUpdate()`。

模型字段与 JSON 键同名。`__TEXT__` 没有对应属性，反序列化时跳过。`Announcement` 的结构尚未确定，暂按 `JsonElement?` 接收：以后改成对象也不会导致旧版本解析失败。当前未使用该字段。

## 流程

1. 通过 `Init.Download.HttpStreamGetter(Manifest, ...)` 取得网络流，交给 `JsonSerializer.DeserializeAsync` 和源生成上下文解析。
2. 过滤：
   - `LatestLauncherVersion` 无法解析时，视为无效清单，记录 Warning 后忽略整份清单。
   - `LatestLauncherVersion` 低于 `Init.ApplicationVersoin` 时，忽略整份清单。
   - 资源列表先去掉空白项；结果为 null、缺失或空列表时，不覆盖配置。
3. 写入 `Init.ConfigManager.Data.DefaultSettings`，然后调用 `Save()`：

| 清单 | 配置 | 转换 |
| --- | --- | --- |
| `Resources.WindowsUpdater` | `Resources.Updater` | 直接覆盖 |
| `Resources.ConnectCore` | `Resources.ConnectCore` | 直接覆盖 |
| `Resources.SkinBodyRender` | `Resources.SkinGetterWithUUID` | 配置的约定是“地址后直接拼接 UUID”，因此只接受以 `${UUID}` 结尾的模板，去掉占位符后作为前缀；其他形式的模板被丢弃 |
| `Function.AllowOfflineLogin` / `AllowYggdrasilLogin` | `AllowNonMicrosoftLogin` | 任一为 `false` 时设为 `false`；否则任一为 `true` 时设为 `true`；都为 null 时忽略 |
| `RecommendedMods` | `RecommandedMods` | 去除空白项与重复项后覆盖 |
| `JavaVersions` | `AvailableJavaVersions` | 只保留正整数并去重，保持清单中的顺序 |

### 推荐模组与 Java 版本（2026-11-13 补充）

- `RecommendedMods` 是 Modrinth 项目 ID 列表，内容与原内置列表相同。JSON 不支持注释，ID 与模组名称的对应关系请查阅 `DBManager.JsonModels.cs` 中内置列表的注释，或 `推荐模组字符串清单-20261112.md`。`RecommendedModInstaller` 在每次安装时读取配置，因此下次安装就会使用新列表。
- `DefaultConfig.AvailableJavaVersions` 是新增配置项，默认值 `[8, 11, 16, 17, 21, 24, 25]` 原先写死在 `JavaInstallPaneViewModel` 中。面板改为在创建时从配置读取；如果默认选中的 21 不在列表里，就改选第一项。各 Java 提供商的最低版本筛选（`MinimumVersion`）不变。如果清单给出的版本某个提供商不支持，会在安装时由该提供商报错。

任何异常（网络、解析、保存）都只记录为 `OlanExceptionAction.Warning`，现有配置保持不变，与 `CheckUpdate` 的处理方式一致。

## 尚未处理

- 没有调用方。需要在合适的位置（例如首页现有的 `CheckUpdate` 后台任务处）调用。
- 目前只有一个清单地址，`Revision` 已建模但不参与判断。增加多源时，取 `Revision` 最大的清单再执行上述流程。
- 仓库清单拆分了两个登入开关，配置里只有一个。以后若要分别控制离线和外置登入，需要在配置和 UI 检测中拆成两个开关。

## 验证

当前环境没有 .NET SDK，未编译、未运行。
