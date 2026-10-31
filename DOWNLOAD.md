# Download SSTD

## Windows 10/11 x64 installer

**[Download SSTD-Setup.exe](https://github.com/cocre6369/SSTD/releases/download/v1.0.2/SSTD-Setup.exe)**  
[Release page and checksums](https://github.com/cocre6369/SSTD/releases/tag/v1.0.2)

- **Version:** 1.0.2
- **File:** `SSTD-Setup.exe`
- **Size:** 54,863,642 bytes
- **SHA-256:** `6db56551c56b77fda28d9deb6316abf2efd92b25fc49d66ca2d6f64e90ee2919`
- **Install type:** per-user, Start Menu shortcut, scoped uninstaller
- **End-user requirements:** Windows 10 2004/build 19041 or Windows 11, x64. No .NET SDK or development tools required.

> The build is unsigned unless a signed release is explicitly identified. Windows SmartScreen may warn about an unknown publisher.

## Upgrading from 1.0.0 or 1.0.1

Install 1.0.2 over the existing installation. It repairs older local SQLite schemas automatically, including `tasks`, `messages`, `events`, and `settings`; you do not need to delete your SSTD data folder. This fixes the `messages.conversation_id` NOT NULL startup error.

## Verify the download

```powershell
Get-FileHash .\SSTD-Setup.exe -Algorithm SHA256
```

Expected SHA-256:

```text
6db56551c56b77fda28d9deb6316abf2efd92b25fc49d66ca2d6f64e90ee2919
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

- 142 portable security/contract/unit tests passed.
- 4 real Windows UI Automation tests passed.
- Executable startup smoke passed.
- Installer install/start/uninstall smoke passed.
- Installer SHA-256 matched after retrieval.

CI did not run end-to-end tasks against a live Ollama model. See [TESTING.md](docs/TESTING.md), [SECURITY.md](docs/SECURITY.md), and [OLLAMA.md](docs/OLLAMA.md).
