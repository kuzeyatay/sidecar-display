using System.Text.Json;

namespace SidecarDisplay;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--install-driver")) { DriverSetup.Install(); return 0; }
            if (args.Contains("--remove-driver")) { DriverSetup.RemoveOwnedDevice(); return 0; }
            if (args.Contains("--allow-lan")) { DriverSetup.AllowPrivateNetwork(); return 0; }
            if (args is ["--place-above", var report])
            {
                var before = Displays.List();
                var display = before.First(d => d.Virtual && d.Attached);
                Displays.PlaceAbove(display);
                var after = Displays.List();
                var placed = after.Single(d => d.Device == display.Device);
                var primary = Screen.PrimaryScreen!.Bounds;
                var verified = placed.Attached && placed.Bounds.Bottom == primary.Top &&
                    before.Where(d => !d.Virtual).SequenceEqual(after.Where(d => !d.Virtual));
                File.WriteAllText(report, JsonSerializer.Serialize(new { verified, primary, before, after }, new JsonSerializerOptions { WriteIndented = true }));
                return verified ? 0 : 1;
            }
            if (args is ["--diagnostics", var destination])
            {
                File.WriteAllText(destination, JsonSerializer.Serialize(new { OS = Environment.OSVersion.ToString(), Displays = Displays.List(), Screens = Screen.AllScreens.Select(s => new { s.DeviceName, s.Bounds, s.Primary }) }, new JsonSerializerOptions { WriteIndented = true })); return 0;
            }
            Application.Run(new MainForm()); return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Sidecar Display", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
