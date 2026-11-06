# Sidecar Display

Use a spare Windows laptop as an extra monitor over your local network.

[![Windows build and tests](https://github.com/kuzeyatay/sidecar-display/actions/workflows/ci.yml/badge.svg)](https://github.com/kuzeyatay/sidecar-display/actions/workflows/ci.yml)

Sidecar is a native Windows app with an extended desktop, an encrypted connection,
and a fullscreen receiver. No browser, account, or dummy HDMI plug is required.

**[Download for Windows x64](https://github.com/kuzeyatay/sidecar-display/releases/latest)** ·
[Setup and troubleshooting](docs/usage.md) · [Release notes](CHANGELOG.md)

## What it does

- Extend your desktop through the bundled signed virtual display driver, or mirror an existing screen.
- Send changed desktop regions to reduce traffic when working with documents, code, and chat.
- Switch the receiver between a window and borderless fullscreen, with automatic reconnection.
- Pair using a temporary invitation. No telemetry, cloud service, or saved pairing secrets.

Sidecar displays the picture only. Use the **main PC's keyboard and mouse**;
remote input and audio forwarding are not supported.

## Requirements

- Two Intel/AMD 64-bit PCs running Windows 10 version 1903 or later, or Windows 11.
- Both PCs connected to the same local network. Ethernet or a strong Wi-Fi connection works best.
- Administrator access on the main PC for virtual display setup and the optional firewall rule.

The portable download includes the .NET runtime. The receiver needs no display
driver. ARM64, macOS, and Linux are not supported by this package.

## Get started

1. Download `SidecarDisplay-0.2.1-Windows-x64.zip` from
   [Releases](https://github.com/kuzeyatay/sidecar-display/releases/latest).
   Extract the **whole ZIP on both PCs**, then open `SidecarDisplay.exe`.
2. On the main PC, select **Share this PC → Set up extra display**. Approve the
   Windows administrator prompt to install the signed driver. Existing virtual
   displays are reused.
3. Choose **Extra desktop (virtual display)**, select the network address reachable
   from the laptop, and click **Start sharing → Copy invitation**.
4. On the laptop, select **Use as a monitor → Paste invitation → Connect to PC**.
5. Move a window onto the extra desktop using the main PC. A newly enabled display
   is placed **above** the primary screen. Change the arrangement in **Display settings**.

Keep Sidecar open on both PCs. Treat the invitation as a password; stopping sharing
invalidates it. Keep the `driver` folder beside the executable.

Sidecar's executable is not code-signed. The bundled third-party display driver is
signed. Each release includes a `.sha256` file; compare it with
`Get-FileHash .\SidecarDisplay-0.2.1-Windows-x64.zip -Algorithm SHA256` before extracting.

## Controls

Use these controls in the receiver window:

| Action | Control |
| --- | --- |
| Toggle fullscreen | **Ctrl+Shift+F**, double-click, or **Fullscreen** |
| Leave fullscreen | **Esc** |
| Show fullscreen toolbar | Move the pointer to the top edge |
| End the connection | **Disconnect** |

## Stream quality

| Preset | Maximum stream size | Frame-rate limit |
| --- | --- | --- |
| Balanced | 1080p | 45 fps |
| Sharper text | Native resolution | 30 fps |
| Fastest | 720p | 60 fps |
| Slower laptop | 720p | 30 fps |

These are limits, not promised frame rates. Actual performance depends on both PCs,
the source resolution, and the network. For an older receiver, start with **Slower
laptop** and a **1280 × 720** desktop. Full-screen video and scrolling cost more than
mostly static windows: this version uses JPEG compression, without hardware video encoding.

## Connection problems

On the main PC, **Allow private LAN** adds a firewall rule for this executable,
TCP port 45910, the Private network profile, and the local subnet. Use a trusted
network marked Private; do not forward this port on your router. Guest Wi-Fi may
block connections between devices.

For connection errors, driver maintenance, updates, and removal, see the
[usage guide](docs/usage.md). Report reproducible problems through
[GitHub Issues](https://github.com/kuzeyatay/sidecar-display/issues).
Never post your invitation in an issue.

## Build from source

On Windows, install the .NET 8 SDK selected by [global.json](https://github.com/kuzeyatay/sidecar-display/blob/main/global.json), then run
from PowerShell in the repository root:

```powershell
./build.ps1                    # Release build
./build.ps1 -Test              # Protocol, image, TLS, and reconnection tests
./build.ps1 -Package           # Self-contained Windows x64 ZIP and SHA-256
./build.ps1 -Test -Package     # Test before packaging
```

Packages are written to `dist/`. Packaging downloads the pinned
[Virtual Display Driver 25.7.23 archive](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/tag/25.7.23)
and verifies its SHA-256. There are no third-party NuGet library dependencies.
A first build requires internet access to restore Microsoft framework/runtime packs.

Desktop verification needs two active displays and is separate from the default
test suite. See [CONTRIBUTING.md](CONTRIBUTING.md) for test modes and source layout,
and [VERIFICATION.md](VERIFICATION.md) for the release's validation scope.

## License

Sidecar Display is [MIT licensed](LICENSE). Bundled driver and runtime components
retain their own notices; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
For the pairing model and vulnerability reporting, see [SECURITY.md](SECURITY.md).
