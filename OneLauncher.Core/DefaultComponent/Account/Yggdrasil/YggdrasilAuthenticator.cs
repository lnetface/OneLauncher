using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Account.Yggdrasil;

namespace OneLauncher.Core.DefaultComponent.Account.Yggdrasil;

/// <summary>
/// 为 Yggdrasil 认证服务器提供认证逻辑的内部抽象基类。
/// </summary>
public abstract class YggdrasilAuthenticator
{
    private readonly HttpClient _httpClient;
    protected abstract string AuthApiRoot { get; }

    internal YggdrasilAuthenticator()
    {
        _httpClient = new();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OneLauncher/" + Init.ApplicationVersoin);
    }
    private async Task<UserModel> ProcessAuthResponse(
        HttpResponseMessage authResponse,
        HttpResponseMessage metaResponse,
        string authUrl, Guid userId)
    {
        if (!authResponse.IsSuccessStatusCode)
        {
            var error = await authResponse.Content.ReadFromJsonAsync<ErrorResponse>(YggdrasilJsonContext.Default.ErrorResponse);
            throw new OlanException("认证失败", ($"检查 Yggdrasil 认证响应状态；请求URL authUrl={authUrl}；请求URL={authResponse.RequestMessage?.RequestUri}；HTTP状态={(int)authResponse.StatusCode}。" + (error?.ErrorMessage ?? "服务器返回了未知的错误。")), OlanExceptionAction.Error);
        }
        if (!metaResponse.IsSuccessStatusCode)
        {
            throw new OlanException("认证失败", $"检查 Yggdrasil 元数据响应状态；请求URL authUrl={authUrl}；请求URL={metaResponse.RequestMessage?.RequestUri}；HTTP状态={(int)metaResponse.StatusCode}。无法获取认证服务器的元数据。", OlanExceptionAction.Error);
        }

        var authData = await authResponse.Content.ReadFromJsonAsync<AuthResponse>(YggdrasilJsonContext.Default.AuthResponse);
        if (authData?.SelectedProfile == null)
            throw new OlanException("认证失败", $"解析 Yggdrasil selectedProfile 玩家档案；请求URL authUrl={authUrl}；请求URL={authResponse.RequestMessage?.RequestUri}；HTTP状态={(int)authResponse.StatusCode}。服务器未返回有效的玩家档案。", OlanExceptionAction.Error);

        return new UserModel(
            UserID: userId,
            name: authData.SelectedProfile.Name,
            uuid: Guid.Parse(authData.SelectedProfile.Id),
            accessToken: authData.AccessToken,
            yggdrasilInfo:
            new YggdrasilInfo(
                Guid.Parse(authData.ClientToken),
                authUrl,
                TextHelper.Base64Encode(await metaResponse.Content.ReadAsStringAsync()))
        );
    }
    /// <summary>
    /// 使用用户名和密码进行认证。
    /// </summary>
    public async Task<UserModel> AuthenticateUseUserNameAndPasswordAsync(string username, string password)
    {
        var endpoint = $"{AuthApiRoot}/authserver/authenticate";

        var payload = new AuthRequest
        {
            Username = username,
            Password = password,
            ClientToken = Guid.NewGuid().ToString()
        };

        try
        {
            var authResponse = await _httpClient.PostAsJsonAsync(endpoint, payload, YggdrasilJsonContext.Default.AuthRequest);
            var metaResponse = await _httpClient.GetAsync(AuthApiRoot);
            return await ProcessAuthResponse(authResponse,metaResponse, AuthApiRoot, Guid.NewGuid());
        }
        catch (Exception ex) when (ex is not OlanException)
        {
            throw new OlanException("网络错误", $"请求 Yggdrasil authenticate 并读取元数据；认证URL={endpoint}；元数据URL={AuthApiRoot}。原因：{ex.Message}", OlanExceptionAction.Error, ex);
        }
    }

    public async Task<UserModel> RefreshAccessTokenAsync(UserModel userToRefresh)
    {
        if (userToRefresh.YggdrasilInfo == null)
            throw new OlanException("认证错误", $"刷新 Yggdrasil 会话前检查账户认证信息；账户ID={userToRefresh.UserID}；账户配置文件={Init.BasePath}/playerdata/account.json。用户信息中缺少 Yggdrasil 信息，无法刷新会话。", OlanExceptionAction.Error);

        var yggdrasilInfo = userToRefresh.YggdrasilInfo.Value;
        var endpoint = $"{yggdrasilInfo.AuthUrl}/authserver/refresh";

        var payload = new RefreshRequest
        {
            AccessToken = userToRefresh.AccessToken,
            ClientToken = yggdrasilInfo.ClientToken.ToString()
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, payload, YggdrasilJsonContext.Default.RefreshRequest);
            return await ProcessAuthResponse(response, await _httpClient.GetAsync(AuthApiRoot),yggdrasilInfo.AuthUrl, userToRefresh.UserID);
        }
        catch (Exception ex) when (ex is not OlanException)
        {
            throw new OlanException("网络错误", $"请求 Yggdrasil refresh 并解析响应；请求URL={endpoint}；元数据URL={yggdrasilInfo.AuthUrl}。原因：{ex.Message}", OlanExceptionAction.Error, ex);
        }
    }
}