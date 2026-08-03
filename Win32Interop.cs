using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowScatter
{
    internal static class Win32Interop
    {
        #region Window Enumeration



        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        #endregion

        #region DWM (Desktop Window Manager)

        [DllImport("dwmapi.dll")]
        internal static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);

        public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;


        [StructLayout(LayoutKind.Sequential)]
        internal struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("dwmapi.dll")]
        internal static extern int DwmFlush();

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out SIZE pSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        internal const int DWM_TNP_RECTDESTINATION = 0x00000001;
        internal const int DWM_TNP_OPACITY = 0x00000004;
        internal const int DWM_TNP_VISIBLE = 0x00000008;
        #endregion

        #region Keyboard Hook

        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);



        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;

        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int WM_SYSKEYUP = 0x0105;
        internal const uint KEYEVENTF_KEYUP = 0x0002;

        internal const int VK_TAB = 0x09;
        internal const int VK_SHIFT = 0x10;
        internal const int VK_CONTROL = 0x11;
        internal const int VK_MENU = 0x12;
        internal const int VK_W = 0x57;
        internal const int VK_LWIN = 0x5B;
        internal const int VK_RWIN = 0x5C;

        #endregion

        #region Window Messages

        internal const int WM_SETTINGCHANGE = 0x001A;

        #endregion

        #region Window Constants

        internal const int SW_RESTORE = 9;

        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        internal static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOZORDER = 0x0004;

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        internal const int SW_SHOWMAXIMIZED = 3;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public System.Drawing.Rectangle rcNormalPosition;
        }

        #endregion

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        internal const int DWM_TNP_RECTSOURCE = 0x00000002;
    }
}
