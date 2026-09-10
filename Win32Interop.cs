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

        #region DirectComposition host window + private DWM visual APIs

        internal const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        internal const int WS_EX_TOOLWINDOW_STYLE = 0x00000080;
        internal const int WS_EX_TRANSPARENT_CLICKTHROUGH = 0x00000020;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const uint WS_POPUP = 0x80000000;
        internal const int SW_HIDE = 0;
        internal const int DWM_TNP_ENABLE3D = 0x4000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
            [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        // --- dwmapi private exports (ordinal-based; research credit: ADeltaX) ---

        // Ordinal 147: creates an IDCompositionVisual2 whose content is a live
        // thumbnail of hwndSource. Do NOT release the returned HTHUMBNAIL.
        internal delegate int DwmpCreateSharedThumbnailVisualDelegate(
            IntPtr hwndDestination, IntPtr hwndSource, uint dwThumbnailFlags,
            ref DWM_THUMBNAIL_PROPERTIES pThumbnailProperties,
            IntPtr pDCompDevice, out IntPtr ppVisual, out IntPtr phThumbnailId);

        // Ordinal 163 (Cobalt/Iron+): creates a visual with the whole desktop
        // (multi-window / virtual desktop compositing).
        internal delegate int DwmpCreateSharedMultiWindowVisualDelegate(
            IntPtr hwndDestination, IntPtr pDCompDevice, out IntPtr ppVisual, out IntPtr phThumbnailId);

        // Ordinal 164: populates the multi-window visual; may only exclude windows.
        internal delegate int DwmpUpdateSharedMultiWindowVisualDelegate(
            IntPtr hThumbnailId,
            IntPtr[]? phwndsInclude, int chwndsInclude,
            IntPtr[]? phwndsExclude, int chwndsExclude,
            out RECT prcSource, out SIZE pDestinationSize, uint dwFlags);

        // user32 private export used to exclude our own windows from the desktop visual.
        internal const int WCA_EXCLUDED_FROM_LIVEPREVIEW = 0x0D;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attrib;
            public IntPtr pvData;
            public int cbData;
        }

        [DllImport("user32.dll")]
        internal static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        [DllImport("d3d11.dll")]
        internal static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software, uint flags,
            IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
            out IntPtr ppDevice, IntPtr pFeatureLevel, IntPtr ppImmediateContext);

        [DllImport("dcomp.dll")]
        internal static extern int DCompositionCreateDevice3(IntPtr renderingDevice, ref Guid iid, out IntPtr dcompositionDevice);

        #endregion

        #region Working Set

        [DllImport("kernel32.dll")]
        internal static extern bool GetProcessWorkingSetSize(IntPtr hProcess,
            out UIntPtr lpMinimumWorkingSetSize, out UIntPtr lpMaximumWorkingSetSize);

        [DllImport("kernel32.dll")]
        internal static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess,
            UIntPtr dwMinimumWorkingSetSize, UIntPtr dwMaximumWorkingSetSize, uint Flags);

        internal const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x00000002;

        #endregion

        #region Multimedia Timer

        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint uPeriod);

        #endregion

        #region Power & Execution State

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint esFlags);

        internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
        internal const uint ES_DISPLAY_REQUIRED = 0x00000002;
        internal const uint ES_CONTINUOUS = 0x80000000;

        #endregion
    }
}
