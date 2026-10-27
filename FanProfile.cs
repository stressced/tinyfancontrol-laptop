using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace TinyFanControl
{
    // What actually lands on disk. Reuses FanSetting so a profile is
    // literally "a snapshot of what settings.json would hold right now".
    public class StoredProfile
    {
        public int Slot { get; set; }
        public List<FanSetting> Fans { get; set; } = new();
    }

    // One button in the footer strip. Observable so the button repaints
    // the moment a slot is filled or cleared.
    public class ProfileSlot : INotifyPropertyChanged
    {
        public int Index { get; init; }
        public string Label => (Index + 1).ToString();

        private List<FanSetting>? _fans;
        public List<FanSetting>? Fans
        {
            get => _fans;
            set
            {
                _fans = value;
                OnPropertyChanged(nameof(Fans));
                OnPropertyChanged(nameof(IsFilled));
            }
        }

        public bool IsFilled => _fans is { Count: > 0 };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // Separate file from settings.json on purpose: FanSettings.Load()
    // deserializes a bare array and swallows exceptions, so changing that
    // file's shape would silently wipe existing users' fan settings.
    public static class ProfileStore
    {
        public const int SlotCount = 4;

        private static readonly string ProfilesPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TinyFanControl-Laptop", "profiles.json");

        public static List<StoredProfile> Load()
        {
            try
            {
                if (File.Exists(ProfilesPath))
                {
                    var json = File.ReadAllText(ProfilesPath);
                    return JsonSerializer.Deserialize<List<StoredProfile>>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        public static void Save(IEnumerable<ProfileSlot> slots)
        {
            var stored = new List<StoredProfile>();
            foreach (var slot in slots)
                if (slot.IsFilled)
                    stored.Add(new StoredProfile { Slot = slot.Index, Fans = slot.Fans! });

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
                File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(stored));
            }
            catch { }
        }
    }
}
