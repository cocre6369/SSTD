# Download SSTD

## Windows 10/11 x64 installer

**[Download SSTD-Setup.exe](https://github.com/cocre6369/SSTD/releases/download/v1.0.0/SSTD-Setup.exe)**  
[Release page and checksums](https://github.com/cocre6369/SSTD/releases/tag/v1.0.0)

- **Version:** 1.0.0
- **File:** `SSTD-Setup.exe`
- **Size:** 54,853,834 bytes
- **SHA-256:** `b750b5180febfd9a30ff14c1580fe25a566d1311746012bb9bfa5e2639733d33`
- **Install type:** per-user, Start Menu shortcut, scoped uninstaller
- **End-user requirements:** Windows 10 2004/build 19041 or Windows 11, x64. No .NET SDK or development tools required.

> The build is unsigned unless a signed release is explicitly identified. Windows SmartScreen may warn about an unknown publisher.

## Verify the download

```powershell
Get-FileHash .\SSTD-Setup.exe -Algorithm SHA256
```

Expected SHA-256:

```text
b750b5180febfd9a30ff14c1580fe25a566d1311746012bb9bfa5e2639733d33
```

## Quick start

1. Run `SSTD-Setup.exe`.
2. Install and start [Ollama](https://ollama.com) separately.
3. Pull a model, for example:

   ```powershell
   ollama pull qwen2.5:7b-instruct
   ```

4. Open SSTD, connect to `http://localhost:11434`, and select a model.
5. Review **Settings → Automation** before enabling desktop control.

## What was verified

- 140 portable security/contract/unit tests passed.
- 4 real Windows UI Automation tests passed.
- Executable startup smoke passed.
- Installer install/start/uninstall smoke passed.
- Installer SHA-256 matched after retrieval.

CI did not run end-to-end tasks against a live Ollama model. See [TESTING.md](docs/TESTING.md), [SECURITY.md](docs/SECURITY.md), and [OLLAMA.md](docs/OLLAMA.md).
