using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace SidecarDisplay;

public sealed record Invitation(string Host, int Port, string Fingerprint, string Secret)
{
    public string Encode() => "sidecar:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    public static Invitation Parse(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("sidecar:", StringComparison.Ordinal) || value.Length > 4096)
            throw new FormatException("Paste the complete invitation copied from the other PC.");
        try
        {
            var item = JsonSerializer.Deserialize<Invitation>(Convert.FromBase64String(value[8..]))!;
            if (item is null || !IPAddress.TryParse(item.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                item.Port is < 1024 or > 65535 || item.Fingerprint.Length != 64 || Convert.FromHexString(item.Fingerprint).Length != 32 ||
                Convert.FromBase64String(item.Secret).Length != 32)
                throw new FormatException();
            return item;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NullReferenceException or ArgumentException)
        { throw new FormatException("This invitation is incomplete or invalid. Copy a fresh one from the sender."); }
    }
}

public static class Wire
{
    public const int MaxPacket = 24 * 1024 * 1024;
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (bytes.Length > MaxPacket) throw new InvalidDataException("Frame exceeds the size limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct);
        await stream.WriteAsync(bytes, ct);
    }
    public static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken ct)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        var count = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (count < 1 || count > Math.Min(limit, MaxPacket)) throw new InvalidDataException("Invalid packet length.");
        var result = new byte[count];
        await stream.ReadExactlyAsync(result, ct);
        return result;
    }
    public static X509Certificate2 NewCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Sidecar Display", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
    public static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}

public sealed class StreamHost : IAsyncDisposable
{
    private readonly X509Certificate2 certificate = Wire.NewCertificate();
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener;
    private readonly Func<byte[]> capture;
    private readonly Func<bool, byte[]>? captureUpdate;
    private readonly int fps;
    private Task? loop;
    private int connected;
    private TcpClient? activeClient;
    public event Action<string>? Status;
    public long FramesSent { get; private set; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public StreamHost(IPAddress address, int port, Func<byte[]> capture, int fps = 20, Func<bool, byte[]>? captureUpdate = null)
    { listener = new TcpListener(address, port); this.capture = capture; this.captureUpdate = captureUpdate; this.fps = Math.Clamp(fps, 1, 60); }
    public Invitation Start(string advertisedAddress)
    {
        listener.Start(4);
        loop = Task.Run(AcceptLoop);
        return new(advertisedAddress, Port, Wire.Fingerprint(certificate), Convert.ToBase64String(secret));
    }
    private readonly List<Task> clients = [];
    private async Task AcceptLoop()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                clients.RemoveAll(t => t.IsCompleted);
                // Bound unauthenticated handshakes; they do not reserve the streaming slot.
                if (clients.Count >= 6) { client.Dispose(); continue; }
                clients.Add(Serve(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { await Task.WhenAll(clients); }
    }
    private async Task Serve(TcpClient client)
    {
        var ownsSlot = false;
        using (client)
        using (var ssl = new SslStream(client.GetStream(), false))
        {
            client.NoDelay = true;
            try
            {
                using var auth = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                auth.CancelAfter(TimeSpan.FromSeconds(6));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                }, auth.Token);
                var supplied = await Wire.ReadAsync(ssl, 33, auth.Token);
                if (supplied.Length is not (32 or 33) || !CryptographicOperations.FixedTimeEquals(supplied.AsSpan(0, 32), secret)) return;
                var updates = supplied.Length == 33 && supplied[32] == 2 && captureUpdate is not null;
                if (Interlocked.CompareExchange(ref connected, 1, 0) != 0)
                { await Wire.WriteAsync(ssl, "BUSY"u8.ToArray(), auth.Token); return; }
                ownsSlot = true;
                activeClient = client;
                await Wire.WriteAsync(ssl, Encoding.UTF8.GetBytes(updates ? "SIDECAR/2" : "SIDECAR/1"), auth.Token);
                Status?.Invoke(updates ? "Connected" : "Connected · update receiver for faster sharing");
                if (updates) { await StreamUpdates(ssl); return; }
                while (!stop.IsCancellationRequested)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var frame = capture();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    await Wire.WriteAsync(ssl, frame, deadline.Token);
                    // Rendering acknowledgement provides backpressure: never queue stale frames.
                    var ack = await Wire.ReadAsync(ssl, 1, deadline.Token);
                    if (ack[0] != 1) throw new InvalidDataException("Invalid acknowledgement.");
                    FramesSent++;
                    var delay = 1000 / fps - (int)watch.ElapsedMilliseconds;
                    if (delay > 0) await Task.Delay(delay, stop.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or SocketException or OperationCanceledException or ObjectDisposedException)
            { if (ownsSlot && !stop.IsCancellationRequested) Status?.Invoke("Receiver disconnected · waiting to reconnect"); }
            catch (Exception ex) { if (ownsSlot) Status?.Invoke("Capture stopped: " + ex.Message); }
            finally { if (ownsSlot) { activeClient = null; Interlocked.Exchange(ref connected, 0); } }
        }
    }
    private async Task StreamUpdates(SslStream ssl)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        using var credits = new SemaphoreSlim(2, 2);
        var outstanding = 0;
        async Task ReadAcknowledgements()
        {
            try
            {
                while (!session.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var ack = await Wire.ReadAsync(ssl, 1, timeout.Token);
                    if (ack[0] != 1 || Interlocked.Decrement(ref outstanding) < 0) throw new InvalidDataException("Invalid acknowledgement.");
                    FramesSent++; credits.Release();
                }
            }
            finally { session.Cancel(); }
        }
        var acknowledgements = ReadAcknowledgements();
        try
        {
            bool first = true;
            while (!session.IsCancellationRequested)
            {
                // At most two frames in flight, including capture/encode. No stale-frame backlog.
                await credits.WaitAsync(session.Token);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var frame = captureUpdate!(first); first = false;
                Interlocked.Increment(ref outstanding);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await Wire.WriteAsync(ssl, frame, timeout.Token);
                var delay = 1000 / fps - (int)watch.ElapsedMilliseconds;
                if (delay > 0) await Task.Delay(delay, session.Token);
            }
        }
        finally
        {
            session.Cancel();
            try { await acknowledgements; } catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        }
    }
    internal void InterruptConnectionForTest() => activeClient?.Dispose();
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        if (loop is not null) await loop;
        certificate.Dispose(); stop.Dispose();
    }
}

public sealed class StreamReceiver
{
    public event Action<string>? Status;
    public long FramesReceived { get; private set; }
    public long BytesReceived { get; private set; }
    public async Task RunAsync(Invitation invite, Func<byte[], Task> present, CancellationToken ct, bool reconnect = true, bool preferUpdates = true)
    {
        var modern = preferUpdates;
        do
        {
            var awaitingHello = false;
            try
            {
                Status?.Invoke("Connecting to " + invite.Host + "…");
                using var client = new TcpClient { NoDelay = true };
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(IPAddress.Parse(invite.Host), invite.Port, connect.Token);
                using var ssl = new SslStream(client.GetStream(), false, (_, cert, _, _) =>
                    cert is not null && string.Equals(Wire.Fingerprint(cert), invite.Fingerprint, StringComparison.OrdinalIgnoreCase));
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                    TargetHost = "Sidecar Display", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, connect.Token);
                var hello = Convert.FromBase64String(invite.Secret);
                if (modern) { Array.Resize(ref hello, 33); hello[32] = 2; }
                await Wire.WriteAsync(ssl, hello, connect.Token); awaitingHello = true;
                var response = Encoding.UTF8.GetString(await Wire.ReadAsync(ssl, 64, connect.Token));
                awaitingHello = false;
                if (response == "BUSY") throw new InvalidOperationException("The sender already has a receiver connected.");
                if (response is not ("SIDECAR/1" or "SIDECAR/2")) throw new InvalidDataException("Incompatible sender version.");
                Status?.Invoke("Connected · encrypted");
                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    var data = await Wire.ReadAsync(ssl, Wire.MaxPacket, timeout.Token);
                    await present(data);
                    FramesReceived++; BytesReceived += data.Length;
                    await Wire.WriteAsync(ssl, new byte[] { 1 }, timeout.Token);
                }
            }
            catch (AuthenticationException) { throw new AuthenticationException("The sender identity could not be verified. Copy a fresh invitation from the sender."); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested) return;
                // Version 0.1 accepts only a 32-byte hello. Retry once without capabilities.
                if (modern && awaitingHello && ex is IOException) { modern = false; await Task.Delay(150, ct); continue; }
                if (!reconnect) throw;
                Status?.Invoke("Connection lost · retrying in 2 seconds. Check sender, Wi-Fi and firewall.");
                await Task.Delay(2000, ct);
            }
        } while (!ct.IsCancellationRequested);
    }
}
