# Security

## Connection model

Sidecar is intended for a trusted local network. The sender binds to the selected
IPv4 address on TCP port 45910. The optional Windows Firewall rule is restricted to
the executable, Private network profile, and local subnet.

The invitation contains the sender address, a SHA-256 certificate fingerprint,
and a random 256-bit pairing secret. The receiver pins that fingerprint; transport
uses TLS 1.2 or 1.3. A self-signed session certificate is not added to Windows trust
stores. Stopping sharing invalidates the invitation.

Treat an invitation as a password: anyone who receives it and can reach the sender
may view the selected display. Sidecar does not save invitations automatically or
write them to app logs. Copying one places it on the Windows clipboard, which may
be retained or synced by clipboard-history tools. Clear it after pairing if needed.
The app does not collect telemetry. The Help link opens GitHub in your browser.

Sidecar provides display streaming only, with one receiver at a time. It is not an
internet-facing remote-access service. Do not forward its port from your router.

## Report a vulnerability

Use this repository's **Security → Report a vulnerability** option when available.
If it is unavailable, open an issue requesting a private reporting channel without
including exploit details, invitations, credentials, or private desktop content.

Include the affected versions, Windows versions, reproduction steps, and impact
through the private channel. Security fixes target the latest release; update both
PCs together. The app executable is unsigned; the bundled display driver is signed.
