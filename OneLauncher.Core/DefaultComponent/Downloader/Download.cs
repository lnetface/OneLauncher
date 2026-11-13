namespace OneLauncher.Core.DefaultComponent.Downloader;

internal partial class Download : IDisposable
{
    public Download(HttpClient? tc = null)
    {
        UnityClient = tc ?? new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 32 
        })
        {
            Timeout = TimeSpan.FromSeconds(60) 
        };
        UnityClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    }
    public readonly HttpClient UnityClient;

    public void Dispose() => UnityClient.Dispose();
}
