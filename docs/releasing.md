# Releasing Sidecar Display

1. Update `Version` in `src/SidecarDisplay.csproj`, the download example in the
   README, `CHANGELOG.md`, and a matching release-notes file in `docs/releases/`.
2. Run `./build.ps1 -Test -Package` on Windows. Run Desktop verification on a machine
   with two active displays. Run Full verification separately if topology changed.
3. Check both UI tabs, minimum window size, and keyboard/fullscreen controls. Confirm
   a connection between two PCs before claiming that release's two-PC acceptance.
4. Record actual results and remaining limits in `VERIFICATION.md`. Inspect the ZIP
   for the executable, driver, documentation, licenses, and runtime notices.
5. Commit the reviewed source, push `main`, and wait for the Windows CI workflow.
6. Tag that exact commit `vX.Y.Z`, push the tag, and publish a GitHub release with
   `dist/SidecarDisplay-X.Y.Z-Windows-x64.zip` and its `.sha256` file.
7. Verify the release tag matches the tested commit and the uploaded asset checksum
   matches the local package. Never replace an existing release with different bytes;
   publish a new version instead.

Packaging uses a separate `dist/X.Y.Z/SidecarDisplay` folder so it does not replace
an older copy that may be running. The build script cleans that staging folder
before publishing. Close any process running from it before rebuilding that version.
