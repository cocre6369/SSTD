# Download SSTD

## Windows 10/11 x64

**[Download SSTD-Setup.msi](https://github.com/cocre6369/SSTD/releases/download/v1.0.4/SSTD-Setup.msi)**<br>
**[Download SSTD-Portable.zip](https://github.com/cocre6369/SSTD/releases/download/v1.0.4/SSTD-Portable.zip)**<br>
[Release page and checksums](https://github.com/cocre6369/SSTD/releases/tag/v1.0.4)

- **Version:** 1.0.4
- **Files:** `SSTD-Setup.msi` and `SSTD-Portable.zip`
- **SHA-256:** published in `SHA256SUMS.txt` on the release page
- **Install type:** native per-user Windows Installer package, Start Menu shortcut, scoped uninstaller
- **Portable type:** extract the ZIP and run `SSTD.exe`; no setup wizard required
- **End-user requirements:** Windows 10 2004/build 19041 or Windows 11, x64. No .NET SDK or development tools required.

> The build is unsigned unless a signed release is explicitly identified. Windows SmartScreen or antivirus software may warn about an unknown publisher. Verify the SHA-256 before allowing the file.

## Why 1.0.4 uses an MSI

Some machines saw `SSTD-Setup.exe` from 1.0.2/1.0.3 remain in Task Manager without showing a wizard after the SmartScreen prompt. The Inno Setup bootstrapper extracts and launches a temporary executable; security software can intercept that hand-off even when the downloaded setup file is excluded.

1.0.4 replaces that bootstrapper with a native `.msi` handled by Windows Installer (`msiexec.exe`). It does not launch a temporary SSTD setup executable. The portable ZIP remains available as a no-install fallback.

## If Windows or antivirus blocks the download

1. Do not turn off Windows Defender or Trend Micro.
2. Check **Windows Security → Virus & threat protection → Protection history** and Trend Micro's detection/quarantine history.
3. Only restore/allow the file if it came from this GitHub release and its SHA-256 matches `SHA256SUMS.txt`.
4. If you use an exclusion, exclude the downloaded `SSTD-Setup.msi` and, after installation, `%LOCALAPPDATA%\Programs\SSTD\SSTD.exe`. For the portable build, exclude the extracted `SSTD.exe` you actually run.
5. If the MSI still does not open, use `SSTD-Portable.zip`: extract it to a folder you own, then run `SSTD.exe`.

The portable build uses the same per-user SSTD data folder as the installed build.

## Upgrading from 1.0.0, 1.0.1, 1.0.2, or 1.0.3

Run `SSTD-Setup.msi`, or use the portable build. SSTD repairs older local SQLite schemas automatically, including `tasks`, `messages`, `events`, and `settings`; you do not need to delete your SSTD data folder. This includes the `messages.conversation_id` NOT NULL startup error fixed in 1.0.2.

If an older Inno Setup installation is already registered, Windows may show both the old SSTD entry and the new MSI entry. Use the new MSI entry for future repair/uninstall. The MSI uninstaller removes only the files and shortcut it installed; it does not remove your SSTD data folder, Ollama, or models.

## Verify the download

```powershell
Get-FileHash .\SSTD-Setup.msi -Algorithm SHA256
Get-FileHash .\SSTD-Portable.zip -Algorithm SHA256
```

Compare the results with `SHA256SUMS.txt` on the release page.

## Quick start

1. Run `SSTD-Setup.msi`, or extract `SSTD-Portable.zip` and run `SSTD.exe`.
2. Install and start [Ollama](https://ollama.com) separately.
3. Pull a model, for example:

   ```powershell
   ollama pull qwen2.5:7b-instruct
   ```

4. Open SSTD, connect to `http://localhost:11434`, and select a model.
5. Review **Settings → Automation** before enabling desktop control.

## What was verified

See [TESTING.md](docs/TESTING.md) for the current Windows CI results. CI did not run end-to-end tasks against a live Ollama model. See also [SECURITY.md](docs/SECURITY.md) and [OLLAMA.md](docs/OLLAMA.md).
