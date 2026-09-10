using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class WindowEnumerator
    {
        private const int MAX_WINDOWS = 20;

        private readonly IntPtr ownerWindowHandle;

        public WindowEnumerator(IntPtr ownerWindowHandle)
        {
            this.ownerWindowHandle = ownerWindowHandle;
        }

        public List<WindowInfo> EnumerateVisibleWindows(MonitorHelper.MonitorBounds targetMonitor)
        {
            var windows = new List<WindowInfo>();

            EnumWindows((h, l) =>
            {
                try
                {
                    if (!IsWindowVisible(h) || IsIconic(h))
                        return true;

                    int len = GetWindowTextLength(h);
                    if (len == 0) return true;

                    var sb = new StringBuilder(len + 1);
                    GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (string.IsNullOrWhiteSpace(title) || IsExcludedWindow(title))
                        return true;

                    RECT rect;
                    if (GetWindowRect(h, out rect))
                    {
                        RECT dwmRect = rect;
                        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out dwmRect, Marshal.SizeOf(typeof(RECT))) == 0)
                        {
                            rect = dwmRect;
                        }

                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;

                        if (width >= 100 && height >= 100)
                        {
                            if (MonitorHelper.IsWindowOnMonitor(rect, targetMonitor))
                            {
                                // Animation coordinates are relative to the selected monitor.
                                RECT normalizedRect = new RECT
                                {
                                    Left = rect.Left - targetMonitor.Left,
                                    Top = rect.Top - targetMonitor.Top,
                                    Right = rect.Right - targetMonitor.Left,
                                    Bottom = rect.Bottom - targetMonitor.Top
                                };

                                WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
                                placement.length = Marshal.SizeOf(placement);
                                bool isMaximized = false;

                                if (GetWindowPlacement(h, ref placement))
                                {
                                    isMaximized = (placement.showCmd == SW_SHOWMAXIMIZED);
                                }

                                windows.Add(new WindowInfo
                                {
                                    Handle = h,
                                    Title = title,
                                    OriginalRect = normalizedRect,
                                    WasMaximized = isMaximized
                                });
                            }
                        }
                    }
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            if (windows.Count > MAX_WINDOWS)
                windows = windows.GetRange(0, MAX_WINDOWS);

            return windows;
        }

        public List<WindowInfo> EnumerateVisibleWindows()
        {
            return EnumerateVisibleWindows(MonitorHelper.GetPrimaryMonitor());
        }

        private bool IsExcludedWindow(string title)
        {
            var lower = title.ToLower();
            return lower.Contains("window scatter") ||
                   lower.Contains("program manager") ||
                   lower.Contains("microsoft text input") ||
                   lower.Contains("windows input") ||
                   lower.Contains("nvidia geforce") ||
                   title == "Default IME" ||
                   title == "MSCTFIME UI" ||
                   title == "GDI+ Window";
        }
    }
}
