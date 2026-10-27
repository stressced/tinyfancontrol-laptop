using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TinyFanControl
{
    public class FanSetting
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; } = 50;
        public bool IsManual { get; set; } = false;
    }

    public static class FanSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TinyFanControl-Laptop", "settings.json");

        public static List<FanSetting> Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<List<FanSetting>>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        public static void Save(IEnumerable<FanControlItem> fans)
        {
            var settings = new List<FanSetting>();
            foreach (var fan in fans)
                settings.Add(new FanSetting { Name = fan.Name, Value = fan.Value, IsManual = fan.IsManual });

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
            }
            catch { }
        }
    }
}
