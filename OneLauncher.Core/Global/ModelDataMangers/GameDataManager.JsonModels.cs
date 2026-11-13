using OneLauncher.Core.Helper.Models;
using System.Collections.Generic;
using System.Data;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Sources;

namespace OneLauncher.Core.Global.ModelDataMangers;
// 标签系统太过度设计了，未来把他简化一下
public class GameDataTag
{
    public string Name { get; init; }
    public Guid ID { get; init; }
}
public class GameDataRoot : IJsonOnDeserialized
{
    /// <summary>
    /// 存储所有游戏数据实例的字典。
    /// Key: InstanceId (string)
    /// Value: GameData 对象
    /// </summary>
    [JsonPropertyName("instances")]
    public Dictionary<string, GameData> Instances { get; set; } = new();

    /// <summary>
    /// 存储每个游戏版本的默认实例映射。
    /// Key: VersionId (e.g., "1.20.1")
    /// Value: InstanceId
    /// </summary>
    [JsonPropertyName("defaults")]
    public Dictionary<string, string> DefaultInstanceMap { get; set; } = new();

    /// <summary>
    /// 存储所有已定义的标签列表。
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<Guid,GameDataTag> Tags { get; set; } = new();
    /// <summary>
    /// 实例ID对于标签ID的映射。
    /// </summary>
    [JsonPropertyName("tagMap")]
    public Dictionary<string,Guid> TagMap { get; set; } = new();

    public void OnDeserialized()
    {
        // 修复 Instances 中的 ID
        foreach (var kvp in Instances)
        {
            if (kvp.Value != null)
            {
                kvp.Value.InstanceId = kvp.Key;
            }
        }

        // 如果 Tags 也有同样问题（Tag对象里也有ID），可以在这里一并处理
        //foreach (var kvp in Tags)
        //{
        //    if (kvp.Value != null)
        //    {
        //        // 假设 GameDataTag 也有 [JsonIgnore] public Guid ID {get;init;}
        //        // 注意：init 属性在构造后无法修改，可能需要改为 set 或通过反射/构造函数处理
        //        // 这里仅作逻辑演示
        //    }
        //}
    }
}
// 以便AOT编译
[JsonSerializable(typeof(List<GameData>))]
[JsonSerializable(typeof(GameDataRoot))]
[JsonSerializable(typeof(GameDataTag))]
[JsonSerializable(typeof(GameData))]
[JsonSerializable(typeof(JvmArguments))]
[JsonSerializable(typeof(string))]
public partial class GameDataJsonContext : JsonSerializerContext { }
