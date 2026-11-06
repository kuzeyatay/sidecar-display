using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SidecarDisplay;

public sealed class ViewerForm : Form
{
    private readonly PictureBox picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Panel toolbar = new() { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(28, 33, 42) };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.White, Padding = new(12, 14, 0, 0) };
    private readonly Label offline = new() { Text = "Connecting…", AutoSize = false, Dock = DockStyle.Top, Height = 44, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, BackColor = Color.FromArgb(83, 65, 21) };
    private readonly CancellationTokenSource stop = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly Invitation? invite;
    private readonly bool startFullscreen;
    private readonly StreamReceiver receiver = new();
    private Rectangle savedBounds;
    private FormWindowState savedState;
    private DateTime lastMouse = DateTime.UtcNow;
    private bool hiddenCursor;
    private bool connected;
    private long lastFrames;
    private long lastBytes;
    private readonly Stopwatch statsClock = Stopwatch.StartNew();
    public bool Fullscreen { get; private set; }
    public long PresentedFrames { get; private set; }
    internal long ReceivedBytes => receiver.BytesReceived;
    internal Bitmap? Snapshot() => picture.Image is null ? null : new Bitmap(picture.Image);
    public ViewerForm(Invitation? invite = null, bool fullscreen = false)
    {
        this.invite = invite; startFullscreen = fullscreen;
        Text = "Sidecar Display — Monitor"; Font = new("Segoe UI", 10); BackColor = Color.Black;
        ClientSize = new(1100, 700); MinimumSize = new(480, 300); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true; AutoScaleMode = AutoScaleMode.Dpi;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 335, Padding = new(5), BackColor = toolbar.BackColor };
        var full = new Button { Text = "Fullscreen (Ctrl+Shift+F)", AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        var disconnect = new Button { Text = "Disconnect", AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        full.Click += (_, _) => ToggleFullscreen(); disconnect.Click += (_, _) => Close(); buttons.Controls.AddRange([full, disconnect]);
        toolbar.Controls.Add(status); toolbar.Controls.Add(buttons);
        Controls.Add(picture); Controls.Add(offline); Controls.Add(toolbar);
        picture.DoubleClick += (_, _) => ToggleFullscreen();
        picture.MouseMove += (_, e) => { lastMouse = DateTime.UtcNow; ShowCursor(); if (Fullscreen && e.Y < 30) toolbar.Visible = true; };
        toolbar.MouseMove += (_, _) => { lastMouse = DateTime.UtcNow; ShowCursor(); };
        receiver.Status += text => UI(() => {
            connected = text.StartsWith("Connected"); status.Text = text; offline.Text = text; offline.Visible = !connected;
            if (!connected) { ShowCursor(); toolbar.Visible = true; }
        });
        timer.Tick += (_, _) => {
            if (Fullscreen && connected && (DateTime.UtcNow - lastMouse).TotalSeconds > 2) { toolbar.Visible = false; if (ContainsFocus && picture.ClientRectangle.Contains(picture.PointToClient(Cursor.Position))) HideCursor(); }
            if (connected && statsClock.ElapsedMilliseconds >= 1000) {
                var seconds = statsClock.Elapsed.TotalSeconds;
                status.Text = $"Encrypted · {(receiver.FramesReceived - lastFrames) / seconds:F0} fps · {(receiver.BytesReceived - lastBytes) * 8 / seconds / 1_000_000:F1} Mbps";
                lastFrames = receiver.FramesReceived; lastBytes = receiver.BytesReceived; statsClock.Restart();
            }
        };
        Shown += async (_, _) => {
            if (startFullscreen) SetFullscreen(true); timer.Start(); Awake.Set(true);
            if (invite is null) return;
            try { await Task.Run(() => receiver.RunAsync(invite, PresentAsync, stop.Token)); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { connected = false; offline.Text = ex.Message; offline.Visible = true; toolbar.Visible = true; ShowCursor(); }
        };
        Deactivate += (_, _) => ShowCursor();
        FormClosed += (_, _) => { stop.Cancel(); timer.Stop(); timer.Dispose(); ShowCursor(); picture.Image?.Dispose(); picture.Image = null; Awake.Set(false); };
    }
    public Task PresentAsync(byte[] bytes)
    {
        var update = DesktopUpdate.Decode(bytes);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || !IsHandleCreated) { update.Dispose(); ready.TrySetCanceled(); return ready.Task; }
        try { BeginInvoke(() => {
            using (update)
            {
                if (IsDisposed || stop.IsCancellationRequested) { ready.TrySetCanceled(); return; }
                try
                {
                    if (picture.Image is null || picture.Image.Size != update.Size)
                    {
                        if (!update.Keyframe) throw new InvalidDataException("Waiting for a complete desktop frame.");
                        var old = picture.Image; picture.Image = new Bitmap(update.Size.Width, update.Size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb); old?.Dispose();
                    }
                    update.Apply((Bitmap)picture.Image);
                    if (update.Patches.Count > 0) picture.Invalidate();
                    PresentedFrames++; ready.TrySetResult();
                }
                catch (Exception ex) { ready.TrySetException(ex); }
            }
        }); } catch (InvalidOperationException) { update.Dispose(); ready.TrySetCanceled(); }
        return ready.Task;
    }
    public void ToggleFullscreen() => SetFullscreen(!Fullscreen);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.F)) { ToggleFullscreen(); return true; }
        if (keyData == Keys.Escape && Fullscreen) { SetFullscreen(false); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void SetFullscreen(bool value)
    {
        if (Fullscreen == value) return;
        if (value)
        {
            savedBounds = Bounds; savedState = WindowState; var screen = Screen.FromControl(this);
            WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; Bounds = screen.Bounds;
            Fullscreen = true; toolbar.Visible = false;
        }
        else
        {
            Fullscreen = false; ShowCursor(); FormBorderStyle = FormBorderStyle.Sizable; Bounds = savedBounds; WindowState = savedState; toolbar.Visible = true;
        }
    }
    private void UI(Action action) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private void HideCursor() { if (!hiddenCursor) { Cursor.Hide(); hiddenCursor = true; } }
    private void ShowCursor() { if (hiddenCursor) { Cursor.Show(); hiddenCursor = false; } }
}

internal static class Awake
{
    public static void Set(bool active) => SetThreadExecutionState(active ? 0x80000003u : 0x80000000u);
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint state);
}
