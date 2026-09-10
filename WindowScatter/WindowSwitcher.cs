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
            thumbnailManager.BringThumbnailToFront(windowHandle, animationManager);

            Task.Run(async () =>
            {
                // Use cached DWM frame bounds; GetWindowRect can include invisible borders.
                var capturedStates = new List<(IntPtr handle, double x, double y, double w, double h)>();

                foreach (var thumb in windowThumbs)
                {
                    var originalLayout = cachedLayouts?.FirstOrDefault(l => l.Window.Handle == thumb.WindowHandle);

                    if (originalLayout != null)
                    {
                        var origRect = originalLayout.Window.OriginalRect;
                        capturedStates.Add((
                            thumb.WindowHandle,
                            origRect.Left,
                            origRect.Top,
                            origRect.Right - origRect.Left,
                            origRect.Bottom - origRect.Top
                        ));
                    }
                    else
                    {
                        RECT rect;
                        if (GetWindowRect(thumb.WindowHandle, out rect))
                        {
                            capturedStates.Add((
                                thumb.WindowHandle,
                                rect.Left,
                                rect.Top,
                                rect.Right - rect.Left,
                                rect.Bottom - rect.Top
                            ));
                        }
                    }
                }

                var layout = cachedLayouts?.FirstOrDefault(l => l.Window.Handle == windowHandle);
                bool wasMaximized = layout != null && layout.Window.WasMaximized;

                await dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        foreach (var state in capturedStates)
                        {
                            var thumb = windowThumbs.FirstOrDefault(t => t.WindowHandle == state.handle);
                            if (thumb != null)
                            {
                                thumb.StartX = state.x;
                                thumb.StartY = state.y;
                                thumb.StartWidth = state.w;
                                thumb.StartHeight = state.h;
                            }
                        }

                        // Refresh any missing thumbnails before the return animation begins.
                        await Task.Run(() =>
                        {
                            dispatcher.Invoke(() =>
                            {
                                thumbnailManager.ReregisterAllThumbnails(animationManager);
                            });
                        });

                        await Task.Delay(10);

                        animationManager.StartReturnAnimation(async () =>
                        {
                            await Task.Delay(100);

                            await Task.Run(() =>
                            {
                                if (wasMaximized)
                                    ShowWindow(windowHandle, SW_SHOWMAXIMIZED);
                                else
                                    ShowWindow(windowHandle, SW_RESTORE);

                                SetForegroundWindow(windowHandle);
                            });

                            await Task.Delay(50);

                            onSwitchComplete?.Invoke();
                        });
                    }
                    catch
                    {
                        onSwitchComplete?.Invoke();
                    }
                }, DispatcherPriority.Normal);
            });
        }
    }
}
