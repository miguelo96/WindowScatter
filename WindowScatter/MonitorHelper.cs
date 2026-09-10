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

            IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
            return GetMonitorBounds(hMonitor);
        }

        public static MonitorBounds GetPrimaryMonitor()
        {
            POINT point = new POINT { X = 0, Y = 0 };
            IntPtr hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            return GetMonitorBounds(hMonitor);
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
