using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using SidecarDisplay;

internal static class Program
{
    static readonly List<object> Results = [];
    static string output = "";
    [STAThread]
    static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts"); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var harness = new Form { Text = "Sidecar verification", ShowInTaskbar = false, Opacity = 0, Size = new(1, 1) };
        var exit = 0;
        harness.Shown += async (_, _) => {
            try
            {
                if (args.Contains("--shortcut")) { await Shortcut(); return; }
                await Test("Invitation validation", InvitationValidation);
                await Test("Framing and bounds", Framing);
                await Test("Authenticated TLS / unauthorized clients / reconnection", Transport);
                await Test("New receiver falls back to a version 0.1 sender", LegacySender);
                await Test("Changed-region encoding, reconstruction, and bandwidth", DesktopUpdates);
                await Test("Bounded two-frame pipeline and keyframes on reconnect", Pipeline);
                if (!args.Contains("--core"))
                {
                    if (!args.Contains("--live")) await Test("Virtual display resize, detach, and extend", VirtualTopology);
                    await Test("Real screen capture → TLS → native rendering and fullscreen", Desktop);
                    await Test("Main window closes and releases resources", MainWindow);
                }
            }
            catch (Exception ex) { exit = 1; Results.Add(new { test = "FAILURE", error = ex.ToString() }); Console.Error.WriteLine(ex); }
            finally { File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { utc = DateTime.UtcNow, scope = args.Contains("--core") ? "core" : args.Contains("--live") ? "desktop" : "full", passed = exit == 0, results = Results }, new JsonSerializerOptions { WriteIndented = true })); harness.Close(); }
        };
        Application.Run(harness); return exit;
    }
    static Task DesktopUpdates()
    {
        using var bitmap = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap); using var font = new Font("Segoe UI", 12);
        g.Clear(Color.White);
        for (int y = 10; y < bitmap.Height; y += 27)
            g.DrawString("Sidecar desktop test   Documents, code and windows   0123456789   Unchanged text stays on the receiver", font, Brushes.DarkSlateGray, 20, y);
        var encoder = new DesktopEncoder(); using var canvas = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        long legacyBytes = 0, updateBytes = 0; double legacyMs = 0, updateMs = 0;
        for (int i = 0; i < 80; i++)
        {
            g.FillRectangle(Brushes.White, 1600, 50, 300, 150); g.FillRectangle(Brushes.Blue, 1600 + i * 3, 80, 24, 24);
            var watch = Stopwatch.StartNew();
            using var full = new MemoryStream(); bitmap.Save(full, ImageFormat.Jpeg); legacyMs += watch.Elapsed.TotalMilliseconds; legacyBytes += full.Length;
            watch.Restart(); var bytes = encoder.Encode(bitmap, 80, i == 0); updateMs += watch.Elapsed.TotalMilliseconds; updateBytes += bytes.Length;
            using var decoded = DesktopUpdate.Decode(bytes); decoded.Apply(canvas);
            var pixel = canvas.GetPixel(1610 + i * 3, 90); Assert(pixel.B > 180 && pixel.R < 80, "Moving patch was not reconstructed");
            if (i > 12) { pixel = canvas.GetPixel(1601, 90); Assert(pixel.R > 220 && pixel.G > 220, "Previous moving patch left a trail"); }
        }
        var unchanged = encoder.Encode(bitmap, 80, false); Assert(unchanged.Length == 20, "Unchanged desktop should be a tiny heartbeat");
        Assert(updateBytes < legacyBytes / 5, "Desktop deltas did not cut bandwidth by at least 80%");
        Results.Add(new { test = "1080p moving desktop region, 80 frames", legacyBytes, updateBytes, bandwidthReductionPercent = 100d * (1d - (double)updateBytes / legacyBytes), legacyEncodeMs = legacyMs / 80, updateEncodeMs = updateMs / 80 });
        using var odd = new Bitmap(1366, 768, PixelFormat.Format24bppRgb); using var og = Graphics.FromImage(odd); og.Clear(Color.White);
        using (var key = DesktopUpdate.Decode(encoder.Encode(odd, 85, false))) Assert(key.Keyframe && key.Size == odd.Size, "Resize did not force a keyframe");
        og.FillRectangle(Brushes.Red, 1350, 750, 16, 18);
        using (var edge = DesktopUpdate.Decode(encoder.Encode(odd, 85, false))) { edge.Apply(odd); Assert(edge.Patches.Count > 0 && odd.GetPixel(1360, 760).R > 180, "Partial edge tile was lost"); }
        foreach (var invalid in new[] { Malformed(100, 100, 1, 0), Malformed(9000, 100, 0, 0), Malformed(100, 100, 0, 65), Malformed(100, 100, 0, 1) })
        {
            try { using var bad = DesktopUpdate.Decode(invalid); throw new Exception("Accepted malformed update"); } catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
        }
        return Task.CompletedTask;
    }
    static async Task Shortcut()
    {
        var frame = TestImage(); await using var host = new StreamHost(IPAddress.Loopback, 0, () => frame, 10);
        using var viewer = new ViewerForm(host.Start("127.0.0.1"));
        var closed = new TaskCompletionSource(); viewer.FormClosed += (_, _) => closed.TrySetResult();
        viewer.Show();
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) => File.WriteAllText(Path.Combine(output, "shortcut-state.json"), JsonSerializer.Serialize(new { viewer.Fullscreen, bounds = viewer.Bounds, border = viewer.FormBorderStyle.ToString(), viewer.PresentedFrames }));
        timer.Start(); await closed.Task;
    }
    static byte[] Malformed(int width, int height, int flags, int count)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(DesktopEncoder.Magic); writer.Write(width); writer.Write(height); writer.Write(flags); writer.Write(count); return stream.ToArray();
    }
    static async Task Pipeline()
    {
        using var bitmap = new Bitmap(320, 240, PixelFormat.Format24bppRgb); using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.CornflowerBlue);
        var encoder = new DesktopEncoder(); int captures = 0, keyframes = 0;
        await using var host = new StreamHost(IPAddress.Loopback, 0, TestImage, 60, reset => {
            Interlocked.Increment(ref captures); if (reset) Interlocked.Increment(ref keyframes); return encoder.Encode(bitmap, 80, reset);
        });
        var invitation = host.Start("127.0.0.1");
        for (int session = 0; session < 2; session++)
        {
            var start = captures; using var timeout = new CancellationTokenSource(6000);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int received = 0;
            var run = new StreamReceiver().RunAsync(invitation, async data => {
                using var update = DesktopUpdate.Decode(data);
                if (++received == 1) { Assert(update.Keyframe, "Reconnected session did not start with a complete frame"); entered.SetResult(); await release.Task; }
                if (received == 6) timeout.Cancel();
            }, timeout.Token, false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(250);
            Assert(captures - start == 2, $"Pipeline should stop after two outstanding frames, got {captures - start}");
            release.SetResult(); await run; Assert(received == 6, "Pipeline failed to resume after slow rendering"); await Task.Delay(150);
        }
        Assert(keyframes == 2, "Expected one keyframe per connection");
    }
    static async Task Test(string name, Func<Task> run) { var sw = Stopwatch.StartNew(); await run(); Results.Add(new { test = name, passed = true, ms = sw.ElapsedMilliseconds }); Console.WriteLine("PASS " + name); }
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    static Task InvitationValidation()
    {
        var original = new Invitation("192.168.1.10", 45910, new string('A', 64), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert(Invitation.Parse(original.Encode()) == original, "Invitation roundtrip changed values");
        foreach (var bad in new[] { "", "https://example.com", "sidecar:hello", "sidecar:" + Convert.ToBase64String("null"u8.ToArray()), (original with { Host = "remote.example.com" }).Encode(), (original with { Port = -1 }).Encode(), (original with { Secret = "AA==" }).Encode() })
        {
            try { Invitation.Parse(bad); throw new Exception("Accepted invalid invitation"); } catch (FormatException) { }
        }
        return Task.CompletedTask;
    }
    static async Task Framing()
    {
        using var ms = new MemoryStream(); var input = RandomNumberGenerator.GetBytes(100_000);
        await Wire.WriteAsync(ms, input, default); ms.Position = 0;
        Assert(input.SequenceEqual(await Wire.ReadAsync(ms, input.Length, default)), "Payload corrupted");
        foreach (var length in new[] { -1, 0, Wire.MaxPacket + 1 })
        {
            var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, length);
            try { await Wire.ReadAsync(new MemoryStream(prefix), Wire.MaxPacket, default); throw new Exception("Accepted invalid length"); } catch (InvalidDataException) { }
        }
        var truncated = new byte[6]; BinaryPrimitives.WriteInt32BigEndian(truncated, 5);
        try { await Wire.ReadAsync(new MemoryStream(truncated), 10, default); throw new Exception("Accepted truncated payload"); } catch (EndOfStreamException) { }
    }
    static byte[] TestImage()
    {
        using var b = new Bitmap(640, 360); using var g = Graphics.FromImage(b); g.Clear(Color.FromArgb(30, 120, 220));
        using var ms = new MemoryStream(); b.Save(ms, ImageFormat.Jpeg); return ms.ToArray();
    }
    static async Task LegacySender()
    {
        using var certificate = Wire.NewCertificate(); var secret = RandomNumberGenerator.GetBytes(32);
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
        var invitation = new Invitation("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, Wire.Fingerprint(certificate), Convert.ToBase64String(secret));
        using var timeout = new CancellationTokenSource(6000); int attempts = 0;
        var sender = Task.Run(async () => {
            for (int i = 0; i < 2; i++)
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                using var ssl = new System.Net.Security.SslStream(socket.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
                attempts++;
                byte[] hello;
                try { hello = await Wire.ReadAsync(ssl, 32, timeout.Token); }
                catch (InvalidDataException) { continue; }
                Assert(hello.SequenceEqual(secret), "Legacy fallback changed the secret");
                await Wire.WriteAsync(ssl, "SIDECAR/1"u8.ToArray(), timeout.Token);
                await Wire.WriteAsync(ssl, TestImage(), timeout.Token);
                await Wire.ReadAsync(ssl, 1, timeout.Token);
            }
        });
        var received = new TaskCompletionSource();
        var receiver = new StreamReceiver().RunAsync(invitation, bytes => { using var update = DesktopUpdate.Decode(bytes); Assert(update.Keyframe, "Legacy JPEG not recognized"); received.TrySetResult(); return Task.CompletedTask; }, timeout.Token);
        await received.Task.WaitAsync(timeout.Token); await sender; timeout.Cancel();
        try { await receiver; } catch (OperationCanceledException) { }
        Assert(attempts == 2, "Expected exactly one fallback handshake");
    }
    static async Task Transport()
    {
        var frame = TestImage(); await using var host = new StreamHost(IPAddress.Loopback, 0, () => frame, 30);
        var invitation = host.Start("127.0.0.1");
        using (var timeout = new CancellationTokenSource(4000))
        {
            var wrong = invitation with { Fingerprint = new string('0', 64) };
            try { await new StreamReceiver().RunAsync(wrong, _ => throw new Exception("Frame leaked"), timeout.Token, false); throw new Exception("Bad certificate accepted"); } catch (AuthenticationException) { }
        }
        using (var timeout = new CancellationTokenSource(4000))
        {
            var wrong = invitation with { Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            try { await new StreamReceiver().RunAsync(wrong, _ => throw new Exception("Frame leaked"), timeout.Token, false); throw new Exception("Bad secret accepted"); } catch (IOException) { }
        }
        for (int session = 0; session < 2; session++)
        {
            using var timeout = new CancellationTokenSource(5000); var count = 0;
            await new StreamReceiver().RunAsync(invitation, data => {
                using var image = ScreenCapture.Decode(data); var c = image.GetPixel(100, 100);
                Assert(Math.Abs(c.B - 220) < 10 && image.Size == new Size(640, 360), "Decoded frame does not match source");
                if (++count == 8) timeout.Cancel(); return Task.CompletedTask;
            }, timeout.Token, false, preferUpdates: session != 0);
            Assert(count == 8, "Did not stream 8 frames"); await Task.Delay(150);
        }
        // No ACK means the sender must not queue an unbounded stream.
        using (var timeout = new CancellationTokenSource(4000))
        {
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
            var run = new StreamReceiver().RunAsync(invitation, async _ => { entered.TrySetResult(); await release.Task; timeout.Cancel(); }, timeout.Token, false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); var before = host.FramesSent;
            await Task.Delay(300); Assert(host.FramesSent == before, "Sender queued frames without render ACK"); release.SetResult(); await run;
        }
        Results.Add(new { sent = host.FramesSent, note = "Invalid certificate and invitation secret rejected; second session reconnects; slow-render backpressure checked" });
        await Task.Delay(150);
        using (var timeout = new CancellationTokenSource(12000))
        {
            var count = 0; bool retried = false; var automatic = new StreamReceiver();
            automatic.Status += text => { if (text.Contains("retrying")) retried = true; };
            await automatic.RunAsync(invitation, _ => {
                count++;
                if (count == 6) host.InterruptConnectionForTest();
                if (count == 16) timeout.Cancel();
                return Task.CompletedTask;
            }, timeout.Token);
            Assert(count == 16 && retried, "Automatic receiver reconnection did not recover from a dropped TCP connection");
        }
    }
    static async Task Desktop()
    {
        var all = Displays.List(); var source = all.FirstOrDefault(d => d.Virtual && d.Attached) ?? all.First(d => d.Attached && d.Device != Screen.PrimaryScreen!.DeviceName);
        var screen = Screen.AllScreens.Single(s => s.DeviceName == source.Device);
        using var pattern = new Pattern { Bounds = screen.Bounds, TopMost = true }; pattern.Show(); pattern.Bounds = screen.Bounds; pattern.Activate(); pattern.Refresh(); await Task.Delay(1500);
        using var capture = new ScreenCapture(source.Device, 1920, 90);
        var bytes = capture.Grab();
        File.WriteAllBytes(Path.Combine(output, "capture-check.jpg"), bytes);
        Results.Add(new { test = "Capture geometry", native = source.Bounds.ToString(), screen = screen.Bounds.ToString(), pattern = pattern.Bounds.ToString(), visible = pattern.Visible });
        using (var decoded = ScreenCapture.Decode(bytes)) CheckPattern(decoded);
        using (var legacy = new ScreenCapture(source.Device, 1920, 90) { LegacyCopyForBenchmark = true })
        {
            var watch = Stopwatch.StartNew(); for (int i = 0; i < 30; i++) legacy.Grab(); var legacyMs = watch.Elapsed.TotalMilliseconds / 30;
            watch.Restart(); for (int i = 0; i < 30; i++) capture.Grab(); var directMs = watch.Elapsed.TotalMilliseconds / 30;
            Results.Add(new { test = "Real 1080p capture and full JPEG encode", legacyMs, directMs, speedup = legacyMs / directMs });
        }
        await using var host = new StreamHost(IPAddress.Loopback, 0, capture.Grab, 45, capture.GrabUpdate);
        var invitation = host.Start("127.0.0.1");
        using var viewer = new ViewerForm(invitation); viewer.StartPosition = FormStartPosition.Manual; viewer.Bounds = new Rectangle(Screen.PrimaryScreen!.WorkingArea.X + 50, 50, 900, 600); viewer.Show();
        var before = viewer.Bounds; viewer.SetFullscreen(true);
        Assert(viewer.FormBorderStyle == FormBorderStyle.None && viewer.Bounds == Screen.FromControl(viewer).Bounds, "Fullscreen does not cover display or still has border");
        viewer.SetFullscreen(false); Assert(viewer.Bounds == before && viewer.FormBorderStyle == FormBorderStyle.Sizable, "Leaving fullscreen failed to restore bounds");
        viewer.SetFullscreen(true);
        var sw = Stopwatch.StartNew();
        while (viewer.PresentedFrames < 80 && sw.ElapsedMilliseconds < 15000) await Task.Delay(50);
        Assert(viewer.PresentedFrames >= 80, "The viewer's own connection did not present 80 frames");
        using (var rendered = viewer.Snapshot()!) CheckPattern(rendered);
        using (var image = new Bitmap(viewer.Width, viewer.Height)) { viewer.DrawToBitmap(image, viewer.ClientRectangle); image.Save(Path.Combine(output, "receiver-fullscreen.png")); }
        Results.Add(new { source = source.Device, virtualDisplay = source.Virtual, resolution = source.Bounds.Size.ToString(), received = viewer.PresentedFrames, seconds = sw.Elapsed.TotalSeconds, fps = viewer.PresentedFrames / sw.Elapsed.TotalSeconds, bytes = viewer.ReceivedBytes, fullscreen = true });
        viewer.Close(); pattern.Close();
    }
    static async Task MainWindow()
    {
        var main = new MainForm(); main.Show(); await Task.Delay(150); main.Close(); await Task.Delay(150);
        Assert(main.IsDisposed, "Main app did not close on its first close request");
    }
    static async Task VirtualTopology()
    {
        var virtualDisplay = Displays.List().FirstOrDefault(d => d.Virtual);
        if (virtualDisplay is null) { Results.Add(new { test = "Virtual topology", skipped = true, reason = "No virtual driver installed" }); return; }
        var physicalBefore = Displays.List().Where(d => !d.Virtual && d.Attached).ToArray();
        Displays.Configure(virtualDisplay, 1920, 1080); await Task.Delay(700);
        var after = Displays.List().Single(d => d.Device == virtualDisplay.Device);
        Assert(after.Attached && after.Bounds.Size == new Size(1920, 1080), "Virtual monitor did not resize to 1080p");
        Displays.Detach(after); await Task.Delay(500);
        Assert(!Displays.List().Single(d => d.Device == after.Device).Attached, "Virtual monitor did not detach");
        Displays.Extend(after); await Task.Delay(700);
        Assert(Displays.List().Single(d => d.Device == after.Device).Attached, "Virtual monitor did not extend again");
        Assert(physicalBefore.SequenceEqual(Displays.List().Where(d => !d.Virtual && d.Attached)), "Virtual display configuration altered physical monitors");
    }
    static void CheckPattern(Bitmap image)
    {
        var c = image.GetPixel(image.Width / 4, image.Height / 4);
        Assert(c.R > 200 && c.G > 60 && c.G < 170 && c.B < 80, $"Captured pixels wrong: {c}");
        var b = image.GetPixel(image.Width * 3 / 4, image.Height * 3 / 4);
        Assert(b.B > 150 && b.R < 100, $"Bottom-right pixels wrong: {b}");
    }
    sealed class Pattern : Form
    {
        public Pattern() { FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Text = "Sidecar verification pattern"; ShowInTaskbar = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(245, 120, 35));
            using var blue = new SolidBrush(Color.FromArgb(30, 90, 215)); e.Graphics.FillRectangle(blue, Width / 2, Height / 2, Width / 2, Height / 2);
            using var font = new Font("Segoe UI", 34, FontStyle.Bold); e.Graphics.DrawString("Sidecar Display\nLive Windows capture test", font, Brushes.White, 60, 60);
        }
    }
}
