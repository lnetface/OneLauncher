using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace OneLauncher.Core.Global;
public class AutoUpdate
{
    const string CheckUpdateUrl = "https://raw.githubusercontent.com/lnetface/OneLauncher/master/OneLauncher.Core/Global/Version.cs";
    const string UpdaterUrlWindows = "https://raw.githubusercontent.com/lnetface/OneLauncher/master/build/install.ps1";
    /// <returns>需要更新返回true，否则返回false</returns>
    public static async Task<bool> CheckUpdate()
    {
        try
        {
            using var httpClient = new HttpClient();
            string response = await httpClient.GetStringAsync(CheckUpdateUrl);
            int start = -1, end = -1;
            for(int i  = 0; i < response.Length; i++)
            {
                if (response[i] == '\"')
                {
                    start = i+1;
                    break;
                }
            }
            if(start == -1) throw new Exception();
            for (int i = start; i < response.Length; i++)
            {
                if (response[i] == '\"')
                {
                    end = i;
                    break;
                }
            }
            if(end == -1) throw new Exception();
            var latestVersion = new Version(response[start..end]);
            if (latestVersion <= new Version(Init.ApplicationVersoin))
                return false;
            else
                return true;
        }
        catch (Exception ex)
        {
            /*无关紧要，忽略就好*/
            return false;
        }
    }
    public static async Task Update()
    {
        try
        {
#if ONELAUNCHER_TARGET_WINDOWS
            var path = Path.Combine(Init.BasePath, "install.ps1");
            await Init.Download.DownloadFile
                (UpdaterUrlWindows, path);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = $"{path} update {Process.GetCurrentProcess()!.MainModule!.FileName}"
            };
            process.Start();
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
