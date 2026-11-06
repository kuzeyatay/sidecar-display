using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

public sealed class ScreenCapture : IDisposable
{
    private readonly string device;
    private readonly int maxWidth;
    private readonly long quality;
    private Bitmap? native;
    private Bitmap? scaled;
    private readonly DesktopEncoder updates = new();
    private readonly DesktopSurface surface = new();
    internal bool LegacyCopyForBenchmark { get; init; }
    private readonly ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders().Single(x => x.MimeType == "image/jpeg");
    public ScreenCapture(string device, int maxWidth = 1920, long quality = 85)
    { this.device = device; this.maxWidth = maxWidth; this.quality = quality; }
    public byte[] Grab()
    {
        var output = Capture();
        using var ms = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        output.Save(ms, encoder, parameters);
        return ms.ToArray();
    }
    public byte[] GrabUpdate(bool reset) => updates.Encode(Capture(), quality, reset);
    private Bitmap Capture()
    {
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device)
            ?? throw new InvalidOperationException("The selected display was disconnected. Stop sharing and select a display again.");
        var b = screen.Bounds;
        if (b.Width > 8192 || b.Height > 8192 || b.Width < 1 || b.Height < 1 || (long)b.Width * b.Height > 34_000_000) throw new InvalidOperationException("Unsupported display size.");
        Bitmap captured;
        if (LegacyCopyForBenchmark)
        {
            if (native is null || native.Size != b.Size) { native?.Dispose(); native = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb); }
            using var g = Graphics.FromImage(native);
            g.CopyFromScreen(b.Location, Point.Empty, b.Size, CopyPixelOperation.SourceCopy);
            var dc = g.GetHdc(); try { DrawCursor(dc, b); } finally { g.ReleaseHdc(dc); }
            captured = native;
        }
        else captured = surface.Capture(b, DrawCursor);
        var ratio = maxWidth == 0 ? 1d : Math.Min(1d, (double)maxWidth / b.Width);
        var size = new Size(Math.Max(1, (int)(b.Width * ratio)), Math.Max(1, (int)(b.Height * ratio)));
        Bitmap output = captured;
        if (size != captured.Size)
        {
            if (scaled is null || scaled.Size != size) { scaled?.Dispose(); scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb); }
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(captured, new Rectangle(Point.Empty, size)); output = scaled;
        }
        return output;
    }
    public static Bitmap Decode(byte[] data)
    {
        using var stream = new MemoryStream(data, false);
        using var image = Image.FromStream(stream, true, true);
        if (image.Width is < 1 or > 8192 || image.Height is < 1 or > 8192 || (long)image.Width * image.Height > 34_000_000)
            throw new InvalidDataException("Unsupported incoming image size.");
        return new Bitmap(image);
    }
    private static void DrawCursor(IntPtr dc, Rectangle bounds)
    {
        var ci = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref ci) || ci.Flags != 1 || !bounds.Contains(ci.Position)) return;
        if (!GetIconInfo(ci.Handle, out var icon)) return;
        try { DrawIconEx(dc, ci.Position.X - bounds.X - (int)icon.X, ci.Position.Y - bounds.Y - (int)icon.Y, ci.Handle, 0, 0, 0, IntPtr.Zero, 3); }
        finally { if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
    }
    public void Dispose() { native?.Dispose(); scaled?.Dispose(); surface.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Handle; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public bool Icon; public uint X, Y; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr handle, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
