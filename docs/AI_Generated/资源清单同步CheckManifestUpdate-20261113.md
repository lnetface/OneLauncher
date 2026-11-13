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

任何异常（网络、解析、保存）都只记录为 `OlanExceptionAction.Warning`，现有配置保持不变，与 `CheckUpdate` 的处理方式一致。

## 尚未处理

- 没有调用方。需要在合适的位置（例如首页现有的 `CheckUpdate` 后台任务处）调用。
- 目前只有一个清单地址，`Revision` 已建模但不参与判断。增加多源时，取 `Revision` 最大的清单再执行上述流程。
- 仓库清单拆分了两个登入开关，配置里只有一个。以后若要分别控制离线和外置登入，需要在配置和 UI 检测中拆成两个开关。

## 验证

当前环境没有 .NET SDK，未编译、未运行。
