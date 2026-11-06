# Contributing

Build on Windows using the .NET 8 SDK selected by `global.json`. Run
`./build.ps1 -Test` before opening a pull request. No third-party NuGet libraries
are required; Microsoft framework and runtime packs are restored from NuGet.

## Test modes

| Command | Coverage | Requirements / side effects |
| --- | --- | --- |
| `./build.ps1 -Test` | Invitations, frame bounds, TLS, authentication, reconnect, image updates, backpressure, legacy compatibility | Windows; no virtual driver or second display required |
| `./build.ps1 -Test -TestMode Desktop` | Core tests, real capture and rendering, fullscreen, main-window lifecycle | Two active displays; briefly shows a test pattern and viewer; does not change topology |
| `./build.ps1 -Test -TestMode Full` | Desktop tests plus virtual-display resize, detach, and extend | Installed virtual display; changes its configuration; stop active sharing first |

A virtual display counts as the second display. Full mode leaves the virtual
screen enabled at 1080p; use Display settings to restore your preferred arrangement.
The topology check is skipped if no virtual driver is installed.
Reports are written to `artifacts/core`, `artifacts/desktop`, or `artifacts/full`.
CI runs only Core mode. A passing core suite does not verify driver installation,
physical network connectivity, capture, or the receiver's desktop integration.

For UI changes, check both tabs at 100% and 150% DPI, the minimum window size,
keyboard navigation, and the fullscreen controls. Keep permanent instructions in
the usage guide; app text should help the next action or explain the current state.

## Source map

| File | Responsibility |
| --- | --- |
| `src/MainForm.cs` | Sender/receiver setup UI |
| `src/ViewerForm.cs` | Native rendering, fullscreen, connection status |
| `src/Protocol.cs` | Invitations, TLS, framing, authentication, reconnection |
| `src/ScreenCapture.cs` | Windows capture, cursor, JPEG encoding |
| `src/DesktopFrames.cs`, `src/DesktopSurface.cs` | Changed-region encoding and reconstruction |
| `src/Displays.cs`, `src/DisplayTopology.cs` | Display enumeration and topology |
| `src/DriverSetup.cs` | Driver integration and private-LAN firewall rule |
| `tests/Program.cs` | Executable regression and desktop integration harness |

Keep changes focused, describe user-visible behavior, and report which checks ran.
Never commit invitations, generated artifacts, credentials, or personal diagnostics.
Use the issue template for bugs; use [SECURITY.md](SECURITY.md) for vulnerabilities.
See [docs/releasing.md](docs/releasing.md) for the release procedure.
