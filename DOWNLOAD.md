# Download SSTD

## Windows 10/11 x64 installer

**[Download SSTD-Setup.exe](https://github.com/cocre6369/SSTD/releases/download/v1.0.1/SSTD-Setup.exe)**  
[Release page and checksums](https://github.com/cocre6369/SSTD/releases/tag/v1.0.1)

- **Version:** 1.0.1
- **File:** `SSTD-Setup.exe`
- **Size:** 54,863,159 bytes
- **SHA-256:** `77a4a49b9399c7762311db861daa7329a844ded394ae4e6567c43dd7c80cd525`
- **Install type:** per-user, Start Menu shortcut, scoped uninstaller
- **End-user requirements:** Windows 10 2004/build 19041 or Windows 11, x64. No .NET SDK or development tools required.

> The build is unsigned unless a signed release is explicitly identified. Windows SmartScreen may warn about an unknown publisher.

## Upgrading from 1.0.0

Install 1.0.1 over the existing installation. It repairs older local SQLite task-history schemas automatically; you do not need to delete your SSTD data folder.

## Verify the download

```powershell
Get-FileHash .\SSTD-Setup.exe -Algorithm SHA256
```

Expected SHA-256:

```text
77a4a49b9399c7762311db861daa7329a844ded394ae4e6567c43dd7c80cd525
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
