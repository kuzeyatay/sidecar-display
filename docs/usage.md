# Setup, updates, and troubleshooting

## Choose a display

Choose **Extra desktop (virtual display)** for a separate Windows desktop, or
select a physical display to mirror it. **Desktop resolution** applies to the
virtual display; **Stream quality** controls what is sent to the laptop.

Newly enabled virtual displays are placed above the primary screen. An existing
arrangement is preserved; a display already above the primary stays aligned above
it when resized. Use **Display settings** to move or disable a display manually.
Sidecar detaches a virtual display it enabled when sharing stops or the app exits.
An unexpected crash can leave it active; disable it in Windows Display settings.

## Cannot connect

1. Check that sharing is still running, and copy a fresh, complete invitation.
2. Choose the main PC's Wi-Fi or Ethernet address reachable from the receiver.
   VPN and virtual network adapters may advertise an unreachable address.
3. Connect both devices to the same trusted local network. Guest Wi-Fi and access
   point client isolation can block communication even on the same Wi-Fi name.
4. On the sender, use **Allow private LAN** and ensure the network is marked Private.
   The rule applies to this executable path, TCP 45910, and local-subnet clients.
5. If Windows previously created an explicit blocking rule for Sidecar, inspect
   that rule in Windows Firewall. An allow rule does not override an explicit block.
6. Disconnect another receiver if one is already connected. One receiver is supported.

Do not disable Windows Firewall or expose the streaming port to the internet.
An identity-verification error requires a new invitation from the sender.
A temporary network interruption is retried automatically.

## Slow, frozen, or blank picture

- Try **Slower laptop**, choose a **1280 × 720** desktop, and restart sharing.
- Check the connection status and the receiver toolbar's frame-rate reading.
- Prefer Ethernet or move closer to the Wi-Fi access point.
- Unlock the main PC. Locking Windows can interrupt capture; stop and restart sharing
  after unlocking if necessary.
- Full-screen scrolling and video need larger JPEG updates. Hardware video encoding,
  protected-content capture, remote input, and audio forwarding are not provided.

Maximum source dimensions are 8192 pixels per side and 34 million decoded pixels.
Sidecar keeps each PC awake while it is sharing or displaying a stream.

## Update

Close Sidecar before replacing its files. Extract the new release on both PCs.
The virtual display driver does not need reinstalling. Restarting the sender
creates a new invitation; copy it to the receiver.

Retain the sender's executable path if you want to reuse its existing firewall
rule. When installing into a new folder, use **Allow private LAN** from that copy.
Version 0.2 and later use changed-region updates together; version 0.1 connections
fall back to full JPEG frames.

## Remove Sidecar or its display

Stop sharing, then choose **Remove app's display** on the sender before deleting
the app folder. This removes only the device instance installed by Sidecar; it does
not remove an existing third-party virtual display installation.

Driver files remain in Windows' driver store, and settings remain under
`%ProgramData%\SidecarDisplay\Driver`. The optional firewall rule also remains;
remove the Sidecar rule in Windows Firewall if you no longer use the app.
The receiver can be removed by closing it and deleting its extracted folder.

The bundled driver comes from
[VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver).
Sidecar does not enable test signing, disable Secure Boot, or add certificates to
Windows trust stores.
