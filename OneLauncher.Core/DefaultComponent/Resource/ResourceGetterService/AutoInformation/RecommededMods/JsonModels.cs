using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;

// 旧配置的对象列表只保留 ModId，后续统一保存为字符串列表。
public sealed class RecommendedModListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("推荐模组必须为数组。");
        List<string> mods = [];
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return mods;
            if (reader.TokenType == JsonTokenType.String)
            {
                mods.Add(reader.GetString()!);
                continue;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("推荐模组必须为 ID 字符串。");
            using var legacy = JsonDocument.ParseValue(ref reader);
            if (!legacy.RootElement.TryGetProperty("ModId", out var id) || id.ValueKind != JsonValueKind.String)
                throw new JsonException("旧推荐模组缺少 ModId。");
            var modId = id.GetString()!;
            if (!mods.Contains(modId, StringComparer.Ordinal))
                mods.Add(modId);
        }
        throw new JsonException("推荐模组数组不完整。");
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var mod in value)
            writer.WriteStringValue(mod);
        writer.WriteEndArray();
    }
}
