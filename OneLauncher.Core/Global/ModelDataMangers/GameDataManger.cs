using OneLauncher.Core.Helper.Models;
using System.Text.Json;

namespace OneLauncher.Core.Global.ModelDataMangers;

public class GameDataManager : BasicDataManager<GameDataRoot>
{
    public List<GameData> AllGameData => Data.Instances.Values.ToList();
    /// <summary>
    /// 获取或创建一个指定版本的游戏数据实例。
    /// 查找逻辑：1. 默认实例 -> 2. 第一个可用实例 -> 3. 创建新实例。
    /// </summary>
    public async Task<GameData> GetOrCreateInstanceAsync(UserVersion userVersion)
    {
        // 尝试获取该版本的默认实例
        var gameData = GetDefaultInstance(userVersion.VersionID);
        if (gameData != null)
            return gameData;
        

        // 2. 如果没有默认实例，则查找该版本的第一个可用实例
        gameData = Data.Instances.FirstOrDefault(x => x.Value.VersionId == userVersion.VersionID).Value;
        if (gameData != null)
        {
            await SetDefaultInstanceAsync(gameData.InstanceId);
            return gameData;
        }

        // 如果完全没有任何实例，则创建一个新的
        // 确定默认游戏数据名称
        string modLoaderName = userVersion.modType.ToModEnum() switch
        {
            ModEnum.fabric => "Fabric",
            ModEnum.neoforge => "NeoForge",
            ModEnum.forge => "Forge",
            _ => "原版"
        };
        string gameDataName = $"{userVersion.VersionID} - {modLoaderName}";
        var newGameData = new GameData(
            name: gameDataName,
            versionId: userVersion.VersionID,
            loader: userVersion.modType.ToModEnum(),
            userModel: UnitedCore.Core.AccountManager.GetDefaultUser().UserID
        );

        // 添加并设为默认
        await AddGameDataAsync(newGameData);
        await SetDefaultInstanceAsync(newGameData.InstanceId);

        return newGameData;
    }
    public GameData? GetInstanceFromId(string instanceId)
    {
        if (Data.Instances.ContainsKey(instanceId))
        {
            return base.Data.Instances[instanceId];
        }
        else return null;
    }
    public GameDataManager(string configPath)
        :base(configPath)
    {
    }
    public IEnumerable<GameData> GetInstancesFromTag(Guid tagId)
    {
        var result = new List<GameData>();
        // 1. 遍历 TagMap 寻找匹配的实例ID
        foreach (var tagMapEntry in Data.TagMap)
        {
            // 2. 如果标签ID匹配
            if (tagMapEntry.Value == tagId)
            {
                string instanceId = tagMapEntry.Key;

                // 3. 使用实例ID从 Instances 字典中获取 GameData 对象
                if (Data.Instances.TryGetValue(instanceId, out GameData gameData))
                {
                    // 4. 将找到的实例添加到结果列表中
                    result.Add(gameData);
                }
            }
        }

        return result;
    }
    public Task CreateTag(GameDataTag tag, Guid tagId)
    {
        Data.Tags.Add(tagId, tag);
        return Save();
    }
    public Task SetTagForInstance(string instanceId, Guid tagId)
    {
        Data.TagMap[instanceId] = tagId;
        return Save();
    }
    /// <summary>
    /// 移除一个实例的标签引用。
    /// </summary>
    public Task RemoveTagFromInstanceAsync(string instanceId)
    {
        if (Data.TagMap.ContainsKey(instanceId))
        {
            Data.TagMap.Remove(instanceId);
        }
        return Save();
    }
    public Task RemoveTagAsync(Guid tagId)
    {
        if (Data.Tags.ContainsKey(tagId))
        {
            // 找到所有引用此标签的实例ID
            var instancesWithTag = Data.TagMap
                .Where(kvp => kvp.Value == tagId)
                .Select(kvp => kvp.Key)
                .ToList(); // 同样需要 ToList 来避免在遍历时修改集合

            // 移除所有引用
            foreach (var instanceId in instancesWithTag)
            {
                Data.TagMap.Remove(instanceId);
            }

            // 最后移除标签本身
            Data.Tags.Remove(tagId);
        }
        return Save();
    }
    public Task SetDefaultInstanceAsync(string targetId)
    {
        Data.DefaultInstanceMap[Data.Instances[targetId].VersionId] = targetId;
        return Save();
    }
    /// <param name="versionId">如果填写，则获取该版本的默认实例；若不填，则获得最高的那个版本的默认实例。</param>
    /// <returns></returns>
    public GameData? GetDefaultInstance(string? versionId = null)
    {
        if (versionId != null)
        {
            var instanceId = Data.DefaultInstanceMap.GetValueOrDefault(versionId);
            return instanceId == null ? null : Data.Instances.GetValueOrDefault(instanceId);
        }

        // 空实例、尚未指定默认实例及已失效的映射都应返回 null。
        // 自定义版本 ID 可能不是 System.Version，仍允许作为默认实例使用。
        return Data.DefaultInstanceMap
            .Where(entry => Data.Instances.ContainsKey(entry.Value))
            .OrderByDescending(entry => Version.TryParse(entry.Key, out var version) ? version : null)
            .Select(entry => Data.Instances[entry.Value])
            .FirstOrDefault();
    }
    public Task AddGameDataAsync(GameData newData)
    {
        Data.Instances.Add(newData.InstanceId,newData);
        // 确保物理文件夹被创建
        Directory.CreateDirectory(newData.InstancePath);
        return Save();
    }
    public Task RemoveGameDataAsync(string dataToRemove)
    {
        // 在删除实例前检查并清理它在 defaults 映射中的记录
        if (Data.DefaultInstanceMap.ContainsValue(dataToRemove))
        {
            var entry = Data.DefaultInstanceMap.FirstOrDefault(kvp => kvp.Value == dataToRemove);
            if (!string.IsNullOrEmpty(entry.Key))
                Data.DefaultInstanceMap.Remove(entry.Key);
        }
        Data.Instances.Remove(dataToRemove);
        return Save();
    }

    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<GameDataRoot> CreateJsonTypeInfo(JsonSerializerOptions options)
        => new GameDataJsonContext(options).GameDataRoot;
}
