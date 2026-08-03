using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    public partial class MainWindow : Window
    {
        private WindowAnimationManager animationManager = null!;
        private ThumbnailManager thumbnailManager = null!;
        private WindowEnumerator windowEnumerator = null!;
        private WindowSwitcher? windowSwitcher;
        private WallpaperManager wallpaperManager = null!;
        private HotCornerManager hotCornerManager = null!;
        private DesktopCaptureManager desktopCaptureManager = null!;
        private bool useDesktopCapture = true;

        private List<WindowThumb> windowThumbs = new List<WindowThumb>();
        private List<WindowLayout>? cachedLayouts;
        private List<IntPtr>? lastWindowHandles;
        private IntPtr selectedWindowHandle = IntPtr.Zero;
        private Random rng = new Random();

        private bool isScatterActive = false;
        private bool isTransitioning = false;
        private object transitionLock = new object();

        private IntPtr hookID = IntPtr.Zero;
        private LowLevelKeyboardProc hookCallback;
        private HotkeyParser configuredHotkey;
        private bool isWinKeyPressed = false;
        private bool isCtrlPressed = false;
        private bool isAltPressed = false;
        private bool isShiftPressed = false;
        private bool suppressStartMenu = false;
        private DateTime startMenuSuppressionStart;

        private HwndSource? hwndSource;
        private DispatcherTimer? wallpaperCheckTimer;
        private string? lastWallpaperPath;

        private DateTime lastActivationTime = DateTime.MinValue;
        private const double IDLE_THRESHOLD_SECONDS = 2.0;

        private DispatcherTimer thumbnailKeepAliveTimer;

        private AppSettings settings;

        public MainWindow()
        {
            InitializeComponent();

            settings = AppSettings.Load();
            configuredHotkey = HotkeyParser.Parse(settings.Hotkey);

            this.Topmost = true;
            this.ShowActivated = true;
            this.Focusable = true;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.AllowsTransparency = false;

            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight - 1;
            this.Left = 0;
            this.Top = 0;

            desktopCaptureManager = new DesktopCaptureManager(BackgroundImage, this);
            wallpaperManager = new WallpaperManager(BackgroundImage, this);

            this.Loaded += OnWindowLoaded;

            hookCallback = HookCallback;
            hookID = SetHook(hookCallback);

            hotCornerManager = new HotCornerManager(settings, async () =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (CanActivateScatter())
                    {
                        await StartScatterAsync();
                    }
                });
            });
            hotCornerManager.Start();

            thumbnailKeepAliveTimer = new DispatcherTimer();
            thumbnailKeepAliveTimer.Interval = TimeSpan.FromSeconds(1);
            thumbnailKeepAliveTimer.Tick += ThumbnailKeepAlive_Tick;
        }

        #region Initialization

        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            animationManager = new WindowAnimationManager(BackgroundImage, BlurOverlay, windowThumbs);
            animationManager.SetDesktopCaptureManager(desktopCaptureManager);
            thumbnailManager = new ThumbnailManager(ScatterCanvas, windowThumbs, helper.Handle, OnWindowClicked, OnWindowHovered);
            windowEnumerator = new WindowEnumerator(helper.Handle);

            if (!useDesktopCapture)
            {
                wallpaperManager.SetWallpaperBackground();
                SetupWallpaperMonitoring();
            }

            this.Visibility = Visibility.Hidden;
        }

        #endregion

        #region Wallpaper Monitoring

        private void SetupWallpaperMonitoring()
        {
            wallpaperCheckTimer = new DispatcherTimer();
            wallpaperCheckTimer.Interval = TimeSpan.FromSeconds(2);
            wallpaperCheckTimer.Tick += (s, e) =>
            {
                string? currentPath = wallpaperManager.GetWallpaperPath();
                if (currentPath != lastWallpaperPath && !string.IsNullOrEmpty(currentPath))
                {
                    lastWallpaperPath = currentPath;
                    wallpaperManager.SetWallpaperBackground();
                }
            };
            wallpaperCheckTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);

            var hwnd = new WindowInteropHelper(this).Handle;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            if (!useDesktopCapture)
                lastWallpaperPath = wallpaperManager.GetWallpaperPath();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETTINGCHANGE)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string? newPath = wallpaperManager.GetWallpaperPath();
                    if (newPath != lastWallpaperPath)
                    {
                        lastWallpaperPath = newPath;
                        wallpaperManager.SetWallpaperBackground();
                    }
                }), DispatcherPriority.Background);
            }

            return IntPtr.Zero;
        }

        #endregion

        #region Thumbnail Keepalive

        private void ThumbnailKeepAlive_Tick(object? sender, EventArgs e)
        {
            if (this.Visibility == Visibility.Visible && windowThumbs.Count > 0)
            {
                foreach (var thumb in windowThumbs)
                {
                    if (thumb.ThumbnailHandle != IntPtr.Zero)
                    {
                        SIZE size;
                        DwmQueryThumbnailSourceSize(thumb.ThumbnailHandle, out size);
                    }
                }
            }
        }

        #endregion

        #region Keyboard Hook

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule?.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    UpdateModifierKeyState(vkCode, true);

                    if (vkCode == configuredHotkey.VirtualKeyCode && CheckModifiersMatch())
                    {
                        suppressStartMenu = true;
                        startMenuSuppressionStart = DateTime.Now;

                        this.Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            if (CanActivateScatter())
                            {
                                await StartScatterAsync();
                                await System.Threading.Tasks.Task.Delay(250);
                                ReleaseModifierKeys();
                            }
                        }));

                        return (IntPtr)1;
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                {
                    if ((vkCode == VK_LWIN || vkCode == VK_RWIN) && suppressStartMenu)
                    {
                        if ((DateTime.Now - startMenuSuppressionStart).TotalMilliseconds < 500)
                            return (IntPtr)1;

                        suppressStartMenu = false;
                    }

                    UpdateModifierKeyState(vkCode, false);
                }
            }

            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private void UpdateModifierKeyState(int vkCode, bool isPressed)
        {
            if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                isWinKeyPressed = isPressed;
            if (vkCode == VK_CONTROL)
                isCtrlPressed = isPressed;
            if (vkCode == VK_MENU)
                isAltPressed = isPressed;
            if (vkCode == VK_SHIFT)
                isShiftPressed = isPressed;
        }

        private bool CheckModifiersMatch()
        {
            bool winMatch = ((configuredHotkey.ModifierKeys & 0x08) != 0) == isWinKeyPressed;
            bool ctrlMatch = ((configuredHotkey.ModifierKeys & 0x01) != 0) == isCtrlPressed;
            bool altMatch = ((configuredHotkey.ModifierKeys & 0x02) != 0) == isAltPressed;
            bool shiftMatch = ((configuredHotkey.ModifierKeys & 0x04) != 0) == isShiftPressed;

            return winMatch && ctrlMatch && altMatch && shiftMatch;
        }

        private void ReleaseModifierKeys()
        {
            isWinKeyPressed = false;
            isCtrlPressed = false;
            isAltPressed = false;
            isShiftPressed = false;
            suppressStartMenu = false;

            keybd_event((byte)VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        #endregion

        #region Event Handlers

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (this.Visibility == Visibility.Visible && !animationManager.IsAnimating && !isTransitioning)
            {
                if (HandleScatterNavigationKey(e.Key))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape && !animationManager.IsAnimating && !isTransitioning)
            {
                e.Handled = true;
                CleanupAndClose();
            }
        }

        private bool HandleScatterNavigationKey(Key key)
        {
            switch (key)
            {
                case Key.Tab:
                    SelectNextWindow((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1);
                    return true;
                case Key.Left:
                    SelectWindowInDirection(-1, 0);
                    return true;
                case Key.Right:
                    SelectWindowInDirection(1, 0);
                    return true;
                case Key.Up:
                    SelectWindowInDirection(0, -1);
                    return true;
                case Key.Down:
                    SelectWindowInDirection(0, 1);
                    return true;
                case Key.Return:
                    if (selectedWindowHandle != IntPtr.Zero)
                        OnWindowClicked(selectedWindowHandle);
                    return true;
                default:
                    return false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            hwndSource?.RemoveHook(WndProc);
            wallpaperCheckTimer?.Stop();
            thumbnailKeepAliveTimer?.Stop();
            hotCornerManager?.Stop();
            desktopCaptureManager?.Cleanup();

            if (hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookID);
                hookID = IntPtr.Zero;
            }

            base.OnClosed(e);
        }

        #endregion

        #region Core Scatter Logic

#if DEBUG
        private const bool VERBOSE_DEBUG = true;
#else
        private const bool VERBOSE_DEBUG = false;
#endif

        private bool CanActivateScatter()
        {
            lock (transitionLock)
            {
                return !isScatterActive &&
                       !animationManager.IsAnimating &&
                       !isTransitioning;
            }
        }

        private void ForceWindowToTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            // Some foreground apps briefly reclaim Z-order, so assert topmost twice.
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private async System.Threading.Tasks.Task StartScatterAsync()
        {
            if (!CanActivateScatter())
            {
                // If window is already visible but not on top, force it
                if (this.Visibility == Visibility.Visible)
                {
                    var helper = new WindowInteropHelper(this);
                    IntPtr ourHwnd = helper.Handle;
                    if (ourHwnd != IntPtr.Zero)
                    {
                        ForceWindowToTop(ourHwnd);
                        this.Activate();
                        this.Focus();
                    }
                }
                return;
            }

            lock (transitionLock)
            {
                isScatterActive = true;
            }

            bool wasIdle = (DateTime.Now - lastActivationTime).TotalSeconds > IDLE_THRESHOLD_SECONDS;
            lastActivationTime = DateTime.Now;

            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr ourHwnd = helper.EnsureHandle();

                if (ourHwnd == IntPtr.Zero) return;

                var targetMonitorBounds = MonitorHelper.GetMonitorFromCursor();

                POINT pt;
                Win32Interop.GetCursorPos(out pt);

                this.WindowState = WindowState.Normal;
                this.Width = targetMonitorBounds.Width;
                this.Height = targetMonitorBounds.Height - 1;
                this.Left = targetMonitorBounds.Left;
                this.Top = targetMonitorBounds.Top;

                desktopCaptureManager.SetMonitorBounds(
                    targetMonitorBounds.Left,
                    targetMonitorBounds.Top,
                    targetMonitorBounds.Width,
                    targetMonitorBounds.Height
                );

                if (wasIdle) Cleanup();
                else animationManager.StopAllAnimations();

                var windowsOnTargetMonitor = windowEnumerator.EnumerateVisibleWindows(targetMonitorBounds);

                if (windowsOnTargetMonitor.Count == 0) return;

                var currentHandles = windowsOnTargetMonitor.Select(w => w.Handle).ToList();
                bool windowStateChanged = HasWindowStateChanged(currentHandles);

                List<WindowLayout> layouts;
                if (!windowStateChanged && cachedLayouts != null)
                    layouts = ReuseCachedLayouts(windowsOnTargetMonitor);
                else
                {
                    layouts = CalculateNewLayouts(windowsOnTargetMonitor, targetMonitorBounds.Width, targetMonitorBounds.Height);
                    cachedLayouts = new List<WindowLayout>(layouts);
                    lastWindowHandles = currentHandles;
                }

                // Create thumbnails while off-screen to avoid visible setup flicker.
                SetWindowPos(ourHwnd, HWND_TOPMOST, -30000, 0,
                    (int)targetMonitorBounds.Width, (int)targetMonitorBounds.Height,
                    SWP_SHOWWINDOW | SWP_NOACTIVATE);

                this.Visibility = Visibility.Visible;

                await System.Threading.Tasks.Task.Delay(10);

                if (useDesktopCapture)
                {
                    bool desktopCaptured = await desktopCaptureManager.CaptureDesktopAsync();
                    if (!desktopCaptured)
                    {
                        useDesktopCapture = false;
                        wallpaperManager.SetWallpaperBackground();
                    }
                }
                else
                {
                    wallpaperManager.SetWallpaperBackground();
                }

                thumbnailManager.RegisterThumbnails(layouts, animationManager);
                SelectClosestWindowToPoint(pt.X - targetMonitorBounds.Left, pt.Y - targetMonitorBounds.Top);

                await System.Threading.Tasks.Task.Delay(16);

                SetWindowPos(ourHwnd, HWND_TOPMOST,
                    targetMonitorBounds.Left, targetMonitorBounds.Top,
                    (int)targetMonitorBounds.Width, (int)targetMonitorBounds.Height,
                    SWP_SHOWWINDOW);

                ForceWindowToTop(ourHwnd);

                this.InvalidateVisual();
                this.Activate();
                this.Focus();

                await System.Threading.Tasks.Task.Delay(10);
                ForceWindowToTop(ourHwnd);

                windowSwitcher = new WindowSwitcher(thumbnailManager, animationManager, windowThumbs, layouts, OnSwitchComplete);
                thumbnailKeepAliveTimer.Start();
                animationManager.StartScatterAnimation();
            }
            finally
            {
                lock (transitionLock) { isScatterActive = false; }
            }
        }

        private bool HasWindowStateChanged(List<IntPtr> currentHandles)
        {
            return lastWindowHandles == null ||
                   lastWindowHandles.Count != currentHandles.Count ||
                   !lastWindowHandles.SequenceEqual(currentHandles);
        }

        private List<WindowLayout> ReuseCachedLayouts(List<WindowInfo> windows)
        {
            var layouts = new List<WindowLayout>(cachedLayouts!);
            var windowsByHandle = windows.ToDictionary(w => w.Handle);

            foreach (var layout in layouts)
            {
                if (windowsByHandle.ContainsKey(layout.Window.Handle))
                {
                    layout.Window = windowsByHandle[layout.Window.Handle];
                }
            }

            return layouts;
        }

        private List<WindowLayout> CalculateNewLayouts(List<WindowInfo> windows, double screenW, double screenH)
        {
            var calculator = new WindowLayoutCalculator(rng);
            return calculator.CalculateLayouts(windows, screenW, screenH);
        }

        #endregion

        #region Window Click Handling

        private void OnWindowClicked(IntPtr windowHandle)
        {
            if (VERBOSE_DEBUG)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== CLICK DEBUG ===");
                sb.AppendLine($"Timestamp: {DateTime.Now:HH:mm:ss.fff}");
                sb.AppendLine($"Window: {windowHandle}");
                sb.AppendLine($"isScatterActive: {isScatterActive}");
                sb.AppendLine($"isAnimating: {animationManager.IsAnimating}");
                sb.AppendLine($"isTransitioning: {isTransitioning}");

                var thumb = windowThumbs.FirstOrDefault(t => t.WindowHandle == windowHandle);
                if (thumb != null)
                {
                    sb.AppendLine($"Border size: {thumb.ClickBorder.Width}x{thumb.ClickBorder.Height}");
                    sb.AppendLine($"Border pos: {Canvas.GetLeft(thumb.ClickBorder)},{Canvas.GetTop(thumb.ClickBorder)}");
                    sb.AppendLine($"Thumb size: {thumb.CurrentWidth}x{thumb.CurrentHeight}");
                    sb.AppendLine($"Thumb pos: {thumb.CurrentX},{thumb.CurrentY}");
                    sb.AppendLine($"IsHitTestVisible: {thumb.ClickBorder.IsHitTestVisible}");

                    double sizeDiff = Math.Abs(thumb.ClickBorder.Width - thumb.CurrentWidth) +
                                     Math.Abs(thumb.ClickBorder.Height - thumb.CurrentHeight);
                    double posDiff = Math.Abs(Canvas.GetLeft(thumb.ClickBorder) - thumb.CurrentX) +
                                    Math.Abs(Canvas.GetTop(thumb.ClickBorder) - thumb.CurrentY);

                    sb.AppendLine($"Size diff: {sizeDiff:F2}px");
                    sb.AppendLine($"Pos diff: {posDiff:F2}px");

                    if (sizeDiff > 1.0 || posDiff > 1.0)
                    {
                        sb.AppendLine("⚠️ WARNING: MISMATCH DETECTED!");
                    }
                }
                else
                {
                    sb.AppendLine("❌ ERROR: Thumb not found!");
                }

                System.Diagnostics.Debug.WriteLine(sb.ToString());
            }

            lock (transitionLock)
            {
                if (isScatterActive || animationManager.IsAnimating || isTransitioning)
                    return;

                isTransitioning = true;
            }

            try
            {
                thumbnailKeepAliveTimer.Stop();
                windowSwitcher!.SwitchToWindow(windowHandle, Dispatcher);
            }
            catch
            {
                lock (transitionLock)
                {
                    isTransitioning = false;
                }
            }
        }

        private void OnWindowHovered(IntPtr windowHandle)
        {
            if (this.Visibility == Visibility.Visible && !animationManager.IsAnimating && !isTransitioning)
                SelectWindow(windowHandle);
        }

        private void SelectClosestWindowToPoint(double x, double y)
        {
            var closest = windowThumbs
                .OrderBy(t => DistanceSquared(GetCenterX(t) - x, GetCenterY(t) - y))
                .FirstOrDefault();

            SelectWindow(closest?.WindowHandle ?? IntPtr.Zero);
        }

        private void SelectNextWindow(int direction)
        {
            if (windowThumbs.Count == 0) return;

            int currentIndex = windowThumbs.FindIndex(t => t.WindowHandle == selectedWindowHandle);
            if (currentIndex < 0) currentIndex = direction > 0 ? -1 : 0;

            int nextIndex = (currentIndex + direction + windowThumbs.Count) % windowThumbs.Count;
            SelectWindow(windowThumbs[nextIndex].WindowHandle);
        }

        private void SelectWindowInDirection(int dx, int dy)
        {
            if (windowThumbs.Count == 0) return;

            var current = windowThumbs.FirstOrDefault(t => t.WindowHandle == selectedWindowHandle) ?? windowThumbs[0];
            double currentX = GetCenterX(current);
            double currentY = GetCenterY(current);

            var candidate = windowThumbs
                .Where(t => t.WindowHandle != current.WindowHandle)
                .Select(t => new
                {
                    Thumb = t,
                    OffsetX = GetCenterX(t) - currentX,
                    OffsetY = GetCenterY(t) - currentY
                })
                .Where(c => (dx == 0 || Math.Sign(c.OffsetX) == dx) && (dy == 0 || Math.Sign(c.OffsetY) == dy))
                .OrderByDescending(c => dx != 0 ? Math.Abs(c.OffsetX) / Math.Max(1, Math.Abs(c.OffsetY)) : Math.Abs(c.OffsetY) / Math.Max(1, Math.Abs(c.OffsetX)))
                .ThenBy(c => DistanceSquared(c.OffsetX, c.OffsetY))
                .FirstOrDefault();

            if (candidate != null)
                SelectWindow(candidate.Thumb.WindowHandle);
            else
                SelectNextWindow(dx + dy > 0 ? 1 : -1);
        }

        private void SelectWindow(IntPtr windowHandle)
        {
            selectedWindowHandle = windowHandle;
            thumbnailManager?.SetSelectedWindow(windowHandle);
        }

        private static double GetCenterX(WindowThumb thumb) => thumb.TargetX + thumb.TargetWidth / 2.0;
        private static double GetCenterY(WindowThumb thumb) => thumb.TargetY + thumb.TargetHeight / 2.0;
        private static double DistanceSquared(double dx, double dy) => dx * dx + dy * dy;

        private void OnSwitchComplete()
        {
            this.Visibility = Visibility.Hidden;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    FullCleanup();
                }
                finally
                {
                    lock (transitionLock)
                    {
                        isTransitioning = false;
                        isScatterActive = false;
                    }
                }
            }), DispatcherPriority.Background);
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            thumbnailKeepAliveTimer?.Stop();
            animationManager?.StopAllAnimations();
            thumbnailManager?.CleanupAllThumbnails();
            selectedWindowHandle = IntPtr.Zero;
        }

        private void FullCleanup()
        {
            thumbnailKeepAliveTimer?.Stop();
            animationManager?.StopAllAnimations();
            thumbnailManager?.CleanupAllThumbnails();

            cachedLayouts = null;
            lastWindowHandles = null;
            selectedWindowHandle = IntPtr.Zero;

            desktopCaptureManager?.Cleanup();
        }

        private void CleanupAndClose()
        {
            lock (transitionLock)
            {
                if (animationManager.IsAnimating || isTransitioning)
                    return;

                isTransitioning = true;
            }

            thumbnailKeepAliveTimer.Stop();

            animationManager.StartReturnAnimation(async () =>
            {
                this.Visibility = Visibility.Hidden;

                await System.Threading.Tasks.Task.Delay(50);

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FullCleanup();
                    }
                    finally
                    {
                        lock (transitionLock)
                        {
                            isTransitioning = false;
                            isScatterActive = false;
                        }
                    }
                }), DispatcherPriority.Background);
            });
        }

        #endregion
    }
}
