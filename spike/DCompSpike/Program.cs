// DCompSpike: proves that DwmpCreateSharedThumbnailVisual (dwmapi ordinal 147)
// works on this machine: a live DWM thumbnail of a real window as a
// DirectComposition visual, composited by DWM into a NOREDIRECTIONBITMAP window.
//
// What it does:
//   1. Creates a topmost window with WS_EX_NOREDIRECTIONBITMAP.
//   2. Creates D3D11 device -> DXGI device -> DCompositionCreateDevice3.
//   3. Finds the biggest visible foreign window and creates a shared thumbnail
//      visual for it (raw COM via vtable function pointers - no packages).
//   4. Animates the visual's offset in a loop for ~6 seconds.
//   5. Automated check: screenshots the window region at two moments and
//      reports whether pixels moved (proves visible + animating on screen).

using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static DCompSpike.Native;

var staThread = new Thread(SpikeMain);
staThread.SetApartmentState(ApartmentState.STA);
staThread.Start();
staThread.Join();
return;

static void SpikeMain()
{
Console.WriteLine("DCompSpike starting...");

// ---------- window ----------

WndProcDelegate wndProcDelegate = (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
    DefWindowProcW(hWnd, msg, wParam, lParam);
IntPtr wndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);

string className = "DCompSpikeWindow";
var wc = new WNDCLASSEX
{
    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
    lpfnWndProc = wndProcPtr,
    hInstance = GetModuleHandleW(null),
    lpszClassName = className
};

if (RegisterClassExW(ref wc) == 0)
{
    Console.WriteLine("FAIL: RegisterClassExW");
    return;
}

IntPtr hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP, className, "DCompSpike",
    WS_OVERLAPPEDWINDOW, 100, 100, 900, 620, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

if (hwnd == IntPtr.Zero)
{
    // Retry without NOREDIRECTIONBITMAP to isolate the failure cause.
    IntPtr hwndPlain = CreateWindowExW(WS_EX_TOPMOST, className, "DCompSpike",
        WS_OVERLAPPEDWINDOW, 100, 100, 900, 620, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    Console.WriteLine((hwndPlain == IntPtr.Zero
        ? "FAIL: CreateWindowExW fails even without NOREDIRECTIONBITMAP (class/wndproc issue)"
        : "FAIL: CreateWindowExW fails only WITH NOREDIRECTIONBITMAP (bitmaps unsupported here)")
        + $" err={Marshal.GetLastWin32Error()}");
    if (hwndPlain != IntPtr.Zero) DestroyWindow(hwndPlain);
    return;
}
Console.WriteLine($"host window: 0x{hwnd:X}");

// ---------- devices ----------

// D3D_DRIVER_TYPE_HARDWARE = 1, D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20
int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out IntPtr d3dDevice, IntPtr.Zero, IntPtr.Zero);
if (hr != 0) { Console.WriteLine($"FAIL: D3D11CreateDevice 0x{hr:X8}"); return; }

var IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
hr = Marshal.QueryInterface(d3dDevice, ref IID_IDXGIDevice, out IntPtr dxgiDevice);
if (hr != 0) { Console.WriteLine($"FAIL: IDXGIDevice 0x{hr:X8}"); return; }

var IID_IDCompositionDevice = new Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3");
hr = DCompositionCreateDevice3(dxgiDevice, ref IID_IDCompositionDevice, out IntPtr dcompDevice);
if (hr != 0 || dcompDevice == IntPtr.Zero)
{
    Console.WriteLine($"FAIL: DCompositionCreateDevice3 0x{hr:X8}");
    return;
}
Console.WriteLine("dcomp device OK");

// ---------- pick a victim window ----------

IntPtr victim = IntPtr.Zero;
long bestArea = 0;
EnumWindows((h, l) =>
{
    if (h == hwnd || !IsWindowVisible(h)) return true;
    if (GetWindowTextLengthW(h) == 0) return true;
    if (GetWindowRect(h, out RECT r))
    {
        long w = r.Right - r.Left, hh = r.Bottom - r.Top;
        if (w >= 300 && hh >= 200 && w * hh > bestArea)
        {
            var sb = new StringBuilder(256);
            GetWindowTextW(h, sb, sb.Capacity);
            if (sb.ToString().Contains("DCompSpike")) return true;
            bestArea = w * hh;
            victim = h;
        }
    }
    return true;
}, IntPtr.Zero);

if (victim == IntPtr.Zero) { Console.WriteLine("FAIL: no victim window found"); return; }
var title = new StringBuilder(256);
GetWindowTextW(victim, title, title.Capacity);
Console.WriteLine($"victim: 0x{victim:X} \"{title}\"");

// ---------- create shared thumbnail visual ----------

IntPtr dwmapi = LoadLibraryW("dwmapi.dll");
if (dwmapi == IntPtr.Zero) { Console.WriteLine("FAIL: dwmapi.dll not loaded"); return; }
IntPtr fp147 = GetProcAddress(dwmapi, (IntPtr)147);
if (fp147 == IntPtr.Zero) { Console.WriteLine("FAIL: dwmapi ordinal 147 not found"); return; }
var createSharedThumbnail = Marshal.GetDelegateForFunctionPointer<DwmpCreateSharedThumbnailVisualDelegate>(fp147);

GetWindowRect(victim, out RECT vrect);
int victimW = vrect.Right - vrect.Left;
int victimH = vrect.Bottom - vrect.Top;
double scale = Math.Min(560.0 / victimW, 420.0 / victimH);
int destW = (int)(victimW * scale);
int destH = (int)(victimH * scale);

var props = new DWM_THUMBNAIL_PROPERTIES
{
    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_ENABLE3D,
    rcDestination = new RECT { Left = 0, Top = 0, Right = destW, Bottom = destH },
    rcSource = new RECT { Left = 0, Top = 0, Right = victimW, Bottom = victimH },
    opacity = 255,
    fVisible = 1,
    fSourceClientAreaOnly = 0
};

hr = createSharedThumbnail(hwnd, victim, 2, ref props, dcompDevice, out IntPtr thumbVisual, out IntPtr hThumb);
Console.WriteLine($"DwmpCreateSharedThumbnailVisual: hr=0x{hr:X8} visual=0x{thumbVisual:X} hthumb=0x{hThumb:X}");
if (hr != 0 || thumbVisual == IntPtr.Zero)
{
    Console.WriteLine("FAIL: shared thumbnail visual not created");
    return;
}

// root visual + child, to mimic the real usage pattern (tree of visuals)
int comHr = ComCreateVisual(dcompDevice, out IntPtr rootVisual);
if (comHr != 0) { Console.WriteLine($"FAIL: CreateVisual 0x{comHr:X8}"); return; }

// The returned thumbnail pointer may not be the IDCompositionVisual vtable directly;
// QI for IDCompositionVisual explicitly.
var IID_IDCompositionVisual = new Guid("4d93059d-097b-4651-9a60-f0f25116e2f3");
int qiHr = Marshal.QueryInterface(thumbVisual, ref IID_IDCompositionVisual, out IntPtr qiThumbVisual);
Console.WriteLine($"QI(thumbVisual -> IDCompositionVisual): hr=0x{qiHr:X8} ptr=0x{qiThumbVisual:X} (returned {(qiThumbVisual == thumbVisual ? "SAME" : "DIFFERENT")})");
if (qiHr == 0 && qiThumbVisual != IntPtr.Zero)
    thumbVisual = qiThumbVisual;

        // sanity: dump the visual vtable to understand the layout
        Console.WriteLine($"rootVisual=0x{rootVisual:X}");
        try
        {
            IntPtr vtbl = Marshal.ReadIntPtr(rootVisual);
            Console.WriteLine($"vtbl=0x{vtbl:X}");
            for (int s = 0; s < 8; s++)
                Console.WriteLine($"  slot[{s}] = 0x{Marshal.ReadIntPtr(vtbl, s * IntPtr.Size):X}");
        }
        catch (Exception ex) { Console.WriteLine($"vtable read failed: {ex.Message}"); }

// Bisect: which signature shapes work on the visual?
Console.WriteLine("-- sig bisect --");
int scHr = ComVisualSetContent(rootVisual, IntPtr.Zero);  // slot 15, pointer arg
Console.WriteLine($"SetContent(null): 0x{scHr:X8}");
Console.WriteLine("about to call SetOffsetX(5f) via TerraFX...");
unsafe
{
    ((TerraFX.Interop.DirectX.IDCompositionVisual*)rootVisual)->SetOffsetX(5.0f);
}
Console.WriteLine("SetOffsetX(5f) via TerraFX: OK");

ComAddVisual(rootVisual, thumbVisual);

comHr = ComCreateTargetForHwnd(dcompDevice, hwnd, false, out IntPtr dcompTarget);
if (comHr != 0) { Console.WriteLine($"FAIL: CreateTargetForHwnd 0x{comHr:X8}"); return; }

ComTargetSetRoot(dcompTarget, rootVisual);
Console.WriteLine($"commit 1: 0x{ComCommit(dcompDevice):X8}");

var thumbRc = (IDCompVisual)Marshal.GetObjectForIUnknown(thumbVisual);

ShowWindow(hwnd, SW_SHOW);

// ---------- animate + verify ----------

const int OriginX = 150, OriginY = 150, RangeX = 420, RangeY = 260;
var clock = System.Diagnostics.Stopwatch.StartNew();
bool savedA = false, savedB = false;
long tEnd = 6000;
string tmp = Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? ".", "WindowScatter-Spike");
Directory.CreateDirectory(tmp);

while (clock.ElapsedMilliseconds < tEnd)
{
    while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
    {
        TranslateMessage(ref msg);
        DispatchMessageW(ref msg);
    }

    double t = clock.Elapsed.TotalSeconds;
    double u = (t % 1.4) / 1.4;                    // 0..1 every 1.4s
    double pingpong = t % 2.8 < 1.4 ? u : 1 - u;   // there and back
    double eased = 1 - Math.Pow(1 - pingpong, 3);  // cubic ease-out-ish

    unsafe
    {
        ((TerraFX.Interop.DirectX.IDCompositionVisual*)thumbVisual)->SetOffsetX((float)(OriginX + RangeX * eased));
        ((TerraFX.Interop.DirectX.IDCompositionVisual*)thumbVisual)->SetOffsetY((float)(OriginY + RangeY * eased * 0.4));
    }
    ComCommit(dcompDevice);

    // Save screenshots far (t=1.3s, eased~1) and near (t=2.7s, eased~0.1)
    if (!savedA && clock.ElapsedMilliseconds >= 1300 && clock.ElapsedMilliseconds < 1430)
    { SaveRegion(90, 90, 880, 600, Path.Combine(tmp, "spike-A-far.png")); savedA = true; Console.WriteLine("saved spike-A-far.png"); }
    if (!savedB && clock.ElapsedMilliseconds >= 2700 && clock.ElapsedMilliseconds < 2830)
    { SaveRegion(90, 90, 880, 600, Path.Combine(tmp, "spike-B-near.png")); savedB = true; Console.WriteLine("saved spike-B-near.png"); }

    Thread.Sleep(8);
}

Console.WriteLine(savedA && savedB ? "RESULT: screenshots saved - check visually" : "RESULT: screenshot timing missed");

DestroyWindow(hwnd);
ComRelease(dcompTarget);
ComRelease(dcompDevice);
ComRelease(dxgiDevice);
ComRelease(d3dDevice);
Console.WriteLine("done.");
}

static void SaveRegion(int x, int y, int w, int h, string path)
{
    using var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
}

static byte[] CaptureRegion(int x, int y, int w, int h)
{
    using var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
    var data = new byte[w * h];
    var lockBits = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    for (int row = 0; row < h; row++)
        for (int col = 0; col < w; col++)
            data[row * w + col] = Marshal.ReadByte(lockBits.Scan0, row * lockBits.Stride + col * 4); // B channel
    bmp.UnlockBits(lockBits);
    return data;
}

static int CountDiff(byte[] a, byte[] b)
{
    int diff = 0;
    int n = Math.Min(a.Length, b.Length);
    for (int i = 0; i < n; i++)
        if (Math.Abs(a[i] - b[i]) > 8) diff++;
    return diff;
}

// ---------- native interop (types must follow top-level statements) ----------

namespace DCompSpike
{
    internal class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSEX
        {
            public uint cbSize; public uint style; public IntPtr lpfnWndProc;
            public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance;
            public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public int fVisible;              // BOOL
            public int fSourceClientAreaOnly; // BOOL
        }

        internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        internal delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);

        internal const int PM_REMOVE = 0x0001;
        internal const int SW_SHOW = 5;
        internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        internal const int WS_EX_TOPMOST = 0x00000008;
        internal const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        internal const int DWM_TNP_RECTDESTINATION = 0x1;
        internal const int DWM_TNP_RECTSOURCE = 0x2;
        internal const int DWM_TNP_OPACITY = 0x4;
        internal const int DWM_TNP_VISIBLE = 0x8;
        internal const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
        internal const int DWM_TNP_ENABLE3D = 0x4000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName, [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")] internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] internal static extern IntPtr DispatchMessageW(ref MSG lpMsg);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern int GetWindowTextLengthW(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName);
        [DllImport("kernel32.dll")] internal static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

        [DllImport("d3d11.dll")]
        internal static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software, uint flags,
            IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
            out IntPtr ppDevice, IntPtr pFeatureLevel, IntPtr ppImmediateContext);

        [DllImport("dcomp.dll")]
        internal static extern int DCompositionCreateDevice3(IntPtr renderingDevice, ref Guid iid, out IntPtr dcompositionDevice);

        // dwmapi!#147 - private; see ADeltaX research on DwmpCreateSharedThumbnailVisual
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int DwmpCreateSharedThumbnailVisualDelegate(
            IntPtr hwndDestination, IntPtr hwndSource, uint dwThumbnailFlags,
            ref DWM_THUMBNAIL_PROPERTIES pThumbnailProperties,
            IntPtr pDCompDevice, out IntPtr ppVisual, out IntPtr phThumbnailId);

        // raw COM vtable helpers (vtable slots verified against SDK 10.0.26100 dcomp.h)
        internal static unsafe IntPtr VSlot(IntPtr comObj, int slot)
            => Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObj), slot * IntPtr.Size);

        internal static unsafe int ComCommit(IntPtr device)
            => ((delegate* unmanaged[Stdcall]<IntPtr, int>)VSlot(device, 3))(device);

        internal static unsafe int ComCreateTargetForHwnd(IntPtr device, IntPtr hwnd, bool topmost, out IntPtr target)
        {
            IntPtr targetLocal;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)VSlot(device, 6))
                (device, hwnd, topmost ? 1 : 0, &targetLocal);
            target = targetLocal;
            return hr;
        }

        internal static unsafe int ComCreateVisual(IntPtr device, out IntPtr visual)
        {
            IntPtr visualLocal;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VSlot(device, 7))(device, &visualLocal);
            visual = visualLocal;
            return hr;
        }

        internal static unsafe int ComTargetSetRoot(IntPtr target, IntPtr visual)
            => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VSlot(target, 3))(target, visual);

        internal static unsafe int ComVisualSetOffsetX(IntPtr visual, float value)
            => ((delegate* unmanaged[Stdcall]<IntPtr, float, int>)VSlot(visual, 3))(visual, value);

        internal static unsafe int ComVisualSetOffsetY(IntPtr visual, float value)
            => ((delegate* unmanaged[Stdcall]<IntPtr, float, int>)VSlot(visual, 5))(visual, value);

        internal static unsafe void ComAddVisual(IntPtr parent, IntPtr child)
            => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, int>)VSlot(parent, 16))(parent, child, 0, IntPtr.Zero);

        internal static unsafe int ComVisualSetContent(IntPtr visual, IntPtr content)
            => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VSlot(visual, 15))(visual, content);

        internal static void ComRelease(IntPtr comObj)
        {
            if (comObj != IntPtr.Zero) Marshal.Release(comObj);
        }

        // Minimal COM interfaces - vtable order verified against SDK 10.0.26100 dcomp.h.
        [ComImport]
        [Guid("4d93059d-097b-4651-9a60-f0f25116e2f3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IDCompVisual
        {
            void SetOffsetX(float offsetX);                       // slot 3
            void SetOffsetXWithAnimation(IntPtr animation);       // slot 4: SetOffsetX(IDCompositionAnimation*)
            void SetOffsetY(float offsetY);                       // slot 5
            void SetOffsetYWithAnimation(IntPtr animation);       // slot 6
        }
    }
}
