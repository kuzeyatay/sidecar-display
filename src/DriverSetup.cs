using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SidecarDisplay;

public static class DriverSetup
{
    private static readonly Guid DisplayClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SidecarDisplay", "Driver");
    private static readonly string Marker = Path.Combine(Root, "owned-device.txt");
    private static readonly Dictionary<string, string> Hashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mttvdd.cat"] = "08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6",
        ["MttVDD.dll"] = "C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05",
        ["MttVDD.inf"] = "550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D"
    };
    public static async Task RunElevated(string command)
    {
        using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, command) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
            ?? throw new IOException("Could not start setup.");
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException("Setup did not finish. Check the setup error dialog, then try again.");
    }
    public static void Install()
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64) throw new PlatformNotSupportedException("The bundled driver supports Intel/AMD 64-bit Windows.");
        if (Displays.List().Any(d => d.Virtual)) return;
        var source = Path.Combine(AppContext.BaseDirectory, "driver");
        foreach (var pair in Hashes)
        {
            var data = File.ReadAllBytes(Path.Combine(source, pair.Key));
            if (Convert.ToHexString(SHA256.HashData(data)) != pair.Value) throw new InvalidDataException("Driver checksum failed: " + pair.Key);
        }
        Directory.CreateDirectory(Root);
        foreach (var name in Hashes.Keys) File.Copy(Path.Combine(source, name), Path.Combine(Root, name), true);
        var config = Path.Combine(Root, "vdd_settings.xml");
        if (!File.Exists(config)) File.Copy(Path.Combine(source, "vdd_settings.xml"), config);
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\MikeTheTech\VirtualDisplayDriver");
        // Existing VDD installations and configuration are left intact.
        if (key.GetValue("VDDPATH") is null) key.SetValue("VDDPATH", Root);
        var guid = DisplayClass;
        var set = SetupDiCreateDeviceInfoList(ref guid, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var dataInfo = new DevInfo { Size = Marshal.SizeOf<DevInfo>() };
        bool registered = false;
        try
        {
            Ensure(SetupDiCreateDeviceInfo(set, "Display", ref guid, "Sidecar virtual display", IntPtr.Zero, 1, ref dataInfo));
            var ids = Encoding.Unicode.GetBytes("Root\\MttVDD\0\0");
            Ensure(SetupDiSetDeviceRegistryProperty(set, ref dataInfo, 1, ids, ids.Length));
            Ensure(SetupDiCallClassInstaller(0x19, set, ref dataInfo)); registered = true;
            Ensure(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, "Root\\MttVDD", Path.Combine(Root, "MttVDD.inf"), 1, out var reboot));
            var id = new StringBuilder(1024);
            Ensure(SetupDiGetDeviceInstanceId(set, ref dataInfo, id, id.Capacity, out _));
            File.WriteAllText(Marker, id.ToString());
            if (reboot) MessageBox.Show("Windows needs a restart to finish installing the virtual display.", "Sidecar Display");
        }
        catch { if (registered) SetupDiCallClassInstaller(5, set, ref dataInfo); throw; }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static void RemoveOwnedDevice()
    {
        if (!File.Exists(Marker)) throw new InvalidOperationException("This app has not installed a virtual display. Existing drivers are left untouched.");
        var id = File.ReadAllText(Marker).Trim();
        if (!id.StartsWith("ROOT\\DISPLAY\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected device identifier.");
        var guid = DisplayClass;
        var set = SetupDiCreateDeviceInfoList(ref guid, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var info = new DevInfo { Size = Marshal.SizeOf<DevInfo>() };
            Ensure(SetupDiOpenDeviceInfo(set, id, IntPtr.Zero, 0, ref info));
            var bytes = new byte[2048];
            Ensure(SetupDiGetDeviceRegistryProperty(set, ref info, 1, out _, bytes, bytes.Length, out _));
            if (!Encoding.Unicode.GetString(bytes).Split('\0').Contains("Root\\MttVDD", StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The device is not the Sidecar virtual display.");
            Ensure(SetupDiCallClassInstaller(5, set, ref info));
            File.Delete(Marker);
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static void AllowPrivateNetwork()
    {
        // Program-scoped, private-profile, local-subnet-only rule. Never disable the firewall.
        var psi = new ProcessStartInfo("netsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "advfirewall", "firewall", "add", "rule", "name=Sidecar Display (private LAN)", "dir=in", "action=allow", "program=" + Environment.ProcessPath, "protocol=TCP", "localport=45910", "profile=private", "remoteip=localsubnet" }) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!; var output = p.StandardOutput.ReadToEnd(); var error = p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(output + error);
    }
    private static void Ensure(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private struct DevInfo { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid guid, IntPtr hwnd);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid guid, string description, IntPtr hwnd, uint flags, ref DevInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref DevInfo data, uint property, byte[] buffer, int size);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiCallClassInstaller(uint operation, IntPtr set, ref DevInfo data);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo data, StringBuilder buffer, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiOpenDeviceInfo(IntPtr set, string id, IntPtr hwnd, uint flags, ref DevInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DevInfo data, uint property, out uint type, byte[] buffer, int size, out int required);
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwnd, string id, string inf, uint flags, out bool reboot);
}
