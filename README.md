# SSTD
## Super Terminal of Deepthinking

**A real, local-first Windows desktop agent built with C#/.NET 8 and WPF.** SSTD connects to Ollama, plans one validated action at a time, operates ordinary Windows applications through UI Automation and native input, observes the result, and keeps an auditable task timeline.

> Despite the project name, SSTD is **not a terminal** and cannot execute shell commands. Ollama is a separate dependency. Desktop control starts disabled. This is working software with explicit compatibility and safety limits—not a claim of human-level, universal computer use.

## Download and run

**Download guide:** [DOWNLOAD.md](DOWNLOAD.md)  
**Download page / Releases:** https://github.com/cocre6369/SSTD/releases/tag/v1.0.1

1. Download `SSTD-Setup.exe` from the download page or Releases page.
2. Verify its SHA-256 against `SHA256SUMS.txt`. The build is unsigned unless a signed release is explicitly identified.
3. Run the installer. It installs for the current user and creates a Start Menu shortcut. Visual Studio, .NET development tools, and a terminal are **not** needed by end users.
4. Install and start Ollama separately. See [Ollama setup](docs/OLLAMA.md).
5. Open SSTD, select an installed model, and test conversation.
6. Review **Settings → Automation**, then enable desktop control. Try: **“Open Notepad and type Hello from SSTD into a new document. Verify the text.”**

The self-contained `SSTD.exe` is also produced by the build. Single-file .NET apps extract bundled native components to the user's temporary directory on first run; this is normal. Windows OCR language packs and Ollama/models are not included.

## What is implemented

- **Local AI:** loopback Ollama connection detection, installed model discovery, model selection, vision capability detection, streaming replies, context, configurable inference limits, explicit model-download approval and actual download progress.
- **Real desktop control:** generic installed-app discovery, process/window identity checks, UIA hierarchy inspection, stable observed element IDs, pattern-based clicks, real Unicode input and productivity shortcuts, window switching and graceful close.
- **Off-screen content:** bounded search through live scrollable regions, fresh inspection after each scroll, stable-boundary detection, and no blind coordinate scripts.
- **Selective vision:** relevant-window/region screenshots, Windows OCR, fresh visual target boxes, optional local vision-model images. No continuous screen capture.
- **Independent security:** closed action registry, blocked system/terminal/credential applications, password-field screening, protected-path rules, external-commit approvals and non-overwriting document creation.
- **Document work:** local composition, typing into ordinary editors, formatting shortcuts, and approved new `.txt`, `.md`, or valid OpenXML `.docx` files with read-back verification.
- **Dashboard:** dark charcoal WPF interface, cyan accents, conversation, connection/model indicators, Stop, real activity timeline, task history, settings and optional tray mode.
- **Local storage:** SQLite settings, conversations and task events; clear controls; no screenshot/clipboard archive.

There are **no Spotify-specific or Word-specific click scripts**. The same observation/action engine is used across applications. Application names in examples are not a whitelist.

## Architecture

```text
SSTD.UI ─────────── WPF dashboard, MVVM, settings, approval dialogs
   │
SSTD.Planner ────── observe → reason → validate → act → verify loop
   │
SSTD.AI ─────────── Ollama HTTP/NDJSON, structured planning, composition
SSTD.Security ───── independent policy; no AI dependency
SSTD.Automation ─── UIA, Win32 input, app discovery, guarded action backend
SSTD.Vision ─────── Windows OCR and on-demand relevant-region capture
SSTD.Memory ─────── SQLite and exclusive new-document writer
SSTD.Core ───────── action schema, contracts, models, secret redaction
SSTD.Tests ──────── adversarial policy/protocol/planner/storage tests
```

The backend rechecks policy and target identity. Approvals are one-shot and destination-bound. STOP cancels future actions and streaming; it cannot undo an action Windows already accepted. See [security architecture](docs/SECURITY.md).

## Build from source

On Windows with the .NET 8 SDK and Inno Setup 6:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
./build.ps1 -Package all -Version 1.0.1
```

Exact publish command:

```powershell
dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts/publish
```

On Linux, `./build.sh test` runs portable tests; `./build.sh cross-publish` produces a Windows binary but cannot validate the WPF runtime. The included native Windows CI workflow builds, tests, packages and smoke-tests installation/uninstallation.

Full instructions: [Build and packaging](docs/BUILD.md).

## Verification and honest limits

`docs/TESTING.md` records the actual verification available for this revision and the checks that require a real interactive desktop. Unit tests use mocked HTTP/planner boundaries; they are not presented as live Ollama benchmarks. Windows integration tests launch a real WPF fixture and Notepad. Spotify, Office, browser sites and games are third-party moving targets and require the listed manual acceptance tests.

SSTD cannot guarantee operation of every custom canvas, game, remote desktop or inaccessible UI. It does not solve CAPTCHAs, enter passwords, bypass UAC, operate administrative tools, delete/overwrite files, or claim playback/document success without evidence. External-commit recognition on unfamiliar interfaces is conservative but not infallible. Use a separate standard Windows account or Windows Sandbox for stronger isolation.

Read [Supported automation](docs/AUTOMATION.md), [Known limitations](docs/LIMITATIONS.md), and [Testing](docs/TESTING.md) before relying on it.

## Privacy

Inference uses your local Ollama server, not a cloud AI API. Apps and websites you automate retain their own network behavior. History is local and unencrypted; protect your Windows account and disk. SSTD never needs administrator privileges for normal operation. Clearing SSTD data does not remove Ollama or its models.

## License

MIT. See [LICENSE](LICENSE). Third-party dependencies retain their own licenses; Ollama and model weights are distributed separately under their respective terms.
