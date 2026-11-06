using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

// Capture directly into a reusable DIB. Graphics.CopyFromScreen on a managed bitmap
// creates an intermediate GDI surface and copies/converts it when releasing the HDC.
internal sealed class DesktopSurface : IDisposable
{
    private IntPtr bitmap;
    public Bitmap? Image { get; private set; }
    public Bitmap Capture(Rectangle bounds, Action<IntPtr, Rectangle> drawCursor)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr memory = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            if (Image?.Size != bounds.Size)
            {
                Dispose();
                var info = new BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, Bits = 32 };
                bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                Image = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppRgb, pixels);
            }
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            old = SelectObject(memory, bitmap);
            if (old == IntPtr.Zero || old == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x00CC0020)) throw new Win32Exception(Marshal.GetLastWin32Error());
            drawCursor(memory, bounds);
            if (!GdiFlush()) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Image!;
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1)) SelectObject(memory, old);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
    public void Dispose() { Image?.Dispose(); Image = null; if (bitmap != IntPtr.Zero) { DeleteObject(bitmap); bitmap = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XResolution, YResolution; public uint Colors, ImportantColors, ColorTable;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
