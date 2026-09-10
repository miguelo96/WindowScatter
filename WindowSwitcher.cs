using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class WindowSwitcher
    {
        private readonly ThumbnailManager thumbnailManager;
        private readonly WindowAnimationManager animationManager;
        private readonly List<WindowThumb> windowThumbs;
        private readonly List<WindowLayout> cachedLayouts;
        private readonly Action onSwitchComplete;

        public WindowSwitcher(ThumbnailManager thumbnailManager, WindowAnimationManager animationManager,
            List<WindowThumb> windowThumbs, List<WindowLayout> cachedLayouts, Action onSwitchComplete)
        {
            this.thumbnailManager = thumbnailManager;
            this.animationManager = animationManager;
            this.windowThumbs = windowThumbs;
            this.cachedLayouts = cachedLayouts;
            this.onSwitchComplete = onSwitchComplete;
        }

        public void SwitchToWindow(IntPtr windowHandle, Dispatcher dispatcher)
        {
            // DWM thumbnail stacking follows registration order rather than the WPF
            // canvas Z-order. Re-register only the clicked thumbnail so it stays on
            // top while it returns to its real window. This is intentionally kept
            // separate from the old all-thumbnail resync below, which added a large
            // idle-time stall to the dismiss path.
            thumbnailManager.BringThumbnailToFront(windowHandle, animationManager);

            var layout = cachedLayouts?.FirstOrDefault(l => l.Window.Handle == windowHandle);
            bool wasMaximized = layout != null && layout.Window.WasMaximized;

            foreach (var thumb in windowThumbs)
            {
                var originalLayout = cachedLayouts?.FirstOrDefault(l => l.Window.Handle == thumb.WindowHandle);

                if (originalLayout != null)
                {
                    var origRect = originalLayout.Window.OriginalRect;
                    thumb.StartX = origRect.Left;
                    thumb.StartY = origRect.Top;
                    thumb.StartWidth = origRect.Right - origRect.Left;
                    thumb.StartHeight = origRect.Bottom - origRect.Top;
                }
                else
                {
                    RECT rect;
                    if (GetWindowRect(thumb.WindowHandle, out rect))
                    {
                        thumb.StartX = rect.Left;
                        thumb.StartY = rect.Top;
                        thumb.StartWidth = rect.Right - rect.Left;
                        thumb.StartHeight = rect.Bottom - rect.Top;
                    }
                }
            }

            try
            {
                animationManager.StartReturnAnimation(async () =>
                {
                    // Short grace: the animation loop's trailing DwmFlush
                    // already presented the final positions, so this only
                    // needs to cover 1-2 presents, not 100ms.
                    await Task.Delay(30);

                    await Task.Run(() =>
                    {
                        if (wasMaximized)
                            ShowWindow(windowHandle, SW_SHOWMAXIMIZED);
                        else
                            ShowWindow(windowHandle, SW_RESTORE);

                        ForceForegroundWindow(windowHandle);
                    });

                    await Task.Delay(30);

                    onSwitchComplete?.Invoke();
                });
            }
            catch
            {
                onSwitchComplete?.Invoke();
            }
        }

        /// <summary>
        /// A bare SetForegroundWindow call silently fails when our process is not
        /// the foreground one (the common case when dismissing an overlay), which
        /// made clicks appear to do nothing every other time (#10). Fall back to
        /// the thread-input attach dance, which is the documented workaround.
        /// </summary>
        private static void ForceForegroundWindow(IntPtr windowHandle)
        {
            try
            {
                if (SetForegroundWindow(windowHandle))
                    return;

                if (GetForegroundWindow() == windowHandle)
                    return;

                try { AllowSetForegroundWindow(ASFW_ANY); } catch { }

                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = 0;
                uint currentThread = GetCurrentThreadId();

                try
                {
                    if (foreground != IntPtr.Zero)
                        foregroundThread = GetWindowThreadProcessId(foreground, out _);

                    if (foregroundThread != 0 && foregroundThread != currentThread)
                        AttachThreadInput(currentThread, foregroundThread, true);

                    BringWindowToTop(windowHandle);
                    SetForegroundWindow(windowHandle);
                }
                finally
                {
                    try
                    {
                        if (foregroundThread != 0 && foregroundThread != currentThread)
                            AttachThreadInput(currentThread, foregroundThread, false);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
