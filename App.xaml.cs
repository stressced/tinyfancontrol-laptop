using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using LibreHardwareMonitor.Hardware;

namespace TinyFanControl
{
    public partial class App : Application
    {
        private static readonly Mutex AppMutex = new(false, "TinyFanControlLaptop_SingleInstance_Mutex");

        private MainWindow? _mainWindow;
        private TaskbarIcon? _trayIcon;
        private Computer? _computer;

        public static System.Collections.ObjectModel.ObservableCollection<FanControlItem> Fans { get; } = new();
        public static System.Collections.ObjectModel.ObservableCollection<ProfileSlot> Profiles { get; } = new();
        public static ICommand SetAutoCommand      { get; private set; } = null!;
        public static ICommand SetManualCommand    { get; private set; } = null!;
        public static ICommand ProfileCommand      { get; private set; } = null!;
        public static ICommand DeleteProfileCommand{ get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (!AppMutex.WaitOne(TimeSpan.Zero, true))
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            SetAutoCommand       = new RelayCommand<FanControlItem>(SetAuto);
            SetManualCommand     = new RelayCommand<FanControlItem>(SetManual);
            ProfileCommand       = new RelayCommand<ProfileSlot>(UseProfile);
            DeleteProfileCommand = new RelayCommand<ProfileSlot>(DeleteProfile);

            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");

            InitializeHardware();
            LoadAndApplySavedSettings();
            LoadProfiles();

            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }
            RefreshReadings();
            _mainWindow.ShowInFront();
        }

        private void InitializeHardware()
        {
            _computer = new Computer { IsMotherboardEnabled = true, IsControllerEnabled = true };
            _computer.Open();
            _computer.Accept(new UpdateVisitor());

            foreach (var hardware in _computer.Hardware)
            {
                var targets = hardware.HardwareType == HardwareType.Motherboard
                    ? hardware.SubHardware.Concat(new[] { hardware })
                    : new[] { hardware };

                foreach (var h in targets)
                {
                    foreach (var cs in h.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null))
                    {
                        Fans.Add(new FanControlItem
                        {
                            Control  = cs.Control!,
                            Sensor   = cs,
                            Hardware = h,
                            Name     = $"{h.Name} · {cs.Name}",
                            Value    = cs.Value ?? 50,
                            IsManual = false
                        });
                    }
                }
            }

            if (Fans.Count == 0)
                MessageBox.Show("No fan controls detected.\n\nCheck:\n1. Running as Administrator\n2. PawniO driver is installed (required by LibreHardwareMonitor)",
                    "TinyFanControl Laptop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void LoadAndApplySavedSettings()
        {
            var saved = FanSettings.Load();
            foreach (var fan in Fans)
            {
                var s = saved.FirstOrDefault(x => x.Name == fan.Name);
                if (s == null) continue;

                fan.IsManual = s.IsManual;
                if (s.IsManual)
                {
                    fan.Value = s.Value;
                    fan.Control.SetSoftware((float)s.Value);
                }
                // An auto fan keeps the live reading taken above: the saved
                // number is a stale snapshot of a BIOS-driven duty.
            }
        }

        // One file read at startup. Nothing touches disk again until a slot
        // is saved or deleted — no timers, no watchers.
        private static void LoadProfiles()
        {
            var stored = ProfileStore.Load();
            for (var i = 0; i < ProfileStore.SlotCount; i++)
                Profiles.Add(new ProfileSlot { Index = i, Fans = stored.FirstOrDefault(p => p.Slot == i)?.Fans });
        }

        // Hand the fan back to the BIOS. SetSoftware() would only pin it in
        // software mode at a fixed duty, which is what used to leave fans
        // stuck (at 0% in the worst case) while the UI still said AUTO.
        private static void SetAuto(FanControlItem? fan)
        {
            if (fan == null) return;
            fan.Control.SetDefault();
            fan.IsManual = false;
            Refresh(fan);
            FanSettings.Save(Fans);
        }

        private static void SetManual(FanControlItem? fan)
        {
            if (fan == null) return;
            fan.Control.SetSoftware((float)fan.Value);
            fan.IsManual = true;
            FanSettings.Save(Fans);
        }

        // Left click: empty slot captures the current speeds, filled slot applies them.
        private static void UseProfile(ProfileSlot? slot)
        {
            if (slot == null) return;
            if (slot.IsFilled) ApplyProfile(slot);
            else               CaptureProfile(slot);
        }

        private static void CaptureProfile(ProfileSlot slot)
        {
            slot.Fans = Fans
                .Select(f => new FanSetting { Name = f.Name, Value = f.Value, IsManual = f.IsManual })
                .ToList();
            ProfileStore.Save(Profiles);
        }

        private static void ApplyProfile(ProfileSlot slot)
        {
            foreach (var fan in Fans)
            {
                var s = slot.Fans!.FirstOrDefault(x => x.Name == fan.Name);
                if (s == null) continue;   // fan missing since the profile was saved

                if (s.IsManual)
                {
                    fan.Value = s.Value;
                    fan.Control.SetSoftware((float)s.Value);
                    fan.IsManual = true;
                }
                else
                {
                    // Same path as Reset, so a saved "auto" fan behaves identically.
                    fan.Control.SetDefault();
                    fan.IsManual = false;
                    Refresh(fan);
                }
            }
            FanSettings.Save(Fans);
        }

        // Right click on a filled slot.
        private static void DeleteProfile(ProfileSlot? slot)
        {
            if (slot is not { IsFilled: true }) return;
            slot.Fans = null;
            ProfileStore.Save(Profiles);
        }

        // One register read, only ever on a user action - opening the window or
        // releasing a fan. Update() costs about 4 ms.
        private static void Refresh(FanControlItem fan)
        {
            try
            {
                fan.Hardware.Update();
                if (fan.Sensor.Value is float v) fan.Value = v;
            }
            catch { }
        }

        public static void RefreshReadings()
        {
            foreach (var group in Fans.Where(f => !f.IsManual).GroupBy(f => f.Hardware))
            {
                try
                {
                    group.Key.Update();
                    foreach (var fan in group)
                        if (fan.Sensor.Value is float v) fan.Value = v;
                }
                catch { }
            }
        }

        // Leaving the app must leave the BIOS in charge, never a duty this app
        // pinned - nothing manages thermals once the process is gone.
        //
        // Two mechanisms do the work, and they are complementary:
        //   SetDefault()      hands the channel back to the BIOS curve. Works on
        //                     CPU / Pump / Chipset; on this board's System Fan
        //                     channels the driver ignores it.
        //   _computer.Close() makes the library rewrite every control register
        //                     with the value it snapshotted at Open(), undoing
        //                     whatever this session changed.
        private static void ReleaseAllToBios()
        {
            foreach (var fan in Fans)
            {
                try { fan.Control.SetDefault(); } catch { }
            }
        }

        private void OnOpenControls(object sender, RoutedEventArgs e) => ShowMainWindow();

        private void OnExit(object sender, RoutedEventArgs e)
        {
            FanSettings.Save(Fans);
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ReleaseAllToBios();
            _computer?.Close();   // also rolls every register back to its Open() state
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
