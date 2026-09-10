using System;
using System.Runtime.InteropServices;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    /// <summary>
    /// A borderless, click-through topmost window that only hosts DirectComposition
    /// visuals (WS_EX_NOREDIRECTIONBITMAP). The WPF window floats above it and handles
    /// all input/chrome; this host never activates and never paints itself.
    /// Must be created on the UI thread (WPF pumps its messages).
    /// </summary>
    internal sealed class DCompHost : IDisposable
    {
        private const string ClassName = "WindowScatterDCompHost";

        private WndProcDelegate? wndProc; // keep delegate alive

        public IntPtr Hwnd { get; private set; } = IntPtr.Zero;

        public bool Create()
        {
            if (Hwnd != IntPtr.Zero)
                return true;

            wndProc = WndProc;
            IntPtr hInstance = Marshal.GetHINSTANCE(typeof(DCompHost).Module);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = hInstance,
                lpszClassName = ClassName
            };

            if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410) // 1410 = already registered
                return false;

            Hwnd = CreateWindowExW(
                WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW_STYLE | WS_EX_TRANSPARENT_CLICKTHROUGH | WS_EX_NOACTIVATE,
                ClassName, "WindowScatter DComp Host", WS_POPUP,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (Hwnd == IntPtr.Zero)
                return false;

            // Keep the host out of any live-preview compositing the shell might do.
            int excluded = 1;
            IntPtr p = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(p, excluded);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attrib = WCA_EXCLUDED_FROM_LIVEPREVIEW,
                    pvData = p,
                    cbData = sizeof(int)
                };
                SetWindowCompositionAttribute(Hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }

            return true;
        }

        public void ShowAt(int left, int top, int width, int height)
        {
            SetWindowPos(Hwnd, HWND_TOPMOST, left, top, width, height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void Hide()
        {
            if (Hwnd != IntPtr.Zero)
                ShowWindow(Hwnd, SW_HIDE);
        }

        public void BringBelow(IntPtr hwndAbove)
        {
            // Position host directly under the WPF overlay window in Z-order.
            SetWindowPos(Hwnd, hwndAbove, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
            => DefWindowProcW(hWnd, msg, wParam, lParam);

        public void Dispose()
        {
            if (Hwnd != IntPtr.Zero)
            {
                DestroyWindow(Hwnd);
                Hwnd = IntPtr.Zero;
            }
        }
    }
}
