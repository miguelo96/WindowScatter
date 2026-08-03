using System;
using System.Collections.Generic;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Windows.Threading;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class WindowAnimationManager
    {
        private const double ANIMATION_DURATION = 0.25;
        private const double TARGET_BLUR_RADIUS = 40;
        private const int TARGET_FPS = 500;
        private const double FRAME_TIME_MS = 1000.0 / TARGET_FPS;

        private readonly Image backgroundImage;
        private readonly Border blurOverlay;
        private readonly List<WindowThumb> windowThumbs;
        private BlurEffect blurEffect;
        private DWM_THUMBNAIL_PROPERTIES sharedProps;

        private DesktopCaptureManager? desktopCaptureManager;

        private DispatcherTimer animationTimer;
        private DateTime animStart;
        private DateTime lastFrameTime = DateTime.MinValue;
        private double currentBlurRadius = 0;
        private bool isReturningToOriginal = false;
        private Action? onCompleteCallback;

        public bool IsAnimating => animationTimer?.IsEnabled ?? false;

        public WindowAnimationManager(Image backgroundImage, Border blurOverlay, List<WindowThumb> windowThumbs)
        {
            this.backgroundImage = backgroundImage;
            this.blurOverlay = blurOverlay;
            this.windowThumbs = windowThumbs;

            this.blurEffect = new BlurEffect
            {
                Radius = 0,
                RenderingBias = RenderingBias.Performance,
                KernelType = KernelType.Gaussian
            };
            this.blurOverlay.Effect = this.blurEffect;

            sharedProps = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
                fVisible = true,
                fSourceClientAreaOnly = true
            };

            animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(FRAME_TIME_MS)
            };
            animationTimer.Tick += AnimationTimer_Tick;
        }

        public void SetDesktopCaptureManager(DesktopCaptureManager manager)
        {
            this.desktopCaptureManager = manager;
        }

        public void StartScatterAnimation()
        {
            animStart = DateTime.Now;
            lastFrameTime = DateTime.MinValue;
            currentBlurRadius = 0;

            if (blurOverlay.Effect != blurEffect)
                blurOverlay.Effect = blurEffect;

            foreach (var thumb in windowThumbs)
            {
                if (thumb.ClickBorder != null) thumb.ClickBorder.IsHitTestVisible = false;
                if (thumb.TitleLabel != null) thumb.TitleLabel.Opacity = 0;
            }

            animationTimer.Start();
        }

        public void StartReturnAnimation(Action onComplete)
        {
            isReturningToOriginal = true;
            onCompleteCallback = onComplete;

            // Changing a full-screen Gaussian blur every frame can stall badly after
            // the overlay has been idle. Drop it before moving thumbnails back.
            currentBlurRadius = 0;
            if (blurEffect != null) blurEffect.Radius = 0;
            desktopCaptureManager?.SetBlur(0);

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

            animStart = DateTime.Now;
            lastFrameTime = DateTime.MinValue;

            animationTimer.Start();
        }

        public void StopAllAnimations()
        {
            animationTimer?.Stop();
            isReturningToOriginal = false;
            currentBlurRadius = 0;

            if (blurEffect != null)
                blurEffect.Radius = 0;

            desktopCaptureManager?.SetBlur(0);
        }

        public void UpdateThumbnailPosition(WindowThumb thumb)
        {
            if (thumb.ThumbnailHandle == IntPtr.Zero)
                return;

            sharedProps.opacity = 255;
            sharedProps.rcDestination.Left = (int)Math.Round(thumb.CurrentX);
            sharedProps.rcDestination.Top = (int)Math.Round(thumb.CurrentY);
            sharedProps.rcDestination.Right = (int)Math.Round(thumb.CurrentX + thumb.CurrentWidth);
            sharedProps.rcDestination.Bottom = (int)Math.Round(thumb.CurrentY + thumb.CurrentHeight);

            DwmUpdateThumbnailProperties(thumb.ThumbnailHandle, ref sharedProps);
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            if (lastFrameTime != DateTime.MinValue && (now - lastFrameTime).TotalMilliseconds < FRAME_TIME_MS * 0.8) return;
            lastFrameTime = now;

            double elapsed = (now - animStart).TotalSeconds;
            double progress = Math.Min(elapsed / ANIMATION_DURATION, 1.0);
            double eased = 1 - Math.Pow(1 - progress, 3);

            if (isReturningToOriginal)
            {
                byte opacity = 255;
                if (progress > 0.80)
                {
                    double fadeProgress = (progress - 0.80) / 0.20;
                    opacity = (byte)(255 * (1.0 - fadeProgress));
                }
                sharedProps.opacity = opacity;
            }
            else
            {
                currentBlurRadius = Lerp(0, TARGET_BLUR_RADIUS, eased);
                if (blurEffect != null) blurEffect.Radius = currentBlurRadius;
                desktopCaptureManager?.SetBlur(currentBlurRadius);

                sharedProps.opacity = 255;
            }

            foreach (var thumb in windowThumbs)
            {
                thumb.CurrentX = Lerp(thumb.StartX, thumb.TargetX, eased);
                thumb.CurrentY = Lerp(thumb.StartY, thumb.TargetY, eased);
                thumb.CurrentWidth = Lerp(thumb.StartWidth, thumb.TargetWidth, eased);
                thumb.CurrentHeight = Lerp(thumb.StartHeight, thumb.TargetHeight, eased);

                UpdateThumbnailPosition(thumb);
            }

            if (progress >= 1.0)
            {
                animationTimer.Stop();

                if (isReturningToOriginal)
                {
                    isReturningToOriginal = false;
                    sharedProps.opacity = 255;
                    onCompleteCallback?.Invoke();
                    onCompleteCallback = null;
                }
                else
                {
                    foreach (var thumb in windowThumbs)
                    {
                        if (thumb.ClickBorder != null) thumb.ClickBorder.IsHitTestVisible = true;
                        if (thumb.TitleLabel != null) thumb.TitleLabel.Opacity = 1;
                        UpdateThumbnailPosition(thumb);
                    }
                }
            }
        }

        private static double Lerp(double start, double end, double t) => start + (end - start) * t;
    }
}
