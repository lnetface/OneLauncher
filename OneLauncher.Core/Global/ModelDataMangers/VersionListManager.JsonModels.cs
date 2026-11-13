using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OneLauncher.Core.Global.ModelDataMangers;

public sealed class VersionListData
{
    [JsonRequired]
    public List<UserVersion> VersionList { get; set; } = new();
}

[JsonSerializable(typeof(VersionListData))]
[JsonSerializable(typeof(UserVersion))]
[JsonSerializable(typeof(ModType))]
public partial class VersionListJsonContext : JsonSerializerContext { }
