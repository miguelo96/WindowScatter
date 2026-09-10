using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class ThumbnailManager
    {
        private readonly Canvas scatterCanvas;
        private readonly List<WindowThumb> windowThumbs;
        private readonly IntPtr ownerWindowHandle;
        private readonly Action<IntPtr> onWindowClicked;
        private readonly Action<IntPtr> onWindowHovered;

        public ThumbnailManager(Canvas scatterCanvas, List<WindowThumb> windowThumbs,
            IntPtr ownerWindowHandle, Action<IntPtr> onWindowClicked, Action<IntPtr> onWindowHovered)
        {
            this.scatterCanvas = scatterCanvas;
            this.windowThumbs = windowThumbs;
            this.ownerWindowHandle = ownerWindowHandle;
            this.onWindowClicked = onWindowClicked;
            this.onWindowHovered = onWindowHovered;
        }

        public void RegisterThumbnails(List<WindowLayout> layouts, WindowAnimationManager animationManager)
        {
            foreach (var layout in layouts)
            {
                var win = layout.Window;
                bool wasMinimized = IsIconic(win.Handle);

                IntPtr thumbHandle;
                int res = DwmRegisterThumbnail(ownerWindowHandle, win.Handle, out thumbHandle);

                if (res != 0 || thumbHandle == IntPtr.Zero)
                    continue;

                RECT currentRect = win.OriginalRect;
                double startX = currentRect.Left;
                double startY = currentRect.Top;
                double startW = currentRect.Right - currentRect.Left;
                double startH = currentRect.Bottom - currentRect.Top;

                var clickBorder = new Border
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    Width = layout.Width,
                    Height = layout.Height,
                    Cursor = Cursors.Hand,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(clickBorder, layout.X);
                Canvas.SetTop(clickBorder, layout.Y);

                var highlightBorder = new Border
                {
                    Width = layout.Width + 10,
                    Height = layout.Height + 10,
                    BorderThickness = new System.Windows.Thickness(3),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Opacity = 0,
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 18,
                        ShadowDepth = 0,
                        Opacity = 0.65,
                        Color = Colors.Black
                    }
                };

                Canvas.SetLeft(highlightBorder, layout.X - 5);
                Canvas.SetTop(highlightBorder, layout.Y - 5);

                var titleLabel = new TextBlock
                {
                    Text = TrimTitle(win.Title),
                    Width = Math.Max(140, layout.Width),
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    TextAlignment = System.Windows.TextAlignment.Center,
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 8,
                        ShadowDepth = 1,
                        Opacity = 0.9,
                        Color = Colors.Black
                    }
                };

                Canvas.SetLeft(titleLabel, layout.X + (layout.Width - titleLabel.Width) / 2);
                Canvas.SetTop(titleLabel, layout.Y + layout.Height + 8);

                var thumb = new WindowThumb
                {
                    WindowHandle = win.Handle,
                    ThumbnailHandle = thumbHandle,
                    WasMinimized = wasMinimized,
                    ClickBorder = clickBorder,
                    HighlightBorder = highlightBorder,
                    TitleLabel = titleLabel,

                    StartX = startX,
                    StartY = startY,
                    StartWidth = startW,
                    StartHeight = startH,

                    TargetX = layout.X,
                    TargetY = layout.Y,
                    TargetWidth = layout.Width,
                    TargetHeight = layout.Height,

                    CurrentX = startX,
                    CurrentY = startY,
                    CurrentWidth = startW,
                    CurrentHeight = startH
                };

                animationManager.UpdateThumbnailPosition(thumb);

                clickBorder.Tag = thumb.WindowHandle;
                clickBorder.MouseDown += ClickBorder_MouseDown;
                clickBorder.MouseEnter += ClickBorder_MouseEnter;

                scatterCanvas.Children.Add(highlightBorder);
                scatterCanvas.Children.Add(titleLabel);
                scatterCanvas.Children.Add(clickBorder);
                windowThumbs.Add(thumb);
            }
        }

        public void SetSelectedWindow(IntPtr windowHandle)
        {
            foreach (var thumb in windowThumbs)
            {
                bool isSelected = thumb.WindowHandle == windowHandle;

                if (thumb.HighlightBorder != null)
                    thumb.HighlightBorder.Opacity = isSelected ? 1 : 0;

                if (thumb.TitleLabel != null)
                    thumb.TitleLabel.FontSize = isSelected ? 14 : 13;
            }
        }

        public void ShowLabels(bool show)
        {
            foreach (var thumb in windowThumbs)
            {
                if (thumb.TitleLabel != null)
                    thumb.TitleLabel.Opacity = show ? 1 : 0;
            }
        }

        public void CleanupAllThumbnails()
        {
            foreach (var thumb in windowThumbs)
            {
                try
                {
                    if (thumb.ThumbnailHandle != IntPtr.Zero)
                        DwmUnregisterThumbnail(thumb.ThumbnailHandle);
                }
                catch { }
            }

            scatterCanvas.Children.Clear();
            windowThumbs.Clear();
        }

        public void BringThumbnailToFront(IntPtr windowHandle, WindowAnimationManager animationManager)
        {
            var clickedThumb = windowThumbs.FirstOrDefault(t => t.WindowHandle == windowHandle);
            if (clickedThumb == null)
                return;

            scatterCanvas.Children.Remove(clickedThumb.ClickBorder);
            scatterCanvas.Children.Remove(clickedThumb.HighlightBorder);
            scatterCanvas.Children.Remove(clickedThumb.TitleLabel);
            scatterCanvas.Children.Add(clickedThumb.HighlightBorder);
            scatterCanvas.Children.Add(clickedThumb.TitleLabel);
            scatterCanvas.Children.Add(clickedThumb.ClickBorder);

            windowThumbs.Remove(clickedThumb);
            windowThumbs.Add(clickedThumb);

            // DWM thumbnail order follows registration order, not canvas Z-order.
            if (clickedThumb.ThumbnailHandle != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(clickedThumb.ThumbnailHandle);
                clickedThumb.ThumbnailHandle = IntPtr.Zero;
            }

            IntPtr newThumb;
            if (DwmRegisterThumbnail(ownerWindowHandle, clickedThumb.WindowHandle, out newThumb) == 0)
            {
                clickedThumb.ThumbnailHandle = newThumb;
                animationManager.UpdateThumbnailPosition(clickedThumb);
            }
        }

        public void ReregisterAllThumbnails(WindowAnimationManager animationManager)
        {
            foreach (var thumb in windowThumbs)
            {
                if (thumb.ThumbnailHandle == IntPtr.Zero)
                {
                    IntPtr thumbHandle;
                    if (DwmRegisterThumbnail(ownerWindowHandle, thumb.WindowHandle, out thumbHandle) == 0)
                    {
                        thumb.ThumbnailHandle = thumbHandle;
                    }
                }

                animationManager.UpdateThumbnailPosition(thumb);
            }
        }

        private void ClickBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.Tag is IntPtr windowHandle)
            {
                onWindowClicked?.Invoke(windowHandle);
            }

            e.Handled = true;
        }

        private void ClickBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            var border = sender as Border;
            if (border?.Tag is IntPtr windowHandle)
            {
                onWindowHovered?.Invoke(windowHandle);
            }
        }

        private static string TrimTitle(string title)
        {
            title = title.Trim();
            return title.Length <= 80 ? title : title.Substring(0, 77) + "...";
        }
    }
}
