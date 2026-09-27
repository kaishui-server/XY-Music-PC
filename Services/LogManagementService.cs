using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.Services
{
    /// <summary>日志导出范围。</summary>
    public enum LogExportKind
    {
        /// <summary>崩溃记录: [FTL] 全部 + 未处理/未观察异常标记的 [ERR]。</summary>
        Crash,
        /// <summary>错误日志: 所有 [ERR] 与 [FTL]。</summary>
        Error,
        /// <summary>全部日志: 所有保留的日志文件。</summary>
        All
    }

    /// <summary>
    /// 日志管理: 等级开关(只记录警告和报错)、当前日志文件条数裁剪、按范围导出、清空。
    /// 静态类 + 独立 JSON 存储 —— Serilog 在宿主构建(数据库服务可用之前)就要读初始配置,
    /// 不能依赖异步的 MusicDatabaseService 设置表。
    /// </summary>
    public static class LogManagementService
    {
        private static readonly string StoreFile = Path.Combine(AppPaths.LocalFolder, "LogSettings.json");
        private static readonly Regex TimestampLine = new(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", RegexOptions.Compiled);
        private static LoggingLevelSwitch? _levelSwitch;
        private static Timer? _trimTimer;
        private static readonly Lock _sync = new();

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XYMusic", "Logs");

        /// <summary>当前日志文件最多保留的条数(0=不限)。</summary>
        public static int RetentionCount { get; private set; } = 1000;

        /// <summary>只记录警告和报错日志。</summary>
        public static bool WarningOnly { get; private set; } = false;

        private class LogSettingsData
        {
            public int RetentionCount { get; set; } = 1000;
            public bool WarningOnly { get; set; } = false;
        }

        /// <summary>
        /// 宿主构建日志管道时调用: 装载持久化配置并应用等级, 启动周期裁剪。
        /// </summary>
        public static void Initialize(LoggingLevelSwitch levelSwitch)
        {
            _levelSwitch = levelSwitch;
            try
            {
                if (File.Exists(StoreFile))
                {
                    var data = JsonSerializer.Deserialize<LogSettingsData>(File.ReadAllText(StoreFile));
                    if (data is not null)
                    {
                        RetentionCount = data.RetentionCount;
                        WarningOnly = data.WarningOnly;
                    }
                }
            }
            catch
            {
                // 配置文件损坏时按默认值继续, 不阻塞启动
            }
            ApplyLevel();
            CleanupTrimTempFiles();
            _trimTimer = new Timer(_ => TrimLogFiles(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public static void SetRetentionCount(int count)
        {
            lock (_sync)
            {
                RetentionCount = count;
                Save();
            }
            TrimLogFiles();
        }

        public static void SetWarningOnly(bool warningOnly)
        {
            lock (_sync)
            {
                WarningOnly = warningOnly;
                Save();
            }
            ApplyLevel();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LocalFolder);
                File.WriteAllText(StoreFile, JsonSerializer.Serialize(new LogSettingsData
                {
                    RetentionCount = RetentionCount,
                    WarningOnly = WarningOnly
                }));
            }
            catch
            {
                // 保存失败不影响运行, 下次修改会重试
            }
        }

        private static void ApplyLevel()
        {
            if (_levelSwitch is not null)
                _levelSwitch.MinimumLevel = WarningOnly ? LogEventLevel.Warning : LogEventLevel.Information;
        }

        /// <summary>按文件名(含日期)排序的全部保留日志文件。</summary>
        public static List<string> GetLogFiles()
        {
            try
            {
                return Directory.Exists(LogDirectory)
                    ? Directory.GetFiles(LogDirectory, "WinUIMusicPlayer*.log").Order().ToList()
                    : [];
            }
            catch
            {
                return [];
            }
        }

        private static string ActiveLogPath =>
            Path.Combine(LogDirectory, $"WinUIMusicPlayer-{DateTime.Now:yyyyMMdd}.log");

        private static void CleanupTrimTempFiles()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return;
                foreach (var tmp in Directory.GetFiles(LogDirectory, "*.trim"))
                    File.Delete(tmp);
            }
            catch { }
        }

        /// <summary>
        /// 裁剪历史日志文件到最近 RetentionCount 条。当天活动文件被 Serilog 以
        /// shared 模式持有句柄(无 Delete 共享权)无法替换, 跨天成为历史文件后裁剪。
        /// </summary>
        public static void TrimLogFiles()
        {
            if (RetentionCount <= 0) return;
            var active = ActiveLogPath;
            foreach (var file in GetLogFiles())
            {
                if (file == active) continue;
                TrimFile(file);
            }
        }

        private static void TrimFile(string path)
        {
            var tmp = path + ".trim";
            try
            {
                if (!File.Exists(path)) return;
                // 单条日志最短约 45 字节, 文件小于该值必然未超限, 免解析
                if (new FileInfo(path).Length < (long)RetentionCount * 48) return;
                var entries = ReadEntries(path);
                if (entries.Count <= RetentionCount) return;
                var sb = new StringBuilder();
                foreach (var line in entries.Skip(entries.Count - RetentionCount))
                    sb.AppendLine(line);
                File.WriteAllText(tmp, sb.ToString());
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                // 裁剪是尽力而为的后台任务, 失败等待下个周期; 清理临时文件避免残留
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        /// <summary>读取日志文件并按时间戳行分组为完整条目(异常堆栈归入所属条目)。
        /// 兼容 SetLength(0) 截断后 Serilog 写入产生的 NUL 稀疏空洞: 剔除 NUL 字符, 跳过空行。</summary>
        private static List<string> ReadEntries(string path)
        {
            var entries = new List<string>();
            StringBuilder? current = null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Contains('\0'))
                    line = line.Replace("\0", "");
                if (line.Length == 0) continue;
                if (TimestampLine.IsMatch(line))
                {
                    if (current is not null) entries.Add(current.ToString());
                    current = new StringBuilder(line);
                }
                else
                {
                    current ??= new StringBuilder();
                    current.AppendLine().Append(line);
                }
            }
            if (current is not null) entries.Add(current.ToString());
            return entries;
        }

        private static bool IsCrashEntry(string entry) =>
            entry.Contains("[FTL]") || entry.Contains("未处理异常") || entry.Contains("未观察到的异常");

        private static bool IsErrorEntry(string entry) =>
            entry.Contains("[ERR]") || entry.Contains("[FTL]");

        /// <summary>
        /// 按范围导出日志到目标文件。返回 (是否成功, 导出条数, 错误信息)。
        /// </summary>
        public static (bool Ok, int Count, string? Error) Export(LogExportKind kind, string targetPath)
        {
            try
            {
                var sb = new StringBuilder();
                var count = 0;
                foreach (var file in GetLogFiles())
                {
                    var entries = ReadEntries(file);
                    if (kind == LogExportKind.All)
                    {
                        if (entries.Count == 0) continue;
                        sb.AppendLine($"===== {Path.GetFileName(file)} =====");
                        foreach (var entry in entries) sb.AppendLine(entry);
                        count += entries.Count;
                    }
                    else
                    {
                        var matched = entries.Where(e => kind == LogExportKind.Crash ? IsCrashEntry(e) : IsErrorEntry(e)).ToList();
                        if (matched.Count == 0) continue;
                        sb.AppendLine($"===== {Path.GetFileName(file)} =====");
                        foreach (var entry in matched) sb.AppendLine(entry);
                        count += matched.Count;
                    }
                }
                if (count == 0) return (true, 0, null);
                File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(false));
                return (true, count, null);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// 清空全部日志文件, 返回清空的文件数。当天活动文件被 Serilog 持有句柄
        /// (无 Delete 共享权)无法删除, 以共享写方式 SetLength(0) 截断 —— 与
        /// PowerShell Clear-Content 等效; Serilog 后续写入在原 position 落盘,
        /// NTFS 以稀疏区填充中间空洞, 不占实际磁盘空间。
        /// </summary>
        public static int ClearAllLogs()
        {
            var cleared = 0;
            var active = ActiveLogPath;
            foreach (var file in GetLogFiles())
            {
                try
                {
                    if (file == active)
                        TruncateSharedFile(file);
                    else
                        File.Delete(file);
                    cleared++;
                }
                catch (IOException)
                {
                    // 与写入竞态的文件跳过
                }
            }
            return cleared;
        }

        private static void TruncateSharedFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            fs.SetLength(0);
            fs.Flush();
        }
    }
}
