using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

public sealed record DisplayInfo(string Device, string Label, bool Virtual, bool Attached, Rectangle Bounds)
{
    public override string ToString() => $"{Label}  ·  {Bounds.Width} × {Bounds.Height}" + (Attached ? "" : " · disabled");
}

public static class Displays
{
    public static List<DisplayInfo> List()
    {
        var result = new List<DisplayInfo>();
        for (uint i = 0; ; i++)
        {
            var d = new Device { Size = Marshal.SizeOf<Device>() };
            if (!EnumDisplayDevices(null, i, ref d, 0)) break;
            if ((d.Flags & 8) != 0) continue; // Ignore mirroring drivers.
            var mode = Mode();
            EnumDisplaySettings(d.Name, -1, ref mode);
            var virtualDisplay = d.Description.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) || d.Id.Contains("MttVDD", StringComparison.OrdinalIgnoreCase);
            var label = virtualDisplay ? "Extra desktop (virtual display)" : $"{d.Name.Replace("\\\\.\\", "")} · {d.Description}";
            result.Add(new(d.Name, label, virtualDisplay, (d.Flags & 1) != 0, new(mode.X, mode.Y, (int)mode.Width, (int)mode.Height)));
        }
        return result;
    }
    public static void Extend(DisplayInfo display, int width = 1920, int height = 1080)
    {
        if (!display.Virtual) throw new InvalidOperationException("Only the virtual display can be configured automatically.");
        var existing = List().FirstOrDefault(d => d.Device == display.Device);
        if (existing is { Attached: true }) return;
        Configure(display, width, height);
    }
    public static void PlaceAbove(DisplayInfo display)
    {
        var size = display.Attached ? display.Bounds.Size : new Size(1920, 1080);
        Configure(display, size.Width, size.Height, AbovePrimary(size.Width, size.Height));
    }
    private static Point AbovePrimary(int width, int height)
    {
        var primary = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No primary monitor found.");
        return new(primary.Left + (primary.Width - width) / 2, primary.Top - height);
    }
    public static void Configure(DisplayInfo display, int width, int height, Point? position = null)
    {
        if (!display.Virtual) throw new InvalidOperationException("Only the virtual display can be configured automatically.");
        var existing = List().FirstOrDefault(d => d.Device == display.Device);
        var wasAbove = existing is { Attached: true } && existing.Bounds.Bottom == Screen.PrimaryScreen?.Bounds.Top;
        var location = position ?? (existing is { Attached: true } && !wasAbove ? existing.Bounds.Location : AbovePrimary(width, height));
        if (existing is { Attached: true } && existing.Bounds.Size == new Size(width, height) && existing.Bounds.Location == location) return;
        var mode = Mode();
        EnumDisplaySettings(display.Device, -1, ref mode);
        mode.Fields = 0x20 | 0x80000 | 0x100000 | 0x40000 | 0x400000;
        mode.Width = (uint)width; mode.Height = (uint)height; mode.Bits = 32; mode.Frequency = 60;
        mode.X = location.X; mode.Y = location.Y;
        var result = ChangeDisplaySettingsEx(display.Device, ref mode, IntPtr.Zero, 1, IntPtr.Zero);
        if (result != 0) DisplayTopology.Enable(display.Device, width, height, mode.X, mode.Y);
    }
    public static void Detach(DisplayInfo display)
    {
        if (!display.Virtual) throw new InvalidOperationException("Only a virtual display can be detached.");
        var mode = Mode();
        mode.Fields = 0x20 | 0x80000 | 0x100000; mode.Width = 0; mode.Height = 0;
        Check(ChangeDisplaySettingsEx(display.Device, ref mode, IntPtr.Zero, 1, IntPtr.Zero));
    }
    private static void Check(int code) { if (code != 0) throw new Win32Exception($"Windows could not configure the display (code {code}). Open Display settings to extend it manually."); }
    private static DevMode Mode() => new() { Size = (ushort)Marshal.SizeOf<DevMode>(), Name = "", Form = "" };
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Spec, Driver, Size, Extra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
        public ushort LogPixels;
        public uint Bits, Width, Height, Flags, Frequency, ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? name, uint index, ref Device device, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string name, int index, ref DevMode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string name, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr param);
}
