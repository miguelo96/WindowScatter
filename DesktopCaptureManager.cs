using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class DesktopCaptureManager
    {
        private readonly Image backgroundImage;
        private readonly Window owner;
        private IntPtr desktopThumbnail = IntPtr.Zero;
        private DWM_THUMBNAIL_PROPERTIES desktopProps;
        private BlurEffect blurEffect;

        private RECT currentMonitorBounds;

        public DesktopCaptureManager(Image backgroundImage, Window owner)
        {
            this.backgroundImage = backgroundImage;
            this.owner = owner;

            this.blurEffect = new BlurEffect
            {
                Radius = 0,
                RenderingBias = RenderingBias.Performance,
                KernelType = KernelType.Gaussian
            };
            this.backgroundImage.Effect = this.blurEffect;

            desktopProps = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_RECTSOURCE,
                fVisible = true,
                opacity = 255,
                fSourceClientAreaOnly = false
            };
        }

        public void SetMonitorBounds(int left, int top, int width, int height)
        {
            currentMonitorBounds = new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }

        public async Task<bool> CaptureDesktopAsync()
        {
            var helper = new WindowInteropHelper(owner);
            IntPtr ownerHwnd = helper.Handle;

            if (ownerHwnd == IntPtr.Zero) return false;

            if (desktopThumbnail != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(desktopThumbnail);
                desktopThumbnail = IntPtr.Zero;
                await Task.Delay(10);
            }

            IntPtr desktopHandle = GetDesktopWindowHandle();
            if (desktopHandle == IntPtr.Zero) return false;

            int result = DwmRegisterThumbnail(ownerHwnd, desktopHandle, out desktopThumbnail);
            if (result != 0 || desktopThumbnail == IntPtr.Zero) return false;

            UpdateDesktopThumbnail();
            DwmFlush();
            await Task.Delay(50);

            SIZE sourceSize;
            int sizeResult = DwmQueryThumbnailSourceSize(desktopThumbnail, out sourceSize);
            if (sizeResult != 0 || sourceSize.cx == 0 || sourceSize.cy == 0)
            {
                Cleanup();
                return false;
            }

            return true;
        }

        public void UpdateDesktopThumbnail()
        {
            if (desktopThumbnail == IntPtr.Zero) return;

            desktopProps.rcDestination.Left = 0;
            desktopProps.rcDestination.Top = 0;
            desktopProps.rcDestination.Right = (int)owner.ActualWidth;
            desktopProps.rcDestination.Bottom = (int)owner.ActualHeight;

            desktopProps.rcSource = currentMonitorBounds;
            desktopProps.dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_RECTSOURCE;

            DwmUpdateThumbnailProperties(desktopThumbnail, ref desktopProps);
        }

        public void SetBlur(double radius)
        {
            if (blurEffect != null)
                blurEffect.Radius = radius;
        }

        public void Cleanup()
        {
            if (desktopThumbnail != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(desktopThumbnail);
                desktopThumbnail = IntPtr.Zero;
            }

            if (blurEffect != null)
                blurEffect.Radius = 0;
        }

        private IntPtr GetDesktopWindowHandle()
        {
            IntPtr workerW = IntPtr.Zero;
            IntPtr progman = FindWindow("Progman", null);

            if (progman == IntPtr.Zero) return IntPtr.Zero;

            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out IntPtr result);

            EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellViewWin = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellViewWin != IntPtr.Zero)
                {
                    workerW = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);

            return workerW != IntPtr.Zero ? workerW : progman;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    }
}
