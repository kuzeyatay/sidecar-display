using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SidecarDisplay;

public sealed class MainForm : Form
{
    private readonly Color ink = Color.FromArgb(29, 39, 53);
    private readonly Color muted = Color.FromArgb(90, 101, 118);
    private readonly Color accent = Color.FromArgb(37, 99, 235);
    private readonly ComboBox displays = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox networks = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label driverStatus = new() { AutoSize = true, MaximumSize = new(760, 0) };
    private readonly Label hostStatus = new() { AutoSize = true, Text = "Ready to share", Padding = new(0, 12, 0, 0) };
    private readonly TextBox invitation = new() { ReadOnly = true, Multiline = true, Height = 64, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Invitation to copy" };
    private readonly TextBox join = new() { Multiline = true, Height = 100, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Sender invitation" };
    private readonly Button share;
    private readonly Button setup;
    private readonly Button copy;
    private readonly Panel hostPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel receiverPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private StreamHost? host;
    private ScreenCapture? capture;
    private DisplayInfo? attachedByUs;
    private bool closing;
    private bool busy;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 2500 };
    public MainForm()
    {
        Text = "Sidecar Display"; Font = new("Segoe UI", 10); ForeColor = ink; BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new(860, 780); MinimumSize = new(760, 660); StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(32, 24, 32, 24), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new(SizeType.Absolute, 68)); root.RowStyles.Add(new(SizeType.Absolute, 58)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 32));
        var heading = Stack(); heading.Controls.Add(Label("Sidecar Display", 27, true)); root.Controls.Add(heading);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var sendTab = Button("Share this PC", true); var receiveTab = Button("Use as a monitor");
        tabs.Controls.AddRange([sendTab, receiveTab]); root.Controls.Add(tabs);
        var body = new Panel { Dock = DockStyle.Fill }; body.Controls.AddRange([receiverPanel, hostPanel]); receiverPanel.Visible = false; hostPanel.BringToFront(); root.Controls.Add(body);
        sendTab.Click += (_, _) => { receiverPanel.Visible = false; hostPanel.Visible = true; hostPanel.BringToFront(); sendTab.BackColor = accent; sendTab.ForeColor = Color.White; receiveTab.BackColor = Color.FromArgb(240, 243, 248); receiveTab.ForeColor = ink; };
        receiveTab.Click += (_, _) => { hostPanel.Visible = false; receiverPanel.Visible = true; receiverPanel.BringToFront(); receiveTab.BackColor = accent; receiveTab.ForeColor = Color.White; sendTab.BackColor = Color.FromArgb(240, 243, 248); sendTab.ForeColor = ink; };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var help = new LinkLabel { Text = "Help", AutoSize = true, LinkColor = accent, Margin = new(0, 3, 16, 0) };
        help.LinkClicked += (_, _) => {
            try { Process.Start(new ProcessStartInfo("https://github.com/kuzeyatay/sidecar-display#readme") { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        };
        footer.Controls.Add(help); footer.Controls.Add(Label($"v{Application.ProductVersion.Split('+')[0]}", 9));
        root.Controls.Add(footer); Controls.Add(root);

        var h = Stack(); hostPanel.Controls.Add(h);
        h.Controls.Add(Label("Display", 14, true));
        h.Controls.Add(driverStatus);
        var actions = Flow(); setup = Button("Set up extra display"); var settings = Button("Display settings"); var remove = Button("Remove app’s display");
        actions.Controls.AddRange([setup, settings, remove]); h.Controls.Add(actions);
        setup.Click += async (_, _) => await SetupDisplay();
        settings.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
        remove.Click += async (_, _) => {
            if (host is not null || busy) return;
            try { await DriverSetup.RunElevated("--remove-driver"); RefreshDisplays(); } catch (Exception ex) { ShowError(ex); }
        };
        h.Controls.Add(Field("Display to share", displays));
        resolution.Items.AddRange(["1920 × 1080", "1366 × 768", "1280 × 720", "2560 × 1440"]); resolution.SelectedIndex = 0;
        h.Controls.Add(Field("Desktop resolution", resolution));
        displays.SelectedIndexChanged += (_, _) => resolution.Enabled = host is null && (displays.SelectedItem as DisplayInfo)?.Virtual == true;
        quality.Items.AddRange(["Balanced · 1080p / 45 fps", "Sharper text · native / 30 fps", "Fastest · 720p / 60 fps", "Slower laptop · 720p / 30 fps"]); quality.SelectedIndex = 0;
        h.Controls.Add(Field("Stream quality", quality));
        h.Controls.Add(Field("Network address", networks));
        h.Controls.Add(Label("Connection", 14, true));
        var shareActions = Flow(); share = Button("Start sharing", true); copy = Button("Copy invitation"); copy.Enabled = false; var firewall = Button("Allow private LAN");
        shareActions.Controls.AddRange([share, copy, firewall]); h.Controls.Add(shareActions);
        share.Click += async (_, _) => { if (host is null) await StartSharing(); else await StopSharing(); };
        copy.Click += (_, _) => { if (invitation.TextLength > 0) { Clipboard.SetText(invitation.Text); hostStatus.Text = "Invitation copied"; } };
        firewall.Click += async (_, _) => { try { await DriverSetup.RunElevated("--allow-lan"); hostStatus.Text = "Firewall rule added. Use a Private network."; } catch (Exception ex) { ShowError(ex); } };
        h.Controls.Add(invitation); h.Controls.Add(hostStatus);
        h.Controls.Add(Label("Keep your invitation private.", 9));

        var r = Stack(); receiverPanel.Controls.Add(r);
        r.Controls.Add(Label("Invitation", 14, true));
        r.Controls.Add(Label("Connect both PCs to the same local network.", 10)); r.Controls.Add(join);
        var receiveActions = Flow(); var paste = Button("Paste invitation"); var connect = Button("Connect to PC", true); var full = new CheckBox { Text = "Start fullscreen", Checked = true, AutoSize = true, Padding = new(10, 12, 0, 0) };
        receiveActions.Controls.AddRange([paste, connect, full]); r.Controls.Add(receiveActions);
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) join.Text = Clipboard.GetText().Trim(); };
        connect.Click += (_, _) => {
            try {
                var inv = Invitation.Parse(join.Text); var viewer = new ViewerForm(inv, full.Checked); viewer.Show(this);
                viewer.FormClosed += (_, _) => { Show(); Activate(); }; Hide();
            } catch (Exception ex) { ShowError(ex); }
        };
        RefreshDisplays(); RefreshNetworks();
        refresh.Tick += (_, _) => { if (host is null && !busy) RefreshDisplays(); }; refresh.Start();
        FormClosing += async (_, e) => { if (closing) return; e.Cancel = true; closing = true; refresh.Stop(); await StopSharing(); BeginInvoke(Close); };
    }
    private TableLayoutPanel Stack() => new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(0, 6, 0, 0) };
    private FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new(0, 8, 0, 8) };
    private Label Label(string text, float size, bool bold = false) => new() { Text = text, AutoSize = true, Font = new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = bold ? ink : muted, Margin = new(0, 3, 0, 9), MaximumSize = new(790, 0) };
    private Button Button(string text, bool primary = false) => new() { Text = text, AutoSize = true, Height = 38, MinimumSize = new(120, 38), Padding = new(12, 3, 12, 3), Margin = new(0, 0, 10, 0), FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.FromArgb(240, 243, 248), ForeColor = primary ? Color.White : ink, FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand };
    private Control Field(string text, Control control)
    {
        var p = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new(0, 3, 0, 7) };
        p.ColumnStyles.Add(new(SizeType.Absolute, 170)); p.ColumnStyles.Add(new(SizeType.Percent, 100));
        p.Controls.Add(Label(text, 10)); p.Controls.Add(control); return p;
    }
    private void RefreshDisplays()
    {
        var selected = (displays.SelectedItem as DisplayInfo)?.Device;
        var list = Displays.List().Where(d => d.Attached || d.Virtual).ToList();
        var previous = displays.Items.Cast<DisplayInfo>().ToArray();
        if (!previous.SequenceEqual(list))
        {
            displays.Items.Clear(); displays.Items.AddRange(list.Cast<object>().ToArray());
            displays.SelectedItem = list.FirstOrDefault(d => d.Device == selected) ?? list.FirstOrDefault(d => d.Virtual) ?? list.FirstOrDefault();
        }
        var v = list.FirstOrDefault(d => d.Virtual);
        driverStatus.Text = v is null ? "Virtual display not installed" : "Virtual display available";
        setup.Text = v is null ? "Set up extra display" : "Enable extra display";
    }
    private void RefreshNetworks()
    {
        var options = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.")).Select(a => new NetworkChoice(a.Address, n.Name))).ToArray();
        networks.Items.AddRange(options.Cast<object>().ToArray());
        if (options.Length > 0) networks.SelectedIndex = 0;
        else { networks.Items.Add(new NetworkChoice(IPAddress.Loopback, "No network — local test only")); networks.SelectedIndex = 0; }
    }
    private async Task SetupDisplay()
    {
        if (host is not null || busy) return; busy = true; setup.Enabled = false;
        try
        {
            var installed = !Displays.List().Any(d => d.Virtual);
            if (installed) { hostStatus.Text = "Installing virtual display…"; await DriverSetup.RunElevated("--install-driver"); }
            for (var i = 0; i < 12 && !Displays.List().Any(d => d.Virtual); i++) await Task.Delay(500);
            var v = Displays.List().FirstOrDefault(d => d.Virtual) ?? throw new InvalidOperationException("Windows has not exposed the virtual display. A restart may be needed.");
            if (installed) Displays.Configure(v, 1920, 1080); else Displays.Extend(v);
            if (installed || !v.Attached) attachedByUs = v; RefreshDisplays(); displays.SelectedItem = displays.Items.Cast<DisplayInfo>().First(d => d.Virtual);
            hostStatus.Text = "Extra desktop ready";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; setup.Enabled = true; }
    }
    private async Task StartSharing()
    {
        if (busy) return; busy = true;
        try
        {
            var d = displays.SelectedItem as DisplayInfo ?? throw new InvalidOperationException("Choose a display first.");
            if (d.Virtual) {
                var sizes = new[] { new Size(1920, 1080), new Size(1366, 768), new Size(1280, 720), new Size(2560, 1440) };
                var size = sizes[resolution.SelectedIndex]; Displays.Configure(d, size.Width, size.Height);
                if (!d.Attached) attachedByUs = d;
                await Task.Delay(800);
            }
            var n = (NetworkChoice)networks.SelectedItem!;
            var q = quality.SelectedIndex;
            capture = new ScreenCapture(d.Device, q == 1 ? 0 : q >= 2 ? 1280 : 1920, q == 1 ? 92 : q == 2 ? 70 : 80);
            // Fail before advertising an invitation when Windows capture is unavailable.
            await Task.Run(() => capture.Grab());
            host = new StreamHost(n.Address, 45910, capture.Grab, q == 1 ? 30 : q == 2 ? 60 : q == 3 ? 30 : 45, capture.GrabUpdate);
            host.Status += text => UI(() => hostStatus.Text = text);
            invitation.Text = host.Start(n.Address.ToString()).Encode();
            share.Text = "Stop sharing"; copy.Enabled = true; setup.Enabled = displays.Enabled = quality.Enabled = resolution.Enabled = networks.Enabled = false;
            hostStatus.Text = "Waiting for connection · " + (d.Virtual ? "extended desktop" : "mirroring") + " · " + n.Address;
            Awake.Set(true);
        }
        catch (Exception ex) { await StopSharing(); ShowError(ex); }
        finally { busy = false; }
    }
    private async Task StopSharing()
    {
        var old = host; host = null;
        if (old is not null) await old.DisposeAsync();
        capture?.Dispose(); capture = null;
        if (attachedByUs is not null) { try { Displays.Detach(attachedByUs); } catch { } attachedByUs = null; }
        invitation.Clear(); share.Text = "Start sharing"; copy.Enabled = false;
        setup.Enabled = displays.Enabled = quality.Enabled = networks.Enabled = true; hostStatus.Text = "Sharing stopped · invitation expired";
        resolution.Enabled = (displays.SelectedItem as DisplayInfo)?.Virtual == true;
        Awake.Set(false);
    }
    private void UI(Action action) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Sidecar Display", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private sealed record NetworkChoice(IPAddress Address, string Name) { public override string ToString() => $"{Address} · {Name}"; }
}
