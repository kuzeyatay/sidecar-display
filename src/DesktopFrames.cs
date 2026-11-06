using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

// Version 2 transmits independent JPEG rectangles, preserving unchanged desktop pixels.
// Every connection starts with a complete frame; TCP preserves subsequent patch order.
internal sealed class DesktopEncoder
{
    internal const int Magic = 0x32464353; // SCF2, little endian
    private const int Tile = 128;
    private byte[] previous = [];
    private byte[] current = [];
    private Size previousSize;
    private readonly ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().Single(x => x.MimeType == "image/jpeg");

    public byte[] Encode(Bitmap bitmap, long quality, bool reset)
    {
        var bounds = new Rectangle(Point.Empty, bitmap.Size);
        var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
        if (bytesPerPixel is not (3 or 4)) throw new InvalidOperationException("Unsupported capture pixel format.");
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, bitmap.PixelFormat);
        var rectangles = new List<Rectangle>();
        var keyframe = reset || previousSize != bitmap.Size;
        try
        {
            var length = checked(data.Stride * bitmap.Height);
            if (current.Length != length) { current = new byte[length]; previous = new byte[length]; keyframe = true; }
            Marshal.Copy(data.Scan0, current, 0, length);
            if (!keyframe)
            {
                for (int y = 0; y < bitmap.Height; y += Tile)
                {
                    int run = -1;
                    for (int x = 0; x <= bitmap.Width; x += Tile)
                    {
                        // The sentinel also handles widths that are not multiples of Tile.
                        var width = Math.Min(Tile, bitmap.Width - x);
                        bool changed = false;
                        if (width > 0)
                            for (int row = y; row < Math.Min(y + Tile, bitmap.Height); row++)
                            {
                                var offset = row * data.Stride + x * bytesPerPixel;
                                if (!current.AsSpan(offset, width * bytesPerPixel).SequenceEqual(previous.AsSpan(offset, width * bytesPerPixel))) { changed = true; break; }
                            }
                        if (changed && run < 0) run = x;
                        if (run >= 0 && (!changed || x + Tile >= bitmap.Width))
                        {
                            var right = changed ? Math.Min(x + Tile, bitmap.Width) : x;
                            var rectangle = new Rectangle(run, y, right - run, Math.Min(Tile, bitmap.Height - y));
                            var merge = rectangles.FindLastIndex(r => r.X == rectangle.X && r.Width == rectangle.Width && r.Bottom == y);
                            if (merge >= 0) { var old = rectangles[merge]; old.Height += rectangle.Height; rectangles[merge] = old; }
                            else rectangles.Add(rectangle);
                            run = -1;
                        }
                    }
                }
                keyframe = rectangles.Count > 64 || rectangles.Sum(r => (long)r.Width * r.Height) > (long)bitmap.Width * bitmap.Height * 0.45;
            }
        }
        finally { bitmap.UnlockBits(data); }
        (previous, current) = (current, previous); previousSize = bitmap.Size;
        if (keyframe) { rectangles.Clear(); rectangles.Add(bounds); }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic); writer.Write(bitmap.Width); writer.Write(bitmap.Height); writer.Write(keyframe ? 1 : 0); writer.Write(rectangles.Count);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        foreach (var rectangle in rectangles)
        {
            writer.Write(rectangle.X); writer.Write(rectangle.Y); writer.Write(rectangle.Width); writer.Write(rectangle.Height);
            var lengthAt = stream.Position; writer.Write(0); var start = stream.Position;
            if (rectangle == bounds) bitmap.Save(stream, jpeg, parameters);
            else { using var patch = bitmap.Clone(rectangle, PixelFormat.Format24bppRgb); patch.Save(stream, jpeg, parameters); }
            var end = stream.Position; stream.Position = lengthAt; writer.Write(checked((int)(end - start))); stream.Position = end;
        }
        return stream.ToArray();
    }
}

internal sealed class DesktopUpdate : IDisposable
{
    public Size Size { get; private init; }
    public bool Keyframe { get; private init; }
    public List<(Point Position, Bitmap Image)> Patches { get; } = [];
    public static DesktopUpdate Decode(byte[] bytes)
    {
        if (bytes.Length < 4 || BitConverter.ToInt32(bytes, 0) != DesktopEncoder.Magic)
        {
            var image = ScreenCapture.Decode(bytes);
            var full = new DesktopUpdate { Size = image.Size, Keyframe = true }; full.Patches.Add((Point.Empty, image)); return full;
        }
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        reader.ReadInt32(); var width = reader.ReadInt32(); var height = reader.ReadInt32(); var flags = reader.ReadInt32(); var count = reader.ReadInt32();
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 34_000_000 || flags is < 0 or > 1 || count is < 0 or > 64 || (flags == 1 && count != 1))
            throw new InvalidDataException("Invalid desktop update dimensions.");
        var result = new DesktopUpdate { Size = new(width, height), Keyframe = flags == 1 };
        try
        {
            for (int i = 0; i < count; i++)
            {
                int x = reader.ReadInt32(), y = reader.ReadInt32(), w = reader.ReadInt32(), h = reader.ReadInt32(), length = reader.ReadInt32();
                if (x < 0 || y < 0 || w < 1 || h < 1 || (long)x + w > width || (long)y + h > height || length < 1 || length > stream.Length - stream.Position ||
                    (result.Keyframe && (x != 0 || y != 0 || w != width || h != height))) throw new InvalidDataException("Invalid desktop patch.");
                var image = ScreenCapture.Decode(reader.ReadBytes(length));
                if (image.Width != w || image.Height != h) { image.Dispose(); throw new InvalidDataException("Desktop patch size mismatch."); }
                result.Patches.Add((new(x, y), image));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing desktop update data.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public void Apply(Bitmap canvas)
    {
        if (canvas.Size != Size) throw new InvalidDataException("Desktop update needs a new complete frame.");
        if (Patches.Count == 0) return;
        using var graphics = Graphics.FromImage(canvas);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        foreach (var patch in Patches) graphics.DrawImageUnscaled(patch.Image, patch.Position);
    }
    public void Dispose() { foreach (var patch in Patches) patch.Image.Dispose(); }
}
