# Changelog

## 0.2.1 — 2026-11-06

First standalone GitHub release.

### Changed

- Removed the tagline, promotional footer, repeated setup instructions, and long
  receiver explanations from the app.
- Shortened section headings, connection status, and display setup messages.
- Reduced the main window height and added Help and version information.

### Added

- Download and setup documentation, troubleshooting, contribution guidance, and
  security reporting instructions.
- Versioned Windows x64 packages with SHA-256 checksum files and bundled notices.
- Windows CI for the build, core regression tests, and portable packaging.
- Separate core, desktop, and full test modes, with the test scope recorded in results.

## 0.2.0 — 2026-11-06

- Changed-region JPEG streaming, off-UI-thread decoding, reusable native capture
  buffers, and a bounded two-update pipeline.
- Ctrl+Shift+F for fullscreen and default placement above the primary monitor.
- Compatibility with the original full-JPEG protocol.

## 0.1.0 — 2026-10-31

- Initial native Windows sender and receiver, invitation pairing, TLS transport,
  fullscreen viewing, reconnection, and signed virtual display driver integration.
