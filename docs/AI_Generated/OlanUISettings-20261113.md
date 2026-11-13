# UI 设置对象拆分

UI 设置统一存储在 `AppConfig.OlanSettings.OlanUISettings` 中，类型为 `OlanUISettings`，定义在 `OneLauncher.Core/Global/ModelDataMangers/DBManager.cs`。

迁移字段包括导航栏按钮间距、标识符显示、各导航页面可见性、主页样式及主页各板块可见性。字段默认值保持原有行为。下载、校验、安装路径、刷新时间和默认实例/服务器仍保留在 `AppSettings`。

主窗口、主页、设置页及账户/游戏数据/服务器列表统一通过嵌套对象访问 UI 设置，仍调用原 DBManager.Save 保存并使用原有通知刷新界面。已有回归代码同步更新访问路径。

旧配置兼容：AppSettings 通过 JsonExtensionData 收集旧版平铺字段，在 IJsonOnDeserialized 回调中迁移至 OlanUISettings，然后清空扩展字段。配置同时存在新旧字段时，以非 null 的新对象为准；新对象缺失或为 null 时读取旧字段；没有配置的字段保留默认值。再次保存只写入嵌套结构，不输出旧 UI 字段。未知旧字段继续忽略。

序列化使用 OneLauncherAppConfigsJsonContext 注册的 OlanUISettings 和 Dictionary<string, JsonElement> 元数据，迁移不依赖反射，支持 AOT。

未来新增 UI 偏好应添加到 OlanUISettings 并通过 OlanSettings.OlanUISettings 访问，无需扩展 AppSettings。若仍需兼容同名旧平铺字段，现有迁移逻辑会自动处理。

本次按项目要求不运行测试；完成引用路径及差异静态检查。