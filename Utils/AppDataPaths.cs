using System;
using System.IO;

namespace WinUIMusicPlayer.Utils;

/// <summary>
/// 提供应用本地数据目录。
/// 非 MSIX 打包(无包标识)进程访问 ApplicationData.Current 会抛 0x80073D54,
/// 此时回退到 %LOCALAPPDATA%\XYMusic
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _localFolder = new(() =>
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XYMusic");
        }
    });

    public static string LocalFolder => _localFolder.Value;
}
