# Download SSTD

## Windows 10/11 x64

**[Download SSTD-Setup.exe](https://github.com/cocre6369/SSTD/releases/download/v1.0.3/SSTD-Setup.exe)**  
**[Download SSTD-Portable.zip](https://github.com/cocre6369/SSTD/releases/download/v1.0.3/SSTD-Portable.zip)**  
[Release page and checksums](https://github.com/cocre6369/SSTD/releases/tag/v1.0.3)

- **Version:** 1.0.3
- **Files:** `SSTD-Setup.exe` and `SSTD-Portable.zip`
- **SHA-256:** published in `SHA256SUMS.txt` on the release page
- **Install type:** per-user, Start Menu shortcut, scoped uninstaller
- **Portable type:** extract the ZIP and run `SSTD.exe`; no setup wizard required
- **End-user requirements:** Windows 10 2004/build 19041 or Windows 11, x64. No .NET SDK or development tools required.

> The build is unsigned unless a signed release is explicitly identified. Windows SmartScreen may warn about an unknown publisher.

## If the setup wizard does not open

1. Open Task Manager and end any `SSTD.exe` or `SSTD-Setup.exe` processes, then try the installer again.
2. If it still does not open, use `SSTD-Portable.zip`: extract it to a folder you own, then run `SSTD.exe`.
3. If Windows blocks the download, check Windows Security → Virus & threat protection → Protection history. Only restore/allow the file if it came from this GitHub release and its SHA-256 matches `SHA256SUMS.txt`.

The portable build uses the same per-user SSTD data folder as the installed build.

## Upgrading from 1.0.0, 1.0.1, or 1.0.2

Install 1.0.3 over the existing installation, or run the portable build. It repairs older local SQLite schemas automatically, including `tasks`, `messages`, `events`, and `settings`; you do not need to delete your SSTD data folder. This includes the `messages.conversation_id` NOT NULL startup error fixed in 1.0.2.

1.0.3 also avoids a setup stall caused by Windows Restart Manager when a previous SSTD process is hidden or failed, and it exits cleanly after a startup error instead of leaving a hidden process behind.

## Verify the download

```powershell
Get-FileHash .\SSTD-Setup.exe -Algorithm SHA256
Get-FileHash .\SSTD-Portable.zip -Algorithm SHA256
```

Compare the results with `SHA256SUMS.txt` on the release page.

## Quick start

1. Run `SSTD-Setup.exe`, or extract `SSTD-Portable.zip` and run `SSTD.exe`.
2. Install and start [Ollama](https://ollama.com) separately.
3. Pull a model, for example:

   ```powershell
   ollama pull qwen2.5:7b-instruct
   ```

4. Open SSTD, connect to `http://localhost:11434`, and select a model.
5. Review **Settings → Automation** before enabling desktop control.

## What was verified

See [TESTING.md](docs/TESTING.md) for the current Windows CI results. CI did not run end-to-end tasks against a live Ollama model. See also [SECURITY.md](docs/SECURITY.md) and [OLLAMA.md](docs/OLLAMA.md).
