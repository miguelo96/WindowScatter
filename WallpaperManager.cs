using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WindowScatter
{
    internal class WallpaperManager
    {
        private readonly Image backgroundImage;
        private readonly Window parentWindow;

        public WallpaperManager(Image backgroundImage, Window parentWindow)
        {
            this.backgroundImage = backgroundImage;
            this.parentWindow = parentWindow;
        }

        public void SetWallpaperBackground()
        {
            try
            {
                string? wallpaperPath = GetWallpaperPath();

                if (!string.IsNullOrEmpty(wallpaperPath) && File.Exists(wallpaperPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(wallpaperPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    double screenWidth = GetOverlayWidth();
                    double screenHeight = GetOverlayHeight();

                    double imageWidth = bitmap.PixelWidth;
                    double imageHeight = bitmap.PixelHeight;

                    double scaleX = screenWidth / imageWidth;
                    double scaleY = screenHeight / imageHeight;
                    double scale = Math.Max(scaleX, scaleY);

                    double scaledWidth = imageWidth * scale;
                    double scaledHeight = imageHeight * scale;

                    double offsetX = (screenWidth - scaledWidth) / 2.0;
                    double offsetY = (screenHeight - scaledHeight) / 2.0;

                    backgroundImage.Width = scaledWidth;
                    backgroundImage.Height = scaledHeight;
                    backgroundImage.Stretch = Stretch.Fill;
                    backgroundImage.Source = bitmap;

                    Canvas.SetLeft(backgroundImage, offsetX);
                    Canvas.SetTop(backgroundImage, offsetY);

                    backgroundImage.Effect = null;
                }
                else
                {
                    parentWindow.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
            }
            catch
            {
                parentWindow.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            }
        }

        /// <summary>
        /// Overlay size in DIPs (i.e. the target monitor size). Falls back to the
        /// primary screen when the window has no size yet.
        /// </summary>
        private double GetOverlayWidth()
        {
            double w = parentWindow.ActualWidth;
            if (w <= 0) w = parentWindow.Width;
            if (double.IsNaN(w) || w <= 0) w = SystemParameters.PrimaryScreenWidth;
            return w;
        }

        private double GetOverlayHeight()
        {
            double h = parentWindow.ActualHeight;
            if (h <= 0) h = parentWindow.Height;
            if (double.IsNaN(h) || h <= 0) h = SystemParameters.PrimaryScreenHeight;
            return h;
        }

        public string? GetWallpaperPath()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        string? path = key.GetValue("WallPaper") as string;
                        if (!string.IsNullOrEmpty(path))
                            return path;
                    }
                }

                string transcodedPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\TranscodedWallpaper"
                );

                if (File.Exists(transcodedPath))
                    return transcodedPath;

                string cachedPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\CachedFiles"
                );

                if (Directory.Exists(cachedPath))
                {
                    var files = Directory.GetFiles(cachedPath);
                    if (files.Length > 0)
                        return files[0];
                }
            }
            catch { }

            return null;
        }
    }
}
