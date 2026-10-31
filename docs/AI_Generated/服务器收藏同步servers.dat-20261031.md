# 服务器收藏同步到 Minecraft 服务器列表

日期：2026-10-31。

## 入口与文件归属

- 后端：`OneLauncher.Core.Helper.Models.ServerEntry.SaveInServerDAT()`，同步方法。
- 前端：`OneLauncher.Views.Panes.PaneViewModels.AddServerPaneViewModel.Save()`。
- 目标：`Init.GameRootPath/instance/{InstanceId}/servers.dat`，属于收藏关联的游戏实例。
- 使用 `fNbt 2.1.0` 读取和写入 Minecraft Java 版 NBT。新文件不压缩；已有文件保留其压缩格式。

`ServerEntry` 沿用工作区已有的文件移动，命名空间调整为 `Helper.Models`，同步更新编辑收藏工厂的引用。启动器配置继续使用既有 JSON 源生成上下文，JSON 字段不变。

## 保存与去重规则

后端读取完整 NBT 根节点和 `servers` 列表，在增加任何条目前逐项比较 `ip` 字段。比较时去掉端口、前后空格及域名末尾的点，并忽略大小写。IPv6 地址支持方括号形式和完整地址形式，使用 `IPAddress` 规范化等价写法；不会按第一个冒号截断 IPv6。

只要同一主机地址已存在，方法立即返回，不写入 dat，也不改变文件时间戳。旧条目的名称、端口和 `hidden` 值不影响去重，也不会被更新。例如已有 `mc.example.net:25565`，添加 `MC.EXAMPLE.NET:25566` 不会修改游戏列表。不同实例独立去重，不通过 DNS 将不同域名或域名与 IP 合并。

未命中时追加一个 Compound，包含收藏名称 `name`、带端口的 `ip` 和 `hidden=0`。IPv6 的端口使用 `[地址]:端口`。已有条目的图标、其他字段、顺序以及根节点中的未知字段均保留。文件或 `servers` 列表不存在时创建；合法空列表的元素类型为 TAG_End 时也可追加。

## 写入和错误处理

读取、检查和保存受进程内锁保护，避免同一启动器内并发添加导致重复或覆盖。先将完整内容写入同目录的唯一临时文件，再替换目标文件。发生序列化或读写错误时保留原文件，清理临时文件，并通过 `OlanException` 返回路径及原因；损坏 NBT 或错误的列表类型不会被当成空列表覆盖。

前端先保存启动器收藏，再使用 `Task.Run(serverEntry.SaveInServerDAT)` 同步游戏列表，随后运行既有服务器状态获取及列表更新回调。dat 同步失败由 `OlanExceptionWorker` 展示，已保存的收藏保留。同地址不同端口的收藏可以分别保存到启动器，游戏列表仍按后端规则去重。

进程内锁只协调本启动器的写入，不协调同时运行的 Minecraft 或其他编辑工具。该变更仅接入新增收藏；编辑及删除收藏没有新增游戏列表同步行为。

## 扩展位置

后续调用方直接复用 `SaveInServerDAT`，无须在 ViewModel 重复读取 NBT 或实现去重。若未来需要同步编辑、删除或图标，应在后端定义相应操作及匹配规则后接入前端，避免改变当前“已有同地址时完全不改写”的契约。

## 验证

```powershell
dotnet build ConsoleTest/ConsoleTest.csproj --nologo --verbosity quiet -clp:ErrorsOnly
dotnet ConsoleTest/bin/Debug/net10.0/ConsoleTest.dll --server-dat
dotnet build OneLauncher.Core/OneLauncher.Core.csproj -r win-x64 -c Release -p:EnableAotAnalyzer=true --nologo --verbosity quiet -clp:ErrorsOnly
```

指定测试项目 ConsoleTest 新增 `--server-dat` 离线模式，无须初始化真实启动器配置，不访问服务器网络。检查创建、追加、不同端口去重、不带端口的旧地址、域名大小写、IPv4/IPv6、中文及 emoji 名称、已有字段保留、实例隔离、空列表、压缩格式、损坏/锁定文件、序列化失败清理、并发添加，以及实际前端 SaveCommand 的收藏保存和 dat 去重。测试夹具生成在仓库内并自动删除。

上述构建和回归检查均已通过。Core Release 构建启用 AOT 分析，新增 NBT 路径不使用反射或动态代码；项目仍有既有编译警告。另将直接引用实际 Core 的最小程序以 win-x64 NativeAOT 发布并运行，确认原生程序可以创建并重读 NBT、保留中文及 emoji 名称，并跳过不同端口的同地址记录。该临时程序及发布产物在验证后清理。
