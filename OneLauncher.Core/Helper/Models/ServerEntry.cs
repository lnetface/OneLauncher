using fNbt;
using OneLauncher.Core.Global;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.Helper.Models;

public class ServerEntry(Guid id, string instanceId, ServerInfo serverInfo, string name, string? description)
{
    private static readonly object ServerDatSaveLock = new();
    private static readonly byte[] PngSignature =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    public Guid Id { get; init; } = id;
    public string InstanceId { get; init; } = instanceId;
    public ServerInfo ServerInfo { get; set; } = serverInfo;
    
    public string Name { get; set; } = name;
    public string? Description { get; set; } = description;
    [JsonIgnore]
    public string IconFileUrl => Path.Combine(Init.BasePath, "customdata", $"{Id}.png");
    [JsonIgnore]
    public string ReadableAddress => Uri.EscapeDataString($"{ServerInfo.Ip}:{ServerInfo.Port}");

    // 以下是预留属性
    [JsonIgnore]
    public uint? PlayersMax;
    [JsonIgnore]
    public uint? Ping
    {
        get
        {
            try
            {
                using Ping pingSender = new();
                PingReply reply = pingSender.Send(ServerInfo.Ip, 120);
                var time = reply.RoundtripTime;
                return reply.Status == IPStatus.Success
                    ? (uint)Math.Min(time, uint.MaxValue)
                    : null;
            }
            catch (PingException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 将收藏写入所属游戏实例的 servers.dat；同一主机（忽略端口）已存在时不改写文件。
    /// 保留已有条目和未知 NBT 字段，文件不存在时创建服务器列表。
    /// </summary>
    public void SaveInServerDAT()
    {
        string host = GetServerHost(ServerInfo.Ip);
        if (host.Length == 0 || ServerInfo.Port == 0)
            throw new OlanException("保存游戏服务器列表失败", "服务器地址或端口无效");

        string instancePath = Path.Combine(Init.GameRootPath, "instance", InstanceId);
        string path = Path.Combine(instancePath, "servers.dat");
        lock (ServerDatSaveLock)
        {
            string? temporaryPath = null;
            try
            {
                var file = new NbtFile(NbtFlavor.Java);
                var compression = NbtCompression.None;
                if (File.Exists(path))
                {
                    file.LoadFromFile(path);
                    compression = file.FileCompression;
                }

                NbtList servers;
                if (file.RootTag["servers"] == null)
                {
                    servers = new NbtList("servers", NbtTagType.Compound);
                    file.RootTag.Add(servers);
                }
                else if (file.RootTag["servers"] is NbtList list &&
                         (list.ListType == NbtTagType.Compound ||
                          list.Count == 0 && list.ListType is NbtTagType.End or NbtTagType.Unknown))
                {
                    servers = list;
                }
                else
                {
                    throw new InvalidDataException("servers 必须是 Compound 类型的列表。");
                }

                foreach (NbtCompound server in servers)
                {
                    if (server["ip"] is NbtString address &&
                        string.Equals(GetServerHost(address.Value), host, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                string addressWithPort = host.Contains(':')
                    ? $"[{host}]:{ServerInfo.Port}"
                    : $"{host}:{ServerInfo.Port}";
                servers.Add(new NbtCompound
                {
                    new NbtString("name", Name),
                    new NbtString("ip", addressWithPort),
                    new NbtByte("hidden", 0)
                });

                Directory.CreateDirectory(instancePath);
                temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
                file.SaveToFile(temporaryPath, compression);
                File.Move(temporaryPath, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NbtFormatException)
            {
                throw new OlanException("保存游戏服务器列表失败", $"无法保存 {path}：{ex.Message}",
                    OlanExceptionAction.Error, ex);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Trace.TraceWarning($"无法清理临时服务器列表 {temporaryPath}：{ex.Message}");
                    }
                }
            }
        }
    }

    private static string GetServerHost(string? address)
    {
        string host = address?.Trim() ?? string.Empty;
        int closingBracket = host.IndexOf(']');
        if (host.StartsWith('[') && closingBracket > 0)
        {
            host = host[1..closingBracket];
        }
        else
        {
            // 仅移除单个冒号后的端口，避免截断未加方括号的 IPv6 地址。
            int colon = host.IndexOf(':');
            if (colon > 0 && colon == host.LastIndexOf(':') && ushort.TryParse(host[(colon + 1)..], out _))
                host = host[..colon];
        }

        host = host.Trim().TrimEnd('.');
        return IPAddress.TryParse(host, out var ip) ? ip.ToString() : host;
    }

    /// <summary>
    /// 通过API获取服务器信息，并更新ServerInfo
    /// </summary>
    public async Task GetAASServerInfo()
    {
        var task = UnitedCore.Core.TaskManager.CreateTask($"检查 {ReadableAddress} 服务器状态"); 
        #region 检查
        // 地址检查已经迁移到 ServerInfo构造函数中
        HttpResponseMessage response;
        try
        {
            response = await Init.Download.UnityClient.GetAsync(
                $"https://api.mcstatus.io/v2/status/java/{ReadableAddress}");
        }
        catch (HttpRequestException ex)
        {
            task.SetFailed( new OlanException("无法获取服务器信息", "连接服务器状态 API 失败", OlanExceptionAction.Error, ex));
            return;
        }
        #endregion

        using (response)
        {
            await using Stream body = await response.Content.ReadAsStreamAsync();
            if (!response.IsSuccessStatusCode)
            {
                task.SetFailed(new OlanException("无法获取服务器信息", "服务器请求失败"));
                return;
            }

            JsonNode node = (await JsonNode.ParseAsync(body))?.Root!;

            bool online;
            string? description;
            string? icon;
            uint? playersMax;
            try
            {
                online = node["online"]?.GetValue<bool>() ?? false;
                description = node["motd"]?["clean"]?.GetValue<string>();
                icon = node["icon"]?.GetValue<string>();
                playersMax = node["players"]?["max"]?.GetValue<uint>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                task.SetFailed(new OlanException("无法获取服务器信息", "服务器状态 API 返回了无效数据", OlanExceptionAction.Error, ex));
                return;
            }

            if (!online)
            {
                task.SetFailed(new OlanException(
                    "服务器离线",
                    $"无法连接到服务器 {ServerInfo.Ip}:{ServerInfo.Port}",
                    OlanExceptionAction.Warning));
            }

            PlayersMax = playersMax;

            // 先检查是否已经存在配置，讲究一个不变现有原则。
            if (icon != null && !File.Exists(IconFileUrl))
                await SaveServerIcon(icon);
            if (Description == null && description != null)
                Description = description;

            task.SetDone();
        }
    }

    private async Task SaveServerIcon(string iconDataUri)
    {
        const string prefix = "data:image/png;base64,";
        if (!iconDataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new OlanException("无法获取服务器图标", "服务器返回了不支持的图标格式",OlanExceptionAction.Warning);

        byte[] iconBytes;
        try
        {
            iconBytes = Convert.FromBase64String(iconDataUri[prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new OlanException(
                "无法获取服务器图标",
                "服务器返回了无效的 Base64 图标数据",
                OlanExceptionAction.Error,
                ex);
        }

        if (iconBytes.Length > 4 * 1024 * 1024 ||
            !iconBytes.AsSpan().StartsWith(PngSignature))
        {
            throw new OlanException("无法获取服务器图标", "服务器返回的 PNG 图标无效或文件过大");
        }

        string iconDirectory = Path.GetDirectoryName(IconFileUrl)!;
        Directory.CreateDirectory(iconDirectory);

        await File.WriteAllBytesAsync(IconFileUrl, iconBytes);

    }
}
