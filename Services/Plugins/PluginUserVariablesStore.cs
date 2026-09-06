using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>
    /// 插件用户变量存储: 按插件平台名持久化到 Plugins/uservars.json。
    /// 参考 BakaMusic private.pluginMeta[platform].userVariables 设计,
    /// 按平台(而非 hash)存储使插件更新后变量自动继承, 运行时经 env.getUserVariables()/env.userVariables 注入。
    /// </summary>
    internal static class PluginUserVariablesStore
    {
        private const int MaxKeyLength = 256;
        private const int MaxValueLength = 32_768;

        private static readonly object _gate = new();
        private static Dictionary<string, Dictionary<string, string>>? _cache;

        private static string StoreFile => Path.Combine(Path.Combine(AppPaths.LocalFolder, "Plugins"), "uservars.json");

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            if (_cache is not null) return _cache;
            lock (_gate)
            {
                _cache ??= ReadFile();
                return _cache;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ReadFile()
        {
            try
            {
                if (File.Exists(StoreFile))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(StoreFile));
                    if (dict is not null) return dict;
                }
            }
            catch { /* 损坏则重置 */ }
            return [];
        }

        private static void Save(Dictionary<string, Dictionary<string, string>> data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
                File.WriteAllText(StoreFile, JsonSerializer.Serialize(data));
            }
            catch { /* 保存失败忽略, 不影响插件运行 */ }
        }

        /// <summary>读取某插件平台的用户变量(值为空的键过滤掉)。</summary>
        public static Dictionary<string, string> Get(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return [];
            var data = Load();
            lock (_gate)
            {
                if (data.TryGetValue(platform, out var vars))
                {
                    var result = new Dictionary<string, string>();
                    foreach (var (k, v) in vars)
                        if (!string.IsNullOrEmpty(k) && k.Length <= MaxKeyLength && v is not null && v.Length <= MaxValueLength)
                            result[k] = v;
                    return result;
                }
                return [];
            }
        }

        /// <summary>写入某插件平台的用户变量。</summary>
        public static void Set(string platform, Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(platform)) return;
            var data = Load();
            lock (_gate)
            {
                if (values.Count == 0) data.Remove(platform);
                else data[platform] = new Dictionary<string, string>(values);
                Save(data);
            }
        }

        /// <summary>卸载插件时清理对应平台的用户变量。</summary>
        public static void Remove(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return;
            var data = Load();
            lock (_gate)
            {
                if (data.Remove(platform)) Save(data);
            }
        }
    }
}
