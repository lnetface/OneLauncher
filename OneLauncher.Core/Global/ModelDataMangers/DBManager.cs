using System.Text.Json;

namespace OneLauncher.Core.Global.ModelDataMangers;

public class DBManager : BasicDataManager<AppConfig>
{
    public DBManager(string configPath)
        :base(configPath)
    {
    }
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppConfig> CreateJsonTypeInfo(JsonSerializerOptions options)
        => new OneLauncherAppConfigsJsonContext(options).AppConfig;
}
