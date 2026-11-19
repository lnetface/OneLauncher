# CurseForge 资源获取接入与 Modrinth 现代化重构-20261119

## 1. 目的

- 在 `OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService` 下新增 `CurseForge` 文件夹，按 Modrinth 的结构提供 **搜索**，以及 **模组、光影包、整合包** 的获取接口。
- 让 Modrinth 代码设计现代化，并把命名空间统一为与目录一致的 `OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth`。
- 本次只改后端接口。前端 ViewModel 只更新了命名空间和调用方式，**还没有加入“选择 CurseForge 来源”的界面**（见第 6 节）。

## 2. 目录与命名空间

```
ResourceGetterService/
├─ ResourceType.cs            ResourceType（Mod/ShaderPack/Modpack）、ResourceSource（Modrinth/CurseForge）
├─ Modrinth/
│  ├─ ModrinthApiClient.cs    HTTP 与源生成反序列化访问层
│  ├─ ModrinthJsonModels.cs   协议模型 + ModrinthJsonContext
│  ├─ SearchModrinth.cs       搜索
│  ├─ GetModModrinth.cs       模组（替代 GetModrinth）
│  ├─ GetShaderPackModrinth.cs
│  └─ GetModpackModrinth.cs
└─ CurseForge/
   ├─ CurseForgeApiClient.cs  x-api-key 鉴权、GET/POST、源生成反序列化
   ├─ CurseForgeJsonModels.cs 协议模型、常量、枚举 + CurseForgeJsonContext
   ├─ CurseForgeMapping.cs    ModEnum/ResourceType 映射、选文件、加载器 ID 转换
   ├─ SearchCurseForge.cs
   ├─ GetModCurseForge.cs
   ├─ GetShaderPackCurseForge.cs
   └─ GetModpackCurseForge.cs
```

已删除：`Modrinth/GetModrinth.cs`、`Modrinth/JsonModelGet.cs`、`Modrinth/jsonModelSearch.cs`。

命名空间变化：

| 旧 | 新 |
| --- | --- |
| `...Resource.ModService.Modrinth`（SearchModrinth、GetModrinth、ResourceType） | `...Resource.ResourceGetterService.Modrinth`；`ResourceType` 改到 `...Resource.ResourceGetterService` |
| `...DefaultComponent.Mod.ModService.Modrinth`（JSON 模型） | `...Resource.ResourceGetterService.Modrinth` |

## 3. Modrinth 现代化要点

| 旧设计 | 新设计 |
| --- | --- |
| `GetModrinth` 是有状态对象：构造后调用 `Init()`，再分别读取 `GetDownloadInfos()` / `GetDependenciesInfos()`，可能返回 `null` 列表 | 静态 `GetModModrinth.GetAsync(...)` 一次返回 `List<NdDowItem>`（第一项为模组本体，其后为依赖）；空列表表示“没有兼容版本或项目不存在” |
| 依赖逐个串行请求，并且**下载所有类型的依赖**（包括 optional） | 只下载一级 `required` 依赖，并行解析；作者指定了 `version_id` 时用指定版本 |
| 解析主版本失败时吞掉异常并返回 null | 只有 404 映射为 null；JSON 错误和其他 HTTP 错误正常抛出 |
| URL 手工拼接 `["{version}"]`，没有编码 | `ModrinthApiClient.BuildProjectVersionsUrl` 先把值序列化成 JSON 数组再做 URL 编码 |
| `SearchModrinth` 实例只为一次调用而创建，还持有可变的 `info` | 静态 `SearchModrinth.SearchAsync(keyword, type, offset, limit, ct)`，支持分页 |
| 模型属性非可空却不赋初值，`size` 为 int | 可空标注与 `[]` 初值写清楚；`size` 改为 long，转换成 `NdDowItem` 时再截断 |
| 文件名校验只用于光影包 | 模组、光影包、整合包都经过统一下载类的 `Download.CreateSafeItem` |

调用方已同步：`FabricProvider`、`QuiltProvider`（Fabric API / QSL 获取失败时跳过，和旧行为一致）、`RecommendedModInstaller`（单个推荐项目失败只跳过该项目）、`ResourceInstallerService`、`ModsBrowserViewModel`、`InstallModPaneViewModel`、回归测试中 `ModpackUiChecks` 与 `ShaderUiChecks` 的 using 和调用。

## 4. CurseForge 实现

### 4.1 API Key

CurseForge Core API 要求每个请求带 `x-api-key`。密钥**只从 `Init.CurseforgeApiKey` 读取**，不读取环境变量或 `config.json`。

`Init.CurseforgeApiKey` 为 `null` 时抛出 `OlanException("无法访问 CurseForge", ..., Warning)`；遇到 401/403 时也抛出同样标题的异常。密钥只附加在单个请求上，不修改共享 `HttpClient` 的默认请求头，也不写入诊断信息。

### 4.2 接口

| 能力 | 入口 | 说明 |
| --- | --- | --- |
| 搜索 | `SearchCurseForge.SearchAsync(keyword, type, gameVersion, loader, index, limit, sortField, ct)` | `GET /v1/mods/search`，gameId=432；classId：模组 6、光影 6552、整合包 4471。只有模组按加载器筛选 |
| 模组 | `GetModCurseForge.GetAsync(...)` | `GET /v1/mods/{id}/files?gameVersion&modLoaderType`，选择发布时间最新的可用文件（排除服务端包），并解析一级 `RequiredDependency` |
| 光影包 | `GetShaderPackCurseForge.GetDownloadInfoAsync(...)` | 只按 Minecraft 版本筛选，不下载依赖 |
| 整合包 | `GetModpackCurseForge.GetAsync(...)` | 见 4.3 |

项目 ID 是整数字符串，入口统一用 `CurseForgeMapping.ParseProjectId` 校验。

### 4.3 整合包：转换为 .mrpack

现有的预览侧栏、`ModpackDownload`、`ModpackImporter` 都基于 `MrpackParser`。为了不改动导入流程，`GetModpackCurseForge` 的处理步骤是：

1. 下载最新整合包 zip 到临时文件，并按配置校验 SHA1。
2. 读取 `manifest.json`，用 `POST /v1/mods/files` 和 `POST /v1/mods` 批量查询文件信息与项目类别（类别决定放入 `mods`、`resourcepacks` 还是 `shaderpacks`）。
3. 生成等价的 `modrinth.index.json`，把加载器 ID 转成 .mrpack 依赖键，例如 `forge-47.2.0` 转成 `forge: 47.2.0`，`fabric-x` 转成 `fabric-loader`。
4. 把原包覆盖目录（`manifest.overrides`，默认 `overrides`）复制到新 .mrpack 的 `overrides/`。
5. 返回 `ModpackDownload`，后续的 `ImportModpack` 与 Modrinth 完全相同。原 zip 在 finally 中删除。

清单中只要有文件因为作者关闭了第三方分发而没有 `downloadUrl`，或者 CurseForge 没有返回某个文件，就**整体失败**并列出这些文件，不静默安装不完整的实例。`required=false` 的文件不安装。

### 4.4 服务入口

`ResourceInstallerService` 新增可选参数 `ResourceSource source = ResourceSource.Modrinth`：

- `GetModpackInfoAsync(projectId, source, ct)`：原来的双参数重载仍然保留。
- `InstallMod(..., source, ct)`、`InstallShaderPack(..., source, ct)`：现有调用都使用命名参数，不受影响。

## 5. 去除多余实体的约定

- 模组获取直接返回 `List<NdDowItem>`，不另设结果类型；重复路径由 `Download.DownloadListAsync` 统一去重。
- 远端地址（仅限 http/https）与文件名（必须是单个文件名，防止写出目标目录）的安全校验统一放在 `Download.CreateSafeItem`（`DefaultComponent/Downloader/DownloadTool.cs`），校验失败返回 `null`，由调用方给出业务错误。
- 模组与光影包直接写入目标目录，由下载器的 `.downloading` 临时文件机制保证原子发布，不需要额外的临时路径。
- 只有整合包需要随机读取压缩包，因此"下载到临时文件并校验 SHA1"的代码直接写在 `GetModpackModrinth` 与 `GetModpackCurseForge` 中。

## 6. AOT

- 两个平台各用一个 `internal` 源生成上下文（`ModrinthJsonContext`、`CurseForgeJsonContext`），列出所有根类型，包括 `List<ModrinthVersion>` 和 POST 请求体类型。
- 计算属性（`IsRequired`、`Sha1`）加了 `[JsonIgnore]`。
- 没有使用反射或动态代码。

## 7. 扩展与后续工作

- **前端**：`ModsBrowserViewModel` 目前仍只调用 Modrinth。接入时需要增加来源下拉框，按来源调用 `SearchCurseForge`，并把 `CurseForgeMod` 映射到 `ResourceItem`（建议增加 `Source` 属性）。支持版本可用 `GetSupportedGameVersions()`，加载器可用 `GetSupportedModLoaders()`。安装侧栏把 `Source` 传给 `ResourceInstallerService`。设置页还需要提供 API Key 的输入入口。
- **新增平台**：在 `ResourceSource` 中添加枚举值，新建同级文件夹，实现 `Search*` 和 `Get*`，返回 `List<NdDowItem>` / `NdDowItem` / `ModpackDownload`，再在 `ResourceInstallerService` 的 switch 中分派。

## 8. 验证

- 当前环境没有 .NET SDK，并且网络访问受限（docs.curseforge.com、api.curseforge.com 都被拦截），**没有进行编译或运行测试**。
- 已静态检查：旧类型名和旧命名空间在 `*.cs` 中没有残留引用；现有回归测试依赖的请求路径（`/v2/search`、`/v2/project/{id}/version`）、查询参数（`game_versions=`、`loaders=`）和异常类型（`OlanException` / `JsonException` / `HttpRequestException`）保持不变。
- CurseForge 的端点、classId 和枚举值依据官方 REST 文档及社区 SDK 整理，需要用真实 API Key 联调确认。建议执行：

```powershell
dotnet build OneLauncher.Core/OneLauncher.Core.csproj --nologo
dotnet build OneLauncher/OneLauncher.csproj --nologo
```
