using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class WindowAnimationManager
    {
        // Apple's Mission Control default expose duration is 0.25s; power users
        // drop it to ~0.1s. 0.20s reads as instant yet still trackable.
        private const double ANIMATION_DURATION = 0.20;

        private readonly List<WindowThumb> windowThumbs;
        private readonly Dispatcher uiDispatcher;
        private DWM_THUMBNAIL_PROPERTIES sharedProps;

        // The animation runs on a dedicated worker thread paced by DwmFlush, so it
        // tracks the display's actual vsync (60/120/144Hz) instead of the ~64Hz
        // DispatcherTimer quantum. Thread.Start gives the worker visibility of
        // everything written before it; all UI-thread work happens in
        // FinishAnimation via the dispatcher.
        private Thread? animThread;
        private volatile bool animRunning;
        private volatile bool isReturningToOriginal;
        private int animGen;
        private long animStartTimestamp;
        private Action? onCompleteCallback;

        // Active DWM keep-alive: keeps DWM composing and GPU clocks warm while the
        // overlay sits idle, matching the effect of background video playback.
        private Thread? keepAliveThread;
        private volatile bool keepAliveRunning;
        private int keepAliveGen;

        // Set WINDOWSCATTER_FRAMELOG=1 to dump per-frame timing CSVs to %TEMP%\WindowScatter.
        private static readonly bool FRAME_LOG_ENABLED =
            Environment.GetEnvironmentVariable("WINDOWSCATTER_FRAMELOG") == "1";
        private List<double>? frameTimes;
        private long lastTickTimestamp;
        private string frameLogPhase = "scatter";
        private int frameLogGen0;
        private int frameLogGen1;
        private int frameLogGen2;
        private double frameLogTotalMemMB;
        private double frameLogStartWsMb;
        private double frameLogStartPrivMb;
        private double returnStartDelayMs = -1;

        public bool IsAnimating => animRunning;

        /// <summary>
        /// Invoked on the UI thread after the scatter animation settles.
        /// Used to re-sync WPF hit-test targets with the live DWM positions (#10).
        /// </summary>
        public Action? OnScatterComplete { get; set; }

        public WindowAnimationManager(List<WindowThumb> windowThumbs)
        {
            this.windowThumbs = windowThumbs;
            this.uiDispatcher = Dispatcher.CurrentDispatcher; // constructed on the UI thread

            sharedProps = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
                fVisible = true,
                fSourceClientAreaOnly = true
            };
        }

        public void StartScatterAnimation()
        {
            StopKeepAlive();
            StopAnimThread();
            isReturningToOriginal = false;
            animStartTimestamp = 0;
            BeginFrameLog("scatter");

            foreach (var thumb in windowThumbs)
            {
                if (thumb.ClickBorder != null) thumb.ClickBorder.IsHitTestVisible = false;
                if (thumb.TitleLabel != null) thumb.TitleLabel.Opacity = 0;
            }

            StartAnimThread();
        }

        public void StartReturnAnimation(Action onComplete)
        {
            long entryTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            StopKeepAlive();
            StopAnimThread();
            isReturningToOriginal = true;
            onCompleteCallback = onComplete;

            BeginFrameLog("return");

            foreach (var thumb in windowThumbs)
            {
                if (thumb.ClickBorder != null)
                    thumb.ClickBorder.IsHitTestVisible = false;
                if (thumb.TitleLabel != null)
                    thumb.TitleLabel.Opacity = 0;
            }

            foreach (var thumb in windowThumbs)
            {
                double tempX = thumb.StartX;
                double tempY = thumb.StartY;
                double tempW = thumb.StartWidth;
                double tempH = thumb.StartHeight;

                thumb.StartX = thumb.TargetX;
                thumb.StartY = thumb.TargetY;
                thumb.StartWidth = thumb.TargetWidth;
                thumb.StartHeight = thumb.TargetHeight;

                thumb.TargetX = tempX;
                thumb.TargetY = tempY;
                thumb.TargetWidth = tempW;
                thumb.TargetHeight = tempH;

                thumb.CurrentX = thumb.StartX;
                thumb.CurrentY = thumb.StartY;
                thumb.CurrentWidth = thumb.StartWidth;
                thumb.CurrentHeight = thumb.StartHeight;
            }

            animStartTimestamp = 0;
            lastTickTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            returnStartDelayMs = (lastTickTimestamp - entryTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            StartAnimThread();
        }

        public void StopAllAnimations()
        {
            StopKeepAlive();
            StopAnimThread();
            FlushFrameLog("stopped");
            isReturningToOriginal = false;
        }

        public void StartKeepAlive()
        {
            StopKeepAlive();
            int gen = ++keepAliveGen;
            keepAliveRunning = true;
            var thread = new Thread(() => KeepAliveLoop(gen))
            {
                IsBackground = true,
                Name = "WindowScatterDwmKeepAlive",
                Priority = ThreadPriority.Normal
            };
            keepAliveThread = thread;
            thread.Start();
        }

        public void StopKeepAlive()
        {
            keepAliveGen++;
            keepAliveRunning = false;
            var thread = keepAliveThread;
            keepAliveThread = null;
            if (thread != null && Thread.CurrentThread != thread && thread.IsAlive)
            {
                thread.Join(100);
            }
        }

        private void KeepAliveLoop(int gen)
        {
            bool toggle = false;
            while (keepAliveRunning && gen == keepAliveGen)
            {
                toggle = !toggle;
                byte opacity = toggle ? (byte)254 : (byte)255;

                var thumbs = windowThumbs.ToArray();
                for (int i = 0; i < thumbs.Length; i++)
                {
                    var thumb = thumbs[i];
                    if (thumb.ThumbnailHandle != IntPtr.Zero)
                    {
                        UpdateThumbnailPosition(thumb, opacity);
                    }
                }

                // Keeps DWM composing at native vsync with near-zero CPU usage.
                DwmFlush();
            }
        }

        public void UpdateThumbnailPosition(WindowThumb thumb, byte opacity = 255)
        {
            if (thumb.ThumbnailHandle == IntPtr.Zero)
                return;

            var props = sharedProps;
            props.opacity = opacity;
            props.rcDestination.Left = (int)Math.Round(thumb.CurrentX);
            props.rcDestination.Top = (int)Math.Round(thumb.CurrentY);
            props.rcDestination.Right = (int)Math.Round(thumb.CurrentX + thumb.CurrentWidth);
            props.rcDestination.Bottom = (int)Math.Round(thumb.CurrentY + thumb.CurrentHeight);

            DwmUpdateThumbnailProperties(thumb.ThumbnailHandle, ref props);
        }

        private void StartAnimThread()
        {
            int gen = ++animGen;
            animRunning = true;
            var thread = new Thread(() => AnimLoop(gen))
            {
                IsBackground = true,
                Name = "WindowScatterAnim",
                Priority = ThreadPriority.Highest
            };
            animThread = thread;
            thread.Start();
        }

        private void StopAnimThread()
        {
            animGen++;
            animRunning = false;
            var thread = animThread;
            animThread = null;
            if (thread != null && Thread.CurrentThread != thread && thread.IsAlive)
                thread.Join();
        }

        private void AnimLoop(int gen)
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            for (;;)
            {
                long iterStart = System.Diagnostics.Stopwatch.GetTimestamp();
                if (animStartTimestamp == 0)
                    animStartTimestamp = iterStart;

                double elapsed = (iterStart - animStartTimestamp) / (double)freq;
                double progress = Math.Min(elapsed / ANIMATION_DURATION, 1.0);
                double e = 1 - Math.Pow(1 - progress, 3);

                var thumbs = windowThumbs.ToArray();
                foreach (var thumb in thumbs)
                {
                    double aspect = thumb.StartHeight > 0
                        ? thumb.StartWidth / thumb.StartHeight
                        : 1.0;
                    if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0)
                        aspect = 1.0;

                    thumb.CurrentX = Lerp(thumb.StartX, thumb.TargetX, e);
                    thumb.CurrentY = Lerp(thumb.StartY, thumb.TargetY, e);
                    thumb.CurrentWidth = Lerp(thumb.StartWidth, thumb.TargetWidth, e);
                    thumb.CurrentHeight = thumb.CurrentWidth / aspect;

                    UpdateThumbnailPosition(thumb);
                }

                RecordFrame();

                // Blocks until DWM presents this frame: paces directly to display vsync.
                DwmFlush();

                if (progress >= 1.0)
                    break;
                if (!animRunning)
                    return;
            }

            animRunning = false;
            uiDispatcher.BeginInvoke(new Action(() =>
            {
                if (gen != animGen)
                    return;
                FinishAnimation();
            }));
        }

        // Runs on the UI thread: WPF touches and the dismiss callback live here.
        private void FinishAnimation()
        {
            if (isReturningToOriginal)
            {
                isReturningToOriginal = false;
                FlushFrameLog("return");
                onCompleteCallback?.Invoke();
                onCompleteCallback = null;
            }
            else
            {
                FlushFrameLog("scatter");
                foreach (var thumb in windowThumbs)
                {
                    if (thumb.ClickBorder != null) thumb.ClickBorder.IsHitTestVisible = true;
                    if (thumb.TitleLabel != null) thumb.TitleLabel.Opacity = 1;
                    UpdateThumbnailPosition(thumb);
                }

                try { OnScatterComplete?.Invoke(); } catch { }

                // Scatter complete: keep DWM warm and ready while the overlay sits idle.
                StartKeepAlive();
            }
        }

        private void BeginFrameLog(string phase)
        {
            if (!FRAME_LOG_ENABLED) return;
            frameTimes = new List<double>(512);
            frameLogPhase = phase;
            frameLogGen0 = GC.CollectionCount(0);
            frameLogGen1 = GC.CollectionCount(1);
            frameLogGen2 = GC.CollectionCount(2);
            frameLogTotalMemMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                proc.Refresh();
                frameLogStartWsMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                frameLogStartPrivMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0);
            }
            catch { }
            lastTickTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private void RecordFrame()
        {
            if (frameTimes == null) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            frameTimes.Add((now - lastTickTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            lastTickTimestamp = now;
        }

        private void FlushFrameLog(string completionPhase)
        {
            if (frameTimes == null) return;

            try
            {
                if (frameTimes.Count > 0)
                {
                    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WindowScatter");
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir,
                        $"frames-{frameLogPhase}-{completionPhase}-{DateTime.Now:HHmmss-fff}.csv");

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("frame,delta_ms");
                    double sum = 0, max = 0;
                    int over33 = 0;
                    for (int i = 0; i < frameTimes.Count; i++)
                    {
                        double d = frameTimes[i];
                        sb.Append(i).Append(',').AppendLine(d.ToString("F2"));
                        sum += d;
                        if (d > max) max = d;
                        if (d > 33) over33++;
                    }
                    sb.AppendLine($"# frames={frameTimes.Count} avg_ms={(sum / frameTimes.Count):F2} " +
                                  $"max_ms={max:F2} frames_over_33ms={over33}");
                    sb.AppendLine($"# gc_during_anim gen0={GC.CollectionCount(0) - frameLogGen0} " +
                                   $"gen1={GC.CollectionCount(1) - frameLogGen1} " +
                                   $"gen2={GC.CollectionCount(2) - frameLogGen2} " +
                                   $"end_mem_mb={(GC.GetTotalMemory(false) / (1024.0 * 1024.0)):F1} " +
                                   $"start_mem_mb={frameLogTotalMemMB:F1}");
                    try
                    {
                        var proc = System.Diagnostics.Process.GetCurrentProcess();
                        proc.Refresh();
                        sb.AppendLine($"# mem start_ws_mb={frameLogStartWsMb:F1} " +
                                      $"start_priv_mb={frameLogStartPrivMb:F1} " +
                                      $"end_ws_mb={(proc.WorkingSet64 / (1024.0 * 1024.0)):F1} " +
                                      $"end_priv_mb={(proc.PrivateMemorySize64 / (1024.0 * 1024.0)):F1}");
                    }
                    catch { }
                    if (frameLogPhase == "return" && returnStartDelayMs >= 0)
                        sb.AppendLine($"# return_start_delay_ms={returnStartDelayMs:F1} (dismiss intent -> first frame)");
                    System.IO.File.WriteAllText(path, sb.ToString());
                }
            }
            catch { }

            frameTimes = null;
        }

        private static double Lerp(double start, double end, double t) => start + (end - start) * t;
    }
}
