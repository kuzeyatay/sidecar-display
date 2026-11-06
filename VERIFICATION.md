# Release verification

## 0.2.1 — 6 November 2026

Built on Windows 11 x64 using .NET SDK 8.0.427. Release builds and self-contained
Windows x64 publishing completed with zero compiler warnings or errors.

### Automated checks

`./build.ps1 -Test` passed all six core groups:

- Invitation parsing and validation.
- Frame lengths, bounds, and truncated-message rejection.
- Authenticated TLS, incorrect certificates and secrets, reconnection, and backpressure.
- Compatibility fallback to a version 0.1 sender.
- Changed-region encoding, reconstruction, resizing, and malformed-update rejection.
- Bounded two-frame pipeline and complete keyframes after reconnecting.

Desktop mode also passed real capture → TLS → native rendering, fullscreen bounds
and restoration, and main-window shutdown. The local 1080p run rendered 80 frames
at approximately 18 updates/second. This is a single-machine loopback measurement,
not a performance promise for Wi-Fi or another receiver.

The synthetic 80-frame text-desktop test sent 470,435 bytes of changed-region
updates versus 43,365,500 bytes of complete JPEGs (98.9% fewer bytes). It is a sparse
moving-region workload, not a full-motion video benchmark.

Reports are generated under `artifacts/core/verification.json` and
`artifacts/desktop/verification.json`. CI publishes its core report as an artifact.
Local reports and screenshots are excluded from the source repository.

### Manual and package checks

- Opened the packaged self-contained app and checked the sender and receiver tabs.
- Reviewed concise labels, Help/version footer, and keyboard focus. The first visual
  pass found clipping in the shorter sender window; its height and label spacing
  were adjusted and the desktop tests rerun. A final visual recheck was interrupted
  when Computer Use was stopped. Other DPI scales remain a manual acceptance item.
- Checked package contents, pinned driver checksum, and runtime/license notices.
- Preserved existing displays and the running older app during release preparation.

### Limits

Display-driver installation, elevation, firewall modification, and virtual-display
resize/detach were not repeated for this copy/documentation release. Desktop mode
does not change topology. These paths retain their existing implementation and
have a separate Full test mode for hardware verification.

A previous version was used successfully between two PCs, but **this exact 0.2.1
build has not been accepted on the second physical laptop**. Its network path,
decoding speed, and real-world performance require that separate acceptance check.
CI verifies the core protocol and package; it cannot establish two-PC compatibility.
