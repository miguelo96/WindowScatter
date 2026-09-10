using System;
using System.Windows.Controls;
using System.Windows.Shapes;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    internal class WindowInfo
    {
        public IntPtr Handle;
        public string Title = string.Empty;
        public RECT OriginalRect;
        public bool WasMaximized;
    }

    public class WindowThumb
    {
        public IntPtr WindowHandle { get; set; }
        public IntPtr ThumbnailHandle { get; set; }
        public bool WasMinimized { get; set; }
        public Border ClickBorder { get; set; } = null!;
        public Border HighlightBorder { get; set; } = null!;
        public TextBlock TitleLabel { get; set; } = null!;

        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartWidth { get; set; }
        public double StartHeight { get; set; }

        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public double TargetWidth { get; set; }
        public double TargetHeight { get; set; }

        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public double CurrentWidth { get; set; }
        public double CurrentHeight { get; set; }
    }

    internal class WindowLayout
    {
        public WindowInfo Window = null!;
        public double Width;
        public double Height;
        public double X;
        public double Y;
        public double OrigWidth;
        public double OrigHeight;
    }
}
