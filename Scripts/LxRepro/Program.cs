using Microsoft.Extensions.Logging;
using System;
using System.IO;
using WinUIMusicPlayer.Services.Plugins;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("repro");

foreach (var file in args)
{
    Console.WriteLine($"=== loading {Path.GetFileName(file)} ===");
    try
    {
        var code = File.ReadAllText(file);
        var hash = "repro-" + Path.GetFileNameWithoutExtension(file);
        var lx = LxPluginRuntime.Load(code, hash, logger);
        Console.WriteLine($"OK: name={lx.ScriptInfo.Name} version={lx.ScriptInfo.Version} sources=[{string.Join(",", lx.Sources.Keys)}]");
        foreach (var (src, info) in lx.Sources)
            Console.WriteLine($"  {src}: actions=[{string.Join(",", info.Actions)}] qualitys=[{string.Join(",", info.Qualities)}]");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.ToString());
    }
}
