using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace TinyFanControl
{
    public class WindowLayoutData
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    // Window geometry lives in its own file for the same reason profiles do:
    // settings.json is a bare array and its loader swallows exceptions, so
    // changing its shape would silently wipe existing fan settings.
    public static class WindowLayout
    {
        private static readonly string LayoutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TinyFanControl", "window.json");

        public static WindowLayoutData? Load()
        {
            try
            {
                if (File.Exists(LayoutPath))
                    return JsonSerializer.Deserialize<WindowLayoutData>(File.ReadAllText(LayoutPath));
            }
            catch { }
            return null;
        }

        public static void Save(Window w)
        {
            // RestoreBounds survives a minimized/maximized window; Left/Top would
            // be -32000 in those states.
            var b = w.WindowState == WindowState.Normal
                ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)
                : w.RestoreBounds;

            if (b.Width <= 0 || b.Height <= 0 || double.IsNaN(b.Left) || double.IsNaN(b.Top))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
                File.WriteAllText(LayoutPath, JsonSerializer.Serialize(new WindowLayoutData
                {
                    Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height
                }));
            }
            catch { }
        }

        // A window saved on a monitor that is no longer attached must not come
        // back off-screen, so require a usable overlap with the virtual desktop.
        public static bool IsUsable(WindowLayoutData d, double minWidth, double minHeight)
        {
            if (d.Width < minWidth || d.Height < minHeight) return false;

            var screen = new Rect(SystemParameters.VirtualScreenLeft,
                                  SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth,
                                  SystemParameters.VirtualScreenHeight);

            var visible = Rect.Intersect(screen, new Rect(d.Left, d.Top, d.Width, d.Height));
            return !visible.IsEmpty && visible.Width >= 120 && visible.Height >= 40;
        }
    }
}
