using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TinyFanControl
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public ICommand SetAutoCmd       => App.SetAutoCommand;
        public ICommand SetManualCmd     => App.SetManualCommand;
        public ICommand ProfileCmd       => App.ProfileCommand;
        public ICommand DeleteProfileCmd => App.DeleteProfileCommand;

        public System.Collections.ObjectModel.ObservableCollection<FanControlItem> Fans => App.Fans;
        public System.Collections.ObjectModel.ObservableCollection<ProfileSlot> Profiles => App.Profiles;

        // The window is resizable now, so only center it the first time it is
        // shown — reopening from the tray must not throw away the user's
        // chosen position and size.
        private bool _positioned;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            RestoreLayout();
        }

        private void RestoreLayout()
        {
            var saved = WindowLayout.Load();
            if (saved == null || !WindowLayout.IsUsable(saved, MinWidth, MinHeight))
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;
            _positioned = true;   // keep ShowInFront from re-centering it
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Native Windows 11 rounded corners + shadow (no-op on older Windows).
            var handle = new WindowInteropHelper(this).Handle;
            var preference = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }

        // Show centered and on top of whatever else has focus
        public void ShowInFront()
        {
            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            if (!_positioned)
            {
                CenterOnScreen();
                _positioned = true;
            }

            // Topmost flip + SetForegroundWindow: the tray icon owns focus after a
            // double-click, so Activate() alone leaves us behind the active window.
            var wasTopmost = Topmost;
            Topmost = true;
            Activate();
            Topmost = wasTopmost;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetForegroundWindow(handle);

            Focus();
        }

        private void CenterOnScreen()
        {
            var area = SystemParameters.WorkArea;
            var w = ActualWidth > 0 ? ActualWidth : Width;
            var h = ActualHeight > 0 ? ActualHeight : Height;
            Left = area.Left + (area.Width  - w) / 2;
            Top  = area.Top  + (area.Height - h) / 2;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        // Minimize button → hide to tray (don't close)
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        // X button → close the entire app
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Patreon_Click(object sender, RoutedEventArgs e) =>
            OpenCreatorLink("https://www.patreon.com/cw/Stressced");

        private void X_Click(object sender, RoutedEventArgs e) =>
            OpenCreatorLink("https://x.com/Stressced");

        private void OpenCreatorLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, $"Could not open your browser.\n\n{url}",
                    "TinyFanControl Laptop", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowLayout.Save(this);
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = false;
            WindowLayout.Save(this);
            Application.Current.Shutdown();
        }
    }
}
