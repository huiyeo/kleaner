using System.IO;
using System.Text.Json;

namespace Kleaner.App;

/// <summary>界面字符串外置加载（预留 i18n：新增语言文件即可切换）。</summary>
public static class S
{
    private static Dictionary<string, string> _map = new();

    public static void Load(string lang = "zh-CN")
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", $"Strings.{lang}.json");
            _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            _map = new Dictionary<string, string>();
        }
    }

    public static string Get(string key) => _map.TryGetValue(key, out var value) ? value : key;

    public static string Format(string key, params object?[] args) =>
        args.Length == 0 ? Get(key) : string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
