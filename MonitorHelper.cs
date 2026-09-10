using System;
using System.Runtime.InteropServices;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class MonitorHelper
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public class MonitorBounds
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsPrimary { get; set; }

            public int Right => Left + Width;
            public int Bottom => Top + Height;

            public override string ToString()
            {
                return $"Monitor: {Width}x{Height} at ({Left},{Top}) {(IsPrimary ? "[PRIMARY]" : "")}";
            }
        }

        public static MonitorBounds GetMonitorFromCursor()
        {
            POINT cursorPos;
            if (!GetCursorPos(out cursorPos))
            {
                return GetPrimaryMonitor();
            }

            return GetMonitorFromPoint(cursorPos.X, cursorPos.Y);
        }

        public static MonitorBounds GetMonitorFromPoint(int x, int y)
        {
            POINT pt = new POINT { X = x, Y = y };
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            return GetMonitorBounds(hMonitor);
        }

        public static MonitorBounds GetPrimaryMonitor()
        {
            POINT point = new POINT { X = 0, Y = 0 };
            IntPtr hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTOPRIMARY);
            return GetMonitorBounds(hMonitor);
        }

        /// <summary>
        /// Effective DPI scale (dpi/96) for the monitor containing the given bounds'
        /// center. Used to convert physical pixels (Win32/DWM space) to WPF DIPs.
        /// Returns 1.0 when the DPI cannot be determined (e.g. older Windows).
        /// </summary>
        public static double GetDpiScaleForMonitor(MonitorBounds monitor)
        {
            return GetDpiScaleAtPoint(
                monitor.Left + monitor.Width / 2,
                monitor.Top + monitor.Height / 2);
        }

        public static double GetDpiScaleAtPoint(int x, int y)
        {
            try
            {
                POINT pt = new POINT { X = x, Y = y };
                IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    if (GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY) == 0 && dpiX > 0)
                        return dpiX / 96.0;
                }
            }
            catch { }

            return 1.0;
        }

        private static MonitorBounds GetMonitorBounds(IntPtr hMonitor)
        {
            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            if (GetMonitorInfo(hMonitor, ref info))
            {
                return new MonitorBounds
                {
                    Left = info.rcMonitor.Left,
                    Top = info.rcMonitor.Top,
                    Width = info.rcMonitor.Right - info.rcMonitor.Left,
                    Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                    IsPrimary = (info.dwFlags & 0x00000001) != 0
                };
            }

            return new MonitorBounds
            {
                Left = 0,
                Top = 0,
                Width = (int)System.Windows.SystemParameters.PrimaryScreenWidth,
                Height = (int)System.Windows.SystemParameters.PrimaryScreenHeight,
                IsPrimary = true
            };
        }

        public static bool IsWindowOnMonitor(RECT windowRect, MonitorBounds monitor)
        {
            // Use the window center so large windows are assigned to a single monitor.
            int centerX = (windowRect.Left + windowRect.Right) / 2;
            int centerY = (windowRect.Top + windowRect.Bottom) / 2;

            return centerX >= monitor.Left && centerX < monitor.Right &&
                   centerY >= monitor.Top && centerY < monitor.Bottom;
        }
    }
}
