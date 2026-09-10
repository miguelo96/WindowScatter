using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static WindowScatter.Win32Interop;

namespace WindowScatter
{
    /// <summary>
    /// Per-window DirectComposition visual backed by a live DWM thumbnail.
    /// All positions in physical pixels, monitor-relative.
    /// </summary>
    internal sealed class DCompThumb
    {
        public IntPtr WindowHandle;
        public IntPtr Visual;         // IDCompositionVisual*
        public IntPtr ScaleTransform; // IDCompositionScaleTransform*
        public IntPtr ThumbnailId;    // HTHUMBNAIL - never release

        public double OrigX, OrigY, OrigW, OrigH;
        public double SlotX, SlotY, SlotW, SlotH;
    }

    /// <summary>
    /// DirectComposition renderer: builds a visual tree where live window thumbnails
    /// are IDCompositionVisuals, and scatter/return transitions run as
    /// IDCompositionAnimation animations inside dwm.exe. The app sends one Commit per
    /// transition - there is no per-frame work in this process at all.
    /// </summary>
    internal sealed unsafe class DCompRenderer : IDisposable
    {
        private static readonly Guid IID_IDCompositionDevice3 = new Guid("0987CB06-F916-48BF-8D35-CE7641781BD9");

        private readonly IntPtr hostHwnd;
        private readonly IntPtr overlayHwnd;

        private IntPtr d3dDevice;
        private IntPtr dxgiDevice;
        private IntPtr device;       // IDCompositionDevice3* (usable as IDCompositionDevice*)
        private IntPtr target;       // IDCompositionTarget*
        private IntPtr rootVisual;   // IDCompositionVisual2*

        private IntPtr desktopVisual;

        private readonly List<DCompThumb> thumbs = new List<DCompThumb>();

        private IDCompositionDevice* Dev => (IDCompositionDevice*)device;
        private IDCompositionVisual* Root => (IDCompositionVisual*)rootVisual;

        private DCompRenderer(IntPtr hostHwnd, IntPtr overlayHwnd)
        {
            this.hostHwnd = hostHwnd;
            this.overlayHwnd = overlayHwnd;
        }

        public bool IsReady => device != IntPtr.Zero;

        /// <summary>Checks the private dwmapi ordinals exist on this system.</summary>
        public static bool ProbeSupported()
        {
            IntPtr dwmapi = LoadLibraryW("dwmapi.dll");
            if (dwmapi == IntPtr.Zero) return false;
            return GetProcAddress(dwmapi, (IntPtr)147) != IntPtr.Zero
                && GetProcAddress(dwmapi, (IntPtr)163) != IntPtr.Zero
                && GetProcAddress(dwmapi, (IntPtr)164) != IntPtr.Zero;
        }

        public static DCompRenderer? TryCreate(IntPtr hostHwnd, IntPtr overlayHwnd)
        {
            var renderer = new DCompRenderer(hostHwnd, overlayHwnd);
            return renderer.Initialize() ? renderer : null;
        }

        private bool Initialize()
        {
            // D3D_DRIVER_TYPE_HARDWARE = 1, D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                out d3dDevice, IntPtr.Zero, IntPtr.Zero);
            if (hr != 0) return false;

            var IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dDevice, ref IID_IDXGIDevice, out dxgiDevice);
            if (hr != 0) return false;

            var iid = IID_IDCompositionDevice3;
            hr = DCompositionCreateDevice3(dxgiDevice, ref iid, out device);
            if (hr != 0 || device == IntPtr.Zero) return false;

            IDCompositionTarget* tgt;
            if (Dev->CreateTargetForHwnd((HWND)hostHwnd, false, &tgt).Value != 0) return false;
            target = (IntPtr)tgt;

            IDCompositionVisual* root;
            if (Dev->CreateVisual(&root).Value != 0) return false;
            rootVisual = (IntPtr)root;

            tgt->SetRoot(root);
            return Dev->Commit().Value == 0;
        }

        /// <summary>
        /// Builds the full scene: desktop visual at the bottom, one live thumbnail
        /// visual per window above it. Windows start at their real positions.
        /// </summary>
        public bool BuildScene(List<WindowLayout> layouts, MonitorHelper.MonitorBounds monitor)
        {
            if (!IsReady) return false;

            IntPtr dwmapi = LoadLibraryW("dwmapi.dll");
            var createThumb = Marshal.GetDelegateForFunctionPointer<DwmpCreateSharedThumbnailVisualDelegate>(
                GetProcAddress(dwmapi, (IntPtr)147));
            var createMulti = Marshal.GetDelegateForFunctionPointer<DwmpCreateSharedMultiWindowVisualDelegate>(
                GetProcAddress(dwmapi, (IntPtr)163));
            var updateMulti = Marshal.GetDelegateForFunctionPointer<DwmpUpdateSharedMultiWindowVisualDelegate>(
                GetProcAddress(dwmapi, (IntPtr)164));

            // --- desktop background visual (everything except our two windows) ---
            int hr = createMulti(hostHwnd, device, out IntPtr deskVisPtr, out IntPtr desktopId);
            if (hr != 0 || deskVisPtr == IntPtr.Zero)
                return false;

            desktopVisual = deskVisPtr;

            var exclude = new[] { hostHwnd, overlayHwnd };
            var srcRect = new Win32Interop.RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom };
            var destSize = new Win32Interop.SIZE { cx = monitor.Width, cy = monitor.Height };
            updateMulti(desktopId, null, 0, exclude, exclude.Length, out srcRect, out destSize, 1);

            Root->AddVisual((IDCompositionVisual*)desktopVisual, false, null);

            // --- one visual per window, in layout order (back to front) ---
            foreach (var layout in layouts)
            {
                var win = layout.Window;
                int w = win.OriginalRect.Right - win.OriginalRect.Left;
                int h = win.OriginalRect.Bottom - win.OriginalRect.Top;

                var props = new Win32Interop.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY
                            | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_ENABLE3D,
                    rcDestination = new Win32Interop.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
                    rcSource = new Win32Interop.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = false
                };

                int createHr = createThumb(hostHwnd, win.Handle, 2, ref props, device,
                    out IntPtr visual, out IntPtr thumbId);
                if (createHr != 0 || visual == IntPtr.Zero)
                    continue;

                IDCompositionVisual* vis = (IDCompositionVisual*)visual;
                Root->AddVisual(vis, true, null);

                IDCompositionScaleTransform* scale;
                IntPtr scalePtr = IntPtr.Zero;
                if (Dev->CreateScaleTransform(&scale).Value == 0)
                    scalePtr = (IntPtr)scale;

                var thumb = new DCompThumb
                {
                    WindowHandle = win.Handle,
                    Visual = visual,
                    ScaleTransform = scalePtr,
                    ThumbnailId = thumbId,
                    OrigX = win.OriginalRect.Left,
                    OrigY = win.OriginalRect.Top,
                    OrigW = w,
                    OrigH = h,
                    SlotX = layout.X,
                    SlotY = layout.Y,
                    SlotW = layout.Width,
                    SlotH = layout.Height
                };

                // Start fully at original position/size.
                vis->SetOffsetX((float)thumb.OrigX);
                vis->SetOffsetY((float)thumb.OrigY);
                if (scalePtr != IntPtr.Zero)
                    vis->SetTransform((IDCompositionTransform*)scalePtr);

                thumbs.Add(thumb);
            }

            return Dev->Commit().Value == 0;
        }

        private IDCompositionAnimation* CubicEase(double from, double to, double durationSec)
        {
            IDCompositionAnimation* anim;
            if (Dev->CreateAnimation(&anim).Value != 0) return null;

            double delta = to - from;
            double t = durationSec <= 0 ? 0.0001 : durationSec;

            // value(u) = from + delta * (1 - (1-u)^3), u in [0,1]
            anim->AddCubic(0.0, (float)from,
                (float)(3 * delta / t),
                (float)(-3 * delta / (t * t)),
                (float)(delta / (t * t * t)));
            anim->End(t, (float)to);
            return anim;
        }

        /// <summary>Animates every thumbnail from original rect to scatter slot. One commit.</summary>
        public void AnimateScatter(double durationSec)
        {
            foreach (var thumb in thumbs)
            {
                IDCompositionVisual* vis = (IDCompositionVisual*)thumb.Visual;

                var animX = CubicEase(thumb.OrigX, thumb.SlotX, durationSec);
                var animY = CubicEase(thumb.OrigY, thumb.SlotY, durationSec);
                if (animX != null) vis->SetOffsetX(animX);
                if (animY != null) vis->SetOffsetY(animY);

                if (thumb.ScaleTransform != IntPtr.Zero)
                {
                    IDCompositionScaleTransform* sc = (IDCompositionScaleTransform*)thumb.ScaleTransform;
                    var sx = CubicEase(1.0, thumb.SlotW / thumb.OrigW, durationSec);
                    var sy = CubicEase(1.0, thumb.SlotH / thumb.OrigH, durationSec);
                    if (sx != null) sc->SetScaleX(sx);
                    if (sy != null) sc->SetScaleY(sy);
                }
            }

            Dev->Commit();
        }

        /// <summary>Animates every thumbnail back to its original rect. One commit.</summary>
        public void AnimateReturn(double durationSec)
        {
            foreach (var thumb in thumbs)
            {
                IDCompositionVisual* vis = (IDCompositionVisual*)thumb.Visual;

                var animX = CubicEase(thumb.SlotX, thumb.OrigX, durationSec);
                var animY = CubicEase(thumb.SlotY, thumb.OrigY, durationSec);
                if (animX != null) vis->SetOffsetX(animX);
                if (animY != null) vis->SetOffsetY(animY);

                if (thumb.ScaleTransform != IntPtr.Zero)
                {
                    IDCompositionScaleTransform* sc = (IDCompositionScaleTransform*)thumb.ScaleTransform;
                    var sx = CubicEase(thumb.SlotW / thumb.OrigW, 1.0, durationSec);
                    var sy = CubicEase(thumb.SlotH / thumb.OrigH, 1.0, durationSec);
                    if (sx != null) sc->SetScaleX(sx);
                    if (sy != null) sc->SetScaleY(sy);
                }
            }

            Dev->Commit();
        }

        public void BringThumbToFront(IntPtr windowHandle)
        {
            foreach (var thumb in thumbs)
            {
                if (thumb.WindowHandle == windowHandle)
                {
                    IDCompositionVisual* vis = (IDCompositionVisual*)thumb.Visual;
                    Root->RemoveVisual(vis);
                    Root->AddVisual(vis, true, null);
                    Dev->Commit();
                    return;
                }
            }
        }

        /// <summary>Releases all thumbnail/desktop visuals. Device and target persist.</summary>
        public void ClearScene()
        {
            if (!IsReady) return;

            Root->RemoveAllVisuals();
            Dev->Commit();

            foreach (var thumb in thumbs)
            {
                Marshal.Release(thumb.Visual);
                if (thumb.ScaleTransform != IntPtr.Zero)
                    Marshal.Release(thumb.ScaleTransform);
            }
            thumbs.Clear();

            if (desktopVisual != IntPtr.Zero)
            {
                Marshal.Release(desktopVisual);
                desktopVisual = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            ClearScene();
            if (rootVisual != IntPtr.Zero) Marshal.Release(rootVisual);
            if (target != IntPtr.Zero) Marshal.Release(target);
            if (device != IntPtr.Zero) Marshal.Release(device);
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
            device = IntPtr.Zero;
        }
    }

}
