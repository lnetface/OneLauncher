# SkyCrystal 下载源接入

## 用意
接入 SkyCrystal API（https://api.skycrystal.fun）作为 Minecraft 原版文件的加速下载源，在设置页“下载源”中以“SkyCrystal源竞速”出现。

## 逻辑
- 新增 `SkyCrystalSourceDownloadUrlGetter`，实现 `IDownloadSourceUrlProvider`，URL 映射如下：
  - 客户端本体：`https://api.skycrystal.fun/mc/java/client/{版本ID}`
  - 资源文件：`https://resources.download.minecraft.net/` → `https://api.skycrystal.fun/mc/java/assets/`
  - 库文件：`https://libraries.minecraft.net/` → `https://api.skycrystal.fun/mc/java/library/`
- `DownloadSourceStrategy` 末尾新增 `RaceWithSkyCrystal`（放在末尾以保持已保存设置的枚举值不变）。
- `DownloadMinecraft` 中该策略与官方源同时竞速，沿用 `RaceDownloader` 的“先到先得、失败回退”机制；SHA1 校验流程不变。
- 设置页 ViewModel 的可选列表与 `DownloadSourceStrategyToStringConverter` 增加对应项。

## 拓展性
- SkyCrystal 还提供 Modrinth（`/modrinth/cdn/{path}`、`/modrinth/{rest}`）、CurseForge（`/curseforge/...`）以及通用 `/proxy?url=` 端点。目前资源检索直接访问 `api.modrinth.com/v2`，而 SkyCrystal 文档标注其 API 代理对应上游 `v1`，版本差异未验证，因此暂未接入；后续可在资源服务层抽象出 Host 提供器后再接入。
- 模组加载器（Forge/NeoForge/Fabric）文件目前不经过 `IDownloadSourceUrlProvider`，如需加速需在对应加载器提供器中处理。
