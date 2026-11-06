using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

// CCD is required to reconnect some indirect displays after they are detached.
// Preserve the exact active paths and modes; only add the requested virtual path.
internal static class DisplayTopology
{
    internal static void Enable(string deviceName, int width, int height, int x, int y)
    {
        if (Marshal.SizeOf<PathInfo>() != 72 || Marshal.SizeOf<SourceDeviceName>() != 84 || Marshal.SizeOf<ModeInfo>() != 64)
            throw new InvalidOperationException("Unexpected native display structure layout.");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(1, out var pc, out var mc));
            var paths = new PathInfo[pc]; var modes = new ModeInfo[mc];
            var code = QueryDisplayConfig(1, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (code == 122) continue;
            Check(code);
            var active = paths.Take((int)pc).Where(p => (p.Flags & 1) != 0).ToList();
            var selected = paths.Take((int)pc).Where(p => p.Target.Available != 0 && SourceName(p.Source) == deviceName).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("Windows did not report an available path for the virtual display.");
            var path = selected.FirstOrDefault(p => (p.Flags & 1) != 0);
            if (path.Target.Available == 0) path = selected[0];
            var source = path.Source;
            active.RemoveAll(p => p.Source.Adapter == source.Adapter && p.Source.Id == source.Id);
            var allModes = modes.Take((int)mc).ToList();
            // Reuse this source's mode if present: duplicate source-mode entries are invalid.
            var index = allModes.FindIndex(m => m.Type == 1 && m.Adapter == source.Adapter && m.Id == source.Id);
            var newMode = new ModeInfo { Type = 1, Id = source.Id, Adapter = source.Adapter, Width = (uint)width, Height = (uint)height, PixelFormat = 4, X = x, Y = y };
            if (index >= 0) allModes[index] = newMode;
            else { index = allModes.Count; allModes.Add(newMode); }
            path.Source.ModeIndex = (uint)index;
            path.Target.ModeIndex = uint.MaxValue;
            path.Target.RefreshNumerator = path.Target.RefreshDenominator = 0;
            path.Target.ScanLine = 0;
            path.Flags = 1;
            active.Add(path);
            // Validate before applying. ALLOW_CHANGES permits Windows to select the new target timing.
            const uint common = 0x20 | 0x400;
            Check(SetDisplayConfig((uint)active.Count, active.ToArray(), (uint)allModes.Count, allModes.ToArray(), common | 0x40));
            Check(SetDisplayConfig((uint)active.Count, active.ToArray(), (uint)allModes.Count, allModes.ToArray(), common | 0x80 | 0x200));
            return;
        }
        throw new InvalidOperationException("Display configuration changed repeatedly. Please try again.");
    }
    private static string SourceName(Source source)
    {
        var name = new SourceDeviceName { Type = 1, Size = (uint)Marshal.SizeOf<SourceDeviceName>(), Adapter = source.Adapter, Id = source.Id, Name = "" };
        return DisplayConfigGetDeviceInfo(ref name) == 0 ? name.Name : "";
    }
    private static void Check(int result) { if (result != 0) throw new Win32Exception(result, "Windows display configuration failed: " + result); }
    [StructLayout(LayoutKind.Sequential)] private record struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Target
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLine;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public uint Width;
        [FieldOffset(20)] public uint Height;
        [FieldOffset(24)] public uint PixelFormat;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
        // Preserve the full target-mode union when marshaling modes supplied by Windows.
        [FieldOffset(16)] public long Raw1;
        [FieldOffset(24)] public long Raw2;
        [FieldOffset(32)] public long Raw3;
        [FieldOffset(40)] public long Raw4;
        [FieldOffset(48)] public long Raw5;
        [FieldOffset(56)] public long Raw6;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceDeviceName
    {
        public uint Type, Size;
        public Luid Adapter;
        public uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint pathCount, [In] PathInfo[] paths, uint modeCount, [In] ModeInfo[] modes, uint flags);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName name);
}
